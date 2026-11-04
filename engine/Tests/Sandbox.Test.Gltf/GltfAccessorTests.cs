using System;
using System.Buffers.Binary;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;
using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace Sandbox.Tests.Gltf;

[TestClass]
public class GltfAccessorTests
{
	[TestMethod]
	public void ReadsInterleavedVectorsUsingViewAndAccessorOffsets()
	{
		var bytes = new byte[36];
		WriteFloat( bytes, 8, 1 );
		WriteFloat( bytes, 12, 2 );
		WriteFloat( bytes, 16, 3 );
		WriteFloat( bytes, 24, 4 );
		WriteFloat( bytes, 28, 5 );
		WriteFloat( bytes, 32, 6 );
		using var document = ReadEmbedded( bytes,
			"[{\"buffer\":0,\"byteOffset\":4,\"byteLength\":32,\"byteStride\":16}]",
			"[{\"bufferView\":0,\"byteOffset\":4,\"componentType\":5126,\"count\":2,\"type\":\"VEC3\"}]" );

		CollectionAssert.AreEqual( new float[] { 1, 2, 3, 4, 5, 6 }, document.ReadFloatAccessor( 0 ) );
	}

	[TestMethod]
	public void SignedNormalizedByteClampsNegativeMinimum()
	{
		using var document = ReadEmbedded( new byte[] { 128, 129, 0, 127 },
			"[{\"buffer\":0,\"byteLength\":4}]",
			"[{\"bufferView\":0,\"componentType\":5120,\"normalized\":true,\"count\":1,\"type\":\"VEC4\"}]" );

		CollectionAssert.AreEqual( new float[] { -1, -1, 0, 1 }, document.ReadFloatAccessor( 0 ) );
	}

	[TestMethod]
	public void UnsignedShortNormalizedDecodesFullRange()
	{
		var bytes = new byte[6];
		BinaryPrimitives.WriteUInt16LittleEndian( bytes.AsSpan( 0, 2 ), 0 );
		BinaryPrimitives.WriteUInt16LittleEndian( bytes.AsSpan( 2, 2 ), 32768 );
		BinaryPrimitives.WriteUInt16LittleEndian( bytes.AsSpan( 4, 2 ), 65535 );
		using var document = ReadEmbedded( bytes,
			"[{\"buffer\":0,\"byteLength\":6}]",
			"[{\"bufferView\":0,\"componentType\":5123,\"normalized\":true,\"count\":3,\"type\":\"SCALAR\"}]" );

		var values = document.ReadFloatAccessor( 0 );
		Assert.AreEqual( 3, values.Length );
		Assert.AreEqual( 0.0f, values[0], 0.000001f );
		Assert.AreEqual( 32768.0f / 65535.0f, values[1], 0.000001f );
		Assert.AreEqual( 1.0f, values[2], 0.000001f );
	}

	[TestMethod]
	public void SparseAccessorOverridesZeroInitializedBase()
	{
		var bytes = new byte[12];
		bytes[0] = 1;
		bytes[1] = 3;
		WriteFloat( bytes, 4, 10 );
		WriteFloat( bytes, 8, 20 );
		using var document = ReadEmbedded( bytes,
			"[{\"buffer\":0,\"byteLength\":2},{\"buffer\":0,\"byteOffset\":4,\"byteLength\":8}]",
			"[{\"componentType\":5126,\"count\":5,\"type\":\"SCALAR\",\"sparse\":{\"count\":2,\"indices\":{\"bufferView\":0,\"componentType\":5121},\"values\":{\"bufferView\":1}}}]" );

		CollectionAssert.AreEqual( new float[] { 0, 10, 0, 20, 0 }, document.ReadFloatAccessor( 0 ) );
	}

