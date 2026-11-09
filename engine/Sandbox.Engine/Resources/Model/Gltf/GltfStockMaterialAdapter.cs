using Sandbox.Gltf;
using Sandbox.Gltf.DirectGpu;
using Sandbox.Engine;
using Sandbox.Resources;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;

namespace Sandbox;

/// <summary>
/// Maps the supported glTF material inputs to the shipped complex shader. Source
/// properties remain in glTF; native material/texture resources are derived caches.
/// Float32 repacking is an intermediate stock-shader adapter, not proof of the
/// final compiled GLB load-time or GPU-memory parity targets.
/// </summary>
internal static class GltfStockMaterialAdapter
{
	/// <summary>
	/// Explicit provided-template candidate inputs. This route is separate from
	/// the default complex material until its generated template has actual GPU
	/// acceptance and is deliberately selected by the backend owner.
	/// </summary>
	internal sealed class SourcePrepared
	{
		internal GltfMaterialTextureEncodingPlan Textures { get; init; }
		internal IReadOnlyDictionary<GltfMaterialTextureRole, SourceTexture> Resources { get; init; }
		internal System.Numerics.Vector4 LinearBaseColorFactor { get; init; }
		internal System.Numerics.Vector3 EmissiveFactor { get; init; }
		internal float MetallicFactor { get; init; }
		internal float RoughnessFactor { get; init; }
		internal float OcclusionStrength { get; init; }
		internal float NormalScale { get; init; }
		internal bool DoubleSided { get; init; }
		internal bool AlphaMask { get; init; }
		internal bool AlphaBlend { get; init; }
		internal float AlphaCutoff { get; init; }
		internal GltfDiffuseTransmission DiffuseTransmission { get; init; }
		internal GltfClearcoat Clearcoat { get; init; }
		internal GltfAnisotropy Anisotropy { get; init; }
	}

	internal sealed record SourceTexture( GltfMaterialTextureEncodingPlan.Binding Encoding, GltfSamplerMapping.Result Sampling, byte[] Resource );
	private static readonly GltfMaterialTextureRole[] ClearcoatRoles =
		[GltfMaterialTextureRole.Clearcoat, GltfMaterialTextureRole.ClearcoatRoughness, GltfMaterialTextureRole.ClearcoatNormal];
	private static readonly GltfMaterialTextureRole[] SourceRoles =
	[
		GltfMaterialTextureRole.BaseColor,
		GltfMaterialTextureRole.MetallicRoughness,
		GltfMaterialTextureRole.Occlusion,
		GltfMaterialTextureRole.Emissive
	];

