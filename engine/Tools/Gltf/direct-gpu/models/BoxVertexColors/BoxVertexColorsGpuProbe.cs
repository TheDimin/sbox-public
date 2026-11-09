using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Sandbox.Gltf.DirectGpu.BoxVertexColorsValidation;

using NMatrix = System.Numerics.Matrix4x4;
using static BoxVertexColorsReference;

/// <summary>Coordinator alone supplies actual frames, scene and compiled shader; no native Model/Mesh.</summary>
internal static class BoxVertexColorsGpuProbe
{
	public sealed record Result( string Name, string Form, string Scope, long PoseRevision, long SubmittedRevision, int DrawCalls, BoxVertexColorsGpuChecks.Measurement Measurement );
	public const int ExpectedMeasurements = 38;
	public static async Task<Result[]> Run( string checkoutRoot, SceneWorld world, Material computeMaterial, Func<Task> waitForCompletedFrames, Func<int?, Material> diagnosticMaterialResolver = null )
	{
		ArgumentNullException.ThrowIfNull( world ); ArgumentNullException.ThrowIfNull( computeMaterial ); ArgumentNullException.ThrowIfNull( waitForCompletedFrames );
		var reference = new BoxVertexColorsReference( checkoutRoot ); var results = new List<Result>();
		foreach ( var form in reference.Forms )
		{
			using var document = form.Read(); using var asset = GltfGpuAsset.Create( document, computeMaterial, diagnosticMaterialResolver ); using var instance = asset.CreateInstance( world );
			await waitForCompletedFrames(); Record( "rest" );
			if ( !form.Derived )
			{
				using var other = asset.CreateInstance( world ); other.Placement = NMatrix.CreateTranslation( 0, 120, 0 ); await waitForCompletedFrames();
				var stable = (byte[])other.Readback().Single().Bytes.Clone();
				instance.Placement = new NMatrix( 1.5f, .2f, 0, 0, 0, .75f, .3f, 0, .1f, 0, 1.25f, 0, 7, -11, 13, 1 ); await waitForCompletedFrames(); Record( "full_affine" );
				Require( other.Readback().Single().Bytes.SequenceEqual( stable ), "Instance A mutated B's color/output storage." );
				instance.Placement = NMatrix.CreateScale( -1, 1, 1 ); await waitForCompletedFrames(); Record( "mirrored" );
				other.Dispose(); instance.Placement = NMatrix.Identity; await waitForCompletedFrames(); Record( "survives_other_disposal" );
			}
			void Record( string name )
			{
				Require( instance.DrawCalls > 0 && instance.PoseRevision == instance.SubmittedRevision && instance.CallbackErrors.Length == 0, "Current revision did not submit: " + string.Join( "\n", instance.CallbackErrors ) );
				var measured = BoxVertexColorsGpuChecks.Measure( reference, form, instance.Readback(), instance.Placement ); BoxVertexColorsGpuChecks.RequireWithin( measured );
				results.Add( new( name, form.Name, diagnosticMaterialResolver is null ? "source_material_bound; completed_gpu_vertex_data_only; image_gate_separate" : "diagnostic_material_override; completed_gpu_vertex_data_only", instance.PoseRevision, instance.SubmittedRevision, instance.DrawCalls, measured ) );
			}
		}
		Require( results.Count == ExpectedMeasurements, "Frozen actual GPU inventory changed." ); return results.ToArray();
	}
}
