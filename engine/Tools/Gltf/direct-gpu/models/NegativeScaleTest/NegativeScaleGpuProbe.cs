using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

namespace Sandbox.Gltf.DirectGpu.NegativeScaleValidation;
using static NegativeScaleReference;

/// <summary>Coordinator supplies real rendered frames. No launch/build/shader operation or native Model/Mesh.</summary>
public static class NegativeScaleGpuProbe
{
	public const int ExpectedMeasurements = 60;
	public sealed record Result( string Form, string Case, int Key, NegativeScaleGpuChecks.Measurement Readback, long RequestedRevision, long SubmittedRevision, int DrawCalls, string MaterialScope );
	public static async Task<IReadOnlyList<Result>> Run( string checkoutRoot, SceneWorld world, Material compute, Func<Task> waitForActualFrames, Func<int?,Material> resolver = null )
	{
		var reference = new NegativeScaleReference( checkoutRoot ); var results = new List<Result>();
		foreach ( var form in reference.Forms )
		{
			using var document = form.Read(); using var asset = GltfGpuAsset.Create( document,compute,resolver ); using var a = asset.CreateInstance( world );
			if ( !form.Fixture )
			{
				using var b = asset.CreateInstance( world ); b.Placement = AffinePlacement;
				await waitForActualFrames(); var frozenB = b.Readback().Select( r => r.Bytes.ToArray() ).ToArray();
				foreach ( var placement in new[] { ("identity",Matrix4x4.Identity),("full_affine",AffinePlacement),("mirrored_affine",MirroredPlacement),("tiny_uniform_mirrored_placement",TinyUniformMirroredPlacement),("rank2_zero_placement",Matrix4x4.CreateScale( 0,2,.75f )),("rank0_zero_placement",Matrix4x4.CreateScale( 0,0,0 )),("recovered_identity",Matrix4x4.Identity) } )
				{
					a.Placement = placement.Item2; await waitForActualFrames(); Record( a,placement.Item1,0 );
				}
				Require( b.Readback().Select( r => r.Bytes ).Zip( frozenB,(x,y) => x.SequenceEqual( y ) ).All( same => same ),"A negative/zero/recovery update corrupted independent instance B." ); Record( b,"B_unchanged_after_A_zero_recovery",0 );
				var bCalls = b.DrawCalls; b.Dispose(); await waitForActualFrames(); Require( b.DrawCalls == bCalls,"Disposed B continued drawing." );
			}
			else
			{
				foreach ( var placement in new[] { Matrix4x4.Identity,MirroredPlacement } )
				{
					a.Placement = placement;
					foreach ( var key in new[] { 0,1,2,3 } ) { a.SetPose( document.EvaluatePose( 0,key ) ); await waitForActualFrames(); Record( a,placement == Matrix4x4.Identity ? "authored_affine_scale_key" : "authored_affine_scale_key_mirrored_placement",key ); }
				}
				foreach ( var placement in new[] { ("tiny_uniform_source_placement_sign_cancellation",TinyUniformMirroredPlacement),("tiny_anisotropic_source_negative_placement_positive",TinyAnisotropicPlacement),("tiny_anisotropic_source_negative_placement_negative",TinyAnisotropicMirroredPlacement) } )
				{
					a.Placement = placement.Item2;
					// Source negative determinant must combine with placement sign before
					// choosing both bound winding and transformed normal/tangent handedness.
					foreach ( var key in new[] { 0,3 } ) { a.SetPose( document.EvaluatePose( 0,key ) ); await waitForActualFrames(); Record( a,placement.Item1,key ); }
				}
			}
			void Record( GltfGpuInstance instance, string name, int key )
			{
				Require( instance.DrawCalls > 0 && instance.SubmittedRevision == instance.PoseRevision && instance.CallbackErrors.Length == 0,"Current GPU revision failed or did not actually draw: " + string.Join( "\n",instance.CallbackErrors ) );
				results.Add( new( form.Name,name,key,NegativeScaleGpuChecks.Assert( form.Expected( key ),instance.Readback(),instance.Placement ),instance.PoseRevision,instance.SubmittedRevision,instance.DrawCalls,resolver is null ? "source stock adapter; pixel fidelity separate" : "caller material override; diagnostics must be identified" ) );
			}
		}
		Require( results.Count == ExpectedMeasurements,"Frozen GPU readback case inventory changed." ); return results;
	}
}
