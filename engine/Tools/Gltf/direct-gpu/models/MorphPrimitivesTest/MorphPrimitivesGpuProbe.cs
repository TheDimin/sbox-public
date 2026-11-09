using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Sandbox.Gltf.DirectGpu.MorphPrimitivesValidation;

using NMatrix = System.Numerics.Matrix4x4;
using NVector = System.Numerics.Vector3;
using static MorphPrimitivesReference;

/// <summary>Only the coordinator invokes this probe with real native rendered frames and borrowed presentation resources.</summary>
internal static class MorphPrimitivesGpuProbe
{
	public sealed record Bounds( float[] Minimum, float[] Maximum, float[] Target, float[] CameraPosition, float OrthographicHeight );
	public static Bounds DefaultBounds( Form form )
	{
		var min = new NVector( float.PositiveInfinity ); var max = new NVector( float.NegativeInfinity );
		for ( var p = 0; p < 2; p++ ) for ( var v = 0; v < Count( p ); v++ ) { var value = EnginePosition( form, p, v, .5f, NMatrix.Identity ); min = NVector.Min( min, value ); max = NVector.Max( max, value ); }
		var center = (min + max) * .5f; var extent = max - min; var span = MathF.Max( extent.X, MathF.Max( extent.Y, extent.Z ) );
		Require( float.IsFinite( span ) && span > 0, "Independent default-morph framing is empty." );
		var camera = center + NVector.Normalize( new NVector( 1, 1, .9f ) ) * span * 3;
		return new( Values( min ), Values( max ), Values( center ), Values( camera ), span * 1.8f );
	}
	public static async Task<IReadOnlyList<object>> Run( string checkoutRoot, string formName, SceneWorld world, Material computeMaterial,
		Func<Task> waitForActualFrames, Func<int?, Material> materialResolver = null, Func<string, float, GltfGpuInstance, Task> afterSample = null )
	{
		var reference = new MorphPrimitivesReference( checkoutRoot ); var form = reference.Forms.Single( x => x.Name == formName ); var results = new List<object>();
		using ( var document = form.Read() ) using ( var asset = GltfGpuAsset.Create( document, computeMaterial, materialResolver ) ) using ( var instance = asset.CreateInstance( world ) )
		{
			await waitForActualFrames(); Submitted( instance );
			var snapshots = instance.Readback(); var revision = instance.PoseRevision; var fingerprints = snapshots.OrderBy( x => x.Primitive ).Select( x => Hash( x.Bytes ) ).ToArray();
			var comparison = MorphPrimitivesGpuChecks.AssertReadback( form, snapshots, .5f, NMatrix.Identity );
			if ( afterSample is not null ) await afterSample( "actual_authored_mesh_default_half_strength", .5f, instance );
			Submitted( instance ); Require( instance.PoseRevision == revision && instance.Readback().OrderBy( x => x.Primitive ).Select( x => Hash( x.Bytes ) ).SequenceEqual( fingerprints ), "Capturing the authored default changed its pose or completed GPU bytes." );
			results.Add( new { name = "actual_authored_mesh_default_half_strength", form = form.Name, comparison, independentBounds = DefaultBounds( form ) } );
		}
		// This sample has no animation. The following derived three-key weight
		// control intentionally exercises signed morphs and instance isolation;
		// it is never labeled authored animation or wall-clock playback proof.
		using ( var document = form.Control( animated: true ) ) using ( var asset = GltfGpuAsset.Create( document, computeMaterial, materialResolver ) ) using ( var a = asset.CreateInstance( world ) ) using ( var b = asset.CreateInstance( world ) )
		{
			b.Placement = NMatrix.CreateTranslation( 8, -5, 7 ); b.SetPose( document.EvaluatePose( 0, 0 ) ); await waitForActualFrames(); Submitted( a ); Submitted( b );
			var frozen = b.Readback().OrderBy( x => x.Primitive ).Select( row => Hash( row.Bytes ) ).ToArray();
			results.Add( new { name = "derived_independent_instance_B", form = form.Name, comparison = MorphPrimitivesGpuChecks.AssertReadback( form, b.Readback(), -.75f, b.Placement ) } );
			foreach ( var selected in new[] { (Name: "derived_signed_negative_weight", Time: 0f, Weight: -.75f), (Name: "derived_signed_midpoint", Time: .5f, Weight: -.125f), (Name: "derived_default_half_key", Time: 1f, Weight: .5f), (Name: "derived_over_one_weight", Time: 2f, Weight: 1.5f) } )
			{
				a.SetPose( document.EvaluatePose( 0, selected.Time ) ); var revision = a.PoseRevision; var draws = a.DrawCalls; await waitForActualFrames(); Submitted( a ); Submitted( b );
				Require( a.PoseRevision == revision && a.DrawCalls > draws && b.Readback().OrderBy( x => x.Primitive ).Select( row => Hash( row.Bytes ) ).SequenceEqual( frozen ), "A pose update mutated B or did not draw." );
				var comparison = MorphPrimitivesGpuChecks.AssertReadback( form, a.Readback(), selected.Weight, a.Placement );
				Submitted( a ); results.Add( new { name = selected.Name, form = form.Name, requestedTime = selected.Time, expectedWeight = selected.Weight, a.PoseRevision, a.SubmittedRevision, a.DrawCalls, comparison } );
			}
			b.Dispose(); a.SetPose( document.EvaluatePose( 0, 1 ) ); await waitForActualFrames(); Submitted( a );
			results.Add( new { name = "survives_other_instance_disposal", form = form.Name, comparison = MorphPrimitivesGpuChecks.AssertReadback( form, a.Readback(), .5f, a.Placement ) } );
		}
		if ( form.Variant == "glTF" )
		{
			using var sourceControl = MorphPrimitivesLayoutFixture.Create( form );
			var bytes = System.Text.Encoding.UTF8.GetBytes( sourceControl.Root.GetRawText() );
			if ( form.Compiled ) bytes = GltfCompiler.Compile( bytes, form.Resolve, Selected );
			using var document = GltfReader.Read( bytes, form.Resolve, Selected );
			using var asset = GltfGpuAsset.Create( document, computeMaterial, materialResolver ); using var instance = asset.CreateInstance( world );
			await waitForActualFrames(); Submitted( instance );
			results.Add( new { name = "derived_sparse_padded_mixed_normalized_nonindexed_GPU", form = form.Name, derivedForm = form.Compiled ? "derived compiled GLB" : "derived source JSON",
				comparison = MorphPrimitivesGpuChecks.AssertReadback( form, instance.Readback(), -.75f, instance.Placement, derivedLayout: true ) } );
		}
		results.Add( new { name = "separate_image_material_and_API_gates", form = form.Name,
			materialScope = materialResolver is null ? "source red/green textured stock material adapter" : "explicit borrowed diagnostic/native override; source material fidelity remains pending",
			imageAcceptance = "pending fresh matched empty/reference/deleted/actual/deleted images, two disjoint primitive witness regions, and independent pixel gate",
			integrationAcceptance = "research probe; public Model.Load API, Windows and benchmarks are separate gates" } );
		Require( results.Count == (form.Variant == "glTF" ? 9 : 8), "Frozen GPU probe row inventory changed." ); return results.AsReadOnly();
	}
	private static void Submitted( GltfGpuInstance i ) => Require( i.DrawCalls > 0 && i.PoseRevision == i.SubmittedRevision && i.CallbackErrors.Length == 0, "No successful current GPU submission: " + string.Join( "\n", i.CallbackErrors ) );
	private static float[] Values( NVector v ) => new[] { v.X, v.Y, v.Z };
}
