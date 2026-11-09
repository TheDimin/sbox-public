using Sandbox.Gltf;
using System;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Sandbox.Tests;

/// <summary>Tests the sequence used by SceneModel, without a native animation-frame approximation.</summary>
[TestClass]
public class GltfSceneAnimationTests
{
	[TestMethod]
	public void ClipMetadataPreservesOrderAndMakesMissingOrDuplicateNamesAddressable()
	{
		using var model = Read( names: ["Walk", "Walk", null] );
		CollectionAssert.AreEqual( new[] { "Walk", "Walk_1", "animation_2" }, model.SequenceNames.ToArray() );
		Assert.AreEqual( 1f, model.Clips[0].Duration );
	}

	[TestMethod]
	public void TwoInstancesHaveIndependentSelectionAndClock()
	{
		using var model = Read( names: ["First", "Second"] );
		var first = new GltfSceneAnimation( model ) { Name = "First", Time = .125f };
		var second = new GltfSceneAnimation( model ) { Name = "Second", Time = .75f };
		first.Advance( .25f, 1 );
		Assert.AreEqual( .375f, first.Time );
		Assert.AreEqual( .75f, second.Time );
		Assert.AreEqual( "Second", second.Name );
	}

	[TestMethod]
	public void ArbitrarySampleTimeUsesOriginalLinearKeyTimes()
	{
		using var model = Read();
		var sequence = new GltfSceneAnimation( model ) { Name = "Move", Time = .1234567f };
		Assert.AreEqual( .1234567f, sequence.EvaluatePose().GetWorldTransform( 0 ).M41, .0000001f );
		Assert.AreEqual( .1234567f, sequence.TimeNormalized, .0000001f );
	}

	[TestMethod]
	public void StepTransitionIsNotSmoothedOrResampled()
	{
		using var model = Read( interpolation: "STEP" );
		var sequence = new GltfSceneAnimation( model ) { Name = "Move", Time = .99999f };
		Assert.AreEqual( 0f, sequence.EvaluatePose().GetWorldTransform( 0 ).M41 );
		sequence.Time = 1f;
		Assert.AreEqual( 1f, sequence.EvaluatePose().GetWorldTransform( 0 ).M41 );
	}

	[TestMethod]
	public void CubicTangentsRemainExactAtArbitraryTimes()
	{
		using var model = Read( interpolation: "CUBICSPLINE" );
		var sequence = new GltfSceneAnimation( model ) { Name = "Move", Time = .123f };
		Assert.AreEqual( 2f * .123f - .123f * .123f, sequence.EvaluatePose().GetWorldTransform( 0 ).M41, .0000001f );
	}

	[TestMethod]
	public void LoopingWrapsPositiveAndNegativePlaybackRates()
	{
		using var model = Read();
		var sequence = new GltfSceneAnimation( model ) { Name = "Move", Time = .9f };
		sequence.Advance( .25f, 1 );
		Assert.AreEqual( .15f, sequence.Time, .000001f );
		sequence.Advance( .25f, -1 );
		Assert.AreEqual( .9f, sequence.Time, .000001f );
		Assert.IsFalse( sequence.IsFinished );
	}

	[TestMethod]
	public void NonLoopingPlaybackClampsAndFinishesAtTheLastKey()
	{
		using var model = Read();
		var sequence = new GltfSceneAnimation( model ) { Name = "Move", Looping = false };
		sequence.Advance( 2, 1 );
		Assert.AreEqual( 1f, sequence.Time );
		Assert.IsTrue( sequence.IsFinished );
		Assert.AreEqual( 1f, sequence.EvaluatePose().GetWorldTransform( 0 ).M41 );
	}

	[TestMethod]
	public void NormalizedSeekingAndPausedPlaybackUseExistingSequenceControls()
	{
		using var model = Read();
		var sequence = new GltfSceneAnimation( model ) { Name = "Move", TimeNormalized = .25f };
		sequence.Advance( 1, 0 );
		Assert.AreEqual( .25f, sequence.Time );
		sequence.TimeNormalized = 2;
		Assert.AreEqual( 1f, sequence.Time );
		sequence.Time = -1;
		Assert.AreEqual( 0f, sequence.Time );
	}

	[TestMethod]
	public void ClearingSelectionRestoresAuthoredPoseAndChangingClipResetsTime()
	{
		using var model = Read( names: ["First", "Second"] );
		var sequence = new GltfSceneAnimation( model ) { Name = "First", Time = .75f };
		sequence.Name = "Second";
		Assert.AreEqual( 0f, sequence.Time );
		sequence.Name = "";
		Assert.AreEqual( 3f, sequence.EvaluatePose().GetWorldTransform( 0 ).M41 );
		Assert.AreEqual( 0f, sequence.Duration );
	}

	[TestMethod]
	public void BindPoseResetDoesNotEraseClipSelectionOrThePlaybackClock()
	{
		using var model = Read();
		var sequence = new GltfSceneAnimation( model ) { Name = "Move", Time = .75f };
		Assert.AreEqual( 3f, sequence.EvaluateBindPose().GetWorldTransform( 0 ).M41 );
		Assert.AreEqual( "Move", sequence.Name );
		Assert.AreEqual( .75f, sequence.Time );
		Assert.AreEqual( .75f, sequence.EvaluatePose().GetWorldTransform( 0 ).M41 );
	}

	[TestMethod]
	public void UnknownSelectionAndUnimplementedTransitionBlendingFailExplicitly()
	{
		using var model = Read();
		var sequence = new GltfSceneAnimation( model );
		Assert.ThrowsException<ArgumentException>( () => sequence.Name = "Missing" );
		Assert.ThrowsException<NotSupportedException>( () => sequence.Blending = true );
	}

