using Sandbox.Gltf;
using System;
using System.IO;

namespace Editor;

/// <summary>Packages a source glTF without modifying its authored data.</summary>
public static class GltfAssetImport
{
	public const string ConsolidateBuffersKey = "gltfConsolidateBuffers";
	public const string CompressionKey = "gltfCompression";

	/// <summary>The compile compression of an asset. An asset with no settings gets empty settings.</summary>
	public static GltfCompileCompression ReadCompression( Asset asset ) => GltfCompileCompression.FromCompiler( ReadCompilerSettings( asset ) );

	static GltfCompressionSettings ReadCompilerSettings( Asset asset ) =>
		asset?.MetaData?.GetElement( CompressionKey ) is { ValueKind: System.Text.Json.JsonValueKind.Object } element
			? GltfCompressionSettings.FromJson( System.Text.Json.Nodes.JsonNode.Parse( element.GetRawText() ) )
			: new GltfCompressionSettings();

	/// <summary>Write the compile compression of an asset. Empty settings remove the key.</summary>
	public static void WriteCompression( Asset asset, GltfCompileCompression settings )
	{
		ArgumentNullException.ThrowIfNull( asset );
		var json = settings?.ToCompiler().ToJson();
		asset.MetaData.Set( CompressionKey, json is { Count: > 0 } ? json : null );
	}
	private static readonly Dictionary<string, HashSet<string>> Dependencies = new( StringComparer.OrdinalIgnoreCase );
	private static readonly HashSet<string> Pending = new( StringComparer.OrdinalIgnoreCase );
	private static RealTimeSince sinceChange;

	// What the last import of each source compiled. A file event for content that was just imported is
	// an echo (the glTF editor saves and imports in one step), and importing again only causes a hitch.
	private static readonly Dictionary<string, ulong> ImportedInput = new( StringComparer.OrdinalIgnoreCase );
	// Sources queued because a file they reference changed. Their own bytes did not change, so they always import.
	private static readonly HashSet<string> DependencyChanged = new( StringComparer.OrdinalIgnoreCase );

	internal static bool IsSource( string path ) => path is not null &&
		(path.EndsWith( ".gltf", StringComparison.OrdinalIgnoreCase ) || path.EndsWith( ".glb", StringComparison.OrdinalIgnoreCase ));

	private static bool IsProjectAsset( Asset asset )
	{
		var root = Project.Current?.GetAssetsPath();
		var source = asset?.AbsoluteSourcePath;
		if ( string.IsNullOrEmpty( root ) || string.IsNullOrEmpty( source ) ) return false;
		var relative = Path.GetRelativePath( root, source );
		return !Path.IsPathRooted( relative ) && relative != ".." && !relative.StartsWith( ".." + Path.DirectorySeparatorChar, StringComparison.Ordinal );
	}

	/// <summary>Whether this source belongs to the open project's editable assets.</summary>
	public static bool CanImport( Asset asset ) => asset is not null && IsSource( asset.RelativePath ) &&
		asset.MetaData is not null && !asset.IsCloud && IsProjectAsset( asset );

	internal static void ImportAll()
	{
		Dependencies.Clear();
		Pending.Clear();
		foreach ( var asset in AssetSystem.All.Where( x => IsSource( x.RelativePath ) && x.HasSourceFile && !x.IsCloud && IsProjectAsset( x ) ) )
			Pending.Add( asset.RelativePath );
		sinceChange = 0;
	}

	internal static void ContentChanged( string path )
	{
		path = path?.Replace( '\\', '/' ).TrimStart( '/' );
		if ( string.IsNullOrEmpty( path ) ) return;
		if ( path.EndsWith( ".meta", StringComparison.OrdinalIgnoreCase ) && IsSource( path[..^5] ) )
			Pending.Add( path[..^5] );
		else if ( IsSource( path ) )
		{
			if ( FileSystem.Content.FileExists( path ) ) Pending.Add( path );
			else
			{
				Pending.Remove( path );
				Dependencies.Remove( path );
				Log.Warning( $"glTF source '{path}' was removed. Its previously compiled file was retained and may still be loaded." );
			}
		}
		foreach ( var entry in Dependencies )
			if ( entry.Value.Contains( path ) )
			{
				Pending.Add( entry.Key );
				DependencyChanged.Add( entry.Key );
			}
		if ( Pending.Count > 0 ) sinceChange = 0;
	}

