using System;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;
using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace Sandbox.Tests.Gltf;

[TestClass]
public class GltfInstancingTests
{
	private const string Extension = "EXT_mesh_gpu_instancing";

	[TestMethod]
	public void TranslationOnlyDefaultsRotationAndScaleAndStaysNodeLocal()
	{
		using var document = SceneFeatureFixture.ReadFloats( new float[] { 1, 2, 3, -4, 5, 6 }, """
			"extensionsUsed":["EXT_mesh_gpu_instancing"],"extensionsRequired":["EXT_mesh_gpu_instancing"],
			"bufferViews":[{"buffer":0,"byteLength":24}],
			"accessors":[{"bufferView":0,"componentType":5126,"count":2,"type":"VEC3","min":[-4,2,3],"max":[1,5,6]}],
			"meshes":[{"primitives":[{"attributes":{"POSITION":0},"mode":0}]}],
			"nodes":[{"mesh":0,"translation":[100,100,100],"extensions":{"EXT_mesh_gpu_instancing":{"attributes":{"TRANSLATION":0}}}}]
			""", Extension );
		var instances = document.ReadInstances( 0 );

		Assert.AreEqual( 2, instances.Length );
		Assert.AreEqual( Matrix4x4.CreateTranslation( 1, 2, 3 ), instances[0] );
		Assert.AreEqual( Matrix4x4.CreateTranslation( -4, 5, 6 ), instances[1] );
	}

	[TestMethod]
	public void InstanceTransformAppliesScaleThenRotationThenTranslation()
	{
		var halfSqrtTwo = MathF.Sqrt( 0.5f );
		using var document = SceneFeatureFixture.ReadFloats( new float[]
		{
			10, 20, 30,
			0, 0, halfSqrtTwo, halfSqrtTwo,
			2, 3, 4
		}, """
			"extensionsUsed":["EXT_mesh_gpu_instancing"],"extensionsRequired":["EXT_mesh_gpu_instancing"],
			"bufferViews":[{"buffer":0,"byteOffset":0,"byteLength":12},{"buffer":0,"byteOffset":12,"byteLength":16},{"buffer":0,"byteOffset":28,"byteLength":12}],
			"accessors":[
			 {"bufferView":0,"componentType":5126,"count":1,"type":"VEC3","min":[10,20,30],"max":[10,20,30]},
			 {"bufferView":1,"componentType":5126,"count":1,"type":"VEC4"},
			 {"bufferView":2,"componentType":5126,"count":1,"type":"VEC3"}
			],
			"meshes":[{"primitives":[{"attributes":{"POSITION":0},"mode":0}]}],
			"nodes":[{"mesh":0,"extensions":{"EXT_mesh_gpu_instancing":{"attributes":{"TRANSLATION":0,"ROTATION":1,"SCALE":2}}}}]
			""", Extension );
		var instances = document.ReadInstances( 0 );

		Assert.AreEqual( 1, instances.Length );
		AssertVector( new Vector3( 10, 22, 30 ), Vector3.Transform( Vector3.UnitX, instances[0] ) );
		AssertVector( new Vector3( 7, 20, 30 ), Vector3.Transform( Vector3.UnitY, instances[0] ) );
	}

	[TestMethod]
	public void SignedNormalizedQuaternionIsDecodedAsInstanceRotation()
	{
		var bytes = new byte[16];
		bytes[14] = 127;
		using var document = SceneFeatureFixture.ReadBytes( bytes, """
			"extensionsUsed":["EXT_mesh_gpu_instancing"],"extensionsRequired":["EXT_mesh_gpu_instancing"],
			"bufferViews":[{"buffer":0,"byteLength":12},{"buffer":0,"byteOffset":12,"byteLength":4}],
			"accessors":[
			 {"bufferView":0,"componentType":5126,"count":1,"type":"VEC3","min":[0,0,0],"max":[0,0,0]},
			 {"bufferView":1,"componentType":5120,"normalized":true,"count":1,"type":"VEC4"}
			],
			"meshes":[{"primitives":[{"attributes":{"POSITION":0},"mode":0}]}],
			"nodes":[{"mesh":0,"extensions":{"EXT_mesh_gpu_instancing":{"attributes":{"ROTATION":1}}}}]
			""", Extension );
		var instances = document.ReadInstances( 0 );

		Assert.AreEqual( 1, instances.Length );
		AssertVector( -Vector3.UnitX, Vector3.Transform( Vector3.UnitX, instances[0] ) );
	}

	[TestMethod]
	public void CustomOnlyAttributeDefinesCountWithIdentityTransforms()
	{
		using var document = SceneFeatureFixture.ReadFloats( new float[] { 0, 1, 2 }, """
			"extensionsUsed":["EXT_mesh_gpu_instancing"],"extensionsRequired":["EXT_mesh_gpu_instancing"],
			"bufferViews":[{"buffer":0,"byteLength":12}],
			"accessors":[
			 {"bufferView":0,"componentType":5126,"count":1,"type":"VEC3","min":[0,1,2],"max":[0,1,2]},
			 {"bufferView":0,"componentType":5126,"count":3,"type":"SCALAR"}
			],
			"meshes":[{"primitives":[{"attributes":{"POSITION":0},"mode":0}]}],
			"nodes":[{"mesh":0,"extensions":{"EXT_mesh_gpu_instancing":{"attributes":{"_ID":1}}}}]
			""", Extension );
		var instances = document.ReadInstances( 0 );

		CollectionAssert.AreEqual( new[] { Matrix4x4.Identity, Matrix4x4.Identity, Matrix4x4.Identity }, instances );
	}

	[TestMethod]
	public void AttributeCountsMustMatch()
	{
		Assert.ThrowsException<GltfException>( () =>
		{
			using var document = SceneFeatureFixture.ReadFloats( new float[] { 1, 2, 3, 4, 5, 6, 1, 1, 1 }, """
				"extensionsUsed":["EXT_mesh_gpu_instancing"],"extensionsRequired":["EXT_mesh_gpu_instancing"],
				"bufferViews":[{"buffer":0,"byteLength":24},{"buffer":0,"byteOffset":24,"byteLength":12}],
				"accessors":[
				 {"bufferView":0,"componentType":5126,"count":2,"type":"VEC3","min":[1,2,3],"max":[4,5,6]},
				 {"bufferView":1,"componentType":5126,"count":1,"type":"VEC3"}
				],
				"meshes":[{"primitives":[{"attributes":{"POSITION":0},"mode":0}]}],
				"nodes":[{"mesh":0,"extensions":{"EXT_mesh_gpu_instancing":{"attributes":{"TRANSLATION":0,"SCALE":1}}}}]
				""", Extension );
			document.ReadInstances( 0 );
		} );
	}

	private static void AssertVector( Vector3 expected, Vector3 actual )
	{
		Assert.AreEqual( expected.X, actual.X, 0.00001f );
		Assert.AreEqual( expected.Y, actual.Y, 0.00001f );
		Assert.AreEqual( expected.Z, actual.Z, 0.00001f );
	}
}
