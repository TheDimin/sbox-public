using System;
using System.Buffers.Binary;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;

namespace Sandbox.Tests.Gltf;

/// <summary>
/// Focused glTF 2.0.1 binary and primitive semantics. Accessor-only fixtures
/// exercise the decoder independently of mesh semantic restrictions.
/// https://registry.khronos.org/glTF/specs/2.0/glTF-2.0.html
/// </summary>
[TestClass]
public class GltfCoreDataConformanceTests
{
	// Core 3.6.2.4/5.3: sparse indices can use any unsigned component size;
	// both sparse buffer views and accessor offsets have independent origins.
	[TestMethod]
	[DataRow( 5121, 1 )]
	[DataRow( 5123, 2 )]
	[DataRow( 5125, 4 )]
	public void SparseIndexWidthsAndOffsetsReplaceOnlySelectedElements( int type, int width )
	{
		var bytes = new byte[32];
		bytes[4 + width] = 1;
		bytes[4 + width * 2] = 3;
		BinaryPrimitives.WriteSingleLittleEndian( bytes.AsSpan( 20 ), -2 );
		BinaryPrimitives.WriteSingleLittleEndian( bytes.AsSpan( 24 ), 6 );
		var views = $"[{{\"buffer\":0,\"byteOffset\":4,\"byteLength\":{width * 3}}},{{\"buffer\":0,\"byteOffset\":16,\"byteLength\":12}}]";
		var accessor = $"[{{\"componentType\":5126,\"count\":4,\"type\":\"SCALAR\",\"sparse\":{{\"count\":2,\"indices\":{{\"bufferView\":0,\"byteOffset\":{width},\"componentType\":{type}}},\"values\":{{\"bufferView\":1,\"byteOffset\":4}}}}}}]";
		using var doc = Open( bytes, views, accessor );
		CollectionAssert.AreEqual( new float[] { 0, -2, 0, 6 }, doc.ReadFloatAccessor( 0 ) );
	}

	[TestMethod]
	[DataRow( 5121, 1 )]
	[DataRow( 5123, 2 )]
	[DataRow( 5125, 4 )]
	public void SparseValuesOutsideTheirOwnViewAreRejected( int type, int width )
	{
		var bytes = new byte[32]; bytes[4 + width] = 1; bytes[4 + width * 2] = 3;
		var views = $"[{{\"buffer\":0,\"byteOffset\":4,\"byteLength\":{width * 3}}},{{\"buffer\":0,\"byteOffset\":16,\"byteLength\":11}}]";
		var accessor = $"[{{\"componentType\":5126,\"count\":4,\"type\":\"SCALAR\",\"sparse\":{{\"count\":2,\"indices\":{{\"bufferView\":0,\"byteOffset\":{width},\"componentType\":{type}}},\"values\":{{\"bufferView\":1,\"byteOffset\":4}}}}}}]";
		using var doc = Open( bytes, views, accessor );
		Assert.ThrowsException<GltfException>( () => doc.ReadFloatAccessor( 0 ) );
	}

	// Core matrix columns are aligned separately; final padding may be omitted.
	[TestMethod]
	public void TwoByteMatricesWithByteComponentsSkipEachColumnPadding()
	{
		byte[] bytes = [1, 2, 99, 99, 3, 4, 99, 99, 5, 6, 99, 99, 7, 8];
		using var doc = Open( bytes, "[{\"buffer\":0,\"byteLength\":14}]", "[{\"bufferView\":0,\"componentType\":5121,\"count\":2,\"type\":\"MAT2\"}]" );
		CollectionAssert.AreEqual( new float[] { 1, 2, 3, 4, 5, 6, 7, 8 }, doc.ReadFloatAccessor( 0 ) );
	}

	[TestMethod]
	public void NormalizedSignedByteAndUnsignedBytePreserveIntermediateValues()
	{
		byte[] bytes = [0x80, 0xc0, 0x00, 0x7f, 0x00, 0x40, 0x80, 0xff];
		using var doc = Open( bytes, "[{\"buffer\":0,\"byteLength\":4},{\"buffer\":0,\"byteOffset\":4,\"byteLength\":4}]",
			"[{\"bufferView\":0,\"componentType\":5120,\"normalized\":true,\"count\":4,\"type\":\"SCALAR\"},{\"bufferView\":1,\"componentType\":5121,\"normalized\":true,\"count\":4,\"type\":\"SCALAR\"}]" );
		var signed = doc.ReadFloatAccessor( 0 ); var unsigned = doc.ReadFloatAccessor( 1 );
		Assert.AreEqual( -1f, signed[0] ); Assert.AreEqual( -64f / 127f, signed[1], 0.000001f ); Assert.AreEqual( 0f, signed[2] ); Assert.AreEqual( 1f, signed[3] );
		Assert.AreEqual( 0f, unsigned[0] ); Assert.AreEqual( 64f / 255f, unsigned[1], 0.000001f ); Assert.AreEqual( 128f / 255f, unsigned[2], 0.000001f ); Assert.AreEqual( 1f, unsigned[3] );
	}

