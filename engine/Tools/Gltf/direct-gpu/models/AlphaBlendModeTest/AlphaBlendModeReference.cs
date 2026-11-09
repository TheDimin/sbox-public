using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace Sandbox.Gltf.DirectGpu.AlphaBlendModeValidation;

/// <summary>Pinned independent source inventory; Ed Mackey / Analytical Graphics, CC-BY-4.0.</summary>
public static class AlphaBlendModeReference
{
	public const string Revision = "90d7ede14c7e280af263824604b427a1ca02cb66";
	public static readonly string[] Modes = ["OPAQUE", "BLEND", "MASK", "MASK", "MASK", "OPAQUE"];
	public static readonly float[] Cutoffs = [.5f, .5f, .25f, .75f, .5f, .5f];
	public static readonly int[] MeshMaterials = [2, 3, 0, 1, 5, 5, 5, 1, 4];
	public static readonly int[] PanelMeshes = [6, 3, 0, 8, 1];
	public static readonly int[] PanelMaterials = [5, 1, 2, 4, 3];
	public static readonly int[] PanelNodes = [8, 4, 5, 7, 6];
	public static readonly float[] PanelTranslations = [-3, -1.5f, 0, 1.5f, 3];
	public static readonly Dictionary<string, string> Hashes = new( StringComparer.Ordinal )
	{
		["glTF/AlphaBlendLabels.png"] = "19c705f7e207384ca1ab01e0665601c051ca33c5848c03ce4fffad625b416b53",
		["glTF/AlphaBlendModeTest.bin"] = "bb89ab8d9ac0cfbc168e5d552e3ec2fe1285fd01bacb65c863d838b3db025c85",
		["glTF/AlphaBlendModeTest.gltf"] = "49e06672900df95593040d35bbc7a2ee5921ae8d46edc44b64ccf2df65e64849",
		["glTF/MatBed_baseColor.jpg"] = "71571cce0d86c7c5ed4ac73d852ec34e72e7234a2f905f2758e118519c950bcf",
		["glTF/MatBed_normal.jpg"] = "d8891a9c08e375507ac8f1fb260577bda5e90506e79db92ebbfb5caa9216be49",
		["glTF/MatBed_occlusionRoughnessMetallic.jpg"] = "e1472454ad6be574c3a418985f06a8ba36877108a94f61590f01e118804379b4",
		["glTF-Binary/AlphaBlendModeTest.glb"] = "37c3577d143071b42dd46e9d942b157837eb25c6340112171d7faecaa987b14e"
	};
	public sealed record Form( string Name, byte[] Bytes, Func<string, byte[]> Resolve )
	{
		public GltfDocument Read() => GltfReader.Read( Bytes, Resolve );
	}
	public static byte[] ReadPinned( string root, string file )
	{
		var bytes = File.ReadAllBytes( Path.Combine( Path.GetFullPath( root ), "Temp/gltf-reference/glTF-Sample-Assets/Models/AlphaBlendModeTest", file ) );
		if ( Convert.ToHexStringLower( SHA256.HashData( bytes ) ) != Hashes[file] ) throw new InvalidDataException( "Changed pinned AlphaBlendModeTest input: " + file );
		return bytes;
	}
	public static Form[] OpenForms( string root )
	{
		byte[] Resolve( string uri ) => ReadPinned( root, "glTF/" + uri );
		var result = new List<Form>();
		foreach ( var source in new[] { ("glTF", "glTF/AlphaBlendModeTest.gltf"), ("glTF-Binary", "glTF-Binary/AlphaBlendModeTest.glb") } )
		{
			var bytes = ReadPinned( root, source.Item2 );
			result.Add( new( source.Item1 + "/source", bytes, Resolve ) );
			result.Add( new( source.Item1 + "/compiled_glb", GltfCompiler.Compile( bytes, Resolve ), Resolve ) );
		}
		return result.ToArray();
	}
	/// <summary>Derived source-panel isolation only: drops occluding bed/decal nodes, preserves all panel buffers and materials.</summary>
	public static Form IsolatedPanels( string root )
	{
		var json = JsonNode.Parse( ReadPinned( root, "glTF/AlphaBlendModeTest.gltf" ) )!.AsObject();
		json["scenes"]![0]!["nodes"] = new JsonArray( 8, 4, 5, 7, 6 );
		return new( "derived_isolated_authored_panels", System.Text.Encoding.UTF8.GetBytes( json.ToJsonString() ), uri => ReadPinned( root, "glTF/" + uri ) );
	}
	// Independent PIL extraction at texel centers x=512, avoiding glyphs and every panel boundary.
	public static readonly int[] RampRows = [256, 384, 512, 640, 768];
	public static readonly byte[] RampAlpha = [45, 87, 128, 169, 210];
	public static readonly byte[] RampRgb = [223, 229, 71];
	public static double Coverage( int material, double alpha ) => Modes[material] switch
	{
		"OPAQUE" => 1,
		"MASK" => alpha >= Cutoffs[material] ? 1 : 0,
		_ => alpha
	};
}
