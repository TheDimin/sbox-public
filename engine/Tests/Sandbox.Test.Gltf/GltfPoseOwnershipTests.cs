using System;
using System.Buffers.Binary;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;

namespace Sandbox.Tests.Gltf;

[TestClass]
public class GltfPoseOwnershipTests
{
	[TestMethod]
	public void OwnPoseRemainsAnIdentityOnlyCheckWhenGeometryCannotDecode()
	{
		var bytes = new byte[36]; BinaryPrimitives.WriteUInt32LittleEndian( bytes, 0x7fc00001 );
		var resolutions = 0;
		using var document = GltfReader.Read( Encoding.UTF8.GetBytes( """
			{"asset":{"version":"2.0"},"buffers":[{"uri":"poison.bin","byteLength":36}],
			 "bufferViews":[{"buffer":0,"byteLength":36}],
			 "accessors":[{"bufferView":0,"componentType":5126,"type":"VEC3","count":3,"min":[0,0,0],"max":[1,1,1]}],
			 "meshes":[{"primitives":[{"attributes":{"POSITION":0}}]}],"nodes":[{"mesh":0}]}
			""" ), _ => { resolutions++; return bytes; } );
		var pose = document.EvaluatePose(); var before = resolutions;
		Assert.ThrowsException<GltfException>( () => document.ReadFloatAccessor( 0 ), "The poison fixture must actually fail geometry decoding." );
		document.ValidatePoseOwnership( pose );
		Assert.AreEqual( before, resolutions, "Pose provenance must not resolve geometry dependencies." );
		Assert.AreEqual( System.Numerics.Matrix4x4.Identity, pose.GetWorldTransform( 0 ) );
	}

	[TestMethod]
	public void ForeignPoseFromIdenticalJsonHasTheExistingVertexEvaluatorError()
	{
		var source = Encoding.UTF8.GetBytes( "{\"asset\":{\"version\":\"2.0\"},\"nodes\":[{}]}" );
		using var a = GltfReader.Read( source ); using var b = GltfReader.Read( source );
		var foreign = b.EvaluatePose();
		var error = Assert.ThrowsException<ArgumentException>( () => a.ValidatePoseOwnership( foreign ) );
		var previous = Assert.ThrowsException<ArgumentException>( () => a.EvaluateVertexPosition( 0, 0, 0, foreign ) );
		Assert.AreEqual( previous.ParamName, error.ParamName ); Assert.AreEqual( previous.Message, error.Message );
	}

	[TestMethod]
	public void NullPoseHasTheExistingVertexEvaluatorNullError()
	{
		using var document = GltfReader.Read( Encoding.UTF8.GetBytes( "{\"asset\":{\"version\":\"2.0\"}}" ) );
		var error = Assert.ThrowsException<ArgumentNullException>( () => document.ValidatePoseOwnership( null ) );
		var previous = Assert.ThrowsException<ArgumentNullException>( () => document.EvaluateVertexPosition( 0, 0, 0, null ) );
		Assert.AreEqual( previous.ParamName, error.ParamName );
	}

	[TestMethod]
	public void RepeatedOwnershipChecksAllocateNoArraysOrDelegateState()
	{
		using var document = GltfReader.Read( Encoding.UTF8.GetBytes( "{\"asset\":{\"version\":\"2.0\"},\"nodes\":[{}]}" ) );
		var pose = document.EvaluatePose();
		for ( var i = 0; i < 100; i++ ) document.ValidatePoseOwnership( pose );
		var before = GC.GetAllocatedBytesForCurrentThread();
		for ( var i = 0; i < 10000; i++ ) document.ValidatePoseOwnership( pose );
		var after = GC.GetAllocatedBytesForCurrentThread();
		Assert.AreEqual( 0L, after - before, "Pose identity verification is used each animation frame and must not decode/materialize data." );
	}
}
