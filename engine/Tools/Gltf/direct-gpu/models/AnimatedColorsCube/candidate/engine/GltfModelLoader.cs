using Sandbox.Engine;
using Sandbox.Gltf;

namespace Sandbox;

/// <summary>
/// glTF resource loading through the managed GPU backend. CPU preparation does
/// not require a graphics device; rendering realizes buffers and stock materials
/// lazily. This is not proof of complete format/API or compiled-layout parity.
/// </summary>
internal static class GltfModelLoader
{
	// These geometry extensions are consumed by both CreateDrawData and the
	// descriptor-driven upload path. Required codecs decode or report their real
	// native decoder error; they never silently select an ordinary fallback.
	// Material/deformation extensions remain separate rendering capabilities.
	internal static readonly IReadOnlySet<string> SupportedExtensions = new HashSet<string>( StringComparer.Ordinal )
	{
		"KHR_mesh_quantization", "KHR_texture_transform", "KHR_draco_mesh_compression", "KHR_texture_basisu",
		"KHR_meshopt_compression", "EXT_meshopt_compression", "EXT_mesh_gpu_instancing", "KHR_materials_unlit", "KHR_materials_variants", "KHR_materials_diffuse_transmission"
	};

	/// <summary>The loader's document policy, shared by source and compiled input.</summary>
	internal static GltfDocument ReadDocument( byte[] source, Func<string, byte[]> resolve = null ) =>
		GltfReader.ReadWithPartialMaterialColorAnimation( source, resolve, supportedExtensions: SupportedExtensions );

	internal static bool IsGltfPath( string path )
	{
		if ( path is null ) return false;
		var fragment = path.IndexOf( '#' );
		if ( fragment >= 0 ) path = path[..fragment];
		return path.EndsWith( ".gltf", StringComparison.OrdinalIgnoreCase ) ||
		 path.EndsWith( ".glb", StringComparison.OrdinalIgnoreCase ) ||
		 path.EndsWith( ".glb_c", StringComparison.OrdinalIgnoreCase );
	}

	internal static string CompiledPath( string path ) => path.EndsWith( ".gltf", StringComparison.OrdinalIgnoreCase )
		? path + ".glb_c" : path.EndsWith( ".glb", StringComparison.OrdinalIgnoreCase ) ? path + "_c" : path;

	internal static Model Load( string path, int? sceneIndex = null )
	{
		ThreadSafe.AssertIsMainThread();
		var selection = ParseScenePath( path, sceneIndex );
		var mountedPath = NormalizeMountedPath( selection.Path );
		var canonical = SceneResourcePath( mountedPath, selection.Scene );
		if ( Game.Resources.TryGet<Model>( canonical, out var cached ) && cached.IsValid && !cached.IsError )
			return cached;
		var resource = Prepare( mountedPath, sceneIndex: selection.Scene );
		try { return Model.FromGltfResource( resource, canonical ); }
		catch { resource.ReleaseOwner(); throw; }
	}

	internal static (Model Model, GltfManagedModelResource Resource) PrepareReload( string path, byte[] compiled )
	{
		ThreadSafe.AssertIsMainThread();
		var selection = ParseScenePath( path );
		var mountedPath = NormalizeMountedPath( selection.Path );
		var canonical = SceneResourcePath( mountedPath, selection.Scene );
		if ( !Game.Resources.TryGet<Model>( canonical, out var model ) || model.GltfResource is null ) return default;
		return (model, Prepare( mountedPath, compiled, selection.Scene ));
	}

	/// <summary>Prepare all live resource selections belonging to exactly one logical source.</summary>
	internal static IReadOnlyList<(Model Model, GltfManagedModelResource Resource)> PrepareReloads( string path, byte[] compiled )
	{
		ThreadSafe.AssertIsMainThread(); ArgumentNullException.ThrowIfNull( compiled );
		var canonical = SceneResourcePath( NormalizeMountedPath( ParseScenePath( path ).Path ), null );
		// Snapshot the weak index before any resources are prepared or notifications
		// can alter it. Full parsed identities prevent similarly prefixed filenames,
		// source .gltf/.glb siblings or individual scenes from being conflated.
		var models = Game.Resources.FindWeakByPathPrefix<Model>( canonical )
			.Where( model => model.IsValid && model.GltfResource is not null && IsGltfPath( model.ResourcePath ) &&
				string.Equals( SceneResourcePath( NormalizeMountedPath( ParseScenePath( model.ResourcePath ).Path ), null ), canonical, StringComparison.OrdinalIgnoreCase ) )
			.Distinct().OrderBy( model => model.ResourcePath, StringComparer.Ordinal ).ToArray();
		var prepared = new List<(Model Model, GltfManagedModelResource Resource)>();
		try
		{
			foreach ( var model in models )
			{
				var selection = ParseScenePath( model.ResourcePath );
				prepared.Add( (model, Prepare( NormalizeMountedPath( selection.Path ), compiled, selection.Scene )) );
			}
			return prepared;
		}
		catch ( Exception error )
		{
			Sandbox.Gltf.DirectGpu.GltfGpuCleanup.Rollback( error, prepared.AsEnumerable().Reverse().Select<(Model Model, GltfManagedModelResource Resource), Action>( entry => entry.Resource.ReleaseOwner ) );
			throw;
		}
	}

