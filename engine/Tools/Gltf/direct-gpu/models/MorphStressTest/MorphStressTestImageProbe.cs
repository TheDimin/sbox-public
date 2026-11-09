using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

namespace Sandbox.Gltf.DirectGpu.MorphStressTestValidation;

using static MorphStressTestReference;

/// <summary>16 matched native default-lit image cases. Source PBR fidelity and realtime animation remain separate gates.</summary>
internal static class MorphStressTestImageProbe
{
	public const int ExpectedCases = 16;
	public sealed record Result( string Form, string State, int Captures, MorphStressTestGpuChecks.Measurement Readback, MorphStressTestImageChecks.Measurement Image, bool Passed, string Error );
	public static async Task<Result[]> Run( string checkoutRoot, Scene scene, CameraComponent camera, Material compute, Func<Task> waitForActualFrames,
		Func<string, Task<Color[]>> capture, double minimumIoU = .99, double maximumMeanRgbError = .003, double maximumChangedFraction = .015, int minimumNegativeChangedPixels = 64 )
	{
		Require( ReferenceEquals( camera.Scene, scene ), "Coordinator camera must belong to the private capture scene." ); var r = new MorphStressTestReference( checkoutRoot ); var results = new List<Result>();
		foreach ( var form in r.Forms )
		{
			using var d = form.Read(); using var asset = GltfGpuAsset.Create( d, compute );
			var selection = new List<(string Name, int Clip, float Time)>();
			for ( var clip = 0; clip < 3; clip++ )
			{
				var c = r.Oracle.GetProperty( "clips" )[clip]; var times = Numbers( c.GetProperty( "times" ) ); var keys = c.GetProperty( "weights" );
				var peak = Enumerable.Range( 0, times.Length ).OrderByDescending( i => Numbers( keys[i] ).Sum() ).First(); selection.Add( ($"clip{clip}_peak_key{peak}", clip, (float)times[peak]) );
			}
			var individual = r.Oracle.GetProperty( "clips" )[0]; var t = Numbers( individual.GetProperty( "times" ) ); var last = Enumerable.Range( 0, t.Length ).OrderByDescending( i => individual.GetProperty( "weights" )[i][7].GetDouble() ).First(); selection.Add( ("eighth_target_peak", 0, (float)t[last]) );
			foreach ( var state in selection )
			{
				GltfGpuInstance actualInstance = null; SceneDirectionalLight light = null; MorphStressTestGpuChecks.Measurement gpu = null; MorphStressTestImageChecks.Measurement image = null; var captures = 0; string failure = null;
				var label = form.Name.Replace( '/', '_' ) + "_" + state.Name; var weights = r.Weights( state.Clip, state.Time );
				try
				{
					await MainThread.Wait(); ConfigureCamera( camera, r ); light = new SceneDirectionalLight( scene.SceneWorld, camera.WorldRotation, Color.White * 2 );
					Require( MorphStressTestImageChecks.IsEmpty( await Take( "empty_before" ) ), "Fresh empty control failed." );
					actualInstance = asset.CreateInstance( scene.SceneWorld ); actualInstance.SetPose( d.EvaluatePose( state.Clip, state.Time ) ); var actual = await Take( "actual_gpu" );
					Require( actualInstance.DrawCalls > 0 && actualInstance.SubmittedRevision == actualInstance.PoseRevision && actualInstance.CallbackErrors.Length == 0, "Actual current-pose GPU draw did not complete." );
					gpu = MorphStressTestGpuChecks.Measure( actualInstance.Readback(), r, weights, Matrix4x4.Identity ); MorphStressTestGpuChecks.RequireWithin( gpu, .0005, 2e-6, 2e-6 );
					var slots = asset.MaterialSlotSourceIndices.ToArray(); var materials = asset.GetMaterials(); Require( slots.Length == materials.Count, "Source material slot mapping lost." );
					var map = slots.Select( (source, slot) => (Source: source ?? -1, Material: materials[slot]) ).ToDictionary( x => x.Source, x => x.Material ); Material ResolveMaterial( int? index ) => map[index ?? -1];
					var draws = actualInstance.DrawCalls; actualInstance.Dispose(); await waitForActualFrames(); Deletes(); Require( actualInstance.DrawCalls == draws, "Deleted instance kept drawing." ); Require( MorphStressTestImageChecks.IsEmpty( await Take( "empty_after_actual" ) ), "Deleted actual geometry remains visible." );
					Color[] reference, negative;
					using ( var expected = new MorphStressTestReferenceScene( r, scene.SceneWorld, weights, Matrix4x4.Identity, ResolveMaterial ) ) { reference = await Take( "independent_reference" ); Require( expected.DrawCalls > 0 && expected.CallbackErrors.Length == 0, "Independent expected draw failed." ); }
					await waitForActualFrames(); Deletes(); Require( MorphStressTestImageChecks.IsEmpty( await Take( "empty_after_reference" ) ), "Deleted reference remains visible." );
					using ( var wrong = new MorphStressTestReferenceScene( r, scene.SceneWorld, new double[8], Matrix4x4.Identity, ResolveMaterial ) ) { negative = await Take( "unmorphed_negative" ); Require( wrong.DrawCalls > 0 && wrong.CallbackErrors.Length == 0, "Unmorphed negative draw failed." ); }
					await waitForActualFrames(); Deletes(); Require( MorphStressTestImageChecks.IsEmpty( await Take( "empty_after_negative" ) ), "Deleted negative remains visible." );
					image = MorphStressTestImageChecks.Compare( actual, reference, negative ); MorphStressTestImageChecks.RequireWithin( image, minimumIoU, maximumMeanRgbError, maximumChangedFraction, minimumNegativeChangedPixels ); Require( captures == 7, "Image capture inventory changed." );
				}
				catch ( Exception e ) { failure = e.ToString(); }
				finally { await MainThread.Wait(); actualInstance?.Dispose(); if ( light.IsValid() ) light.Delete(); Deletes(); }
				results.Add( new( form.Name, state.Name, captures, gpu, image, failure is null, failure ) );
				async Task<Color[]> Take( string suffix ) { captures++; return await capture( label + "_" + suffix ); }
				void Deletes() { using var scope = scene.Push(); scene.ProcessDeletes(); }
			}
		}
		Require( results.Count == ExpectedCases, "Frozen image inventory changed." ); return results.ToArray();
	}
	public static void ConfigureCamera( CameraComponent camera, MorphStressTestReference r )
	{
		// Independent interval bounds cover every combination of actual [0,1]
		// weights, even when different target deltas have opposite component signs.
		var points = new List<double[]>();
		for ( var p = 0; p < 2; p++ ) for ( var v = 0; v < r.VertexCount( p ); v++ )
		{
			var primitive = r.Primitive( p ); var source = r.AccessorValues( primitive.GetProperty( "attributes" ).GetProperty( "POSITION" ).GetInt32() )[v]; var lo = (double[])source.Clone(); var hi = (double[])source.Clone();
			foreach ( var target in primitive.GetProperty( "targets" ).EnumerateArray() )
			{
				var delta = r.AccessorValues( target.GetProperty( "POSITION" ).GetInt32() )[v]; for ( var c = 0; c < 3; c++ ) { lo[c] += Math.Min( 0, delta[c] ); hi[c] += Math.Max( 0, delta[c] ); }
			}
			points.Add( new[] { lo[2] / .0254, lo[0] / .0254, lo[1] / .0254 } ); points.Add( new[] { hi[2] / .0254, hi[0] / .0254, hi[1] / .0254 } );
		}
		var min = Enumerable.Range( 0, 3 ).Select( c => points.Min( p => p[c] ) ).ToArray(); var max = Enumerable.Range( 0, 3 ).Select( c => points.Max( p => p[c] ) ).ToArray();
		var center = new global::Vector3( (float)((min[0] + max[0]) / 2), (float)((min[1] + max[1]) / 2), (float)((min[2] + max[2]) / 2) ); var span = (float)Enumerable.Range( 0, 3 ).Max( c => max[c] - min[c] );
		camera.WorldPosition = center + new global::Vector3( 1, .65f, 1 ).Normal * (span * 3 + 100); camera.WorldRotation = Rotation.LookAt( center - camera.WorldPosition, global::Vector3.Up );
		camera.Orthographic = true; camera.OrthographicHeight = span * 1.6f; camera.ZNear = 1; camera.ZFar = span * 8 + 500; camera.BackgroundColor = Color.Black; camera.ClearFlags = ClearFlags.All;
		camera.CustomSize = new Vector2( 512, 512 ); camera.EnablePostProcessing = true; camera.DebugMode = SceneCameraDebugMode.Normal;
	}
}