	/// <summary>
	/// Prepare original encoded color/data images for the explicit provided
	/// Surface/Lit candidate. Factors remain linear uniforms. Matching image,
	/// transfer and mip demands share one owned resource; role samplers remain
	/// independent. This does not change default Prepare/Acquire behavior.
	/// </summary>
	internal static SourcePrepared PrepareSource( GltfDocument document, GltfMaterial material, Func<int, GltfImage> imageReader = null )
	{
		ArgumentNullException.ThrowIfNull( document );
		imageReader ??= document.ReadImage;
		if ( material is not null && (material.Unlit || material.AlphaMode is not ("OPAQUE" or "MASK" or "BLEND")) )
			throw new GltfException( "The provided Surface/Lit source-texture candidate does not support unlit or unknown alpha modes." );
		if ( material?.AlphaMode == "BLEND" && material.DiffuseTransmission is not null )
			throw new GltfException( "Source-lit BLEND with diffuse transmission is not yet supported." );
		if ( material?.AlphaMode == "MASK" && (!float.IsFinite( material.AlphaCutoff ) || material.AlphaCutoff < 0) )
			throw new GltfException( "MASK alpha cutoff must be finite and non-negative." );
		var factor = material?.BaseColorFactor ?? System.Numerics.Vector4.One;
		var emissive = material?.EmissiveFactor ?? System.Numerics.Vector3.Zero;
		var metallic = material?.MetallicFactor ?? 1;
		var roughness = material?.RoughnessFactor ?? 1;
		var strength = material?.OcclusionTexture?.Strength ?? 1;
		if ( material?.DiffuseTransmission is { } transmission )
		{
			foreach ( var value in new[] { transmission.Factor, transmission.ColorFactor.X, transmission.ColorFactor.Y, transmission.ColorFactor.Z } )
				if ( !float.IsFinite( value ) || value < 0 || value > 1 ) throw new GltfException( "Diffuse transmission factors must contain finite linear values in [0,1]." );
		}
		var normalScale = material?.NormalTexture?.Scale ?? 1;
		if ( !float.IsFinite( normalScale ) ) throw new GltfException( "Normal texture scale must be finite." );
		foreach ( var value in new[] { factor.X, factor.Y, factor.Z, factor.W, metallic, roughness, strength } )
			if ( !float.IsFinite( value ) || value < 0 || value > 1 ) throw new GltfException( "Source material factors must contain finite linear values in [0,1]." );
		// KHR_materials_emissive_strength scales emission above one; it stays linear and non-negative.
		foreach ( var value in new[] { emissive.X, emissive.Y, emissive.Z } )
			if ( !float.IsFinite( value ) || value < 0 ) throw new GltfException( "Emissive factor must be finite and non-negative." );
		// Existing four-role templates remain usable. A normal map requires a
		// separately generated template with explicit normal sampling support.
		GltfMaterialTextureRole[] roles = material?.NormalTexture is null ? SourceRoles : [.. SourceRoles, GltfMaterialTextureRole.Normal];
		if ( material?.DiffuseTransmission is not null ) roles = [.. roles, GltfMaterialTextureRole.DiffuseTransmission, GltfMaterialTextureRole.DiffuseTransmissionColor];
		// The diffuse-transmission shader has no coat or anisotropy lobe; those layers apply to the surface shader only.
		var clearcoat = material?.DiffuseTransmission is null ? material?.Clearcoat : null;
		var anisotropy = material?.DiffuseTransmission is null ? material?.Anisotropy : null;
		if ( clearcoat is not null ) roles = [.. roles, .. ClearcoatRoles];
		if ( anisotropy is not null ) roles = [.. roles, GltfMaterialTextureRole.Anisotropy];
		var textures = GltfMaterialTextureEncodingPlan.CreateForRoles( document, material, roles );
		var encodedResources = new Dictionary<(int Image, bool IsSrgb, bool Mipmaps), byte[]>();
		var images = new Dictionary<int, GltfImage>();
		var resources = new Dictionary<GltfMaterialTextureRole, SourceTexture>();
		foreach ( var (role, binding) in textures.Bindings )
		{
			if ( binding.ImageIndex is not int image ) throw new GltfException( $"Source material role {role} has no supported image source." );
			var sampling = GltfSamplerMapping.Map( binding.Sampler, 9987, 9729 );
			var mipmaps = !sampling.RequiresBaseMipOnly;
			var key = (image, binding.IsSrgb, mipmaps);
			if ( !images.TryGetValue( image, out var encoded ) ) { encoded = imageReader( image ); images.Add( image, encoded ); }
			// Different texture objects can select the same image under different
			// source contracts; validate every role even when resource bytes share.
			document.ValidateTextureImage( binding.TextureInfo.Index, encoded );
			if ( !encodedResources.TryGetValue( key, out var resource ) )
			{
				resource = GltfPreparedTextureCache.Shared.GetOrCreate( encoded, binding.IsSrgb, mipmaps,
					() => mipmaps ? GltfTextureCompiler.CompileWithMipmaps( encoded, binding.IsSrgb ) : GltfTextureCompiler.Compile( encoded, binding.IsSrgb ) );
				encodedResources.Add( key, resource );
			}
			resources.Add( role, new( binding, sampling, resource ) );
		}
		return new()
		{
			Textures = textures, Resources = new ReadOnlyDictionary<GltfMaterialTextureRole, SourceTexture>( resources ),
			LinearBaseColorFactor = factor, EmissiveFactor = emissive, MetallicFactor = metallic, RoughnessFactor = roughness,
			OcclusionStrength = strength, NormalScale = normalScale, DoubleSided = material?.DoubleSided ?? false,
			AlphaMask = material?.AlphaMode == "MASK", AlphaBlend = material?.AlphaMode == "BLEND", AlphaCutoff = material?.AlphaCutoff ?? .5f, DiffuseTransmission = material?.DiffuseTransmission,
			Clearcoat = clearcoat, Anisotropy = anisotropy
		};
	}

