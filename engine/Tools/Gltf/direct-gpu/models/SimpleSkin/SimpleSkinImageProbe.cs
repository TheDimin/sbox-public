using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

namespace Sandbox.Gltf.DirectGpu.Validation;

using NVector3 = System.Numerics.Vector3;

/// <summary>
/// Model-local image orchestration. The coordinator supplies the already proven
/// native capture route; this class does not create a camera, target, shader or
/// another renderer. Captures are explicitly sampled poses, never playback proof.
/// </summary>
public static class SimpleSkinImageProbe
{
	public sealed record Frame( int Width, int Height, global::Color[] Pixels, string Path, string Sha256,
		long EngineFrame, double ObservedWallclockSeconds, string CameraStateJson );
	public sealed record FrameMetadata( string Label, int Width, int Height, string Path, string Sha256,
		long EngineFrame, double ObservedWallclockSeconds, string CameraStateJson, int ForegroundPixels );
	public sealed record Comparison( int IntersectionPixels, int UnionPixels, int DifferentMaskPixels,
		double SilhouetteIoU, double MeanAbsoluteRgbError, double MaximumRgbError );
	public sealed record Row( string Form, string Case, float RequestedSourceTime, float? ObservedPlaybackTime,
		string ClockMode, Matrix4x4 Placement, FrameMetadata[] Captures, Comparison ActualVsReference,
		Comparison WrongPhaseVsReference, SimpleSkinGpuChecks.Measurement GpuMeasurement,
		int ActualDrawCalls, long RequestedRevision, long SubmittedRevision );

	public static Task<Row[]> MeasureAll( string checkoutRoot, Sandbox.Scene scene,
		Sandbox.Material borrowedComputeMaterial, Sandbox.Material borrowedDiagnosticMaterial,
		Func<string, Task<Frame>> captureNative, Func<Task> awaitCompletedFrame )
		=> MeasureCore( checkoutRoot, scene, borrowedComputeMaterial, borrowedDiagnosticMaterial, captureNative, awaitCompletedFrame, null, null );

	/// <summary>
	/// Explicit bounded selection for smoke execution. Unknown, duplicate or empty
	/// names fail before any GPU asset is created. Unselected matrix rows remain
	/// pending; filtering never changes their acceptance requirements.
	/// </summary>
	public static Task<Row[]> MeasureSelected( string checkoutRoot, Sandbox.Scene scene,
		Sandbox.Material borrowedComputeMaterial, Sandbox.Material borrowedDiagnosticMaterial,
		Func<string, Task<Frame>> captureNative, Func<Task> awaitCompletedFrame,
		string[] selectedForms, string[] selectedCases )
	{
		SimpleSkinCoreTests.Require( selectedForms is { Length: > 0 } && selectedCases is { Length: > 0 }, "A smoke selection must explicitly name forms and cases." );
		SimpleSkinCoreTests.Require( selectedForms.All( value => !string.IsNullOrWhiteSpace( value ) ) && selectedCases.All( value => !string.IsNullOrWhiteSpace( value ) )
			&& selectedForms.Distinct( StringComparer.Ordinal ).Count() == selectedForms.Length && selectedCases.Distinct( StringComparer.Ordinal ).Count() == selectedCases.Length,
			"Smoke selection names must be nonempty and unique." );
		return MeasureCore( checkoutRoot, scene, borrowedComputeMaterial, borrowedDiagnosticMaterial, captureNative, awaitCompletedFrame,
			new HashSet<string>( selectedForms, StringComparer.Ordinal ), new HashSet<string>( selectedCases, StringComparer.Ordinal ) );
	}

