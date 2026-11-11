using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;

namespace Sandbox.Tests.Gltf;

[TestClass]
public sealed class GltfDracoPrimitiveDataTests
{
	// Actual native-decoder preflight. Khronos Sample Assets Box, Cesium,
	// CC-BY-4.0; attribution and license are in Fixtures/KhronosBox.
	private const string BoxDraco = "RFJBQ08CAgEBAAAACAwBCwAAA19bCgEBEFUEXOONRgL/AAAAAQABAAkDAAECAQEJAwAAAwEBAQADAwEwARADACSWEwokBAAAAAD/BwAAAAAAvwAAAL8AAAC/AACAPwsGAwEBAQEBQAEA/wAAAH8AAAD/AqFBCAAA";

	[TestMethod]
	public void NativeBoxDecodeSuppliesRawFloatAttributesAndDecodedTopology()
	{
		using var document = Document(
			"{\"componentType\":5126,\"count\":24,\"type\":\"VEC3\",\"bufferView\":999,\"byteOffset\":999},"
			+ "{\"componentType\":5123,\"count\":36,\"type\":\"SCALAR\",\"bufferView\":999}",
			"{\"attributes\":{\"POSITION\":0},\"indices\":1,\"extensions\":{\"KHR_draco_mesh_compression\":{\"bufferView\":0,\"attributes\":{\"POSITION\":1}}}}",
			Convert.FromBase64String( BoxDraco ) );
		var data = document.ReadDracoPrimitiveData( 0, 0 );
		Assert.AreEqual( 24, data.VertexCount );
		Assert.AreEqual( 4, data.SourceMode ); Assert.AreEqual( 4, data.Mode );
		var position = data.Attributes["POSITION"];
		Assert.AreEqual( 0, position.AccessorIndex ); Assert.AreEqual( 24, position.Count );
		Assert.AreEqual( 5126, position.ComponentType ); Assert.AreEqual( 3, position.Components );
		Assert.IsFalse( position.Normalized ); Assert.AreEqual( 12, position.ByteStride );
		Assert.AreEqual( 288, position.Bytes.Length );
		Assert.AreEqual( 1, data.Indices.AccessorIndex ); Assert.AreEqual( 36, data.Indices.Count );
		Assert.AreEqual( 5123, data.Indices.ComponentType ); Assert.AreEqual( 2, data.Indices.ByteStride );
		Assert.AreEqual( 360L, data.OwnedBytes );
		var expected = GltfDraco.Decode( Convert.FromBase64String( BoxDraco ) );
		CollectionAssert.AreEqual( expected.Attributes[1].Bytes, position.Bytes );
		for ( var i = 0; i < expected.Indices.Length; i++ )
			Assert.AreEqual( expected.Indices[i], (uint)BinaryPrimitives.ReadUInt16LittleEndian( data.Indices.Bytes.AsSpan( i * 2, 2 ) ) );
	}

	// The remaining cache-seeded cases exercise the adapter/representation
	// contract independently of native codec availability or encoder fidelity.
	[TestMethod]
	[DataRow( 5120, 3, true, "80FF7F" )]
	[DataRow( 5121, 3, true, "0001FF" )]
	[DataRow( 5122, 3, true, "0080FFFF007F" )]
	[DataRow( 5123, 3, true, "00000180FFFF" )]
	[DataRow( 5125, 1, false, "01000001" )]
	[DataRow( 5126, 1, false, "00000080" )]
	public void RepresentationPreservesExactComponentBytesAndNormalization( int type, int components, bool normalized, string elementHex )
	{
		var element = Convert.FromHexString( elementHex );
		var bytes = Enumerable.Range( 0, 3 ).SelectMany( _ => element ).ToArray();
		using var document = SingleAttribute( type, components, normalized );
		Seed( document, 0, Mesh( bytes, type, components ) );
		var stream = document.ReadDracoPrimitiveData( 0, 0 ).Attributes["_DATA"];
		Assert.AreEqual( type, stream.ComponentType ); Assert.AreEqual( components, stream.Components );
		Assert.AreEqual( normalized, stream.Normalized ); Assert.AreEqual( 3, stream.Count );
		Assert.AreEqual( element.Length, stream.ByteStride );
		CollectionAssert.AreEqual( bytes, stream.Bytes );
		Assert.AreNotSame( bytes, stream.Bytes );
	}