	[TestMethod]
	public void SparseAccessorOverridesExistingBaseValues()
	{
		var bytes = new byte[20];
		WriteFloat( bytes, 0, 1 );
		WriteFloat( bytes, 4, 2 );
		WriteFloat( bytes, 8, 3 );
		bytes[12] = 1;
		WriteFloat( bytes, 16, 99 );
		using var document = ReadEmbedded( bytes,
			"[{\"buffer\":0,\"byteLength\":12},{\"buffer\":0,\"byteOffset\":12,\"byteLength\":1},{\"buffer\":0,\"byteOffset\":16,\"byteLength\":4}]",
			"[{\"bufferView\":0,\"componentType\":5126,\"count\":3,\"type\":\"SCALAR\",\"sparse\":{\"count\":1,\"indices\":{\"bufferView\":1,\"componentType\":5121},\"values\":{\"bufferView\":2}}}]" );

		CollectionAssert.AreEqual( new float[] { 1, 99, 3 }, document.ReadFloatAccessor( 0 ) );
	}

	[TestMethod]
	public void ByteMatrixSkipsEachColumnPadding()
	{
		var bytes = new byte[] { 1, 2, 3, 200, 4, 5, 6, 201, 7, 8, 9, 202 };
		using var document = ReadEmbedded( bytes,
			"[{\"buffer\":0,\"byteLength\":12}]",
			"[{\"bufferView\":0,\"componentType\":5121,\"count\":1,\"type\":\"MAT3\"}]" );

		CollectionAssert.AreEqual( new float[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }, document.ReadFloatAccessor( 0 ) );
	}

	[TestMethod]
	public void LastMatrixMayOmitFinalColumnPadding()
	{
		// Each three-byte column starts on a four-byte boundary. The final
		// column has no following data and need not store its trailing pad byte.
		var bytes = new byte[] { 1, 2, 3, 0, 4, 5, 6, 0, 7, 8, 9, 0,
			10, 11, 12, 0, 13, 14, 15, 0, 16, 17, 18 };
		using var document = ReadEmbedded( bytes,
			"[{\"buffer\":0,\"byteLength\":23}]",
			"[{\"bufferView\":0,\"componentType\":5121,\"count\":2,\"type\":\"MAT3\"}]" );

		CollectionAssert.AreEqual( new float[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18 }, document.ReadFloatAccessor( 0 ) );
	}

	[TestMethod]
	public void UnsignedIntIndicesKeepValuesBeyondSixteenBits()
	{
		// An isolated binary-decoder test, not a complete conformant mesh. Large
		// indices test integer precision without allocating billions of vertices.
		var bytes = new byte[12];
		BinaryPrimitives.WriteUInt32LittleEndian( bytes.AsSpan( 0, 4 ), 0 );
		BinaryPrimitives.WriteUInt32LittleEndian( bytes.AsSpan( 4, 4 ), 70000 );
		BinaryPrimitives.WriteUInt32LittleEndian( bytes.AsSpan( 8, 4 ), 4000000000 );
		using var document = ReadEmbedded( bytes,
			"[{\"buffer\":0,\"byteLength\":12}]",
			"[{\"bufferView\":0,\"componentType\":5125,\"count\":3,\"type\":\"SCALAR\"}]" );

		CollectionAssert.AreEqual( new uint[] { 0, 70000, 4000000000 }, document.ReadIndexAccessor( 0 ) );
	}

	[TestMethod]
	public void ExternalBufferUsesResolverAndReadsAccessor()
	{
		const string json = "{\"asset\":{\"version\":\"2.0\"},\"buffers\":[{\"uri\":\"mesh.bin\",\"byteLength\":4}],\"bufferViews\":[{\"buffer\":0,\"byteLength\":4}],\"accessors\":[{\"bufferView\":0,\"componentType\":5126,\"count\":1,\"type\":\"SCALAR\"}]}";
		var bytes = new byte[4];
		WriteFloat( bytes, 0, 2.5f );
		string requested = null;
		using var document = GltfReader.Read( Encoding.UTF8.GetBytes( json ), uri =>
		{
			requested = uri;
			return bytes;
		} );

		CollectionAssert.AreEqual( new[] { 2.5f }, document.ReadFloatAccessor( 0 ) );
		Assert.AreEqual( "mesh.bin", requested );
	}

