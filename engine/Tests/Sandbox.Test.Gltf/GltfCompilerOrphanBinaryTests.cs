using System;
using System.Buffers.Binary;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;

namespace Sandbox.Tests.Gltf;

/// <summary>Portable GLBs can contain a BIN chunk even when core buffers do not use it.</summary>
[TestClass]
public class GltfCompilerOrphanBinaryTests
{
	[TestMethod]
	public void EmptyUnreferencedBinaryIsRetainedWithZeroPackagingBudget()
	{
		var input = Glb( """{"asset":{"version":"2.0"}}""", [] );
		var output = GltfCompiler.Compile( input, maxBufferBytes: 0 );
		CollectionAssert.AreEqual( Array.Empty<byte>(), BinaryChunk( output ) );
		using var document = GltfReader.Read( output );
	}

	[TestMethod]
	public void UnreferencedBinaryWithoutBuffersIsPreservedExactly()
	{
		byte[] payload = [0x12, 0x34, 0x56, 0x78];
		var input = Glb( """{"asset":{"version":"2.0"},"extras":{"retain":true}}""", payload );
		var output = GltfCompiler.Compile( input );
		using var document = GltfReader.Read( output );
		Assert.IsTrue( document.Root.GetProperty( "extras" ).GetProperty( "retain" ).GetBoolean() );
		CollectionAssert.AreEqual( payload, BinaryChunk( output ) );
		CollectionAssert.AreEqual( output, GltfCompiler.Compile( output ) );
	}

	[TestMethod]
	public void UnreferencedBinaryCoexistsWithEmbeddedFirstBuffer()
	{
		var input = Glb( """{"asset":{"version":"2.0"},"buffers":[{"uri":"geometry.bin","byteLength":4}],"bufferViews":[{"buffer":0,"byteLength":4}],"accessors":[{"bufferView":0,"componentType":5121,"count":4,"type":"SCALAR"}]}""", [0x12, 0x34, 0x56, 0x78] );
		var output = GltfCompiler.Compile( input, _ => [1, 2, 3, 4] );
		using var document = GltfReader.Read( output );
		CollectionAssert.AreEqual( new byte[] { 0x12, 0x34, 0x56, 0x78 }, BinaryChunk( output ) );
		CollectionAssert.AreEqual( new uint[] { 1, 2, 3, 4 }, document.ReadIndexAccessor( 0 ) );
		StringAssert.StartsWith( document.Root.GetProperty( "buffers" )[0].GetProperty( "uri" ).GetString(), "data:" );
		CollectionAssert.AreEqual( output, GltfCompiler.Compile( output ) );
	}

	[TestMethod]
	public void UnreferencedBinaryRetainsMultipleBufferIndicesWithoutExternalDependencies()
	{
		var input = Glb( """{"asset":{"version":"2.0"},"buffers":[{"uri":"a.bin","byteLength":2},{"uri":"b.bin","byteLength":2}],"bufferViews":[{"buffer":0,"byteLength":2},{"buffer":1,"byteLength":2}],"accessors":[{"bufferView":0,"componentType":5121,"count":2,"type":"SCALAR"},{"bufferView":1,"componentType":5121,"count":2,"type":"SCALAR"}]}""", [0x12, 0x34, 0x56, 0x78] );
		var output = GltfCompiler.Compile( input, uri => uri == "a.bin" ? [1, 2] : uri == "b.bin" ? [3, 4] : throw new InvalidOperationException( uri ) );
		using var document = GltfReader.Read( output );
		Assert.AreEqual( 2, document.Root.GetProperty( "buffers" ).GetArrayLength() );
		Assert.AreEqual( 1, document.Root.GetProperty( "bufferViews" )[1].GetProperty( "buffer" ).GetInt32() );
		CollectionAssert.AreEqual( new byte[] { 0x12, 0x34, 0x56, 0x78 }, BinaryChunk( output ) );
		CollectionAssert.AreEqual( new uint[] { 1, 2 }, document.ReadIndexAccessor( 0 ) );
		CollectionAssert.AreEqual( new uint[] { 3, 4 }, document.ReadIndexAccessor( 1 ) );
	}

