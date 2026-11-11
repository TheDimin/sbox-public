using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;

namespace Sandbox.Tests.Gltf;

[TestClass]
public sealed class GltfCompressionTests
{
	static readonly HashSet<string> Supported = new( StringComparer.Ordinal ) { "KHR_mesh_quantization" };

	static readonly float[] Positions = [0, 0, 0, 2, 0, 0, 0, 4, 0, 1, 1, -2];
	static readonly float[] Normals = [0, 0, 1, 0, 0, 1, 0.6f, 0, 0.8f, 0, -1, 0];
	static readonly float[] TexCoords = [0, 0, 1, 0, 0, 1, 0.25f, 0.75f];
	static readonly float[] Weights = [1, 0, 0, 0, 0.5f, 0.5f, 0, 0, 0.3333f, 0.3333f, 0.3334f, 0, 0.9f, 0.1f, 0, 0];
	static readonly uint[] Indices = [0, 1, 2, 0, 2, 3];

	/// <summary>A GLB with one mesh on a node at (10, 0, 0), all data as floats and 32-bit indices.</summary>
	static byte[] Source( float[] texCoords = null, bool byteNormals = false )
	{
		texCoords ??= TexCoords;
		var binary = new List<byte>();
		var views = new JsonArray();
		var accessors = new JsonArray();
		int Add( byte[] bytes, int componentType, string type, int count, bool normalized = false, float[] min = null, float[] max = null )
		{
			while ( binary.Count % 4 != 0 ) binary.Add( 0 );
			views.Add( new JsonObject { ["buffer"] = 0, ["byteOffset"] = binary.Count, ["byteLength"] = bytes.Length } );
			binary.AddRange( bytes );
			var accessor = new JsonObject { ["bufferView"] = views.Count - 1, ["componentType"] = componentType, ["type"] = type, ["count"] = count };
			if ( normalized ) accessor["normalized"] = true;
			if ( min is not null ) accessor["min"] = new JsonArray( min.Select( x => (JsonNode)x ).ToArray() );
			if ( max is not null ) accessor["max"] = new JsonArray( max.Select( x => (JsonNode)x ).ToArray() );
			accessors.Add( accessor );
			return accessors.Count - 1;
		}
		static byte[] FloatBytes( float[] values ) => values.SelectMany( BitConverter.GetBytes ).ToArray();
		var position = Add( FloatBytes( Positions ), 5126, "VEC3", 4, min: [0, 0, -2], max: [2, 4, 0] );
		var normal = byteNormals
			? Add( Normals.Select( x => (byte)(sbyte)MathF.Round( x * 127 ) ).Chunk( 3 ).SelectMany( x => x.Append( (byte)0 ) ).ToArray(), 5120, "VEC3", 4, normalized: true )
			: Add( FloatBytes( Normals ), 5126, "VEC3", 4 );
		var uv = Add( FloatBytes( texCoords ), 5126, "VEC2", 4 );
		var joints = Add( new ushort[] { 0, 1, 2, 3, 0, 1, 0, 0, 0, 1, 2, 0, 3, 2, 0, 0 }.SelectMany( BitConverter.GetBytes ).ToArray(), 5123, "VEC4", 4 );
		var weights = Add( FloatBytes( Weights ), 5126, "VEC4", 4 );
		var indices = Add( Indices.SelectMany( BitConverter.GetBytes ).ToArray(), 5125, "SCALAR", 6 );
		if ( byteNormals ) views[normal]!["byteStride"] = 4;
		var root = new JsonObject
		{
			["asset"] = new JsonObject { ["version"] = "2.0" },
			["buffers"] = new JsonArray( new JsonObject { ["uri"] = "data:application/octet-stream;base64," + Convert.ToBase64String( binary.ToArray() ), ["byteLength"] = binary.Count } ),
			["bufferViews"] = views,
			["accessors"] = accessors,
			["meshes"] = new JsonArray( new JsonObject
			{
				["name"] = "Box",
				["primitives"] = new JsonArray( new JsonObject
				{
					["attributes"] = new JsonObject { ["POSITION"] = position, ["NORMAL"] = normal, ["TEXCOORD_0"] = uv, ["JOINTS_0"] = joints, ["WEIGHTS_0"] = weights },
					["indices"] = indices,
				} ),
			} ),
			["nodes"] = new JsonArray( new JsonObject { ["name"] = "Root", ["mesh"] = 0, ["translation"] = new JsonArray( 10, 0, 0 ) } ),
			["scenes"] = new JsonArray( new JsonObject { ["nodes"] = new JsonArray( 0 ) } ),
			["scene"] = 0,
		};
		return GltfCompiler.Compile( Encoding.UTF8.GetBytes( root.ToJsonString() ) );
	}

