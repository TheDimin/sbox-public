using System;
using System.Numerics;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;

namespace Sandbox.Tests.Gltf;

[TestClass]
public class GltfAnimatedDeformationTests
{
	[TestMethod]
	public void UnskinnedVertexFollowsAnimatedParentAndRetainsChildScale()
	{
		var fixture = new DeformationFixture();
		var position = fixture.Floats( "VEC3", 1, [1, 2, 3], true );
		var times = fixture.Floats( "SCALAR", 2, [0, 2], true );
		var translation = fixture.Floats( "VEC3", 2, [0, 0, 0, 4, 0, 0] );
		using var document = fixture.Read( $$$"""
			"meshes":[{"primitives":[{"mode":0,"attributes":{"POSITION":{{{position}}} }}]}],
			"nodes":[{"children":[1]},{"mesh":0,"scale":[2,3,4]}],
			"animations":[{"samplers":[{"input":{{{times}}},"output":{{{translation}}} }],"channels":[{"sampler":0,"target":{"node":0,"path":"translation"}}]}]
			""" );
		var first = document.EvaluatePose( 0, 1 );
		var last = document.EvaluatePose( 0, 2 );
		AssertVector( new Vector3( 4, 6, 12 ), document.EvaluateVertexPosition( 1, 0, 0, first ) );
		AssertVector( new Vector3( 6, 6, 12 ), document.EvaluateVertexPosition( 1, 0, 0, last ) );
		AssertVector( new Vector3( 2, 6, 12 ), document.EvaluateVertexPosition( 1, 0, 0 ) );
	}

	[TestMethod]
	public void AnimatedMorphAndJointHierarchyApplyAllInfluencesBeforeWorldTransform()
	{
		using var document = AnimatedSkinnedFixture();
		var pose = document.EvaluatePose( 0, 1 );
		// Morph: (1,0,0) + (-0.5)*(2,0,0) = origin.
		// Inverse-bind first joint translates x by -1; joint x positions are 0,2,4,6,8.
		// Weights are .125 for the first four and .5 for the fifth: x = 5.375.
		// The animated joint parent contributes y = 1 to all influences. The mesh node's
		// independent translation (100,200,300) does not transform skinned vertices.
		AssertVector( new Vector3( 5.375f, 1, 0 ), document.EvaluateVertexPosition( 0, 0, 0, pose ) );
		Assert.AreEqual( -.5f, pose.GetMorphWeights( 0 )[0], .000001f );
		AssertVector( new Vector3( 6.5f, 0, 0 ), document.EvaluateVertexPosition( 0, 0, 0 ) );
	}

	[TestMethod]
	public void CompiledGlbPreservesAnimatedDeformationAcrossSampleTimes()
	{
		using var source = AnimatedSkinnedFixture();
		var sourceJson = Encoding.UTF8.GetBytes( source.Root.GetRawText() );
		var glb = GltfCompiler.Compile( sourceJson );
		using var compiled = GltfReader.Read( glb );
		foreach ( var time in new[] { 0f, .5f, 1f, 2f } )
		{
			var fromSource = source.EvaluateVertexPosition( 0, 0, 0, source.EvaluatePose( 0, time ) );
			var fromCompiled = compiled.EvaluateVertexPosition( 0, 0, 0, compiled.EvaluatePose( 0, time ) );
			AssertVector( fromSource, fromCompiled );
		}
	}

	[TestMethod]
	public void PoseMustBeNonNullAndBelongToTheEvaluatedDocument()
	{
		using var source = AnimatedSkinnedFixture();
		using var other = AnimatedSkinnedFixture();
		Assert.ThrowsException<ArgumentNullException>( () => source.EvaluateVertexPosition( 0, 0, 0, null ) );
		Assert.ThrowsException<ArgumentException>( () => source.EvaluateVertexPosition( 0, 0, 0, other.EvaluatePose() ) );
	}

	private static GltfDocument AnimatedSkinnedFixture()
	{
		var fixture = new DeformationFixture();
		var position = fixture.Floats( "VEC3", 1, [1, 0, 0], true );
		var delta = fixture.Floats( "VEC3", 1, [2, 0, 0], true );
		var joints0 = fixture.Bytes( "VEC4", 1, [0, 1, 2, 3] );
		var weights0 = fixture.Floats( "VEC4", 1, [.125f, .125f, .125f, .125f] );
		var joints1 = fixture.Bytes( "VEC4", 1, [4, 0, 0, 0] );
		var weights1 = fixture.Floats( "VEC4", 1, [.5f, 0, 0, 0] );
		var inverse = fixture.Floats( "MAT4", 5, [
			2, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, -1, 0, 0, 1,
			1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1,
			1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1,
			1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1,
			1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1] );
		var times = fixture.Floats( "SCALAR", 2, [0, 2], true );
		var translations = fixture.Floats( "VEC3", 2, [0, 0, 0, 0, 2, 0] );
		var morphWeights = fixture.Floats( "SCALAR", 2, [0, -1] );
		return fixture.Read( $$$"""
			"meshes":[{"weights":[0],"primitives":[{"mode":0,"attributes":{"POSITION":{{{position}}},"JOINTS_0":{{{joints0}}},"WEIGHTS_0":{{{weights0}}},"JOINTS_1":{{{joints1}}},"WEIGHTS_1":{{{weights1}}} },"targets":[{"POSITION":{{{delta}}} }]}]}],
			"nodes":[{"mesh":0,"skin":0,"translation":[100,200,300]},{},{"translation":[2,0,0]},{"translation":[4,0,0]},
			         {"translation":[6,0,0]},{"translation":[8,0,0]},{"children":[1,2,3,4,5]}],
			"skins":[{"joints":[1,2,3,4,5],"inverseBindMatrices":{{{inverse}}} }],
			"animations":[{"samplers":[{"input":{{{times}}},"output":{{{translations}}} },{"input":{{{times}}},"output":{{{morphWeights}}} }],
			  "channels":[{"sampler":0,"target":{"node":6,"path":"translation"}},{"sampler":1,"target":{"node":0,"path":"weights"}}]}]
			""" );
	}

	private static void AssertVector( Vector3 expected, Vector3 actual )
	{
		Assert.AreEqual( expected.X, actual.X, .00001f );
		Assert.AreEqual( expected.Y, actual.Y, .00001f );
		Assert.AreEqual( expected.Z, actual.Z, .00001f );
	}
}