	// Core 3.7.2: all three index component types are valid and preserve order.
	[TestMethod]
	[DataRow( 5121, 1 )]
	[DataRow( 5123, 2 )]
	[DataRow( 5125, 4 )]
	public void IndexedTrianglesAcceptAllCoreIndexWidths( int type, int width )
	{
		var bytes = new byte[width * 3]; bytes[0] = 2; bytes[width * 2] = 1;
		var views = $"[{{\"buffer\":0,\"byteLength\":{bytes.Length}}}]";
		var accessors = "[{\"componentType\":5126,\"count\":3,\"type\":\"VEC3\",\"min\":[0,0,0],\"max\":[0,0,0]}," +
			$"{{\"bufferView\":0,\"componentType\":{type},\"count\":3,\"type\":\"SCALAR\"}}]";
		using var doc = Open( bytes, views, accessors, "{\"attributes\":{\"POSITION\":0},\"indices\":1}" );
		CollectionAssert.AreEqual( new uint[] { 2, 0, 1 }, doc.ReadPrimitive( 0, 0 ).Indices );
	}

	[TestMethod]
	public void ByteRestartIndexIsRejectedEvenWhenInsideVertexRange()
	{
		byte[] bytes = [0, 1, 255];
		const string accessors = "[{\"componentType\":5126,\"count\":256,\"type\":\"VEC3\",\"min\":[0,0,0],\"max\":[0,0,0]},{\"bufferView\":0,\"componentType\":5121,\"count\":3,\"type\":\"SCALAR\"}]";
		using var doc = Open( bytes, "[{\"buffer\":0,\"byteLength\":3}]", accessors, "{\"attributes\":{\"POSITION\":0},\"indices\":1}" );
		Assert.ThrowsException<GltfException>( () => doc.ReadPrimitive( 0, 0 ) );
	}

	[TestMethod]
	public void UnsignedShortRestartIndexIsRejectedEvenWhenInsideVertexRange()
	{
		byte[] bytes = [0, 0, 1, 0, 255, 255];
		const string accessors = "[{\"componentType\":5126,\"count\":65536,\"type\":\"VEC3\",\"min\":[0,0,0],\"max\":[0,0,0]},{\"bufferView\":0,\"componentType\":5123,\"count\":3,\"type\":\"SCALAR\"}]";
		using var doc = Open( bytes, "[{\"buffer\":0,\"byteLength\":6}]", accessors, "{\"attributes\":{\"POSITION\":0},\"indices\":1}" );
		Assert.ThrowsException<GltfException>( () => doc.ReadPrimitive( 0, 0 ) );
	}

	[TestMethod]
	[DataRow( "VEC2", 5121, false )]
	[DataRow( "SCALAR", 5121, true )]
	[DataRow( "SCALAR", 5122, false )]
	[DataRow( "SCALAR", 5126, false )]
	public void PrimitiveIndicesRequireAnUnnormalizedUnsignedScalar( string shape, int type, bool normalized )
	{
		var accessors = "[{\"componentType\":5126,\"count\":3,\"type\":\"VEC3\",\"min\":[0,0,0],\"max\":[0,0,0]}," +
			$"{{\"componentType\":{type},\"normalized\":{(normalized ? "true" : "false")},\"count\":3,\"type\":\"{shape}\"}}]";
		using var doc = Open( null, null, accessors, "{\"attributes\":{\"POSITION\":0},\"indices\":1}" );
		Assert.ThrowsException<GltfException>( () => doc.ReadPrimitive( 0, 0 ) );
	}

	[TestMethod]
	[DataRow( 0, 1 )]
	[DataRow( 1, 2 )]
	[DataRow( 2, 2 )]
	[DataRow( 3, 2 )]
	[DataRow( 4, 3 )]
	[DataRow( 5, 3 )]
	[DataRow( 6, 3 )]
	public void EachCoreTopologyAcceptsItsMinimumImplicitVertexCount( int mode, int count )
	{
		using var doc = Primitive( mode, count );
		var primitive = doc.ReadPrimitive( 0, 0 );
		Assert.AreEqual( mode, primitive.Mode );
		CollectionAssert.AreEqual( Enumerable.Range( 0, count ).Select( x => (uint)x ).ToArray(), primitive.Indices );
	}

