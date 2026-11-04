using Sandbox.Engine;
using Sandbox.Gltf;

namespace Sandbox;

/// <summary>
/// Initial static, untextured glTF adapter through the existing native model and
/// stock-material pipelines. It is not a complete glTF loader or a GPU-optimized
/// compiled-resource implementation. Unsupported material/deformation inputs fail.
/// </summary>
internal static class GltfModelLoader
{
	internal static bool IsGltfPath( string path ) => path is not null &&
		(path.EndsWith( ".gltf", StringComparison.OrdinalIgnoreCase ) ||
		 path.EndsWith( ".glb", StringComparison.OrdinalIgnoreCase ) ||
		 path.EndsWith( ".glb_c", StringComparison.OrdinalIgnoreCase ));

	internal static Model Load( string path )
	{
		ThreadSafe.AssertIsMainThread();
		var mountedPath = NormalizeMountedPath( path );
		var canonical = Resource.FixPath( mountedPath );
		if ( Game.Resources.TryGet<Model>( canonical, out var cached ) && cached.IsValid && !cached.IsError )
			return cached;
		if ( Graphics.IsActive ) throw new GltfException( "glTF models cannot be constructed during rendering." );

		var fs = EngineFileSystem.Mounted ?? throw new GltfException( "No mounted filesystem is available for glTF loading." );
		var storagePath = mountedPath;
		// Serialized resource paths omit _c. Resolve the compiled representation on a
		// cold load too, while preserving the logical .glb identity and relative URIs.
		if ( !fs.FileExists( storagePath ) && mountedPath.EndsWith( ".glb", StringComparison.OrdinalIgnoreCase ) && fs.FileExists( mountedPath + "_c" ) )
			storagePath = mountedPath + "_c";
		if ( !fs.FileExists( storagePath ) ) throw new GltfException( $"glTF model '{mountedPath}' was not found in the mounted filesystem." );
		var source = fs.ReadAllBytes( storagePath ).ToArray();
		byte[] Resolve( string uri )
		{
			var dependency = ResolveMountedUri( mountedPath, uri );
			if ( !fs.FileExists( dependency ) ) throw new GltfException( $"glTF dependency '{uri}' was not found at '{dependency}'." );
			return fs.ReadAllBytes( dependency ).ToArray();
		}

		using var document = GltfReader.Read( source, Resolve );
		if ( document.UnsupportedOptionalExtensions.Count > 0 )
			Log.Warning( $"glTF '{mountedPath}' uses unsupported optional extensions: {string.Join( ", ", document.UnsupportedOptionalExtensions )}. The standard core fallback is used where available." );
		var scene = GltfStaticSceneAssembler.Assemble( document );
		if ( !scene.HasBounds || scene.Primitives.Count == 0 ) throw new GltfException( "The glTF default scene contains no renderable geometry." );

		// Validate all selected materials and geometry before allocating native resources.
		var prepared = new List<(GltfModelPrimitiveBuilder.PreparedPrimitive Geometry, bool DoubleSided, System.Numerics.Vector4 Color)>();
		foreach ( var primitive in scene.Primitives )
		{
			var material = primitive.Data.Material is int materialIndex ? document.ReadMaterial( materialIndex ) : null;
			ValidateMaterial( material );
			foreach ( var color in primitive.Data.Colors.Span )
				if ( color != System.Numerics.Vector4.One )
					throw new GltfException( "The initial glTF model loader does not yet support non-white vertex colors." );
			var effectiveColor = material?.BaseColorFactor ?? System.Numerics.Vector4.One;
			// OPAQUE ignores alpha; cache effective rendering inputs independently of
			// authored material identity, which remains in the glTF source.
			effectiveColor.W = 1;
			prepared.Add( (GltfModelPrimitiveBuilder.Prepare( primitive.Data ), material?.DoubleSided ?? false, effectiveColor) );
		}

		var stock = Material.Load( "materials/default/white.vmat" );
		if ( stock is null || !stock.IsValid || stock.IsError ) throw new GltfException( "The stock glTF material template is unavailable." );
		var leases = new List<GltfMaterialLease<Material>>();
		var meshes = new List<Mesh>();
		Model result = null;
		try
		{
			var materials = new Dictionary<(bool DoubleSided, System.Numerics.Vector4 Color), Material>();
			var builder = Model.Builder.WithName( "gltf_model_" + Guid.NewGuid().ToString( "N" ) );
			foreach ( var (geometry, doubleSided, color) in prepared )
			{
				var descriptor = (doubleSided, color);
				if ( !materials.TryGetValue( descriptor, out var material ) )
				{
					var key = new GltfMaterialKey( stock, 0,
						[new( "F_RENDER_BACKFACES", doubleSided ? 1 : 0 ), new( "F_METALNESS_TEXTURE", 0 ), new( "F_ALPHA_TEST", 0 ), new( "F_TRANSLUCENT", 0 )],
						[new( "g_vColorTint", color )], [], [new( "g_flMetalness", 1f ), new( "g_flRoughnessScaleFactor", 0f )] );
					var lease = GltfMaterialCache.Shared.Acquire( key );
					leases.Add( lease );
					material = lease.Value;
					materials.Add( descriptor, material );
				}
				var mesh = new Mesh( "gltf_primitive", material, geometry.PrimitiveType );
				meshes.Add( mesh );
				mesh.CreateVertexBuffer( geometry.Vertices.Length, geometry.Vertices.AsSpan() );
				mesh.CreateIndexBuffer( geometry.Indices.Length, geometry.Indices.AsSpan() );
				mesh.Bounds = geometry.Bounds;
				builder.AddMesh( mesh );
			}
			builder.WithViewBounds( new BBox( scene.BoundsMin, scene.BoundsMax ) );
			result = builder.Create();
			if ( result is null || ReferenceEquals( result, Model.Error ) )
			{
				result = null;
				throw new GltfException( "Native glTF model construction failed." );
			}
			// Transfer ownership before validation/publication so rollback follows the
			// same ordered model -> mesh -> material release as ordinary destruction.
			result.SetGltfLifetime( GlobalContext.Current, leases, meshes );
			leases = null;
			meshes = null;
			if ( !result.IsValid || result.IsError ) throw new GltfException( "Native glTF model construction failed." );

			// ModelBuilder registers its temporary procedural name and native GUID. Remove
			// those aliases before publishing a stable path for this mounted source.
			Game.Resources.Unregister( result );
			result.Guid = Guid.Empty;
			result.Name = canonical;
			result.RegisterWeakResourceId( canonical );
			return result;
		}
		catch
		{
			if ( meshes is null && leases is null ) result?.Destroy();
			else
			{
				try { ReleaseMeshes( meshes ); }
				catch ( Exception e ) { Log.Warning( e, e.Message ); }
				if ( leases is not null )
					foreach ( var lease in leases )
						try { lease.Dispose(); }
						catch ( Exception e ) { Log.Warning( e, e.Message ); }
			}
			throw;
		}
	}

