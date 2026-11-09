using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Sandbox.Gltf.DirectGpu.MeshoptCubeValidation;

using static MeshoptCubeReference;

/// <summary>Only the coordinator supplies actual rendered frames and invokes this driver.</summary>
public static class MeshoptCubeGpuProbe
{
	public static async Task<IReadOnlyList<object>> Run( string checkoutRoot, string formName, SceneWorld world, Material computeMaterial,
		Func<Task> waitForActualFrames, Func<int?, Material> materialResolver = null )
	{
		var reference = new MeshoptCubeReference( checkoutRoot ); var form = reference.Forms.Single( x => x.Name == formName );
		using var document = form.Read(); using var asset = GltfGpuAsset.Create( document, computeMaterial, materialResolver );
		using var a = asset.CreateInstance( world ); using var b = asset.CreateInstance( world );
		b.Placement = System.Numerics.Matrix4x4.CreateTranslation( 3, -5, 7 ); b.SetPose( document.EvaluatePose( 0, 1 ) );
		await waitForActualFrames(); Submitted( a ); Submitted( b );
		var results = new List<object>();
		results.Add( new { name = "authored_rest", form = form.Name, comparison = MeshoptCubeGpuChecks.AssertReadback( reference, a.Readback(), null ) } );
		results.Add( new { name = "independent_instance_B", form = form.Name, comparison = MeshoptCubeGpuChecks.AssertReadback( reference, b.Readback(), 1, 3, -5, 7 ) } );
		var frozenB = b.Readback().OrderBy( row => row.Node ).Select( row => (byte[])row.Bytes.Clone() ).ToArray();
		foreach ( var time in PoseTimes )
		{
			a.SetPose( document.EvaluatePose( 0, time ) ); var previous = a.DrawCalls; await waitForActualFrames(); Submitted( a ); Submitted( b ); Require( a.DrawCalls > previous, "Pose did not submit a fresh draw." );
			var other = b.Readback().OrderBy( row => row.Node ).ToArray(); Require( other.Length == frozenB.Length && other.Select( (row, index) => row.Bytes.SequenceEqual( frozenB[index] ) ).All( x => x ), "A animation mutated B GPU output." );
			results.Add( new { name = "rotation_key_or_midpoint", sourceTime = time, form = form.Name, a.PoseRevision, a.SubmittedRevision, a.DrawCalls, comparison = MeshoptCubeGpuChecks.AssertReadback( reference, a.Readback(), time ) } );
		}
		b.Dispose(); a.SetPose( document.EvaluatePose( 0, .375f ) ); await waitForActualFrames(); Submitted( a );
		results.Add( new { name = "survives_other_instance_disposal", form = form.Name, comparison = MeshoptCubeGpuChecks.AssertReadback( reference, a.Readback(), .375f ) } );
		results.Add( new { name = "source_material_gate", form = form.Name,
			requestedMaterials = materialResolver is null ? "stock source materials: ten distinct textured MASK labels plus default material and per-vertex cube colors" : "explicit caller override: diagnostic material; cannot prove source material fidelity",
			geometryReadbackProven = true, materialImageAcceptance = "pending independent expected material images with matched camera and fresh empty/positive/deleted-empty controls",
			wallClockPlaybackAcceptance = "pending; deterministic seeks are not playback cadence evidence" } );
		return results.AsReadOnly();
	}
	private static void Submitted( GltfGpuInstance instance ) => Require( instance.DrawCalls > 0 && instance.PoseRevision == instance.SubmittedRevision && instance.CallbackErrors.Length == 0, "No successful current GPU submission: " + string.Join( "\n", instance.CallbackErrors ) );
}
