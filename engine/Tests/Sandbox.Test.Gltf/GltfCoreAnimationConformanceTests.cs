using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;
using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace Sandbox.Tests.Gltf;

/// <summary>
/// Reader/CPU conformance cases for glTF 2.0 sections 3.6, 3.7.3, 3.11 and appendix C.
/// These tests do not establish native sequence, component or rendered deformation support.
/// </summary>
[TestClass]
public sealed class GltfCoreAnimationConformanceTests
{
	[TestMethod]
	public void MixedChannelTimeRangesClampIndependentlyWithoutMovingTheClipOrigin()
	{
		var fixture = new Fixture();
		var position = fixture.Floats( "VEC3", [1, 0, 0], bounds: true );
		var delta = fixture.Floats( "VEC3", [2, 0, 0], bounds: true );
		var translationTimes = fixture.Floats( "SCALAR", [1, 3], bounds: true );
		var translations = fixture.Floats( "VEC3", [2, 0, 0, 6, 0, 0] );
		var rotationTimes = fixture.Floats( "SCALAR", [0, 4], bounds: true );
		var rotations = fixture.Floats( "VEC4", [0, 0, 0, 1, 0, 0, 1, 0] );
		var scaleTimes = fixture.Floats( "SCALAR", [2, 6], bounds: true );
		var scales = fixture.Floats( "VEC3", [2, 3, 4, 6, 3, 4] );
		var morphTimes = fixture.Floats( "SCALAR", [.5f, 1.5f], bounds: true );
		var weights = fixture.Floats( "SCALAR", [-1, 3] );
		using var document = fixture.Read( $$$$$$"""
			"meshes":[{"primitives":[{"mode":0,"attributes":{"POSITION":{{{{{{position}}}}}}},"targets":[{"POSITION":{{{{{{delta}}}}}}}]}]}],
			"nodes":[{"mesh":0}],
			"animations":[{"samplers":[
			 {"input":{{{{{{translationTimes}}}}}},"output":{{{{{{translations}}}}}}},
			 {"input":{{{{{{rotationTimes}}}}}},"output":{{{{{{rotations}}}}}}},
			 {"input":{{{{{{scaleTimes}}}}}},"output":{{{{{{scales}}}}}}},
			 {"input":{{{{{{morphTimes}}}}}},"output":{{{{{{weights}}}}}}}],
			 "channels":[{"sampler":0,"target":{"node":0,"path":"translation"}},
			 {"sampler":1,"target":{"node":0,"path":"rotation"}},
			 {"sampler":2,"target":{"node":0,"path":"scale"}},
			 {"sampler":3,"target":{"node":0,"path":"weights"}}]}]
			""" );

		AssertVector( Vector3.Zero, document.EvaluateVertexPosition( 0, 0, 0, document.EvaluatePose( 0, 0 ) ) );
		// At one second: morphed x=3, scale x=2, rotate 45 degrees, translate x=2.
		var diagonal = 3 * MathF.Sqrt( 2 );
		AssertVector( new Vector3( 2 + diagonal, diagonal, 0 ), document.EvaluateVertexPosition( 0, 0, 0, document.EvaluatePose( 0, 1 ) ) );
		// At three seconds: the morph track has ended, but rotation and scale continue.
		var laterDiagonal = 21 / MathF.Sqrt( 2 );
		AssertVector( new Vector3( 6 - laterDiagonal, laterDiagonal, 0 ), document.EvaluateVertexPosition( 0, 0, 0, document.EvaluatePose( 0, 3 ) ) );
	}

	[TestMethod]
	[DataRow( "translation", 2f, 3f, 4f, 6f, 7f, 8f )]
	[DataRow( "rotation", 1f, 1f, 1f, -1f, -1f, 1f )]
	[DataRow( "scale", 1f, 2f, 3f, 0f, -2f, 4f )]
	[DataRow( "weights", 1f, 1f, 1f, 3f, 1f, 1f )]
	public void StepChannelsChangeAtTheAuthoredKeyBetweenItsImmediateFloatNeighbors( string path,
		float beforeX, float beforeY, float beforeZ, float atX, float atY, float atZ )
	{
		var fixture = new Fixture();
		var position = fixture.Floats( "VEC3", [1, 1, 1], bounds: true );
		var delta = fixture.Floats( "VEC3", [1, 0, 0], bounds: true );
		var times = fixture.Floats( "SCALAR", [.125f, .7f, 2.125f], bounds: true );
		var output = path switch
		{
			"translation" => fixture.Floats( "VEC3", [1, 2, 3, 5, 6, 7, -1, -2, -3] ),
			"rotation" => fixture.Floats( "VEC4", [0, 0, 0, 1, 0, 0, 1, 0, 1, 0, 0, 0] ),
			"scale" => fixture.Floats( "VEC3", [1, 2, 3, 0, -2, 4, 5, 6, 7] ),
			_ => fixture.Floats( "SCALAR", [0, 2, -1] )
		};
		using var document = fixture.Read( $$$$$$"""
			"meshes":[{"primitives":[{"mode":0,"attributes":{"POSITION":{{{{{{position}}}}}}},"targets":[{"POSITION":{{{{{{delta}}}}}}}]}]}],
			"nodes":[{"mesh":0}],"animations":[{"samplers":[{"input":{{{{{{times}}}}}},"output":{{{{{{output}}}}}},"interpolation":"STEP"}],
			"channels":[{"sampler":0,"target":{"node":0,"path":"{{{{{{path}}}}}}"}}]}]
			""" );

		AssertVector( new Vector3( beforeX, beforeY, beforeZ ), document.EvaluateVertexPosition( 0, 0, 0, document.EvaluatePose( 0, MathF.BitDecrement( .7f ) ) ) );
		AssertVector( new Vector3( atX, atY, atZ ), document.EvaluateVertexPosition( 0, 0, 0, document.EvaluatePose( 0, .7f ) ) );
		AssertVector( new Vector3( atX, atY, atZ ), document.EvaluateVertexPosition( 0, 0, 0, document.EvaluatePose( 0, MathF.BitIncrement( .7f ) ) ) );
	}

