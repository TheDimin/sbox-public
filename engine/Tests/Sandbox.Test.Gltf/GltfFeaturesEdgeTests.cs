using System;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;

namespace Sandbox.Tests.Gltf;

[TestClass]
public sealed class GltfFeaturesEdgeTests
{
	[TestMethod]
	public void OptionalInstancingWithoutCapabilityDoesNotExpandInstances()
	{
		using var document = SceneFeatureFixture.Read( """
			{"asset":{"version":"2.0"},"extensionsUsed":["EXT_mesh_gpu_instancing"],
			 "accessors":[{"componentType":5126,"count":1,"type":"SCALAR"},{"componentType":5126,"count":1,"type":"VEC3","min":[0,0,0],"max":[0,0,0]}],
			 "meshes":[{"primitives":[{"mode":0,"attributes":{"POSITION":1}}]}],
			 "nodes":[{"mesh":0,"extensions":{"EXT_mesh_gpu_instancing":{"attributes":{"_ID":0}}}}]}
			""" );
		Assert.AreEqual( 0, document.ReadInstances( 0 ).Length );
	}

	[TestMethod]
	public void UninstancedNodeDoesNotExpandInstances()
	{
		using var document = SceneFeatureFixture.Read( """{"asset":{"version":"2.0"},"nodes":[{}]}""", "EXT_mesh_gpu_instancing" );
		Assert.AreEqual( 0, document.ReadInstances( 0 ).Length );
	}

	[TestMethod]
	public void InstanceRotationIsNormalized()
	{
		using var document = SceneFeatureFixture.ReadFloats( [0, 0, 2, 2], """
			"extensionsUsed":["EXT_mesh_gpu_instancing"],"extensionsRequired":["EXT_mesh_gpu_instancing"],
			"bufferViews":[{"buffer":0,"byteLength":16}],"accessors":[{"bufferView":0,"componentType":5126,"count":1,"type":"VEC4"},{"componentType":5126,"count":1,"type":"VEC3","min":[0,0,0],"max":[0,0,0]}],
			"meshes":[{"primitives":[{"mode":0,"attributes":{"POSITION":1}}]}],
			"nodes":[{"mesh":0,"extensions":{"EXT_mesh_gpu_instancing":{"attributes":{"ROTATION":0}}}}]
			""", "EXT_mesh_gpu_instancing" );
		var result = Vector3.Transform( Vector3.UnitX, document.ReadInstances( 0 )[0] );
		Assert.AreEqual( 0, result.X, 0.00001f );
		Assert.AreEqual( 1, result.Y, 0.00001f );
		Assert.AreEqual( 0, result.Z, 0.00001f );
	}

	[TestMethod]
	public void ZeroLengthInstanceQuaternionIsRejected()
	{
		Assert.ThrowsException<GltfException>( () =>
		{
			using var document = SceneFeatureFixture.ReadFloats( [0, 0, 0, 0], """
				"extensionsUsed":["EXT_mesh_gpu_instancing"],"extensionsRequired":["EXT_mesh_gpu_instancing"],
				"bufferViews":[{"buffer":0,"byteLength":16}],"accessors":[{"bufferView":0,"componentType":5126,"count":1,"type":"VEC4"},{"componentType":5126,"count":1,"type":"VEC3","min":[0,0,0],"max":[0,0,0]}],
				"meshes":[{"primitives":[{"mode":0,"attributes":{"POSITION":1}}]}],
				"nodes":[{"mesh":0,"extensions":{"EXT_mesh_gpu_instancing":{"attributes":{"ROTATION":0}}}}]
				""", "EXT_mesh_gpu_instancing" );
			_ = document.ReadInstances( 0 );
		} );
	}

	[TestMethod]
	public void InstanceCustomAttributeCountMustMatchTransforms()
	{
		Assert.ThrowsException<GltfException>( () =>
		{
			using var document = SceneFeatureFixture.Read( """
				{"asset":{"version":"2.0"},"extensionsUsed":["EXT_mesh_gpu_instancing"],"extensionsRequired":["EXT_mesh_gpu_instancing"],
				 "accessors":[{"componentType":5126,"count":2,"type":"VEC3"},{"componentType":5126,"count":1,"type":"SCALAR"},{"componentType":5126,"count":1,"type":"VEC3","min":[0,0,0],"max":[0,0,0]}],
				 "meshes":[{"primitives":[{"mode":0,"attributes":{"POSITION":2}}]}],"nodes":[{"mesh":0,"extensions":{
				 "EXT_mesh_gpu_instancing":{"attributes":{"TRANSLATION":0,"_ID":1}}}}]}
				""", "EXT_mesh_gpu_instancing" );
			_ = document.ReadInstances( 0 );
		} );
	}

	[TestMethod]
	public void NegativeOrthographicMagnificationIsPermittedByCore()
	{
		using var document = SceneFeatureFixture.Read( """
			{"asset":{"version":"2.0"},"cameras":[{"type":"orthographic","orthographic":{"xmag":-2,"ymag":1,"znear":0,"zfar":10}}]}
			""" );
		Assert.AreEqual( -2f, document.ReadCamera( 0 ).XMagnitude );
	}

	[TestMethod]
	public void PerspectiveYfovGreaterThanPiIsNotRejectedByShouldRecommendation()
	{
		using var document = SceneFeatureFixture.Read( """
			{"asset":{"version":"2.0"},"cameras":[{"type":"perspective","perspective":{"yfov":4,"znear":0.1}}]}
			""" );
		Assert.AreEqual( 4f, document.ReadCamera( 0 ).YFov );
	}

	[TestMethod]
	public void UnsupportedPunctualLightsCannotBeReadAsSupported()
	{
		Assert.ThrowsException<GltfException>( () =>
		{
			using var document = SceneFeatureFixture.Read( """
				{"asset":{"version":"2.0"},"extensionsUsed":["KHR_lights_punctual"],
				 "extensions":{"KHR_lights_punctual":{"lights":[{"type":"point"}]}}}
				""" );
			_ = document.ReadLight( 0 );
		} );
	}
}
