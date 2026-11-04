using System;
using System.Buffers.Binary;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;
using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;
using Vector3 = System.Numerics.Vector3;

namespace Sandbox.Tests.Gltf;

[TestClass]
public sealed class GltfAnimationChannelTests
{
	private static void AssertVector( Vector3 expected, Vector3 actual )
	{
		Assert.AreEqual( expected.X, actual.X, 0.0001f, "X" );
		Assert.AreEqual( expected.Y, actual.Y, 0.0001f, "Y" );
		Assert.AreEqual( expected.Z, actual.Z, 0.0001f, "Z" );
	}

	private static void AssertValues( float[] expected, float[] actual )
	{
		Assert.AreEqual( expected.Length, actual.Length );
		for ( var i = 0; i < expected.Length; i++ )
			Assert.AreEqual( expected[i], actual[i], 0.00001f, $"Component {i}" );
	}

	private static GltfDocument ReadFloats( float[] values, string fields )
	{
		var bytes = new byte[values.Length * sizeof( float )];
		for ( var i = 0; i < values.Length; i++ )
			BinaryPrimitives.WriteSingleLittleEndian( bytes.AsSpan( i * sizeof( float ), sizeof( float ) ), values[i] );
		return ReadBytes( bytes, fields );
	}

	private static GltfDocument ReadBytes( byte[] bytes, string fields )
	{
		var base64 = Convert.ToBase64String( bytes );
		var json = $$"""
			{"asset":{"version":"2.0"},"buffers":[{"uri":"data:application/octet-stream;base64,{{base64}}","byteLength":{{bytes.Length}}}],{{fields}}}
			""";
		return GltfReader.Read( Encoding.UTF8.GetBytes( json ) );
	}

	[TestMethod]
	public void TranslationChannelUpdatesDescendantsAndRetainsUntargetedTrs()
	{
		using var document = ReadFloats( [0, 2, 0, 0, 0, 4, 0, 0], """
			"bufferViews":[{"buffer":0,"byteLength":8},{"buffer":0,"byteOffset":8,"byteLength":24}],
			"accessors":[{"bufferView":0,"componentType":5126,"count":2,"type":"SCALAR","min":[0],"max":[2]},
			             {"bufferView":1,"componentType":5126,"count":2,"type":"VEC3"}],
			"nodes":[{"translation":[100,2,3],"rotation":[0,0,0.70710678,0.70710678],"scale":[2,3,4],"children":[1]},
			         {"translation":[0,1,0]}],
			"animations":[{"samplers":[{"input":0,"output":1}],"channels":[{"sampler":0,"target":{"node":0,"path":"translation"}}]}]
			""" );

		var rest = document.EvaluatePose();
		var halfway = document.EvaluatePose( 0, 1 );
		AssertVector( new Vector3( 97, 2, 3 ), Vector3.Transform( Vector3.Zero, rest.GetWorldTransform( 1 ) ) );
		AssertVector( new Vector3( -1, 0, 0 ), Vector3.Transform( Vector3.Zero, halfway.GetWorldTransform( 1 ) ) );
		AssertVector( new Vector3( -1, 2, 0 ), Vector3.Transform( Vector3.UnitX, halfway.GetWorldTransform( 1 ) ) );
		AssertVector( new Vector3( 97, 2, 3 ), Vector3.Transform( Vector3.Zero, document.GetWorldTransform( 1 ) ) );
	}

	[TestMethod]
	public void RotationAndScaleChannelsCombineWithUntargetedTranslation()
	{
		using var document = ReadFloats( [
			0, 2,
			0, 0, 0, 1, 0, 0, -0.8660254f, -0.5f,
			1, 1, 1, 3, 2, 1
		], """
			"bufferViews":[{"buffer":0,"byteLength":8},{"buffer":0,"byteOffset":8,"byteLength":32},{"buffer":0,"byteOffset":40,"byteLength":24}],
			"accessors":[{"bufferView":0,"componentType":5126,"count":2,"type":"SCALAR","min":[0],"max":[2]},
			             {"bufferView":1,"componentType":5126,"count":2,"type":"VEC4"},
			             {"bufferView":2,"componentType":5126,"count":2,"type":"VEC3"}],
			"nodes":[{"translation":[2,3,4],"scale":[4,4,4]}],
			"animations":[{"samplers":[{"input":0,"output":1},{"input":0,"output":2}],"channels":[
			  {"sampler":0,"target":{"node":0,"path":"rotation"}},
			  {"sampler":1,"target":{"node":0,"path":"scale"}}
			]}]
			""" );

		var world = document.EvaluatePose( 0, 0.5f ).GetWorldTransform( 0 );
		AssertVector( new Vector3( 3.299038f, 3.75f, 4 ), Vector3.Transform( Vector3.UnitX, world ) );
	}