	/// <summary>
	/// Bind the explicit provided source-texture template to a completed canonical
	/// per-slot UV selection. No source stream identity enters the shared material
	/// cache; every material-bound affine row and selector does enter its key.
	/// The caller owns the borrowed proven template and the published slot packet.
	/// </summary>
	internal static GltfMaterialLease<Material> AcquireSource( SourcePrepared prepared, Material template, GltfMaterialUvPlan uv, GltfMaterialUvProjection projection = null, bool featureSidedness = false )
	{
		ArgumentNullException.ThrowIfNull( prepared ); ArgumentNullException.ThrowIfNull( template ); ArgumentNullException.ThrowIfNull( uv );
		ThreadSafe.AssertIsMainThread();
		if ( Graphics.IsActive ) throw new GltfException( "Source material resources cannot be realized during rendering." );
		if ( !template.IsValid || template.IsError ) throw new GltfException( "The provided source-texture template is unavailable." );
		if ( prepared.AlphaMask && (template.GetFeature( "F_ALPHA_TEST" ) != 1 || !template.Flags.IsAlphaTest || template.Flags.IsTranslucent) )
			throw new GltfException( "The source MASK template must have an actual opaque alpha-test permutation." );
		if ( prepared.AlphaBlend && (template.GetFeature( "F_TRANSLUCENT" ) != 1 || !template.Flags.IsTranslucent || template.Flags.IsAlphaTest) )
			throw new GltfException( "The source BLEND template must have an actual translucent permutation." );
		if ( prepared.DoubleSided && template.GetFeature( "F_RENDER_BACKFACES" ) != 1 )
			throw new GltfException( "The provided source-texture template must enable backfaces for a double-sided material." );
		if ( prepared.Resources.Count != uv.Bindings.Count ) throw new GltfException( "Source material and canonical per-slot UV roles disagree." );
		if ( projection is not null && (projection.Sources.Count > (prepared.DiffuseTransmission is null ? 5 : 7) || projection.Slots.Count != uv.Bindings.Count) )
			throw new GltfException( "The five-input shader projection does not match its material UV bindings." );
		var vectors = new List<KeyValuePair<string, Vector4>>
		{
			new( "g_vGltfBaseColor", new( prepared.LinearBaseColorFactor.X, prepared.LinearBaseColorFactor.Y, prepared.LinearBaseColorFactor.Z, (prepared.AlphaMask || prepared.AlphaBlend) ? prepared.LinearBaseColorFactor.W : 1 ) ),
			new( "g_vGltfEmissiveFactor", new( prepared.EmissiveFactor.X, prepared.EmissiveFactor.Y, prepared.EmissiveFactor.Z, 0 ) )
		};
		var scalars = new List<KeyValuePair<string, float>>
		{
			new( "g_flGltfMetallicFactor", prepared.MetallicFactor ), new( "g_flGltfRoughnessFactor", prepared.RoughnessFactor ),
			new( "g_flGltfOcclusionStrength", prepared.OcclusionStrength )
		};
		if ( prepared.DiffuseTransmission is { } transmission )
		{
			scalars.Add( new( "g_flGltfDiffuseTransmissionFactor", transmission.Factor ) );
			vectors.Add( new( "g_vGltfDiffuseTransmissionColorFactor", new( transmission.ColorFactor.X, transmission.ColorFactor.Y, transmission.ColorFactor.Z, 0 ) ) );
			foreach ( var role in new[] { GltfMaterialTextureRole.DiffuseTransmission, GltfMaterialTextureRole.DiffuseTransmissionColor } )
				scalars.Add( new( "g_flGltfHas" + role + "Texture", prepared.Resources.ContainsKey( role ) ? 1f : 0f ) );
		}
		if ( prepared.Clearcoat is { } coat )
		{
			scalars.Add( new( "g_flGltfClearcoatFactor", coat.Factor ) );
			scalars.Add( new( "g_flGltfClearcoatRoughnessFactor", coat.RoughnessFactor ) );
			scalars.Add( new( "g_flGltfClearcoatNormalScale", coat.NormalTexture?.Scale ?? 1 ) );
			foreach ( var role in ClearcoatRoles )
				scalars.Add( new( "g_flGltfHas" + role + "Texture", prepared.Resources.ContainsKey( role ) ? 1f : 0f ) );
		}
		if ( prepared.Anisotropy is { } aniso )
		{
			scalars.Add( new( "g_flGltfAnisotropyStrength", aniso.Strength ) );
			// Rotation is constant per material: pass it as a direction to avoid per-pixel trig.
			vectors.Add( new( "g_vGltfAnisotropyRotation", new( MathF.Cos( aniso.Rotation ), MathF.Sin( aniso.Rotation ), 0, 0 ) ) );
			scalars.Add( new( "g_flGltfHasAnisotropyTexture", prepared.Resources.ContainsKey( GltfMaterialTextureRole.Anisotropy ) ? 1f : 0f ) );
		}
		if ( prepared.AlphaMask ) scalars.Add( new( "g_flGltfAlphaCutoff", prepared.AlphaCutoff ) );
		// Missing glTF textures multiply by one; anonymous shader input defaults
		// are not a realized material resource. Freeze every exact presence flag
		// into the same scalar/key/readback path as the authored factors.
		foreach ( var role in SourceRoles )
			scalars.Add( new( "g_flGltfHas" + role + "Texture", prepared.Resources.ContainsKey( role ) ? 1f : 0f ) );
		if ( projection is not null || prepared.Resources.ContainsKey( GltfMaterialTextureRole.Normal ) )
		{
			scalars.Add( new( "g_flGltfHasNormalTexture", prepared.Resources.ContainsKey( GltfMaterialTextureRole.Normal ) ? 1f : 0f ) );
			scalars.Add( new( "g_flGltfNormalScale", prepared.NormalScale ) );
			// The single-sided specialization may optimize this backface-only
			// uniform away. The double-sided specialization must expose it.
			if ( !featureSidedness && (prepared.DoubleSided || template.HasParameter( "g_flGltfDoubleSided" )) )
				scalars.Add( new( "g_flGltfDoubleSided", prepared.DoubleSided ? 1f : 0f ) );
		}
		foreach ( var (role, source) in prepared.Resources )
		{
			if ( !uv.Bindings.TryGetValue( role, out var selected ) || !SameTextureInfo( source.Encoding.TextureInfo, selected.TextureInfo ) )
				throw new GltfException( $"Source material role {role} is not paired with its canonical UV selection." );
			var slot = selected.TexCoord;
			if ( projection is null )
			{
				if ( slot is < 0 or > 1 ) throw new GltfException( $"Provided source-texture role {role} requires TEXCOORD_{slot}; this candidate currently exposes only UV0 and UV1." );
			}
			else if ( !projection.Slots.TryGetValue( role, out slot ) || slot < 0 || slot >= (prepared.DiffuseTransmission is null ? 5 : 7) || slot >= projection.Sources.Count ||
				projection.Sources[slot].TexCoord != selected.TexCoord || !ReferenceEquals( projection.Sources[slot].Stream, selected.Source ) )
				throw new GltfException( $"Source material role {role} does not match the retained projected UV stream." );
			var name = "Gltf" + role;
			vectors.Add( new( "g_v" + name + "UvRowU", new( selected.Transform.RowU.X, selected.Transform.RowU.Y, selected.Transform.RowU.Z, 0 ) ) );
			vectors.Add( new( "g_v" + name + "UvRowV", new( selected.Transform.RowV.X, selected.Transform.RowV.Y, selected.Transform.RowV.Z, 0 ) ) );
			scalars.Add( new( "g_fl" + name + (projection is null ? "Uv1" : "UvSlot"), slot ) );
			RequireSourceParameter( template, "g_t" + name + "Texture" );
		}
		foreach ( var value in vectors ) RequireSourceParameter( template, value.Key );
		foreach ( var value in scalars ) RequireSourceParameter( template, value.Key );
		var ownerContext = GlobalContext.Current;
		var textures = new List<GltfMaterialLease<Texture>>(); GltfMaterialLease<Material> material = null;
		try
		{
			var bindings = new List<GltfMaterialTextureBinding>();
			var acquired = GltfNativeTextureCache.AcquireBatch( prepared.Resources.Values.Select( source => source.Resource ).ToArray() );
			textures.AddRange( acquired );
			var textureIndex = 0;
			foreach ( var (role, source) in prepared.Resources )
			{
				var texture = acquired[textureIndex++];
				bindings.Add( new( "g_tGltf" + role + "Texture", texture.Value, texture.Value.DirtyVersion, source.Sampling.State, "Gltf" + role + "Sampler" ) );
			}
			var features = new List<KeyValuePair<string, int>> { new( "F_RENDER_BACKFACES", prepared.DoubleSided ? 1 : 0 ), new( "F_ALPHA_TEST", prepared.AlphaMask ? 1 : 0 ), new( "F_TRANSLUCENT", prepared.AlphaBlend ? 1 : 0 ) };
			if ( prepared.Clearcoat is not null ) features.Add( new( "F_GLTF_CLEARCOAT", 1 ) );
			if ( prepared.Anisotropy is not null ) features.Add( new( "F_GLTF_ANISOTROPY", 1 ) );
			var key = new GltfMaterialKey( template, 0, [.. features], vectors, bindings, scalars );
			material = GltfMaterialCache.Shared.Acquire( key );
			foreach ( var value in vectors )
			{
				var actual = material.Value.GetVector4( value.Key );
				if ( actual.x != value.Value.x || actual.y != value.Value.y || actual.z != value.Value.z || (prepared.AlphaMask && value.Key == "g_vGltfBaseColor" && actual.w != value.Value.w) ) throw new GltfException( $"Source material vector {value.Key} did not retain its exact linear/affine values." );
			}
			foreach ( var value in scalars )
				if ( material.Value.GetVector4( value.Key ).x != value.Value ) throw new GltfException( $"Source material scalar {value.Key} did not retain its exact float value." );
			var retained = material;
			return new GltfMaterialLease<Material>( material.Value, () =>
			{
				using var ownerScope = new GlobalContext.GlobalContextScope( ownerContext );
				GltfGpuCleanup.ReleaseAll( ReleaseSteps( retained, textures ) );
			}, () => ThreadSafe.AssertIsMainThread() );
		}
		catch ( Exception original )
		{
			GltfGpuCleanup.Rollback( original, ReleaseSteps( material, textures ) );
			throw;
		}
	}

