using Sandbox.Gltf;
using System.Collections.ObjectModel;

namespace Sandbox;

/// <summary>The source components used by a core material texture role.</summary>
[Flags]
internal enum GltfMaterialTextureChannels
{
	Red = 1,
	Green = 2,
	Blue = 4,
	Alpha = 8,
	Rgb = Red | Green | Blue,
	Rgba = Rgb | Alpha
}

/// <summary>
/// Resolves material texture links and encoding policy without opening images or
/// allocating native resources. Per-role bindings deliberately preserve distinct
/// transfer functions when the same image is used for both color and data.
/// </summary>
internal sealed class GltfMaterialTextureEncodingPlan
{
	internal sealed class Binding
	{
		internal GltfMaterialTextureRole Role { get; init; }
		internal GltfTextureInfo TextureInfo { get; init; }
		internal int TextureIndex => TextureInfo.Index;
		internal int? ImageIndex { get; init; }
		internal int? SamplerIndex { get; init; }
		// Null means the source omitted a sampler. Omitted min/mag values in an
		// explicit sampler remain null; backend default/approximation is separate.
		internal GltfSampler Sampler { get; init; }
		internal bool IsSrgb { get; init; }
		// Components defined by this texture role; material alphaMode separately
		// determines whether its linear base-color alpha affects coverage.
		internal GltfMaterialTextureChannels Channels { get; init; }
		internal bool HasLinearAlpha => Role is GltfMaterialTextureRole.BaseColor or GltfMaterialTextureRole.DiffuseTransmission;
		internal bool UsesAlphaCoverage { get; init; }
		/// <summary>
		/// PNG/JPEG ICC, gamma and intent metadata cannot change a material role's
		/// transfer function. Codec format constraints such as KTX2 DFD transfer,
		/// orientation and swizzle remain independently validated by that codec.
		/// </summary>
		internal bool IgnoreImageColorMetadata => true;
		internal bool TopLeftOrigin => true;
		internal bool StraightAlpha => true;
	}

	internal IReadOnlyDictionary<GltfMaterialTextureRole, Binding> Bindings { get; }
	private GltfMaterialTextureEncodingPlan( Dictionary<GltfMaterialTextureRole, Binding> bindings ) => Bindings = new ReadOnlyDictionary<GltfMaterialTextureRole, Binding>( bindings );

	internal static GltfMaterialTextureEncodingPlan Create( GltfDocument document, GltfMaterial material ) => CreateForRoles( document, material,
		[GltfMaterialTextureRole.BaseColor, GltfMaterialTextureRole.MetallicRoughness, GltfMaterialTextureRole.Normal, GltfMaterialTextureRole.Occlusion, GltfMaterialTextureRole.Emissive, GltfMaterialTextureRole.DiffuseTransmission, GltfMaterialTextureRole.DiffuseTransmissionColor,
		GltfMaterialTextureRole.Clearcoat, GltfMaterialTextureRole.ClearcoatRoughness, GltfMaterialTextureRole.ClearcoatNormal, GltfMaterialTextureRole.Anisotropy] );

	/// <summary>
	/// Plans only roles consumed by the selected material model. For example,
	/// KHR_materials_unlit consumes BaseColor, so ignored PBR inputs cause no link
	/// validation or image I/O. Missing supported images remain capability metadata.
	/// </summary>
	internal static GltfMaterialTextureEncodingPlan CreateForRoles( GltfDocument document, GltfMaterial material, IEnumerable<GltfMaterialTextureRole> roles )
	{
		ArgumentNullException.ThrowIfNull( document );
		ArgumentNullException.ThrowIfNull( roles );
		// The internal consumed-role contract matches UV planning. Validate the
		// complete selection before resolving any texture links, even if an
		// earlier role has an invalid texture reference.
		var selected = new HashSet<GltfMaterialTextureRole>();
		foreach ( var role in roles )
		{
			if ( !Enum.IsDefined( role ) ) throw new ArgumentOutOfRangeException( nameof( roles ), "Unknown material texture role." );
			if ( !selected.Add( role ) ) throw new ArgumentException( "Consumed material roles must be unique.", nameof( roles ) );
		}
		var bindings = new Dictionary<GltfMaterialTextureRole, Binding>();
		foreach ( var role in selected.Order() )
		{
			var (info, srgb, channels) = Describe( material, role );
			if ( info is null ) continue;
			if ( info.IsSrgb != srgb ) throw new GltfException( $"Texture role {role} contradicts its required {(srgb ? "sRGB" : "linear")} transfer function." );
			// ReadTexture selects a supported codec source or its standards-defined
			// fallback. It validates metadata only; encoded bytes remain unopened.
			var texture = document.ReadTexture( info.Index );
			bindings.Add( role, new Binding
			{
				Role = role, TextureInfo = info, ImageIndex = texture.Image,
				SamplerIndex = texture.Sampler, Sampler = texture.Sampler is int sampler ? document.ReadSampler( sampler ) : null,
				IsSrgb = srgb, Channels = channels,
				UsesAlphaCoverage = role == GltfMaterialTextureRole.BaseColor && (material?.AlphaMode is "MASK" or "BLEND")
			} );
		}
		return new( bindings );
	}

	private static (GltfTextureInfo Info, bool IsSrgb, GltfMaterialTextureChannels Channels) Describe( GltfMaterial material, GltfMaterialTextureRole role ) => role switch
	{
		GltfMaterialTextureRole.BaseColor => (material?.BaseColorTexture, true, GltfMaterialTextureChannels.Rgba),
		GltfMaterialTextureRole.MetallicRoughness => (material?.MetallicRoughnessTexture, false, GltfMaterialTextureChannels.Green | GltfMaterialTextureChannels.Blue),
		GltfMaterialTextureRole.Normal => (material?.NormalTexture, false, GltfMaterialTextureChannels.Rgb),
		GltfMaterialTextureRole.Occlusion => (material?.OcclusionTexture, false, GltfMaterialTextureChannels.Red),
		GltfMaterialTextureRole.Emissive => (material?.EmissiveTexture, true, GltfMaterialTextureChannels.Rgb),
		GltfMaterialTextureRole.DiffuseTransmission => (material?.DiffuseTransmission?.Texture, false, GltfMaterialTextureChannels.Alpha),
		GltfMaterialTextureRole.DiffuseTransmissionColor => (material?.DiffuseTransmission?.ColorTexture, true, GltfMaterialTextureChannels.Rgb),
		GltfMaterialTextureRole.Clearcoat => (material?.Clearcoat?.Texture, false, GltfMaterialTextureChannels.Red),
		GltfMaterialTextureRole.ClearcoatRoughness => (material?.Clearcoat?.RoughnessTexture, false, GltfMaterialTextureChannels.Green),
		GltfMaterialTextureRole.ClearcoatNormal => (material?.Clearcoat?.NormalTexture, false, GltfMaterialTextureChannels.Rgb),
		// Direction RG and strength B are linear data, never sRGB.
		GltfMaterialTextureRole.Anisotropy => (material?.Anisotropy?.Texture, false, GltfMaterialTextureChannels.Rgb),
		_ => throw new ArgumentOutOfRangeException( nameof( role ), role, "Unknown core glTF material texture role." )
	};
}