	[TestMethod]
	public void RotationChannelAcceptsNormalizedSignedShortQuaternionValues()
	{
		var data = new byte[24];
		BinaryPrimitives.WriteSingleLittleEndian( data.AsSpan( 0, 4 ), 0 );
		BinaryPrimitives.WriteSingleLittleEndian( data.AsSpan( 4, 4 ), 2 );
		short[] keys = [0, 0, 0, 32767, 0, 0, 28377, 16384];
		for ( var i = 0; i < keys.Length; i++ )
			BinaryPrimitives.WriteInt16LittleEndian( data.AsSpan( 8 + i * 2, 2 ), keys[i] );
		using var document = ReadBytes( data, """
			"bufferViews":[{"buffer":0,"byteLength":8},{"buffer":0,"byteOffset":8,"byteLength":16}],
			"accessors":[{"bufferView":0,"componentType":5126,"count":2,"type":"SCALAR","min":[0],"max":[2]},
			             {"bufferView":1,"componentType":5122,"normalized":true,"count":2,"type":"VEC4"}],
			"nodes":[{}],"animations":[{"samplers":[{"input":0,"output":1}],
			"channels":[{"sampler":0,"target":{"node":0,"path":"rotation"}}]}]
			""" );
		var point = Vector3.Transform( Vector3.UnitX, document.EvaluatePose( 0, 1 ).GetWorldTransform( 0 ) );
		AssertVector( new Vector3( 0.5f, 0.8660254f, 0 ), point );
	}

	[TestMethod]
	public void MorphWeightsReadFlattenedScalarOutputsAndDoNotAffectOtherInstances()
	{
		using var document = ReadFloats( [0, 2, 0.2f, 0.4f, 0.8f, 0.8f, 0.2f, 0], """
			"bufferViews":[{"buffer":0,"byteLength":8},{"buffer":0,"byteOffset":8,"byteLength":24}],
			"accessors":[{"bufferView":0,"componentType":5126,"count":2,"type":"SCALAR","min":[0],"max":[2]},
			             {"bufferView":1,"componentType":5126,"count":6,"type":"SCALAR"},
			             {"componentType":5126,"count":3,"type":"VEC3","min":[0,0,0],"max":[0,0,0]}],
			"meshes":[{"weights":[0,0.5,1],"primitives":[{"attributes":{"POSITION":2},
			  "targets":[{"POSITION":2},{"POSITION":2},{"POSITION":2}]}]}],
			"nodes":[{"mesh":0,"weights":[0.9,0.8,0.7]},{"mesh":0}],
			"animations":[{"samplers":[{"input":0,"output":1}],"channels":[{"sampler":0,"target":{"node":0,"path":"weights"}}]}]
			""" );

		var rest = document.EvaluatePose();
		var first = document.EvaluatePose( 0, 0.5f );
		var second = document.EvaluatePose( 0, 2 );
		AssertValues( [0.9f, 0.8f, 0.7f], rest.GetMorphWeights( 0 ) );
		AssertValues( [0.35f, 0.35f, 0.6f], first.GetMorphWeights( 0 ) );
		AssertValues( [0.8f, 0.2f, 0], second.GetMorphWeights( 0 ) );
		AssertValues( [0, 0.5f, 1], first.GetMorphWeights( 1 ) );
		AssertValues( [0.35f, 0.35f, 0.6f], first.GetMorphWeights( 0 ) );
		AssertValues( [0.9f, 0.8f, 0.7f], document.GetMorphWeights( 0 ) );
	}

