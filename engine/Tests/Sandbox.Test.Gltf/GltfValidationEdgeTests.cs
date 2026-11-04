using System;
using System.Buffers.Binary;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;
using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace Sandbox.Tests.Gltf;

/// <summary>Low-level container and accessor cases independent of mesh attribute semantics.</summary>
[TestClass]
public class GltfValidationEdgeTests
{
	[TestMethod]
	public void AccessorWithoutBufferViewOrSparseDataIsZeroInitialized()
	{
		const string json = """
			{"asset":{"version":"2.0"},"accessors":[{"componentType":5126,"count":2,"type":"VEC3"}]}
			""";
		using var document = GltfReader.Read( Encoding.UTF8.GetBytes( json ) );
		CollectionAssert.AreEqual( new float[] { 0, 0, 0, 0, 0, 0 }, document.ReadFloatAccessor( 0 ) );
	}

	[TestMethod]
	public void AccessorCountMustBePositive()
	{
		const string json = """
			{"asset":{"version":"2.0"},"accessors":[{"componentType":5126,"count":0,"type":"SCALAR"}]}
			""";
		Assert.ThrowsException<GltfException>( () =>
		{
			using var document = GltfReader.Read( Encoding.UTF8.GetBytes( json ) );
			document.ReadFloatAccessor( 0 );
		} );
	}

	[TestMethod]
	public void BufferDataUriMayUsePercentEncodingWithoutBase64()
	{
		const string json = """
			{"asset":{"version":"2.0"},"buffers":[{"uri":"data:application/octet-stream,%00%01%FE%20","byteLength":4}],
			 "bufferViews":[{"buffer":0,"byteLength":4}],
			 "accessors":[{"bufferView":0,"componentType":5121,"count":4,"type":"SCALAR"}]}
			""";
		using var document = GltfReader.Read( Encoding.UTF8.GetBytes( json ), _ => throw new InvalidOperationException( "Data URI must not use the external resolver." ) );
		CollectionAssert.AreEqual( new float[] { 0, 1, 254, 32 }, document.ReadFloatAccessor( 0 ) );
	}

	[TestMethod]
	public void ExternalBufferMayContainBytesBeyondItsDeclaredLength()
	{
		const string json = """
			{"asset":{"version":"2.0"},"buffers":[{"uri":"shared.bin","byteLength":3}],
			 "bufferViews":[{"buffer":0,"byteLength":3}],
			 "accessors":[{"bufferView":0,"componentType":5121,"count":3,"type":"SCALAR"}]}
			""";
		using var document = GltfReader.Read( Encoding.UTF8.GetBytes( json ), _ => new byte[] { 1, 2, 3, 99 } );
		CollectionAssert.AreEqual( new float[] { 1, 2, 3 }, document.ReadFloatAccessor( 0 ) );
	}

	[TestMethod]
	public void SignedNormalizedShortClampsBothRepresentationsOfNegativeOne()
	{
		var bytes = new byte[6];
		BinaryPrimitives.WriteInt16LittleEndian( bytes.AsSpan( 0, 2 ), short.MinValue );
		BinaryPrimitives.WriteInt16LittleEndian( bytes.AsSpan( 2, 2 ), -32767 );
		BinaryPrimitives.WriteInt16LittleEndian( bytes.AsSpan( 4, 2 ), short.MaxValue );
		using var document = OpenEmbedded( bytes,
			"[{\"buffer\":0,\"byteLength\":6}]",
			"[{\"bufferView\":0,\"componentType\":5122,\"normalized\":true,\"count\":3,\"type\":\"SCALAR\"}]" );
		CollectionAssert.AreEqual( new float[] { -1, -1, 1 }, document.ReadFloatAccessor( 0 ) );
	}

	[TestMethod]
	public void ShortMatrixSkipsColumnPaddingAndMayOmitTrailingPadding()
	{
		var bytes = new byte[22];
		for ( var column = 0; column < 3; column++ )
		{
			for ( var row = 0; row < 3; row++ )
			{
				BinaryPrimitives.WriteInt16LittleEndian( bytes.AsSpan( column * 8 + row * 2, 2 ), (short)(column * 3 + row + 1) );
			}
		}
		using var document = OpenEmbedded( bytes,
			"[{\"buffer\":0,\"byteLength\":22}]",
			"[{\"bufferView\":0,\"componentType\":5122,\"count\":1,\"type\":\"MAT3\"}]" );
		CollectionAssert.AreEqual( new float[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }, document.ReadFloatAccessor( 0 ) );
	}

