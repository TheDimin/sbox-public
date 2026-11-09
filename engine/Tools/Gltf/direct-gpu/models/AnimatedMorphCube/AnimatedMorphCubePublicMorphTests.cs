using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace Sandbox.Gltf.DirectGpu.Validation;

/// <summary>
/// Positive zero-fade public API cases, written before MorphAccessor integration.
/// Loads through Model.Load and edits only existing component/sequence/morph APIs.
/// Read-only private pose diagnostics prevent a dictionary-only setter from
/// falsely passing; these synchronous cases do not establish GPU pixel output.
/// </summary>
public static class AnimatedMorphCubePublicMorphTests
{
	public sealed record Result( string Name, string Form, bool Passed, string Error );
	private const string First = "gltf_node_0_target_0";
	private const string Second = "gltf_node_0_target_1";

	/// <summary>Nine cases in original glTF and its actual compiled GLB: eighteen rows.</summary>
	public static Result[] Run( string checkout, string referencePath )
	{
		Require( Application.IsEditor && !Game.IsPlaying && ThreadSafe.IsMainThread && !Graphics.IsActive, "Public morph tests require the idle editor main thread outside graphics." );
		using var reference = JsonDocument.Parse( File.ReadAllBytes( referencePath ) );
		var form = reference.RootElement.GetProperty( "forms" ).EnumerateArray().Single( value => value.GetProperty( "source" ).GetString().Contains( "/glTF/", StringComparison.Ordinal ) );
		var results = new List<Result>();
		var cases = new (string Name, Action<Fixture, Scene> Test)[]
		{
			("logical_resource_names_do_not_expand_signed_native_controllers", (fixture, _) =>
			{
				var model = fixture.Load(); NativeFree( model );
				Require( model.MorphCount == 2 && model.Morphs.Count == 2 && model.Morphs.Names.SequenceEqual( new[] { First, Second } ), "Expose exactly two logical source targets, not native positive/negative controller pairs." );
				Require( model.GetMorphName( 0 ) == First && model.GetMorphName( 1 ) == Second, "Logical index/name lookup disagrees." );
			}),
			("zero_fade_signed_override_reaches_the_actual_instance_pose", (fixture, scene) =>
			{
				var renderer = Add( fixture, scene ); var expected = fixture.Key( 63 ); Seek( renderer, scene, expected );
				Require( renderer.ShouldShowMorphsEditor && !renderer.Morphs.ContainsOverride( First ), "Existing editor must expose authored targets without inventing overrides." );
				Near( expected.Weights[0], renderer.Morphs.Get( First ), "Initial animation-driven weight" );
				var before = renderer.GltfInstance.PoseRevision;
				renderer.Morphs.Set( First.ToUpperInvariant(), -.5f, 0 ); Tick( scene );
				Require( renderer.Morphs.ContainsOverride( First ), "Case-insensitive named override was not retained." );
				Near( -.5f, renderer.Morphs.Get( First ), "Signed public override" );
				Weights( renderer, -.5f, expected.Weights[1] );
				Require( renderer.GltfInstance.PoseRevision > before, "Morph override never reached the renderer instance." );
			}),
			("clear_returns_to_current_animation_instead_of_defaults_or_old_phase", (fixture, scene) =>
			{
				var renderer = Add( fixture, scene ); Seek( renderer, scene, fixture.Key( 63 ) );
				renderer.Morphs.Set( First, -.5f, 0 ); Tick( scene );
				var current = fixture.Key( 32 ); Seek( renderer, scene, current );
				Weights( renderer, -.5f, current.Weights[1] );
				renderer.Morphs.Clear( First, 0 ); Tick( scene );
				Require( !renderer.Morphs.ContainsOverride( First ), "Clear did not remove the manual override." );
				Near( current.Weights[0], renderer.Morphs.Get( First ), "Clear must resume the current authored sample" );
				Weights( renderer, current.Weights[0], current.Weights[1] );
			}),
			("two_public_instances_do_not_share_manual_weights_or_source_defaults", (fixture, scene) =>
			{
				var first = Add( fixture, scene ); var second = Add( fixture, scene );
				var a = fixture.Key( 63 ); var b = fixture.Key( 10 ); Seek( first, scene, a ); Seek( second, scene, b );
				Require( ReferenceEquals( first.Model, second.Model ) && !ReferenceEquals( first.GltfInstance, second.GltfInstance ), "The model is shared but renderer state must be independent." );
				first.Morphs.Set( First, -2, 0 ); Tick( scene );
				Weights( first, -2, a.Weights[1] ); Weights( second, b.Weights[0], b.Weights[1] );
				Require( !second.Morphs.ContainsOverride( First ), "Instance A leaked its manual override into B." );
				Near( b.Weights[0], second.Morphs.Get( First ), "Independent B animation" );
				first.Morphs.Clear( First, 0 ); Tick( scene );
				Weights( first, a.Weights[0], a.Weights[1] ); Weights( second, b.Weights[0], b.Weights[1] );
			}),
			("nonfinite_manual_set_is_rejected_before_dictionary_or_pose_mutation", (fixture, scene) =>
			{
				var renderer = Add( fixture, scene ); var expected = fixture.Key( 63 ); Seek( renderer, scene, expected );
				renderer.Morphs.Set( First, -.5f, 0 ); Tick( scene );
				foreach ( var invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity } )
				{
					var pose = Pose( renderer ); var revision = renderer.GltfInstance.PoseRevision;
					Throws<ArgumentOutOfRangeException>( () => renderer.Morphs.Set( First, invalid, 0 ) );
					Require( renderer.Morphs.ContainsOverride( First ) && ReferenceEquals( pose, Pose( renderer ) ) && renderer.GltfInstance.PoseRevision == revision, "Rejected weight mutated existing pose/instance state." );
					Near( -.5f, renderer.Morphs.Get( First ), "Rejected weight replaced the valid dictionary entry" );
					Weights( renderer, -.5f, expected.Weights[1] );
				}
			}),
			("clearing_one_target_preserves_other_signed_overrides", (fixture, scene) =>
			{
				var renderer = Add( fixture, scene ); var expected = fixture.Key( 63 ); Seek( renderer, scene, expected );
				renderer.Morphs.Set( First, -.5f, 0 ); renderer.Morphs.Set( Second, 2.5f, 0 ); Tick( scene );
				Weights( renderer, -.5f, 2.5f );
				renderer.Morphs.Clear( First, 0 ); Tick( scene );
				Require( !renderer.Morphs.ContainsOverride( First ) && renderer.Morphs.ContainsOverride( Second ), "Clear removed the wrong target or all overrides." );
				Weights( renderer, expected.Weights[0], 2.5f );
			}),
			("ordinary_editor_ticks_consume_full_uncapped_elapsed_and_apply_authored_weights", (fixture, scene) =>
			{
				var renderer = Add( fixture, scene ); renderer.Sequence.Looping = false; renderer.PlaybackRate = 1;
				var before = renderer.GltfInstance.PoseRevision; var boneFrame = scene.GetSystem<SceneAnimationSystem>().BoneFrame;
				scene.EditorTick( .75f, .75f ); scene.EditorTick( 1.375f, .625f );
				Require( renderer.Sequence.Time == 1.375f, "Public editor animation capped or dropped elapsed scene time." );
				Require( scene.GetSystem<SceneAnimationSystem>().BoneFrame >= boneFrame + 2 && renderer.GltfInstance.PoseRevision >= before + 2, "The ordinary scene animation stages did not apply both updates." );
				var expected = fixture.At( 1.375f ); Weights( renderer, expected[0], expected[1] );
			}),
			("ordinary_editor_tick_uses_signed_rate_and_keeps_multiloop_phase", (fixture, scene) =>
			{
				var renderer = Add( fixture, scene ); renderer.Sequence.Looping = true; renderer.Sequence.Time = .25f; renderer.PlaybackRate = -1;
				var duration = (double)renderer.Sequence.Duration; var expectedTime = (.25 - 9) % duration; if ( expectedTime < 0 ) expectedTime += duration;
				scene.EditorTick( 9, 9 );
				Require( renderer.Sequence.Time == (float)expectedTime, "Public reverse playback lost its full signed frame or positive loop phase." );
				var expected = fixture.At( (float)expectedTime ); Weights( renderer, expected[0], expected[1] );
			}),
			("zero_rate_editor_tick_runs_animation_stage_without_replacing_pose", (fixture, scene) =>
			{
				var renderer = Add( fixture, scene ); Seek( renderer, scene, fixture.Key( 63 ) );
				var pose = Pose( renderer ); var before = renderer.GltfInstance.PoseRevision; var time = renderer.Sequence.Time;
				var boneFrame = scene.GetSystem<SceneAnimationSystem>().BoneFrame;
				scene.EditorTick( .75f, .75f );
				Require( scene.GetSystem<SceneAnimationSystem>().BoneFrame > boneFrame, "The zero-rate control never ran the ordinary editor animation stage." );
				Require( renderer.Sequence.Time == time && renderer.GltfInstance.PoseRevision == before && ReferenceEquals( pose, Pose( renderer ) ), "Zero playback rate changed the clock or needlessly reevaluated its immutable pose." );
			})
		};
		foreach ( var compiled in new[] { false, true } ) foreach ( var test in cases )
		{
			Fixture fixture = null; Scene scene = null; string failure = null;
			try
			{
				fixture = new Fixture( checkout, form, compiled ); scene = Scene.CreateEditorScene();
				using var scope = scene.Push(); test.Test( fixture, scene );
			}
			catch ( Exception error ) { failure = error.ToString(); }
			finally
			{
				try { scene?.Destroy(); } catch ( Exception error ) { failure += "\nScene cleanup: " + error; }
				try { fixture?.Dispose(); } catch ( Exception error ) { failure += "\nFixture cleanup: " + error; }
			}
			results.Add( new Result( test.Name, compiled ? "glTF/compiled_glb" : "glTF/source", failure is null, failure ) );
		}
		return results.ToArray();
	}

	private static SkinnedModelRenderer Add( Fixture fixture, Scene scene )
	{
		var model = fixture.Load(); NativeFree( model );
		var renderer = scene.CreateObject().Components.Create<SkinnedModelRenderer>( false );
		renderer.UseAnimGraph = false; renderer.PlayAnimationsInEditorScene = true; renderer.PlaybackRate = 0; renderer.Model = model; renderer.Enabled = true;
		Require( renderer.SceneObject is SceneCustomObject && renderer.GltfInstance is not null && renderer.SceneModel is null, "The public component must use managed own-buffer presentation." );
		renderer.Sequence.Name = "Square"; Tick( scene ); return renderer;
	}
	private static void Seek( SkinnedModelRenderer renderer, Scene scene, Fixture.KeySample key ) { renderer.Sequence.Time = key.Time; Tick( scene ); }
	private static void Tick( Scene scene ) { using var scope = scene.Push(); scene.EditorTick( 0, 0 ); }
	private static GltfPose Pose( SkinnedModelRenderer renderer )
	{
		var field = typeof( SkinnedModelRenderer ).GetField( "gltfPose", BindingFlags.Instance | BindingFlags.NonPublic );
		Require( field?.GetValue( renderer ) is GltfPose, "The actual managed component pose diagnostic is unavailable." );
		var pose = (GltfPose)field.GetValue( renderer );
		var instanceField = renderer.GltfInstance.GetType().GetField( "pose", BindingFlags.Instance | BindingFlags.NonPublic );
		Require( ReferenceEquals( pose, instanceField?.GetValue( renderer.GltfInstance ) ), "The component's inspected pose was not the pose supplied to its GPU instance." );
		return pose;
	}
	private static void Weights( SkinnedModelRenderer renderer, float first, float second )
	{
		var actual = Pose( renderer ).GetMorphWeights( 0 );
		Require( actual.Length == 2, "The pose must retain exactly two logical target weights." );
		Near( first, actual[0], "Actual target 0 pose weight" ); Near( second, actual[1], "Actual target 1 pose weight" );
	}
	private static void NativeFree( Model model ) => Require( model is { IsValid: true, IsError: false } && model.native.IsNull, "Model.Load must use the real managed resource without native model/controller placeholders." );
	private static void Near( float expected, float actual, string label ) => Require( float.IsFinite( actual ) && MathF.Abs( expected - actual ) <= 2e-6f, $"{label}: expected{expected:R}, actual{actual:R}" );
	private static void Require( bool condition, string message ) { if ( !condition ) throw new InvalidOperationException( message ); }
	private static void Throws<T>( Action body ) where T : Exception { try { body(); } catch ( T ) { return; } throw new InvalidOperationException( "Expected " + typeof( T ).Name ); }

	private sealed class Fixture : IDisposable
	{
		internal sealed record KeySample( float Time, float[] Weights );
		private readonly JsonElement reference;
		private readonly string directory = Path.Combine( Path.GetTempPath(), "cube_public_morph_" + Guid.NewGuid().ToString( "N" ) );
		private readonly BaseFileSystem aggregate;
		private LocalFileSystem mount;
		private readonly string path;
		private Model model;
		internal Fixture( string checkout, JsonElement reference, bool compiled )
		{
			this.reference = reference;
			try
			{
				var source = Path.Combine( checkout, "Temp/gltf-reference/glTF-Sample-Assets", reference.GetProperty( "source" ).GetString() );
				var bytes = File.ReadAllBytes( source ); Require( Hash( bytes ) == reference.GetProperty( "sha256" ).GetString(), "Pinned cube source differs from the independent reference." );
				var parent = Path.GetDirectoryName( source );
				byte[] Resolve( string uri ) => File.ReadAllBytes( Path.Combine( parent, Uri.UnescapeDataString( uri ) ) );
				var binary = Resolve( "AnimatedMorphCube.bin" ); Require( Hash( binary ) == reference.GetProperty( "bufferSha256" )[0].GetString(), "Pinned cube binary dependency changed." );
				Directory.CreateDirectory( directory ); path = "cube_manual_" + Guid.NewGuid().ToString( "N" ) + (compiled ? ".glb_c" : ".gltf");
				File.WriteAllBytes( Path.Combine( directory, path ), compiled ? GltfCompiler.Compile( bytes, Resolve ) : bytes );
				if ( !compiled ) File.WriteAllBytes( Path.Combine( directory, "AnimatedMorphCube.bin" ), binary );
				aggregate = FileSystem.Mounted ?? throw new InvalidOperationException( "Mounted filesystem is unavailable." ); mount = new LocalFileSystem( directory ); aggregate.Mount( mount );
			}
			catch { Dispose(); throw; }
		}
		internal Model Load() => model ??= Model.Load( path );
		internal KeySample Key( int index )
		{
			var time = reference.GetProperty( "keyTimes" )[index].GetSingle();
			var sample = reference.GetProperty( "samples" ).EnumerateArray().Single( value => value.GetProperty( "time" ).GetSingle() == time );
			return new KeySample( time, sample.GetProperty( "weights" ).EnumerateArray().Select( value => value.GetSingle() ).ToArray() );
		}
		internal float[] At( float time )
		{
			// Independent source-key interpolation, never the producer evaluator.
			var keys = reference.GetProperty( "keyTimes" ); var index = 0;
			while ( index + 1 < keys.GetArrayLength() && keys[index + 1].GetSingle() <= time ) index++;
			var next = Math.Min( index + 1, keys.GetArrayLength() - 1 );
			var first = Key( index ); var second = Key( next );
			var amount = index == next ? 0 : Math.Clamp( ((double)time - first.Time) / (second.Time - first.Time), 0, 1 );
			return first.Weights.Select( (value, target) => (float)(value * (1 - amount) + second.Weights[target] * amount) ).ToArray();
		}
		public void Dispose()
		{
			var failures = new List<Exception>();
			void Clean( Action body ) { try { body(); } catch ( Exception error ) { failures.Add( error ); } }
			if ( model is not null && model.ResourcePath == Resource.FixPath( path ) ) Clean( model.Destroy );
			if ( mount is not null ) { Clean( () => aggregate.UnMount( mount ) ); Clean( mount.Dispose ); mount = null; }
			Clean( () => { if ( Directory.Exists( directory ) ) Directory.Delete( directory, true ); } );
			if ( failures.Count > 0 ) throw new AggregateException( failures );
		}
		private static string Hash( byte[] bytes ) => Convert.ToHexStringLower( SHA256.HashData( bytes ) );
	}
}