	[TestMethod]
	public void MorphChannelAcceptsNormalizedUnsignedByteWeights()
	{
		var data = new byte[12];
		BinaryPrimitives.WriteSingleLittleEndian( data.AsSpan( 0, 4 ), 0 );
		BinaryPrimitives.WriteSingleLittleEndian( data.AsSpan( 4, 4 ), 2 );
		data[8] = 0;
		data[9] = 255;
		data[10] = 255;
		data[11] = 0;
		using var document = ReadBytes( data, """
			"bufferViews":[{"buffer":0,"byteLength":8},{"buffer":0,"byteOffset":8,"byteLength":4}],
			"accessors":[{"bufferView":0,"componentType":5126,"count":2,"type":"SCALAR","min":[0],"max":[2]},
			             {"bufferView":1,"componentType":5121,"normalized":true,"count":4,"type":"SCALAR"},
			             {"componentType":5126,"count":3,"type":"VEC3","min":[0,0,0],"max":[0,0,0]}],
			"meshes":[{"primitives":[{"attributes":{"POSITION":2},"targets":[{"POSITION":2},{"POSITION":2}]}]}],
			"nodes":[{"mesh":0}],"animations":[{"samplers":[{"input":0,"output":1}],
			"channels":[{"sampler":0,"target":{"node":0,"path":"weights"}}]}]
			""" );
		AssertValues( [0.5f, 0.5f], document.EvaluatePose( 0, 1 ).GetMorphWeights( 0 ) );
	}

	[TestMethod]
	public void CubicMorphChannelGroupsTangentsAndValuesPerKey()
	{
		using var document = ReadFloats( [
			0, 2,
			0, 0,  0.1f, 0.2f,  0.4f, -0.2f,
			-0.2f, 0.6f,  0.9f, 0.8f,  0, 0
		], """
			"bufferViews":[{"buffer":0,"byteLength":8},{"buffer":0,"byteOffset":8,"byteLength":48}],
			"accessors":[{"bufferView":0,"componentType":5126,"count":2,"type":"SCALAR","min":[0],"max":[2]},
			             {"bufferView":1,"componentType":5126,"count":12,"type":"SCALAR"},
			             {"componentType":5126,"count":3,"type":"VEC3","min":[0,0,0],"max":[0,0,0]}],
			"meshes":[{"primitives":[{"attributes":{"POSITION":2},"targets":[{"POSITION":2},{"POSITION":2}]}]}],
			"nodes":[{"mesh":0}],"animations":[{"samplers":[{"input":0,"output":1,"interpolation":"CUBICSPLINE"}],
			"channels":[{"sampler":0,"target":{"node":0,"path":"weights"}}]}]
			""" );
		AssertValues( [0.65f, 0.3f], document.EvaluatePose( 0, 1 ).GetMorphWeights( 0 ) );
	}

	[TestMethod]
	public void StepTranslationChangesExactlyAtTheNextKey()
	{
		using var document = ReadFloats( [0, 1, 2, 0, 0, 8, 0, 0], """
			"bufferViews":[{"buffer":0,"byteLength":8},{"buffer":0,"byteOffset":8,"byteLength":24}],
			"accessors":[{"bufferView":0,"componentType":5126,"count":2,"type":"SCALAR","min":[0],"max":[1]},
			             {"bufferView":1,"componentType":5126,"count":2,"type":"VEC3"}],
			"nodes":[{}],"animations":[{"samplers":[{"input":0,"output":1,"interpolation":"STEP"}],
			"channels":[{"sampler":0,"target":{"node":0,"path":"translation"}}]}]
			""" );
		AssertVector( new Vector3( 2, 0, 0 ), Vector3.Transform( Vector3.Zero, document.EvaluatePose( 0, 0.999f ).GetWorldTransform( 0 ) ) );
		AssertVector( new Vector3( 8, 0, 0 ), Vector3.Transform( Vector3.Zero, document.EvaluatePose( 0, 1 ).GetWorldTransform( 0 ) ) );
	}

	[TestMethod]
	public void CubicSplineChannelUsesAccessorTangentsAndKeyDuration()
	{
		using var document = ReadFloats( [
			1, 3,
			0, 0, 0,  0, 0, 0,  2, 0, 0,
			0, 0, 0,  2, 0, 0,  0, 0, 0
		], """
			"bufferViews":[{"buffer":0,"byteLength":8},{"buffer":0,"byteOffset":8,"byteLength":72}],
			"accessors":[{"bufferView":0,"componentType":5126,"count":2,"type":"SCALAR","min":[1],"max":[3]},
			             {"bufferView":1,"componentType":5126,"count":6,"type":"VEC3"}],
			"nodes":[{}],"animations":[{"samplers":[{"input":0,"output":1,"interpolation":"CUBICSPLINE"}],
			"channels":[{"sampler":0,"target":{"node":0,"path":"translation"}}]}]
			""" );

		AssertVector( new Vector3( 1.5f, 0, 0 ), Vector3.Transform( Vector3.Zero, document.EvaluatePose( 0, 2 ).GetWorldTransform( 0 ) ) );
	}

