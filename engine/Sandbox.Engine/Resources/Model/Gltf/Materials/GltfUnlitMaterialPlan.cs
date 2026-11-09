using Sandbox.Gltf;

namespace Sandbox;

using NVector4 = System.Numerics.Vector4;

/// <summary>
/// Immutable authored KHR_materials_unlit inputs, without native resources or
/// shader selection. The extension uses the linear base-color product only;
/// metallic/roughness, normals, occlusion and emission do not contribute.
/// Primitive bindings and renderer capabilities are deliberately separate.
/// </summary>
internal sealed class GltfUnlitMaterialPlan
{
	internal enum CoverageMode { Opaque, Mask, Blend }

	/// <summary>
	/// Semantics a material realization must actually support. A caller supplies
	/// proven capabilities; creating this CPU plan does not establish them.
	/// Texture support includes its canonical UV, transfer and sampler binding.
	/// Vertex color support includes linear multiplication and [0,1] clamping.
	/// </summary>
	[Flags]
	internal enum Features
	{
		None = 0,
		/// <summary>Unlit linear RGB output, with OPAQUE alpha ignored; not just a writable uniform.</summary>
		LinearFactor = 1,
		VertexRgb = 2,
		VertexAlpha = 4,
		BaseColorTexture = 8,
		TextureAlpha = 16,
		Mask = 32,
		Blend = 64,
		DoubleSided = 128,
		All = LinearFactor | VertexRgb | VertexAlpha | BaseColorTexture | TextureAlpha | Mask | Blend | DoubleSided
	}

	internal readonly record struct Coverage( bool Discard, float OutputAlpha );
	private readonly GltfMaterial material;
	internal int? MaterialIndex { get; }
	internal NVector4 LinearBaseColorFactor { get; }
	internal CoverageMode AlphaMode { get; }
	internal float? EffectiveAlphaCutoff { get; }
	internal bool CullBackFaces { get; }
	/// <summary>OPAQUE ignores alpha; RGB stays linear and never becomes premultiplied.</summary>
	internal NVector4 LinearFactorForShader => AlphaMode == CoverageMode.Opaque
		? new( LinearBaseColorFactor.X, LinearBaseColorFactor.Y, LinearBaseColorFactor.Z, 1 ) : LinearBaseColorFactor;

	private GltfUnlitMaterialPlan( GltfMaterial material, int? materialIndex, CoverageMode alphaMode )
	{
		this.material = material; MaterialIndex = materialIndex; LinearBaseColorFactor = material.BaseColorFactor;
		AlphaMode = alphaMode; EffectiveAlphaCutoff = alphaMode == CoverageMode.Mask ? material.AlphaCutoff : null;
		CullBackFaces = !material.DoubleSided;
	}

	/// <summary>
	/// Consumes an already parsed effective material. Unsupported optional unlit
	/// must remain the core lit fallback; this factory never upgrades that fallback.
	/// Valid rendering inputs beyond a shader's capabilities remain representable.
	/// </summary>
	internal static GltfUnlitMaterialPlan Create( GltfMaterial material, int? materialIndex = null )
	{
		ArgumentNullException.ThrowIfNull( material );
		if ( materialIndex < 0 ) throw new ArgumentOutOfRangeException( nameof( materialIndex ) );
		if ( !material.Unlit ) throw new GltfException( "The unlit material plan requires a selected KHR_materials_unlit material." );
		var factor = material.BaseColorFactor;
		if ( !Unit( factor.X ) || !Unit( factor.Y ) || !Unit( factor.Z ) || !Unit( factor.W ) )
			throw new GltfException( "Unlit baseColorFactor must contain finite linear values in [0,1]." );
		var mode = material.AlphaMode switch
		{
			"OPAQUE" => CoverageMode.Opaque,
			"MASK" => CoverageMode.Mask,
			"BLEND" => CoverageMode.Blend,
			_ => throw new GltfException( "Unlit material has an unknown alphaMode." )
		};
		// glTF imposes no upper bound on alphaCutoff. Outside MASK it has no
		// rendering effect; source/schema validation remains the reader's job.
		if ( mode == CoverageMode.Mask && (!float.IsFinite( material.AlphaCutoff ) || material.AlphaCutoff < 0) )
			throw new GltfException( "Unlit MASK alphaCutoff must be finite and nonnegative." );
		return new( material, materialIndex, mode );
	}

	/// <summary>
	/// Core coverage semantics for a completed linear base-color alpha product.
	/// This CPU policy does not stand in for a shader/image acceptance test.
	/// </summary>
	internal Coverage EvaluateCoverage( float baseColorAlpha )
	{
		if ( AlphaMode == CoverageMode.Opaque ) return new( false, 1 );
		if ( !Unit( baseColorAlpha ) ) throw new ArgumentOutOfRangeException( nameof( baseColorAlpha ), "Base-color alpha must be finite and in [0,1]." );
		if ( AlphaMode == CoverageMode.Blend ) return new( false, baseColorAlpha );
		var discard = baseColorAlpha < EffectiveAlphaCutoff!.Value;
		return new( discard, discard ? 0 : 1 );
	}

