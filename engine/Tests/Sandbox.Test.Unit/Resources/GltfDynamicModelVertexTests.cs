using NativeEngine;
using Sandbox.Gltf;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Sandbox.Tests;

[TestClass]
public class GltfDynamicModelVertexTests
{
	[TestMethod]
	public void LayoutContainsOnlyPresentStreamsAndRetainsHigherUvSets()
	{
		var bare = GltfModelPrimitiveBuilder.Prepare( Triangle() );
		Assert.AreEqual( 3, bare.VertexFields.Length );
		Assert.AreEqual( 40, bare.VertexStride );
		var data = Triangle( new Dictionary<int, ReadOnlyMemory<System.Numerics.Vector2>> { [3] = new System.Numerics.Vector2[] { new( .25f, .5f ), default, default } } );
		var withUv = GltfModelPrimitiveBuilder.Prepare( data );
		Assert.AreEqual( 48, withUv.VertexStride );
		var field = withUv.VertexFields.Single( field => field.Semantic == "texcoord" );
		Assert.AreEqual( 3, field.SemanticIndex );
		Assert.AreEqual( .25f, BinaryPrimitives.ReadSingleLittleEndian( withUv.VertexBytes.AsSpan( field.Offset, 4 ) ) );
	}

	[TestMethod]
	public void StridedNormalizedAccessorsRetainExactBytesAndNativeFormats()
	{
		var (source, raw) = Source();
		using var document = GltfReader.Read( source );
		var prepared = GltfModelPrimitiveBuilder.Prepare( document.CreateDrawData( 0, 0 ), upload: document.MaterializePrimitiveUpload( 0, 0 ) );
		var color = prepared.VertexFields.Single( field => field.Semantic == "color" );
		var firstUv = prepared.VertexFields.Single( field => field.Semantic == "texcoord" && field.SemanticIndex == 0 );
		var secondUv = prepared.VertexFields.Single( field => field.Semantic == "texcoord" && field.SemanticIndex == 1 );
		Assert.AreEqual( ColorFormat.COLOR_FORMAT_R8G8B8A8_UNORM, color.Format );
		Assert.AreEqual( ColorFormat.COLOR_FORMAT_R16G16_UNORM, firstUv.Format );
		Assert.AreEqual( ColorFormat.COLOR_FORMAT_R8G8_UNORM, secondUv.Format );
		for ( var vertex = 0; vertex < 3; vertex++ )
		{
			AssertBytes( raw.AsSpan( vertex * 24 + 12, 4 ), prepared, vertex, color );
			AssertBytes( raw.AsSpan( vertex * 24 + 16, 4 ), prepared, vertex, firstUv );
			AssertBytes( raw.AsSpan( vertex * 24 + 20, 2 ), prepared, vertex, secondUv );
		}
		Assert.AreEqual( 17f / 255f, prepared.Vertices[0].Color.z );
		Assert.AreEqual( 32768f / 65535f, prepared.Vertices[0].TexCoord0.y );
		// Source tail padding contains NaN-like bytes. It is not an attribute and
		// must neither become a float value nor contaminate a generated direction.
		Assert.IsTrue( float.IsFinite( prepared.Vertices[0].Normal.x ) );
	}

	[TestMethod]
	public void ThreeComponentByteColorUsesAnExplicitFloatConversionWithOpaqueAlpha()
	{
		var (source, _) = Source();
		var json = Encoding.UTF8.GetString( source ).Replace( "\"type\":\"VEC4\"", "\"type\":\"VEC3\"", StringComparison.Ordinal );
		using var document = GltfReader.Read( Encoding.UTF8.GetBytes( json ) );
		var prepared = GltfModelPrimitiveBuilder.Prepare( document.CreateDrawData( 0, 0 ), upload: document.MaterializePrimitiveUpload( 0, 0 ) );
		var field = prepared.VertexFields.Single( value => value.Semantic == "color" );
		Assert.AreEqual( ColorFormat.COLOR_FORMAT_R32G32B32A32_FLOAT, field.Format );
		Assert.AreEqual( new Vector4( 0, 1, 17f / 255f, 1 ), prepared.Vertices[0].Color );
	}

	[TestMethod]
	public void MaterialUvSelectionAndTransformConvertsOnlyTheEffectiveStockChannel()
	{
		var (source, raw) = Source();
		using var document = GltfReader.Read( source );
		var texture = new GltfTextureInfo { TexCoord = 1, Tiling = new( 2, 3 ), Offset = new( .25f, -.5f ) };
		var prepared = GltfModelPrimitiveBuilder.Prepare( document.CreateDrawData( 0, 0 ), new GltfMaterial { BaseColorTexture = texture }, document.MaterializePrimitiveUpload( 0, 0 ) );
		var selected = prepared.VertexFields.Single( field => field.Semantic == "texcoord" && field.SemanticIndex == 0 );
		var original = prepared.VertexFields.Single( field => field.Semantic == "texcoord" && field.SemanticIndex == 1 );
		Assert.AreEqual( ColorFormat.COLOR_FORMAT_R32G32_FLOAT, selected.Format );
		Assert.AreEqual( ColorFormat.COLOR_FORMAT_R8G8_UNORM, original.Format );
		Assert.AreEqual( new Vector2( 2.25f, -.5f ), prepared.Vertices[0].TexCoord0 );
		AssertBytes( raw.AsSpan( 20, 2 ), prepared, 0, original );
	}

