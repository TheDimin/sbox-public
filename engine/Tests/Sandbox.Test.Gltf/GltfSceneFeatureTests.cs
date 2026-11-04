using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;
using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace Sandbox.Tests.Gltf;

[TestClass]
public class GltfSceneFeatureTests
{
	[TestMethod]
	public void SkinWithoutInverseBindAccessorUsesIdentityPerJoint()
	{
		using var document = SceneFeatureFixture.Read( """
			{"asset":{"version":"2.0"},"nodes":[{"children":[1]},{}],"skins":[{"joints":[1,0],"skeleton":0}]}
			""" );
		var skin = document.ReadSkin( 0 );

		CollectionAssert.AreEqual( new[] { 1, 0 }, skin.Joints );
		Assert.AreEqual( 0, skin.Skeleton );
		CollectionAssert.AreEqual( new[] { Matrix4x4.Identity, Matrix4x4.Identity }, skin.InverseBindMatrices );
	}

	[TestMethod]
	public void SkinReadsInverseBindMatricesInJointOrder()
	{
		var matrixData = new float[]
		{
			1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1,
			1, 0, 0, 0, 0, 2, 0, 0, 0, 0, 3, 0, 4, 5, 6, 1
		};
		using var document = SceneFeatureFixture.ReadFloats( matrixData, """
			"bufferViews":[{"buffer":0,"byteLength":128}],
			"accessors":[{"bufferView":0,"componentType":5126,"count":2,"type":"MAT4"}],
			"nodes":[{"children":[1]},{}],
			"skins":[{"joints":[0,1],"inverseBindMatrices":0}]
			""" );
		var skin = document.ReadSkin( 0 );

		Assert.IsNull( skin.Skeleton );
		CollectionAssert.AreEqual( new[] { 0, 1 }, skin.Joints );
		Assert.AreEqual( Matrix4x4.Identity, skin.InverseBindMatrices[0] );
		Assert.AreEqual( new Matrix4x4( 1, 0, 0, 0, 0, 2, 0, 0, 0, 0, 3, 0, 4, 5, 6, 1 ), skin.InverseBindMatrices[1] );
	}

	[TestMethod]
	public void SkinRejectsTooFewInverseBindMatrices()
	{
		var matrixData = new float[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 };
		Assert.ThrowsException<GltfException>( () =>
		{
			using var document = SceneFeatureFixture.ReadFloats( matrixData, """
				"bufferViews":[{"buffer":0,"byteLength":64}],
				"accessors":[{"bufferView":0,"componentType":5126,"count":1,"type":"MAT4"}],
				"nodes":[{"children":[1]},{}],
				"skins":[{"joints":[0,1],"inverseBindMatrices":0}]
				""" );
			document.ReadSkin( 0 );
		} );
	}

	[TestMethod]
	public void SkinRejectsJointIndexOutsideNodes()
	{
		Assert.ThrowsException<GltfException>( () =>
		{
			using var document = SceneFeatureFixture.Read( """
				{"asset":{"version":"2.0"},"nodes":[{}],"skins":[{"joints":[2]}]}
				""" );
			document.ReadSkin( 0 );
		} );
	}

	[TestMethod]
	public void MorphTargetsKeepDeltasSeparateAndMissingSemanticsNull()
	{
		using var document = CreateMorphDocument();
		var targets = document.ReadMorphTargets( 0, 0 );

		Assert.AreEqual( 2, targets.Length );
		CollectionAssert.AreEqual( new float[] { 1, 2, 3 }, targets[0].PositionDeltas );
		CollectionAssert.AreEqual( new float[] { -1, 0, 1 }, targets[0].NormalDeltas );
		Assert.IsNull( targets[0].TangentDeltas );
		Assert.IsNull( targets[1].PositionDeltas );
		Assert.IsNull( targets[1].NormalDeltas );
		CollectionAssert.AreEqual( new float[] { 0.1f, 0.2f, 0.3f }, targets[1].TangentDeltas );
	}

	[TestMethod]
	public void MorphWeightsPreferNodeThenMeshThenZeroDefaults()
	{
		using var document = CreateMorphDocument();
		CollectionAssert.AreEqual( new float[] { 0.25f, 0.75f }, document.GetMorphWeights( 0 ) );
		CollectionAssert.AreEqual( new float[] { 0.125f, 0.875f }, document.GetMorphWeights( 1 ) );
		CollectionAssert.AreEqual( new float[] { 0, 0 }, document.GetMorphWeights( 2 ) );
	}