	[TestMethod]
	[DataRow( "0000807F" )]
	[DataRow( "000080FF" )]
	[DataRow( "0100C07F" )]
	public void RepresentationRejectsNonFiniteFloatComponents( string elementHex )
	{
		using var document = SingleAttribute( 5126, 1 );
		var bytes = Enumerable.Range( 0, 3 ).SelectMany( _ => Convert.FromHexString( elementHex ) ).ToArray();
		Seed( document, 0, Mesh( bytes, 5126, 1 ) );
		Assert.ThrowsException<GltfException>( () => document.ReadDracoPrimitiveData( 0, 0 ) );
	}

	[TestMethod]
	public void RepresentationRejectsDecodedComponentFormatThatDisagreesWithAccessor()
	{
		using var document = SingleAttribute( 5126, 1 );
		Seed( document, 0, Mesh( new byte[6], 5123, 1 ) );
		Assert.ThrowsException<GltfException>( () => document.ReadDracoPrimitiveData( 0, 0 ) );
	}

	[TestMethod]
	public void RepresentationRetainsOriginalJointIntegersAndNormalizedWeightBytes()
	{
		var joints = Enumerable.Range( 0, 3 ).SelectMany( _ => Convert.FromHexString( "FFFF018001010100" ) ).ToArray();
		var weights = Enumerable.Range( 0, 3 ).SelectMany( _ => new byte[] { 128, 64, 32, 31 } ).ToArray();
		using var document = Document(
			"{\"componentType\":5123,\"count\":3,\"type\":\"VEC4\"},{\"componentType\":5121,\"normalized\":true,\"count\":3,\"type\":\"VEC4\"}",
			"{\"attributes\":{\"JOINTS_0\":0,\"WEIGHTS_0\":1},\"extensions\":{\"KHR_draco_mesh_compression\":{\"bufferView\":0,\"attributes\":{\"JOINTS_0\":7,\"WEIGHTS_0\":8}}}}" );
		Seed( document, 0, new GltfDracoMesh
		{
			VertexCount = 3,
			Indices = [2, 0, 1],
			Attributes = new Dictionary<uint, GltfDracoAttribute>
			{
				[7] = new() { ComponentType = 5123, Components = 4, Bytes = joints },
				[8] = new() { ComponentType = 5121, Components = 4, Bytes = weights }
			}
		} );
		var data = document.ReadDracoPrimitiveData( 0, 0 );
		var jointStream = data.Attributes["JOINTS_0"];
		Assert.AreEqual( 5123, jointStream.ComponentType ); Assert.IsFalse( jointStream.Normalized );
		Assert.AreEqual( 8, jointStream.ByteStride ); CollectionAssert.AreEqual( joints, jointStream.Bytes );
		Assert.AreEqual( (ushort)65535, BinaryPrimitives.ReadUInt16LittleEndian( jointStream.Bytes ) );
		Assert.AreEqual( (ushort)32769, BinaryPrimitives.ReadUInt16LittleEndian( jointStream.Bytes.AsSpan( 2 ) ) );
		Assert.IsTrue( data.Attributes["WEIGHTS_0"].Normalized );
		CollectionAssert.AreEqual( weights, data.Attributes["WEIGHTS_0"].Bytes );
	}

