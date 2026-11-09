using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Sandbox.Gltf.DirectGpu.Tests;

using NM = System.Numerics.Matrix4x4;

/// <summary>Coordinator-supplied real frames and scene; no editor launch or shader compile.</summary>
internal static class FoxGpuProbe
{
	public static async Task<IReadOnlyList<object>> Run( GltfDocument document, FoxReference reference, SceneWorld world, Material computeMaterial,
		Func<Task> waitForActualFrames, Func<int?, Material> materialResolver = null )
	{
		var results = new List<object>(); using var asset = GltfGpuAsset.Create( document, computeMaterial, materialResolver );
		using var a = asset.CreateInstance( world ); using var b = asset.CreateInstance( world );
		b.Placement = NM.CreateTranslation( 0, 140, 0 ); b.SetPose( document.EvaluatePose( 2, .5f ) );
		await waitForActualFrames(); RequireSubmitted( a ); RequireSubmitted( b );
		var stableB = b.Readback().Select( row => (byte[])row.Bytes.Clone() ).ToArray();
		results.Add( Row( "rest", null, 0, a, reference ) );
		for ( var clip = 0; clip < reference.ClipCount; clip++ )
		{
			// Explicit authored key/midpoint numeric checks; high-cadence capture
			// and independently observed real-time playback are separate runs.
			var keys = reference.KeyTimes( clip );
			var times = keys.Concat( keys.Zip( keys.Skip( 1 ), (x, y) => (x + y) * .5f ) ).Append( keys[^1] + .01f ).Distinct().Order().ToArray();
			foreach ( var time in times )
			{
				a.SetPose( document.EvaluatePose( clip, time ) ); await waitForActualFrames(); RequireSubmitted( a ); RequireSubmitted( b );
				results.Add( Row( "clip_" + clip, clip, time, a, reference ) );
				var other = b.Readback(); if ( other.Count != stableB.Length || other.Where( (row, index) => !row.Bytes.SequenceEqual( stableB[index] ) ).Any() ) throw new InvalidDataException( "Animating Fox instance A mutated B's GPU buffers." );
			}
		}
		foreach ( var placement in new[] { NM.CreateTranslation( 3, -4, 5 ), NM.CreateScale( 2, .5f, 1.25f ) * NM.CreateTranslation( 3, -4, 5 ),
			new NM( 2, .2f, 0, 0, 0, .5f, .3f, 0, 0, 0, -1, 0, 3, -4, 5, 1 ) } )
		{
			a.Placement = placement; a.SetPose( document.EvaluatePose( 2, .5f ) ); await waitForActualFrames(); RequireSubmitted( a ); results.Add( Row( "placement", 2, .5f, a, reference ) );
		}
		b.Dispose(); a.SetPose( document.EvaluatePose( 0, .5f ) ); await waitForActualFrames(); RequireSubmitted( a ); results.Add( Row( "surviving_instance", 0, .5f, a, reference ) );
		return results.AsReadOnly();
	}
	private static object Row( string name, int? clip, float time, GltfGpuInstance instance, FoxReference reference ) => new
	{
		name, clip, source_time = time, instance.PoseRevision, instance.SubmittedRevision, instance.DrawCalls,
		material_scope = "source default stock-material adapter unless caller explicitly supplies a diagnostic resolver",
		comparison = FoxGpuChecks.Measure( reference, clip, time, instance.Placement, instance.Readback() )
	};
	private static void RequireSubmitted( GltfGpuInstance instance )
	{
		if ( instance.DrawCalls <= 0 || instance.PoseRevision != instance.SubmittedRevision || instance.CallbackErrors.Length != 0 ) throw new InvalidDataException( "Fox current pose was not successfully submitted: " + string.Join( "\n", instance.CallbackErrors ) );
	}
}
