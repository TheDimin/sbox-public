using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;
using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace Sandbox.Tests.Gltf;

[TestClass]
public class GltfContainerTests
{
	private const uint JsonChunk = 0x4E4F534A;
	private const uint BinChunk = 0x004E4942;

	[TestMethod]
	public void ReadsJsonAssetAndPreservesRoot()
	{
		using var document = GltfReader.Read( Encoding.UTF8.GetBytes( "{\"asset\":{\"version\":\"2.0\"},\"extras\":{\"title\":\"tree\"}}" ) );
		Assert.AreEqual( "2.0", document.Root.GetProperty( "asset" ).GetProperty( "version" ).GetString() );
		Assert.AreEqual( "tree", document.Root.GetProperty( "extras" ).GetProperty( "title" ).GetString() );
	}

	[TestMethod]
	[DataRow( "{}" )]
	[DataRow( "[]" )]
	[DataRow( "{\"asset\":{}}" )]
	[DataRow( "{\"asset\":{\"version\":\"1.0\"}}" )]
	[DataRow( "{\"asset\":{\"version\":\"2\"}}" )]
	[DataRow( "{\"asset\":{\"version\":\"2.1\",\"minVersion\":\"2.1\"}}" )]
	[DataRow( "{\"asset\":{\"version\":\"2.0\",\"minVersion\":\"2.1\"}}" )]
	[DataRow( "{\"asset\":{\"version\":\"2.0\"}" )]
	public void InvalidOrUnsupportedJsonFails( string json )
	{
		Assert.ThrowsException<GltfException>( () => GltfReader.Read( Encoding.UTF8.GetBytes( json ) ) );
	}

	[TestMethod]
	public void LaterMinorVersionWithoutNewMinimumRemainsReadable()
	{
		using var document = GltfReader.Read( Encoding.UTF8.GetBytes( "{\"asset\":{\"version\":\"2.1\"}}" ) );
		Assert.AreEqual( "2.1", document.Root.GetProperty( "asset" ).GetProperty( "version" ).GetString() );
	}

	[TestMethod]
	public void UnknownRequiredExtensionFailsAndOptionalIsReported()
	{
		const string required = "{\"asset\":{\"version\":\"2.0\"},\"extensionsUsed\":[\"EXT_unknown\"],\"extensionsRequired\":[\"EXT_unknown\"]}";
		Assert.ThrowsException<GltfException>( () => GltfReader.Read( Encoding.UTF8.GetBytes( required ) ) );

		const string optional = "{\"asset\":{\"version\":\"2.0\"},\"extensionsUsed\":[\"EXT_unknown\"]}";
		using var document = GltfReader.Read( Encoding.UTF8.GetBytes( optional ) );
		CollectionAssert.AreEqual( new[] { "EXT_unknown" }, new List<string>( document.UnsupportedOptionalExtensions ) );
	}

	[TestMethod]
	public void RequiredExtensionMustBeDeclaredUsedAndSupported()
	{
		var supported = new HashSet<string>( StringComparer.Ordinal ) { "EXT_demo" };
		const string declared = "{\"asset\":{\"version\":\"2.0\"},\"extensionsUsed\":[\"EXT_demo\"],\"extensionsRequired\":[\"EXT_demo\"]}";
		using var document = GltfReader.Read( Encoding.UTF8.GetBytes( declared ), supportedExtensions: supported );
		Assert.AreEqual( 0, document.UnsupportedOptionalExtensions.Count );

		const string undeclared = "{\"asset\":{\"version\":\"2.0\"},\"extensionsRequired\":[\"EXT_demo\"]}";
		Assert.ThrowsException<GltfException>( () => GltfReader.Read( Encoding.UTF8.GetBytes( undeclared ), supportedExtensions: supported ) );
	}

	[TestMethod]
	public void ReadsGlbWithBinaryPaddingAndIgnoresLaterUnknownChunk()
	{
		const string json = "{\"asset\":{\"version\":\"2.0\"},\"buffers\":[{\"byteLength\":3}],\"bufferViews\":[{\"buffer\":0,\"byteLength\":3}],\"accessors\":[{\"bufferView\":0,\"componentType\":5121,\"count\":3,\"type\":\"SCALAR\"}]}";
		var glb = MakeGlb( json, new byte[] { 1, 4, 7 }, includeUnknownChunk: true );
		using var document = GltfReader.Read( glb );
		CollectionAssert.AreEqual( new uint[] { 1, 4, 7 }, document.ReadIndexAccessor( 0 ) );
	}

	[TestMethod]
	public void GlbJsonOnlyIsValid()
	{
		using var document = GltfReader.Read( MakeGlb( "{\"asset\":{\"version\":\"2.0\"}}" ) );
		Assert.AreEqual( "2.0", document.Root.GetProperty( "asset" ).GetProperty( "version" ).GetString() );
	}

