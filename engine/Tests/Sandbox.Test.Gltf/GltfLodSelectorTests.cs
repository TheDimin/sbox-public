using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;

namespace Sandbox.Tests.Gltf;

[TestClass]
public class GltfLodSelectorTests
{
	static readonly float[] Spec = [0.5f, 0.2f, 0.01f];

	[TestMethod]
	public void SpecExampleRangesSelectLevelsAndCullBelowLast()
	{
		Assert.AreEqual( 0, GltfLodSelector.Select( Spec, 1.0f ) );
		Assert.AreEqual( 0, GltfLodSelector.Select( Spec, 0.5f ) );
		Assert.AreEqual( 1, GltfLodSelector.Select( Spec, 0.3f ) );
		Assert.AreEqual( 2, GltfLodSelector.Select( Spec, 0.05f ) );
		Assert.AreEqual( -1, GltfLodSelector.Select( Spec, 0.001f ) );
	}

	[TestMethod]
	public void ZeroLastThresholdNeverCulls()
	{
		Assert.AreEqual( 2, GltfLodSelector.Select( GltfLodSelector.DefaultThresholds( 3 ), 0f ) );
		CollectionAssert.AreEqual( new[] { 0.5f, 0.25f, 0f }, GltfLodSelector.DefaultThresholds( 3 ) );
	}

	[TestMethod]
	public void HysteresisKeepsLevelNearBoundaryButNotFarFromIt()
	{
		Assert.AreEqual( 0, GltfLodSelector.Select( Spec, 0.48f, current: 0 ) );
		Assert.AreEqual( 1, GltfLodSelector.Select( Spec, 0.48f, current: 1 ) );
		Assert.AreEqual( 1, GltfLodSelector.Select( Spec, 0.40f, current: 0 ) );
		Assert.AreEqual( 0, GltfLodSelector.Select( Spec, 0.9f, current: 1 ) );
	}

	[TestMethod]
	public void ForcedLevelAndBiasStayInsideChain()
	{
		Assert.AreEqual( 2, GltfLodSelector.Adjust( 0, 3, forced: 5, bias: 0 ) );
		Assert.AreEqual( 0, GltfLodSelector.Adjust( 2, 3, forced: 0, bias: 4 ) );
		Assert.AreEqual( 2, GltfLodSelector.Adjust( 1, 3, forced: null, bias: 4 ) );
		Assert.AreEqual( 0, GltfLodSelector.Adjust( 1, 3, forced: null, bias: -4 ) );
		Assert.AreEqual( -1, GltfLodSelector.Adjust( -1, 3, forced: null, bias: 1 ) );
	}

	[TestMethod]
	public void CoverageFallsWithDistanceAndClamps()
	{
		var near = GltfLodSelector.Coverage( 1, 3, 60 );
		var far = GltfLodSelector.Coverage( 1, 30, 60 );
		Assert.IsTrue( near > far && far > 0 );
		Assert.AreEqual( 1f, GltfLodSelector.Coverage( 1, 0.5f, 60 ) );
		Assert.AreEqual( 0f, GltfLodSelector.Coverage( 0, 5, 60 ) );
	}
}