	[EditorEvent.Frame]
	private static void ImportPending()
	{
		if ( Pending.Count == 0 || sinceChange < 0.2f ) return;
		var path = Pending.First();
		Pending.Remove( path );
		var asset = AssetSystem.FindByPath( path );
		if ( asset is null && IsSource( path ) )
		{
			var fullPath = FileSystem.Content.GetFullPath( path );
			if ( !string.IsNullOrEmpty( fullPath ) && File.Exists( fullPath ) ) asset = AssetSystem.RegisterFile( fullPath );
		}
		if ( asset is null || !asset.HasSourceFile || asset.IsCloud || !IsProjectAsset( asset ) ) return;
		var dependencyChanged = DependencyChanged.Remove( path );
		if ( !dependencyChanged && ImportedInput.TryGetValue( asset.RelativePath.TrimStart( '/' ), out var imported ) && imported == InputHash( asset ) ) return;
		try { Reimport( asset ); }
		catch ( Exception e ) { Log.Warning( e, $"Could not import glTF '{path}'" ); }
	}

	/// <summary>
	/// Rebuild the lossless GLB alongside the source. External image references
	/// remain relative to the source and are not embedded by this compiler.
	/// </summary>
	public static void Reimport( Asset asset )
	{
		ArgumentNullException.ThrowIfNull( asset );
		if ( !CanImport( asset ) ) throw new InvalidOperationException( "Import requires an editable .gltf or .glb source in the current project's Assets folder." );
		var sourcePath = asset.AbsoluteSourcePath;
		if ( string.IsNullOrEmpty( sourcePath ) || !File.Exists( sourcePath ) )
			throw new FileNotFoundException( "The glTF source file is unavailable.", sourcePath );

		var relative = asset.RelativePath;
		var dependencies = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		byte[] Resolve( string uri )
		{
			var path = GltfModelLoader.ResolveMountedUri( relative, uri );
			dependencies.Add( path );
			var absolute = FileSystem.Content.GetFullPath( path );
			if ( string.IsNullOrEmpty( absolute ) || !File.Exists( absolute ) )
				throw new FileNotFoundException( $"glTF dependency '{uri}' was not found.", path );
			return File.ReadAllBytes( absolute );
		}

		var consolidate = asset.MetaData.Get( ConsolidateBuffersKey, true );
		byte[] bytes;
		try
		{
			// Editor overrides (see GltfEditorSession) apply to the source JSON. The source file stays unchanged.
			var overrideWarnings = new List<string>();
			var source = GltfOverrides.ApplyToSource( File.ReadAllBytes( sourcePath ), GltfOverrides.Read( asset ), overrideWarnings );
			foreach ( var warning in overrideWarnings ) Log.Warning( $"{relative}: {warning}" );
			var compression = ReadCompilerSettings( asset );
			bytes = GltfCompiler.Compile( source, Resolve,
				GltfModelLoader.SupportedExtensions, 256L * 1024 * 1024, consolidate, orderForLodStreaming: compression.IsEmpty );
			if ( !compression.IsEmpty )
			{
				// Compression adds buffer views at the end, so the order for LOD streaming is made after it.
				var compressionWarnings = new List<string>();
				var compressed = GltfCompression.Apply( bytes, compression, GltfModelLoader.SupportedExtensions, compressionWarnings, out var result );
				foreach ( var warning in compressionWarnings ) Log.Warning( $"{relative}: {warning}" );
				if ( result.Accessors > 0 )
					Log.Info( $"{relative}: compression stored {result.Accessors} accessors in {result.CompressedBytes:N0} bytes, from {result.SourceBytes:N0}." );
				bytes = GltfCompiler.Compile( compressed, Resolve, GltfModelLoader.SupportedExtensions, 256L * 1024 * 1024, consolidate, orderForLodStreaming: true );
			}
			using var document = GltfModelLoader.ReadDocument( bytes, Resolve );
			if ( document.Root.TryGetProperty( "images", out var images ) && images.ValueKind == System.Text.Json.JsonValueKind.Array )
				foreach ( var image in images.EnumerateArray() )
					if ( image.TryGetProperty( "uri", out var uri ) && uri.ValueKind == System.Text.Json.JsonValueKind.String && uri.GetString() is { } name && !name.StartsWith( "data:", StringComparison.OrdinalIgnoreCase ) )
						dependencies.Add( GltfModelLoader.ResolveMountedUri( relative, name ) );
			// The native model data: what the runtime and the editor load, with no per-vertex or per-texel work at load.
			try { bytes = GltfNativeCompiler.AppendTo( bytes, document ); }
			catch ( GltfNativeUnsupportedException e ) { Log.Info( $"{relative}: {e.Message} It loads through the source path." ); }
		}
		finally { Dependencies[relative.TrimStart( '/' )] = dependencies; }
		ImportedInput[relative.TrimStart( '/' )] = InputHash( asset );
		// Keep .gltf and .glb sources with the same basename distinct.
		var output = GltfModelLoader.CompiledPath( sourcePath );
		var replacements = GltfModelLoader.PrepareReloads( relative, bytes );
		var replacementTransferred = false;
		AggregateException postCommitErrors = null;
		try
		{
			PublishAtomically( output, bytes, () =>
			{
				postCommitErrors = Model.ReplaceGltfResources( replacements );
				replacementTransferred = true;
			} );
			if ( postCommitErrors is not null )
				Log.Warning( postCommitErrors, $"glTF '{relative}' was imported, but reload notifications or cleanup failed." );
			GltfModelLoader.ReloadNative( relative, bytes );
		}
		finally
		{
			if ( !replacementTransferred )
				foreach ( var replacement in replacements ) replacement.Resource.ReleaseOwner();
		}
	}