	[TestMethod]
	public void FloatAccessorCannotRequestIntegerNormalization()
	{
		Assert.ThrowsException<GltfException>( () =>
		{
			using var document = OpenEmbedded( new byte[4],
				"[{\"buffer\":0,\"byteLength\":4}]",
				"[{\"bufferView\":0,\"componentType\":5126,\"normalized\":true,\"count\":1,\"type\":\"SCALAR\"}]" );
			document.ReadFloatAccessor( 0 );
		} );
	}

	[TestMethod]
	public void ByteOffsetIsInvalidWhenBaseBufferViewIsAbsent()
	{
		const string json = """
			{"asset":{"version":"2.0"},"accessors":[{"byteOffset":0,"componentType":5126,"count":1,"type":"SCALAR"}]}
			""";
		Assert.ThrowsException<GltfException>( () =>
		{
			using var document = GltfReader.Read( Encoding.UTF8.GetBytes( json ) );
			document.ReadFloatAccessor( 0 );
		} );
	}

	[TestMethod]
	public void AccessorRejectsTotalOffsetMisalignedToComponentSize()
	{
		Assert.ThrowsException<GltfException>( () =>
		{
			using var document = OpenEmbedded( new byte[3],
				"[{\"buffer\":0,\"byteOffset\":1,\"byteLength\":2}]",
				"[{\"bufferView\":0,\"componentType\":5123,\"count\":1,\"type\":\"SCALAR\"}]" );
			document.ReadFloatAccessor( 0 );
		} );
	}

	[TestMethod]
	public void StrideCannotBeSmallerThanTheAccessorElement()
	{
		Assert.ThrowsException<GltfException>( () =>
		{
			using var document = OpenEmbedded( new byte[24],
				"[{\"buffer\":0,\"byteLength\":24,\"byteStride\":8}]",
				"[{\"bufferView\":0,\"componentType\":5126,\"count\":2,\"type\":\"VEC3\"}]" );
			document.ReadFloatAccessor( 0 );
		} );
	}

	[TestMethod]
	public void SparseCountCannotExceedBaseAccessorCount()
	{
		var bytes = new byte[12];
		bytes[1] = 1;
		Assert.ThrowsException<GltfException>( () =>
		{
			using var document = OpenEmbedded( bytes,
				"[{\"buffer\":0,\"byteLength\":2},{\"buffer\":0,\"byteOffset\":4,\"byteLength\":8}]",
				"[{\"componentType\":5126,\"count\":1,\"type\":\"SCALAR\",\"sparse\":{\"count\":2,\"indices\":{\"bufferView\":0,\"componentType\":5121},\"values\":{\"bufferView\":1}}}]" );
			document.ReadFloatAccessor( 0 );
		} );
	}

	[TestMethod]
	public void SparseIndexBufferViewCannotSpecifyGpuTarget()
	{
		Assert.ThrowsException<GltfException>( () =>
		{
			using var document = OpenEmbedded( new byte[8],
				"[{\"buffer\":0,\"byteLength\":1,\"target\":34962},{\"buffer\":0,\"byteOffset\":4,\"byteLength\":4}]",
				"[{\"componentType\":5126,\"count\":1,\"type\":\"SCALAR\",\"sparse\":{\"count\":1,\"indices\":{\"bufferView\":0,\"componentType\":5121},\"values\":{\"bufferView\":1}}}]" );
			document.ReadFloatAccessor( 0 );
		} );
	}

	[TestMethod]
	public void GlbAcceptsThreeBytesOfBinaryPadding()
	{
		const string json = """
			{"asset":{"version":"2.0"},"buffers":[{"byteLength":1}],"bufferViews":[{"buffer":0,"byteLength":1}],"accessors":[{"bufferView":0,"componentType":5121,"count":1,"type":"SCALAR"}]}
			""";
		using var document = GltfReader.Read( BuildGlb( json, new byte[] { 42, 0, 0, 0 } ) );
		CollectionAssert.AreEqual( new float[] { 42 }, document.ReadFloatAccessor( 0 ) );
	}

