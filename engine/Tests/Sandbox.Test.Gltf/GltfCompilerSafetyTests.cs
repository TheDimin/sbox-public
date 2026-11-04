using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;
using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace Sandbox.Tests.Gltf;

[TestClass]
public sealed class GltfCompilerSafetyTests
{
	[TestMethod]
	public void OneBufferKeepsItsMetadataAndUnknownExtensionsWhenNoReferencesMove()
	{
		var source = Encoding.UTF8.GetBytes( """
			{"asset":{"version":"2.0"},"extensionsUsed":["EXT_unknown"],"buffers":[{"name":"Geometry","uri":"mesh.bin","byteLength":2,"extras":{"tag":42}}],"extensions":{"EXT_unknown":{"buffer":0}}}
			""" );
		var first = GltfCompiler.Compile( source, _ => [9, 8] );
		using var document = GltfReader.Read( first );

		Assert.AreEqual( "Geometry", document.Root.GetProperty( "buffers" )[0].GetProperty( "name" ).GetString() );
		Assert.AreEqual( 42, document.Root.GetProperty( "buffers" )[0].GetProperty( "extras" ).GetProperty( "tag" ).GetInt32() );
		Assert.AreEqual( 0, document.Root.GetProperty( "extensions" ).GetProperty( "EXT_unknown" ).GetProperty( "buffer" ).GetInt32() );
		CollectionAssert.AreEqual( first, GltfCompiler.Compile( first ) );
	}

	[TestMethod]
	public void BufferMetadataIsPreservedWhenBuffersCannotBeMerged()
	{
		var source = Encoding.UTF8.GetBytes( """
			{"asset":{"version":"2.0"},"buffers":[{"uri":"a.bin","byteLength":1},{"name":"source-two","extras":{"keep":1},"uri":"b.bin","byteLength":1}]}
			""" );
		var compiled = GltfCompiler.Compile( source, _ => [5] );
		using var document = GltfReader.Read( compiled );
		var buffers = document.Root.GetProperty( "buffers" );
		Assert.AreEqual( 2, buffers.GetArrayLength() );
		Assert.AreEqual( "source-two", buffers[1].GetProperty( "name" ).GetString() );
		Assert.AreEqual( 1, buffers[1].GetProperty( "extras" ).GetProperty( "keep" ).GetInt32() );
		StringAssert.StartsWith( buffers[1].GetProperty( "uri" ).GetString(), "data:" );
		CollectionAssert.AreEqual( compiled, GltfCompiler.Compile( compiled ) );
	}

	[TestMethod]
	public void ApplicationExtrasKeepTheirOriginalBufferReferences()
	{
		var source = Encoding.UTF8.GetBytes( """
			{"asset":{"version":"2.0"},"extras":{"applicationData":{"buffer":1}},"buffers":[{"uri":"a.bin","byteLength":1},{"uri":"b.bin","byteLength":1}]}
			""" );
		var compiled = GltfCompiler.Compile( source, _ => [1] );
		using var document = GltfReader.Read( compiled );
		Assert.AreEqual( 2, document.Root.GetProperty( "buffers" ).GetArrayLength() );
		Assert.AreEqual( 1, document.Root.GetProperty( "extras" ).GetProperty( "applicationData" ).GetProperty( "buffer" ).GetInt32() );
		CollectionAssert.AreEqual( compiled, GltfCompiler.Compile( compiled ) );
	}

