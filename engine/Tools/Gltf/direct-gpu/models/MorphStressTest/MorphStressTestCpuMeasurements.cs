using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Sandbox.Gltf.DirectGpu.MorphStressTestValidation;

using static MorphStressTestReference;

/// <summary>Bounded callable CPU observations. No forced collection, VRAM estimate or performance-parity assertion.</summary>
public static class MorphStressTestCpuMeasurements
{
	public const int ExpectedCases = 4;
	public sealed record Measurement( string Form, int SourceBytes, int PackedBytes, double LoadMilliseconds, double PrimitiveAndMorphStageMilliseconds,
		double AllKeyAndInteriorPoseMilliseconds, int PoseSamples, long CurrentThreadAllocatedBytes, long OwnedInputArenaBytes, long DeclaredOutputBytes,
		long MorphUploadOwnedBytes, string Scope );
	public static Measurement[] Run( string checkoutRoot )
	{
		var reference = new MorphStressTestReference( checkoutRoot ); var rows = new List<Measurement>();
		foreach ( var form in reference.Forms )
		{
			var allocatedBefore = GC.GetAllocatedBytesForCurrentThread(); var watch = Stopwatch.StartNew(); using var d = form.Read(); var loadMs = watch.Elapsed.TotalMilliseconds; watch.Restart();
			long arenas = 0, output = 0, targets = 0;
			for ( var primitive = 0; primitive < 2; primitive++ )
			{
				var plan = GltfGpuPrimitivePlan.Create( d, 0, primitive ); arenas += plan.InputBytes.Length; output += (long)plan.VertexStride * plan.SourceVertices.Length; targets += d.MaterializeMorphTargetsUpload( 0, primitive ).OwnedBytes;
			}
			var stageMs = watch.Elapsed.TotalMilliseconds; watch.Restart(); var samples = 0;
			for ( var clip = 0; clip < 3; clip++ )
			{
				var times = Numbers( reference.Oracle.GetProperty( "clips" )[clip].GetProperty( "times" ) );
				foreach ( var time in times.Concat( times.Zip( times.Skip( 1 ), (a, b) => (a + b) / 2 ) ).Concat( new[] { -1.0, times[^1] + 1 } ) ) { d.EvaluatePose( clip, (float)time ); samples++; }
			}
			var poseMs = watch.Elapsed.TotalMilliseconds; var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
			Require( samples == 1065 && arenas > 0 && output > 0 && targets > 0, "Bounded observation inventory changed." );
			var source = reference.Forms.Single( x => x.Variant == form.Variant && !x.Compiled ); var packed = reference.Forms.Single( x => x.Variant == form.Variant && x.Compiled );
			rows.Add( new( form.Name, source.Bytes.Length, packed.Bytes.Length, loadMs, stageMs, poseMs, samples, allocated, arenas, output, targets,
				"One ordered CPU observation, including managed preparation overhead; arena/output bytes are logical buffer payloads. Not resident memory, native VRAM, kernel GPU time, matched vmdl, warmed throughput or parity evidence." ) );
		}
		Require( rows.Count == ExpectedCases, "Frozen CPU measurement inventory changed." ); return rows.ToArray();
	}
}
