using System;
using System.Buffers.Binary;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;
using Sandbox.Gltf.DirectGpu;

namespace Sandbox.Tests.Gltf.DirectGpu;

using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

/// <summary>Authored time and morph-pose behavior; these are not rendering or wallclock proofs.</summary>
[TestClass]
public class GltfGpuAnimationPlayerTests
{
	[TestMethod]
	public void NoAutoplayAndPausedAdvanceDoesNotEvaluateOrApplyPose()
	{
		using var document = Fixture(); var callbacks = 0;
		var player = new GltfGpuAnimationPlayer( document, _ => callbacks++ );
		Assert.IsNull( player.Animation ); Assert.IsFalse( player.IsPlaying ); Assert.AreEqual( 0, callbacks );
		Assert.AreEqual( -.5f, player.CurrentPose.GetMorphWeights( 0 )[0] );
		Assert.IsFalse( player.Advance( 3 ) ); Assert.AreEqual( 0, callbacks );
		player.SelectClip( 0 ); Assert.AreEqual( 1, callbacks );
		var pose = player.CurrentPose;
		Assert.IsFalse( player.Advance( 3 ) ); Assert.AreSame( pose, player.CurrentPose ); Assert.AreEqual( 1, callbacks );
	}

	[TestMethod]
	public void FullElapsedTimeIsAppliedOnceWithoutPreviewStallCap()
	{
		using var document = Fixture(); var callbacks = 0;
		var player = new GltfGpuAnimationPlayer( document, _ => callbacks++ );
		player.SelectClip( 0 ); player.Play();
		Assert.IsTrue( player.Advance( .75 ) ); Assert.AreEqual( .75, player.CurrentTime, 1e-12 );
		Assert.AreEqual( .1875f, player.CurrentPose.GetMorphWeights( 0 )[0], 1e-7f );
		Assert.IsTrue( player.Advance( .625 ) ); Assert.AreEqual( 1.375, player.CurrentTime, 1e-12 );
		Assert.AreEqual( .34375f, player.CurrentPose.GetMorphWeights( 0 )[0], 1e-7f );
		Assert.AreEqual( 3, callbacks ); // Select plus exactly one pose per advance.
	}

	[TestMethod]
	public void IndependentInstancesAndRetainedPoseSnapshotsDoNotInterfere()
	{
		using var document = Fixture();
		var first = new GltfGpuAnimationPlayer( document ); var second = new GltfGpuAnimationPlayer( document );
		first.SelectClip( 0 ); second.SelectClip( 0 ); first.Play(); second.Play();
		first.Advance( 1 ); var snapshot = first.CurrentPose;
		second.Advance( 3 ); first.Advance( .5 );
		Assert.AreEqual( .25f, snapshot.GetMorphWeights( 0 )[0], 1e-7f );
		Assert.AreEqual( .375f, first.CurrentPose.GetMorphWeights( 0 )[0], 1e-7f );
		Assert.AreEqual( .75f, second.CurrentPose.GetMorphWeights( 0 )[0], 1e-7f );
		Assert.AreEqual( -.5f, document.GetMorphWeights( 0 )[0] );
	}

	[TestMethod]
	public void LoopingKeepsFullRemainderAndNonloopingAppliesEndpointThenStops()
	{
		using var document = Fixture(); var callbacks = 0;
		var player = new GltfGpuAnimationPlayer( document, _ => callbacks++ );
		player.SelectClip( 0, looping: true ); player.Play(); player.Advance( 10.5 );
		Assert.AreEqual( 2.5, player.CurrentTime, 1e-12 ); Assert.IsTrue( player.IsPlaying );
		Assert.AreEqual( .625f, player.CurrentPose.GetMorphWeights( 0 )[0], 1e-7f );
		player.SelectClip( 0 ); player.Play(); player.Advance( 10.5 );
		Assert.AreEqual( 4d, player.CurrentTime ); Assert.AreEqual( 1f, player.CurrentPose.GetMorphWeights( 0 )[0] );
		Assert.IsFalse( player.IsPlaying ); var count = callbacks;
		Assert.IsFalse( player.Advance( .5 ) ); Assert.AreEqual( count, callbacks );
	}

	[TestMethod]
	public void SeekAndSpeedUseAuthoredSecondsAndResetToRestIsExplicit()
	{
		using var document = Fixture();
		var player = new GltfGpuAnimationPlayer( document ); player.SelectClip( 0 );
		player.Seek( 3 ); Assert.IsFalse( player.IsPlaying ); Assert.AreEqual( .75f, player.CurrentPose.GetMorphWeights( 0 )[0] );
		player.Speed = .5; player.Play(); player.Advance( 1 ); Assert.AreEqual( 3.5, player.CurrentTime );
		player.Pause(); Assert.IsFalse( player.Advance( 1 ) ); Assert.AreEqual( 3.5, player.CurrentTime );
		player.SelectClip( null ); Assert.IsNull( player.Animation ); Assert.AreEqual( 0d, player.CurrentTime );
		Assert.AreEqual( -.5f, player.CurrentPose.GetMorphWeights( 0 )[0] );
	}