	[TestMethod]
	[DataRow( "KHR_meshopt_compression", 0 )]
	[DataRow( "KHR_meshopt_compression", 1 )]
	[DataRow( "EXT_meshopt_compression", 0 )]
	[DataRow( "EXT_meshopt_compression", 1 )]
	public void RequiredMeshoptFallbackPlaceholderAndBufferIdsArePreserved( string extension, int placeholderIndex )
	{
		var encoded = GltfMeshoptTests.VertexFixtureV0;
		var compressed = "{\"uri\":\"mesh.bin\",\"byteLength\":" + encoded.Length + "}";
		var fallback = "{\"byteLength\":48,\"extensions\":{\"" + extension + "\":{\"fallback\":true}}}";
		var buffers = placeholderIndex == 0 ? fallback + "," + compressed : compressed + "," + fallback;
		var compressedIndex = 1 - placeholderIndex;
		var source = Encoding.UTF8.GetBytes( "{\"asset\":{\"version\":\"2.0\"},\"extensionsUsed\":[\"" + extension + "\"],"
			+ "\"extensionsRequired\":[\"" + extension + "\"],\"buffers\":[" + buffers + "],\"bufferViews\":[{\"buffer\":" + placeholderIndex
			+ ",\"byteLength\":48,\"byteStride\":12,\"extensions\":{\"" + extension + "\":{\"buffer\":" + compressedIndex
			+ ",\"byteLength\":" + encoded.Length + ",\"byteStride\":12,\"count\":4,\"mode\":\"ATTRIBUTES\"}}}],"
			+ "\"accessors\":[{\"bufferView\":0,\"componentType\":5123,\"count\":4,\"type\":\"VEC3\"}]}" );
		var supported = new HashSet<string> { extension };
		using var before = GltfReader.Read( source, _ => encoded, supported );
		var compiled = GltfCompiler.Compile( source, _ => encoded, supported );
		using var after = GltfReader.Read( compiled, supportedExtensions: supported );
		Assert.AreEqual( 2, after.Root.GetProperty( "buffers" ).GetArrayLength() );
		Assert.IsFalse( after.Root.GetProperty( "buffers" )[placeholderIndex].TryGetProperty( "uri", out _ ) );
		Assert.AreEqual( placeholderIndex, after.Root.GetProperty( "bufferViews" )[0].GetProperty( "buffer" ).GetInt32() );
		Assert.AreEqual( compressedIndex, after.Root.GetProperty( "bufferViews" )[0].GetProperty( "extensions" ).GetProperty( extension ).GetProperty( "buffer" ).GetInt32() );
		CollectionAssert.AreEqual( before.ReadFloatAccessor( 0 ), after.ReadFloatAccessor( 0 ) );
		CollectionAssert.AreEqual( compiled, GltfCompiler.Compile( compiled, supportedExtensions: supported ) );
		var jsonChunk = BinaryPrimitives.ReadUInt32LittleEndian( compiled.AsSpan( 12 ) );
		if ( placeholderIndex == 0 )
		{
			var binHeader = 20 + (int)jsonChunk;
			Assert.AreEqual( 0x004e4942u, BinaryPrimitives.ReadUInt32LittleEndian( compiled.AsSpan( binHeader + 4 ) ) );
			Assert.AreEqual( 48u, BinaryPrimitives.ReadUInt32LittleEndian( compiled.AsSpan( binHeader ) ) );
			foreach ( var value in compiled.AsSpan( binHeader + 8, 48 ) ) Assert.AreEqual( (byte)0, value );
		}
	}

	[TestMethod]
	public void OversizedFirstPlaceholderIsRejectedBeforeMaterializingIt()
	{
		var encoded = GltfMeshoptTests.VertexFixtureV0;
		var source = Encoding.UTF8.GetBytes( "{\"asset\":{\"version\":\"2.0\"},\"extensionsUsed\":[\"EXT_meshopt_compression\"],"
			+ "\"extensionsRequired\":[\"EXT_meshopt_compression\"],\"buffers\":[{\"byteLength\":536870912},"
			+ "{\"uri\":\"mesh.bin\",\"byteLength\":" + encoded.Length + "}],\"bufferViews\":[{\"buffer\":0,\"byteLength\":48,"
			+ "\"extensions\":{\"EXT_meshopt_compression\":{\"buffer\":1,\"byteLength\":" + encoded.Length
			+ ",\"byteStride\":12,\"count\":4,\"mode\":\"ATTRIBUTES\"}}}]}" );
		var error = Assert.ThrowsException<GltfException>( () => GltfCompiler.Compile( source, _ => encoded,
			new HashSet<string> { "EXT_meshopt_compression" }, maxBufferBytes: 1024 ) );
		StringAssert.Contains( error.Message, "budget" );
	}