	private static async Task<Row[]> MeasureCore( string checkoutRoot, Sandbox.Scene scene,
		Sandbox.Material borrowedComputeMaterial, Sandbox.Material borrowedDiagnosticMaterial,
		Func<string, Task<Frame>> captureNative, Func<Task> awaitCompletedFrame,
		IReadOnlySet<string> selectedForms, IReadOnlySet<string> selectedCases )
	{
		ArgumentNullException.ThrowIfNull( scene ); ArgumentNullException.ThrowIfNull( captureNative );
		ArgumentNullException.ThrowIfNull( awaitCompletedFrame );
		var rows = new List<Row>();
		var cases = new[]
		{
			(Name: "bind", Time: 0f, Placement: Matrix4x4.Identity),
			(Name: "positive_rotation_key", Time: 1f, Placement: Matrix4x4.Identity),
			(Name: "between_rotation_keys", Time: .75f, Placement: Matrix4x4.Identity),
			(Name: "negative_rotation_key", Time: 4f, Placement: Matrix4x4.Identity),
			(Name: "affine_world_shear", Time: .75f, Placement: new Matrix4x4( 1, .25f, 0, 0, 0, 1.5f, .125f, 0, 0, 0, .75f, 0, 9, 4, -3, 1 )),
			(Name: "mirrored_y_single_sided", Time: .75f, Placement: Matrix4x4.CreateScale( 1, -1, 1 ))
		};
		var forms = SimpleSkinReference.OpenForms( checkoutRoot );
		SimpleSkinCoreTests.Require( selectedForms is null || selectedForms.All( name => forms.Any( form => form.Name == name ) ), "Smoke selection names an unknown SimpleSkin form." );
		SimpleSkinCoreTests.Require( selectedCases is null || selectedCases.All( name => cases.Any( test => test.Name == name ) ), "Smoke selection names an unknown SimpleSkin case." );
		foreach ( var form in forms.Where( form => selectedForms is null || selectedForms.Contains( form.Name ) ) )
		{
			using var document = form.Read();
			using var asset = GltfGpuAsset.Create( document, borrowedComputeMaterial, _ => borrowedDiagnosticMaterial );
			foreach ( var test in cases.Where( test => selectedCases is null || selectedCases.Contains( test.Name ) ) )
			{
				var labels = new List<FrameMetadata>(); var errors = new ConcurrentQueue<string>();
				Sandbox.SceneCustomObject reference = null; GltfGpuInstance instance = null;
				var referenceCalls = 0;
				var prefix = "SimpleSkin_" + form.Name.Replace( '/', '_' ) + "_" + test.Name + "_";
				try
				{
					var emptyBefore = await Capture( "empty_before" ); RequireEmpty( emptyBefore, "before" );
					MakeReference( test.Time );
					var expected = await Capture( "independent_reference" ); RequirePositive( expected, "reference" );
					SimpleSkinCoreTests.Require( System.Threading.Volatile.Read( ref referenceCalls ) > 0 && errors.IsEmpty, "Independent reference callback did not draw successfully." );
					RemoveReference(); var beforeDeleteCalls = System.Threading.Volatile.Read( ref referenceCalls );
					var emptyReference = await Capture( "empty_after_reference" ); RequireEmpty( emptyReference, "after reference deletion" );
					SimpleSkinCoreTests.Require( System.Threading.Volatile.Read( ref referenceCalls ) == beforeDeleteCalls, "Deleted reference callback is still executing." );

					Comparison sensitivity = null;
					if ( test.Name == "bind" )
					{
						MakeReference( 1 );
						var wrongPhase = await Capture( "deliberately_wrong_phase_reference" ); RequirePositive( wrongPhase, "wrong phase reference" );
						sensitivity = Compare( expected, wrongPhase );
						SimpleSkinCoreTests.Require( sensitivity.DifferentMaskPixels > 0, "Image oracle cannot distinguish a deliberately wrong animation phase." );
						RemoveReference(); var emptyWrong = await Capture( "empty_after_wrong_phase" ); RequireEmpty( emptyWrong, "after wrong phase deletion" );
					}

					instance = asset.CreateInstance( scene.SceneWorld ); instance.Placement = test.Placement;
					instance.SetPose( document.EvaluatePose( 0, test.Time ) );
					var wantedRevision = instance.PoseRevision;
					var actual = await Capture( "actual_own_gpu_buffers" ); RequirePositive( actual, "actual GPU geometry" );
					SimpleSkinCoreTests.Require( instance.DrawCalls > 0 && instance.SubmittedRevision == wantedRevision && !instance.CallbackErrors.Any(), "Actual output did not submit the current own-buffer pose." );
					var readback = SimpleSkinGpuChecks.Measure( instance.Readback(), test.Time, test.Placement );
					var drawCalls = instance.DrawCalls; var submittedRevision = instance.SubmittedRevision;
					instance.Dispose(); var afterDeleteCalls = instance.DrawCalls;
					using ( scene.Push() ) scene.ProcessDeletes();
					var emptyAfter = await Capture( "empty_after_actual" ); RequireEmpty( emptyAfter, "after actual GPU instance deletion" );
					SimpleSkinCoreTests.Require( instance.DrawCalls == afterDeleteCalls, "Deleted GPU callback is still submitting draws." );
					instance = null;
					rows.Add( new Row( form.Name, test.Name, test.Time, null,
						"explicit immutable GltfPose sample; no playback clock", test.Placement, labels.ToArray(),
						Compare( expected, actual ), sensitivity, readback, drawCalls, wantedRevision, submittedRevision ) );
				}
				finally
				{
					RemoveReference(); instance?.Dispose();
					using ( scene.Push() ) scene.ProcessDeletes();
					await awaitCompletedFrame();
				}

				async Task<Frame> Capture( string label )
				{
					await awaitCompletedFrame();
					var frame = await captureNative( prefix + label );
					SimpleSkinCoreTests.Require( frame is not null && frame.Width > 0 && frame.Height > 0 && frame.Pixels is not null && frame.Pixels.Length == checked(frame.Width * frame.Height), "Capture dimensions/pixel count mismatch." );
					// Own the reference pixels even if a capture adapter reuses its
					// readback array for a later frame. Metadata alone is not an oracle.
					frame = frame with { Pixels = (global::Color[])frame.Pixels.Clone() };
					SimpleSkinCoreTests.Require( frame.EngineFrame >= 0 && double.IsFinite( frame.ObservedWallclockSeconds ) && !string.IsNullOrWhiteSpace( frame.CameraStateJson ) && !string.IsNullOrWhiteSpace( frame.Path ) && frame.Sha256 is { Length: 64 }, "Capture is missing observed camera/frame/wallclock/artifact metadata." );
					labels.Add( new FrameMetadata( label, frame.Width, frame.Height, frame.Path, frame.Sha256, frame.EngineFrame, frame.ObservedWallclockSeconds, frame.CameraStateJson, Coverage( frame ) ) );
					SimpleSkinCoreTests.Require( errors.IsEmpty, "Reference callback errors: " + string.Join( "\n", errors ) );
					return frame;
				}

				void MakeReference( float sourceTime )
				{
					var vertices = SimpleSkinGpuChecks.ReferenceTriangles( sourceTime, test.Placement );
					var minimum = new NVector3( float.PositiveInfinity ); var maximum = new NVector3( float.NegativeInfinity );
					foreach ( var vertex in vertices )
					{
						var value = new NVector3( vertex.Position.x, vertex.Position.y, vertex.Position.z );
						minimum = NVector3.Min( minimum, value ); maximum = NVector3.Max( maximum, value );
					}
					minimum -= new NVector3( .001f ); maximum += new NVector3( .001f );
					reference = new Sandbox.SceneCustomObject( scene.SceneWorld ); reference.Flags.IsOpaque = true; reference.Flags.IsTranslucent = false;
					reference.Bounds = new global::BBox( new global::Vector3( minimum.X, minimum.Y, minimum.Z ), new global::Vector3( maximum.X, maximum.Y, maximum.Z ) );
					reference.RenderOverride = _ =>
					{
						try { System.Threading.Interlocked.Increment( ref referenceCalls ); Sandbox.Graphics.Draw( vertices.AsSpan(), vertices.Length, borrowedDiagnosticMaterial ); }
						catch ( Exception error ) { errors.Enqueue( error.ToString() ); }
					};
				}

				void RemoveReference()
				{
					if ( reference is null ) return;
					reference.RenderOverride = null; reference.Delete(); reference = null;
					using ( scene.Push() ) scene.ProcessDeletes();
				}
			}
		}
		return rows.ToArray();
	}

