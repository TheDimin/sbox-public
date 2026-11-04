using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;
using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;
using Matrix = System.Numerics.Matrix4x4;
using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;

namespace Sandbox.Tests.Gltf;

[TestClass]
public sealed class GltfEvaluationEdgeTests
{
	private static GltfDocument Read( string json, params string[] supported ) =>
		GltfReader.Read( Encoding.UTF8.GetBytes( json ), supportedExtensions: new HashSet<string>( supported, StringComparer.Ordinal ) );

	private static void AssertValues( float[] expected, float[] actual )
	{
		Assert.AreEqual( expected.Length, actual.Length );
		for ( var i = 0; i < expected.Length; i++ )
			Assert.AreEqual( expected[i], actual[i], 0.00001f, $"Component {i}" );
	}

	private static bool SameOrientedTriangle( uint a, uint b, uint c, uint[] result, int start ) =>
		(result[start] == a && result[start + 1] == b && result[start + 2] == c) ||
		(result[start] == b && result[start + 1] == c && result[start + 2] == a) ||
		(result[start] == c && result[start + 1] == a && result[start + 2] == b);

	[TestMethod]
	public void LinearAnimationSupportsMoreThanFourMorphWeights()
	{
		float[] values = [0, 0.25f, 0.5f, 0.75f, 1, 1, 0.75f, 0.5f, 0.25f, 0];
		AssertValues( [0.5f, 0.5f, 0.5f, 0.5f, 0.5f],
			GltfAnimationSampler.Sample( [0, 2], values, 5, "LINEAR", 1 ) );
	}

	[TestMethod]
	public void CubicAnimationUsesOutgoingAndIncomingTangentsWithoutChangingKeyValues()
	{
		float[] values = [111, -99, 2, 3, -2, 4, 8, -6, 10, 20, 777, 999];
		AssertValues( [2, 3], GltfAnimationSampler.Sample( [0, 2], values, 2, "CUBICSPLINE", 0 ) );
		AssertValues( [3.5f, 14], GltfAnimationSampler.Sample( [0, 2], values, 2, "CUBICSPLINE", 1 ) );
		AssertValues( [10, 20], GltfAnimationSampler.Sample( [0, 2], values, 2, "CUBICSPLINE", 2 ) );
	}

	[TestMethod]
	public void QuaternionKeyAtExactTimeUsesItsStoredValue()
	{
		float[] values = [0, 0, 0, 1, 0, 0, 0, -1, 0, 0, 0, 1];
		AssertValues( [0, 0, 0, -1], GltfAnimationSampler.Sample( [0, 1, 2], values, 4, "LINEAR", 1, rotation: true ) );
	}

	[TestMethod]
	public void AnimationRejectsNegativeTimestampsAndSingleKeyCubicSpline()
	{
		Assert.ThrowsException<GltfException>( () => GltfAnimationSampler.Sample( [-1, 1], [0, 1], 1, "LINEAR", 0 ) );
		Assert.ThrowsException<GltfException>( () => GltfAnimationSampler.Sample( [0], [0, 1, 0], 1, "CUBICSPLINE", 0 ) );
	}

	[TestMethod]
	public void AnimationRejectsNonFiniteInputAndOutputEvenAtAnEarlierKey()
	{
		Assert.ThrowsException<GltfException>( () => GltfAnimationSampler.Sample( [0, float.PositiveInfinity], [1, 2], 1, "LINEAR", 0 ) );
		Assert.ThrowsException<GltfException>( () => GltfAnimationSampler.Sample( [0, 1], [1, float.NaN], 1, "LINEAR", 0 ) );
		Assert.ThrowsException<GltfException>( () => GltfAnimationSampler.Sample( [0, 1], [1, float.NegativeInfinity], 1, "LINEAR", 0 ) );
	}

	[TestMethod]
	[DataRow( 0 )]
	[DataRow( 1 )]
	[DataRow( 2 )]
	[DataRow( 3 )]
	[DataRow( 4 )]
	[DataRow( 5 )]
	[DataRow( 6 )]
	public void EmptyPrimitiveIndexArraysAreInvalid( int mode )
	{
		Assert.ThrowsException<GltfException>( () => GltfGeometry.ExpandIndices( [], mode ) );
	}

	[TestMethod]
	public void PrimitiveCountsMustMatchTheirTopology()
	{
		Assert.ThrowsException<GltfException>( () => GltfGeometry.ExpandIndices( [0], 2 ) );
		Assert.ThrowsException<GltfException>( () => GltfGeometry.ExpandIndices( [0], 3 ) );
		Assert.ThrowsException<GltfException>( () => GltfGeometry.ExpandIndices( [0, 1, 2], 1 ) );
		Assert.ThrowsException<GltfException>( () => GltfGeometry.ExpandIndices( [0, 1], 4 ) );
		Assert.ThrowsException<GltfException>( () => GltfGeometry.ExpandIndices( [0, 1], 5 ) );
		Assert.ThrowsException<GltfException>( () => GltfGeometry.ExpandIndices( [0, 1], 6 ) );
	}