	[TestMethod]
	public void SamplingLaterTimeDoesNotMutatePreviouslyEvaluatedPose()
	{
		using var document = ReadFloats( [0, 2, 0, 0, 0, 4, 0, 0], """
			"bufferViews":[{"buffer":0,"byteLength":8},{"buffer":0,"byteOffset":8,"byteLength":24}],
			"accessors":[{"bufferView":0,"componentType":5126,"count":2,"type":"SCALAR","min":[0],"max":[2]},
			             {"bufferView":1,"componentType":5126,"count":2,"type":"VEC3"}],
			"nodes":[{}],"animations":[{"samplers":[{"input":0,"output":1}],
			"channels":[{"sampler":0,"target":{"node":0,"path":"translation"}}]}]
			""" );
		var first = document.EvaluatePose( 0, 0.5f );
		var second = document.EvaluatePose( 0, 2 );
		AssertVector( new Vector3( 4, 0, 0 ), Vector3.Transform( Vector3.Zero, second.GetWorldTransform( 0 ) ) );
		AssertVector( new Vector3( 1, 0, 0 ), Vector3.Transform( Vector3.Zero, first.GetWorldTransform( 0 ) ) );
		AssertVector( Vector3.Zero, Vector3.Transform( Vector3.Zero, document.EvaluatePose().GetWorldTransform( 0 ) ) );
	}

	[TestMethod]
	[DataRow( "{\"input\":0,\"output\":1}", "{\"sampler\":1,\"target\":{\"node\":0,\"path\":\"translation\"}}" )]
	[DataRow( "{\"input\":2,\"output\":1}", "{\"sampler\":0,\"target\":{\"node\":0,\"path\":\"translation\"}}" )]
	[DataRow( "{\"input\":0,\"output\":2}", "{\"sampler\":0,\"target\":{\"node\":0,\"path\":\"translation\"}}" )]
	[DataRow( "{\"input\":0,\"output\":1}", "{\"sampler\":0,\"target\":{\"node\":1,\"path\":\"translation\"}}" )]
	[DataRow( "{\"input\":0,\"output\":1}", "{\"sampler\":0,\"target\":{\"node\":0,\"path\":\"rotation\"}}" )]
	[DataRow( "{\"input\":0,\"output\":1}", "{\"sampler\":0,\"target\":{\"node\":0,\"path\":\"unknown\"}}" )]
	public void InvalidSamplerAndTargetReferencesAreRejected( string sampler, string channel )
	{
		var fields = """
			"bufferViews":[{"buffer":0,"byteLength":8},{"buffer":0,"byteOffset":8,"byteLength":24}],
			"accessors":[{"bufferView":0,"componentType":5126,"count":2,"type":"SCALAR","min":[0],"max":[2]},
			             {"bufferView":1,"componentType":5126,"count":2,"type":"VEC3"}],
			"nodes":[{}],"animations":[{"samplers":[SAMPLER],"channels":[CHANNEL]}]
			""".Replace( "SAMPLER", sampler ).Replace( "CHANNEL", channel );
		Assert.ThrowsException<GltfException>( () =>
		{
			using var document = ReadFloats( [0, 2, 0, 0, 0, 4, 0, 0], fields );
			_ = document.EvaluatePose( 0, 1 );
		} );
	}

	[TestMethod]
	public void OutputAccessorCountMustMatchTheNumberOfKeys()
	{
		Assert.ThrowsException<GltfException>( () =>
		{
			using var document = ReadFloats( [0, 2, 0, 0, 0], """
				"bufferViews":[{"buffer":0,"byteLength":8},{"buffer":0,"byteOffset":8,"byteLength":12}],
				"accessors":[{"bufferView":0,"componentType":5126,"count":2,"type":"SCALAR","min":[0],"max":[2]},
				             {"bufferView":1,"componentType":5126,"count":1,"type":"VEC3"}],
				"nodes":[{}],"animations":[{"samplers":[{"input":0,"output":1}],
				"channels":[{"sampler":0,"target":{"node":0,"path":"translation"}}]}]
				""" );
			_ = document.EvaluatePose( 0, 1 );
		} );
	}