	[TestMethod]
	[DataRow( 5121, 1 )]
	[DataRow( 5123, 2 )]
	[DataRow( 5125, 4 )]
	public void RepresentationPacksDecodedTriangleListInDeclaredIndexWidth( int type, int width )
	{
		using var document = Document(
			$"{{\"componentType\":5126,\"count\":4,\"type\":\"SCALAR\"}},{{\"componentType\":{type},\"count\":4,\"type\":\"SCALAR\"}}",
			"{\"mode\":5,\"attributes\":{\"_DATA\":0},\"indices\":1,\"extensions\":{\"KHR_draco_mesh_compression\":{\"bufferView\":0,\"attributes\":{\"_DATA\":7}}}}" );
		uint[] triangles = [0, 1, 2, 2, 1, 3];
		Seed( document, 0, Mesh( new byte[16], 5126, 1, indices: triangles, count: 4 ) );
		var data = document.ReadDracoPrimitiveData( 0, 0 );
		Assert.AreEqual( 5, data.SourceMode ); Assert.AreEqual( 4, data.Mode );
		Assert.AreEqual( 1, data.Indices.AccessorIndex ); Assert.AreEqual( 6, data.Indices.Count );
		Assert.AreEqual( type, data.Indices.ComponentType ); Assert.AreEqual( width, data.Indices.ByteStride );
		Assert.AreEqual( 6 * width, data.Indices.Bytes.Length );
		for ( var i = 0; i < triangles.Length; i++ )
		{
			var bytes = data.Indices.Bytes.AsSpan( i * width, width );
			var value = width == 1 ? bytes[0] : width == 2 ? BinaryPrimitives.ReadUInt16LittleEndian( bytes ) : BinaryPrimitives.ReadUInt32LittleEndian( bytes );
			Assert.AreEqual( triangles[i], value );
		}
	}

	[TestMethod]
	public void RepresentationWithoutSourceIndicesStillReturnsCodecTopology()
	{
		using var document = SingleAttribute( 5126, 1 );
		Seed( document, 0, Mesh( new byte[12], 5126, 1 ) );
		var indices = document.ReadDracoPrimitiveData( 0, 0 ).Indices;
		Assert.IsNull( indices.AccessorIndex ); Assert.AreEqual( 5125, indices.ComponentType );
		Assert.AreEqual( 3, indices.Count ); Assert.AreEqual( 4, indices.ByteStride );
		CollectionAssert.AreEqual( Convert.FromHexString( "020000000000000001000000" ), indices.Bytes );
	}

	[TestMethod]
	public void RepresentationUsesEachPrimitivesOwnMappingForSharedAccessors()
	{
		using var document = Document( "{\"componentType\":5125,\"count\":3,\"type\":\"SCALAR\"}",
			Primitive( 7 ) + "," + Primitive( 8 ) );
		var first = Convert.FromHexString( "000000010000000100000001" );
		var second = Convert.FromHexString( "010000010100000101000001" );
		// 16777216 and 16777217 collapse to the same float. Raw primitive
		// mappings must remain distinct even when a global float read cannot.
		Seed( document, 0, Mesh( first, 5125, 1 ) );
		Seed( document, 1, Mesh( second, 5125, 1, id: 8 ) );
		CollectionAssert.AreEqual( first, document.ReadDracoPrimitiveData( 0, 0 ).Attributes["_DATA"].Bytes );
		CollectionAssert.AreEqual( second, document.ReadDracoPrimitiveData( 0, 1 ).Attributes["_DATA"].Bytes );
	}

	[TestMethod]
	public void RepresentationDoesNotInspectAnUnrelatedPrimitivesInvalidMapping()
	{
		using var document = Document( "{\"componentType\":5126,\"count\":3,\"type\":\"SCALAR\"}",
			Primitive( 7 ) + ",{\"attributes\":{\"_DATA\":999},\"extensions\":{\"KHR_draco_mesh_compression\":{\"attributes\":false}}}" );
		Seed( document, 0, Mesh( new byte[12], 5126, 1 ) );
		Assert.AreEqual( 3, document.ReadDracoPrimitiveData( 0, 0 ).VertexCount );
	}

	[TestMethod]
	public void RepresentationReturnsDetachedMutableArraysThatSurviveDocumentDisposal()
	{
		using var document = SingleAttribute( 5126, 1 );
		Seed( document, 0, Mesh( new byte[12], 5126, 1 ) );
		var first = document.ReadDracoPrimitiveData( 0, 0 );
		first.Attributes["_DATA"].Bytes[0] = 255; first.Indices.Bytes[0] = 255;
		var second = document.ReadDracoPrimitiveData( 0, 0 );
		Assert.AreEqual( (byte)0, second.Attributes["_DATA"].Bytes[0] );
		Assert.AreEqual( (byte)2, second.Indices.Bytes[0] );
		document.Dispose();
		Assert.AreEqual( (byte)255, first.Attributes["_DATA"].Bytes[0] );
		Assert.AreEqual( 12, second.Indices.Bytes.Length );
	}