	[TestMethod]
	public void GlbMayCombineItsBinaryChunkWithExternalBuffers()
	{
		const string json = """
			{"asset":{"version":"2.0"},"buffers":[{"byteLength":4},{"uri":"other.bin","byteLength":4}],
			 "bufferViews":[{"buffer":0,"byteLength":4},{"buffer":1,"byteLength":4}],
			 "accessors":[{"bufferView":0,"componentType":5126,"count":1,"type":"SCALAR"},
			              {"bufferView":1,"componentType":5126,"count":1,"type":"SCALAR"}]}
			""";
		var inGlb = new byte[4];
		var external = new byte[4];
		BinaryPrimitives.WriteSingleLittleEndian( inGlb, 1.0f );
		BinaryPrimitives.WriteSingleLittleEndian( external, 2.5f );
		string resolved = null;
		using var document = GltfReader.Read( BuildGlb( json, inGlb ), uri =>
		{
			resolved = uri;
			return external;
		} );

		CollectionAssert.AreEqual( new[] { 1.0f }, document.ReadFloatAccessor( 0 ) );
		CollectionAssert.AreEqual( new[] { 2.5f }, document.ReadFloatAccessor( 1 ) );
		Assert.AreEqual( "other.bin", resolved );
	}

	[TestMethod]
	public void GlbRejectsMoreThanThreeBytesBeyondBufferLength()
	{
		const string json = """
			{"asset":{"version":"2.0"},"buffers":[{"byteLength":4}],"bufferViews":[{"buffer":0,"byteLength":4}],"accessors":[{"bufferView":0,"componentType":5121,"count":4,"type":"SCALAR"}]}
			""";
		Assert.ThrowsException<GltfException>( () =>
		{
			using var document = GltfReader.Read( BuildGlb( json, new byte[8] ) );
			document.ReadFloatAccessor( 0 );
		} );
	}

	private static GltfDocument OpenEmbedded( byte[] bytes, string views, string accessors )
	{
		var encoded = Convert.ToBase64String( bytes );
		var json = $$"""
			{"asset":{"version":"2.0"},"buffers":[{"uri":"data:application/gltf-buffer;base64,{{encoded}}","byteLength":{{bytes.Length}}}],"bufferViews":{{views}},"accessors":{{accessors}}}
			""";
		return GltfReader.Read( Encoding.UTF8.GetBytes( json ) );
	}

	private static byte[] BuildGlb( string json, byte[] binary )
	{
		var jsonBytes = Encoding.UTF8.GetBytes( json );
		var jsonLength = (jsonBytes.Length + 3) & ~3;
		var result = new byte[12 + 8 + jsonLength + 8 + binary.Length];
		BinaryPrimitives.WriteUInt32LittleEndian( result.AsSpan( 0, 4 ), 0x46546C67 );
		BinaryPrimitives.WriteUInt32LittleEndian( result.AsSpan( 4, 4 ), 2 );
		BinaryPrimitives.WriteUInt32LittleEndian( result.AsSpan( 8, 4 ), (uint)result.Length );
		BinaryPrimitives.WriteUInt32LittleEndian( result.AsSpan( 12, 4 ), (uint)jsonLength );
		BinaryPrimitives.WriteUInt32LittleEndian( result.AsSpan( 16, 4 ), 0x4E4F534A );
		jsonBytes.CopyTo( result, 20 );
		Array.Fill( result, (byte)' ', 20 + jsonBytes.Length, jsonLength - jsonBytes.Length );
		var binaryHeader = 20 + jsonLength;
		BinaryPrimitives.WriteUInt32LittleEndian( result.AsSpan( binaryHeader, 4 ), (uint)binary.Length );
		BinaryPrimitives.WriteUInt32LittleEndian( result.AsSpan( binaryHeader + 4, 4 ), 0x004E4942 );
		binary.CopyTo( result, binaryHeader + 8 );
		return result;
	}
}
