using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;
using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace Sandbox.Tests.Gltf;

[TestClass]
public class GltfPoseEdgeTests
{
	[TestMethod]
	public void PrimitiveWithoutMorphTargetsRemainsUndeformedWhenMeshHasWeights()
	{
		var fixture = new DeformationFixture();
		var position = fixture.Floats( "VEC3", 1, [1, 2, 3], true );
		var delta = fixture.Floats( "VEC3", 1, [2, 0, 0], true );
		using var document = fixture.Read( $$$"""
			"meshes":[{"weights":[1],"primitives":[{"mode":0,"attributes":{"POSITION":{{{position}}} }},
			 {"mode":0,"attributes":{"POSITION":{{{position}}} },"targets":[{"POSITION":{{{delta}}} }]}]}],
			"nodes":[{"mesh":0,"translation":[3,0,0]}]
			""" );
		Assert.AreEqual( new Vector3( 4, 2, 3 ), document.EvaluateVertexPosition( 0, 0, 0 ) );
		Assert.AreEqual( new Vector3( 6, 2, 3 ), document.EvaluateVertexPosition( 0, 1, 0 ) );
	}

	[TestMethod]
	public void PoseReturnsIndependentCopiesOfItsMorphWeights()
	{
		var fixture = new DeformationFixture();
		var position = fixture.Floats( "VEC3", 1, [0, 0, 0], true );
		using var document = fixture.Read( $$$"""
			"meshes":[{"weights":[0.25],"primitives":[{"mode":0,"attributes":{"POSITION":{{{position}}} },"targets":[{}]}]}],
			"nodes":[{"mesh":0}]
			""" );
		var pose = document.EvaluatePose();
		var weights = pose.GetMorphWeights( 0 );
		weights[0] = 1;
		Assert.AreEqual( 0.25f, pose.GetMorphWeights( 0 )[0] );
		Assert.AreEqual( 0.25f, document.GetMorphWeights( 0 )[0] );
	}

	[TestMethod]
	public void VertexIndexSelectsTheRequestedPositionAndIsRangeChecked()
	{
		using var document = SceneFeatureFixture.ReadFloats( [1, 2, 3, 4, 5, 6], """
			"bufferViews":[{"buffer":0,"byteLength":24}],
			"accessors":[{"bufferView":0,"componentType":5126,"count":2,"type":"VEC3","min":[1,2,3],"max":[4,5,6]}],
			"meshes":[{"primitives":[{"mode":0,"attributes":{"POSITION":0}}]}],
			"nodes":[{"mesh":0}]
			""" );
		Assert.AreEqual( new Vector3( 4, 5, 6 ), document.EvaluateVertexPosition( 0, 0, 1 ) );
		Assert.ThrowsException<GltfException>( () => document.EvaluateVertexPosition( 0, 0, 2 ) );
	}
}