	private static bool SameTextureInfo( GltfTextureInfo a, GltfTextureInfo b ) => a.Index == b.Index && a.TexCoord == b.TexCoord && a.IsSrgb == b.IsSrgb && a.Offset == b.Offset && a.Tiling == b.Tiling && a.Rotation == b.Rotation;
	private static void RequireSourceParameter( Material template, string name )
	{
		if ( !template.HasParameter( name ) ) throw new GltfException( $"The provided source-texture template does not declare required parameter {name}. A writable property alone is not a capability proof." );
	}

	internal sealed class Prepared
	{
		internal Vector4 ColorTint { get; init; }
		internal float MetallicFactor { get; init; }
		internal float RoughnessFactor { get; init; }
		internal bool DoubleSided { get; init; }
		internal byte[] BaseColorResource { get; init; }
		internal byte[] NormalRoughnessResource { get; init; }
		internal byte[] MetalnessResource { get; init; }
		internal Rendering.SamplerState ColorSampler { get; init; }
		internal Rendering.SamplerState MetallicRoughnessSampler { get; init; }
	}

	/// <summary>CPU validation and native texture serialization, without GPU allocation.</summary>
	internal static Prepared Prepare( GltfDocument document, GltfMaterial material, Func<int, GltfImage> imageReader = null )
	{
		ArgumentNullException.ThrowIfNull( document );
		imageReader ??= document.ReadImage;
		if ( material is not null )
		{
			if ( material.Unlit || material.AlphaMode != "OPAQUE" || material.EmissiveFactor != System.Numerics.Vector3.Zero )
				throw new GltfException( "The stock glTF model material adapter does not yet support unlit, emissive or non-opaque materials." );
			if ( material.NormalTexture is not null || material.OcclusionTexture is not null || material.EmissiveTexture is not null )
				throw new GltfException( "The stock glTF model material adapter does not yet support normal, occlusion or emissive textures." );
		}
		var factor = material?.BaseColorFactor ?? System.Numerics.Vector4.One;
		var metallic = material?.MetallicFactor ?? 1;
		var roughness = material?.RoughnessFactor ?? 1;
		if ( !float.IsFinite( metallic ) || metallic < 0 || metallic > 1 || !float.IsFinite( roughness ) || roughness < 0 || roughness > 1 )
			throw new GltfException( "Material metallic and roughness factors must be finite values in [0,1]." );
		var tint = new Vector4( EncodeTint( factor.X ), EncodeTint( factor.Y ), EncodeTint( factor.Z ), 1 );
		byte[] colorResource = null;
		byte[] normalRoughnessResource = null, metalnessResource = null;
		var sampler = GltfSamplerMapping.Map( null, 9987, 9729 );
		var metallicRoughnessSampler = sampler;
		GltfTexture colorTexture = null, metallicRoughnessTexture = null;
		if ( material?.BaseColorTexture is { } colorInfo )
		{
			colorTexture = document.ReadTexture( colorInfo.Index );
			if ( colorTexture.Image is null ) throw new GltfException( "Base color texture has no supported image source." );
			sampler = GltfSamplerMapping.Map( colorTexture.Sampler is int index ? document.ReadSampler( index ) : null, 9987, 9729 );
		}
		if ( material?.MetallicRoughnessTexture is { } surfaceInfo )
		{
			metallicRoughnessTexture = document.ReadTexture( surfaceInfo.Index );
			if ( metallicRoughnessTexture.Image is null ) throw new GltfException( "Metallic-roughness texture has no supported image source." );
			metallicRoughnessSampler = GltfSamplerMapping.Map( metallicRoughnessTexture.Sampler is int index ? document.ReadSampler( index ) : null, 9987, 9729 );
			// Base color and roughness share the stock shader's g_sAniso. This
			// restriction cannot silently merge two intentionally different samplers.
			if ( colorTexture is not null && (sampler.State != metallicRoughnessSampler.State || sampler.RequiresBaseMipOnly != metallicRoughnessSampler.RequiresBaseMipOnly) )
				throw new GltfException( "Base color and metallic-roughness use different samplers, but the stock shader shares their sampler binding." );
		}
		if ( colorTexture is not null )
		{
			var encoded = imageReader( colorTexture.Image.Value );
			document.ValidateTextureImage( material.BaseColorTexture.Index, encoded );
			colorResource = sampler.RequiresBaseMipOnly ? GltfTextureCompiler.Compile( encoded, true ) : GltfTextureCompiler.CompileWithMipmaps( encoded, true );
		}
		if ( metallicRoughnessTexture is not null )
		{
			var encoded = imageReader( metallicRoughnessTexture.Image.Value );
			document.ValidateTextureImage( material.MetallicRoughnessTexture.Index, encoded );
			var resources = GltfTextureCompiler.CompileMetallicRoughness( encoded, metallic, roughness, !metallicRoughnessSampler.RequiresBaseMipOnly );
			normalRoughnessResource = resources.NormalRoughness; metalnessResource = resources.Metalness;
		}
		return new Prepared
		{
			ColorTint = tint, MetallicFactor = metallic, RoughnessFactor = roughness,
			DoubleSided = material?.DoubleSided ?? false, BaseColorResource = colorResource,
			ColorSampler = sampler.State, NormalRoughnessResource = normalRoughnessResource ?? CompileFlatNormalRoughness( roughness ),
			MetalnessResource = metalnessResource, MetallicRoughnessSampler = metallicRoughnessSampler.State
		};
	}

