using Sandbox.Gltf;
using Sandbox.Gltf.DirectGpu;
using Sandbox.Engine;

namespace Sandbox;

/// <summary>
/// Explicit candidate for the staged five-UV unlit shader. This does not select
/// a template or advertise public unlit, alpha, sampler, or pass support.
/// </summary>
internal static class GltfUnlitSourceMaterialAdapter
{
	internal sealed class Prepared
	{
		internal GltfUnlitMaterialPlan Plan { get; init; }
		internal GltfStockMaterialAdapter.SourceTexture BaseColor { get; init; }
	}

	internal static Prepared Prepare( GltfDocument document, GltfMaterial material, int? materialIndex = null, Func<int, GltfImage> imageReader = null )
	{
		ArgumentNullException.ThrowIfNull( document );
		imageReader ??= document.ReadImage;
		var plan = GltfUnlitMaterialPlan.Create( material, materialIndex );
		var textures = GltfMaterialTextureEncodingPlan.CreateForRoles( document, material, [GltfMaterialTextureRole.BaseColor] );
		GltfStockMaterialAdapter.SourceTexture color = null;
		if ( textures.Bindings.TryGetValue( GltfMaterialTextureRole.BaseColor, out var binding ) )
		{
			if ( binding.ImageIndex is not int image ) throw new GltfException( "Unlit base color has no supported image source." );
			var sampling = GltfSamplerMapping.Map( binding.Sampler, 9987, 9729 );
			var encoded = imageReader( image );
			document.ValidateTextureImage( binding.TextureInfo.Index, encoded );
			var resource = sampling.RequiresBaseMipOnly ? GltfTextureCompiler.Compile( encoded, true ) : GltfTextureCompiler.CompileWithMipmaps( encoded, true );
			color = new( binding, sampling, resource );
		}
		return new() { Plan = plan, BaseColor = color };
	}

