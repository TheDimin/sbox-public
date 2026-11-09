using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sandbox.Gltf.DirectGpu.UnlitTestValidation;

using Vector3 = System.Numerics.Vector3;
using Vector4 = System.Numerics.Vector4;

/// <summary>
/// One-model oracle: literal geometry extracted independently with Python struct
/// from the pinned little-endian .bin, plus hand-audited transforms/materials.
/// Attribution: Ed Mackey, Analytical Graphics, Inc. (2019), CC-BY-4.0.
/// This data is only an expected result; never feed it into the actual renderer.
/// </summary>
public static partial class UnlitTestReference
{
	public const string CorpusRevision = "90d7ede14c7e280af263824604b427a1ca02cb66";
	public const string Extension = "KHR_materials_unlit";
	public static readonly IReadOnlySet<string> Selected = new HashSet<string>( StringComparer.Ordinal ) { Extension };
	public static readonly Vector4[] BaseColors = { new( 1, .217637640824031f, 0, 1 ), new( 0, .217637640824031f, 1, 1 ) };
	private static readonly IReadOnlyDictionary<string, string> Hashes = new Dictionary<string, string>( StringComparer.Ordinal )
	{
		["glTF/UnlitTest.gltf"] = "b611ca0892cb1a0118e92b72ff52b3afed3e4903c15378daa72c4c252a0334e1",
		["glTF/UnlitTest.bin"] = "a6a81ec57560242f29ee2c2a94f452a923b5548bf256b6529b0c07dea3f5c907",
		["glTF-Binary/UnlitTest.glb"] = "e07b68c6fd9fbf73d372c610a681b89d6b013bdd1a506e46a411df82210a2481"
	};

	public sealed class Form
	{
		public string Name { get; init; }
		public bool Compiled { get; init; }
		public byte[] Bytes { get; init; }
		public Func<string, byte[]> Resolve { get; init; }
		public GltfDocument Read() => GltfReader.Read( Bytes, Resolve, Selected );
	}

	public static Form[] OpenForms( string checkoutRoot )
	{
		ValidateCorpusManifest( checkoutRoot );
		var binary = ReadPinned( checkoutRoot, "glTF/UnlitTest.bin" );
		byte[] Resolve( string uri ) => uri == "UnlitTest.bin" ? binary : throw new InvalidOperationException( "Unexpected UnlitTest dependency: " + uri );
		var forms = new List<Form>();
		foreach ( var source in new[] { ("glTF", "glTF/UnlitTest.gltf"), ("glTF-Binary", "glTF-Binary/UnlitTest.glb") } )
		{
			var bytes = ReadPinned( checkoutRoot, source.Item2 );
			forms.Add( new() { Name = source.Item1 + "/source", Bytes = bytes, Resolve = Resolve } );
			forms.Add( new() { Name = source.Item1 + "/compiled_glb", Compiled = true, Bytes = GltfCompiler.Compile( bytes, Resolve, Selected ), Resolve = Resolve } );
		}
		return forms.ToArray();
	}

	public static JsonObject SourceJson( string checkoutRoot ) => JsonNode.Parse( ReadPinned( checkoutRoot, "glTF/UnlitTest.gltf" ) )!.AsObject();
	public static byte[] ResolvePinnedBinary( string checkoutRoot, string uri ) => uri == "UnlitTest.bin" ? ReadPinned( checkoutRoot, "glTF/UnlitTest.bin" ) : throw new InvalidOperationException( "Unexpected model dependency: " + uri );

	private static void ValidateCorpusManifest( string checkoutRoot )
	{
		using var manifest = JsonDocument.Parse( File.ReadAllBytes( Path.Combine( Path.GetFullPath( checkoutRoot ), "research/gltf-validation/corpus-case-manifest.json" ) ) );
		UnlitTestCoreTests.Require( manifest.RootElement.GetProperty( "corpus" ).GetProperty( "commit" ).GetString() == CorpusRevision, "Pinned corpus manifest revision changed." );
		var metadata = File.ReadAllText( Path.Combine( ModelRoot( checkoutRoot ), "metadata.json" ) );
		UnlitTestCoreTests.Require( metadata.Contains( "CC-BY-4.0", StringComparison.Ordinal ) && metadata.Contains( "Ed Mackey", StringComparison.Ordinal ), "Upstream model attribution metadata is missing." );
	}
	private static string ModelRoot( string checkoutRoot ) => Path.Combine( Path.GetFullPath( checkoutRoot ), "Temp/gltf-reference/glTF-Sample-Assets/Models/UnlitTest" );
	private static byte[] ReadPinned( string checkoutRoot, string file )
	{
		var path = Path.Combine( ModelRoot( checkoutRoot ), file ); var info = new FileInfo( path );
		UnlitTestCoreTests.Require( info.Exists && info.Length is > 0 and < 32768, "Missing or oversized pinned model input: " + file );
		var bytes = File.ReadAllBytes( path );
		UnlitTestCoreTests.Require( Convert.ToHexStringLower( SHA256.HashData( bytes ) ) == Hashes[file], "Pinned source hash changed: " + file );
		return bytes;
	}

	public static Vector3 WorldPosition( int node, int vertex )
	{
		if ( (uint)node >= 2 || (uint)vertex >= 96 ) throw new ArgumentOutOfRangeException();
		return Positions[vertex] + new Vector3( node == 0 ? -1.2f : 1.2f, 0, 0 );
	}
	public static Vector3 EnginePosition( int node, int vertex )
	{
		var p = WorldPosition( node, vertex ); return new( p.Z / .0254f, p.X / .0254f, p.Y / .0254f );
	}
	public static Vector3 EngineNormal( int vertex )
	{
		if ( (uint)vertex >= 96 ) throw new ArgumentOutOfRangeException( nameof( vertex ) );
		var n = Normals[vertex]; return Vector3.Normalize( new( n.Z, n.X, n.Y ) );
	}
}