	[TestMethod]
	public void UnreferencedBinaryObeysPackagingBudget()
	{
		var input = Glb( """{"asset":{"version":"2.0"}}""", [0x12, 0x34, 0x56, 0x78] );
		var error = Assert.ThrowsException<GltfException>( () => GltfCompiler.Compile( input, maxBufferBytes: 7 ) );
		StringAssert.Contains( error.Message, "budget" );
		CollectionAssert.AreEqual( new byte[] { 0x12, 0x34, 0x56, 0x78 }, BinaryChunk( GltfCompiler.Compile( input, maxBufferBytes: 8 ) ) );
	}

	[TestMethod]
	public void OrphanBudgetIncludesFirstEmbeddedBuffer()
	{
		var input = Glb( """{"asset":{"version":"2.0"},"buffers":[{"uri":"geometry.bin","byteLength":4}]}""", [0x12, 0x34, 0x56, 0x78] );
		// Two BIN copies (8 bytes), plus 6 UTF-16/JSON bytes per estimated
		// base64 character (8 encoded characters and 64 bytes overhead).
		const int budget = 8 + 6 * (8 + 64);
		Assert.ThrowsException<GltfException>( () => GltfCompiler.Compile( input, _ => [1, 2, 3, 4], maxBufferBytes: budget - 1 ) );
		var compiled = GltfCompiler.Compile( input, _ => [1, 2, 3, 4], maxBufferBytes: budget );
		CollectionAssert.AreEqual( new byte[] { 0x12, 0x34, 0x56, 0x78 }, BinaryChunk( compiled ) );
	}

	[TestMethod]
	public void ReferencedBinaryContinuesToRoundTripNormally()
	{
		var input = Glb( """{"asset":{"version":"2.0"},"buffers":[{"byteLength":4}],"bufferViews":[{"buffer":0,"byteLength":4}],"accessors":[{"bufferView":0,"componentType":5121,"count":4,"type":"SCALAR"}]}""", [1, 2, 3, 4] );
		using var document = GltfReader.Read( GltfCompiler.Compile( input ) );
		CollectionAssert.AreEqual( new uint[] { 1, 2, 3, 4 }, document.ReadIndexAccessor( 0 ) );
	}

	private static byte[] Glb( string json, byte[] payload )
	{
		var text = Encoding.UTF8.GetBytes( json );
		var padded = (text.Length + 3) & ~3;
		var output = new byte[20 + padded + 8 + payload.Length];
		BinaryPrimitives.WriteUInt32LittleEndian( output, 0x46546c67 );
		BinaryPrimitives.WriteUInt32LittleEndian( output.AsSpan( 4 ), 2 );
		BinaryPrimitives.WriteUInt32LittleEndian( output.AsSpan( 8 ), (uint)output.Length );
		BinaryPrimitives.WriteUInt32LittleEndian( output.AsSpan( 12 ), (uint)padded );
		BinaryPrimitives.WriteUInt32LittleEndian( output.AsSpan( 16 ), 0x4e4f534a );
		output.AsSpan( 20, padded ).Fill( 0x20 ); text.CopyTo( output, 20 );
		var bin = 20 + padded;
		BinaryPrimitives.WriteUInt32LittleEndian( output.AsSpan( bin ), (uint)payload.Length );
		BinaryPrimitives.WriteUInt32LittleEndian( output.AsSpan( bin + 4 ), 0x004e4942 );
		payload.CopyTo( output, bin + 8 );
		return output;
	}

	private static byte[] BinaryChunk( byte[] glb )
	{
		var offset = 20 + (int)BinaryPrimitives.ReadUInt32LittleEndian( glb.AsSpan( 12 ) );
		Assert.IsTrue( offset + 8 <= glb.Length, "Compiler discarded the BIN chunk." );
		Assert.AreEqual( 0x004e4942u, BinaryPrimitives.ReadUInt32LittleEndian( glb.AsSpan( offset + 4 ) ) );
		var length = (int)BinaryPrimitives.ReadUInt32LittleEndian( glb.AsSpan( offset ) );
		return glb.AsSpan( offset + 8, length ).ToArray();
	}
}