	[TestMethod]
	public void RepresentationFlagsSparseOverlaysWhileReturningDecodedBaseBytes()
	{
		using var document = Document(
			"{\"componentType\":5121,\"count\":3,\"type\":\"SCALAR\",\"sparse\":{\"count\":1,\"indices\":{\"bufferView\":0,\"componentType\":5121},\"values\":{\"bufferView\":0}}}", Primitive( 7 ) );
		Seed( document, 0, Mesh( [10, 20, 30], 5121, 1 ) );
		var stream = document.ReadDracoPrimitiveData( 0, 0 ).Attributes["_DATA"];
		Assert.IsTrue( stream.HasSparseOverrides ); Assert.AreEqual( 0, stream.AccessorIndex );
		CollectionAssert.AreEqual( new byte[] { 10, 20, 30 }, stream.Bytes );
	}

	[TestMethod]
	public void RepresentationPreflightsEveryOwnedByteIncludingTopology()
	{
		using var document = SingleAttribute( 5126, 1 );
		Seed( document, 0, Mesh( new byte[12], 5126, 1 ) );
		Assert.ThrowsException<GltfException>( () => document.ReadDracoPrimitiveData( 0, 0, maxOwnedBytes: -1 ) );
		Assert.ThrowsException<GltfException>( () => document.ReadDracoPrimitiveData( 0, 0, maxOwnedBytes: 23 ) );
		Assert.AreEqual( 24L, document.ReadDracoPrimitiveData( 0, 0, maxOwnedBytes: 24 ).OwnedBytes );
	}

	[TestMethod]
	public void RepresentationSharesClonedStorageForRepeatedDecodedAttributeIds()
	{
		using var document = Document(
			"{\"componentType\":5121,\"count\":3,\"type\":\"SCALAR\"},{\"componentType\":5121,\"normalized\":true,\"count\":3,\"type\":\"SCALAR\"}",
			"{\"attributes\":{\"_DATA\":0,\"_ALIAS\":1},\"extensions\":{\"KHR_draco_mesh_compression\":{\"bufferView\":0,\"attributes\":{\"_DATA\":7,\"_ALIAS\":7}}}}" );
		byte[] source = [1, 128, 255];
		Seed( document, 0, Mesh( source, 5121, 1 ) );
		var data = document.ReadDracoPrimitiveData( 0, 0, maxOwnedBytes: 15 );
		Assert.AreEqual( 15L, data.OwnedBytes );
		Assert.AreSame( data.Attributes["_DATA"].Bytes, data.Attributes["_ALIAS"].Bytes );
		Assert.AreNotSame( source, data.Attributes["_DATA"].Bytes );
		Assert.IsFalse( data.Attributes["_DATA"].Normalized ); Assert.IsTrue( data.Attributes["_ALIAS"].Normalized );
		Assert.AreEqual( 0, data.Attributes["_DATA"].AccessorIndex ); Assert.AreEqual( 1, data.Attributes["_ALIAS"].AccessorIndex );
	}

	[TestMethod]
	public void SparseSourceStripIndicesCannotBeAppliedToDecodedTriangleListPositions()
	{
		using var document = Document(
			"{\"componentType\":5126,\"count\":4,\"type\":\"SCALAR\"},{\"componentType\":5123,\"count\":4,\"type\":\"SCALAR\",\"sparse\":{\"count\":1,\"indices\":{\"bufferView\":0,\"componentType\":5121},\"values\":{\"bufferView\":0}}}",
			"{\"mode\":5,\"attributes\":{\"_DATA\":0},\"indices\":1,\"extensions\":{\"KHR_draco_mesh_compression\":{\"bufferView\":0,\"attributes\":{\"_DATA\":7}}}}" );
		Seed( document, 0, Mesh( new byte[16], 5126, 1, indices: [0, 1, 2, 2, 1, 3], count: 4 ) );
		var error = Assert.ThrowsException<GltfException>( () => document.ReadDracoPrimitiveData( 0, 0 ) );
		StringAssert.Contains( error.Message, "sparse" ); StringAssert.Contains( error.Message, "strip" );
	}

