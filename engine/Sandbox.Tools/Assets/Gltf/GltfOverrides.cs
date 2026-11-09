using System;
using System.IO;
using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Editor;

/// <summary>
/// Editor changes to a glTF, kept apart from the authored source like ModelDoc keeps
/// its changes apart from the FBX. Each changed element stores a JSON merge patch
/// (RFC 7396). Shape: <c>{ "materials": { "0": { patch } }, "extensions/KHR_lights_punctual/lights": { ... } }</c>.
/// The source file stays unchanged. The import applies the patches before it compiles.
/// </summary>
public static class GltfOverrides
{
	/// <summary>Asset metadata key that holds the overrides.</summary>
	public const string MetaKey = "gltfOverrides";

	const uint GlbMagic = 0x46546C67;
	const uint JsonChunk = 0x4E4F534A;

	/// <summary>Read the overrides of an asset, or null if it has none.</summary>
	public static JsonObject Read( Asset asset )
	{
		var element = asset?.MetaData?.GetElement( MetaKey );
		if ( element is not { ValueKind: JsonValueKind.Object } ) return null;
		var node = JsonNode.Parse( element.Value.GetRawText() )?.AsObject();
		return node is { Count: > 0 } ? node : null;
	}

	/// <summary>Write the overrides of an asset. Empty overrides remove the key.</summary>
	public static void Write( Asset asset, JsonObject overrides )
	{
		ArgumentNullException.ThrowIfNull( asset );
		asset.MetaData.Set( MetaKey, overrides is { Count: > 0 } ? overrides : null );
	}

	/// <summary>
	/// Apply overrides to a .gltf or .glb source. Returns the source unchanged when there are no overrides.
	/// </summary>
	public static byte[] ApplyToSource( byte[] source, JsonObject overrides, List<string> warnings = null )
	{
		ArgumentNullException.ThrowIfNull( source );
		if ( overrides is not { Count: > 0 } ) return source;
		var root = ReadJson( source );
		ApplyToRoot( root, overrides, warnings );
		return WriteJson( source, root );
	}

	/// <summary>Apply overrides to a parsed glTF root in place, and keep extensionsUsed in sync.</summary>
	internal static void ApplyToRoot( JsonObject root, JsonObject overrides, List<string> warnings = null )
	{
		var before = ExtensionNamesPresent( root );
		foreach ( var (collectionPath, entries) in overrides )
		{
			if ( entries is not JsonObject elements ) continue;
			var collection = Collection( root, collectionPath );
			foreach ( var (key, patch) in elements )
			{
				if ( !int.TryParse( key, out var index ) || collection is null || index < 0 || index >= collection.Count || collection[index] is not JsonObject element )
				{
					warnings?.Add( $"glTF override '{collectionPath}/{key}' has no matching element in the source and was skipped." );
					continue;
				}
				ApplyPatch( element, patch );
			}
		}
		SyncExtensionsUsed( root, before );
	}

	/// <summary>Create the merge patch that changes <paramref name="before"/> into <paramref name="after"/>, or null if they are equal.</summary>
	internal static JsonObject CreatePatch( JsonObject before, JsonObject after )
	{
		var patch = new JsonObject();
		foreach ( var (key, _) in before )
			if ( !after.ContainsKey( key ) ) patch[key] = null;
		foreach ( var (key, value) in after )
		{
			var existed = before.TryGetPropertyValue( key, out var old );
			if ( existed && JsonNode.DeepEquals( old, value ) ) continue;
			if ( old is JsonObject oldObject && value is JsonObject newObject ) patch[key] = CreatePatch( oldObject, newObject );
			else patch[key] = value?.DeepClone();
		}
		return patch.Count > 0 ? patch : null;
	}

	/// <summary>Merge a patch into a target object in place (RFC 7396).</summary>
	internal static void ApplyPatch( JsonObject target, JsonNode patch )
	{
		if ( patch is not JsonObject changes ) return;
		foreach ( var (key, value) in changes )
		{
			if ( value is null ) { target.Remove( key ); continue; }
			if ( value is JsonObject childPatch && target[key] is JsonObject child ) { ApplyPatch( child, childPatch ); continue; }
			if ( value is JsonObject newObject )
			{
				var created = new JsonObject();
				ApplyPatch( created, newObject );
				target[key] = created;
				continue;
			}
			target[key] = value.DeepClone();
		}
	}

	/// <summary>Find a collection by its slash path, such as "materials" or "extensions/KHR_lights_punctual/lights".</summary>
	internal static JsonArray Collection( JsonObject root, string path )
	{
		JsonNode node = root;
		foreach ( var part in path.Split( '/' ) )
			node = (node as JsonObject)?[part];
		return node as JsonArray;
	}

	internal static JsonObject ReadJson( byte[] source )
	{
		var json = IsGlb( source ) ? GlbJson( source ) : (ReadOnlySpan<byte>)source;
		if ( json.StartsWith( Encoding.UTF8.Preamble ) ) json = json[3..];
		return JsonNode.Parse( json )?.AsObject() ?? throw new InvalidDataException( "glTF root must be an object." );
	}