	/// <summary>
	/// Binds this source slot to one primitive's exact owned descriptors. Only the
	/// BaseColor role is consumed, so ignored lit-role UVs/images are not resolved.
	/// The returned context is not interchangeable with another primitive merely
	/// because both use the same authored material index.
	/// </summary>
	internal PrimitiveBinding Bind( GltfDocument document, GltfPrimitiveUploadData upload )
	{
		ArgumentNullException.ThrowIfNull( document ); ArgumentNullException.ThrowIfNull( upload );
		if ( upload.Layout is null || upload.Attributes is null || upload.Layout.SourceMode is < 0 or > 6 )
			throw new GltfException( "Unlit primitive binding requires a valid source upload layout." );
		upload.Attributes.TryGetValue( "COLOR_0", out var color );
		if ( color is not null && (color.Components is not (3 or 4) || color.Count != upload.Layout.VertexCount ||
			!((color.ComponentType == 5126 && !color.Normalized) || (color.ComponentType is 5121 or 5123 && color.Normalized))) )
			throw new GltfException( "Unlit COLOR_0 requires matching RGB/RGBA FLOAT or normalized unsigned byte/short source data." );
		var roles = new[] { GltfMaterialTextureRole.BaseColor };
		var uv = GltfMaterialUvPlan.CreateForRoles( material, upload, roles, MaterialIndex );
		var textures = GltfMaterialTextureEncodingPlan.CreateForRoles( document, material, roles );
		if ( uv.Bindings.Count != textures.Bindings.Count ) throw new GltfException( "Unlit UV and encoding role plans disagree." );
		foreach ( var (role, binding) in uv.Bindings )
		{
			if ( !textures.Bindings.TryGetValue( role, out var texture ) || texture.TextureIndex != binding.TextureInfo.Index || texture.TextureInfo.TexCoord != binding.TextureInfo.TexCoord )
				throw new GltfException( "Unlit base-color UV and encoding source identities disagree." );
		}
		return new( this, upload.Layout.SourceMode, upload.Attributes.ContainsKey( "NORMAL" ), color, uv, textures );
	}

	internal sealed class PrimitiveBinding
	{
		internal GltfUnlitMaterialPlan Source { get; }
		internal int SourceMode { get; }
		internal bool HasAuthoredNormal { get; }
		/// <summary>Raw source descriptor; RGB alpha is implicitly one. No bytes are rewritten.</summary>
		internal GltfUploadStream ColorSource { get; }
		internal int VertexColorComponents => ColorSource?.Components ?? 0;
		internal GltfMaterialUvPlan Uv { get; }
		internal GltfMaterialTextureEncodingPlan Textures { get; }
		internal Features RequiredFeatures { get; }
		internal bool HasResolvedBaseColorImage => !Textures.Bindings.TryGetValue( GltfMaterialTextureRole.BaseColor, out var texture ) || texture.ImageIndex.HasValue;

		internal PrimitiveBinding( GltfUnlitMaterialPlan source, int mode, bool hasNormal, GltfUploadStream color,
			GltfMaterialUvPlan uv, GltfMaterialTextureEncodingPlan textures )
		{
			Source = source; SourceMode = mode; HasAuthoredNormal = hasNormal; ColorSource = color; Uv = uv; Textures = textures;
			var features = Features.LinearFactor;
			if ( color is not null ) features |= Features.VertexRgb;
			if ( source.AlphaMode != CoverageMode.Opaque && color?.Components == 4 ) features |= Features.VertexAlpha;
			if ( textures.Bindings.ContainsKey( GltfMaterialTextureRole.BaseColor ) )
			{
				features |= Features.BaseColorTexture;
				if ( source.AlphaMode != CoverageMode.Opaque ) features |= Features.TextureAlpha;
			}
			if ( source.AlphaMode == CoverageMode.Mask ) features |= Features.Mask;
			if ( source.AlphaMode == CoverageMode.Blend ) features |= Features.Blend;
			if ( !source.CullBackFaces ) features |= Features.DoubleSided;
			RequiredFeatures = features;
		}

		internal Features GetUnsupportedFeatures( Features supported )
		{
			if ( (supported & ~Features.All) != 0 ) throw new ArgumentOutOfRangeException( nameof( supported ) );
			return RequiredFeatures & ~supported;
		}

		/// <summary>
		/// Explicit realization boundary. An opaque untextured candidate cannot
		/// silently claim MASK, BLEND, sidedness or arbitrary texture sampling.
		/// Capability flags must come from the selected proven template/backend.
		/// </summary>
		internal void RequireRealizable( Features supported )
		{
			if ( !HasResolvedBaseColorImage ) throw new GltfException( $"Unlit material {Source.MaterialIndex?.ToString() ?? "(unindexed)"} base-color texture has no supported image source." );
			var missing = GetUnsupportedFeatures( supported );
			if ( missing != Features.None ) throw new GltfException( $"Unlit material {Source.MaterialIndex?.ToString() ?? "(unindexed)"} requires unsupported realization capabilities: {missing}." );
		}
	}

	private static bool Unit( float value ) => float.IsFinite( value ) && value is >= 0 and <= 1;
}