	[TestMethod]
	public void AccessorCannotReadBeyondItsBufferViewEvenWhenBufferHasBytes()
	{
		Assert.ThrowsException<GltfException>( () =>
		{
			using var document = ReadEmbedded( new byte[12],
				"[{\"buffer\":0,\"byteLength\":4}]",
				"[{\"bufferView\":0,\"componentType\":5126,\"count\":2,\"type\":\"SCALAR\"}]" );
			document.ReadFloatAccessor( 0 );
		} );
	}

	[TestMethod]
	public void BufferViewCannotReadBeyondDeclaredBufferLength()
	{
		const string json = "{\"asset\":{\"version\":\"2.0\"},\"buffers\":[{\"uri\":\"mesh.bin\",\"byteLength\":4}],\"bufferViews\":[{\"buffer\":0,\"byteOffset\":4,\"byteLength\":4}],\"accessors\":[{\"bufferView\":0,\"componentType\":5126,\"count\":1,\"type\":\"SCALAR\"}]}";
		Assert.ThrowsException<GltfException>( () =>
		{
			using var document = GltfReader.Read( Encoding.UTF8.GetBytes( json ), _ => new byte[8] );
			document.ReadFloatAccessor( 0 );
		} );
	}

	[TestMethod]
	public void TruncatedExternalBufferFailsInsteadOfReturningPartialData()
	{
		const string json = "{\"asset\":{\"version\":\"2.0\"},\"buffers\":[{\"uri\":\"mesh.bin\",\"byteLength\":8}],\"bufferViews\":[{\"buffer\":0,\"byteLength\":8}],\"accessors\":[{\"bufferView\":0,\"componentType\":5126,\"count\":2,\"type\":\"SCALAR\"}]}";
		Assert.ThrowsException<GltfException>( () =>
		{
			using var document = GltfReader.Read( Encoding.UTF8.GetBytes( json ), _ => new byte[4] );
			document.ReadFloatAccessor( 0 );
		} );
	}

	[TestMethod]
	public void IndexAccessorMustUseAnUnsignedScalarComponent()
	{
		Assert.ThrowsException<GltfException>( () =>
		{
			using var document = ReadEmbedded( new byte[4],
				"[{\"buffer\":0,\"byteLength\":4}]",
				"[{\"bufferView\":0,\"componentType\":5126,\"count\":1,\"type\":\"SCALAR\"}]" );
			document.ReadIndexAccessor( 0 );
		} );
	}

	[TestMethod]
	[DataRow( (byte)1, (byte)1 )]
	[DataRow( (byte)1, (byte)5 )]
	public void SparseIndicesMustBeStrictlyIncreasingAndInBounds( byte first, byte second )
	{
		var bytes = new byte[12];
		bytes[0] = first;
		bytes[1] = second;
		WriteFloat( bytes, 4, 10 );
		WriteFloat( bytes, 8, 20 );
		Assert.ThrowsException<GltfException>( () =>
		{
			using var document = ReadEmbedded( bytes,
				"[{\"buffer\":0,\"byteLength\":2},{\"buffer\":0,\"byteOffset\":4,\"byteLength\":8}]",
				"[{\"componentType\":5126,\"count\":5,\"type\":\"SCALAR\",\"sparse\":{\"count\":2,\"indices\":{\"bufferView\":0,\"componentType\":5121},\"values\":{\"bufferView\":1}}}]" );
			document.ReadFloatAccessor( 0 );
		} );
	}

	private static GltfDocument ReadEmbedded( byte[] bytes, string bufferViews, string accessors )
	{
		var dataUri = Convert.ToBase64String( bytes );
		var json = $$"""
			{"asset":{"version":"2.0"},"buffers":[{"uri":"data:application/octet-stream;base64,{{dataUri}}","byteLength":{{bytes.Length}}}],"bufferViews":{{bufferViews}},"accessors":{{accessors}}}
			""";
		return GltfReader.Read( Encoding.UTF8.GetBytes( json ) );
	}

	private static void WriteFloat( byte[] destination, int offset, float value )
	{
		BinaryPrimitives.WriteSingleLittleEndian( destination.AsSpan( offset, 4 ), value );
	}
}