	internal static float EncodeTint( float linear )
	{
		if ( !float.IsFinite( linear ) || linear < 0 || linear > 1 ) throw new GltfException( "Base color factor must be finite and in [0,1]." );
		if ( linear is 0 or 1 ) return linear;
		return linear <= .0031308f ? 12.92f * linear : 1.055f * MathF.Pow( linear, 1f / 2.4f ) - .055f;
	}

	private static byte[] CompileFlatNormalRoughness( float roughness )
	{
		// The complex shader decodes hemi-octahedral RG normals and reads B as
		// roughness. Float32 keeps authored roughness endpoints/precision, instead
		// of approximating factors with an exponent on the stock 8-bit texture.
		float[] pixels = [1.00392163f * .5f, 1.00392163f * .5f, roughness, 1];
		var writer = new VTexWriter();
		writer.Header.Width = writer.Header.Height = writer.Header.Depth = 1;
		writer.Header.MipCount = 1;
		writer.Header.Format = VTexWriter.VTEX_Format_t.VTEX_FORMAT_RGBA32323232F;
		writer.Header.Flags = VTexWriter.VTEX_Flags_t.VTEX_FLAG_NO_LOD | VTexWriter.VTEX_Flags_t.VTEX_FLAG_CREATE_LINEAR_API_TEXTURE;
		var resource = new ResourceWriter { ResourceVersion = 1 };
		resource.SetDataBlock( writer.GetData() );
		resource.SetStreamingData( MemoryMarshal.AsBytes( pixels.AsSpan() ).ToArray() );
		return resource.ToArray();
	}

