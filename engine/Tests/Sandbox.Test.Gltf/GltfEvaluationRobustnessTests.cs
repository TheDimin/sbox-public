using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;
using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace Sandbox.Tests.Gltf;

[TestClass]
public sealed class GltfEvaluationRobustnessTests
{
	[TestMethod]
	public void QuaternionInterpolationDoesNotMutateSourceKeysWhenChoosingShortestPath()
	{
		float[] times = [0, 1];
		float[] values = [0, 0, 0, 1, 0, 0, -0.8660254f, -0.5f];
		var originalTimes = (float[])times.Clone();
		var originalValues = (float[])values.Clone();

		_ = GltfAnimationSampler.Sample( times, values, 4, "LINEAR", 0.5f, rotation: true );

		CollectionAssert.AreEqual( originalTimes, times );
		CollectionAssert.AreEqual( originalValues, values );
	}

	[TestMethod]
	public void InvalidQuaternionDimensionsAndZeroLengthKeysAreRejected()
	{
		Assert.ThrowsException<GltfException>( () => GltfAnimationSampler.Sample( [0, 1], [0, 0, 1, 0, 0, 1], 3, "LINEAR", 0.5f, rotation: true ) );
		Assert.ThrowsException<GltfException>( () => GltfAnimationSampler.Sample( [0, 1], [0, 0, 0, 1, 0, 0, 0, 0], 4, "LINEAR", 0.5f, rotation: true ) );
	}

	[TestMethod]
	public void CubicRotationNormalizesBeforeNarrowingLargeFiniteTangents()
	{
		float[] values =
		[
			0, 0, 0, 0, 0, 0, 0, 1, float.MaxValue, 0, 0, 0,
			-float.MaxValue, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0
		];

		var result = GltfAnimationSampler.Sample( [0, 8], values, 4, "CUBICSPLINE", 4, rotation: true );

		Assert.AreEqual( 1f, result[0], 0.00001f );
		Assert.AreEqual( 0f, result[1], 0.00001f );
		Assert.AreEqual( 0f, result[2], 0.00001f );
		Assert.AreEqual( 0f, result[3], 0.00001f );
	}

	[TestMethod]
	public void NonFiniteSampleTimeIsRejected()
	{
		Assert.ThrowsException<GltfException>( () => GltfAnimationSampler.Sample( [0, 1], [0, 1], 1, "LINEAR", float.NaN ) );
		Assert.ThrowsException<GltfException>( () => GltfAnimationSampler.Sample( [0, 1], [0, 1], 1, "LINEAR", float.PositiveInfinity ) );
	}
}
