using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NMatrix = System.Numerics.Matrix4x4;
using NativeFrame = Sandbox.Gltf.DirectGpu.GltfGpuTriangleGateTests.NativeCapture.Frame;

namespace Sandbox.Gltf.DirectGpu.Validation;

/// <summary>
/// Two public sequence poses, with the existing source mount, literal SimpleSkin
/// oracle, actual GPU checker and coordinator-owned native capture framework.
/// Deterministic seeks prove public animation presentation, not wall-clock rate.
/// </summary>
internal static class SimpleSkinPublicCapture
{
	public sealed record ImageResult( int Intersection, int Union, double IoU, double MeanRgbError, double MaximumRgbError );
	public sealed record FrameResult( string Label, string Path, string Sha256, int Coverage, ulong EngineFrame, double WallSeconds, string CameraState );
	public sealed record PoseResult( float RequestedTime, float ObservedTime, long RequestedRevision, long SubmittedRevision,
		int DrawCalls, SimpleSkinGpuChecks.Measurement Gpu, ImageResult Image );
	public sealed record Result( string Form, string PublicResourcePath, PoseResult[] Poses, FrameResult[] Captures,
		int ActualChangedPixels, int ReferenceChangedPixels, bool Passed, string Scope, string Error );

	/// <summary>
	/// Uses NativeCapture.Scene/Camera/WaitFrames/Take supplied by the coordinator.
	/// All numerical/image bounds are explicit acceptance inputs, not hidden defaults.
	/// </summary>
	public static async Task<Result> Run( string checkoutRoot, Scene scene, CameraComponent camera,
		Func<Task> waitFrames, Func<string, Task<NativeFrame>> capture,
		float positionToleranceInches, float normalTolerance, double minimumIoU, double maximumMeanRgbError, double maximumRgbError, bool compiled = false )
	{
		ArgumentNullException.ThrowIfNull( scene ); ArgumentNullException.ThrowIfNull( camera );
		ArgumentNullException.ThrowIfNull( waitFrames ); ArgumentNullException.ThrowIfNull( capture );
		Require( ReferenceEquals( scene, camera.Scene ), "Use the coordinator's existing private capture scene/camera." );
		Require( double.IsFinite( minimumIoU ) && minimumIoU > 0 && minimumIoU <= 1 && double.IsFinite( maximumMeanRgbError ) && maximumMeanRgbError >= 0 && double.IsFinite( maximumRgbError ) && maximumRgbError >= 0, "Image acceptance bounds must be explicit and finite." );
		var poses = new List<PoseResult>(); var frames = new List<FrameResult>();
		var referenceErrors = new ConcurrentQueue<string>();
		SimpleSkinPublicTests.Fixture fixture = null;
		SkinnedModelRenderer renderer = null;
		SceneCustomObject literal = null;
		var referenceCalls = 0; var actualChanged = 0; var referenceChanged = 0;
		string resourcePath = null, failure = null;
		try
		{
			await MainThread.Wait();
			Require( Application.IsEditor && !Game.IsPlaying && !Graphics.IsActive, "Requires idle editor main thread outside rendering." );
			// Same measured native view as the historical independent SimpleSkin
			// smoke, now reached through the real public resource/component APIs.
			camera.WorldPosition = new Vector3( 200, 0, 40 );
			camera.WorldRotation = Rotation.LookAt( new Vector3( 0, 0, 40 ) - camera.WorldPosition, Vector3.Up );
			camera.Orthographic = true; camera.OrthographicHeight = 120;
			camera.ZNear = 1; camera.ZFar = 1000;
			camera.BackgroundColor = Color.Black; camera.ClearFlags = ClearFlags.All;
			camera.EnablePostProcessing = true; camera.DebugMode = SceneCameraDebugMode.Normal;
			fixture = new SimpleSkinPublicTests.Fixture( checkoutRoot, "glTF", compiled );
			var model = fixture.Load(); // Existing fixture calls ordinary public Model.Load.
			resourcePath = fixture.ResourcePath;
			Require( model is { IsValid: true, IsError: false } && model.native.IsNull, "Public source load must produce a real managed resource without a native Model." );
			var materials = model.Materials;
			Require( materials.Length == 1 && materials[0] is { IsValid: true, IsError: false }, "SimpleSkin's absent material must resolve through the ordinary default source-material path." );
			RequireEmpty( await Take( "empty_before" ) );
			var references = new NativeFrame[2];
			for ( var index = 0; index < 2; index++ )
			{
				var vertices = SimpleSkinGpuChecks.ReferenceTriangles( index, NMatrix.Identity );
				literal = new SceneCustomObject( scene.SceneWorld );
				literal.Flags.IsOpaque = true; literal.Flags.IsTranslucent = false;
				literal.Bounds = BBox.FromPoints( vertices.Select( vertex => vertex.Position ) ).Grow( .001f );
				var callsBefore = referenceCalls;
				literal.RenderOverride = _ =>
				{
					try { System.Threading.Interlocked.Increment( ref referenceCalls ); Graphics.Draw( vertices.AsSpan(), vertices.Length, materials[0] ); }
					catch ( Exception error ) { referenceErrors.Enqueue( error.ToString() ); }
				};
				references[index] = await Take( "independent_reference_" + index );
				RequirePositive( references[index] );
				Require( referenceCalls > callsBefore && referenceErrors.IsEmpty, "Independent literal geometry did not draw successfully." );
				RemoveReference();
				var deletedCalls = referenceCalls;
				RequireEmpty( await Take( "empty_after_reference_" + index ) );
				Require( referenceCalls == deletedCalls, "Deleted literal callback still runs." );
			}
			referenceChanged = Changed( references[0], references[1] );
			Require( referenceChanged > 0, "Independent images must distinguish the two authored phases." );
			using ( scene.Push() )
			{
				renderer = scene.CreateObject().Components.Create<SkinnedModelRenderer>( false );
				renderer.UseAnimGraph = false; renderer.PlayAnimationsInEditorScene = true;
				renderer.PlaybackRate = 0; renderer.Model = model;
				renderer.Sequence.Name = "animation_0"; renderer.Enabled = true;
			}
			var instance = renderer.GltfInstance;
			Require( instance is not null && renderer.SceneModel is null && renderer.SceneObject is SceneCustomObject && renderer.SceneObject.Model is null, "The actual public skinned renderer must own the native-free custom draw path." );
			Require( scene.SceneWorld.SceneObjects.Count( row => row.IsValid() && ReferenceEquals( row.Component, renderer ) && row is SceneCustomObject ) == 1, "The component must own exactly one primary custom object." );
			Require( instance.Placement == NMatrix.Identity && renderer.SceneObject.Transform == Transform.Zero, "Placement must be applied once." );
			var actual = new NativeFrame[2];
			for ( var index = 0; index < 2; index++ )
			{
				renderer.Sequence.Time = index; // The sole actual-pose control is the public sequence API.
				using ( scene.Push() ) scene.EditorTick( 0, 0 );
				var requestedRevision = instance.PoseRevision;
				actual[index] = await Take( "actual_public_skinned_sequence_" + index );
				RequirePositive( actual[index] );
				Require( instance.SubmittedRevision == requestedRevision && instance.DrawCalls > 0 && !instance.CallbackErrors.Any(), "Actual public pose has not completed its own-buffer GPU submission." );
				var gpu = SimpleSkinGpuChecks.Measure( instance.Readback(), index, NMatrix.Identity );
				SimpleSkinGpuChecks.RequireWithin( gpu, positionToleranceInches, normalTolerance );
				var comparison = Compare( references[index], actual[index] );
				Require( comparison.Union > 64 && comparison.IoU >= minimumIoU && comparison.MeanRgbError <= maximumMeanRgbError && comparison.MaximumRgbError <= maximumRgbError, "Public pose image differs from independent geometry beyond the explicit limits." );
				Require( renderer.Sequence.Time == index, "The paused public sequence moved during capture." );
				poses.Add( new( index, renderer.Sequence.Time, requestedRevision, instance.SubmittedRevision, instance.DrawCalls, gpu, comparison ) );
			}
			actualChanged = Changed( actual[0], actual[1] );
			Require( actualChanged > 0, "The actual public skinned model stayed visually static." );
			DestroyRenderer();
			var finalCalls = instance.DrawCalls;
			RequireEmpty( await Take( "empty_after_public_renderer" ) );
			Require( instance.DrawCalls == finalCalls, "Destroyed public renderer still submits draws." );
			Require( model.IsValid && ReferenceEquals( model, fixture.Load() ), "Removing the component must preserve its cached resource." );
			Require( frames.Count == 8 && poses.Count == 2, "Public capture inventory changed." );
		}
		catch ( Exception error ) { failure = error.ToString(); }
		finally
		{
			await MainThread.Wait();
			Clean( RemoveReference, "reference" ); Clean( DestroyRenderer, "renderer" ); Clean( () => fixture?.Dispose(), "mount" );
		}
		return new( compiled ? "glTF/compiled_glb" : "glTF/source", resourcePath, poses.ToArray(), frames.ToArray(), actualChanged, referenceChanged, failure is null,
			"Public Model.Load + SkinnedModelRenderer deterministic seconds seeks; actual GPU readbacks and native captures against independent literal geometry. Shared source material isolates geometry/binding, not independent material fidelity. No wall-clock playback or performance claim.", failure );

		async Task<NativeFrame> Take( string label )
		{
			await waitFrames(); await MainThread.Wait();
			var frame = await capture( "SimpleSkin_public_" + (compiled ? "compiled_" : "source_") + label );
			await MainThread.Wait();
			Require( frame is not null && frame.Width > 0 && frame.Height > 0 && frame.Pixels.Length == frame.Width * frame.Height && frame.Sha256?.Length == 64 && !string.IsNullOrEmpty( frame.Path ) && !string.IsNullOrEmpty( frame.CameraStateJson ), "Native capture lacks pixels or provenance." );
			frame = frame with { Pixels = (Color[])frame.Pixels.Clone() };
			frames.Add( new( label, frame.Path, frame.Sha256, frame.Coverage, frame.EngineFrame, frame.ObservedWallclockSeconds, frame.CameraStateJson ) );
			return frame;
		}
		void RemoveReference() { if ( literal is null ) return; literal.RenderOverride = null; literal.Delete(); literal = null; using var scope = scene.Push(); scene.ProcessDeletes(); }
		void DestroyRenderer() { if ( renderer is null ) return; using var scope = scene.Push(); renderer.GameObject.Destroy(); scene.EditorTick( 0, 0 ); scene.ProcessDeletes(); renderer = null; }
		void Clean( Action cleanup, string label ) { try { cleanup(); } catch ( Exception error ) { failure += "\n" + label + " cleanup: " + error; } }
	}

