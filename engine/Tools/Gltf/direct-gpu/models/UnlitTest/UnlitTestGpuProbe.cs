using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Sandbox.Gltf.DirectGpu.UnlitTestValidation;

using Matrix4x4 = System.Numerics.Matrix4x4;

/// <summary>Coordinator supplies the real scene, actual frame waits, and compiled compute material.</summary>
internal static class UnlitTestGpuProbe
{
	public sealed record Result( string Name, string Form, string MaterialScope, long PoseRevision, long SubmittedRevision,
		int DrawCalls, UnlitTestGpuChecks.Measurement Measurement );
	public const int ExpectedMeasurements = 20;

	public static async Task<IReadOnlyList<Result>> Run( string checkoutRoot, SceneWorld world, Material computeMaterial,
		Func<Task> waitForActualFrames, float positionToleranceInches, float normalTolerance, Func<int?, Material> diagnosticMaterialResolver = null, Action<GltfGpuAsset> configureAsset = null )
	{
		ArgumentNullException.ThrowIfNull( world ); ArgumentNullException.ThrowIfNull( computeMaterial ); ArgumentNullException.ThrowIfNull( waitForActualFrames );
		var results = new List<Result>();
		foreach ( var form in UnlitTestReference.OpenForms( checkoutRoot ) )
		{
			using var document = form.Read();
			using var asset = GltfGpuAsset.Create( document, computeMaterial, diagnosticMaterialResolver );
			configureAsset?.Invoke( asset );
			using var a = asset.CreateInstance( world ); using var b = asset.CreateInstance( world );
			b.Placement = Matrix4x4.CreateTranslation( 0, 210, 0 );
			await waitForActualFrames(); Submitted( a ); Submitted( b );
			var bStable = b.Readback().OrderBy( row => row.Node ).Select( row => (byte[])row.Bytes.Clone() ).ToArray();
			Record( "rest_authored_two_material_scene", a );
			foreach ( var test in new[]
			{
				("translated", Matrix4x4.CreateTranslation( 7, -11, 13 )),
				("full_affine_shear", new Matrix4x4( 1.5f, .2f, 0, 0, 0, .75f, .3f, 0, .1f, 0, 1.25f, 0, 7, -11, 13, 1 )),
				("mirrored_full_affine", new Matrix4x4( -1.5f, .2f, 0, 0, 0, .75f, .3f, 0, .1f, 0, 1.25f, 0, 7, -11, 13, 1 ))
			} )
			{
				a.Placement = test.Item2; a.SetPose( document.EvaluatePose() );
				await waitForActualFrames(); Submitted( a ); Submitted( b ); Record( test.Item1, a );
				var other = b.Readback().OrderBy( row => row.Node ).ToArray();
				if ( other.Length != bStable.Length || other.Where( (row, index) => !row.Bytes.SequenceEqual( bStable[index] ) ).Any() ) throw new InvalidDataException( "Updating UnlitTest instance A mutated B's independent output buffers." );
			}
			b.Dispose(); a.Placement = Matrix4x4.Identity; a.SetPose( document.EvaluatePose() );
			await waitForActualFrames(); Submitted( a ); Record( "survives_other_instance_disposal", a );

			void Record( string name, GltfGpuInstance instance )
			{
				var measurement = UnlitTestGpuChecks.Measure( instance.Readback(), instance.Placement );
				UnlitTestGpuChecks.RequireWithin( measurement, positionToleranceInches, normalTolerance );
				results.Add( new( name, form.Name, configureAsset is not null ? "actual_installed_source_adapter_candidate_template" : diagnosticMaterialResolver is null ? "authored_default_source_material" : "explicit_diagnostic_override_geometry_only",
					instance.PoseRevision, instance.SubmittedRevision, instance.DrawCalls, measurement ) );
			}
		}
		UnlitTestCoreTests.Require( results.Count == ExpectedMeasurements, "Frozen GPU measurement inventory changed." );
		return results.AsReadOnly();
	}

	private static void Submitted( GltfGpuInstance instance )
	{
		if ( instance.DrawCalls <= 0 || instance.PoseRevision != instance.SubmittedRevision || instance.CallbackErrors.Length != 0 )
			throw new InvalidDataException( "Current UnlitTest revision was not successfully submitted: " + string.Join( "\n", instance.CallbackErrors ) );
	}
}