	[TestMethod]
	public void DistinctSubframeKeysRetainTheRepresentableTimeBetweenThem()
	{
		var fixture = new Fixture();
		var first = .3f;
		var middle = MathF.BitIncrement( first );
		var last = MathF.BitIncrement( middle );
		var times = fixture.Floats( "SCALAR", [first, last], bounds: true );
		var translations = fixture.Floats( "VEC3", [0, 0, 0, 4, -2, 6] );
		using var document = fixture.Read( $$$$$$"""
			"nodes":[{}],"animations":[{"samplers":[{"input":{{{{{{times}}}}}},"output":{{{{{{translations}}}}}}}],
			"channels":[{"sampler":0,"target":{"node":0,"path":"translation"}}]}]
			""" );

		AssertVector( new Vector3( 2, -1, 3 ), Vector3.Transform( Vector3.Zero, document.EvaluatePose( 0, middle ).GetWorldTransform( 0 ) ) );
	}

	[TestMethod]
	[DataRow( .5f, -.25f )]
	[DataRow( 1f, 0f )]
	[DataRow( 1.75f, .84375f )]
	public void CubicScaleReconstructsAPolynomialAcrossUnequalSegmentsIncludingZeroAndNegativeScale( float time, float expectedX )
	{
		var fixture = new Fixture();
		var times = fixture.Floats( "SCALAR", [.25f, 1, 2.5f], bounds: true );
		// x(t)=(t-1)^3, with derivative 3*(t-1)^2; y and z remain one.
		var scales = fixture.Floats( "VEC3", [
			1.6875f, 0, 0, -.421875f, 1, 1, 1.6875f, 0, 0,
			0, 0, 0, 0, 1, 1, 0, 0, 0,
			6.75f, 0, 0, 3.375f, 1, 1, 6.75f, 0, 0] );
		using var document = fixture.Read( $$$$$$"""
			"nodes":[{}],"animations":[{"samplers":[{"input":{{{{{{times}}}}}},"output":{{{{{{scales}}}}}},"interpolation":"CUBICSPLINE"}],
			"channels":[{"sampler":0,"target":{"node":0,"path":"scale"}}]}]
			""" );

		AssertVector( new Vector3( expectedX, 3, 4 ), Vector3.Transform( new Vector3( 2, 3, 4 ), document.EvaluatePose( 0, time ).GetWorldTransform( 0 ) ) );
	}

	[TestMethod]
	[DataRow( .5f, 176f / 185f, 57f / 185f )]
	[DataRow( 1f, .8f, .6f )]
	[DataRow( 1.5f, 104f / 185f, 153f / 185f )]
	public void CubicQuaternionUsesAuthoredNonzeroTangentsAndNormalizesBeforeApplyingRotation( float time, float expectedX, float expectedY )
	{
		var fixture = new Fixture();
		var times = fixture.Floats( "SCALAR", [0, 2], bounds: true );
		// Before normalization, q(t)=(0,0,.3*t,1-.1*t). Both authored keys are unit quaternions.
		var rotations = fixture.Floats( "VEC4", [
			0, 0, 0, 0, 0, 0, 0, 1, 0, 0, .3f, -.1f,
			0, 0, .3f, -.1f, 0, 0, .6f, .8f, 0, 0, 0, 0] );
		using var document = fixture.Read( $$$$$$"""
			"nodes":[{}],"animations":[{"samplers":[{"input":{{{{{{times}}}}}},"output":{{{{{{rotations}}}}}},"interpolation":"CUBICSPLINE"}],
			"channels":[{"sampler":0,"target":{"node":0,"path":"rotation"}}]}]
			""" );

		var transform = document.EvaluatePose( 0, time ).GetWorldTransform( 0 );
		AssertVector( new Vector3( expectedX, expectedY, 0 ), Vector3.Transform( Vector3.UnitX, transform ) );
		Assert.AreEqual( 1f, Vector3.TransformNormal( Vector3.UnitY, transform ).Length(), .00001f );
	}

	[TestMethod]
	public void SparseCubicMorphOutputsPreserveFiveTargetsTangentsAndUnboundedWeights()
	{
		var fixture = new Fixture();
		var position = fixture.Floats( "VEC3", [1, 2, 3], bounds: true );
		var deltas = new[]
		{
			fixture.Floats( "VEC3", [1, 0, 0], bounds: true ), fixture.Floats( "VEC3", [0, 1, 0], bounds: true ),
			fixture.Floats( "VEC3", [0, 0, 1], bounds: true ), fixture.Floats( "VEC3", [2, 0, 0], bounds: true ),
			fixture.Floats( "VEC3", [0, 4, 0], bounds: true )
		};
		var times = fixture.Floats( "SCALAR", [0, 2], bounds: true );
		// Five known polynomials: t^3, -t^2, 2*t, -2 and .5.
		var weights = fixture.SparseScalars( [
			0, 0, 0, 0, 0, 0, 0, 0, -2, .5f, 0, 0, 2, 0, 0,
			12, -4, 2, 0, 0, 8, -4, 4, -2, .5f, 0, 0, 0, 0, 0] );
		var targets = string.Join( ",", deltas.Select( value => $$$$$$"""{"POSITION":{{{{{{value}}}}}}}""" ) );
		using var document = fixture.Read( $$$$$$"""
			"meshes":[{"primitives":[{"mode":0,"attributes":{"POSITION":{{{{{{position}}}}}}},"targets":[{{{{{{targets}}}}}}]}]}],
			"nodes":[{"mesh":0}],"animations":[{"samplers":[{"input":{{{{{{times}}}}}},"output":{{{{{{weights}}}}}},"interpolation":"CUBICSPLINE"}],
			"channels":[{"sampler":0,"target":{"node":0,"path":"weights"}}]}]
			""" );

		var middle = document.EvaluatePose( 0, 1 );
		CollectionAssert.AreEqual( new float[] { 1, -1, 2, -2, .5f }, middle.GetMorphWeights( 0 ) );
		AssertVector( new Vector3( -2, 3, 5 ), document.EvaluateVertexPosition( 0, 0, 0, middle ) );
		AssertVector( new Vector3( 5, 0, 7 ), document.EvaluateVertexPosition( 0, 0, 0, document.EvaluatePose( 0, 2 ) ) );
	}