	[TestMethod]
	public void FlatNormalCornerSplittingCopiesEveryNormalizedSourceAttribute()
	{
		var (source, raw) = Source();
		using var document = GltfReader.Read( source );
		var original = document.CreateDrawData( 0, 0 );
		var data = new GltfDrawData { Mode = 4, Positions = original.Positions, Colors = original.Colors, TexCoords = original.TexCoords, Indices = new uint[] { 0, 1, 2, 2, 1, 0 } };
		var prepared = GltfModelPrimitiveBuilder.Prepare( data, upload: document.MaterializePrimitiveUpload( 0, 0 ) );
		Assert.AreEqual( 6, prepared.VertexCount );
		CollectionAssert.AreEqual( new[] { 0, 1, 2, 3, 4, 5 }, prepared.Indices );
		var color = prepared.VertexFields.Single( field => field.Semantic == "color" );
		var uv = prepared.VertexFields.Single( field => field.Semantic == "texcoord" && field.SemanticIndex == 1 );
		for ( var corner = 0; corner < 6; corner++ )
		{
			var vertex = (int)data.Indices.Span[corner];
			AssertBytes( raw.AsSpan( vertex * 24 + 12, 4 ), prepared, corner, color );
			AssertBytes( raw.AsSpan( vertex * 24 + 20, 2 ), prepared, corner, uv );
		}
		Assert.AreEqual( new Vector3( 1, 0, 0 ), prepared.Vertices[0].Normal );
		Assert.AreEqual( new Vector3( -1, 0, 0 ), prepared.Vertices[3].Normal );
	}

	[TestMethod]
	public void DifferentRoleUvMappingsAndMissingNormalMapTangentsFailExplicitly()
	{
		var data = Triangle( new Dictionary<int, ReadOnlyMemory<System.Numerics.Vector2>> { [0] = new System.Numerics.Vector2[3], [1] = new System.Numerics.Vector2[3] } );
		var error = Assert.ThrowsException<GltfException>( () => GltfModelPrimitiveBuilder.Prepare( data, new GltfMaterial { BaseColorTexture = new() { TexCoord = 0, Tiling = System.Numerics.Vector2.One }, EmissiveTexture = new() { TexCoord = 1, Tiling = System.Numerics.Vector2.One } } ) );
		StringAssert.Contains( error.Message, "same texture coordinate" );
		error = Assert.ThrowsException<GltfException>( () => GltfModelPrimitiveBuilder.Prepare( data, new GltfMaterial { NormalTexture = new() { TexCoord = 0, Tiling = System.Numerics.Vector2.One } } ) );
		StringAssert.Contains( error.Message, "MikkTSpace" );
	}

	private static void AssertBytes( ReadOnlySpan<byte> expected, GltfModelPrimitiveBuilder.PreparedPrimitive primitive, int vertex, RawVertexField field ) =>
		CollectionAssert.AreEqual( expected.ToArray(), primitive.VertexBytes.AsSpan( vertex * primitive.VertexStride + field.Offset, expected.Length ).ToArray() );

	private static GltfDrawData Triangle( IReadOnlyDictionary<int, ReadOnlyMemory<System.Numerics.Vector2>> uv = null ) => new()
	{
		Mode = 4, Positions = new System.Numerics.Vector3[] { new( 0, 0, 0 ), new( 1, 0, 0 ), new( 0, 1, 0 ) }, Indices = new uint[] { 0, 1, 2 }, TexCoords = uv
	};

	private static (byte[] Json, byte[] Raw) Source()
	{
		var bytes = new byte[72];
		float[] positions = [0, 0, 0, 1, 0, 0, 0, 1, 0];
		for ( var i = 0; i < 3; i++ )
		{
			for ( var component = 0; component < 3; component++ ) BinaryPrimitives.WriteSingleLittleEndian( bytes.AsSpan( i * 24 + component * 4, 4 ), positions[i * 3 + component] );
			bytes[i * 24 + 12] = (byte)(i * 63); bytes[i * 24 + 13] = 255; bytes[i * 24 + 14] = 17; bytes[i * 24 + 15] = 128;
			BinaryPrimitives.WriteUInt16LittleEndian( bytes.AsSpan( i * 24 + 16, 2 ), (ushort)(i * 32767) );
			BinaryPrimitives.WriteUInt16LittleEndian( bytes.AsSpan( i * 24 + 18, 2 ), 32768 );
			bytes[i * 24 + 20] = 255; bytes[i * 24 + 21] = (byte)(i * 127); bytes[i * 24 + 22] = 255; bytes[i * 24 + 23] = 127;
		}
		var json = "{\"asset\":{\"version\":\"2.0\"},\"buffers\":[{\"uri\":\"data:application/octet-stream;base64," + Convert.ToBase64String( bytes )
			+ "\",\"byteLength\":72}],\"bufferViews\":[{\"buffer\":0,\"byteLength\":72,\"byteStride\":24}],\"accessors\":["
			+ "{\"bufferView\":0,\"byteOffset\":0,\"componentType\":5126,\"count\":3,\"type\":\"VEC3\",\"min\":[0,0,0],\"max\":[1,1,0]},"
			+ "{\"bufferView\":0,\"byteOffset\":12,\"componentType\":5121,\"normalized\":true,\"count\":3,\"type\":\"VEC4\"},"
			+ "{\"bufferView\":0,\"byteOffset\":16,\"componentType\":5123,\"normalized\":true,\"count\":3,\"type\":\"VEC2\"},"
			+ "{\"bufferView\":0,\"byteOffset\":20,\"componentType\":5121,\"normalized\":true,\"count\":3,\"type\":\"VEC2\"}],"
			+ "\"meshes\":[{\"primitives\":[{\"attributes\":{\"POSITION\":0,\"COLOR_0\":1,\"TEXCOORD_0\":2,\"TEXCOORD_1\":3}}]}]}";
		return (Encoding.UTF8.GetBytes( json ), bytes);
	}
}
