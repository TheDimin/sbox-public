using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sandbox.Gltf.DirectGpu.TextureCoordinateTestValidation;

/// <summary>Frozen independent Python struct/Pillow source oracle. No producer evaluator supplies expected output.</summary>
public sealed class TextureCoordinateReference
{
	public const string CorpusRevision = "90d7ede14c7e280af263824604b427a1ca02cb66";
	public sealed class Stream
	{
		public int Accessor { get; set; } public int View { get; set; } public int Buffer { get; set; }
		public int ViewOffset { get; set; } public int ViewLength { get; set; } public int AccessorOffset { get; set; }
		public int Stride { get; set; } public int ComponentType { get; set; } public int Components { get; set; } public int Count { get; set; }
		public bool Normalized { get; set; } public double[][] Values { get; set; }
	}
	public sealed class Primitive
	{
		public int Mesh { get; set; } public int Node { get; set; } public int Material { get; set; } public int Mode { get; set; }
		public Stream Indices { get; set; } public Dictionary<string, Stream> Streams { get; set; }
	}
	public sealed class SourceForm { public string Name { get; set; } public string Path { get; set; } public string Sha256 { get; set; } public Primitive[] Primitives { get; set; } }
	public sealed class Derived { public Primitive[] Primitives { get; set; } }
	public sealed class Landmark
	{
		public int Mesh { get; set; } public int X { get; set; } public int Y { get; set; }
		public double U { get; set; } public double V { get; set; } public double WhiteU { get; set; } public double WhiteV { get; set; }
		public int[] Rgba { get; set; } public int[] WrongVrgba { get; set; }
	}
	private sealed class Audit
	{
		public string CorpusRevision { get; set; } public Dictionary<string, string> Files { get; set; }
		public SourceForm[] Forms { get; set; } public Derived DerivedFixture { get; set; } public Landmark[] Landmarks { get; set; }
	}
	public sealed record Form( string Name, bool Compiled, bool Derived, byte[] Bytes, Primitive[] Expected, Func<string, byte[]> Resolve )
	{
		public GltfDocument Read() => GltfReader.Read( Bytes, Resolve );
	}
	private string directory;
	private Audit audit;
	private readonly Dictionary<string, byte[]> files = new( StringComparer.Ordinal );
	public byte[] PngBytes => files["glTF/TextureCoordinateTemplate.png"];
	public IReadOnlyList<Landmark> Landmarks => audit.Landmarks;
	public static TextureCoordinateReference Open( string checkoutRoot )
	{
		var reference = new TextureCoordinateReference { directory = Path.Combine( Path.GetFullPath( checkoutRoot ), "Temp/gltf-reference/glTF-Sample-Assets/Models/TextureCoordinateTest" ) };
		var oraclePath = Path.Combine( checkoutRoot, "research/gltf-validation/direct-gpu/models/TextureCoordinateTest/source-audit.json" );
		reference.audit = JsonSerializer.Deserialize<Audit>( File.ReadAllBytes( oraclePath ), new JsonSerializerOptions { PropertyNameCaseInsensitive = true } );
		TextureCoordinateCoreTests.Require( reference.audit.CorpusRevision == CorpusRevision && reference.audit.Forms.Length == 3 && reference.audit.Landmarks.Length == 12, "Pinned independent oracle identity changed." );
		foreach ( var (path, hash) in reference.audit.Files )
		{
			var bytes = File.ReadAllBytes( Path.Combine( reference.directory, path ) ); TextureCoordinateCoreTests.Require( Hash( bytes ) == hash, "Pinned source hash changed: " + path );
			reference.files.Add( path, bytes );
		}
		TextureCoordinateCoreTests.Require( File.ReadAllText( Path.Combine( reference.directory, "LICENSE.md" ) ).Contains( "Creative Commons", StringComparison.Ordinal ), "Preserve upstream CC0 license." );
		return reference;
	}
	public Form[] Forms()
	{
		var forms = new List<Form>();
		foreach ( var source in audit.Forms )
		{
			var bytes = files[source.Path];
			forms.Add( new( source.Name + "/source", false, false, bytes, source.Primitives, Resolve ) );
			forms.Add( new( source.Name + "/compiled_glb", true, false, GltfCompiler.Compile( bytes, Resolve ), source.Primitives, Resolve ) );
		}
		var fixture = LayoutFixture();
		forms.Add( new( "derived_normalized_interleaved_uv1/source", false, true, fixture, audit.DerivedFixture.Primitives, Resolve ) );
		forms.Add( new( "derived_normalized_interleaved_uv1/compiled_glb", true, true, GltfCompiler.Compile( fixture, Resolve ), audit.DerivedFixture.Primitives, Resolve ) );
		return forms.ToArray();
	}
	public byte[] Resolve( string uri ) => uri switch
	{
		"TextureCoordinateTest.bin" => files["glTF/TextureCoordinateTest.bin"],
		"TextureCoordinateTemplate.png" => PngBytes,
		_ => throw new InvalidOperationException( "Unexpected source dependency: " + uri )
	};
	public void RequireOriginalFilesUnchanged()
	{
		foreach ( var (path, hash) in audit.Files ) TextureCoordinateCoreTests.Require( Hash( files[path] ) == hash && Hash( File.ReadAllBytes( Path.Combine( directory, path ) ) ) == hash, "Original pinned input was mutated: " + path );
	}
	private byte[] LayoutFixture()
	{
		var root = JsonNode.Parse( files["glTF/TextureCoordinateTest.gltf"] ); var original = files["glTF/TextureCoordinateTest.bin"];
		var data = Enumerable.Repeat( (byte)0xd7, original.Length + 16 + 4 * 52 ).ToArray(); original.CopyTo( data, 0 ); var start = original.Length + 16;
		foreach ( var mesh in new[] { 0, 1, 2, 4 } )
		{
			var primitive = root["meshes"][mesh]["primitives"][0]; var source = audit.Forms[0].Primitives[mesh].Streams["TEXCOORD_0"].Values;
			for ( var vertex = 0; vertex < 4; vertex++ )
			{
				var u = checked((ushort)Math.Round( (1 - source[vertex][0]) * 65535, MidpointRounding.ToEven )); var v = checked((ushort)Math.Round( source[vertex][1] * 65535, MidpointRounding.ToEven ));
				BinaryPrimitives.WriteUInt16LittleEndian( data.AsSpan( start + 4 + vertex * 12 ), u ); BinaryPrimitives.WriteUInt16LittleEndian( data.AsSpan( start + 6 + vertex * 12 ), v );
			}
			var views = root["bufferViews"].AsArray(); var accessors = root["accessors"].AsArray();
			primitive["attributes"]["TEXCOORD_1"] = accessors.Count;
			accessors.Add( new JsonObject { ["bufferView"] = views.Count, ["byteOffset"] = 4, ["componentType"] = 5123, ["normalized"] = true, ["count"] = 4, ["type"] = "VEC2", ["name"] = "DerivedMirroredU_InterleavedNormalized" } );
			views.Add( new JsonObject { ["buffer"] = 0, ["byteOffset"] = start, ["byteLength"] = 52, ["byteStride"] = 12, ["target"] = 34962 } );
			root["materials"][audit.Forms[0].Primitives[mesh].Material]["pbrMetallicRoughness"]["baseColorTexture"]["texCoord"] = 1; start += 52;
		}
		root["buffers"][0] = new JsonObject { ["uri"] = "data:application/octet-stream;base64," + Convert.ToBase64String( data ), ["byteLength"] = data.Length };
		return Encoding.UTF8.GetBytes( root.ToJsonString() );
	}
	public static float ReadComponent( GltfUploadStream stream, int vertex, int component )
	{
		var size = stream.ComponentType == 5121 ? 1 : stream.ComponentType == 5123 ? 2 : stream.ComponentType == 5126 ? 4 : throw new InvalidOperationException( "Unexpected source component format." );
		var bytes = stream.Storage.Bytes.Span[(stream.ByteOffset + vertex * stream.ByteStride + component * size)..];
		return stream.ComponentType switch
		{
			5126 => BinaryPrimitives.ReadSingleLittleEndian( bytes ),
			5123 => stream.Normalized ? BinaryPrimitives.ReadUInt16LittleEndian( bytes ) / 65535f : BinaryPrimitives.ReadUInt16LittleEndian( bytes ),
			5121 => stream.Normalized ? bytes[0] / 255f : bytes[0], _ => throw new InvalidOperationException()
		};
	}
	public static System.Numerics.Vector3 EnginePosition( double[] raw ) => new( (float)raw[2] / .0254f, (float)raw[0] / .0254f, (float)raw[1] / .0254f );
	public static System.Numerics.Vector3 EngineNormal( double[] raw ) => System.Numerics.Vector3.Normalize( new( (float)raw[2], (float)raw[0], (float)raw[1] ) );
	private static string Hash( byte[] bytes ) => Convert.ToHexStringLower( SHA256.HashData( bytes ) );
}