	// Anonymous vertex-color templates have no realized default color texture.
	// Keep the neutral glTF multiplier in the same retained native-texture cache
	// as authored images, rather than borrowing or disposing an engine singleton.
	private static readonly Lazy<byte[]> WhiteBaseColorResource = new( () =>
	{
		var writer = new VTexWriter();
		writer.Header.Width = writer.Header.Height = writer.Header.Depth = 1;
		writer.Header.MipCount = 1;
		writer.Header.Format = VTexWriter.VTEX_Format_t.VTEX_FORMAT_RGBA8888;
		writer.Header.Flags = VTexWriter.VTEX_Flags_t.VTEX_FLAG_NO_LOD;
		var resource = new ResourceWriter { ResourceVersion = 1 };
		resource.SetDataBlock( writer.GetData() );
		resource.SetStreamingData( new byte[] { 255, 255, 255, 255 } );
		return resource.ToArray();
	} );

	// Anonymous stock templates do not realize the declared default AO texture.
	// glTF without occlusion must use neutral 1, shared through the owned cache.
	private static readonly Lazy<byte[]> WhiteOcclusionResource = new( () =>
	{
		var writer = new VTexWriter();
		writer.Header.Width = writer.Header.Height = writer.Header.Depth = 1;
		writer.Header.MipCount = 1;
		writer.Header.Format = VTexWriter.VTEX_Format_t.VTEX_FORMAT_RGBA8888;
		writer.Header.Flags = VTexWriter.VTEX_Flags_t.VTEX_FLAG_NO_LOD | VTexWriter.VTEX_Flags_t.VTEX_FLAG_CREATE_LINEAR_API_TEXTURE;
		var resource = new ResourceWriter { ResourceVersion = 1 };
		resource.SetDataBlock( writer.GetData() );
		resource.SetStreamingData( new byte[] { 255, 255, 255, 255 } );
		return resource.ToArray();
	} );

