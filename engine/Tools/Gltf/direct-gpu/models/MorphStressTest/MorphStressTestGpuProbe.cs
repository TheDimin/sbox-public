using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

namespace Sandbox.Gltf.DirectGpu.MorphStressTestValidation;

using static MorphStressTestReference;

/// <summary>Callable actual-GPU/readback probe. Coordinator supplies admitted native presentation and real frames.</summary>
internal static class MorphStressTestGpuProbe
{
	public const int ExpectedMeasurements = 48;
	public static async Task<IReadOnlyList<object>> Run( string checkoutRoot, SceneWorld world, Material computeMaterial,
		Func<Task> waitForActualFrames, Func<string, GltfGpuInstance, Task> afterSample = null, Func<int?, Material> materialResolver = null,
		double positionToleranceInches = .0005, double normalTolerance = 2e-6, double tangentValidityTolerance = 2e-6 )
	{
		ArgumentNullException.ThrowIfNull( waitForActualFrames ); var r = new MorphStressTestReference( checkoutRoot ); var rows = new List<object>();
		foreach ( var form in r.Forms )
		{
			var setup = Stopwatch.StartNew(); using var d = form.Read(); var readMs = setup.Elapsed.TotalMilliseconds; setup.Restart();
			using var asset = GltfGpuAsset.Create( d, computeMaterial, materialResolver ); var assetMs = setup.Elapsed.TotalMilliseconds; setup.Restart();
			using var a = asset.CreateInstance( world ); var instanceMs = setup.Elapsed.TotalMilliseconds;
			await Capture( "rest", null, 0, a, new double[8] );
			for ( var clip = 0; clip < 3; clip++ )
			{
				var c = r.Oracle.GetProperty( "clips" )[clip]; var times = Numbers( c.GetProperty( "times" ) ); var keys = c.GetProperty( "weights" );
				var peak = Enumerable.Range( 0, times.Length ).OrderByDescending( i => Numbers( keys[i] ).Sum() ).First();
				await Capture( $"clip{clip}_peak_authored_key{peak}", clip, (float)times[peak], a, r.Weights( clip, (float)times[peak] ) );
				var time = (float)((times[times.Length / 2] + times[times.Length / 2 + 1]) / 2);
				await Capture( $"clip{clip}_linear_interior", clip, time, a, r.Weights( clip, time ) );
			}
			var individual = r.Oracle.GetProperty( "clips" )[0]; var individualTimes = Numbers( individual.GetProperty( "times" ) );
			var lastTargetPeak = Enumerable.Range( 0, individualTimes.Length ).OrderByDescending( i => individual.GetProperty( "weights" )[i][7].GetDouble() ).First();
			var lastTime = (float)individualTimes[lastTargetPeak]; await Capture( "eighth_target_peak", 0, lastTime, a, r.Weights( 0, lastTime ) );
			using ( var b = asset.CreateInstance( world ) )
			{
				b.Placement = AffinePlacement; await Capture( "independent_B_affine", 0, lastTime, b, r.Weights( 0, lastTime ) );
				var fingerprint = b.Readback().Select( x => Hash( x.Bytes ) ).ToArray(); a.SetPose( d.EvaluatePose() ); await waitForActualFrames();
				Require( b.Readback().Select( x => Hash( x.Bytes ) ).SequenceEqual( fingerprint ), "Updating A mutated B output." );
			}
			await Capture( "A_after_B_disposal", null, 0, a, new double[8] );

			async Task Capture( string name, int? clip, float time, GltfGpuInstance instance, double[] weights )
			{
				var before = instance.DrawCalls; var timer = Stopwatch.StartNew(); instance.SetPose( d.EvaluatePose( clip, time ) ); var cpuPoseMs = timer.Elapsed.TotalMilliseconds; var revision = instance.PoseRevision;
				timer.Restart(); await waitForActualFrames(); var frameWaitMs = timer.Elapsed.TotalMilliseconds; RequireSubmitted( instance, before, revision );
				timer.Restart(); var snapshots = instance.Readback(); var readbackMs = timer.Elapsed.TotalMilliseconds;
				var measured = MorphStressTestGpuChecks.Measure( snapshots, r, weights, instance.Placement ); MorphStressTestGpuChecks.RequireWithin( measured, positionToleranceInches, normalTolerance, tangentValidityTolerance );
				var hashes = snapshots.Select( x => Hash( x.Bytes ) ).ToArray();
				if ( afterSample is not null ) { await afterSample( form.Name.Replace( '/', '_' ) + "_" + name, instance ); RequireSubmitted( instance, before, revision ); Require( instance.Readback().Select( x => Hash( x.Bytes ) ).SequenceEqual( hashes ), "Capture mutated current pose output." ); }
				rows.Add( new { name, form = form.Name, clip, requestedSourceTime = time, weights, measured, instance.PoseRevision, instance.SubmittedRevision, instance.DrawCalls,
					cpuReadMilliseconds = readMs, assetCreateMilliseconds = assetMs, instanceCreateMilliseconds = instanceMs, cpuPoseMilliseconds = cpuPoseMs, frameWaitMilliseconds = frameWaitMs, blockingReadbackMilliseconds = readbackMs,
					vertexByteHashes = hashes, indexHashes = snapshots.Select( x => Hash( System.Runtime.InteropServices.MemoryMarshal.AsBytes( x.Indices.AsSpan() ) ) ).ToArray(),
					materialScope = materialResolver is null ? "source stock-adapter materials; image fidelity separate" : "explicit borrowed diagnostic override",
					scope = "completed actual GPU vertex/index readback; CPU wall times include scheduling; no kernel GPU timing, wall-clock animation, image parity or performance parity claim" } );
			}
		}
		foreach ( var compiled in new[] { false, true } ) foreach ( var ninth in new[] { false, true } ) foreach ( var nodeOverride in new[] { false, true } )
		{
			var form = r.Fixture( ninth, nodeOverride, compiled ); using var d = form.Read(); using var asset = GltfGpuAsset.Create( d, computeMaterial, materialResolver ); using var instance = asset.CreateInstance( world );
			var weights = new double[ninth ? 9 : 8]; weights[^1] = 1.75; if ( nodeOverride ) weights = ninth ? r.SignedWeights.Append( -.375 ).ToArray() : r.SignedWeights;
			var before = instance.DrawCalls; instance.SetPose( d.EvaluatePose() ); var revision = instance.PoseRevision; await waitForActualFrames(); RequireSubmitted( instance, before, revision );
			var measured = MorphStressTestGpuChecks.Measure( instance.Readback(), r, weights, instance.Placement ); MorphStressTestGpuChecks.RequireWithin( measured, positionToleranceInches, normalTolerance, tangentValidityTolerance );
			if ( afterSample is not null ) await afterSample( form.Name.Replace( '/', '_' ), instance );
			rows.Add( new { name = form.Name, weights, measured, instance.PoseRevision, instance.SubmittedRevision, instance.DrawCalls, scope = "synthetic signed/ninth-target device control; actual Khronos source shapes remain eight targets" } );
		}
		Require( rows.Count == ExpectedMeasurements, "Frozen actual-GPU probe inventory changed." ); return rows;
	}
	private static void RequireSubmitted( GltfGpuInstance instance, int beforeDraws, long revision )
	{
		Require( instance.DrawCalls > beforeDraws && instance.PoseRevision == revision && instance.SubmittedRevision == revision && instance.CallbackErrors.Length == 0, "No completed current-revision raw GPU draw or callback failed." );
	}
}
