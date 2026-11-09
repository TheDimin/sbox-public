using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;

namespace Sandbox.Tests;

[TestClass]
public sealed class GltfAnimationMixerTests
{
	[TestMethod]
	public void SamplesIndependentClipsAtOneAuthoredTimeAndClampsTheShorterClip()
	{
		using var model = Model();
		var mixer = new GltfAnimationMixer( model );
		mixer.Select( ["Move", "Scale"] );
		Assert.AreEqual( 2, mixer.Duration );
		mixer.Time = .5f;
		var pose = mixer.EvaluatePose();
		Assert.AreEqual( 1f, pose.GetWorldTransform( 0 ).M41, 1e-6f );
		Assert.AreEqual( 1.5f, pose.GetWorldTransform( 1 ).M11, 1e-6f );
		mixer.Time = 1.5f;
		pose = mixer.EvaluatePose();
		Assert.AreEqual( 2f, pose.GetWorldTransform( 0 ).M41, 1e-6f );
		Assert.AreEqual( 2.5f, pose.GetWorldTransform( 1 ).M11, 1e-6f );
	}

	[TestMethod]
	public void SelectionFailuresLeaveSelectionClockAndPoseUntouched()
	{
		using var model = Model();
		var mixer = new GltfAnimationMixer( model );
		mixer.Select( ["Move", "Scale"] );
		mixer.Time = .5f;
		var before = mixer.EvaluatePose().GetWorldTransform( 0 );
		Assert.ThrowsException<ArgumentException>( () => mixer.Select( ["Move", "Missing"] ) );
		Assert.ThrowsException<GltfException>( () => mixer.Select( ["Move", "Overlap"] ) );
		Assert.ThrowsException<GltfException>( () => mixer.Select( ["Move", "Move"] ) );
		CollectionAssert.AreEqual( new[] { "Move", "Scale" }, mixer.Names.ToArray() );
		Assert.AreEqual( .5f, mixer.Time );
		Assert.AreEqual( before, mixer.EvaluatePose().GetWorldTransform( 0 ) );
	}

	[TestMethod]
	public void SharedClockWrapsAtGroupDurationAndEmptySelectionRestoresPose()
	{
		using var model = Model();
		var mixer = new GltfAnimationMixer( model ) { Looping = true };
		mixer.Select( ["Move", "Scale"] );
		mixer.Time = 1.75f;
		mixer.Advance( .5, 1 );
		Assert.AreEqual( .25f, mixer.Time, 1e-6f );
		mixer.Select( [] );
		Assert.AreEqual( 0f, mixer.Duration );
		Assert.AreEqual( 0f, mixer.Time );
		Assert.AreEqual( 0f, mixer.EvaluatePose().GetWorldTransform( 0 ).M41 );
		Assert.AreEqual( 1f, mixer.EvaluatePose().GetWorldTransform( 1 ).M11 );
	}

	[TestMethod]
	public void ReversePlaybackAndEndpointSeeksUseGroupDuration()
	{
		using var model = Model();
		var mixer = new GltfAnimationMixer( model ) { Looping = true };
		mixer.Select( ["Move", "Scale"] );
		mixer.Time = 2;
		Assert.AreEqual( 2f, mixer.Time );
		mixer.Advance( .25, -1 );
		Assert.AreEqual( 1.75f, mixer.Time );
		mixer.Advance( -2, 1 );
		Assert.AreEqual( 1.75f, mixer.Time );
		mixer.Looping = false;
		mixer.Time = 3;
		Assert.AreEqual( 2f, mixer.Time );
		mixer.Advance( 3, -1 );
		Assert.AreEqual( 0f, mixer.Time );
		Assert.IsTrue( mixer.IsFinished );
	}