	[TestMethod]
	public void MorphTargetAccessorMustMatchBaseVertexCount()
	{
		Assert.ThrowsException<GltfException>( () =>
		{
			using var document = SceneFeatureFixture.ReadFloats( new float[9], """
				"bufferViews":[{"buffer":0,"byteLength":12},{"buffer":0,"byteOffset":12,"byteLength":24}],
				"accessors":[
				 {"bufferView":0,"componentType":5126,"count":1,"type":"VEC3","min":[0,0,0],"max":[0,0,0]},
				 {"bufferView":1,"componentType":5126,"count":2,"type":"VEC3","min":[0,0,0],"max":[0,0,0]}
				],
				"meshes":[{"primitives":[{"mode":0,"attributes":{"POSITION":0},"targets":[{"POSITION":1}]}]}]
				""" );
			document.ReadMorphTargets( 0, 0 );
		} );
	}

	[TestMethod]
	public void PerspectiveCameraPreservesVerticalFovAndInfiniteFarPlane()
	{
		using var document = SceneFeatureFixture.Read( """
			{"asset":{"version":"2.0"},"cameras":[{"type":"perspective","perspective":{"yfov":1.2,"znear":0.25}}]}
			""" );
		var camera = document.ReadCamera( 0 );

		Assert.AreEqual( "perspective", camera.Type );
		Assert.AreEqual( 1.2f, camera.YFov, 0.000001f );
		Assert.AreEqual( 0.25f, camera.ZNear, 0.000001f );
		Assert.IsNull( camera.AspectRatio );
		Assert.IsNull( camera.ZFar );
	}

	[TestMethod]
	public void PerspectiveCameraPreservesExplicitAspectAndFarPlane()
	{
		using var document = SceneFeatureFixture.Read( """
			{"asset":{"version":"2.0"},"cameras":[{"type":"perspective","perspective":{"yfov":0.75,"aspectRatio":1.5,"znear":0.1,"zfar":200}}]}
			""" );
		var camera = document.ReadCamera( 0 );

		Assert.AreEqual( "perspective", camera.Type );
		Assert.AreEqual( 0.75f, camera.YFov, 0.000001f );
		Assert.AreEqual( 1.5f, camera.AspectRatio );
		Assert.AreEqual( 0.1f, camera.ZNear, 0.000001f );
		Assert.AreEqual( 200.0f, camera.ZFar );
	}

	[TestMethod]
	public void OrthographicCameraPreservesMagnitudesAndClipPlanes()
	{
		using var document = SceneFeatureFixture.Read( """
			{"asset":{"version":"2.0"},"cameras":[{"type":"orthographic","orthographic":{"xmag":2.5,"ymag":1.5,"znear":0,"zfar":50}}]}
			""" );
		var camera = document.ReadCamera( 0 );

		Assert.AreEqual( "orthographic", camera.Type );
		Assert.AreEqual( 2.5f, camera.XMagnitude, 0.000001f );
		Assert.AreEqual( 1.5f, camera.YMagnitude, 0.000001f );
		Assert.AreEqual( 0.0f, camera.ZNear, 0.000001f );
		Assert.AreEqual( 50.0f, camera.ZFar );
	}

	[TestMethod]
	[DataRow( "{\"type\":\"perspective\",\"perspective\":{\"yfov\":1,\"znear\":0}}" )]
	[DataRow( "{\"type\":\"perspective\",\"perspective\":{\"yfov\":1,\"znear\":2,\"zfar\":1}}" )]
	[DataRow( "{\"type\":\"orthographic\",\"orthographic\":{\"xmag\":0,\"ymag\":1,\"znear\":0,\"zfar\":10}}" )]
	[DataRow( "{\"type\":\"unknown\"}" )]
	public void InvalidCameraParametersAreRejected( string cameraJson )
	{
		var json = "{\"asset\":{\"version\":\"2.0\"},\"cameras\":[" + cameraJson + "]}";
		Assert.ThrowsException<GltfException>( () =>
		{
			using var document = SceneFeatureFixture.Read( json );
			document.ReadCamera( 0 );
		} );
	}

	[TestMethod]
	public void PunctualLightsUseSpecifiedValuesAndStandardSpotDefaults()
	{
		const string json = """
			{"asset":{"version":"2.0"},"extensionsUsed":["KHR_lights_punctual"],
			 "extensions":{"KHR_lights_punctual":{"lights":[
			   {"type":"point","color":[0.2,0.5,1],"intensity":4,"range":12},
			   {"type":"spot","spot":{}}
			 ]}}}
			""";
		using var document = SceneFeatureFixture.Read( json, "KHR_lights_punctual" );
		var point = document.ReadLight( 0 );
		var spot = document.ReadLight( 1 );

		Assert.AreEqual( "point", point.Type );
		Assert.AreEqual( new Vector3( 0.2f, 0.5f, 1 ), point.Color );
		Assert.AreEqual( 4.0f, point.Intensity, 0.000001f );
		Assert.AreEqual( 12.0f, point.Range );
		Assert.AreEqual( "spot", spot.Type );
		Assert.AreEqual( Vector3.One, spot.Color );
		Assert.AreEqual( 1.0f, spot.Intensity, 0.000001f );
		Assert.IsNull( spot.Range );
		Assert.AreEqual( 0.0f, spot.InnerConeAngle, 0.000001f );
		Assert.AreEqual( MathF.PI / 4, spot.OuterConeAngle, 0.000001f );
	}