	[TestMethod]
	public void SelectingAnotherAnimationRestoresPropertiesThatTheNewClipDoesNotTarget()
	{
		var fixture = new Fixture();
		var position = fixture.Floats( "VEC3", [1, 0, 0], bounds: true );
		var delta = fixture.Floats( "VEC3", [2, 0, 0], bounds: true );
		var times = fixture.Floats( "SCALAR", [0, 2], bounds: true );
		var translations = fixture.Floats( "VEC3", [10, 0, 0, 12, 0, 0] );
		var scales = fixture.Floats( "VEC3", [2, 1, 1, 4, 1, 1] );
		var weights = fixture.Floats( "SCALAR", [1, 3] );
		using var document = fixture.Read( $$$$$$"""
			"meshes":[{"weights":[0.25],"primitives":[{"mode":0,"attributes":{"POSITION":{{{{{{position}}}}}}},"targets":[{"POSITION":{{{{{{delta}}}}}}}]}]}],
			"nodes":[{"mesh":0,"translation":[5,0,0],"scale":[2,1,1]}],
			"animations":[
			 {"samplers":[{"input":{{{{{{times}}}}}},"output":{{{{{{translations}}}}}}}],"channels":[{"sampler":0,"target":{"node":0,"path":"translation"}}]},
			 {"samplers":[{"input":{{{{{{times}}}}}},"output":{{{{{{scales}}}}}}}],"channels":[{"sampler":0,"target":{"node":0,"path":"scale"}}]},
			 {"samplers":[{"input":{{{{{{times}}}}}},"output":{{{{{{weights}}}}}}}],"channels":[{"sampler":0,"target":{"node":0,"path":"weights"}}]}]
			""" );

		var translated = document.EvaluatePose( 0, 1 );
		var scaled = document.EvaluatePose( 1, 1 );
		var morphed = document.EvaluatePose( 2, 1 );
		AssertVector( new Vector3( 14, 0, 0 ), document.EvaluateVertexPosition( 0, 0, 0, translated ) );
		AssertVector( new Vector3( 9.5f, 0, 0 ), document.EvaluateVertexPosition( 0, 0, 0, scaled ) );
		AssertVector( new Vector3( 15, 0, 0 ), document.EvaluateVertexPosition( 0, 0, 0, morphed ) );
		// At t=1 the composed pose has weight 2, scale 3 and translation
		// 11: morph before transform gives (1 + 2*2)*3 + 11 = 26.
		var composed = document.EvaluatePoses( [0, 1, 2], 1 );
		CollectionAssert.AreEqual( new float[] { 2 }, composed.GetMorphWeights( 0 ) );
		AssertVector( new Vector3( 26, 0, 0 ), document.EvaluateVertexPosition( 0, 0, 0, composed ) );
		AssertVector( new Vector3( 8, 0, 0 ), document.EvaluateVertexPosition( 0, 0, 0, document.EvaluatePose() ) );
		AssertVector( new Vector3( 14, 0, 0 ), document.EvaluateVertexPosition( 0, 0, 0, translated ) );
	}

	[TestMethod]
	[DataRow( "weights", 5120 )]
	[DataRow( "weights", 5122 )]
	[DataRow( "weights", 5123 )]
	[DataRow( "rotation", 5120 )]
	[DataRow( "rotation", 5121 )]
	[DataRow( "rotation", 5123 )]
	public void RemainingCoreNormalizedAnimationFormatsDecodeBeforeInterpolation( string path, int componentType )
	{
		var fixture = new Fixture();
		var position = fixture.Floats( "VEC3", [1, 0, 0], bounds: true );
		var delta = fixture.Floats( "VEC3", [2, 0, 0], bounds: true );
		var times = fixture.Floats( "SCALAR", [0, 2], bounds: true );
		var maximum = componentType switch { 5120 => 127, 5121 => 255, 5122 => 32767, _ => 65535 };
		var minimum = componentType switch { 5120 => -128, 5122 => -32768, _ => 0 };
		var output = path == "rotation"
			? fixture.Integers( "VEC4", componentType, [0, 0, 0, maximum, 0, 0, maximum, 0], normalized: true )
			: fixture.Integers( "SCALAR", componentType, [minimum, maximum], normalized: true );
		using var document = fixture.Read( $$$$$$"""
			"meshes":[{"primitives":[{"mode":0,"attributes":{"POSITION":{{{{{{position}}}}}}},"targets":[{"POSITION":{{{{{{delta}}}}}}}]}]}],
			"nodes":[{"mesh":0}],"animations":[{"samplers":[{"input":{{{{{{times}}}}}},"output":{{{{{{output}}}}}}}],
			"channels":[{"sampler":0,"target":{"node":0,"path":"{{{{{{path}}}}}}"}}]}]
			""" );

		var pose = document.EvaluatePose( 0, .5f );
		var expected = path == "rotation" ? new Vector3( MathF.Sqrt( .5f ), MathF.Sqrt( .5f ), 0 )
			: new Vector3( minimum < 0 ? 0 : 1.5f, 0, 0 );
		AssertVector( expected, document.EvaluateVertexPosition( 0, 0, 0, pose ) );
	}

