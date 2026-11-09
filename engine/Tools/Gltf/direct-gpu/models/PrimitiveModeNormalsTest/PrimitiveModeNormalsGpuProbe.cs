using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Sandbox.Gltf.DirectGpu.PrimitiveModeNormalsValidation;

using Matrix4x4 = System.Numerics.Matrix4x4;
using static PrimitiveModeNormalsReference;

/// <summary>The coordinator supplies compiled compute material, real frames and capture controls. Creates zero native models/meshes.</summary>
public static class PrimitiveModeNormalsGpuProbe
{
	public sealed record Result( string Name, string Form, string MaterialScope, long Revision, int DrawCalls, bool SubmittedModesValidated, PrimitiveModeNormalsGpuChecks.Measurement[] Measurements );
	public const int ExpectedMeasurements = 8;
	public static async Task<IReadOnlyList<Result>> Run( string checkoutRoot, SceneWorld world, Material computeMaterial, Func<Task> waitForActualFrames, Func<int?, Material> diagnosticMaterialResolver = null, bool requireSubmittedModeEvidence = false )
	{
		var reference = new PrimitiveModeNormalsReference( checkoutRoot ); var results = new List<Result>();
		foreach ( var form in reference.Forms )
		{
			using var document = form.Read(); using var asset = GltfGpuAsset.Create( document, computeMaterial, diagnosticMaterialResolver ); using var instance = asset.CreateInstance( world );
			await waitForActualFrames(); Record( "rest_all_25_primitives" );
			foreach ( var test in new[]
			{
				("full_affine", new Matrix4x4( 1.5f, .2f, 0, 0, 0, .75f, .3f, 0, .1f, 0, 1.25f, 0, 7, -11, 13, 1 )),
				("mirrored_full_affine", new Matrix4x4( -1.5f, .2f, 0, 0, 0, .75f, .3f, 0, .1f, 0, 1.25f, 0, 7, -11, 13, 1 ))
			} )
			{
				instance.Placement = test.Item2; await waitForActualFrames(); Record( test.Item1 );
			}
			instance.Placement = Matrix4x4.Identity; await waitForActualFrames(); Record( "restored_winding_and_placement" );
			void Record( string name )
			{
				Require( instance.DrawCalls >= 25 && instance.SubmittedRevision == instance.PoseRevision && instance.CallbackErrors.Length == 0, "All primitive draws did not submit successfully for current revision: " + string.Join( "\n", instance.CallbackErrors ) );
				var snapshots = instance.Readback();
				if ( requireSubmittedModeEvidence ) PrimitiveModeNormalsGpuChecks.RequireSubmittedModes( snapshots, instance.PoseRevision );
				var measurements = PrimitiveModeNormalsGpuChecks.Measure( reference, snapshots, instance.Placement );
				foreach ( var row in measurements ) PrimitiveModeNormalsGpuChecks.RequireWithin( row );
				results.Add( new( name, form.Name, diagnosticMaterialResolver is null ? "source_stock_material_adapter_geometry_readback_only" : "explicit_diagnostic_material_geometry_readback_only", instance.PoseRevision, instance.DrawCalls, requireSubmittedModeEvidence, measurements ) );
			}
		}
		Require( results.Count == ExpectedMeasurements, "Frozen GPU measurement inventory changed." ); return results.AsReadOnly();
	}
}