	[TestMethod]
	[DataRow( 256, 255u )]
	[DataRow( 3, 3u )]
	public void RepresentationRejectsDecodedIndexWidthRestartAndVertexRangeViolations( int count, uint invalidIndex )
	{
		using var document = Document(
			$"{{\"componentType\":5121,\"count\":{count},\"type\":\"SCALAR\"}},{{\"componentType\":5121,\"count\":3,\"type\":\"SCALAR\"}}",
			"{\"attributes\":{\"_DATA\":0},\"indices\":1,\"extensions\":{\"KHR_draco_mesh_compression\":{\"bufferView\":0,\"attributes\":{\"_DATA\":7}}}}" );
		Seed( document, 0, Mesh( new byte[count], 5121, 1, indices: [0, 1, invalidIndex], count: count ) );
		Assert.ThrowsException<GltfException>( () => document.ReadDracoPrimitiveData( 0, 0 ) );
	}

	[TestMethod]
	public void SelectedCorruptCodecCannotReturnUsableFallbackBytes()
	{
		using var document = Document( "{\"bufferView\":0,\"componentType\":5121,\"count\":3,\"type\":\"SCALAR\"}", Primitive( 7 ) );
		Assert.ThrowsException<GltfException>( () => document.ReadDracoPrimitiveData( 0, 0 ) );
	}

	[TestMethod]
	public void OrdinaryPrimitiveIsNotReinterpretedThroughAnotherPrimitivesDracoBinding()
	{
		using var document = Document( "{\"componentType\":5126,\"count\":3,\"type\":\"SCALAR\"}",
			"{\"attributes\":{\"_DATA\":0}}," + Primitive( 7 ) );
		Seed( document, 1, Mesh( new byte[12], 5126, 1 ) );
		Assert.ThrowsException<GltfException>( () => document.ReadDracoPrimitiveData( 0, 0 ) );
		Assert.AreEqual( 3, document.ReadDracoPrimitiveData( 0, 1 ).VertexCount );
	}

	private static GltfDocument SingleAttribute( int type, int components, bool normalized = false )
		=> Document( $"{{\"bufferView\":999,\"byteOffset\":123,\"componentType\":{type},\"count\":3,\"type\":\"{(components == 1 ? "SCALAR" : "VEC" + components)}\",\"normalized\":{normalized.ToString().ToLowerInvariant()}}}", Primitive( 7 ) );

	private static string Primitive( uint id )
		=> $"{{\"attributes\":{{\"_DATA\":0}},\"extensions\":{{\"KHR_draco_mesh_compression\":{{\"bufferView\":0,\"attributes\":{{\"_DATA\":{id}}}}}}}}}";

	private static GltfDocument Document( string accessors, string primitives, byte[] encoded = null )
	{
		encoded ??= new byte[4];
		var json = $$$"""
			{"asset":{"version":"2.0"},"extensionsUsed":["KHR_draco_mesh_compression"],
			 "buffers":[{"uri":"data:application/octet-stream;base64,{{{Convert.ToBase64String( encoded )}}}","byteLength":{{{encoded.Length}}}}],
			 "bufferViews":[{"buffer":0,"byteLength":{{{encoded.Length}}}}],
			 "accessors":[{{{accessors}}}],"meshes":[{"primitives":[{{{primitives}}}]}]}
			""";
		return GltfReader.Read( Encoding.UTF8.GetBytes( json ), supportedExtensions: new HashSet<string> { "KHR_draco_mesh_compression" } );
	}

	private static GltfDracoMesh Mesh( byte[] bytes, int type, int components, uint id = 7, uint[] indices = null, int count = 3 )
		=> new()
		{
			VertexCount = count,
			Indices = indices ?? [2, 0, 1],
			Attributes = new Dictionary<uint, GltfDracoAttribute>
			{
				[id] = new() { ComponentType = type, Components = components, Bytes = bytes }
			}
		};

	private static void Seed( GltfDocument document, int primitive, GltfDracoMesh decoded )
	{
		// Deliberately seed the existing decoder cache rather than add a
		// production-only injection hook or claim these are encoded fixtures.
		var field = typeof( GltfDocument ).GetField( "dracoCache", BindingFlags.NonPublic | BindingFlags.Instance );
		Assert.IsNotNull( field );
		var cache = (Dictionary<(int Mesh, int Primitive), GltfDracoMesh>)field.GetValue( document );
		cache.Add( (0, primitive), decoded );
	}
}
