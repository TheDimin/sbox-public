using System.Collections.ObjectModel;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sandbox.Gltf;

/// <summary>A packaged GLB and the immutable external images it refers to.</summary>
public sealed class GltfArtifactBundle
{
	public byte[] Artifact { get; init; }
	public IReadOnlyDictionary<string, byte[]> Dependencies { get; init; }
}

/// <summary>Finds external image payloads that must accompany a packaged GLB.</summary>
public static class GltfArtifactDependencies
{
	/// <summary>
	/// Resolves original image URIs through the caller, then rewrites those known
	/// references to deterministic content-addressed package-local names.
	/// </summary>
	public static GltfArtifactBundle Prepare( byte[] compiled, Func<string, byte[]> resolve, IReadOnlySet<string> supportedExtensions = null )
	{
		using var document = GltfReader.Read( compiled, supportedExtensions: supportedExtensions );
		if ( compiled.Length < 20 || BinaryPrimitives.ReadUInt32LittleEndian( compiled ) != 0x46546c67 )
			throw new GltfException( "A compiled GLB container is required." );
		var root = JsonNode.Parse( document.Root.GetRawText() )?.AsObject() ?? throw new GltfException( "glTF root must be an object." );
		var dependencies = new Dictionary<string, byte[]>( StringComparer.Ordinal );
		var resolved = new Dictionary<string, string>( StringComparer.Ordinal );
		if ( document.Root.TryGetProperty( "images", out var sourceImages ) )
		{
			if ( sourceImages.ValueKind != JsonValueKind.Array || root["images"] is not JsonArray images )
				throw new GltfException( "Images must be an array." );
			for ( var i = 0; i < sourceImages.GetArrayLength(); i++ )
			{
				var image = sourceImages[i];
				if ( image.ValueKind != JsonValueKind.Object ) throw new GltfException( "Image must be an object." );
				if ( !image.TryGetProperty( "uri", out _ ) ) continue;
				var uri = GltfJson.String( image, "uri" );
				if ( uri.StartsWith( "data:", StringComparison.OrdinalIgnoreCase ) ) continue;
				if ( !resolved.TryGetValue( uri, out var path ) )
				{
					byte[] bytes;
					try { bytes = resolve?.Invoke( uri ) ?? throw new GltfException( $"Missing image dependency '{uri}'." ); }
					catch ( Exception e ) when ( e is not (GltfException or OutOfMemoryException or OperationCanceledException) )
					{
						throw new GltfException( $"Failed to resolve image dependency '{uri}': {e.Message}", e );
					}
					var extension = ImageFileExtension( bytes );
					path = "gltf-images/" + Convert.ToHexStringLower( SHA256.HashData( bytes ) ) + extension;
					if ( dependencies.TryGetValue( path, out var previous ) && !previous.AsSpan().SequenceEqual( bytes ) )
						throw new GltfException( "Image dependency hash collision." );
					dependencies[path] = bytes;
					resolved.Add( uri, path );
				}
				images[i]!.AsObject()["uri"] = path;
			}
		}
		if ( resolved.Count > 0 && HasOpaqueUriReference( document.Root, resolved.Keys.ToHashSet( StringComparer.Ordinal ) ) )
			throw new GltfException( "Cannot safely rewrite an image URI also referenced by opaque extension or extras data." );
		return new GltfArtifactBundle
		{
			Artifact = RewriteJsonChunk( compiled, JsonSerializer.SerializeToUtf8Bytes( root ) ),
			Dependencies = new ReadOnlyDictionary<string, byte[]>( dependencies )
		};
	}