	static byte[] WriteJson( byte[] source, JsonObject root )
	{
		var json = JsonSerializer.SerializeToUtf8Bytes( root );
		if ( !IsGlb( source ) ) return json;

		// GLB: replace chunk 0 (JSON) and keep every later chunk byte for byte.
		var oldJsonLength = (int)BinaryPrimitives.ReadUInt32LittleEndian( source.AsSpan( 12 ) );
		var rest = source.AsSpan( 20 + oldJsonLength );
		var padded = (json.Length + 3) & ~3;
		var output = new byte[20 + padded + rest.Length];
		BinaryPrimitives.WriteUInt32LittleEndian( output, GlbMagic );
		BinaryPrimitives.WriteUInt32LittleEndian( output.AsSpan( 4 ), 2 );
		BinaryPrimitives.WriteUInt32LittleEndian( output.AsSpan( 8 ), (uint)output.Length );
		BinaryPrimitives.WriteUInt32LittleEndian( output.AsSpan( 12 ), (uint)padded );
		BinaryPrimitives.WriteUInt32LittleEndian( output.AsSpan( 16 ), JsonChunk );
		json.CopyTo( output, 20 );
		output.AsSpan( 20 + json.Length, padded - json.Length ).Fill( (byte)' ' );
		rest.CopyTo( output.AsSpan( 20 + padded ) );
		return output;
	}

	/// <summary>The payload of the BIN chunk of a GLB, or null for a .gltf or a GLB without one.</summary>
	internal static byte[] ReadGlbBinChunk( byte[] source )
	{
		if ( !IsGlb( source ) ) return null;
		var position = 12L;
		while ( position + 8 <= source.Length )
		{
			var length = BinaryPrimitives.ReadUInt32LittleEndian( source.AsSpan( (int)position ) );
			var type = BinaryPrimitives.ReadUInt32LittleEndian( source.AsSpan( (int)position + 4 ) );
			if ( position + 8 + length > source.Length ) return null;
			if ( type == 0x004E4942 ) return source.AsSpan( (int)position + 8, (int)length ).ToArray();
			position += 8 + length;
		}
		return null;
	}

	static bool IsGlb( byte[] source ) => source.Length >= 20 && BinaryPrimitives.ReadUInt32LittleEndian( source ) == GlbMagic;

	static ReadOnlySpan<byte> GlbJson( byte[] source )
	{
		var length = (int)BinaryPrimitives.ReadUInt32LittleEndian( source.AsSpan( 12 ) );
		if ( BinaryPrimitives.ReadUInt32LittleEndian( source.AsSpan( 16 ) ) != JsonChunk || 20L + length > source.Length )
			throw new InvalidDataException( "GLB does not start with a valid JSON chunk." );
		return source.AsSpan( 20, length );
	}

	/// <summary>
	/// Add extension names that overrides introduced, and remove names whose data overrides removed.
	/// Names with no "extensions" data (such as KHR_mesh_quantization) are never removed.
	/// </summary>
	static void SyncExtensionsUsed( JsonObject root, HashSet<string> before )
	{
		var after = ExtensionNamesPresent( root );
		var removed = before.Where( x => !after.Contains( x ) ).ToHashSet( StringComparer.Ordinal );
		var used = Names( root, "extensionsUsed" ).Where( x => !removed.Contains( x ) ).ToList();
		used.AddRange( after.Where( x => !used.Contains( x ) ).Order( StringComparer.Ordinal ) );
		var required = Names( root, "extensionsRequired" ).Where( x => !removed.Contains( x ) ).ToList();
		SetNames( root, "extensionsUsed", used );
		SetNames( root, "extensionsRequired", required );
	}

	static List<string> Names( JsonObject root, string key ) =>
		root[key] is JsonArray array ? array.Select( x => x?.GetValue<string>() ).Where( x => x is not null ).ToList() : new();

	static void SetNames( JsonObject root, string key, List<string> names )
	{
		if ( names.Count == 0 ) { root.Remove( key ); return; }
		root[key] = new JsonArray( names.Select( x => (JsonNode)JsonValue.Create( x ) ).ToArray() );
	}

	static HashSet<string> ExtensionNamesPresent( JsonNode node, HashSet<string> names = null )
	{
		names ??= new HashSet<string>( StringComparer.Ordinal );
		switch ( node )
		{
			case JsonObject obj:
				foreach ( var (key, value) in obj )
				{
					if ( key == "extensions" && value is JsonObject extensions )
						foreach ( var (name, _) in extensions ) names.Add( name );
					if ( key != "extras" ) ExtensionNamesPresent( value, names );
				}
				break;
			case JsonArray array:
				foreach ( var item in array ) ExtensionNamesPresent( item, names );
				break;
		}
		return names;
	}
}
