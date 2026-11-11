using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;

namespace Sandbox.Tests.Gltf;

[TestClass]
public class GltfCompilerLodOrderTests
{
	static readonly HashSet<string> Known = new( StringComparer.Ordinal ) { "MSFT_lod", "KHR_materials_unlit" };

	// Four 36-byte views: 0 is LOD0 geometry, 1 is LOD1, 2 is LOD2, 3 is shared (a node outside the chain).
	static byte[] Buffer()
	{
		var bytes = new List<byte>();
		for ( var view = 0; view < 4; view++ )
			foreach ( var f in Enumerable.Range( 0, 9 ).Select( i => view * 10f + i ) ) bytes.AddRange( BitConverter.GetBytes( f ) );
		return bytes.ToArray();
	}

	const string Json = """
		{"asset":{"version":"2.0"},"extensionsUsed":["MSFT_lod"],
		 "buffers":[{"uri":"a.bin","byteLength":144}],
		 "bufferViews":[{"buffer":0,"byteOffset":0,"byteLength":36},{"buffer":0,"byteOffset":36,"byteLength":36},{"buffer":0,"byteOffset":72,"byteLength":36},{"buffer":0,"byteOffset":108,"byteLength":36}],
		 "accessors":[
		  {"bufferView":0,"componentType":5126,"count":3,"type":"VEC3","min":[0,0,0],"max":[8,8,8]},
		  {"bufferView":1,"componentType":5126,"count":3,"type":"VEC3","min":[0,0,0],"max":[18,18,18]},
		  {"bufferView":2,"componentType":5126,"count":3,"type":"VEC3","min":[0,0,0],"max":[28,28,28]},
		  {"bufferView":3,"componentType":5126,"count":3,"type":"VEC3","min":[0,0,0],"max":[38,38,38]}],
		 "meshes":[{"primitives":[{"attributes":{"POSITION":0}}]},{"primitives":[{"attributes":{"POSITION":1}}]},{"primitives":[{"attributes":{"POSITION":2}}]},{"primitives":[{"attributes":{"POSITION":3}}]}],
		 "nodes":[{"mesh":0,"extensions":{"MSFT_lod":{"ids":[1,2]}},"extras":{"MSFT_screencoverage":[0.5,0.2,0.01]}},{"mesh":1},{"mesh":2},{"mesh":3}],
		 "scenes":[{"nodes":[0,3]}],"scene":0}
		""";

	static byte[] Resolve( string path ) => path == "a.bin" ? Buffer() : throw new InvalidOperationException( path );

	[TestMethod]
	public void ViewsAreOrderedSharedFirstThenLowestDetailToHighest()
	{
		var output = GltfCompiler.Compile( Encoding.UTF8.GetBytes( Json ), Resolve, Known, 1 << 20, true, orderForLodStreaming: true );
		using var document = GltfReader.Read( output, supportedExtensions: Known );
		int Offset( int view ) => document.Root.GetProperty( "bufferViews" )[view].TryGetProperty( "byteOffset", out var o ) ? o.GetInt32() : 0;
		Assert.IsTrue( Offset( 3 ) < Offset( 2 ) && Offset( 2 ) < Offset( 1 ) && Offset( 1 ) < Offset( 0 ), "Expected shared, LOD2, LOD1, LOD0." );
		Assert.AreEqual( 1, document.Root.GetProperty( "buffers" ).GetArrayLength() );
		// The values still resolve through the accessors after the move.
		Assert.AreEqual( 0f, document.ReadFloatAccessor( 0 )[0] );
		Assert.AreEqual( 18f, document.ReadFloatAccessor( 1 )[^1] );
		Assert.AreEqual( 20f, document.ReadFloatAccessor( 2 )[0] );
		Assert.AreEqual( 30f, document.ReadFloatAccessor( 3 )[0] );
	}

	[TestMethod]
	public void WithoutTheOptionTheLayoutKeepsAuthoredOrder()
	{
		var output = GltfCompiler.Compile( Encoding.UTF8.GetBytes( Json ), Resolve, Known, 1 << 20, true );
		using var document = GltfReader.Read( output, supportedExtensions: Known );
		var offsets = Enumerable.Range( 0, 4 ).Select( v => document.Root.GetProperty( "bufferViews" )[v].TryGetProperty( "byteOffset", out var o ) ? o.GetInt32() : 0 ).ToArray();
		CollectionAssert.AreEqual( offsets.Order().ToArray(), offsets );
	}

	[TestMethod]
	public void ViewSharedByTwoLevelsIsSharedAndUnknownExtrasStillBlockOrdering()
	{
		using var shared = GltfReader.Read( Encoding.UTF8.GetBytes( Json.Replace( "{\"mesh\":2}", "{\"mesh\":1}" ) ), Resolve, Known );
		CollectionAssert.AreEqual( new[] { 0, -1, -1, -1 }.Select( x => x ).ToArray(), GltfStreamOrder.ViewKeys( shared ).Select( x => x == 2 ? 0 : x ).ToArray() );

		var blocked = Json.Replace( "\"scenes\"", "\"extras\":{\"app\":{\"buffer\":0}},\"scenes\"" );
		var output = GltfCompiler.Compile( Encoding.UTF8.GetBytes( blocked ), Resolve, Known, 1 << 20, true, orderForLodStreaming: true );
		using var document = GltfReader.Read( output, supportedExtensions: Known );
		var offsets = Enumerable.Range( 0, 4 ).Select( v => document.Root.GetProperty( "bufferViews" )[v].TryGetProperty( "byteOffset", out var o ) ? o.GetInt32() : 0 ).ToArray();
		CollectionAssert.AreEqual( offsets.Order().ToArray(), offsets );
	}

	[TestMethod]
	public void KnownExtensionsAndLodExtrasNoLongerBlockMergingButUnknownOnesDo()
	{
		var two = """
			{"asset":{"version":"2.0"},"extensionsUsed":["KHR_materials_unlit"],"buffers":[{"uri":"a.bin","byteLength":3},{"uri":"b.bin","byteLength":4}],
			 "bufferViews":[{"buffer":0,"byteLength":3},{"buffer":1,"byteLength":4}],
			 "materials":[{"extensions":{"KHR_materials_unlit":{}},"extras":{"MSFT_screencoverage":[1]}}]}
			""";
		static byte[] Files( string path ) => path == "a.bin" ? [1, 2, 3] : [4, 0, 5, 0];
		using var merged = GltfReader.Read( GltfCompiler.Compile( Encoding.UTF8.GetBytes( two ), Files, Known ), supportedExtensions: Known );
		Assert.AreEqual( 1, merged.Root.GetProperty( "buffers" ).GetArrayLength() );
		var unknown = two.Replace( "KHR_materials_unlit\":{}", "KHR_materials_unlit\":{},\"EXT_unknown\":{\"buffer\":1}" );
		using var kept = GltfReader.Read( GltfCompiler.Compile( Encoding.UTF8.GetBytes( unknown ), Files, Known ), supportedExtensions: Known );
		Assert.AreEqual( 2, kept.Root.GetProperty( "buffers" ).GetArrayLength() );
	}
}