	public static Comparison Compare( Frame reference, Frame actual )
	{
		SimpleSkinCoreTests.Require( reference.Width == actual.Width && reference.Height == actual.Height && reference.CameraStateJson == actual.CameraStateJson, "Actual and reference have different observed dimensions/camera state." );
		var intersection = 0; var union = 0; var different = 0; var sum = 0d; var maximum = 0d;
		for ( var pixel = 0; pixel < reference.Pixels.Length; pixel++ )
		{
			var a = reference.Pixels[pixel]; var b = actual.Pixels[pixel];
			var aVisible = Foreground( a ); var bVisible = Foreground( b );
			if ( aVisible && bVisible ) intersection++; if ( aVisible || bVisible ) union++; if ( aVisible != bVisible ) different++;
			var red = Math.Abs( (double)a.r - b.r ); var green = Math.Abs( (double)a.g - b.g ); var blue = Math.Abs( (double)a.b - b.b );
			sum += red + green + blue; maximum = Math.Max( maximum, Math.Max( red, Math.Max( green, blue ) ) );
		}
		return new Comparison( intersection, union, different, union == 0 ? 0 : (double)intersection / union, sum / (3 * reference.Pixels.Length), maximum );
	}

	/// <summary>Call only with reviewed image and numerical thresholds; no defaults weaken an unreviewed gate.</summary>
	public static void RequireWithin( Row row, double minimumSilhouetteIoU, double maximumMeanRgbError,
		double maximumRgbError, float positionToleranceInches, float normalTolerance )
	{
		SimpleSkinCoreTests.Require( double.IsFinite( minimumSilhouetteIoU ) && minimumSilhouetteIoU > 0 && minimumSilhouetteIoU <= 1
			&& double.IsFinite( maximumMeanRgbError ) && maximumMeanRgbError >= 0
			&& double.IsFinite( maximumRgbError ) && maximumRgbError >= 0, "Invalid explicit image acceptance thresholds." );
		SimpleSkinCoreTests.Require( row.ActualVsReference.UnionPixels > 64 && row.ActualVsReference.SilhouetteIoU >= minimumSilhouetteIoU,
			$"{row.Form}/{row.Case}: silhouette IoU {row.ActualVsReference.SilhouetteIoU:R} is below {minimumSilhouetteIoU:R}." );
		SimpleSkinCoreTests.Require( row.ActualVsReference.MeanAbsoluteRgbError <= maximumMeanRgbError && row.ActualVsReference.MaximumRgbError <= maximumRgbError,
			$"{row.Form}/{row.Case}: image RGB error exceeds reviewed thresholds." );
		SimpleSkinGpuChecks.RequireWithin( row.GpuMeasurement, positionToleranceInches, normalTolerance );
	}

	private static int Coverage( Frame frame ) => frame.Pixels.Count( Foreground );
	private static bool Foreground( global::Color color )
	{
		SimpleSkinCoreTests.Require( float.IsFinite( color.r ) && float.IsFinite( color.g ) && float.IsFinite( color.b ), "Capture contains nonfinite RGB." );
		return color.r > .03f || color.g > .03f || color.b > .03f;
	}
	private static void RequireEmpty( Frame frame, string label ) => SimpleSkinCoreTests.Require( Coverage( frame ) == 0, "Native empty control contains geometry " + label + "." );
	private static void RequirePositive( Frame frame, string label ) => SimpleSkinCoreTests.Require(
		Coverage( frame ) > 64 && !frame.Pixels.Any( color => color.r > .8f && color.g < .2f && color.b > .8f ),
		"Native positive control is empty, insufficient or retains its magenta target sentinel: " + label + "." );
}