	[TestMethod]
	[DataRow( 1, 1 )]
	[DataRow( 1, 3 )]
	[DataRow( 2, 1 )]
	[DataRow( 3, 1 )]
	[DataRow( 4, 1 )]
	[DataRow( 4, 4 )]
	[DataRow( 5, 2 )]
	[DataRow( 6, 2 )]
	public void CoreTopologyRejectsInsufficientOrIncompleteImplicitVertices( int mode, int count )
	{
		using var doc = Primitive( mode, count );
		Assert.ThrowsException<GltfException>( () => doc.ReadPrimitive( 0, 0 ) );
	}

	// These are metadata-only accessor-layout checks; actual semantic values are
	// covered elsewhere and empty-base accessors here are intentionally isolated.
	[TestMethod]
	[DataRow( "TEXCOORD_0", "VEC2", 5126, false )]
	[DataRow( "TEXCOORD_0", "VEC2", 5121, true )]
	[DataRow( "TEXCOORD_0", "VEC2", 5123, true )]
	[DataRow( "COLOR_0", "VEC3", 5126, false )]
	[DataRow( "COLOR_0", "VEC3", 5121, true )]
	[DataRow( "COLOR_0", "VEC3", 5123, true )]
	[DataRow( "COLOR_0", "VEC4", 5126, false )]
	[DataRow( "COLOR_0", "VEC4", 5121, true )]
	[DataRow( "COLOR_0", "VEC4", 5123, true )]
	[DataRow( "JOINTS_0", "VEC4", 5121, false )]
	[DataRow( "JOINTS_0", "VEC4", 5123, false )]
	[DataRow( "WEIGHTS_0", "VEC4", 5126, false )]
	[DataRow( "WEIGHTS_0", "VEC4", 5121, true )]
	[DataRow( "WEIGHTS_0", "VEC4", 5123, true )]
	public void CoreAttributeLayoutFormatsAreAccepted( string semantic, string shape, int type, bool normalized )
	{
		using var doc = AttributeLayout( semantic, shape, type, normalized );
		Assert.AreEqual( 1, doc.ReadPrimitive( 0, 0 ).Attributes[semantic] );
		Assert.AreEqual( type, doc.DescribePrimitiveUpload( 0, 0 ).Attributes[semantic].ComponentType );
	}

	[TestMethod]
	[DataRow( "COLOR_0", "VEC3", 5121, false )]
	[DataRow( "COLOR_0", "VEC4", 5123, false )]
	[DataRow( "TEXCOORD_0", "VEC2", 5121, false )]
	[DataRow( "WEIGHTS_0", "VEC4", 5123, false )]
	[DataRow( "JOINTS_0", "VEC4", 5126, false )]
	[DataRow( "_CUSTOM", "SCALAR", 5125, false )]
	public void InvalidCoreAttributeComponentFormatsAreRejected( string semantic, string shape, int type, bool normalized )
	{
		using var doc = AttributeLayout( semantic, shape, type, normalized );
		Assert.ThrowsException<GltfException>( () => doc.ReadPrimitive( 0, 0 ) );
	}

	[TestMethod]
	[DataRow( "COLOR_00", "VEC3" )]
	[DataRow( "COLOR_1", "VEC3" )]
	[DataRow( "TEXCOORD_01", "VEC2" )]
	[DataRow( "TEXCOORD_1", "VEC2" )]
	public void InvalidIndexedSemanticsAreRejectedByBothPrimitiveAndUploadPaths( string semantic, string shape )
	{
		using var doc = AttributeLayout( semantic, shape, 5126, false );
		Assert.ThrowsException<GltfException>( () => doc.ReadPrimitive( 0, 0 ) );
		Assert.ThrowsException<GltfException>( () => doc.DescribePrimitiveUpload( 0, 0 ) );
		Assert.ThrowsException<GltfException>( () => doc.MaterializePrimitiveUpload( 0, 0 ) );
	}

	[TestMethod]
	[DataRow( "JOINTS_0", 5121, false )]
	[DataRow( "WEIGHTS_0", 5126, false )]
	public void SkinningLayoutStillRequiresItsMatchingAttribute( string semantic, int type, bool normalized )
	{
		using var doc = AttributeLayout( semantic, "VEC4", type, normalized, includeSkinningPartner: false );
		var error = Assert.ThrowsException<GltfException>( () => doc.ReadPrimitive( 0, 0 ) );
		StringAssert.Contains( error.Message, semantic == "JOINTS_0" ? "WEIGHTS_0" : "JOINTS_0" );
		Assert.ThrowsException<GltfException>( () => doc.DescribePrimitiveUpload( 0, 0 ) );
	}

