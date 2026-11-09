using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Sandbox.Tests;

using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

/// <summary>Shared authored-seconds clock logic, independent of documents, models and rendering.</summary>
[TestClass]
public class GltfPlaybackClockTests
{
	[TestMethod]
	public void ResetKeepsLoopSettingAndResetsTimeAndDirection()
	{
		var clock = new GltfPlaybackClock { Looping = true }; clock.Reset( 4 ); clock.Seek( 2 ); clock.Advance( 1, -1 );
		Assert.IsTrue( clock.Reverse ); clock.Reset( 8 );
		Assert.AreEqual( 8d, clock.Duration ); Assert.AreEqual( 0d, clock.Time );
		Assert.IsFalse( clock.Reverse ); Assert.IsTrue( clock.Looping ); Assert.IsFalse( clock.IsFinished );
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void ExplicitTimeSeekAlwaysClampsRatherThanLooping( bool looping )
	{
		var clock = new GltfPlaybackClock { Looping = looping }; clock.Reset( 4 );
		clock.Seek( -10 ); Assert.AreEqual( 0d, clock.Time );
		clock.Seek( 10 ); Assert.AreEqual( 4d, clock.Time );
		Assert.AreEqual( !looping, clock.IsFinished );
	}

	[TestMethod]
	[DataRow( .75, 2d, 1d, 2.5, false )]
	[DataRow( .75, -2d, 3d, 1.5, true )]
	[DataRow( -.75, -2d, 1d, 2.5, false )]
	[DataRow( -.75, 2d, 3d, 1.5, true )]
	public void SignedDeltaTimesSignedRateDeterminesMovement( double delta, double rate, double start, double expected, bool reverse )
	{
		var clock = new GltfPlaybackClock(); clock.Reset( 4 ); clock.Seek( start );
		Assert.IsTrue( clock.Advance( delta, rate ) );
		Assert.AreEqual( expected, clock.Time ); Assert.AreEqual( reverse, clock.Reverse ); Assert.IsFalse( clock.IsFinished );
	}

	[TestMethod]
	[DataRow( 9d, 1d, 1.25 )]
	[DataRow( 9d, -1d, 3.25 )]
	[DataRow( -9d, -1d, 1.25 )]
	[DataRow( -9d, 1d, 3.25 )]
	public void LoopingPreservesPositivePhaseAcrossMultipleSignedCycles( double delta, double rate, double expected )
	{
		var clock = new GltfPlaybackClock { Looping = true }; clock.Reset( 4 ); clock.Seek( .25 );
		Assert.IsTrue( clock.Advance( delta, rate ) ); Assert.AreEqual( expected, clock.Time ); Assert.IsFalse( clock.IsFinished );
	}

	[TestMethod]
	public void FinishEndpointFollowsLastNonzeroMovementAndSeekDoesNotChangeDirection()
	{
		var clock = new GltfPlaybackClock(); clock.Reset( 4 ); clock.Seek( 3 ); clock.Advance( 10, -1 );
		Assert.AreEqual( 0d, clock.Time ); Assert.IsTrue( clock.Reverse ); Assert.IsTrue( clock.IsFinished );
		clock.Seek( 4 ); Assert.IsFalse( clock.IsFinished );
		clock.Advance( 10, 1 ); Assert.AreEqual( 4d, clock.Time ); Assert.IsFalse( clock.Reverse ); Assert.IsTrue( clock.IsFinished );
		clock.Seek( 0 ); Assert.IsFalse( clock.IsFinished );
	}

	[TestMethod]
	[DataRow( 0d, 1d )]
	[DataRow( 1d, 0d )]
	[DataRow( -0d, -2d )]
	public void ZeroDeltaOrRatePreservesClockAndLastDirection( double delta, double rate )
	{
		var clock = new GltfPlaybackClock(); clock.Reset( 4 ); clock.Seek( 2 ); clock.Advance( 1, -1 ); clock.Seek( 4 );
		Assert.IsFalse( clock.Advance( delta, rate ) ); Assert.AreEqual( 4d, clock.Time );
		Assert.IsTrue( clock.Reverse ); Assert.IsFalse( clock.IsFinished );
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void ZeroDurationHasStableEndpointAndNoAdvance( bool looping )
	{
		var clock = new GltfPlaybackClock { Looping = looping }; clock.Reset( 0 ); clock.Seek( 7 );
		Assert.IsFalse( clock.Advance( 1, -1 ) ); Assert.AreEqual( 0d, clock.Time ); Assert.IsFalse( clock.Reverse );
		Assert.AreEqual( !looping, clock.IsFinished );
	}

	[TestMethod]
	[DataRow( double.NaN )]
	[DataRow( double.PositiveInfinity )]
	[DataRow( double.NegativeInfinity )]
	public void NonfiniteOperationsDoNotPublishStateChanges( double invalid )
	{
		var clock = new GltfPlaybackClock(); clock.Reset( 4 ); clock.Seek( 2 ); clock.Advance( 1, -1 );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => clock.Reset( invalid ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => clock.Seek( invalid ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => clock.Advance( invalid, 1 ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => clock.Advance( 1, invalid ) );
		Assert.AreEqual( 4d, clock.Duration ); Assert.AreEqual( 1d, clock.Time ); Assert.IsTrue( clock.Reverse );
	}

	[TestMethod]
	public void NegativeDurationIsRejectedWithoutReplacingPriorState()
	{
		var clock = new GltfPlaybackClock(); clock.Reset( 4 ); clock.Seek( 2 ); clock.Advance( 1, -1 );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => clock.Reset( -1 ) );
		Assert.AreEqual( 4d, clock.Duration ); Assert.AreEqual( 1d, clock.Time ); Assert.IsTrue( clock.Reverse );
	}

	[TestMethod]
	public void MultiplicationAndAdditionOverflowAreAtomic()
	{
		var clock = new GltfPlaybackClock(); clock.Reset( 4 ); clock.Seek( 2 ); clock.Advance( 1, -1 );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => clock.Advance( double.MaxValue, double.MaxValue ) );
		Assert.AreEqual( 1d, clock.Time ); Assert.IsTrue( clock.Reverse );
		clock.Reset( double.MaxValue ); clock.Seek( double.MaxValue );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => clock.Advance( double.MaxValue, 1 ) );
		Assert.AreEqual( double.MaxValue, clock.Time ); Assert.IsFalse( clock.Reverse );
	}

	[TestMethod]
	public void PositiveLoopDoesNotLoseSmallTimeByAddingLargeDuration()
	{
		var clock = new GltfPlaybackClock { Looping = true }; clock.Reset( 33554432 );
		clock.Advance( 1e-9, 1 ); Assert.AreEqual( 1e-9, clock.Time );
	}

	[TestMethod]
	public void RoundedReverseLoopBoundaryKeepsExistingZeroEndpointPolicy()
	{
		var clock = new GltfPlaybackClock { Looping = true }; clock.Reset( 33554432 );
		clock.Advance( -1e-9, 1 );
		// In double precision, adding this tiny negative remainder to the
		// duration rounds to the exact duration. Existing sequence semantics
		// apply modulo again, preserving the [0,duration) range at zero.
		Assert.AreEqual( 0d, clock.Time ); Assert.IsTrue( clock.Time < clock.Duration );
		Assert.IsTrue( clock.Reverse ); Assert.IsFalse( clock.IsFinished );
	}

	[TestMethod]
	public void IndependentInstanceClocksNeverShareState()
	{
		var first = new GltfPlaybackClock(); var second = new GltfPlaybackClock(); first.Reset( 4 ); second.Reset( 8 );
		first.Seek( 3 ); second.Seek( 5 ); first.Advance( 1, -1 );
		Assert.AreEqual( 2d, first.Time ); Assert.AreEqual( 5d, second.Time ); Assert.IsFalse( second.Reverse );
	}

	[TestMethod]
	public void WarmAdvancementAllocatesNoManagedMemory()
	{
		var clock = new GltfPlaybackClock { Looping = true }; clock.Reset( 4 );
		for ( var iteration = 0; iteration < 512; iteration++ ) clock.Advance( 1d / 64, 1 );
		clock.Reset( 4 ); var before = GC.GetAllocatedBytesForCurrentThread();
		for ( var iteration = 0; iteration < 10000; iteration++ ) clock.Advance( 1d / 64, 1 );
		var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
		Assert.AreEqual( .25, clock.Time ); Assert.AreEqual( 0L, allocated );
	}
}
