using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Sandbox.Gltf.DirectGpu.Tests;

using NM = System.Numerics.Matrix4x4;

/// <summary>
/// Bounded nine-row smoke before the complete Fox key/midpoint matrix. The
/// coordinator owns scene/camera, shader compilation, real frame waits and PNGs.
/// All six clip sample callbacks occur after the second instance is disposed.
/// </summary>
internal static class FoxGpuSmokeProbe
{
	public static async Task<IReadOnlyList<object>> Run( GltfDocument document, FoxReference reference, SceneWorld world,
		Material computeMaterial, Func<Task> waitForActualFrames,
		Func<string, GltfGpuInstance, Task> afterSample = null, Func<int?, Material> materialResolver = null,
		Action<object> completedRow = null )
	{
		ArgumentNullException.ThrowIfNull( document ); ArgumentNullException.ThrowIfNull( reference );
		ArgumentNullException.ThrowIfNull( world ); ArgumentNullException.ThrowIfNull( waitForActualFrames );
		var result = new List<object>();
		using var asset = GltfGpuAsset.Create( document, computeMaterial, materialResolver );
		using var a = asset.CreateInstance( world ); using var b = asset.CreateInstance( world );
		// Both instances must remain inside the coordinator's camera frustum;
		// the full probe may arrange a wider scene separately.
		var secondOffset = FoxDiagnostics.FitSmokeCamera( reference ).SecondInstanceOffset;
		b.Placement = NM.CreateTranslation( secondOffset.x, secondOffset.y, secondOffset.z );
		var bTime = reference.Duration( 2 ) * .5f;
		b.SetPose( document.EvaluatePose( 2, bTime ) );
		await waitForActualFrames(); RequireSubmitted( a ); RequireSubmitted( b );
		var stableB = b.Readback().Select( row => (byte[])row.Bytes.Clone() ).ToArray();
		Add( Row( "second_instance_initial", 2, bTime, b, reference, materialResolver is null ) );

		var aTime = reference.Duration( 2 ) * .25f;
		a.SetPose( document.EvaluatePose( 2, aTime ) );
		await waitForActualFrames(); RequireSubmitted( a ); RequireSubmitted( b );
		var after = b.Readback();
		if ( after.Count != stableB.Length || after.Where( (row, index) => !row.Bytes.SequenceEqual( stableB[index] ) ).Any() )
			throw new InvalidDataException( "Advancing Fox instance A changed independent instance B's GPU output." );
		Add( Row( "independent_first_instance_advanced", 2, aTime, a, reference, materialResolver is null ) );

		b.Dispose(); var deletedDraws = b.DrawCalls;
		await waitForActualFrames(); RequireSubmitted( a );
		if ( b.DrawCalls != deletedDraws ) throw new InvalidDataException( "Disposed Fox instance continued submitting draws." );
		var disposedReadRejected = false;
		try { _ = b.Readback(); } catch ( ObjectDisposedException ) { disposedReadRejected = true; }
		if ( !disposedReadRejected ) throw new InvalidDataException( "Disposed Fox GPU output remained accessible." );
		Add( Row( "first_instance_survives_second_disposal", 2, aTime, a, reference, materialResolver is null ) );

		for ( var clip = 0; clip < reference.ClipCount; clip++ )
		{
			foreach ( var sample in new[] { (Suffix: "start", Time: 0f), (Suffix: "middle", Time: reference.Duration( clip ) * .5f) } )
			{
				var name = "clip_" + clip + "_" + sample.Suffix;
				a.SetPose( document.EvaluatePose( clip, sample.Time ) );
				await waitForActualFrames(); RequireSubmitted( a );
				Add( Row( name, clip, sample.Time, a, reference, materialResolver is null ) );
				if ( afterSample is not null ) { await afterSample( name, a ); RequireSubmitted( a ); }
			}
		}
		if ( result.Count != 9 ) throw new InvalidDataException( "Fox smoke inventory changed; audit the request's nine expected rows." );
		return result.AsReadOnly();
		void Add( object row ) { result.Add( row ); completedRow?.Invoke( row ); }
	}

	private static object Row( string name, int clip, float time, GltfGpuInstance instance, FoxReference reference, bool sourceMaterials ) => new
	{
		name, clip, clip_name = reference.ClipName( clip ), source_time = time,
		instance.PoseRevision, instance.SubmittedRevision, instance.DrawCalls,
		material_scope = sourceMaterials ? "source material through default stock adapter" : "explicit caller material override",
		comparison = FoxGpuChecks.Measure( reference, clip, time, instance.Placement, instance.Readback() ),
		scope = "Deterministic current-revision GPU smoke. Full key matrix, rendered parity and actual wall-clock playback have separate acceptance gates."
	};

	private static void RequireSubmitted( GltfGpuInstance instance )
	{
		if ( instance.DrawCalls <= 0 || instance.SubmittedRevision != instance.PoseRevision || instance.CallbackErrors.Length != 0 )
			throw new InvalidDataException( "Fox current GPU revision was not successfully submitted: " + string.Join( "\n", instance.CallbackErrors ) );
	}
}
