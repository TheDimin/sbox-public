using System;
using System.Buffers.Binary;
using NativeEngine;
using Sandbox;

namespace ResourceTests;

/// <summary>
/// The native model behaviour the compiled glTF path depends on. The model is built in memory at load: a saved
/// <c>.vmdl_c</c> keeps meshes but not a runtime skeleton, morphs or clips, so the compiled glTF stores
/// ready-to-upload data and builds the model with <see cref="ModelBuilder"/> instead.
/// </summary>
[TestClass]
public class GltfNativeVmdlProofTests
{
	const int Stride = 36;

	static Model BuildSkinnedMorphedAnimated()
	{
		var material = Material.Load( "materials/default/white.vmat" );
		var fields = new RawVertexField[]
		{
			new( "position", 0, 0, ColorFormat.COLOR_FORMAT_R32G32B32_FLOAT ),
			new( "normal", 0, 12, ColorFormat.COLOR_FORMAT_R32G32B32_FLOAT ),
			new( "blendindices", 0, 24, ColorFormat.COLOR_FORMAT_R16G16B16A16_UINT ),
			new( "blendweight", 0, 32, ColorFormat.COLOR_FORMAT_R8G8B8A8_UNORM ),
		};
		var bytes = new byte[Stride * 3];
		void Vertex( int index, Vector3 position, ushort bone0, ushort bone1, byte weight0, byte weight1 )
		{
			var span = bytes.AsSpan( index * Stride );
			BinaryPrimitives.WriteSingleLittleEndian( span[0..], position.x );
			BinaryPrimitives.WriteSingleLittleEndian( span[4..], position.y );
			BinaryPrimitives.WriteSingleLittleEndian( span[8..], position.z );
			BinaryPrimitives.WriteSingleLittleEndian( span[20..], 1 );
			BinaryPrimitives.WriteUInt16LittleEndian( span[24..], bone0 );
			BinaryPrimitives.WriteUInt16LittleEndian( span[26..], bone1 );
			span[32] = weight0; span[33] = weight1;
		}
		Vertex( 0, new Vector3( 0, 0, 0 ), 0, 0, 255, 0 );
		Vertex( 1, new Vector3( 10, 0, 0 ), 0, 0, 255, 0 );
		Vertex( 2, new Vector3( 0, 0, 20 ), 1, 0, 128, 127 );
		var mesh = new Mesh( "proof", material );
		mesh.CreateRawVertexBuffer( Stride, 3, bytes, fields );
		mesh.CreateIndexBuffer( 3, new[] { 0, 1, 2 } );
		mesh.Bounds = new BBox( Vector3.Zero, new Vector3( 10, 0, 20 ) );
		mesh.AddMorph( "smile", [new MorphDelta( 2, new Vector3( 0, 0, 5 ), Vector3.Zero )] );

		var builder = Model.Builder
			.AddBone( "root", Vector3.Zero, Rotation.Identity )
			.AddBone( "tip", new Vector3( 0, 0, 10 ), Rotation.Identity, "root" )
			.AddMesh( mesh );
		var posed = new Transform( new Vector3( 0, 5, 10 ), Rotation.FromPitch( 90 ), new Vector3( 2, 2, 2 ) );
		builder.AddAnimation( "wave", 1 ).WithLooping()
			.AddFrame( new[] { new Transform( Vector3.Zero ), posed }.AsSpan() )
			.AddFrame( new[] { new Transform( Vector3.Zero ), posed }.AsSpan() );
		return builder.WithName( "gltf_native_proof" ).Create();
	}

	[TestMethod]
	public void BuilderKeepsSkeletonClipsAndInterleavedSkinLayout()
	{
		var model = BuildSkinnedMorphedAnimated();
		Assert.IsFalse( model.IsError );
		Assert.AreEqual( 2, model.BoneCount );
		Assert.AreEqual( "root", model.Bones.AllBones[0].Name );
		Assert.AreEqual( "tip", model.Bones.AllBones[1].Name );
		Assert.AreEqual( 1, model.AnimationCount );
		Assert.AreEqual( "wave", model.GetAnimationName( 0 ) );
	}

	/// <summary>Morph data needs a render device; headless tests drop it, and the mesh data with it.</summary>
	[TestMethod]
	public void BuilderKeepsMorphTargets()
	{
		if ( Environment.GetEnvironmentVariable( "SBOX_TEST_GRAPHICS" ) != "1" ) Assert.Inconclusive( "Set SBOX_TEST_GRAPHICS=1: native morph data needs a render device." );
		var model = BuildSkinnedMorphedAnimated();
		Assert.AreEqual( 1, model.MorphCount );
		Assert.AreEqual( "smile", model.GetMorphName( 0 ) );
		Assert.AreEqual( 3, MeshGlue.GetModelNumVertices( model.native ), "the morphed, skinned mesh keeps its interleaved vertices" );
	}

	/// <summary>Native frames are parent-local and keep scale, so glTF animated scale needs no conversion.</summary>
	[TestMethod]
	public void ClipFramesAreParentLocalAndKeepScale()
	{
		if ( Environment.GetEnvironmentVariable( "SBOX_TEST_GRAPHICS" ) != "1" ) Assert.Inconclusive( "Set SBOX_TEST_GRAPHICS=1 to play native clips." );
		var world = new SceneWorld();
		try
		{
			var scene = new SceneModel( world, BuildSkinnedMorphedAnimated(), Transform.Zero ) { UseAnimGraph = false };
			scene.CurrentSequence.Name = "wave";
			scene.CurrentSequence.Time = 0.5f;
			scene.Update( 0.01f );
			var local = scene.GetBoneLocalTransform( 1 );
			Assert.IsTrue( (local.Position - new Vector3( 0, 5, 10 )).Length < 1e-3f, $"position {local.Position}" );
			Assert.IsTrue( (local.Scale - new Vector3( 2, 2, 2 )).Length < 1e-4f, $"scale {local.Scale}" );
			Assert.AreEqual( 90, local.Rotation.Angles().pitch, 0.01f );
		}
		finally { world.Delete(); }
	}
}
