using System.IO;
using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Editor.Gltf;

/// <summary>
/// Disk proofs for the corpus run: input hashes, engine binaries, stock resource RERL graph,
/// and the Python-compatible recipe fingerprint. Same rules as the former Run script.
/// </summary>
internal static class GltfCorpusProof
{
	public static readonly string[] StockCompiled =
	{
		"game/core/shaders/gltf_deform_cs.shader_c", "game/core/shaders/complex.shader_c",
		"game/core/shaders/vertex_color.shader_c", "game/core/shaders/depth_only.shader_c",
		"game/core/shaders/gltf_stock_sampler.shader_c",
		"game/core/shaders/tools_shading_complexity.shader_c", "game/core/materials/default/white.vmat_c",
		"game/core/materials/default/default_ao_tga_7449568e.generated.vtex_c",
		"game/core/materials/default/default_bentnormal_tga_8334dba4.generated.vtex_c",
		"game/core/materials/default/white_vmat_g_tcolor_a0ce44b8.generated.vtex_c",
		"game/core/materials/default/white_vmat_g_tnormal_7a668049.generated.vtex_c",
		"game/core/materials/default/default_color_tga_29ae19a3.generated.vtex_c",
	};

	public static readonly string[] Binaries = { "Sandbox.Engine.dll", "Sandbox.Gltf.dll", "Sandbox.SceneRenderer.dll" };

	public static string Hash( ReadOnlySpan<byte> bytes ) => Convert.ToHexStringLower( System.Security.Cryptography.SHA256.HashData( bytes ) );
	public static string HashFile( string path ) { using var stream = File.OpenRead( path ); return Convert.ToHexStringLower( System.Security.Cryptography.SHA256.HashData( stream ) ); }

	static void Require( bool value, string message ) { if ( !value ) throw new InvalidOperationException( message ); }

	/// <summary>
	/// Hand-audited stock resources. Delegated modes live inside shader DATA, not the standard RERL table,
	/// so a shader rebuild needs a fresh delegation audit and a new hash here (moved unchanged from the former Run script).
	/// </summary>
	public sealed class Config
	{
		public string[] StockSource = { "game/core/shaders/gltf_deform_cs.shader", "game/core/shaders/complex.shader", "game/core/shaders/vertex_color.shader", "game/core/shaders/depth_only.shader", "game/core/shaders/gltf_stock_sampler.shader", "game/core/shaders/tools_shading_complexity.shader", "game/core/materials/default/white.vmat" };
		public Dictionary<string, string> AuditedShaders = new()
		{
			["game/core/shaders/gltf_deform_cs.shader_c"] = "4e5f9c9da78a82fcdef7ae72d0280a454697a0bf1eea6e46a83ca5a94c259c65",
			["game/core/shaders/complex.shader_c"] = "b30471c714e1828378b357d0e5c8ddb697b5a5acacee3b3da69973b21b172d9a",
			["game/core/shaders/vertex_color.shader_c"] = "bee584e6566632ceedebd5104df859ab5f22da5c4d4e371e7082f1b7cc37808c",
			["game/core/shaders/gltf_stock_sampler.shader_c"] = "6d42bde0cc6424c257b0b0cab8907d1a6d769897c40266f58b41603f458f269f",
			["game/core/shaders/depth_only.shader_c"] = "1a7704ac650c4b712f45ccb08e60750c29d18d4b048e4bb0bcec56c1619ed338",
			["game/core/shaders/tools_shading_complexity.shader_c"] = "491aca41131266c08786f59a62a410aa62ec6dc4bc929cd9efcf276bdd686936",
		};
		public Dictionary<string, string[]> Delegations = new()
		{
			["game/core/shaders/vertex_color.shader_c"] = new[] { "game/core/shaders/depth_only.shader_c", "game/core/shaders/tools_shading_complexity.shader_c" },
			["game/core/shaders/gltf_stock_sampler.shader_c"] = new[] { "game/core/shaders/depth_only.shader_c", "game/core/shaders/tools_shading_complexity.shader_c" },
		};
	}