	static GltfCompressionSettings Settings( GltfAttributeStorage storage, bool compactIntegers = false )
	{
		var settings = new GltfCompressionSettings { CompactIntegers = compactIntegers };
		foreach ( var group in Enum.GetValues<GltfAttributeGroup>() ) settings.Global[group] = storage;
		return settings;
	}

	static JsonObject Json( GltfDocument document ) => JsonNode.Parse( document.Root.GetRawText() )!.AsObject();

	static void AssertClose( float[] expected, float[] actual, float tolerance )
	{
		Assert.AreEqual( expected.Length, actual.Length );
		for ( var i = 0; i < expected.Length; i++ ) Assert.AreEqual( expected[i], actual[i], tolerance, $"value {i}" );
	}

	[TestMethod]
	public void SixteenBitDataReadsBackCloseToTheSourceAndIsSmaller()
	{
		var source = Source();
		var warnings = new List<string>();
		var compressed = GltfCompression.Apply( source, Settings( GltfAttributeStorage.Int16 ), Supported, warnings, out var result );

		// With four vertices the new JSON is larger than the saving, so compare the binary chunk.
		static int BinaryLength( byte[] glb ) => BitConverter.ToInt32( glb, 20 + BitConverter.ToInt32( glb, 12 ) );
		Assert.IsTrue( BinaryLength( compressed ) < BinaryLength( source ), $"{BinaryLength( compressed )} >= {BinaryLength( source )}" );
		Assert.IsTrue( result.CompressedBytes < result.SourceBytes );
		using var document = GltfReader.Read( compressed, supportedExtensions: Supported );
		var json = Json( document );
		CollectionAssert.Contains( json["extensionsRequired"]!.AsArray().Select( x => x!.GetValue<string>() ).ToArray(), "KHR_mesh_quantization" );
		Assert.AreEqual( 5122, json["accessors"]![0]!["componentType"]!.GetValue<int>() );
		Assert.AreEqual( 5122, json["accessors"]![1]!["componentType"]!.GetValue<int>() );
		Assert.AreEqual( 5123, json["accessors"]![2]!["componentType"]!.GetValue<int>() );
		AssertClose( Normals, document.ReadFloatAccessor( 1 ), 1e-4f );
		AssertClose( TexCoords, document.ReadFloatAccessor( 2 ), 1e-4f );

		// Positions are integers around the mesh centre. A new child node holds the offset and scale, so the
		// positions in world space do not change.
		var nodes = json["nodes"]!.AsArray();
		Assert.IsNull( nodes[0]!["mesh"] );
		var child = nodes[nodes[0]!["children"]![0]!.GetValue<int>()]!;
		Assert.AreEqual( 0, child["mesh"]!.GetValue<int>() );
		var offset = child["translation"]!.AsArray().Select( x => x!.GetValue<float>() ).ToArray();
		var scale = child["scale"]![0]!.GetValue<float>();
		var stored = document.ReadFloatAccessor( 0 );
		AssertClose( Positions, stored.Select( ( x, i ) => x * scale + offset[i % 3] ).ToArray(), 1e-3f );
		Assert.AreEqual( stored.Where( ( _, i ) => i % 3 == 0 ).Min(), json["accessors"]![0]!["min"]![0]!.GetValue<float>() );
	}

