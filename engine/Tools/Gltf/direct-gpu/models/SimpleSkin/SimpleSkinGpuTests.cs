using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

namespace Sandbox.Gltf.DirectGpu.Validation;

/// <summary>
/// Coordinator-driven tests: awaitCompletedFrame must wait for a real native
/// callback submission in a visible private validation scene and return on the
/// engine main thread. It must not evaluate a pose or advance animation itself.
/// A returned measurement is pending numerical review, not an implicit pass.
/// </summary>
public static class SimpleSkinGpuTests
{
	public sealed record Row( string Name, string Form, long RequestedRevision, long SubmittedRevision, int DrawCalls,
		SimpleSkinGpuChecks.Measurement Measurement );

	public static async Task<Row[]> MeasureAll( string checkoutRoot, Sandbox.SceneWorld sceneWorld, Sandbox.Material computeMaterial,
		Func<Task> awaitCompletedFrame, Func<int?, Sandbox.Material> materialResolver )
	{
		ArgumentNullException.ThrowIfNull( awaitCompletedFrame );
		ArgumentNullException.ThrowIfNull( materialResolver );
		var rows = new List<Row>();
		foreach ( var form in SimpleSkinReference.OpenForms( checkoutRoot ) )
		{
			using var document = form.Read();
			using var asset = GltfGpuAsset.Create( document, computeMaterial, materialResolver );
			using var a = asset.CreateInstance( sceneWorld );
			using var b = asset.CreateInstance( sceneWorld );
			// Different placements make accidental shared instance output visible.
			b.Placement = Matrix4x4.CreateTranslation( 0, 140, 0 );
			b.SetPose( document.EvaluatePose( 0, 3.75f ) );
			foreach ( var time in SimpleSkinReference.SampleTimes )
			{
				a.SetPose( document.EvaluatePose( 0, time ) );
				var revision = a.PoseRevision;
				await awaitCompletedFrame();
				Add( "explicit_pose_gpu_positions_and_generated_normals", form.Name, a, time, Matrix4x4.Identity, revision );
				Add( "independent_instance_retains_its_phase_and_output", form.Name, b, 3.75f, b.Placement, b.PoseRevision );
			}

			// Placement is a complete affine matrix. A TRS round-trip would lose
			// this valid composed world shear and fail the independent positions.
			var affine = new Matrix4x4( 1, .25f, 0, 0, 0, 1.5f, .125f, 0, 0, 0, .75f, 0, 9, 4, -3, 1 );
			a.Placement = affine;
			a.SetPose( document.EvaluatePose( 0, .75f ) );
			await awaitCompletedFrame();
			Add( "full_affine_placement_preserves_world_shear", form.Name, a, .75f, affine, a.PoseRevision );
			Add( "affine_change_does_not_mutate_other_instance", form.Name, b, 3.75f, b.Placement, b.PoseRevision );
		}
		return rows.ToArray();

		void Add( string name, string formName, GltfGpuInstance instance, float time, Matrix4x4 placement, long requestedRevision )
		{
			SimpleSkinCoreTests.Require( instance.SubmittedRevision == requestedRevision, "Readback would observe a stale or unsubmitted pose revision." );
			SimpleSkinCoreTests.Require( instance.DrawCalls > 0 && !instance.CallbackErrors.Any(), "No successful own-buffer draw or callback errors occurred." );
			var measurement = SimpleSkinGpuChecks.Measure( instance.Readback(), time, placement );
			rows.Add( new Row( name, formName, requestedRevision, instance.SubmittedRevision, instance.DrawCalls, measurement ) );
		}
	}
}