	[TestMethod]
	public void SharedMeshInstancesUseTheirOwnSkinJointOrderAndInverseBindMatrices()
	{
		var fixture = new Fixture();
		var position = fixture.Floats( "VEC3", [2, 0, 0], bounds: true );
		var joints = fixture.Integers( "VEC4", 5121, [0, 1, 0, 0] );
		var weights = fixture.Floats( "VEC4", [.25f, .75f, 0, 0] );
		var firstInverse = fixture.Floats( "MAT4", [
			1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, -1, 0, 0, 1,
			1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1] );
		var secondInverse = fixture.Floats( "MAT4", [
			1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, -2, 0, 1,
			2, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, -3, 0, 0, 1] );
		using var document = fixture.Read( $$$$$$"""
			"meshes":[{"primitives":[{"mode":0,"attributes":{"POSITION":{{{{{{position}}}}}},"JOINTS_0":{{{{{{joints}}}}}},"WEIGHTS_0":{{{{{{weights}}}}}}}}]}],
			"nodes":[{"mesh":0,"skin":0,"translation":[100,0,0]},{"mesh":0,"skin":1,"translation":[-100,0,0]},
			 {"translation":[10,0,0]},{"translation":[0,20,0]},{"children":[2,3]}],
			"skins":[{"joints":[2,3],"skeleton":4,"inverseBindMatrices":{{{{{{firstInverse}}}}}}},
			 {"joints":[3,2],"skeleton":4,"inverseBindMatrices":{{{{{{secondInverse}}}}}}}]
			""" );

		AssertVector( new Vector3( 4.25f, 15, 0 ), document.EvaluateVertexPosition( 0, 0, 0 ) );
		AssertVector( new Vector3( 8.75f, 4.5f, 0 ), document.EvaluateVertexPosition( 1, 0, 0 ) );
		var pose = document.EvaluatePose();
		AssertVector( new Vector3( 4.25f, 15, 0 ), document.EvaluateVertexPosition( 0, 0, 0, pose ) );
		AssertVector( new Vector3( 8.75f, 4.5f, 0 ), document.EvaluateVertexPosition( 1, 0, 0, pose ) );
	}

	[TestMethod]
	[DataRow( 5121, 16, 127, 255, 46848 )]
	[DataRow( 5123, 4096, 32767, 65535, 12058368 )]
	public void NineQuantizedInfluencesIncludeUnsignedShortJoint256AndIdentityInverseBindDefaults(
		int componentType, int smallWeight, int lastWeight, int denominator, int weightedTranslationNumerator )
	{
		var fixture = new Fixture();
		var position = fixture.Floats( "VEC3", [2, -1, 3], bounds: true );
		var attributes = new Dictionary<string, int> { ["POSITION"] = position };
		for ( var set = 0; set < 3; set++ )
		{
			var jointValues = set == 2 ? new[] { 256, 0, 0, 0 } : Enumerable.Range( set * 4, 4 ).Select( index => index * 32 ).ToArray();
			var weightValues = set == 2 ? new[] { lastWeight, 0, 0, 0 } : new[] { smallWeight, smallWeight, smallWeight, smallWeight };
			attributes["JOINTS_" + set] = fixture.Integers( "VEC4", 5123, jointValues );
			attributes["WEIGHTS_" + set] = fixture.Integers( "VEC4", componentType, weightValues, normalized: true );
		}
		var nodes = new List<object> { new { mesh = 0, skin = 0, translation = new[] { 1000, 0, 0 } } };
		for ( var joint = 0; joint <= 256; joint++ ) nodes.Add( new { translation = new[] { joint, 0, 0 } } );
		nodes.Add( new { translation = new[] { 0, 10, 0 }, children = Enumerable.Range( 1, 257 ).ToArray() } );
		using var document = fixture.Read( $$$$$$"""
			"meshes":[{"primitives":[{"mode":0,"attributes":{{{{{{JsonSerializer.Serialize( attributes )}}}}}}}]}],
			"nodes":{{{{{{JsonSerializer.Serialize( nodes )}}}}}},"skins":[{"joints":{{{{{{JsonSerializer.Serialize( Enumerable.Range( 1, 257 ) )}}}}}},"skeleton":258}]
			""" );

		// The eight small influences use x=0,32,...,224; the ninth uses x=256.
		var expected = new Vector3( 2 + (float)weightedTranslationNumerator / denominator, 9, 3 );
		AssertVector( expected, document.EvaluateVertexPosition( 0, 0, 0 ) );
		AssertVector( expected, document.EvaluateVertexPosition( 0, 0, 0, document.EvaluatePose() ) );
	}