	[TestMethod]
	public void DegenerateTrianglesDoNotResetTriangleStripParity()
	{
		var triangles = GltfGeometry.ExpandIndices( [0, 1, 2, 2, 3, 4], 5 );
		Assert.IsTrue( triangles.Length >= 6 && triangles.Length % 3 == 0 );
		Assert.IsTrue( SameOrientedTriangle( 0, 1, 2, triangles, 0 ) );
		Assert.IsTrue( SameOrientedTriangle( 3, 2, 4, triangles, triangles.Length - 3 ) );
	}

	[TestMethod]
	public void BasisConversionPreservesHandednessAndNegativeTransformDeterminants()
	{
		var x = GltfGeometry.ConvertDirection( Vector3.UnitX );
		var y = GltfGeometry.ConvertDirection( Vector3.UnitY );
		var z = GltfGeometry.ConvertDirection( Vector3.UnitZ );
		var cross = Vector3.Cross( x, y );
		Assert.AreEqual( z.X, cross.X, 0.00001f );
		Assert.AreEqual( z.Y, cross.Y, 0.00001f );
		Assert.AreEqual( z.Z, cross.Z, 0.00001f );
		var converted = GltfGeometry.ConvertTransform( Matrix.CreateScale( -2, 3, 4 ) );
		Assert.AreEqual( -24.0f, converted.GetDeterminant(), 0.0001f );
	}

	[TestMethod]
	public void UnsupportedOptionalTextureTransformUsesCoreTextureInfo()
	{
		using var document = Read( """
			{"asset":{"version":"2.0"},"extensionsUsed":["KHR_texture_transform"],
			 "images":[{"uri":"data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4////fwAJ+wP9KobjigAAAABJRU5ErkJggg=="}],
			 "textures":[{"source":0}],"materials":[{"pbrMetallicRoughness":{"baseColorTexture":{"index":0,"texCoord":1,
			 "extensions":{"KHR_texture_transform":{"offset":[0.25,0.5],"scale":[2,3],"texCoord":2}}}}}]}
			""" );
		var texture = document.ReadMaterial( 0 ).BaseColorTexture ?? throw new AssertFailedException( "Missing base color texture" );
		Assert.AreEqual( 1, texture.TexCoord );
		Assert.AreEqual( Vector2.Zero, texture.Offset );
		Assert.AreEqual( Vector2.One, texture.Tiling );
		Assert.AreEqual( new Vector2( 0.1f, 0.2f ), texture.TransformUv( new Vector2( 0.1f, 0.2f ) ) );
	}

	[TestMethod]
	[DataRow( "\"magFilter\":9987" )]
	[DataRow( "\"minFilter\":123" )]
	[DataRow( "\"wrapS\":123" )]
	[DataRow( "\"wrapT\":123" )]
	public void SamplerRejectsInvalidFilterAndWrapEnums( string property )
	{
		var json = "{\"asset\":{\"version\":\"2.0\"},\"samplers\":[{" + property + "}]}";
		Assert.ThrowsException<GltfException>( () =>
		{
			using var document = Read( json );
			_ = document.ReadSampler( 0 );
		} );
	}

	[TestMethod]
	public void MissingPrimitiveMaterialUsesDefaultWhenVariantHasNoMapping()
	{
		using var document = Read( """
			{"asset":{"version":"2.0"},"extensionsUsed":["KHR_materials_variants"],
			 "extensions":{"KHR_materials_variants":{"variants":[{"name":"Red"}]}},
			 "meshes":[{"primitives":[{"attributes":{"POSITION":0}}]}],
			 "accessors":[{"componentType":5126,"count":3,"type":"VEC3","min":[0,0,0],"max":[0,0,0]}]}
			""", "KHR_materials_variants" );
		Assert.IsNull( document.GetMaterialForVariant( 0, 0, null ) );
		Assert.IsNull( document.GetMaterialForVariant( 0, 0, 0 ) );
	}

	[TestMethod]
	public void DuplicateVariantMappingOnOnePrimitiveIsInvalid()
	{
		Assert.ThrowsException<GltfException>( () =>
		{
			using var document = Read( """
				{"asset":{"version":"2.0"},"extensionsUsed":["KHR_materials_variants"],
				 "extensions":{"KHR_materials_variants":{"variants":[{"name":"Red"}]}},"materials":[{},{}],
				 "meshes":[{"primitives":[{"attributes":{"POSITION":0},"extensions":{"KHR_materials_variants":{
				   "mappings":[{"material":0,"variants":[0]},{"material":1,"variants":[0]}]
				 }}}]}],"accessors":[{"componentType":5126,"count":3,"type":"VEC3","min":[0,0,0],"max":[0,0,0]}]}
				""", "KHR_materials_variants" );
			_ = document.GetMaterialForVariant( 0, 0, 0 );
		} );
	}
}
