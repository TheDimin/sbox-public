using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

namespace Sandbox.Gltf.DirectGpu.NegativeScaleValidation;
using static NegativeScaleReference;

/// <summary>12 source-material geometry/normal/culling cases. Stock material adaptation remains a separate fidelity gate.</summary>
public static class NegativeScaleImageProbe
{
	public const int ExpectedCases = 12;
	public sealed record Result( string Form, string Placement, int Captures, NegativeScaleGpuChecks.Measurement Readback, NegativeScaleImageChecks.Measurement Image, bool Passed, string Error );
	public static async Task<Result[]> Run( string checkoutRoot, Scene scene, CameraComponent camera, Material compute, Func<Task> waitForActualFrames, Func<string,Task<Color[]>> capture )
	{
		Require( ReferenceEquals( camera.Scene,scene ),"Coordinator camera must belong to the private scene." ); var reference = new NegativeScaleReference( checkoutRoot ); var results = new List<Result>();
		foreach ( var form in reference.Forms.Where( f => !f.Fixture ) )
		{
			using var document = form.Read(); using var asset = GltfGpuAsset.Create( document,compute );
			foreach ( var placement in new[] { ("identity",Matrix4x4.Identity),("full_affine",AffinePlacement),("mirrored_affine",MirroredPlacement) } )
			{
				GltfGpuInstance instance = null; SceneDirectionalLight light = null; var captures = 0; NegativeScaleGpuChecks.Measurement readback = null; NegativeScaleImageChecks.Measurement image = null; string failure = null;
				var label = form.Name.Replace( '/','_' ) + "_" + placement.Item1;
				try
				{
					await MainThread.Wait(); ConfigureCamera( camera,form.Expected(),placement.Item2 ); light = new SceneDirectionalLight( scene.SceneWorld,camera.WorldRotation,Color.White * 2 );
					Require( NegativeScaleImageChecks.IsEmpty( await Take( "empty_before" ) ),"Fresh native empty control failed." );
					instance = asset.CreateInstance( scene.SceneWorld ); instance.Placement = placement.Item2; var actual = await Take( "actual_gpu_buffers" );
					Require( instance.DrawCalls > 0 && instance.SubmittedRevision == instance.PoseRevision && instance.CallbackErrors.Length == 0,"Actual GPU draw did not complete." ); readback = NegativeScaleGpuChecks.Assert( form.Expected(),instance.Readback(),placement.Item2 );
					var sourceSlots = asset.MaterialSlotSourceIndices.ToArray(); var materials = asset.GetMaterials(); Require( sourceSlots.Length == materials.Count,"Borrowed source material slot mismatch." );
					var map = sourceSlots.Select( (source,slot) => (source,Material: materials[slot]) ).ToDictionary( r => r.source ?? -1,r => r.Material ); Material Resolve( int? index ) => map[index ?? -1];
					var calls = instance.DrawCalls; instance.Dispose(); await waitForActualFrames(); Deletes(); Require( instance.DrawCalls == calls,"Deleted GPU instance continued drawing." );
					Require( NegativeScaleImageChecks.IsEmpty( await Take( "empty_after_actual" ) ),"Deleted actual geometry remains visible." );
					Color[] literal,negative;
					using ( var r = new NegativeScaleReferenceScene( scene.SceneWorld,form.Expected(),placement.Item2,Resolve ) ) { literal = await Take( "independent_literal_reference" ); Require( r.DrawCalls > 0 && r.CallbackErrors.Length == 0,"Literal reference failed to draw." ); }
					await waitForActualFrames(); Deletes(); Require( NegativeScaleImageChecks.IsEmpty( await Take( "empty_after_reference" ) ),"Deleted literal reference remains visible." );
					using ( var r = new NegativeScaleReferenceScene( scene.SceneWorld,form.Expected(),placement.Item2,Resolve,true ) ) { negative = await Take( "wrong_single_sided_winding_control" ); Require( r.DrawCalls > 0 && r.CallbackErrors.Length == 0,"Wrong-winding control failed to draw." ); }
					await waitForActualFrames(); Deletes(); Require( NegativeScaleImageChecks.IsEmpty( await Take( "empty_after_negative" ) ),"Deleted wrong-winding control remains visible." );
					image = NegativeScaleImageChecks.Compare( actual,literal,negative ); Require( image.Passed,"Independent geometry/normal/front-face image comparison failed." ); Require( captures == 7,"Image capture inventory changed." );
				}
				catch ( Exception e ) { failure = e.ToString(); }
				finally { await MainThread.Wait(); instance?.Dispose(); if ( light.IsValid() ) light.Delete(); Deletes(); }
				results.Add( new( form.Name,placement.Item1,captures,readback,image,failure is null,failure ) );
				async Task<Color[]> Take( string suffix ) { captures++; return await capture( label + "_" + suffix ); }
				void Deletes() { using var scope = scene.Push(); scene.ProcessDeletes(); }
			}
		}
		Require( results.Count == ExpectedCases,"Frozen source-material image inventory changed." ); return results.ToArray();
	}
	public static void ConfigureCamera( CameraComponent camera, System.Text.Json.JsonElement expected, Matrix4x4 placement )
	{
		// Independent all-vertex oracle bounds, never producer-computed bounds.
		var points = expected.GetProperty( "rows" ).EnumerateArray().SelectMany( r => Enumerable.Range( 0,r.GetProperty( "positions" ).GetArrayLength() ).Select( v => Position( r,v,placement ) ) ).ToArray();
		var min = Enumerable.Range( 0,3 ).Select( c => points.Min( p => p[c] ) ).ToArray(); var max = Enumerable.Range( 0,3 ).Select( c => points.Max( p => p[c] ) ).ToArray();
		var center = new global::Vector3( (float)((min[0] + max[0]) / 2),(float)((min[1] + max[1]) / 2),(float)((min[2] + max[2]) / 2) );
		camera.WorldPosition = center + global::Vector3.Forward * 1500; camera.WorldRotation = Rotation.LookAt( center - camera.WorldPosition,global::Vector3.Up ); camera.Orthographic = true;
		camera.OrthographicHeight = (float)Math.Max( max[1] - min[1],max[2] - min[2] ) * 1.2f; camera.ZNear = 1; camera.ZFar = 5000; camera.BackgroundColor = Color.Black; camera.ClearFlags = ClearFlags.All; camera.CustomSize = new Vector2( 384,384 ); camera.EnablePostProcessing = true; camera.DebugMode = SceneCameraDebugMode.Normal;
	}
}