	[TestMethod]
	[DataRow( "{\"type\":\"point\",\"range\":0}" )]
	[DataRow( "{\"type\":\"point\",\"intensity\":-1}" )]
	[DataRow( "{\"type\":\"spot\"}" )]
	[DataRow( "{\"type\":\"spot\",\"spot\":{\"innerConeAngle\":0.9,\"outerConeAngle\":0.5}}" )]
	[DataRow( "{\"type\":\"spot\",\"spot\":{\"outerConeAngle\":1.7}}" )]
	[DataRow( "{\"type\":\"unknown\"}" )]
	public void InvalidPunctualLightParametersAreRejected( string lightJson )
	{
		var json = "{\"asset\":{\"version\":\"2.0\"},\"extensionsUsed\":[\"KHR_lights_punctual\"],\"extensions\":{\"KHR_lights_punctual\":{\"lights\":[" + lightJson + "]}}}";
		Assert.ThrowsException<GltfException>( () =>
		{
			using var document = SceneFeatureFixture.Read( json, "KHR_lights_punctual" );
			document.ReadLight( 0 );
		} );
	}

	private static GltfDocument CreateMorphDocument()
	{
		var data = new float[] { 0, 0, 1, 1, 2, 3, -1, 0, 1, 1, 0, 0, 1, 0.1f, 0.2f, 0.3f };
		return SceneFeatureFixture.ReadFloats( data, """
			"bufferViews":[
			  {"buffer":0,"byteOffset":0,"byteLength":12},
			  {"buffer":0,"byteOffset":12,"byteLength":12},
			  {"buffer":0,"byteOffset":24,"byteLength":12},
			  {"buffer":0,"byteOffset":36,"byteLength":16},
			  {"buffer":0,"byteOffset":52,"byteLength":12}
			],
			"accessors":[
			  {"bufferView":0,"componentType":5126,"count":1,"type":"VEC3","min":[0,0,1],"max":[0,0,1]},
			  {"bufferView":1,"componentType":5126,"count":1,"type":"VEC3","min":[1,2,3],"max":[1,2,3]},
			  {"bufferView":2,"componentType":5126,"count":1,"type":"VEC3"},
			  {"bufferView":3,"componentType":5126,"count":1,"type":"VEC4"},
			  {"bufferView":4,"componentType":5126,"count":1,"type":"VEC3"}
			],
			"meshes":[
			  {"weights":[0.125,0.875],"primitives":[{"mode":0,"attributes":{"POSITION":0,"NORMAL":0,"TANGENT":3},"targets":[{"POSITION":1,"NORMAL":2},{"TANGENT":4}]}]},
			  {"primitives":[{"mode":0,"attributes":{"POSITION":0,"NORMAL":0,"TANGENT":3},"targets":[{"POSITION":1,"NORMAL":2},{"TANGENT":4}]}]}
			],
			"nodes":[{"mesh":0,"weights":[0.25,0.75]},{"mesh":0},{"mesh":1}]
			""" );
	}
}

internal static class SceneFeatureFixture
{
	public static GltfDocument Read( string json, params string[] extensions )
	{
		return GltfReader.Read( Encoding.UTF8.GetBytes( json ), supportedExtensions: new HashSet<string>( extensions, StringComparer.Ordinal ) );
	}

	public static GltfDocument ReadFloats( float[] values, string fields, params string[] extensions )
	{
		var bytes = new byte[values.Length * sizeof( float )];
		for ( var i = 0; i < values.Length; i++ )
			BinaryPrimitives.WriteSingleLittleEndian( bytes.AsSpan( i * sizeof( float ), sizeof( float ) ), values[i] );
		return ReadBytes( bytes, fields, extensions );
	}

	public static GltfDocument ReadBytes( byte[] bytes, string fields, params string[] extensions )
	{
		var encoded = Convert.ToBase64String( bytes );
		var json = $$"""
			{"asset":{"version":"2.0"},"buffers":[{"uri":"data:application/octet-stream;base64,{{encoded}}","byteLength":{{bytes.Length}}}],{{fields}}}
			""";
		return Read( json, extensions );
	}
}
