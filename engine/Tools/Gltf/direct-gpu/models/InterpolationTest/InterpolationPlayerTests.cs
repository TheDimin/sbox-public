using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Sandbox.Gltf.DirectGpu.InterpolationTestValidation;

/// <summary>Actual sample clips through the instance-owned player, not a render callback clock.</summary>
[TestClass]
public sealed class InterpolationPlayerTests
{
	[DataTestMethod]
	[DataRow( 0 )] [DataRow( 1 )] [DataRow( 2 )] [DataRow( 3 )] [DataRow( 4 )]
	[DataRow( 5 )] [DataRow( 6 )] [DataRow( 7 )] [DataRow( 8 )]
	public void EachAuthoredClipKeepsSecondsAndIndependentInstances( int clip )
	{
		using var input = new InterpolationConformanceTests.Inputs( InterpolationConformanceTests.CheckoutRoot, "glTF" );
		var delivered = new List<GltfPose>();
		var first = new GltfGpuAnimationPlayer( input.Document, pose => delivered.Add( pose ) );
		Assert.AreEqual( 0, delivered.Count, "Construction must not autoplay or submit a pose." );
		Assert.IsNull( first.Animation ); Assert.IsFalse( first.IsPlaying ); Assert.AreEqual( 0d, first.CurrentTime );
		CheckPose( input.Reference, first.CurrentPose, null, 0 );
		first.SelectClip( clip );
		Assert.AreEqual( clip, first.Animation!.Value ); Assert.AreEqual( 2d, first.Duration );
		Assert.AreEqual( 1, delivered.Count ); Assert.IsFalse( first.IsPlaying );
		Assert.IsTrue( ReferenceEquals( first.CurrentPose, delivered[^1] ), "Skin/morph consumers must receive the exact same pose object." );
		CheckPose( input.Reference, first.CurrentPose, clip, 0 );
		Assert.IsFalse( first.Advance( .75 ) ); Assert.AreEqual( 1, delivered.Count );
		first.Play(); Assert.AreEqual( 1, delivered.Count );
		Assert.IsTrue( first.Advance( .75 ) ); Assert.AreEqual( .75d, first.CurrentTime ); Assert.AreEqual( 2, delivered.Count );
		CheckPose( input.Reference, first.CurrentPose, clip, .75 );
		Assert.IsTrue( first.Advance( .625 ) ); Assert.AreEqual( 1.375d, first.CurrentTime ); Assert.AreEqual( 3, delivered.Count );
		CheckPose( input.Reference, first.CurrentPose, clip, 1.375 );
		const double preciseSeek = 1.1234567890123;
		first.Seek( preciseSeek ); Assert.AreEqual( preciseSeek, first.CurrentTime, "Only sampling may round to FLOAT, never the stored instance clock." );
		Assert.AreEqual( 4, delivered.Count ); CheckPose( input.Reference, first.CurrentPose, clip, (float)preciseSeek );

		var secondDelivered = new List<GltfPose>();
		var second = new GltfGpuAnimationPlayer( input.Document, pose => secondDelivered.Add( pose ) );
		second.SelectClip( (clip + 1) % 9, true ); second.Play(); second.Advance( .125 );
		var secondSnapshot = second.CurrentPose;
		var firstSnapshot = first.CurrentPose;
		first.Advance( .25 );
		Assert.AreEqual( .125d, second.CurrentTime ); Assert.AreEqual( 2, secondDelivered.Count );
		Assert.IsTrue( ReferenceEquals( secondSnapshot, second.CurrentPose ) );
		CheckPose( input.Reference, second.CurrentPose, (clip + 1) % 9, .125 );
		CheckPose( input.Reference, firstSnapshot, clip, (float)preciseSeek );

		first.Seek( 1.875 ); var beforeFinish = delivered.Count;
		first.Play(); Assert.IsTrue( first.Advance( .25 ) );
		Assert.AreEqual( 2d, first.CurrentTime ); Assert.IsFalse( first.IsPlaying ); Assert.AreEqual( beforeFinish + 1, delivered.Count );
		CheckPose( input.Reference, first.CurrentPose, clip, 2 );
		Assert.IsFalse( first.Advance( .5 ) ); Assert.AreEqual( beforeFinish + 1, delivered.Count );
		first.SelectClip( clip, true ); var beforeLoop = delivered.Count;
		first.Play(); Assert.IsTrue( first.Advance( 6.375 ) );
		Assert.AreEqual( .375d, first.CurrentTime ); Assert.IsTrue( first.IsPlaying ); Assert.AreEqual( beforeLoop + 1, delivered.Count );
		CheckPose( input.Reference, first.CurrentPose, clip, .375 );
		first.Speed = 2; Assert.IsTrue( first.Advance( .3125 ) ); Assert.AreEqual( 1d, first.CurrentTime );
		CheckPose( input.Reference, first.CurrentPose, clip, 1 );
		first.Pause(); var beforePause = delivered.Count; Assert.IsFalse( first.Advance( 100 ) ); Assert.AreEqual( beforePause, delivered.Count );
		first.SelectClip( null ); Assert.IsNull( first.Animation ); Assert.AreEqual( 0d, first.Duration ); Assert.IsFalse( first.IsPlaying );
		CheckPose( input.Reference, first.CurrentPose, null, 0 );
		Assert.AreEqual( .125d, second.CurrentTime ); CheckPose( input.Reference, second.CurrentPose, (clip + 1) % 9, .125 );
	}

	private static void CheckPose( InterpolationReference reference, GltfPose pose, int? clip, double time )
	{
		for ( var node = 0; node < reference.NodeCount; node++ )
		{
			var expected = reference.World( node, clip, time );
			var actual = InterpolationConformanceTests.Matrix( pose.GetWorldTransform( node ) );
			for ( var component = 0; component < expected.Length; component++ )
			{
				var error = Math.Abs( expected[component] - actual[component] ) / Math.Max( 1, Math.Abs( expected[component] ) );
				Assert.IsTrue( double.IsFinite( error ) && error <= 8 * 1.1920928955078125e-7,
					$"clip {clip} time {time:R} node {node} component {component}: expected {expected[component]:R}, actual {actual[component]:R}, scaled error {error:R}" );
			}
		}
	}
}