	[TestMethod]
	[DataRow( "JOINTS_0" )]
	[DataRow( "WEIGHTS_0" )]
	public void SkinningAttributesMustBePairedWhenPresent( string omitted )
	{
		var fixture = new Fixture();
		var position = fixture.Floats( "VEC3", [1, 0, 0], bounds: true );
		var joints = fixture.Integers( "VEC4", 5121, [0, 0, 0, 0] );
		var weights = fixture.Floats( "VEC4", [1, 0, 0, 0] );
		var attributes = new Dictionary<string, int> { ["POSITION"] = position };
		if ( omitted != "JOINTS_0" ) attributes["JOINTS_0"] = joints;
		if ( omitted != "WEIGHTS_0" ) attributes["WEIGHTS_0"] = weights;
		AssertRejectsVertexEvaluation( fixture, $$$$$$"""
			"meshes":[{"primitives":[{"mode":0,"attributes":{{{{{{JsonSerializer.Serialize( attributes )}}}}}}}]}],
			"nodes":[{"mesh":0,"skin":0},{}],"skins":[{"joints":[1]}]
			""" );
	}

	[TestMethod]
	public void ASkinLeavesPrimitivesWithoutSkinningAttributesUnderTheirNodeTransform()
	{
		var fixture = new Fixture();
		var position = fixture.Floats( "VEC3", [1, 0, 0], bounds: true );
		var joints = fixture.Integers( "VEC4", 5121, [0, 0, 0, 0] );
		var weights = fixture.Floats( "VEC4", [1, 0, 0, 0] );
		using var document = fixture.Read( $$$$$$"""
			"meshes":[{"primitives":[{"mode":0,"attributes":{"POSITION":{{{{{{position}}}}}}}},
			 {"mode":0,"attributes":{"POSITION":{{{{{{position}}}}}},"JOINTS_0":{{{{{{joints}}}}}},"WEIGHTS_0":{{{{{{weights}}}}}}}}]}],
			"nodes":[{"mesh":0,"skin":0,"translation":[3,4,5]},{"translation":[10,0,0]}],"skins":[{"joints":[1]}]
			""" );
		AssertVector( new Vector3( 4, 4, 5 ), document.EvaluateVertexPosition( 0, 0, 0 ) );
		AssertVector( new Vector3( 11, 0, 0 ), document.EvaluateVertexPosition( 0, 1, 0 ) );
		var pose = document.EvaluatePose();
		AssertVector( new Vector3( 4, 4, 5 ), document.EvaluateVertexPosition( 0, 0, 0, pose ) );
		AssertVector( new Vector3( 11, 0, 0 ), document.EvaluateVertexPosition( 0, 1, 0, pose ) );
	}

	[TestMethod]
	public void ZeroWeightJointPaddingMayRepeatTheWeightedJoint()
	{
		var fixture = new Fixture();
		var position = fixture.Floats( "VEC3", [1, 0, 0], bounds: true );
		var joints = fixture.Integers( "VEC4", 5121, [0, 0, 0, 0] );
		var weights = fixture.Integers( "VEC4", 5121, [255, 0, 0, 0], normalized: true );
		using var document = fixture.Read( $$$$$$"""
			"meshes":[{"primitives":[{"mode":0,"attributes":{"POSITION":{{{{{{position}}}}}},"JOINTS_0":{{{{{{joints}}}}}},"WEIGHTS_0":{{{{{{weights}}}}}}}}]}],
			"nodes":[{"mesh":0,"skin":0},{"translation":[2,0,0]}],"skins":[{"joints":[1]}]
			""" );
		AssertVector( new Vector3( 3, 0, 0 ), document.EvaluateVertexPosition( 0, 0, 0 ) );
		AssertVector( new Vector3( 3, 0, 0 ), document.EvaluateVertexPosition( 0, 0, 0, document.EvaluatePose() ) );
	}