	/// <summary>
	/// Python json.dumps(value, sort_keys=True, separators=(',', ':'), ensure_ascii=False).
	/// Numbers keep their raw text: the recipe file is written by Python, so raw text is Python repr.
	/// </summary>
	public static string Canonical( JsonElement value )
	{
		var builder = new StringBuilder();
		Write( builder, value );
		return builder.ToString();
	}

	static void Write( StringBuilder output, JsonElement value )
	{
		switch ( value.ValueKind )
		{
			case JsonValueKind.Object:
				output.Append( '{' );
				var first = true;
				foreach ( var property in value.EnumerateObject().OrderBy( p => p.Name, StringComparer.Ordinal ) )
				{
					if ( !first ) output.Append( ',' );
					first = false;
					WriteString( output, property.Name ); output.Append( ':' ); Write( output, property.Value );
				}
				output.Append( '}' );
				break;
			case JsonValueKind.Array:
				output.Append( '[' );
				var index = 0;
				foreach ( var item in value.EnumerateArray() ) { if ( index++ > 0 ) output.Append( ',' ); Write( output, item ); }
				output.Append( ']' );
				break;
			case JsonValueKind.String: WriteString( output, value.GetString() ); break;
			case JsonValueKind.Number: output.Append( value.GetRawText() ); break;
			case JsonValueKind.True: output.Append( "true" ); break;
			case JsonValueKind.False: output.Append( "false" ); break;
			default: output.Append( "null" ); break;
		}
	}

	static void WriteString( StringBuilder output, string text )
	{
		output.Append( '"' );
		foreach ( var c in text )
		{
			switch ( c )
			{
				case '"': output.Append( "\\\"" ); break;
				case '\\': output.Append( "\\\\" ); break;
				case '\n': output.Append( "\\n" ); break;
				case '\r': output.Append( "\\r" ); break;
				case '\t': output.Append( "\\t" ); break;
				case '\b': output.Append( "\\b" ); break;
				case '\f': output.Append( "\\f" ); break;
				default:
					if ( c < 0x20 ) output.Append( "\\u" ).Append( ((int)c).ToString( "x4" ) );
					else output.Append( c );
					break;
			}
		}
		output.Append( '"' );
	}

	public static string RecipeFingerprint( JsonElement recipe ) => Hash( Encoding.UTF8.GetBytes( Canonical( recipe ) ) );

	public static JsonObject VerifyInputs( string corpusRoot, JsonElement files, string path, IEnumerable<string> dependencies )
	{
		var deps = new JsonObject();
		string main = null;
		foreach ( var relative in new[] { path }.Concat( dependencies ) )
		{
			var full = Path.GetFullPath( Path.Combine( corpusRoot, relative ) );
			Require( full.StartsWith( corpusRoot.TrimEnd( '/' ) + "/", StringComparison.Ordinal ), "Input dependency escaped the corpus" );
			var info = new FileInfo( full ); var resolved = info.ResolveLinkTarget( true ); if ( resolved is not null ) info = new FileInfo( resolved.FullName );
			var expected = files.GetProperty( relative );
			Require( info.Exists && info.Length == expected.GetProperty( "bytes" ).GetInt64(), "Pinned input length changed: " + relative );
			var actual = HashFile( info.FullName );
			Require( actual == expected.GetProperty( "sha256" ).GetString(), "Pinned input hash changed: " + relative );
			if ( relative == path && main is null ) main = actual; else deps[relative] = actual;
		}
		return new JsonObject { ["path"] = path, ["sha256"] = main, ["dependenciesVerified"] = true, ["dependencies"] = deps };
	}