	[TestMethod]
	[DataRow( float.NaN )]
	[DataRow( float.PositiveInfinity )]
	[DataRow( float.NegativeInfinity )]
	public void NonfiniteTimeOrRateCannotAlterClockOrPose( float value )
	{
		using var model = Model();
		var mixer = new GltfAnimationMixer( model );
		mixer.Select( ["Move"] ); mixer.Time = .5f;
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => mixer.Time = value );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => mixer.Advance( 1, value ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => mixer.Advance( value, 1 ) );
		Assert.AreEqual( .5f, mixer.Time );
		Assert.AreEqual( 1f, mixer.EvaluatePose().GetWorldTransform( 0 ).M41 );
	}

	[TestMethod]
	public void IndependentMixersDoNotShareSelectionOrClock()
	{
		using var model = Model();
		var first = new GltfAnimationMixer( model );
		var second = new GltfAnimationMixer( model );
		first.Select( ["Move", "Scale"] ); second.Select( ["Move"] );
		first.Time = .25f; second.Time = .75f;
		Assert.AreEqual( .5f, first.EvaluatePose().GetWorldTransform( 0 ).M41 );
		Assert.AreEqual( 1.5f, second.EvaluatePose().GetWorldTransform( 0 ).M41 );
		first.Select( [] );
		Assert.AreEqual( .75f, second.Time );
		Assert.AreEqual( 1.5f, second.EvaluatePose().GetWorldTransform( 0 ).M41 );
	}

	[TestMethod]
	public void SelectedNamesAreCopiedFromTheCallersMutableList()
	{
		using var model = Model();
		var mixer = new GltfAnimationMixer( model );
		var selected = new System.Collections.Generic.List<string> { "Move", "Scale" };
		mixer.Select( selected );
		selected[0] = "Overlap"; selected.Clear();
		CollectionAssert.AreEqual( new[] { "Move", "Scale" }, mixer.Names.ToArray() );
		mixer.Time = .5f;
		Assert.AreEqual( 1f, mixer.EvaluatePose().GetWorldTransform( 0 ).M41 );
	}

	[TestMethod]
	public void LeadingDelayUsesAuthoredTimeWithoutShiftingTheFirstKeyToZero()
	{
		using var model = Model( leading: true );
		var mixer = new GltfAnimationMixer( model );
		mixer.Select( ["Move"] );
		mixer.Time = .25f;
		Assert.AreEqual( 0f, mixer.EvaluatePose().GetWorldTransform( 0 ).M41 );
		mixer.Time = .75f;
		Assert.AreEqual( 1f, mixer.EvaluatePose().GetWorldTransform( 0 ).M41 );
	}

	[TestMethod]
	public void GeneratedAliasesDoNotAuthorizeRebindAfterReordering()
	{
		using var first = Model( names: ["Walk", "Walk", null] );
		using var reordered = Model( names: ["Walk", null, "Walk"] );
		var mixer = new GltfAnimationMixer( first );
		mixer.Select( ["Walk"] );
		Assert.IsFalse( mixer.CanRebindTo( reordered ), "An authored duplicate is ambiguous even when its first alias has no suffix." );
		mixer.Select( ["animation_2"] );
		Assert.IsFalse( mixer.CanRebindTo( reordered ), "An index-derived unnamed alias is not a stable identity." );
		using var unique = Model();
		var safe = new GltfAnimationMixer( unique ); safe.Select( ["Move", "Scale"] );
		using var sameNames = Model();
		Assert.IsTrue( safe.CanRebindTo( sameNames ) );
		using var noAnimations = new GltfModelAnimation( GltfReader.Read( Encoding.UTF8.GetBytes( "{\"asset\":{\"version\":\"2.0\"}}" ) ) );
		Assert.IsFalse( safe.CanRebindTo( noAnimations ), "Removing animations must fail rebind without looking up an absent array." );
	}

	[TestMethod]
	public void UnsupportedOptionalPointerFallsBackWhileCoreChannelsStillAnimate()
	{
		using var source = Model();
		var json = JsonNode.Parse( source.Document.Root.GetRawText() );
		json["nodes"][1]["translation"] = JsonNode.Parse( "[0,0,0]" );
		json["animations"][0]["channels"].AsArray().Add( JsonNode.Parse( "{\"sampler\":0,\"target\":{\"path\":\"pointer\",\"extensions\":{\"KHR_animation_pointer\":{\"pointer\":\"/nodes/1/translation\"}}}}" ) );
		json["extensionsUsed"] = JsonNode.Parse( "[\"KHR_animation_pointer\"]" );
		using var model = new GltfModelAnimation( GltfReader.Read( Encoding.UTF8.GetBytes( json.ToJsonString() ) ) );
		CollectionAssert.Contains( model.Document.UnsupportedOptionalExtensions.ToArray(), "KHR_animation_pointer" );
		var mixer = new GltfAnimationMixer( model ); mixer.Select( ["Move"] ); mixer.Time = .5f;
		Assert.AreEqual( 1f, mixer.EvaluatePose().GetWorldTransform( 0 ).M41 );
		Assert.AreEqual( 0f, mixer.EvaluatePose().GetWorldTransform( 1 ).M41 );
		// The core fallback ignores the pointer channel; this is not evidence of animated material color.
		using var falselyAdvertised = GltfReader.Read( Encoding.UTF8.GetBytes( json.ToJsonString() ), supportedExtensions: new System.Collections.Generic.HashSet<string> { "KHR_animation_pointer" } );
		var error = Assert.ThrowsException<GltfException>( () => falselyAdvertised.EvaluatePose( 0, .5f ) );
		StringAssert.Contains( error.Message, "KHR_animation_pointer" );
	}

	private static GltfModelAnimation Model( bool leading = false, string[] names = null )
	{
		names ??= ["Move", "Scale", "Overlap"];
		using var stream = new MemoryStream();
		using ( var writer = new BinaryWriter( stream, Encoding.UTF8, leaveOpen: true ) )
			foreach ( var value in new float[] { leading ? .5f : 0, 1, 0, 2, 0, 0, 0, 2, 0, 0, 1, 1, 1, 3, 3, 3 } ) writer.Write( value );
		var bytes = stream.ToArray();
		var json = "{\"asset\":{\"version\":\"2.0\"},\"buffers\":[{\"uri\":\"data:application/octet-stream;base64," + Convert.ToBase64String( bytes ) + "\",\"byteLength\":64}],"
			+ "\"bufferViews\":[{\"buffer\":0,\"byteLength\":8},{\"buffer\":0,\"byteOffset\":8,\"byteLength\":8},{\"buffer\":0,\"byteOffset\":16,\"byteLength\":24},{\"buffer\":0,\"byteOffset\":40,\"byteLength\":24}],"
			+ "\"accessors\":[{\"bufferView\":0,\"componentType\":5126,\"count\":2,\"type\":\"SCALAR\",\"min\":[" + (leading ? "0.5" : "0") + "],\"max\":[1]},{\"bufferView\":1,\"componentType\":5126,\"count\":2,\"type\":\"SCALAR\",\"min\":[0],\"max\":[2]},{\"bufferView\":2,\"componentType\":5126,\"count\":2,\"type\":\"VEC3\"},{\"bufferView\":3,\"componentType\":5126,\"count\":2,\"type\":\"VEC3\"}],"
			+ "\"nodes\":[{},{}],\"animations\":[" + Clip( names[0], 0, 2, 0, "translation" ) + "," + Clip( names[1], 1, 3, 1, "scale" ) + "," + Clip( names[2], 0, 2, 0, "translation" ) + "]}";
		return new GltfModelAnimation( GltfReader.Read( Encoding.UTF8.GetBytes( json ) ) );
	}
	private static string Clip( string name, int input, int output, int node, string path ) =>
		"{" + (name is null ? "" : "\"name\":\"" + name + "\",") + "\"samplers\":[{\"input\":" + input + ",\"output\":" + output + "}],\"channels\":[{\"sampler\":0,\"target\":{\"node\":" + node + ",\"path\":\"" + path + "\"}}]}";
}