	/// <summary>A hash of everything in the asset itself that the compile reads: the source bytes and its import settings.</summary>
	static ulong InputHash( Asset asset )
	{
		using var hash = System.Security.Cryptography.IncrementalHash.CreateHash( System.Security.Cryptography.HashAlgorithmName.SHA256 );
		hash.AppendData( File.ReadAllBytes( asset.AbsoluteSourcePath ) );
		hash.AppendData( System.Text.Encoding.UTF8.GetBytes( GltfOverrides.Read( asset )?.ToJsonString() ?? "" ) );
		hash.AppendData( [asset.MetaData.Get( ConsolidateBuffersKey, true ) ? (byte)1 : (byte)0] );
		hash.AppendData( System.Text.Encoding.UTF8.GetBytes( ReadCompilerSettings( asset ).ToJson().ToJsonString() ) );
		return BitConverter.ToUInt64( hash.GetHashAndReset() );
	}

	internal static void PublishAtomically( string output, byte[] bytes, Action afterPublish )
	{
		var temporary = output + "." + Guid.NewGuid().ToString( "N" ) + ".tmp";
		var backup = output + "." + Guid.NewGuid().ToString( "N" ) + ".bak";
		var backedUp = false;
		var published = false;
		try
		{
			File.WriteAllBytes( temporary, bytes );
			if ( File.Exists( output ) )
			{
				File.Move( output, backup );
				backedUp = true;
			}
			File.Move( temporary, output );
			published = true;
			afterPublish();
		}
		catch
		{
			if ( published ) File.Delete( output );
			if ( backedUp ) File.Move( backup, output );
			throw;
		}
		finally
		{
			if ( File.Exists( temporary ) ) File.Delete( temporary );
		}
		if ( backedUp )
		{
			try { File.Delete( backup ); }
			catch ( Exception e ) { Log.Warning( e, $"Could not remove glTF import backup '{backup}'" ); }
		}
	}
}