	[TestMethod]
	public void GlbRejectsBadHeaderVersionAndDeclaredLength()
	{
		var glb = MakeGlb( "{\"asset\":{\"version\":\"2.0\"}}" );
		var wrongVersion = (byte[])glb.Clone();
		BinaryPrimitives.WriteUInt32LittleEndian( wrongVersion.AsSpan( 4, 4 ), 1 );
		Assert.ThrowsException<GltfException>( () => GltfReader.Read( wrongVersion ) );

		var wrongLength = (byte[])glb.Clone();
		BinaryPrimitives.WriteUInt32LittleEndian( wrongLength.AsSpan( 8, 4 ), (uint)glb.Length + 4 );
		Assert.ThrowsException<GltfException>( () => GltfReader.Read( wrongLength ) );

		var truncated = glb[..^1];
		Assert.ThrowsException<GltfException>( () => GltfReader.Read( truncated ) );
	}

	[TestMethod]
	public void GlbRejectsChunkThatIsMisalignedOrExtendsBeyondFile()
	{
		var glb = MakeGlb( "{\"asset\":{\"version\":\"2.0\"}}" );
		var unaligned = (byte[])glb.Clone();
		var jsonLength = BinaryPrimitives.ReadUInt32LittleEndian( unaligned.AsSpan( 12, 4 ) );
		BinaryPrimitives.WriteUInt32LittleEndian( unaligned.AsSpan( 12, 4 ), jsonLength - 1 );
		Assert.ThrowsException<GltfException>( () => GltfReader.Read( unaligned ) );

		var oversized = (byte[])glb.Clone();
		BinaryPrimitives.WriteUInt32LittleEndian( oversized.AsSpan( 12, 4 ), jsonLength + 4 );
		Assert.ThrowsException<GltfException>( () => GltfReader.Read( oversized ) );
	}

	[TestMethod]
	public void GlbRequiresJsonFirstAndBinWhenBufferHasNoUri()
	{
		var wrongChunk = MakeGlb( "{\"asset\":{\"version\":\"2.0\"}}" );
		BinaryPrimitives.WriteUInt32LittleEndian( wrongChunk.AsSpan( 16, 4 ), BinChunk );
		Assert.ThrowsException<GltfException>( () => GltfReader.Read( wrongChunk ) );

		var missingBin = MakeGlb( "{\"asset\":{\"version\":\"2.0\"},\"buffers\":[{\"byteLength\":4}]}" );
		Assert.ThrowsException<GltfException>( () =>
		{
			using var document = GltfReader.Read( missingBin );
		} );
	}

	private static byte[] MakeGlb( string json, byte[] binary = null, bool includeUnknownChunk = false )
	{
		var jsonBytes = Encoding.UTF8.GetBytes( json );
		var jsonLength = (jsonBytes.Length + 3) & ~3;
		var binaryLength = binary is null ? 0 : (binary.Length + 3) & ~3;
		var total = 12 + 8 + jsonLength + (binary is null ? 0 : 8 + binaryLength) + (includeUnknownChunk ? 12 : 0);
		var glb = new byte[total];
		BinaryPrimitives.WriteUInt32LittleEndian( glb.AsSpan( 0, 4 ), 0x46546C67 );
		BinaryPrimitives.WriteUInt32LittleEndian( glb.AsSpan( 4, 4 ), 2 );
		BinaryPrimitives.WriteUInt32LittleEndian( glb.AsSpan( 8, 4 ), (uint)total );
		BinaryPrimitives.WriteUInt32LittleEndian( glb.AsSpan( 12, 4 ), (uint)jsonLength );
		BinaryPrimitives.WriteUInt32LittleEndian( glb.AsSpan( 16, 4 ), JsonChunk );
		jsonBytes.CopyTo( glb, 20 );
		Array.Fill( glb, (byte)0x20, 20 + jsonBytes.Length, jsonLength - jsonBytes.Length );
		var offset = 20 + jsonLength;
		if ( binary is not null )
		{
			BinaryPrimitives.WriteUInt32LittleEndian( glb.AsSpan( offset, 4 ), (uint)binaryLength );
			BinaryPrimitives.WriteUInt32LittleEndian( glb.AsSpan( offset + 4, 4 ), BinChunk );
			binary.CopyTo( glb, offset + 8 );
			offset += 8 + binaryLength;
		}

		if ( includeUnknownChunk )
		{
			BinaryPrimitives.WriteUInt32LittleEndian( glb.AsSpan( offset, 4 ), 4 );
			BinaryPrimitives.WriteUInt32LittleEndian( glb.AsSpan( offset + 4, 4 ), 0x12345678 );
		}
		return glb;
	}
}
