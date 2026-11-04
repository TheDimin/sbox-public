using System;
using System.Buffers.Binary;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;
using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace Sandbox.Tests.Gltf;

[TestClass]
public sealed class GltfReaderRobustnessTests
{
	private const string WhitePng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4////fwAJ+wP9KobjigAAAABJRU5ErkJggg==";

	[TestMethod]
	public void UnsignedIndicesPreserveIntegersNotRepresentableAsFloatsIncludingSparseReplacements()
	{
		var bytes = new byte[16];
		BinaryPrimitives.WriteUInt32LittleEndian( bytes.AsSpan( 0, 4 ), 16777217 );
		BinaryPrimitives.WriteUInt32LittleEndian( bytes.AsSpan( 4, 4 ), 123 );
		bytes[8] = 1;
		BinaryPrimitives.WriteUInt32LittleEndian( bytes.AsSpan( 12, 4 ), 4294967294 );
		var json = """
			{"asset":{"version":"2.0"},"buffers":[{"uri":"data:application/octet-stream;base64,BINARY_PAYLOAD","byteLength":16}],
			 "bufferViews":[{"buffer":0,"byteLength":8},{"buffer":0,"byteOffset":8,"byteLength":1},{"buffer":0,"byteOffset":12,"byteLength":4}],
			 "accessors":[{"bufferView":0,"componentType":5125,"count":2,"type":"SCALAR","sparse":{"count":1,"indices":{"bufferView":1,"componentType":5121},"values":{"bufferView":2}}}]}
			""".Replace( "BINARY_PAYLOAD", Convert.ToBase64String( bytes ) );
		using var document = GltfReader.Read( Encoding.UTF8.GetBytes( json ) );

		CollectionAssert.AreEqual( new uint[] { 16777217, 4294967294 }, document.ReadIndexAccessor( 0 ) );
	}

	[TestMethod]
	public void FractionalJsonIntegerCannotRoundToAValidCount()
	{
		const string json = "{\"asset\":{\"version\":\"2.0\"},\"accessors\":[{\"componentType\":5126,\"count\":1.0000000000000001,\"type\":\"SCALAR\"}]}";
		using var document = GltfReader.Read( Encoding.UTF8.GetBytes( json ) );

		Assert.ThrowsException<GltfException>( () => document.ReadFloatAccessor( 0 ) );
	}

	[TestMethod]
	public void ImpossibleAccessorRangeIsRejectedBeforeAllocatingItsDeclaredElements()
	{
		const string json = """
			{"asset":{"version":"2.0"},"buffers":[{"uri":"data:application/octet-stream;base64,AAAAAA==","byteLength":4}],"bufferViews":[{"buffer":0,"byteLength":4}],"accessors":[{"bufferView":0,"componentType":5126,"count":2147483647,"type":"SCALAR"}]}
			""";
		using var document = GltfReader.Read( Encoding.UTF8.GetBytes( json ) );
		Assert.ThrowsException<GltfException>( () => document.ReadFloatAccessor( 0 ) );
	}

	[TestMethod]
	public void MathematicallyIntegralExponentIsAcceptedForIntegerProperties()
	{
		const string json = "{\"asset\":{\"version\":\"2.0\"},\"accessors\":[{\"componentType\":5126,\"count\":1e0,\"type\":\"SCALAR\"}]}";
		using var document = GltfReader.Read( Encoding.UTF8.GetBytes( json ) );

		CollectionAssert.AreEqual( new float[] { 0 }, document.ReadFloatAccessor( 0 ) );
	}

	[TestMethod]
	public void IntegralNumberSpellingsAlsoWorkForSceneAndSamplerReferences()
	{
		const string json = """
			{"asset":{"version":"2.0"},"nodes":[{"children":[1e0]},{}],"samplers":[{"wrapS":1.0497e4,"wrapT":10497.0}]}
			""";
		using var document = GltfReader.Read( Encoding.UTF8.GetBytes( json ) );
		Assert.AreEqual( System.Numerics.Matrix4x4.Identity, document.GetWorldTransform( 1 ) );
		Assert.AreEqual( 10497, document.ReadSampler( 0 ).WrapS );
		Assert.AreEqual( 10497, document.ReadSampler( 0 ).WrapT );
	}

	[TestMethod]
	public void IntegralNumberSpellingsWorkForSkinAccessorComponentTypes()
	{
		using var document = SceneFeatureFixture.ReadFloats( [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1], """
			"nodes":[{}],"bufferViews":[{"buffer":0,"byteLength":64}],"accessors":[{"bufferView":0,"componentType":5.126e3,"count":1e0,"type":"MAT4"}],"skins":[{"joints":[0e0],"inverseBindMatrices":0e0}]
			""" );
		Assert.AreEqual( System.Numerics.Matrix4x4.Identity, document.ReadSkin( 0 ).InverseBindMatrices[0] );
	}

	[TestMethod]
	[DataRow( "images", "null" )]
	[DataRow( "images", "1" )]
	[DataRow( "textures", "null" )]
	[DataRow( "textures", "1" )]
	public void NonObjectImageOrTextureReportsGltfError( string kind, string item )
	{
		var json = $$"""{"asset":{"version":"2.0"},"{{kind}}":[{{item}}]}""";
		using var document = GltfReader.Read( Encoding.UTF8.GetBytes( json ) );

		Assert.ThrowsException<GltfException>( () =>
		{
			if ( kind == "images" ) document.ReadImage( 0 );
			else document.ReadTexture( 0 );
		} );
	}

	[TestMethod]
	public void NonObjectBufferViewReportsGltfError()
	{
		const string json = "{\"asset\":{\"version\":\"2.0\"},\"bufferViews\":[null],\"accessors\":[{\"bufferView\":0,\"componentType\":5126,\"count\":1,\"type\":\"SCALAR\"}]}";
		using var document = GltfReader.Read( Encoding.UTF8.GetBytes( json ) );

		Assert.ThrowsException<GltfException>( () => document.ReadFloatAccessor( 0 ) );
	}

	[TestMethod]
	[DataRow( "\"target\":34962" )]
	[DataRow( "\"byteStride\":4" )]
	public void ImageBufferViewsCannotSpecifyGpuTargetOrStride( string forbiddenProperty )
	{
		var size = Convert.FromBase64String( WhitePng ).Length;
		var json = $$"""
			{"asset":{"version":"2.0"},"buffers":[{"uri":"data:application/octet-stream;base64,{{WhitePng}}","byteLength":{{size}}}],
			 "bufferViews":[{"buffer":0,"byteLength":{{size}},{{forbiddenProperty}}}],"images":[{"bufferView":0,"mimeType":"image/png"}]}
			""";
		using var document = GltfReader.Read( Encoding.UTF8.GetBytes( json ) );

		Assert.ThrowsException<GltfException>( () => document.ReadImage( 0 ) );
	}

	[TestMethod]
	public void EmbeddedImageUriMediaTypeMustMatchItsActualImageEvenWhenMimeTypeIsExplicit()
	{
		var json = $$"""{"asset":{"version":"2.0"},"images":[{"uri":"data:image/jpeg;base64,{{WhitePng}}","mimeType":"image/png"}]}""";
		using var document = GltfReader.Read( Encoding.UTF8.GetBytes( json ) );

		Assert.ThrowsException<GltfException>( () => document.ReadImage( 0 ) );
	}
}