	[TestMethod]
	[DataRow( -1 )]
	[DataRow( 0 )]
	[DataRow( 1 )]
	public void MixedQuantizedWeightSetsUseOneExactIntegerSum( int lastWeightAdjustment )
	{
		var fixture = new Fixture();
		var position = fixture.Floats( "VEC3", [1, 0, 0], bounds: true );
		var joints0 = fixture.Integers( "VEC4", 5121, [0, 1, 2, 3] );
		var joints1 = fixture.Integers( "VEC4", 5121, [4, 5, 6, 7] );
		var weights0 = fixture.Integers( "VEC4", 5121, [1, 4, 16, 64], normalized: true );
		var weights1 = fixture.Integers( "VEC4", 5123, [640, 2080, 8200, 32770 + lastWeightAdjustment], normalized: true );
		var nodes = new List<object> { new { mesh = 0, skin = 0 } };
		for ( var joint = 0; joint < 8; joint++ ) nodes.Add( new { translation = new[] { joint, 0, 0 } } );
		nodes.Add( new { children = Enumerable.Range( 1, 8 ).ToArray() } );
		var fields = $$$$$$"""
			"meshes":[{"primitives":[{"mode":0,"attributes":{"POSITION":{{{{{{position}}}}}},"JOINTS_0":{{{{{{joints0}}}}}},"WEIGHTS_0":{{{{{{weights0}}}}}},"JOINTS_1":{{{{{{joints1}}}}}},"WEIGHTS_1":{{{{{{weights1}}}}}}}}]}],
			"nodes":{{{{{{JsonSerializer.Serialize( nodes )}}}}}},"skins":[{"joints":[1,2,3,4,5,6,7,8],"skeleton":9}]
			""";
		if ( lastWeightAdjustment != 0 ) { AssertRejectsVertexEvaluation( fixture, fields ); return; }
		using var document = fixture.Read( fields );
		var expected = new Vector3( 1 + 350146f / 65535, 0, 0 );
		AssertVector( expected, document.EvaluateVertexPosition( 0, 0, 0 ) );
		AssertVector( expected, document.EvaluateVertexPosition( 0, 0, 0, document.EvaluatePose() ) );
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void AJointCannotHaveTwoNonzeroInfluencesForOneVertexEvenAcrossSets( bool acrossSets )
	{
		var fixture = new Fixture();
		var position = fixture.Floats( "VEC3", [1, 0, 0], bounds: true );
		var joints = fixture.Integers( "VEC4", 5121, acrossSets ? [0, 1, 0, 0] : [0, 0, 0, 0] );
		var weights = fixture.Floats( "VEC4", acrossSets ? [.25f, .25f, 0, 0] : [.25f, .75f, 0, 0] );
		var attributes = new Dictionary<string, int> { ["POSITION"] = position, ["JOINTS_0"] = joints, ["WEIGHTS_0"] = weights };
		if ( acrossSets )
		{
			attributes["JOINTS_1"] = fixture.Integers( "VEC4", 5121, [0, 0, 0, 0] );
			attributes["WEIGHTS_1"] = fixture.Floats( "VEC4", [.5f, 0, 0, 0] );
		}
		AssertRejectsVertexEvaluation( fixture, $$$$$$"""
			"meshes":[{"primitives":[{"mode":0,"attributes":{{{{{{JsonSerializer.Serialize( attributes )}}}}}}}]}],
			"nodes":[{"mesh":0,"skin":0},{},{},{"children":[1,2]}],"skins":[{"joints":[1,2],"skeleton":3}]
			""" );
	}

	[TestMethod]
	[DataRow( 5121, 127 )]
	[DataRow( 5121, 128 )]
	[DataRow( 5123, 32767 )]
	[DataRow( 5123, 32768 )]
	public void QuantizedJointWeightsMustSumToTheIntegerNormalizationDenominator( int componentType, int weight )
	{
		var fixture = new Fixture();
		var position = fixture.Floats( "VEC3", [1, 0, 0], bounds: true );
		var joints = fixture.Integers( "VEC4", 5121, [0, 1, 0, 0] );
		var weights = fixture.Integers( "VEC4", componentType, [weight, weight, 0, 0], normalized: true );
		AssertRejectsVertexEvaluation( fixture, $$$$$$"""
			"meshes":[{"primitives":[{"mode":0,"attributes":{"POSITION":{{{{{{position}}}}}},"JOINTS_0":{{{{{{joints}}}}}},"WEIGHTS_0":{{{{{{weights}}}}}}}}]}],
			"nodes":[{"mesh":0,"skin":0},{},{},{"children":[1,2]}],"skins":[{"joints":[1,2],"skeleton":3}]
			""" );
	}

	[TestMethod]
	[DataRow( 3, 1f )]
	[DataRow( 7, 1f )]
	[DataRow( 11, 1f )]
	[DataRow( 15, 2f )]
	public void InverseBindMatricesRequireAnAffineFourthRow( int component, float value )
	{
		var fixture = new Fixture();
		float[] matrix = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
		matrix[component] = value;
		var inverse = fixture.Floats( "MAT4", matrix );
		Assert.ThrowsException<GltfException>( () =>
		{
			using var document = fixture.Read( $$$$$$"""
				"nodes":[{}],"skins":[{"joints":[0],"inverseBindMatrices":{{{{{{inverse}}}}}}}]
				""" );
			_ = document.ReadSkin( 0 );
		} );
	}

	[TestMethod]
	[DataRow( "min" )]
	[DataRow( "max" )]
	public void AnimationInputAccessorsRequireBothDeclaredTimeBounds( string omittedBound )
	{
		var fixture = new Fixture();
		var times = fixture.Floats( "SCALAR", [0, 2], bounds: true );
		fixture.RemoveAccessorProperty( times, omittedBound );
		var translations = fixture.Floats( "VEC3", [0, 0, 0, 2, 0, 0] );
		Assert.ThrowsException<GltfException>( () =>
		{
			using var document = fixture.Read( $$$$$$"""
				"nodes":[{}],"animations":[{"samplers":[{"input":{{{{{{times}}}}}},"output":{{{{{{translations}}}}}}}],
				"channels":[{"sampler":0,"target":{"node":0,"path":"translation"}}]}]
				""" );
			_ = document.EvaluatePose( 0, 1 );
		} );
	}

	[TestMethod]
	[DataRow( "input" )]
	[DataRow( "output" )]
	[DataRow( "inverseBindMatrices" )]
	public void AnimationAndInverseBindBufferViewsCannotDeclareVertexStride( string target )
	{
		var fixture = new Fixture();
		var times = fixture.Floats( "SCALAR", [0, 2], bounds: true );
		var translations = fixture.Floats( "VEC3", [0, 0, 0, 2, 0, 0] );
		var inverse = fixture.Floats( "MAT4", [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1] );
		fixture.SetStride( target == "input" ? times : target == "output" ? translations : inverse,
			target == "input" ? 4 : target == "output" ? 12 : 64 );
		Assert.ThrowsException<GltfException>( () =>
		{
			using var document = fixture.Read( $$$$$$"""
				"nodes":[{}],"skins":[{"joints":[0],"inverseBindMatrices":{{{{{{inverse}}}}}}}],
				"animations":[{"samplers":[{"input":{{{{{{times}}}}}},"output":{{{{{{translations}}}}}}}],
				"channels":[{"sampler":0,"target":{"node":0,"path":"translation"}}]}]
				""" );
			if ( target == "inverseBindMatrices" ) _ = document.ReadSkin( 0 );
			else _ = document.EvaluatePose( 0, 1 );
		} );
	}

	[TestMethod]
	public void DeclaredAnimationTimeBoundsUseFloat32Comparison()
	{
		var fixture = new Fixture();
		var times = fixture.Floats( "SCALAR", [.1f, .3f], bounds: true );
		var translations = fixture.Floats( "VEC3", [0, 0, 0, 2, 0, 0] );
		using var document = fixture.Read( AnimationFields( times, translations ) );
		AssertVector( Vector3.UnitX, Vector3.Transform( Vector3.Zero, document.EvaluatePose( 0, .2f ).GetWorldTransform( 0 ) ) );
	}

	[TestMethod]
	[DataRow( "min", -1f )]
	[DataRow( "max", 3f )]
	public void AnimationTimeBoundsMustMatchTheDecodedTimeRange( string property, float incorrect )
	{
		var fixture = new Fixture();
		var times = fixture.Floats( "SCALAR", [0, 2], bounds: true );
		fixture.SetAccessorProperty( times, property, new[] { incorrect } );
		var translations = fixture.Floats( "VEC3", [0, 0, 0, 2, 0, 0] );
		using var document = fixture.Read( AnimationFields( times, translations ) );
		Assert.ThrowsException<GltfException>( () => document.EvaluatePose( 0, 1 ) );
	}

	[TestMethod]
	public void DescriptorOnlyTimeAccessorKeepsTheCoreBoundsException()
	{
		var fixture = new Fixture();
		var times = fixture.Floats( "SCALAR", [0], bounds: true );
		fixture.RemoveAccessorProperty( times, "bufferView" );
		fixture.SetAccessorProperty( times, "min", new[] { 1f } );
		fixture.SetAccessorProperty( times, "max", new[] { 2f } );
		var translations = fixture.Floats( "VEC3", [5, 0, 0] );
		using var document = fixture.Read( AnimationFields( times, translations ) );
		AssertVector( new Vector3( 5, 0, 0 ), Vector3.Transform( Vector3.Zero, document.EvaluatePose( 0, 0 ).GetWorldTransform( 0 ) ) );
	}

	[TestMethod]
	[DataRow( "input" )]
	[DataRow( "output" )]
	[DataRow( "inverseBindMatrices" )]
	public void AnimationAndInverseBindBufferViewsCannotDeclareGpuTarget( string target )
	{
		var fixture = new Fixture();
		var times = fixture.Floats( "SCALAR", [0, 2], bounds: true );
		var translations = fixture.Floats( "VEC3", [0, 0, 0, 2, 0, 0] );
		var inverse = fixture.Floats( "MAT4", [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1] );
		fixture.SetViewProperty( target == "input" ? times : target == "output" ? translations : inverse, "target", 34962 );
		using var document = fixture.Read( $$$$$$"""
			"skins":[{"joints":[0],"inverseBindMatrices":{{{{{{inverse}}}}}}}],{{{{{{AnimationFields( times, translations )}}}}}}
			""" );
		if ( target == "inverseBindMatrices" ) Assert.ThrowsException<GltfException>( () => document.ReadSkin( 0 ) );
		else Assert.ThrowsException<GltfException>( () => document.EvaluatePose( 0, 1 ) );
	}

	[TestMethod]
	public void UnusedInverseBindMatricesDoNotAcquireJointOnlyAffineRestrictions()
	{
		var fixture = new Fixture();
		var inverse = fixture.Floats( "MAT4", [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1,
			1, 0, 0, 1, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 2] );
		using var document = fixture.Read( $$$$$$"""
			"nodes":[{}],"skins":[{"joints":[0],"inverseBindMatrices":{{{{{{inverse}}}}}}}]
			""" );
		Assert.AreEqual( Matrix4x4.Identity, document.ReadSkin( 0 ).InverseBindMatrices.Single() );
	}

	[TestMethod]
	public void MorphWeightAnimationPreservesAMatrixNodeWithoutDecomposition()
	{
		var fixture = new Fixture();
		var position = fixture.Floats( "VEC3", [1, 0, 0], bounds: true );
		var delta = fixture.Floats( "VEC3", [2, 0, 0], bounds: true );
		var times = fixture.Floats( "SCALAR", [0, 2], bounds: true );
		var weights = fixture.Floats( "SCALAR", [0, 1] );
		using var document = fixture.Read( $$$$$$"""
			"meshes":[{"primitives":[{"mode":0,"attributes":{"POSITION":{{{{{{position}}}}}}},"targets":[{"POSITION":{{{{{{delta}}}}}}}]}]}],
			"nodes":[{"mesh":0,"matrix":[0,2,0,0,-3,0,0,0,0,0,4,0,3,4,5,1]}],
			"animations":[{"samplers":[{"input":{{{{{{times}}}}}},"output":{{{{{{weights}}}}}}}],"channels":[{"sampler":0,"target":{"node":0,"path":"weights"}}]}]
			""" );
		var expectedMatrix = new Matrix4x4( 0, 2, 0, 0, -3, 0, 0, 0, 0, 0, 4, 0, 3, 4, 5, 1 );
		var pose = document.EvaluatePose( 0, 1 );
		Assert.AreEqual( expectedMatrix, pose.GetWorldTransform( 0 ) );
		AssertVector( new Vector3( 3, 8, 5 ), document.EvaluateVertexPosition( 0, 0, 0, pose ) );
	}

	private static string AnimationFields( int times, int output ) => $$$$$$"""
		"nodes":[{}],"animations":[{"samplers":[{"input":{{{{{{times}}}}}},"output":{{{{{{output}}}}}}}],"channels":[{"sampler":0,"target":{"node":0,"path":"translation"}}]}]
		""";

	private static void AssertVector( Vector3 expected, Vector3 actual )
	{
		Assert.AreEqual( expected.X, actual.X, .0001f, "X" );
		Assert.AreEqual( expected.Y, actual.Y, .0001f, "Y" );
		Assert.AreEqual( expected.Z, actual.Z, .0001f, "Z" );
	}

	private static void AssertRejectsVertexEvaluation( Fixture fixture, string fields )
	{
		foreach ( var posed in new[] { false, true } )
		{
			Assert.ThrowsException<GltfException>( () =>
			{
				using var document = fixture.Read( fields );
				if ( posed ) _ = document.EvaluateVertexPosition( 0, 0, 0, document.EvaluatePose() );
				else _ = document.EvaluateVertexPosition( 0, 0, 0 );
			}, posed ? "Sampled pose" : "Rest pose" );
		}
	}

	private sealed class Fixture
	{
		private readonly List<byte> bytes = [];
		private readonly List<Dictionary<string, object>> views = [];
		private readonly List<Dictionary<string, object>> accessors = [];

		public int Floats( string type, float[] values, bool bounds = false )
		{
			var data = new byte[values.Length * sizeof( float )];
			for ( var i = 0; i < values.Length; i++ ) BinaryPrimitives.WriteSingleLittleEndian( data.AsSpan( i * sizeof( float ) ), values[i] );
			var components = Components( type );
			var accessor = AddAccessor( type, 5126, values.Length / components, AddView( data ) );
			if ( bounds )
			{
				var minimum = new float[components];
				var maximum = new float[components];
				for ( var component = 0; component < components; component++ )
				{
					minimum[component] = maximum[component] = values[component];
					for ( var i = component + components; i < values.Length; i += components )
					{
						minimum[component] = MathF.Min( minimum[component], values[i] );
						maximum[component] = MathF.Max( maximum[component], values[i] );
					}
				}
				accessors[accessor]["min"] = minimum;
				accessors[accessor]["max"] = maximum;
			}
			return accessor;
		}

		public int Integers( string type, int componentType, int[] values, bool normalized = false )
		{
			var width = componentType is 5120 or 5121 ? 1 : 2;
			var data = new byte[values.Length * width];
			for ( var i = 0; i < values.Length; i++ )
			{
				if ( componentType == 5120 ) data[i] = unchecked((byte)checked((sbyte)values[i]));
				else if ( componentType == 5121 ) data[i] = checked((byte)values[i]);
				else if ( componentType == 5122 ) BinaryPrimitives.WriteInt16LittleEndian( data.AsSpan( i * width ), checked((short)values[i]) );
				else BinaryPrimitives.WriteUInt16LittleEndian( data.AsSpan( i * width ), checked((ushort)values[i]) );
			}
			var accessor = AddAccessor( type, componentType, values.Length / Components( type ), AddView( data ) );
			if ( normalized ) accessors[accessor]["normalized"] = true;
			return accessor;
		}

		public int SparseScalars( float[] values )
		{
			var indices = Enumerable.Range( 0, values.Length ).Where( index => values[index] != 0 ).ToArray();
			var indexView = AddView( indices.Select( index => checked((byte)index) ).ToArray() );
			var data = new byte[indices.Length * sizeof( float )];
			for ( var i = 0; i < indices.Length; i++ ) BinaryPrimitives.WriteSingleLittleEndian( data.AsSpan( i * sizeof( float ) ), values[indices[i]] );
			var valueView = AddView( data );
			var accessor = AddAccessor( "SCALAR", 5126, values.Length, null );
			accessors[accessor]["sparse"] = new { count = indices.Length, indices = new { bufferView = indexView, componentType = 5121 }, values = new { bufferView = valueView } };
			return accessor;
		}

		public void RemoveAccessorProperty( int accessor, string property ) => accessors[accessor].Remove( property );
		public void SetStride( int accessor, int stride ) => views[(int)accessors[accessor]["bufferView"]]["byteStride"] = stride;
		public void SetAccessorProperty( int accessor, string property, object value ) => accessors[accessor][property] = value;
		public void SetViewProperty( int accessor, string property, object value ) => views[(int)accessors[accessor]["bufferView"]][property] = value;

		public GltfDocument Read( string fields )
		{
			var buffer = new { uri = "data:application/octet-stream;base64," + Convert.ToBase64String( bytes.ToArray() ), byteLength = bytes.Count };
			return GltfReader.Read( Encoding.UTF8.GetBytes( $$$$$$"""
				{"asset":{"version":"2.0"},"buffers":[{{{{{{JsonSerializer.Serialize( buffer )}}}}}}],
				"bufferViews":{{{{{{JsonSerializer.Serialize( views )}}}}}},"accessors":{{{{{{JsonSerializer.Serialize( accessors )}}}}}},{{{{{{fields}}}}}}}
				""" ) );
		}

		private int AddView( byte[] data )
		{
			while ( bytes.Count % 4 != 0 ) bytes.Add( 0 );
			var offset = bytes.Count;
			bytes.AddRange( data );
			views.Add( new Dictionary<string, object> { ["buffer"] = 0, ["byteOffset"] = offset, ["byteLength"] = data.Length } );
			return views.Count - 1;
		}

		private int AddAccessor( string type, int componentType, int count, int? view )
		{
			var accessor = new Dictionary<string, object> { ["type"] = type, ["componentType"] = componentType, ["count"] = count };
			if ( view.HasValue ) accessor["bufferView"] = view.Value;
			accessors.Add( accessor );
			return accessors.Count - 1;
		}

		private static int Components( string type ) => type switch { "SCALAR" => 1, "VEC3" => 3, "VEC4" => 4, "MAT4" => 16, _ => throw new ArgumentException( type ) };
	}
}
