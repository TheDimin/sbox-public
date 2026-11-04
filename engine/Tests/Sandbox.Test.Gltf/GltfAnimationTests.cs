using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;
using System;
using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace Sandbox.Test.Gltf;

[TestClass]
public sealed class GltfAnimationTests
{
	private static void AssertValues( float[] expected, float[] actual, float tolerance = 0.00001f )
	{
		Assert.AreEqual( expected.Length, actual.Length );
		for ( var i = 0; i < expected.Length; i++ )
			Assert.AreEqual( expected[i], actual[i], tolerance, $"Component {i}" );
	}

	[TestMethod]
	public void StepChangesAtTheKeyRatherThanBeforeIt()
	{
		float[] times = [0, 0.7f, 2];
		float[] values = [10, 20, 30];

		AssertValues( [10], GltfAnimationSampler.Sample( times, values, 1, "STEP", -1 ) );
		AssertValues( [10], GltfAnimationSampler.Sample( times, values, 1, "STEP", 0.699f ) );
		AssertValues( [20], GltfAnimationSampler.Sample( times, values, 1, "STEP", 0.7f ) );
		AssertValues( [20], GltfAnimationSampler.Sample( times, values, 1, "STEP", 1.9f ) );
		AssertValues( [30], GltfAnimationSampler.Sample( times, values, 1, "STEP", 3 ) );
	}

	[TestMethod]
	public void LinearInterpolatesEachComponentUsingActualKeyTimes()
	{
		float[] times = [0.5f, 2.5f, 5];
		float[] values = [0, 4, 8, 4, 8, 12, 10, 2, 0];

		AssertValues( [0, 4, 8], GltfAnimationSampler.Sample( times, values, 3, "LINEAR", 0 ) );
		AssertValues( [1, 5, 9], GltfAnimationSampler.Sample( times, values, 3, "LINEAR", 1 ) );
		AssertValues( [4, 8, 12], GltfAnimationSampler.Sample( times, values, 3, "LINEAR", 2.5f ) );
		AssertValues( [10, 2, 0], GltfAnimationSampler.Sample( times, values, 3, "LINEAR", 10 ) );
	}

	[TestMethod]
	public void CubicSplineScalesTangentsBySegmentDuration()
	{
		float[] times = [1, 3];
		// Per key: in-tangent, value, out-tangent. The first out-tangent is 2 units/second.
		float[] values = [0, 0, 2, 0, 2, 0];

		AssertValues( [0], GltfAnimationSampler.Sample( times, values, 1, "CUBICSPLINE", 1 ) );
		AssertValues( [1.5f], GltfAnimationSampler.Sample( times, values, 1, "CUBICSPLINE", 2 ) );
		AssertValues( [2], GltfAnimationSampler.Sample( times, values, 1, "CUBICSPLINE", 3 ) );
	}

	[TestMethod]
	public void CubicSplineUsesVectorTangentsForEachComponent()
	{
		float[] times = [0, 2];
		float[] values = [
			0, 0, 0, 0, 0, 0, 2, -2, 0,
			0, 0, 0, 2, 4, 8, 0, 0, 0
		];

		AssertValues( [1.5f, 1.5f, 4], GltfAnimationSampler.Sample( times, values, 3, "CUBICSPLINE", 1 ) );
	}

	[TestMethod]
	public void LinearRotationUsesShortestPathSphericalInterpolation()
	{
		float[] times = [0, 1];
		// Both last keys encode the same 120-degree Z rotation, with opposite quaternion signs.
		float[] positive = [0, 0, 0, 1, 0, 0, 0.8660254f, 0.5f];
		float[] negative = [0, 0, 0, 1, 0, 0, -0.8660254f, -0.5f];
		float[] expected = [0, 0, 0.25881905f, 0.9659258f];

		AssertValues( expected, GltfAnimationSampler.Sample( times, positive, 4, "LINEAR", 0.25f, rotation: true ) );
		AssertValues( expected, GltfAnimationSampler.Sample( times, negative, 4, "LINEAR", 0.25f, rotation: true ) );
	}

	[TestMethod]
	public void CubicRotationNormalizesTheSplineWithoutFlippingKeys()
	{
		float[] times = [0, 1];
		float[] values = [
			0, 0, 0, 0,  0, 0, 0, 1,  0, 0, 0, 0,
			0, 0, 0, 0,  0, 0, -0.8f, -0.6f,  0, 0, 0, 0
		];

		AssertValues( [0, 0, -0.8944272f, 0.4472136f],
			GltfAnimationSampler.Sample( times, values, 4, "CUBICSPLINE", 0.5f, rotation: true ) );
	}

	[TestMethod]
	public void SingleKeyStaysConstantOutsideItsTime()
	{
		float[] times = [2];
		float[] values = [1, 2, 3];
		AssertValues( values, GltfAnimationSampler.Sample( times, values, 3, "LINEAR", 0 ) );
		AssertValues( values, GltfAnimationSampler.Sample( times, values, 3, "LINEAR", 3 ) );
	}

	[TestMethod]
	public void RejectsInvalidTimesAndValueCounts()
	{
		Assert.ThrowsException<GltfException>( () => GltfAnimationSampler.Sample( [], [], 1, "LINEAR", 0 ) );
		Assert.ThrowsException<GltfException>( () => GltfAnimationSampler.Sample( [0, 0], [1, 2], 1, "LINEAR", 0 ) );
		Assert.ThrowsException<GltfException>( () => GltfAnimationSampler.Sample( [1, 0], [1, 2], 1, "LINEAR", 0 ) );
		Assert.ThrowsException<GltfException>( () => GltfAnimationSampler.Sample( [0, float.NaN], [1, 2], 1, "LINEAR", 0 ) );
		Assert.ThrowsException<GltfException>( () => GltfAnimationSampler.Sample( [0, 1], [1, 2, 3], 2, "LINEAR", 0 ) );
		Assert.ThrowsException<GltfException>( () => GltfAnimationSampler.Sample( [0, 1], [1, 2], 1, "CUBICSPLINE", 0 ) );
		Assert.ThrowsException<GltfException>( () => GltfAnimationSampler.Sample( [0, 1], [1, 2], 1, "UNKNOWN", 0 ) );
	}
}