	private static GltfManagedModelResource Prepare( string mountedPath, byte[] preparedBytes = null, int? sceneIndex = null )
	{
		if ( Graphics.IsActive ) throw new GltfException( "glTF models cannot be constructed during rendering." );

		var fs = EngineFileSystem.Mounted ?? throw new GltfException( "No mounted filesystem is available for glTF loading." );
		var storagePath = mountedPath;
		// Serialized resource paths omit _c. Resolve the compiled representation on a
		// cold load too, while preserving the logical .glb identity and relative URIs.
		var compiledPath = CompiledPath( mountedPath );
		if ( !fs.FileExists( storagePath ) && fs.FileExists( compiledPath ) )
			storagePath = compiledPath;
		if ( preparedBytes is null && !fs.FileExists( storagePath ) ) throw new GltfException( $"glTF model '{mountedPath}' was not found in the mounted filesystem." );
		var source = preparedBytes ?? fs.ReadAllBytes( storagePath ).ToArray();
		byte[] Resolve( string uri )
		{
			var dependency = ResolveMountedUri( mountedPath, uri );
			if ( !fs.FileExists( dependency ) ) throw new GltfException( $"glTF dependency '{uri}' was not found at '{dependency}'." );
			return fs.ReadAllBytes( dependency ).ToArray();
		}

		using var sourceOwner = new DocumentOwner( ReadDocument( source, Resolve ) );
		var document = sourceOwner.Document;
		if ( document.UnsupportedOptionalExtensions.Count > 0 )
			Log.Warning( $"glTF '{mountedPath}' uses unsupported optional extensions: {string.Join( ", ", document.UnsupportedOptionalExtensions )}. The standard core fallback is used where available." );
		var resource = new GltfManagedModelResource( document, sceneIndex );
		sourceOwner.Release(); // Successful preparation now owns this document generation.
		// A selected scene may legally contain no drawable primitives. Preserve it
		// as an empty model; HasRenderMeshes reports whether anything can render.
		return resource;
	}

	// Selection is an identity fragment, never a mounted filename or a URI base.
	// Canonicalize it before cache lookup so source/compiled aliases share one view.
	internal static (string Path, int? Scene) ParseScenePath( string path, int? sceneIndex = null )
	{
		if ( sceneIndex < 0 ) throw new ArgumentOutOfRangeException( nameof( sceneIndex ) );
		if ( path is null ) throw new ArgumentNullException( nameof( path ) );
		var fragment = path.IndexOf( '#' );
		if ( fragment < 0 ) return (path, sceneIndex);
		var suffix = path[(fragment + 1)..];
		if ( !suffix.StartsWith( "scene=", StringComparison.Ordinal ) ||
			!int.TryParse( suffix.AsSpan( 6 ), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var selected ) || selected < 0 )
			throw new GltfException( "Invalid glTF scene selection fragment; expected #scene=<nonnegative index>." );
		if ( sceneIndex.HasValue && sceneIndex.Value != selected ) throw new ArgumentException( "Conflicting glTF scene selections.", nameof( sceneIndex ) );
		return (path[..fragment], selected);
	}

	internal static string SceneResourcePath( string mountedPath, int? sceneIndex ) =>
		(mountedPath.EndsWith( ".gltf.glb_c", StringComparison.OrdinalIgnoreCase ) ? Resource.FixPath( mountedPath[..^6] ) : Resource.FixPath( mountedPath )) + (sceneIndex.HasValue ? "#scene=" + sceneIndex.Value.ToString( System.Globalization.CultureInfo.InvariantCulture ) : "");

	private sealed class DocumentOwner( GltfDocument document ) : IDisposable
	{
		internal GltfDocument Document { get; private set; } = document;
		internal void Release() => Document = null;
		public void Dispose() => Document?.Dispose();
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