	[TestMethod]
	public void OptionalMeshoptPreservesTheUncompressedFallbackForUnsupportedReaders()
	{
		var encoded = GltfMeshoptTests.VertexFixtureV0;
		var fallback = new byte[48];
		for ( var i = 0; i < fallback.Length; i += 2 ) fallback[i] = 7;
		var json = "{\"asset\":{\"version\":\"2.0\"},\"extensionsUsed\":[\"EXT_meshopt_compression\"],"
			+ "\"buffers\":[{\"uri\":\"mesh.bin\",\"byteLength\":" + encoded.Length + "},{\"uri\":\"fallback.bin\",\"byteLength\":48,"
			+ "\"extensions\":{\"EXT_meshopt_compression\":{\"fallback\":true}}}],\"bufferViews\":[{\"buffer\":1,\"byteLength\":48,\"byteStride\":12,"
			+ "\"extensions\":{\"EXT_meshopt_compression\":{\"buffer\":0,\"byteLength\":" + encoded.Length
			+ ",\"byteStride\":12,\"count\":4,\"mode\":\"ATTRIBUTES\"}}}],"
			+ "\"accessors\":[{\"bufferView\":0,\"componentType\":5123,\"count\":4,\"type\":\"VEC3\"}]}";
		byte[] Resolve( string uri ) => uri == "mesh.bin" ? encoded : fallback;
		var supported = new HashSet<string> { "EXT_meshopt_compression" };
		var compiled = GltfCompiler.Compile( Encoding.UTF8.GetBytes( json ), Resolve, supported );
		using var ordinary = GltfReader.Read( compiled );
		using var compressed = GltfReader.Read( compiled, supportedExtensions: supported );
		CollectionAssert.AreEqual( new float[] { 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7 }, ordinary.ReadFloatAccessor( 0 ) );
		CollectionAssert.AreEqual( new float[] { 0, 0, 0, 300, 0, 0, 0, 300, 0, 300, 300, 0 }, compressed.ReadFloatAccessor( 0 ) );
	}

	[TestMethod]
	public void UnknownGlbChunksAreNotSilentlyDropped()
	{
		var json = Encoding.UTF8.GetBytes( "{\"asset\":{\"version\":\"2.0\"}}" );
		var jsonLength = (json.Length + 3) & ~3;
		var glb = new byte[12 + 8 + jsonLength + 12];
		BinaryPrimitives.WriteUInt32LittleEndian( glb, 0x46546c67 );
		BinaryPrimitives.WriteUInt32LittleEndian( glb.AsSpan( 4 ), 2 );
		BinaryPrimitives.WriteUInt32LittleEndian( glb.AsSpan( 8 ), (uint)glb.Length );
		BinaryPrimitives.WriteUInt32LittleEndian( glb.AsSpan( 12 ), (uint)jsonLength );
		BinaryPrimitives.WriteUInt32LittleEndian( glb.AsSpan( 16 ), 0x4e4f534a );
		glb.AsSpan( 20, jsonLength ).Fill( 0x20 );
		json.CopyTo( glb, 20 );
		var chunk = 20 + jsonLength;
		BinaryPrimitives.WriteUInt32LittleEndian( glb.AsSpan( chunk ), 4 );
		BinaryPrimitives.WriteUInt32LittleEndian( glb.AsSpan( chunk + 4 ), 0x12345678 );
		BinaryPrimitives.WriteUInt32LittleEndian( glb.AsSpan( chunk + 8 ), 0xabcdef01 );

		Assert.ThrowsException<GltfException>( () => GltfCompiler.Compile( glb ) );
	}
}