	internal static GltfMaterialLease<Material> Acquire( Prepared prepared, Material template, GltfMaterialUvPlan uv, GltfMaterialUvProjection projection )
	{
		ArgumentNullException.ThrowIfNull( prepared ); ArgumentNullException.ThrowIfNull( template );
		ArgumentNullException.ThrowIfNull( uv ); ArgumentNullException.ThrowIfNull( projection );
		ThreadSafe.AssertIsMainThread();
		if ( Graphics.IsActive ) throw new GltfException( "Unlit material resources cannot be realized during rendering." );
		if ( !template.IsValid || template.IsError ) throw new GltfException( "The provided unlit source template is unavailable." );
		if ( projection.Sources.Count > 5 || uv.Bindings.Count != projection.Slots.Count || uv.Bindings.Count != (prepared.BaseColor is null ? 0 : 1) )
			throw new GltfException( "Unlit base-color UV projection does not match its prepared source." );
		var factor = prepared.Plan.LinearFactorForShader;
		var vectors = new List<KeyValuePair<string, Vector4>> { new( "g_vGltfBaseColor", new( factor.X, factor.Y, factor.Z, factor.W ) ) };
		var scalars = new List<KeyValuePair<string, float>> { new( "g_flGltfHasBaseColorTexture", prepared.BaseColor is null ? 0 : 1 ) };
		var mask = prepared.Plan.AlphaMode == GltfUnlitMaterialPlan.CoverageMode.Mask;
		var blend = prepared.Plan.AlphaMode == GltfUnlitMaterialPlan.CoverageMode.Blend;
		// Reflection can be permutation-specific. The caller owns a template for
		// the intended mode; this adapter never mutates a shared template to probe it.
		if ( template.GetFeature( "F_ALPHA_TEST" ) != (mask ? 1 : 0) || template.GetFeature( "F_TRANSLUCENT" ) != (blend ? 1 : 0) )
			throw new GltfException( "The provided unlit template does not match the requested alpha permutation." );
		if ( mask ) scalars.Add( new( "g_flGltfAlphaCutoff", prepared.Plan.EffectiveAlphaCutoff!.Value ) );
		if ( prepared.BaseColor is { } color )
		{
			if ( !uv.Bindings.TryGetValue( GltfMaterialTextureRole.BaseColor, out var selected ) ||
				!projection.Slots.TryGetValue( GltfMaterialTextureRole.BaseColor, out var slot ) || slot is < 0 or > 4 || slot >= projection.Sources.Count ||
				projection.Sources[slot].TexCoord != selected.TexCoord || !ReferenceEquals( projection.Sources[slot].Stream, selected.Source ) ||
				selected.TextureInfo.Index != color.Encoding.TextureInfo.Index || selected.TextureInfo.TexCoord != color.Encoding.TextureInfo.TexCoord ||
				selected.TextureInfo.Offset != color.Encoding.TextureInfo.Offset || selected.TextureInfo.Tiling != color.Encoding.TextureInfo.Tiling || selected.TextureInfo.Rotation != color.Encoding.TextureInfo.Rotation )
				throw new GltfException( "Unlit base-color source and projected UV stream disagree." );
			vectors.Add( new( "g_vGltfBaseColorUvRowU", new( selected.Transform.RowU.X, selected.Transform.RowU.Y, selected.Transform.RowU.Z, 0 ) ) );
			vectors.Add( new( "g_vGltfBaseColorUvRowV", new( selected.Transform.RowV.X, selected.Transform.RowV.Y, selected.Transform.RowV.Z, 0 ) ) );
			scalars.Add( new( "g_flGltfBaseColorUvSlot", slot ) );
			Require( template, "g_tGltfBaseColorTexture" );
		}
		foreach ( var value in vectors ) Require( template, value.Key );
		foreach ( var value in scalars ) Require( template, value.Key );
		var ownerContext = GlobalContext.Current;
		GltfMaterialLease<Texture> texture = null; GltfMaterialLease<Material> material = null;
		try
		{
			var bindings = new List<GltfMaterialTextureBinding>();
			if ( prepared.BaseColor is { } source )
			{
				texture = GltfNativeTextureCache.Acquire( source.Resource );
				bindings.Add( new( "g_tGltfBaseColorTexture", texture.Value, texture.Value.DirtyVersion, source.Sampling.State, "GltfBaseColorSampler" ) );
			}
			var key = new GltfMaterialKey( template, 0,
				[new( "F_RENDER_BACKFACES", prepared.Plan.CullBackFaces ? 0 : 1 ), new( "F_ALPHA_TEST", mask ? 1 : 0 ), new( "F_TRANSLUCENT", blend ? 1 : 0 )], vectors, bindings, scalars );
			material = GltfMaterialCache.Shared.Acquire( key );
			if ( material.Value.Flags.IsTranslucent != blend || material.Value.Flags.IsAlphaTest != mask )
				throw new GltfException( "The unlit material did not retain its requested native alpha classification." );
			foreach ( var value in vectors )
			{
				var actual = material.Value.GetVector4( value.Key );
				if ( actual.x != value.Value.x || actual.y != value.Value.y || actual.z != value.Value.z || (value.Key == "g_vGltfBaseColor" && actual.w != value.Value.w) )
					throw new GltfException( $"Unlit material vector {value.Key} did not retain its values." );
			}
			foreach ( var value in scalars )
				if ( material.Value.GetVector4( value.Key ).x != value.Value ) throw new GltfException( $"Unlit material scalar {value.Key} did not retain its value." );
			var retainedMaterial = material; var retainedTexture = texture;
			return new GltfMaterialLease<Material>( material.Value, () =>
			{
				using var scope = new GlobalContext.GlobalContextScope( ownerContext );
				GltfGpuCleanup.ReleaseAll( ReleaseSteps( retainedMaterial, retainedTexture ) );
			}, () => ThreadSafe.AssertIsMainThread() );
		}
		catch ( Exception original )
		{
			GltfGpuCleanup.Rollback( original, ReleaseSteps( material, texture ) );
			throw;
		}
	}

	private static void Require( Material template, string name )
	{
		if ( !template.HasParameter( name ) ) throw new GltfException( $"The provided unlit source template does not declare required parameter {name}." );
	}
	private static IEnumerable<Action> ReleaseSteps( GltfMaterialLease<Material> material, GltfMaterialLease<Texture> texture )
	{
		if ( material is not null ) yield return material.Dispose;
		if ( texture is not null ) yield return texture.Dispose;
	}
}