	[TestMethod]
	[DataRow( "JOINTS_0", "VEC3", 5121, false )]
	[DataRow( "JOINTS_0", "VEC4", 5121, true )]
	[DataRow( "WEIGHTS_0", "VEC3", 5126, false )]
	[DataRow( "WEIGHTS_0", "VEC4", 5120, true )]
	public void InvalidSkinningFormatFailsEvenWithAValidPartner( string semantic, string shape, int type, bool normalized )
	{
		using var doc = AttributeLayout( semantic, shape, type, normalized );
		var error = Assert.ThrowsException<GltfException>( () => doc.ReadPrimitive( 0, 0 ) );
		StringAssert.Contains( error.Message, $"Invalid accessor layout for '{semantic}'" );
		Assert.ThrowsException<GltfException>( () => doc.DescribePrimitiveUpload( 0, 0 ) );
	}

	[TestMethod]
	public void UnsignedIntIndicesRemainLegalWhenUnsignedIntVertexAttributesAreRejected()
	{
		var bytes = new byte[12];
		BinaryPrimitives.WriteUInt32LittleEndian( bytes.AsSpan( 0 ), 2 );
		BinaryPrimitives.WriteUInt32LittleEndian( bytes.AsSpan( 4 ), 0 );
		BinaryPrimitives.WriteUInt32LittleEndian( bytes.AsSpan( 8 ), 1 );
		const string views = "[{\"buffer\":0,\"byteLength\":12}]";
		const string accessors = "[{\"componentType\":5126,\"count\":3,\"type\":\"VEC3\",\"min\":[0,0,0],\"max\":[0,0,0]}," +
			"{\"bufferView\":0,\"componentType\":5125,\"count\":3,\"type\":\"SCALAR\"}]";
		using var indices = Open( bytes, views, accessors, "{\"attributes\":{\"POSITION\":0},\"indices\":1}" );
		CollectionAssert.AreEqual( new uint[] { 2, 0, 1 }, indices.ReadPrimitive( 0, 0 ).Indices );
		Assert.AreEqual( 5125, indices.DescribePrimitiveUpload( 0, 0 ).Indices.ComponentType );
		CollectionAssert.AreEqual( bytes, indices.MaterializePrimitiveUpload( 0, 0 ).Indices.Storage.Bytes.ToArray() );

		using var vertex = Open( bytes, views, accessors, "{\"attributes\":{\"POSITION\":0,\"_CUSTOM\":1}}" );
		Assert.ThrowsException<GltfException>( () => vertex.ReadPrimitive( 0, 0 ) );
		Assert.ThrowsException<GltfException>( () => vertex.DescribePrimitiveUpload( 0, 0 ) );
		// The general decoder does not acquire a mesh-role restriction.
		CollectionAssert.AreEqual( new uint[] { 2, 0, 1 }, vertex.ReadIndexAccessor( 1 ) );
	}

	private static GltfDocument AttributeLayout( string semantic, string shape, int type, bool normalized, bool includeSkinningPartner = true )
	{
		var accessors = "[{\"componentType\":5126,\"count\":3,\"type\":\"VEC3\",\"min\":[0,0,0],\"max\":[0,0,0]}," +
			$"{{\"componentType\":{type},\"normalized\":{(normalized ? "true" : "false")},\"count\":3,\"type\":\"{shape}\"}}";
		var attributes = $"\"POSITION\":0,\"{semantic}\":1";
		// This suite isolates attribute format validation; implicit-zero data is
		// intentional. Even metadata-only JOINTS/WEIGHTS fixtures require a pair.
		if ( includeSkinningPartner && semantic is ("JOINTS_0" or "WEIGHTS_0") )
		{
			var partner = semantic == "JOINTS_0" ? "WEIGHTS_0" : "JOINTS_0";
			var partnerType = semantic == "JOINTS_0" ? 5126 : 5121;
			accessors += $",{{\"componentType\":{partnerType},\"count\":3,\"type\":\"VEC4\"}}";
			attributes += $",\"{partner}\":2";
		}
		return Open( null, null, accessors + "]", "{\"attributes\":{" + attributes + "}}" );
	}

	private static GltfDocument Primitive( int mode, int count )
	{
		var accessors = $"[{{\"componentType\":5126,\"count\":{count},\"type\":\"VEC3\",\"min\":[0,0,0],\"max\":[0,0,0]}}]";
		return Open( null, null, accessors, $"{{\"attributes\":{{\"POSITION\":0}},\"mode\":{mode}}}" );
	}

	private static GltfDocument Open( byte[] bytes, string views, string accessors, string primitive = null )
	{
		var buffer = bytes is null ? "" : $",\"buffers\":[{{\"uri\":\"data:application/octet-stream;base64,{Convert.ToBase64String( bytes )}\",\"byteLength\":{bytes.Length}}}],\"bufferViews\":{views}";
		var mesh = primitive is null ? "" : ",\"meshes\":[{\"primitives\":[" + primitive + "]}]";
		var json = "{\"asset\":{\"version\":\"2.0\"},\"accessors\":" + accessors + buffer + mesh + "}";
		return GltfReader.Read( Encoding.UTF8.GetBytes( json ) );
	}
}