	[TestMethod]
	public void ClockRetainsSubframePrecisionBeyondFloatTimePrecision()
	{
		using var document = Fixture( 33554432 );
		var player = new GltfGpuAnimationPlayer( document ); player.SelectClip( 0 ); player.Seek( 16777216 ); player.Play();
		player.Advance( .125 ); player.Advance( .125 );
		Assert.AreEqual( 16777216.25, player.CurrentTime, 1e-10 );
		// EvaluatePose takes float, but source time accumulation must remain double.
	}

	[TestMethod]
	[DataRow( double.NaN )]
	[DataRow( double.PositiveInfinity )]
	[DataRow( double.NegativeInfinity )]
	public void InvalidElapsedSeekAndSpeedLeaveExistingStateUnchanged( double invalid )
	{
		using var document = Fixture();
		var player = new GltfGpuAnimationPlayer( document ); player.SelectClip( 0 ); player.Seek( 1 ); player.Play();
		var pose = player.CurrentPose;
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => player.Advance( invalid ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => player.Seek( invalid ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => player.Speed = invalid );
		Assert.AreEqual( 1d, player.CurrentTime ); Assert.AreEqual( 1d, player.Speed ); Assert.AreSame( pose, player.CurrentPose );
	}

	[TestMethod]
	public void ReversePlaybackRateUsesAuthoredSecondsAndOneSharedPose()
	{
		using var document = Fixture(); var callbacks = 0;
		var player = new GltfGpuAnimationPlayer( document, _ => callbacks++ );
		player.SelectClip( 0 ); player.Seek( 3 ); player.Speed = -2; player.Play();
		Assert.IsTrue( player.Advance( .375 ) );
		Assert.AreEqual( 2.25, player.CurrentTime ); Assert.AreEqual( .5625f, player.CurrentPose.GetMorphWeights( 0 )[0] );
		Assert.AreEqual( 3, callbacks ); Assert.IsTrue( player.IsPlaying );
	}

	[TestMethod]
	public void ReverseLoopRetainsPositiveRemainderAcrossSeveralCycles()
	{
		using var document = Fixture();
		var player = new GltfGpuAnimationPlayer( document ); player.SelectClip( 0, looping: true );
		player.Seek( .25 ); player.Speed = -1; player.Play(); player.Advance( 9 );
		Assert.AreEqual( 3.25, player.CurrentTime ); Assert.AreEqual( .8125f, player.CurrentPose.GetMorphWeights( 0 )[0] );
		Assert.IsTrue( player.IsPlaying );
	}

	[TestMethod]
	public void ReverseNonloopAppliesZeroEndpointBeforeStopping()
	{
		using var document = Fixture(); var callbacks = 0;
		var player = new GltfGpuAnimationPlayer( document, _ => callbacks++ ); player.SelectClip( 0 );
		player.Seek( .5 ); player.Speed = -2; player.Play(); player.Advance( .5 );
		Assert.AreEqual( 0d, player.CurrentTime ); Assert.AreEqual( 0f, player.CurrentPose.GetMorphWeights( 0 )[0] );
		Assert.IsFalse( player.IsPlaying ); var before = callbacks;
		Assert.IsFalse( player.Advance( .25 ) ); Assert.AreEqual( before, callbacks );
	}

	[TestMethod]
	[DataRow( -.5, 1d, 1d )]
	[DataRow( -.5, -2d, 2.5 )]
	public void SignedDeltaAndRateFollowExistingSequenceDirection( double delta, double speed, double expected )
	{
		using var document = Fixture();
		var player = new GltfGpuAnimationPlayer( document ); player.SelectClip( 0 ); player.Seek( 1.5 );
		player.Speed = speed; player.Play(); Assert.IsTrue( player.Advance( delta ) );
		Assert.AreEqual( expected, player.CurrentTime ); Assert.AreEqual( (float)(expected / 4), player.CurrentPose.GetMorphWeights( 0 )[0] );
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void ExplicitSequenceSeekClampsEvenWhenLooping( bool looping )
	{
		using var document = Fixture();
		var player = new GltfGpuAnimationPlayer( document ); player.SelectClip( 0, looping );
		player.Seek( -1 ); Assert.AreEqual( 0d, player.CurrentTime ); Assert.AreEqual( 0f, player.CurrentPose.GetMorphWeights( 0 )[0] );
		player.Seek( 8 ); Assert.AreEqual( 4d, player.CurrentTime ); Assert.AreEqual( 1f, player.CurrentPose.GetMorphWeights( 0 )[0] );
		Assert.IsFalse( player.IsPlaying );
	}

	[TestMethod]
	public void ZeroDeltaOrRateDoesNotEvaluateOrApplyAnotherPose()
	{
		using var document = Fixture(); var callbacks = 0;
		var player = new GltfGpuAnimationPlayer( document, _ => callbacks++ ); player.SelectClip( 0 ); player.Play();
		var pose = player.CurrentPose;
		Assert.IsFalse( player.Advance( 0 ) ); Assert.AreSame( pose, player.CurrentPose ); Assert.AreEqual( 1, callbacks );
		player.Speed = 0;
		Assert.IsFalse( player.Advance( 3 ) ); Assert.AreSame( pose, player.CurrentPose ); Assert.AreEqual( 1, callbacks );
		Assert.IsTrue( player.IsPlaying );
	}

	[TestMethod]
	public void ExplicitPlayCanResumeReverseFromCompletedForwardEndpoint()
	{
		using var document = Fixture();
		var player = new GltfGpuAnimationPlayer( document ); player.SelectClip( 0 ); player.Play(); player.Advance( 4 );
		Assert.IsFalse( player.IsPlaying ); Assert.AreEqual( 4d, player.CurrentTime );
		player.Speed = -1; player.Play(); Assert.IsTrue( player.Advance( 1 ) );
		Assert.AreEqual( 3d, player.CurrentTime ); Assert.AreEqual( .75f, player.CurrentPose.GetMorphWeights( 0 )[0] );
		Assert.IsTrue( player.IsPlaying );
	}

	[TestMethod]
	public void ZeroRateRetainsLastMovementDirectionAtOppositeEndpoint()
	{
		using var document = Fixture();
		var player = new GltfGpuAnimationPlayer( document ); player.SelectClip( 0 ); player.Seek( 2 );
		player.Speed = -1; player.Play(); player.Advance( 1 ); player.Speed = 0; player.Seek( 4 );
		var pose = player.CurrentPose;
		Assert.IsFalse( player.Advance( 1 ) ); Assert.AreSame( pose, player.CurrentPose ); Assert.AreEqual( 4d, player.CurrentTime );
		Assert.IsTrue( player.IsPlaying );
	}

	[TestMethod]
	public void FiniteOverflowDoesNotPublishClockOrPoseChanges()
	{
		using var document = Fixture();
		var player = new GltfGpuAnimationPlayer( document ); player.SelectClip( 0 ); player.Seek( 1 ); player.Play();
		player.Speed = double.MaxValue; var pose = player.CurrentPose;
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => player.Advance( double.MaxValue ) );
		Assert.AreEqual( 1d, player.CurrentTime ); Assert.AreSame( pose, player.CurrentPose ); Assert.IsTrue( player.IsPlaying );
	}

	private static GltfDocument Fixture( float duration = 4 )
	{
		var bytes = new byte[16];
		BinaryPrimitives.WriteSingleLittleEndian( bytes.AsSpan( 4 ), duration );
		BinaryPrimitives.WriteSingleLittleEndian( bytes.AsSpan( 12 ), 1 );
		var json = $$$"""
			{"asset":{"version":"2.0"},"buffers":[{"uri":"data:application/octet-stream;base64,{{{Convert.ToBase64String( bytes )}}}","byteLength":16}],
			"bufferViews":[{"buffer":0,"byteLength":8},{"buffer":0,"byteOffset":8,"byteLength":8}],
			"accessors":[{"componentType":5126,"count":3,"type":"VEC3","min":[0,0,0],"max":[0,0,0]},
			{"bufferView":0,"componentType":5126,"count":2,"type":"SCALAR","min":[0],"max":[{{{duration.ToString( System.Globalization.CultureInfo.InvariantCulture )}}}]},
			{"bufferView":1,"componentType":5126,"count":2,"type":"SCALAR"}],
			"meshes":[{"weights":[-0.5],"primitives":[{"attributes":{"POSITION":0},"targets":[{"POSITION":0}]}]}],
			"nodes":[{"mesh":0}],"animations":[{"samplers":[{"input":1,"output":2}],"channels":[{"sampler":0,"target":{"node":0,"path":"weights"}}]}]}
			""";
		return GltfReader.Read( Encoding.UTF8.GetBytes( json ) );
	}
}
