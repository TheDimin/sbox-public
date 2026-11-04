using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;
using System;
using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;
using NumericsMatrix = System.Numerics.Matrix4x4;
using NumericsVector = System.Numerics.Vector3;

namespace Sandbox.Test.Gltf;

[TestClass]
public sealed class GltfGeometryTests
{
	private static void AssertVector( NumericsVector expected, NumericsVector actual, float tolerance = 0.0001f )
	{
		Assert.AreEqual( expected.X, actual.X, tolerance, "X" );
		Assert.AreEqual( expected.Y, actual.Y, tolerance, "Y" );
		Assert.AreEqual( expected.Z, actual.Z, tolerance, "Z" );
	}

	private static void AssertOrientedTriangles( uint[] expected, uint[] actual )
	{
		Assert.AreEqual( expected.Length, actual.Length );
		for ( var i = 0; i < expected.Length; i += 3 )
		{
			var a = expected[i];
			var b = expected[i + 1];
			var c = expected[i + 2];
			Assert.IsTrue(
				(actual[i] == a && actual[i + 1] == b && actual[i + 2] == c) ||
				(actual[i] == b && actual[i + 1] == c && actual[i + 2] == a) ||
				(actual[i] == c && actual[i + 1] == a && actual[i + 2] == b),
				$"Triangle {i / 3} has wrong vertices or winding" );
		}
	}

	[TestMethod]
	public void PointsAndExplicitLinesPreserveTheirIndices()
	{
		CollectionAssert.AreEqual( new uint[] { 7, 3, 9 }, GltfGeometry.ExpandIndices( [7, 3, 9], 0 ) );
		CollectionAssert.AreEqual( new uint[] { 7, 3, 9, 2 }, GltfGeometry.ExpandIndices( [7, 3, 9, 2], 1 ) );
	}

	[TestMethod]
	public void LineLoopClosesAndLineStripRemainsOpen()
	{
		CollectionAssert.AreEqual( new uint[] { 7, 8, 8, 9, 9, 7 }, GltfGeometry.ExpandIndices( [7, 8, 9], 2 ) );
		CollectionAssert.AreEqual( new uint[] { 7, 8, 8, 9 }, GltfGeometry.ExpandIndices( [7, 8, 9], 3 ) );
	}

	[TestMethod]
	public void TriangleListPreservesVertexOrder()
	{
		CollectionAssert.AreEqual( new uint[] { 2, 3, 5, 5, 3, 7 },
			GltfGeometry.ExpandIndices( [2, 3, 5, 5, 3, 7], 4 ) );
	}

	[TestMethod]
	public void TriangleStripAlternatesOrderToPreserveFaceWinding()
	{
		AssertOrientedTriangles( [0, 1, 2, 2, 1, 3, 2, 3, 4],
			GltfGeometry.ExpandIndices( [0, 1, 2, 3, 4], 5 ) );
	}

	[TestMethod]
	public void TriangleFanKeepsTheFirstVertexAsItsCenter()
	{
		AssertOrientedTriangles( [4, 8, 2, 4, 2, 9, 4, 9, 1],
			GltfGeometry.ExpandIndices( [4, 8, 2, 9, 1], 6 ) );
	}

	[TestMethod]
	public void UnknownPrimitiveModeIsRejected()
	{
		Assert.ThrowsException<GltfException>( () => GltfGeometry.ExpandIndices( [0, 1, 2], 7 ) );
	}

	[TestMethod]
	public void PositionConversionUsesGltfAxisBasisAndMetersToInches()
	{
		const float metersToInches = 1.0f / 0.0254f;
		AssertVector( new NumericsVector( 0, metersToInches, 0 ), GltfGeometry.ConvertPosition( NumericsVector.UnitX ) );
		AssertVector( new NumericsVector( 0, 0, metersToInches ), GltfGeometry.ConvertPosition( NumericsVector.UnitY ) );
		AssertVector( new NumericsVector( metersToInches, 0, 0 ), GltfGeometry.ConvertPosition( NumericsVector.UnitZ ) );
		AssertVector( new NumericsVector( 3, 1, 2 ) * metersToInches,
			GltfGeometry.ConvertPosition( new NumericsVector( 1, 2, 3 ) ) );
	}

	[TestMethod]
	public void DirectionConversionChangesBasisWithoutChangingLength()
	{
		var original = new NumericsVector( 1, -2, 3 );
		var converted = GltfGeometry.ConvertDirection( original );
		AssertVector( new NumericsVector( 3, 1, -2 ), converted );
		Assert.AreEqual( original.Length(), converted.Length(), 0.00001f );
	}

	[TestMethod]
	public void TransformConversionPreservesTransformedPointsUnderTheBasisChange()
	{
		var source = NumericsMatrix.CreateScale( 2, -1, 0.5f ) *
			NumericsMatrix.CreateRotationY( MathF.PI / 3 ) *
			NumericsMatrix.CreateTranslation( 1, 2, -3 );
		var point = new NumericsVector( -0.5f, 2, 4 );
		var expected = GltfGeometry.ConvertPosition( NumericsVector.Transform( point, source ) );
		var actual = NumericsVector.Transform( GltfGeometry.ConvertPosition( point ), GltfGeometry.ConvertTransform( source ) );

		AssertVector( expected, actual, 0.001f );
	}
}
