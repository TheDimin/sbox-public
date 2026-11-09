using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Sandbox.Gltf.DirectGpu.MeshPrimitiveModesValidation;

using NMatrix = global::System.Numerics.Matrix4x4;
using static MeshPrimitiveModesReference;

/// <summary>One form and selected real mode per bounded call. The coordinator owns scene/camera/shader/capture and receipts.</summary>
public static class MeshPrimitiveModesGpuProbe
{
	public sealed record NumericRow( string Name, long PoseRevision, long SubmittedRevision, int DrawCalls, MeshPrimitiveModesGpuChecks.Measurement Measurement );
	public sealed record ImageRow( string Form, int Mode, MeshPrimitiveModesGpuChecks.Measurement Readback, MeshPrimitiveModesImageChecks.Measurement Image );
	public static async Task<MeshPrimitiveModesGpuChecks.Measurement[]> RunWholeSceneNumeric( string checkoutRoot, string formName,
		SceneWorld world, Material computeMaterial, Func<Task> waitForActualFrames, Func<int?, Material> materialResolver = null )
	{
		var form = new Inputs( checkoutRoot ).Get( formName ); using var document = form.Read();
		using var asset = GltfGpuAsset.Create( document, computeMaterial, materialResolver ); using var instance = asset.CreateInstance( world );
		await waitForActualFrames(); RequireSubmitted( instance ); var snapshots = instance.Readback();
		Require( snapshots.Count == 7 && snapshots.Select( x => x.Node ).Order().SequenceEqual( Enumerable.Range( 0, 7 ) ), "Full authored default scene lost or duplicated a primitive." );
		return snapshots.OrderBy( x => x.Node ).Select( snapshot =>
		{
			var result = MeshPrimitiveModesGpuChecks.Measure( form, snapshot.Node, NMatrix.Identity, [snapshot] ); MeshPrimitiveModesGpuChecks.AcceptNumeric( result ); return result;
		} ).ToArray();
	}
	public static async Task<NumericRow[]> RunNumeric( string checkoutRoot, string formName, int mode, SceneWorld world, Material computeMaterial,
		Func<Task> waitForActualFrames, Func<int?, Material> materialResolver = null )
	{
		var form = new Inputs( checkoutRoot ).Get( formName ); using var document = form.Read( mode );
		using var asset = GltfGpuAsset.Create( document, computeMaterial, materialResolver );
		using var a = asset.CreateInstance( world ); using var b = asset.CreateInstance( world );
		b.Placement = NMatrix.CreateTranslation( 0, 20, 0 ); await waitForActualFrames(); RequireSubmitted( a ); RequireSubmitted( b );
		var stableB = b.Readback().Single().Bytes.ToArray(); var rows = new List<NumericRow>();
		foreach ( var (name, placement) in new[] { ("identity", NMatrix.Identity), ("reflection_shear", AffinePlacement) } )
		{
			a.Placement = placement; await waitForActualFrames(); RequireSubmitted( a ); RequireSubmitted( b );
			var measurement = MeshPrimitiveModesGpuChecks.Measure( form, mode, placement, a.Readback() );
			MeshPrimitiveModesGpuChecks.AcceptNumeric( measurement ); rows.Add( new( name, a.PoseRevision, a.SubmittedRevision, a.DrawCalls, measurement ) );
			Require( stableB.SequenceEqual( b.Readback().Single().Bytes ), "Changing instance A mutated instance B's actual GPU output." );
		}
		b.Dispose(); a.Placement = NMatrix.Identity; await waitForActualFrames(); RequireSubmitted( a );
		var final = MeshPrimitiveModesGpuChecks.Measure( form, mode, NMatrix.Identity, a.Readback() ); MeshPrimitiveModesGpuChecks.AcceptNumeric( final );
		rows.Add( new( "survives_other_instance_disposal", a.PoseRevision, a.SubmittedRevision, a.DrawCalls, final ) ); return rows.ToArray();
	}

	// Diagnostic geometry images use one borrowed material on both paths; their
	// normal-debug pixels do not establish the default no-normal unlit material.
	// capture must return actual matched native-camera pixels and persist PNGs,
	// camera/target state and hashes in the coordinator's execution receipt.
	public static async Task<ImageRow> RunImage( string checkoutRoot, string formName, int mode, SceneWorld world, Material computeMaterial, Material drawMaterial,
		Func<Task> waitForActualFrames, Func<string, Task<global::Color[]>> capture )
	{
		var form = new Inputs( checkoutRoot ).Get( formName );
		await waitForActualFrames(); MeshPrimitiveModesImageChecks.RequireEmpty( await capture( "empty_before" ) );
		global::Color[] reference, negative;
		using ( var literal = new MeshPrimitiveModesReferenceScene( world, mode, NMatrix.Identity, drawMaterial ) )
		{
			reference = await capture( "literal_reference" ); Require( literal.DrawCalls > 0 && literal.CallbackErrors.Length == 0, "Literal reference did not draw successfully." );
		}
		await waitForActualFrames(); MeshPrimitiveModesImageChecks.RequireEmpty( await capture( "empty_after_reference" ) );
		using ( var literal = new MeshPrimitiveModesReferenceScene( world, mode, NMatrix.Identity, drawMaterial, corruptTopology: true ) )
		{
			negative = await capture( "literal_negative_topology" ); Require( literal.DrawCalls > 0 && literal.CallbackErrors.Length == 0, "Negative reference callback failed." );
		}
		await waitForActualFrames(); MeshPrimitiveModesImageChecks.RequireEmpty( await capture( "empty_after_negative" ) );
		using var document = form.Read( mode ); using var asset = GltfGpuAsset.Create( document, computeMaterial, _ => drawMaterial );
		var instance = asset.CreateInstance( world );
		try
		{
			var actual = await capture( "direct_gpu_actual" ); RequireSubmitted( instance );
			var readback = MeshPrimitiveModesGpuChecks.Measure( form, mode, NMatrix.Identity, instance.Readback() ); MeshPrimitiveModesGpuChecks.AcceptNumeric( readback );
			var image = MeshPrimitiveModesImageChecks.Measure( mode, actual, reference, negative );
			var beforeDelete = instance.DrawCalls; instance.Dispose(); await waitForActualFrames();
			MeshPrimitiveModesImageChecks.RequireEmpty( await capture( "empty_after_actual" ) ); Require( beforeDelete == instance.DrawCalls, "Deleted GPU callback continued drawing." );
			MeshPrimitiveModesImageChecks.AcceptExactGeometry( image ); return new( formName, mode, readback, image );
		}
		finally { instance.Dispose(); }
	}
	private static void RequireSubmitted( GltfGpuInstance instance ) => Require( instance.DrawCalls > 0 && instance.PoseRevision == instance.SubmittedRevision &&
		instance.CallbackErrors is { Length: 0 }, "No successful actual current-revision raw GPU draw." );
}