	internal static GltfMaterialLease<Material> Acquire( Prepared prepared, Material template, bool linearVertexColor = false )
	{
		var ownerContext = GlobalContext.Current;
		var textures = new List<GltfMaterialLease<Texture>>();
		GltfMaterialLease<Material> material = null;
		try
		{
			var bindings = new List<GltfMaterialTextureBinding>();
			var normal = GltfNativeTextureCache.Acquire( prepared.NormalRoughnessResource );
			textures.Add( normal );
			var hasMetallicTexture = prepared.MetalnessResource is not null;
			// Legacy stock shader samples AO unconditionally, even when the glTF
			// material has no AO. Its anonymous template may leave it unbound.
			var occlusion = GltfNativeTextureCache.Acquire( WhiteOcclusionResource.Value );
			textures.Add( occlusion );
			bindings.Add( new( "g_tAmbientOcclusion", occlusion.Value, occlusion.Value.DirtyVersion, prepared.MetallicRoughnessSampler ) );
			// Retain requested states in bindings/cache identity. Stock sampler
			// override behavior is separately unproven (and currently fails in the
			// managed-renderer sampling probe); packing is not sampling acceptance.
			bindings.Add( new( "g_tNormal", normal.Value, normal.Value.DirtyVersion, prepared.MetallicRoughnessSampler, hasMetallicTexture ? "g_sAniso" : null ) );
			if ( prepared.BaseColorResource is not null || linearVertexColor )
			{
				var color = GltfNativeTextureCache.Acquire( prepared.BaseColorResource ?? WhiteBaseColorResource.Value );
				textures.Add( color );
				// The opaque vertex-color shader compiles g_tColor as the concrete
				// g_tColor2 parameter; setting the macro name has no effect on draw.
				// A synthetic white color has no authored sampler. Its constant value
				// can share the metallic-roughness sampler without imposing defaults
				// on the real texture that also uses g_sAniso.
				var colorSampler = prepared.BaseColorResource is null && hasMetallicTexture ? prepared.MetallicRoughnessSampler : prepared.ColorSampler;
				bindings.Add( new( linearVertexColor ? "g_tColor2" : "g_tColor", color.Value, color.Value.DirtyVersion, colorSampler, "g_sAniso" ) );
			}
			if ( hasMetallicTexture )
			{
				var metalness = GltfNativeTextureCache.Acquire( prepared.MetalnessResource ); textures.Add( metalness );
				bindings.Add( new( "g_tMetalness", metalness.Value, metalness.Value.DirtyVersion, prepared.MetallicRoughnessSampler, "g_sTrilinearWrap" ) );
			}
			var features = new List<KeyValuePair<string, int>> { new( "F_RENDER_BACKFACES", prepared.DoubleSided ? 1 : 0 ), new( linearVertexColor ? "F_GLTF_METALLIC_TEXTURE" : "F_METALNESS_TEXTURE", hasMetallicTexture ? 1 : 0 ), new( "F_ALPHA_TEST", 0 ), new( "F_TRANSLUCENT", 0 ) };
			if ( linearVertexColor ) features.Add( new( "F_GLTF_LINEAR_VERTEX_COLOR", 1 ) );
			var key = new GltfMaterialKey( template, 0, features,
				[new( "g_vColorTint", prepared.ColorTint )], bindings,
				// Texture feature reads G directly; factors are already baked into
				// the packed resources and must not be multiplied a second time.
				[new( "g_flMetalness", hasMetallicTexture ? 1f : prepared.MetallicFactor ), new( "g_flRoughnessScaleFactor", 1f )] );
			material = GltfMaterialCache.Shared.Acquire( key );
			var retained = material;
			return new GltfMaterialLease<Material>( material.Value, () =>
			{
				using var ownerScope = new GlobalContext.GlobalContextScope( ownerContext );
				GltfGpuCleanup.ReleaseAll( ReleaseSteps( retained, textures ) );
			}, () => ThreadSafe.AssertIsMainThread() );
		}
		catch ( Exception original )
		{
			GltfGpuCleanup.Rollback( original, ReleaseSteps( material, textures ) );
			throw;
		}
	}

	private static IEnumerable<Action> ReleaseSteps( GltfMaterialLease<Material> material, List<GltfMaterialLease<Texture>> textures )
	{
		if ( material is not null ) yield return material.Dispose;
		foreach ( var texture in textures ) yield return texture.Dispose;
	}
}