	/// <summary>Bounded Source 2 ResourceFileHeader/RERL relative pointers.</summary>
	public static List<string> ResourceReferences( byte[] data )
	{
		Require( data.Length >= 16, "Truncated resource header" );
		var size = BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( 0 ) );
		var version = BinaryPrimitives.ReadUInt16LittleEndian( data.AsSpan( 4 ) );
		var offset = BinaryPrimitives.ReadInt32LittleEndian( data.AsSpan( 8 ) );
		var count = BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( 12 ) );
		long table = 8 + (long)offset;
		Require( version == 12 && 16 <= size && size <= data.Length && count <= 256 && 16 <= table && table <= size && table + count * 12 <= size, "Unsupported resource block table" );
		var result = new SortedSet<string>( StringComparer.Ordinal ); var seen = false;
		for ( var index = 0; index < count; index++ )
		{
			var entry = table + index * 12;
			var tag = Encoding.ASCII.GetString( data, (int)entry, 4 );
			var blockOffset = BinaryPrimitives.ReadInt32LittleEndian( data.AsSpan( (int)entry + 4 ) );
			var length = BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( (int)entry + 8 ) );
			long start = entry + 4 + blockOffset, end = start + length;
			Require( table + count * 12 <= start && start <= end && end <= size, "Invalid resource block bounds" );
			if ( tag != "RERL" ) continue;
			Require( !seen && length >= 8, "Invalid duplicate or truncated RERL" );
			seen = true;
			var refOffset = BinaryPrimitives.ReadInt32LittleEndian( data.AsSpan( (int)start ) );
			var refCount = BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( (int)start + 4 ) );
			long refs = start + refOffset;
			Require( refCount <= 256 && start + 8 <= refs && refs + refCount * 16 <= end, "Invalid RERL entries" );
			for ( var refIndex = 0; refIndex < refCount; refIndex++ )
			{
				long pointer = refs + refIndex * 16 + 8;
				long text = pointer + BinaryPrimitives.ReadInt32LittleEndian( data.AsSpan( (int)pointer ) );
				Require( refs + refCount * 16 <= text && text < end, "Invalid RERL name pointer" );
				var limit = (int)Math.Min( end, text + 4097 );
				var stop = Array.IndexOf( data, (byte)0, (int)text, limit - (int)text );
				Require( stop >= 0, "Unterminated RERL name" );
				var name = Encoding.UTF8.GetString( data, (int)text, stop - (int)text );
				Require( name.Length > 0 && !name.StartsWith( '/' ) && !name.Contains( '\\' ) && name.Split( '/' ).All( p => p is not ("" or "." or "..") ), "Unsafe RERL resource path" );
				result.Add( "game/core/" + name + (name.EndsWith( "_c" ) ? "" : "_c") );
			}
		}
		return result.ToList();
	}

	/// <summary>Stock resource proof version 2: same output shape as the former Run script.</summary>
	public static JsonObject StockResourceProof( string root, Config config )
	{
		var resources = new SortedDictionary<string, JsonObject>( StringComparer.Ordinal );
		var graph = new SortedDictionary<string, List<string>>( StringComparer.Ordinal );
		var errors = new List<string>();
		var pending = new Stack<string>( StockCompiled.Concat( config.StockSource ) );
		var core = Path.Combine( root, "game/core" );
		while ( pending.Count > 0 )
		{
			var name = pending.Pop();
			if ( resources.ContainsKey( name ) ) continue;
			if ( resources.Count >= 256 ) { errors.Add( "Stock resource dependency limit exceeded" ); break; }
			var path = Path.Combine( root, name );
			var linked = false;
			for ( var current = path; current is not null && current.Length >= core.Length; current = Path.GetDirectoryName( current ) )
				if ( new FileInfo( current ).LinkTarget is not null || new DirectoryInfo( current ).LinkTarget is not null ) { linked = true; break; }
			var readable = File.Exists( path ) && !linked;
			var value = new JsonObject { ["present"] = File.Exists( path ) || Directory.Exists( path ) || linked, ["sha256"] = null, ["origin"] = readable ? "loose-core" : linked ? "unproven-link" : "unavailable" };
			resources[name] = value;
			if ( config.StockSource.Contains( name ) )
			{
				if ( readable ) value["sha256"] = HashFile( path );
				continue;
			}
			graph[name] = new List<string>();
			try
			{
				Require( readable, "Missing or unproven compiled resource" );
				Require( new FileInfo( path ).Length <= 64 * 1024 * 1024, "Compiled resource exceeds inspection limit" );
				var data = File.ReadAllBytes( path );
				var hash = Hash( data );
				value["sha256"] = hash;
				var children = new SortedSet<string>( ResourceReferences( data ), StringComparer.Ordinal );
				if ( config.Delegations.TryGetValue( name, out var delegated ) ) children.UnionWith( delegated );
				graph[name] = children.ToList();
				foreach ( var child in graph[name] ) pending.Push( child );
				if ( name.EndsWith( ".shader_c" ) )
					Require( config.AuditedShaders.TryGetValue( name, out var audited ) && audited == hash, "Shader delegation bytes require a new audit" );
			}
			catch ( Exception error ) when ( error is IOException or InvalidOperationException or ArgumentException or DecoderFallbackException )
			{
				errors.Add( name + ": " + error.Message );
			}
		}
		var resourceNode = new JsonObject(); foreach ( var pair in resources ) resourceNode[pair.Key] = pair.Value;
		var graphNode = new JsonObject(); foreach ( var pair in graph ) graphNode[pair.Key] = new JsonArray( pair.Value.Select( v => (JsonNode)v ).ToArray() );
		errors.Sort( StringComparer.Ordinal );
		return new JsonObject
		{
			["stockResourceProofVersion"] = 2,
			["stockResources"] = resourceNode,
			["stockDependencies"] = graphNode,
			["stockDependencyErrors"] = new JsonArray( errors.Select( e => (JsonNode)e ).ToArray() ),
		};
	}

	/// <summary>Hash of this runner's own sources: the C# driver files and the watchdog script.</summary>
	public static string SourceHash( IEnumerable<string> files )
	{
		using var sha = System.Security.Cryptography.IncrementalHash.CreateHash( System.Security.Cryptography.HashAlgorithmName.SHA256 );
		foreach ( var file in files.OrderBy( f => f, StringComparer.Ordinal ) )
		{
			sha.AppendData( Encoding.UTF8.GetBytes( Path.GetFileName( file ) + "\n" ) );
			sha.AppendData( File.ReadAllBytes( file ) );
		}
		return Convert.ToHexStringLower( sha.GetHashAndReset() );
	}

	public static JsonObject DiskProducer( string root, Config config, IEnumerable<string> driverFiles, string watchdog )
	{
		var proof = StockResourceProof( root, config );
		var binaries = new JsonObject();
		foreach ( var name in Binaries ) binaries[name] = HashFile( Path.Combine( root, "game/bin/managed", name ) );
		var deform = proof["stockResources"]?["game/core/shaders/gltf_deform_cs.shader_c"]?["sha256"]?.GetValue<string>();
		var producer = new JsonObject
		{
			["binaries"] = binaries,
			["driverSha256"] = SourceHash( driverFiles ),
			["runnerSha256"] = HashFile( watchdog ),
			["deformShaderSha256"] = deform,
		};
		foreach ( var pair in proof.ToList() ) { proof.Remove( pair.Key ); producer[pair.Key] = pair.Value; }
		return producer;
	}

	/// <summary>Cheap drift signal between full proofs: size and write time of every guarded file.</summary>
	public static string Stamp( IEnumerable<string> paths )
	{
		var builder = new StringBuilder();
		foreach ( var path in paths.OrderBy( p => p, StringComparer.Ordinal ) )
		{
			var info = new FileInfo( path );
			builder.Append( path ).Append( '|' ).Append( info.Exists ? info.Length : -1 ).Append( '|' ).Append( info.Exists ? info.LastWriteTimeUtc.Ticks : 0 ).Append( '\n' );
		}
		return Hash( Encoding.UTF8.GetBytes( builder.ToString() ) );
	}
}