	[TestMethod]
	public void CubicOutputRequiresThreeVectorsPerKey()
	{
		Assert.ThrowsException<GltfException>( () =>
		{
			using var document = ReadFloats( [0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], """
				"bufferViews":[{"buffer":0,"byteLength":8},{"buffer":0,"byteOffset":8,"byteLength":60}],
				"accessors":[{"bufferView":0,"componentType":5126,"count":2,"type":"SCALAR","min":[0],"max":[1]},
				             {"bufferView":1,"componentType":5126,"count":5,"type":"VEC3"}],
				"nodes":[{}],"animations":[{"samplers":[{"input":0,"output":1,"interpolation":"CUBICSPLINE"}],
				"channels":[{"sampler":0,"target":{"node":0,"path":"translation"}}]}]
				""" );
			_ = document.EvaluatePose( 0, 0.5f );
		} );
	}

	[TestMethod]
	public void MorphOutputScalarCountMustMatchKeysTimesTargetCount()
	{
		Assert.ThrowsException<GltfException>( () =>
		{
			using var document = ReadFloats( [0, 1, 0.1f, 0.2f, 0.3f], """
				"bufferViews":[{"buffer":0,"byteLength":8},{"buffer":0,"byteOffset":8,"byteLength":12}],
				"accessors":[{"bufferView":0,"componentType":5126,"count":2,"type":"SCALAR","min":[0],"max":[1]},
				             {"bufferView":1,"componentType":5126,"count":3,"type":"SCALAR"},
				             {"componentType":5126,"count":3,"type":"VEC3","min":[0,0,0],"max":[0,0,0]}],
				"meshes":[{"primitives":[{"attributes":{"POSITION":2},"targets":[{"POSITION":2},{"POSITION":2}]}]}],
				"nodes":[{"mesh":0}],"animations":[{"samplers":[{"input":0,"output":1}],
				"channels":[{"sampler":0,"target":{"node":0,"path":"weights"}}]}]
				""" );
			_ = document.EvaluatePose( 0, 0.5f );
		} );
	}

	[TestMethod]
	public void AnimatedNodeCannotUseMatrixTransform()
	{
		Assert.ThrowsException<GltfException>( () =>
		{
			using var document = ReadFloats( [0, 1, 0, 0, 0, 1, 0, 0], """
				"bufferViews":[{"buffer":0,"byteLength":8},{"buffer":0,"byteOffset":8,"byteLength":24}],
				"accessors":[{"bufferView":0,"componentType":5126,"count":2,"type":"SCALAR","min":[0],"max":[1]},
				             {"bufferView":1,"componentType":5126,"count":2,"type":"VEC3"}],
				"nodes":[{"matrix":[1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1]}],
				"animations":[{"samplers":[{"input":0,"output":1}],"channels":[{"sampler":0,"target":{"node":0,"path":"translation"}}]}]
				""" );
			_ = document.EvaluatePose( 0, 0.5f );
		} );
	}

	[TestMethod]
	public void AnAnimationCannotTargetTheSameNodePropertyTwice()
	{
		Assert.ThrowsException<GltfException>( () =>
		{
			using var document = ReadFloats( [0, 2, 0, 0, 0, 4, 0, 0], """
				"bufferViews":[{"buffer":0,"byteLength":8},{"buffer":0,"byteOffset":8,"byteLength":24}],
				"accessors":[{"bufferView":0,"componentType":5126,"count":2,"type":"SCALAR","min":[0],"max":[2]},
				             {"bufferView":1,"componentType":5126,"count":2,"type":"VEC3"}],
				"nodes":[{}],"animations":[{"samplers":[{"input":0,"output":1}],"channels":[
				 {"sampler":0,"target":{"node":0,"path":"translation"}},
				 {"sampler":0,"target":{"node":0,"path":"translation"}}
				]}]
				""" );
			_ = document.EvaluatePose( 0, 1 );
		} );
	}

	[TestMethod]
	public void UnsupportedChannelWithoutTargetNodeIsIgnored()
	{
		using var document = ReadFloats( [0, 2, 0, 0, 0, 4, 0, 0], """
			"bufferViews":[{"buffer":0,"byteLength":8},{"buffer":0,"byteOffset":8,"byteLength":24}],
			"accessors":[{"bufferView":0,"componentType":5126,"count":2,"type":"SCALAR","min":[0],"max":[2]},
			             {"bufferView":1,"componentType":5126,"count":2,"type":"VEC3"}],
			"nodes":[{"translation":[7,8,9]}],"animations":[{"samplers":[{"input":0,"output":1}],
			"channels":[{"sampler":0,"target":{"path":"translation"}}]}]
			""" );
		AssertVector( new Vector3( 7, 8, 9 ), Vector3.Transform( Vector3.Zero, document.EvaluatePose( 0, 1 ).GetWorldTransform( 0 ) ) );
	}
}