	[TestMethod]
	public void EightBitWeightsOfEachVertexSumToExactlyOne()
	{
		var compressed = GltfCompression.Apply( Source(), Settings( GltfAttributeStorage.Int8 ), Supported, null, out _ );
		using var document = GltfReader.Read( compressed, supportedExtensions: Supported );
		Assert.AreEqual( 5121, Json( document )["accessors"]![4]!["componentType"]!.GetValue<int>() );
		var weights = document.ReadFloatAccessor( 4 );
		for ( var v = 0; v < 4; v++ )
		{
			Assert.AreEqual( 255, weights.Skip( v * 4 ).Take( 4 ).Sum( x => (int)MathF.Round( x * 255 ) ), $"vertex {v}" );
			// A zero weight stays zero, so a repeated joint index never gets an influence.
			for ( var c = 0; c < 4; c++ )
				if ( Weights[v * 4 + c] == 0 ) Assert.AreEqual( 0, weights[v * 4 + c] );
		}
	}

	[TestMethod]
	public void DataIsNeverStoredInMoreBitsThanTheSource()
	{
		var source = Source( byteNormals: true );
		var settings = new GltfCompressionSettings();
		settings.Global[GltfAttributeGroup.Normal] = GltfAttributeStorage.Int16;
		var compressed = GltfCompression.Apply( source, settings, Supported, null, out var result );
		Assert.AreSame( source, compressed );
		Assert.AreEqual( 0, result.Accessors );
	}

	[TestMethod]
	public void CompactIntegersShrinkIndicesAndJointsWithoutChangingThem()
	{
		var settings = new GltfCompressionSettings { CompactIntegers = true };
		var compressed = GltfCompression.Apply( Source(), settings, Supported, null, out _ );
		using var document = GltfReader.Read( compressed, supportedExtensions: Supported );
		var json = Json( document );
		Assert.AreEqual( 5123, json["accessors"]![5]!["componentType"]!.GetValue<int>() );
		Assert.AreEqual( 5121, json["accessors"]![3]!["componentType"]!.GetValue<int>() );
		CollectionAssert.AreEqual( Indices, document.ReadIndexAccessor( 5 ) );
		CollectionAssert.AreEqual( new float[] { 0, 1, 2, 3, 0, 1, 0, 0, 0, 1, 2, 0, 3, 2, 0, 0 }, document.ReadFloatAccessor( 3 ) );
		// Lossless integer packing needs no extension.
		Assert.IsNull( json["extensionsRequired"] );
	}

	[TestMethod]
	public void PrimitiveOverridesAndOutOfRangeTexCoordsKeepTheSource()
	{
		var settings = Settings( GltfAttributeStorage.Int8 );
		settings.Primitives[(0, 0)] = new() { [GltfAttributeGroup.Normal] = GltfAttributeStorage.Source };
		var warnings = new List<string>();
		var compressed = GltfCompression.Apply( Source( texCoords: [0, 0, 2, 0, 0, 1, 0.5f, 0.5f] ), settings, Supported, warnings, out _ );
		using var document = GltfReader.Read( compressed, supportedExtensions: Supported );
		var json = Json( document );
		Assert.AreEqual( 5126, json["accessors"]![1]!["componentType"]!.GetValue<int>() );
		Assert.AreEqual( 5126, json["accessors"]![2]!["componentType"]!.GetValue<int>() );
		Assert.IsTrue( warnings.Any( x => x.Contains( "TEXCOORD_0" ) && x.Contains( "outside -1 to 1" ) ), string.Join( "\n", warnings ) );

		var copy = GltfCompressionSettings.FromJson( JsonNode.Parse( settings.ToJson().ToJsonString() ) );
		Assert.AreEqual( GltfAttributeStorage.Source, copy.Get( 0, 0, GltfAttributeGroup.Normal ) );
		Assert.AreEqual( GltfAttributeStorage.Int8, copy.Get( 0, 0, GltfAttributeGroup.TexCoord ) );
		Assert.AreEqual( GltfAttributeStorage.Int8, copy.Get( 1, 0, GltfAttributeGroup.Normal ) );
	}
}