	private static void ValidateMaterial( GltfMaterial material )
	{
		if ( material is null ) return; // glTF's default material.
		if ( material.MetallicFactor != 1 || material.RoughnessFactor != 1 ) throw new GltfException( "The initial glTF model loader supports only default metallicFactor and roughnessFactor." );
		if ( material.EmissiveFactor != System.Numerics.Vector3.Zero || material.Unlit || material.AlphaMode != "OPAQUE" )
			throw new GltfException( "The initial glTF model loader does not yet support emissive, unlit or non-opaque materials." );
		if ( material.BaseColorTexture is not null || material.MetallicRoughnessTexture is not null || material.NormalTexture is not null || material.OcclusionTexture is not null || material.EmissiveTexture is not null )
			throw new GltfException( "The initial glTF model loader does not yet support material textures." );
	}

	internal static void ReleaseMeshes( List<Mesh> meshes )
	{
		if ( meshes is null ) return;
		Exception first = null;
		foreach ( var mesh in meshes )
		{
			if ( mesh is null || mesh.native.IsNull ) continue;
			var native = mesh.native;
			mesh.native = default;
			GC.SuppressFinalize( mesh );
			try { native.DestroyStrongHandle(); }
			catch ( Exception e ) { first ??= e; Log.Warning( e, e.Message ); }
		}
		if ( first is not null ) throw first;
	}

	/// <summary>Resolve a relative glTF URI within the logical mounted filesystem root.</summary>
	internal static string ResolveMountedUri( string modelPath, string uri )
	{
		if ( string.IsNullOrEmpty( uri ) || uri.Contains( '?' ) || uri.Contains( '#' ) ) throw new GltfException( "glTF dependency URI must be a relative path without a query or fragment." );
		for ( var i = 0; i < uri.Length; i++ )
		{
			if ( uri[i] != '%' ) continue;
			if ( i + 2 >= uri.Length || !Uri.IsHexDigit( uri[i + 1] ) || !Uri.IsHexDigit( uri[i + 2] ) ) throw new GltfException( "glTF dependency URI contains an invalid percent escape." );
			i += 2;
		}
		var decoded = Uri.UnescapeDataString( uri );
		if ( decoded.StartsWith( '/' ) || decoded.Contains( '\\' ) || decoded.Contains( ':' ) || decoded.Contains( '\0' ) ) throw new GltfException( "glTF dependency URI must stay within the mounted filesystem." );
		var model = NormalizeMountedPath( modelPath );
		var slash = model.LastIndexOf( '/' );
		var segments = slash < 0 ? new List<string>() : model[..slash].Split( '/' ).ToList();
		foreach ( var segment in decoded.Split( '/' ) )
		{
			if ( segment is "" or "." ) continue;
			if ( segment == ".." )
			{
				if ( segments.Count == 0 ) throw new GltfException( "glTF dependency URI escapes the mounted filesystem." );
				segments.RemoveAt( segments.Count - 1 );
			}
			else segments.Add( segment );
		}
		if ( segments.Count == 0 ) throw new GltfException( "glTF dependency URI does not name a file." );
		return string.Join( '/', segments );
	}

	internal static string NormalizeMountedPath( string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) || path.Contains( '\\' ) || path.Contains( ':' ) || path.Contains( '\0' ) ) throw new GltfException( "Invalid mounted glTF model path." );
		var segments = new List<string>();
		foreach ( var segment in path.TrimStart( '/' ).Split( '/' ) )
		{
			if ( segment is "" or "." ) continue;
			if ( segment == ".." )
			{
				if ( segments.Count == 0 ) throw new GltfException( "glTF model path escapes the mounted filesystem." );
				segments.RemoveAt( segments.Count - 1 );
			}
			else segments.Add( segment );
		}
		if ( segments.Count == 0 ) throw new GltfException( "glTF model path does not name a file." );
		return string.Join( '/', segments );
	}
}
