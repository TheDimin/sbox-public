using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;

namespace Sandbox.Tests.Gltf;

[TestClass]
public class GltfPrimitiveTests
{
	[TestMethod]
	public void NonIndexedPrimitiveUsesEveryVertexAndDefaultTriangleMode()
	{
		using var document = Read( """{"attributes":{"POSITION":0}}""" );
		var primitive = document.ReadPrimitive( 0, 0 );
		Assert.AreEqual( 3, primitive.VertexCount );
		Assert.AreEqual( 4, primitive.Mode );
		Assert.IsNull( primitive.Material );
		CollectionAssert.AreEqual( new uint[] { 0, 1, 2 }, primitive.Indices );
		Assert.AreEqual( 0, primitive.Attributes["POSITION"] );
	}

	[TestMethod]
	public void IndexedPrimitiveRetainsOrderAndMaterial()
	{
		using var document = Read( """{"attributes":{"POSITION":0},"indices":1,"material":0}""",
			""",{"bufferView":0,"componentType":5121,"count":3,"type":"SCALAR"}""",
			""","buffers":[{"uri":"data:application/octet-stream;base64,AgAB","byteLength":3}],"bufferViews":[{"buffer":0,"byteLength":3}],"materials":[{}]""" );
		var primitive = document.ReadPrimitive( 0, 0 );
		CollectionAssert.AreEqual( new uint[] { 2, 0, 1 }, primitive.Indices );
		Assert.AreEqual( 0, primitive.Material );
	}

	[TestMethod]
	public void MultipleUvSetsAndApplicationAttributesRemainAddressable()
	{
		using var document = Read( """{"attributes":{"POSITION":0,"TEXCOORD_0":1,"TEXCOORD_1":2,"_TAG":3}}""",
			""",{"componentType":5126,"count":3,"type":"VEC2"},{"componentType":5123,"normalized":true,"count":3,"type":"VEC2"},{"componentType":5121,"count":3,"type":"SCALAR"}""" );
		var primitive = document.ReadPrimitive( 0, 0 );
		Assert.AreEqual( 4, primitive.Attributes.Count );
		Assert.AreEqual( 1, primitive.Attributes["TEXCOORD_0"] );
		Assert.AreEqual( 2, primitive.Attributes["TEXCOORD_1"] );
		Assert.AreEqual( 3, primitive.Attributes["_TAG"] );
	}

	[TestMethod]
	public void AttributeCountsMustAgree()
	{
		using var document = Read( """{"attributes":{"POSITION":0,"TEXCOORD_0":1}}""",
			""",{"componentType":5126,"count":2,"type":"VEC2"}""" );
		Assert.ThrowsException<GltfException>( () => document.ReadPrimitive( 0, 0 ) );
	}

	[TestMethod]
	[DataRow( "TEXCOORD_01", "VEC2", 5126, false )]
	[DataRow( "TEXCOORD_1", "VEC2", 5126, false )]
	[DataRow( "TEXCOORD_0", "VEC3", 5126, false )]
	[DataRow( "TEXCOORD_0", "VEC2", 5123, false )]
	[DataRow( "COLOR_0", "VEC2", 5126, false )]
	[DataRow( "NORMAL", "VEC4", 5126, false )]
	[DataRow( "TANGENT", "VEC3", 5126, false )]
	[DataRow( "JOINTS_0", "VEC4", 5121, true )]
	public void InvalidCoreAttributeLayoutsAreRejected( string name, string type, int componentType, bool normalized )
	{
		var attribute = $"{{\"componentType\":{componentType},\"normalized\":{normalized.ToString().ToLowerInvariant()},\"count\":3,\"type\":\"{type}\"}}";
		using var document = Read( $"{{\"attributes\":{{\"POSITION\":0,\"{name}\":1}}}}", "," + attribute );
		Assert.ThrowsException<GltfException>( () => document.ReadPrimitive( 0, 0 ) );
	}

	[TestMethod]
	public void OutOfRangeIndicesFailBeforeGpuSubmission()
	{
		using var document = Read( """{"attributes":{"POSITION":0},"indices":1}""",
			""",{"bufferView":0,"componentType":5121,"count":3,"type":"SCALAR"}""",
			""","buffers":[{"uri":"data:application/octet-stream;base64,AAED","byteLength":3}],"bufferViews":[{"buffer":0,"byteLength":3}]""" );
		Assert.ThrowsException<GltfException>( () => document.ReadPrimitive( 0, 0 ) );
	}

	[TestMethod]
	[DataRow( "{\"attributes\":{\"POSITION\":6}}" )]
	[DataRow( "{\"attributes\":{\"POSITION\":0},\"material\":9}" )]
	[DataRow( "{\"attributes\":{\"POSITION\":0},\"mode\":7}" )]
	public void InvalidReferencesAndPrimitiveModesFailClearly( string primitive )
	{
		Assert.ThrowsException<GltfException>( () =>
		{
			using var document = Read( primitive );
			_ = document.ReadPrimitive( 0, 0 );
		} );
	}

	[TestMethod]
	public void QuantizedPositionRequiresTheSelectedQuantizationExtension()
	{
		const string json = """
			{"asset":{"version":"2.0"},"extensionsUsed":["KHR_mesh_quantization"],"extensionsRequired":["KHR_mesh_quantization"],"accessors":[{"componentType":5122,"normalized":true,"count":3,"type":"VEC3","min":[0,0,0],"max":[0,0,0]}],"meshes":[{"primitives":[{"attributes":{"POSITION":0}}]}]}
			""";
		Assert.ThrowsException<GltfException>( () => GltfReader.Read( Encoding.UTF8.GetBytes( json ) ) );
		using var document = GltfReader.Read( Encoding.UTF8.GetBytes( json ), supportedExtensions: new HashSet<string> { "KHR_mesh_quantization" } );
		Assert.AreEqual( 3, document.ReadPrimitive( 0, 0 ).VertexCount );
		CollectionAssert.AreEqual( new float[9], document.ReadFloatAccessor( 0 ) );
	}

	private static GltfDocument Read( string primitive, string extraAccessors = "", string extraRoot = "" )
	{
		var json = "{\"asset\":{\"version\":\"2.0\"},\"accessors\":[{\"componentType\":5126,\"count\":3,\"type\":\"VEC3\",\"min\":[0,0,0],\"max\":[0,0,0]}" +
			extraAccessors + "],\"meshes\":[{\"primitives\":[" + primitive + "]}]" + extraRoot + "}";
		return GltfReader.Read( Encoding.UTF8.GetBytes( json ) );
	}
}