	private static string ImageFileExtension( ReadOnlySpan<byte> data )
	{
		if ( data.StartsWith( new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 } ) ) return ".png";
		if ( data.Length >= 3 && data[0] == 0xff && data[1] == 0xd8 && data[2] == 0xff ) return ".jpg";
		if ( data.StartsWith( new byte[] { 0xab, 0x4b, 0x54, 0x58, 0x20, 0x32, 0x30, 0xbb, 13, 10, 26, 10 } ) ) return ".ktx2";
		return ".bin";
	}

	private static bool HasOpaqueUriReference( JsonElement value, HashSet<string> uris, bool opaque = false )
	{
		if ( value.ValueKind == JsonValueKind.String ) return opaque && uris.Contains( value.GetString() );
		if ( value.ValueKind == JsonValueKind.Array ) return value.EnumerateArray().Any( x => HasOpaqueUriReference( x, uris, opaque ) );
		if ( value.ValueKind != JsonValueKind.Object ) return false;
		foreach ( var property in value.EnumerateObject() )
			if ( HasOpaqueUriReference( property.Value, uris, opaque || property.Name is "extensions" or "extras" ) ) return true;
		return false;
	}

	private static byte[] RewriteJsonChunk( byte[] compiled, byte[] json )
	{
		var originalJsonLength = BinaryPrimitives.ReadUInt32LittleEndian( compiled.AsSpan( 12 ) );
		var tailOffset = checked(20 + (int)originalJsonLength);
		var jsonLength = (json.LongLength + 3) & ~3L;
		var total = 20L + jsonLength + compiled.Length - tailOffset;
		if ( total > Array.MaxLength ) throw new GltfException( "Packaged GLB exceeds supported length." );
		var result = new byte[(int)total];
		BinaryPrimitives.WriteUInt32LittleEndian( result, 0x46546c67 );
		BinaryPrimitives.WriteUInt32LittleEndian( result.AsSpan( 4 ), 2 );
		BinaryPrimitives.WriteUInt32LittleEndian( result.AsSpan( 8 ), (uint)total );
		BinaryPrimitives.WriteUInt32LittleEndian( result.AsSpan( 12 ), (uint)jsonLength );
		BinaryPrimitives.WriteUInt32LittleEndian( result.AsSpan( 16 ), 0x4e4f534a );
		result.AsSpan( 20, (int)jsonLength ).Fill( 0x20 );
		json.CopyTo( result, 20 );
		compiled.AsSpan( tailOffset ).CopyTo( result.AsSpan( 20 + (int)jsonLength ) );
		return result;
	}
	/// <summary>
	/// Resolves core external images to safe, URI-decoded relative paths. Embedded
	/// images require no sidecar. Extension-specific dependencies need their own handling.
	/// </summary>
	public static IReadOnlyDictionary<string, byte[]> CollectImages( JsonElement root, Func<string, byte[]> resolve )
	{
		var result = new Dictionary<string, byte[]>( StringComparer.Ordinal );
		if ( root.ValueKind != JsonValueKind.Object ) throw new GltfException( "glTF root must be an object." );
		if ( !root.TryGetProperty( "images", out var images ) ) return new ReadOnlyDictionary<string, byte[]>( result );
		if ( images.ValueKind != JsonValueKind.Array ) throw new GltfException( "Images must be an array." );
		foreach ( var image in images.EnumerateArray() )
		{
			if ( image.ValueKind != JsonValueKind.Object ) throw new GltfException( "Image must be an object." );
			if ( !image.TryGetProperty( "uri", out _ ) ) continue;
			var uri = GltfJson.String( image, "uri" );
			if ( uri.StartsWith( "data:", StringComparison.OrdinalIgnoreCase ) ) continue;
			var path = RelativePath( uri );
			byte[] data;
			try { data = resolve?.Invoke( uri ) ?? throw new GltfException( $"Missing image dependency '{uri}'." ); }
			catch ( Exception e ) when ( e is not (GltfException or OutOfMemoryException or OperationCanceledException) )
			{
				throw new GltfException( $"Failed to resolve image dependency '{uri}': {e.Message}", e );
			}
			if ( result.TryGetValue( path, out var previous ) )
			{
				if ( !previous.AsSpan().SequenceEqual( data ) ) throw new GltfException( $"Conflicting image dependencies map to '{path}'." );
			}
			else result.Add( path, data );
		}
		return new ReadOnlyDictionary<string, byte[]>( result );
	}

	/// <summary>Validates and decodes an image URI to a relative, portable package path.</summary>
	public static string RelativePath( string uri )
	{
		if ( string.IsNullOrEmpty( uri ) || uri.Contains( '?' ) || uri.Contains( '#' ) )
			throw new GltfException( "Image dependency URI must be a relative path without query or fragment." );
		for ( var i = 0; i < uri.Length; i++ )
		{
			if ( uri[i] != '%' ) continue;
			if ( i + 2 >= uri.Length || !Uri.IsHexDigit( uri[i + 1] ) || !Uri.IsHexDigit( uri[i + 2] ) )
				throw new GltfException( "Image dependency URI contains an invalid percent escape." );
			i += 2;
		}
		var path = Uri.UnescapeDataString( uri );
		ValidateRelativePath( path );
		return path;
	}

	/// <summary>Writes a compiled artifact and its pre-resolved image sidecars.</summary>
	public static void WritePackage( string artifactPath, byte[] artifact, IReadOnlyDictionary<string, byte[]> dependencies )
	{
		if ( artifact is null || dependencies is null ) throw new GltfException( "Artifact and dependencies are required." );
		var fullArtifact = Path.GetFullPath( artifactPath );
		var directory = Path.GetDirectoryName( fullArtifact ) ?? throw new GltfException( "Artifact has no containing directory." );
		var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
		var paths = new List<(string Path, byte[] Bytes)> { (fullArtifact, artifact) };
		foreach ( var dependency in dependencies )
		{
			ValidateRelativePath( dependency.Key );
			if ( dependency.Value is null ) throw new GltfException( "Image dependency has no data." );
			var target = SafePath( directory, dependency.Key );
			foreach ( var existing in paths )
			{
				if ( target.Equals( existing.Path, comparison ) || target.StartsWith( existing.Path + Path.DirectorySeparatorChar, comparison ) ||
					existing.Path.StartsWith( target + Path.DirectorySeparatorChar, comparison ) )
					throw new GltfException( $"Image dependency path conflicts with another package file: '{dependency.Key}'." );
			}
			paths.Add( (target, dependency.Value) );
		}
		foreach ( var item in paths ) RequireNoLinks( item.Path );
		foreach ( var item in paths.Skip( 1 ) )
		{
			if ( Directory.Exists( item.Path ) ) throw new GltfException( "Image dependency path is an existing directory." );
			if ( File.Exists( item.Path ) && !File.ReadAllBytes( item.Path ).AsSpan().SequenceEqual( item.Bytes ) )
				throw new GltfException( "Existing immutable image dependency has different contents." );
		}
		foreach ( var item in paths.Skip( 1 ) )
		{
			var parent = Path.GetDirectoryName( item.Path )!;
			Directory.CreateDirectory( parent );
			RequireNoLinks( item.Path );
			if ( File.Exists( item.Path ) )
			{
				if ( !File.ReadAllBytes( item.Path ).AsSpan().SequenceEqual( item.Bytes ) )
					throw new GltfException( "Existing immutable image dependency has different contents." );
				continue;
			}
			var temporary = TemporaryPath( item.Path );
			try
			{
				WriteNewFile( temporary, item.Bytes );
				try { File.Move( temporary, item.Path, overwrite: false ); }
				catch ( IOException ) when ( File.Exists( item.Path ) )
				{
					RequireNoLinks( item.Path );
					if ( !File.ReadAllBytes( item.Path ).AsSpan().SequenceEqual( item.Bytes ) )
						throw new GltfException( "Existing immutable image dependency has different contents." );
				}
			}
			finally { if ( File.Exists( temporary ) ) File.Delete( temporary ); }
		}
		Directory.CreateDirectory( directory );
		RequireNoLinks( fullArtifact );
		var temporaryArtifact = TemporaryPath( fullArtifact );
		try
		{
			WriteNewFile( temporaryArtifact, artifact );
			RequireNoLinks( fullArtifact );
			File.Move( temporaryArtifact, fullArtifact, overwrite: true );
		}
		finally { if ( File.Exists( temporaryArtifact ) ) File.Delete( temporaryArtifact ); }
	}

	private static string TemporaryPath( string target ) => target + "." + Guid.NewGuid().ToString( "N" ) + ".tmp";

	private static void WriteNewFile( string path, byte[] bytes )
	{
		using var stream = new FileStream( path, FileMode.CreateNew, FileAccess.Write, FileShare.None );
		stream.Write( bytes );
		stream.Flush( flushToDisk: true );
	}

	/// <summary>Resolves a packaged dependency without consulting the source asset directory.</summary>
	public static byte[] ResolvePackaged( string artifactPath, string uri )
	{
		var directory = Path.GetDirectoryName( Path.GetFullPath( artifactPath ) ) ?? throw new GltfException( "Artifact has no containing directory." );
		var path = SafePath( directory, RelativePath( uri ) );
		RequireNoLinks( path );
		try { return File.ReadAllBytes( path ); }
		catch ( Exception e ) when ( e is IOException or UnauthorizedAccessException )
		{
			throw new GltfException( $"Packaged image dependency '{uri}' cannot be read.", e );
		}
	}

	private static void ValidateRelativePath( string path )
	{
		if ( path.StartsWith( '/' ) || path.IndexOfAny( ['\\', ':', '?', '#', '*', '"', '<', '>', '|'] ) >= 0 ||
			path.Any( char.IsControl ) )
			throw new GltfException( "Image dependency URI is not a portable relative path." );
		foreach ( var segment in path.Split( '/' ) )
		{
			if ( segment is "" or "." or ".." || segment.EndsWith( ' ' ) || segment.EndsWith( '.' ) )
				throw new GltfException( "Image dependency URI contains an unsafe path component." );
		}
	}

	private static string SafePath( string directory, string relative )
	{
		var root = Path.TrimEndingDirectorySeparator( Path.GetFullPath( directory ) );
		var path = Path.GetFullPath( Path.Combine( root, relative.Replace( '/', Path.DirectorySeparatorChar ) ) );
		var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
		var prefix = Path.EndsInDirectorySeparator( root ) ? root : root + Path.DirectorySeparatorChar;
		if ( !path.StartsWith( prefix, comparison ) )
			throw new GltfException( "Image dependency path escapes the package directory." );
		return path;
	}

	private static void RequireNoLinks( string path )
	{
		var full = Path.GetFullPath( path );
		var root = Path.GetPathRoot( full )!;
		var current = root;
		foreach ( var component in full[root.Length..].Split( Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries ) )
		{
			current = Path.Combine( current, component );
			try
			{
				if ( (File.GetAttributes( current ) & FileAttributes.ReparsePoint) != 0 )
					throw new GltfException( "Symbolic links are not allowed in package paths." );
			}
			catch ( FileNotFoundException ) { }
			catch ( DirectoryNotFoundException ) { }
		}
	}
}
