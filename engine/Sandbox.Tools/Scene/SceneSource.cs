using System.IO;
using System.Text.Json;

namespace Editor;

internal static class SceneSource
{
	internal static Asset FindAsset( SceneFile file )
	{
		if ( file is null )
			return null;

		var asset = AssetSystem.FindByPath( file.ResourcePath );
		if ( file.Guid != System.Guid.Empty && (asset is null || asset.Guid != file.Guid) )
			asset = AssetSystem.All.FirstOrDefault( x => x.Guid == file.Guid );

		if ( asset is not null && file.ResourcePath != asset.Path )
			file.InitializeSource( asset.Path, asset.Guid );

		return asset;
	}

	internal static string ReadJson( string path )
	{
		var json = File.ReadAllText( path );
		if ( !json.StartsWith( '<' ) )
			return json;

		var kv = NativeEngine.EngineGlue.LoadKeyValues3( json );
		try
		{
			return NativeEngine.EngineGlue.KeyValues3ToJson( kv.FindOrCreateMember( "data" ) );
		}
		finally
		{
			kv.DeleteThis();
		}
	}

	internal static SceneFile LoadForEditing( Asset asset )
	{
		var path = asset.GetSourceFile( true );
		var json = ReadJson( path );
		var blobPath = path + "_d";
		var blobs = File.Exists( blobPath ) ? File.ReadAllBytes( blobPath ) : [];
		return SceneFile.FromSource( asset.Path, asset.Guid, json, blobs );
	}

	internal static bool Save( Asset asset, SceneFile file )
	{
		file.InitializeSource( asset.Path, asset.Guid );
		if ( !asset.SaveSource( file ) )
			return false;

		return asset.LoadResource<SceneFile>() is not null;
	}

	internal static SceneFile LoadForPlay( SceneEditorSession editor )
	{
		if ( editor.HasUnsavedChanges || editor.Scene.Source is not SceneFile { ResourcePath: { Length: > 0 } } source )
			return editor.Scene.CreateSceneFile();

		try
		{
			if ( !editor.IsMounted && FindAsset( source ) is { } asset )
			{
				return LoadCompiledScene( asset.Path, asset.Guid, asset.GetCompiledFile( true ),
					asset.GetSourceFile( true ), System.OperatingSystem.IsLinux() );
			}

			var file = SceneFile.Load( source.ResourcePath );
			if ( file is null )
				Log.Warning( $"Could not load scene '{source.ResourcePath}'." );
			return file;
		}
		catch ( System.Exception e ) when ( e is IOException or InvalidDataException or System.UnauthorizedAccessException or JsonException )
		{
			Log.Error( e, $"Could not load compiled scene '{source.ResourcePath}'." );
			return null;
		}
	}

	/// <summary>
	/// Linux's native directory watcher can leave the compiled-file catalog stale. For a local
	/// scene only, accept the adjacent compiled output after decoding it through the normal loader.
	/// This does not register assets, recompile source, or alter the native resource cache.
	/// </summary>
	internal static SceneFile LoadCompiledScene( string resourcePath, System.Guid guid, string compiledPath, string sourcePath, bool allowDiskFallback )
	{
		var usedDiskFallback = false;
		if ( allowDiskFallback && !File.Exists( compiledPath )
			&& !string.IsNullOrEmpty( sourcePath ) && Path.IsPathFullyQualified( sourcePath )
			&& string.Equals( Path.GetExtension( sourcePath ), ".scene", System.StringComparison.OrdinalIgnoreCase )
			&& File.Exists( sourcePath ) && File.Exists( sourcePath + "_c" ) )
		{
			compiledPath = sourcePath + "_c";
			usedDiskFallback = true;
		}

		if ( string.IsNullOrEmpty( compiledPath ) )
			throw new FileNotFoundException( $"No compiled file exists for '{resourcePath}'." );

		var file = SceneFile.FromCompiled( resourcePath, guid, File.ReadAllBytes( compiledPath ) );
		if ( usedDiskFallback )
			Log.Warning( $"Loaded compiled scene '{resourcePath}' from disk because the native asset catalog did not expose its compiled file. Native directory watching is unavailable on Linux." );
		return file;
	}
}