	[TestMethod]
	[DataRow( float.NaN )]
	[DataRow( float.PositiveInfinity )]
	[DataRow( float.NegativeInfinity )]
	public void NonFiniteClockInputIsRejectedBeforeEvaluation( float invalid )
	{
		using var model = Read();
		var sequence = new GltfSceneAnimation( model ) { Name = "Move" };
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => sequence.Time = invalid );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => sequence.TimeNormalized = invalid );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => sequence.Advance( invalid, 1 ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => sequence.Advance( 1, invalid ) );
	}

	[TestMethod]
	public void DisposingModelMetadataReleasesTheRetainedDocument()
	{
		var model = Read();
		var sequence = new GltfSceneAnimation( model ) { Name = "Move" };
		model.Dispose();
		Assert.AreEqual( "Move", sequence.Name );
		Assert.AreEqual( 1f, sequence.Duration );
		Assert.AreEqual( 0f, sequence.Time );
		Assert.ThrowsException<ObjectDisposedException>( () => sequence.EvaluatePose() );
		model.Dispose();
	}

	[TestMethod]
	public void NonzeroFirstKeyKeepsItsAuthoredTimelineOffset()
	{
		using var model = Read( start: 2, end: 3 );
		Assert.AreEqual( 2f, model.Clips[0].StartTime );
		var sequence = new GltfSceneAnimation( model ) { Name = "Move" };
		Assert.AreEqual( 3f, sequence.Duration, "The original glTF time origin and leading delay must not be shifted." );
		Assert.AreEqual( 0f, sequence.EvaluatePose().GetWorldTransform( 0 ).M41 );
		sequence.Time = 2.5f;
		Assert.AreEqual( .5f, sequence.EvaluatePose().GetWorldTransform( 0 ).M41 );
	}

	[TestMethod]
	public void InvalidSelectionPreservesTheLastValidClipAndTime()
	{
		using var model = Read();
		var sequence = new GltfSceneAnimation( model ) { Name = "Move", Time = .75f };
		Assert.ThrowsException<ArgumentException>( () => sequence.Name = "Missing" );
		Assert.AreEqual( "Move", sequence.Name );
		Assert.AreEqual( .75f, sequence.Time );
	}

	[TestMethod]
	[DataRow( float.MaxValue, float.MaxValue )]
	[DataRow( float.MaxValue, float.MinValue )]
	[DataRow( float.MinValue, float.MaxValue )]
	[DataRow( float.MinValue, float.MinValue )]
	public void ExtremeFiniteDeltaAndRateKeepTheLoopingClockFinite( float delta, float rate )
	{
		using var model = Read();
		var sequence = new GltfSceneAnimation( model ) { Name = "Move", Time = .5f };
		sequence.Advance( delta, rate );
		Assert.IsTrue( float.IsFinite( sequence.Time ) );
		Assert.IsTrue( sequence.Time >= 0 && sequence.Time < sequence.Duration );
		Assert.IsTrue( float.IsFinite( sequence.EvaluatePose().GetWorldTransform( 0 ).M41 ) );
	}

	[TestMethod]
	[DataRow( float.MaxValue, 1f )]
	[DataRow( float.MinValue, 0f )]
	public void ExtremeFiniteRateClampsNonLoopingClockToTheCorrectEndpoint( float rate, float expected )
	{
		using var model = Read();
		var sequence = new GltfSceneAnimation( model ) { Name = "Move", Time = .5f, Looping = false };
		sequence.Advance( float.MaxValue, rate );
		Assert.AreEqual( expected, sequence.Time );
		Assert.IsTrue( sequence.IsFinished );
	}

	private static GltfModelAnimation Read( string interpolation = "LINEAR", string[] names = null, float start = 0, float end = 1 )
	{
		using var stream = new MemoryStream();
		using ( var writer = new BinaryWriter( stream, Encoding.UTF8, leaveOpen: true ) )
		{
			writer.Write( start ); writer.Write( end );
			float[] values = interpolation == "CUBICSPLINE"
				? [0, 0, 0, 0, 0, 0, 2, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0]
				: [0, 0, 0, 1, 0, 0];
			foreach ( var value in values ) writer.Write( value );
		}
		var binary = stream.ToArray();
		var clips = string.Join( ",", (names ?? ["Move"]).Select( name =>
			"{" + (name is null ? "" : "\"name\":\"" + name + "\",") + "\"samplers\":[{\"input\":0,\"output\":1,\"interpolation\":\"" + interpolation + "\"}],\"channels\":[{\"sampler\":0,\"target\":{\"node\":0,\"path\":\"translation\"}}]}" ) );
		var json = "{\"asset\":{\"version\":\"2.0\"},\"buffers\":[{\"uri\":\"data:application/octet-stream;base64," + Convert.ToBase64String( binary ) + "\",\"byteLength\":" + binary.Length + "}],"
			+ "\"bufferViews\":[{\"buffer\":0,\"byteLength\":8},{\"buffer\":0,\"byteOffset\":8,\"byteLength\":" + (binary.Length - 8) + "}],"
			+ "\"accessors\":[{\"bufferView\":0,\"componentType\":5126,\"count\":2,\"type\":\"SCALAR\",\"min\":[" + start.ToString( CultureInfo.InvariantCulture ) + "],\"max\":[" + end.ToString( CultureInfo.InvariantCulture ) + "]},{\"bufferView\":1,\"componentType\":5126,\"count\":" + (interpolation == "CUBICSPLINE" ? 6 : 2) + ",\"type\":\"VEC3\"}],"
			+ "\"nodes\":[{\"translation\":[3,0,0]}],\"animations\":[" + clips + "]}";
		return new GltfModelAnimation( GltfReader.Read( Encoding.UTF8.GetBytes( json ) ) );
	}
}