	private static ImageResult Compare( NativeFrame expected, NativeFrame actual )
	{
		Require( expected.Width == actual.Width && expected.Height == actual.Height && expected.CameraStateJson == actual.CameraStateJson, "Capture camera/dimensions changed." );
		var intersection = 0; var union = 0; var sum = 0d; var maximum = 0d;
		for ( var i = 0; i < expected.Pixels.Length; i++ )
		{
			var a = expected.Pixels[i]; var b = actual.Pixels[i];
			var x = GltfGpuTriangleGateTests.NativeCapture.Foreground( a ); var y = GltfGpuTriangleGateTests.NativeCapture.Foreground( b );
			if ( x && y ) intersection++; if ( x || y ) union++;
			foreach ( var difference in new[] { Math.Abs( (double)a.r - b.r ), Math.Abs( (double)a.g - b.g ), Math.Abs( (double)a.b - b.b ) } )
			{
				Require( double.IsFinite( difference ), "Capture contains nonfinite color." ); sum += difference; maximum = Math.Max( maximum, difference );
			}
		}
		return new( intersection, union, union == 0 ? 0 : (double)intersection / union, sum / (expected.Pixels.Length * 3), maximum );
	}
	private static int Changed( NativeFrame a, NativeFrame b ) => a.Pixels.Zip( b.Pixels, (x, y) => x.r != y.r || x.g != y.g || x.b != y.b ).Count( changed => changed );
	private static void RequireEmpty( NativeFrame frame ) => Require( frame.Coverage == 0, "Fresh empty/deleted control contains visible geometry." );
	private static void RequirePositive( NativeFrame frame ) => Require( frame.Coverage > 64 && !frame.Pixels.Any( pixel => pixel.r > .8f && pixel.g < .2f && pixel.b > .8f ), "Positive geometry is missing or contains the magenta target sentinel." );
	private static void Require( bool condition, string message ) { if ( !condition ) throw new InvalidOperationException( message ); }
}
