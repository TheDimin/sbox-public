using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Sandbox.Gltf.DirectGpu.Tests;

using Matrix4x4 = global::System.Numerics.Matrix4x4;

/// <summary>
/// Model-owned numeric execution plan. The coordinator supplies the real editor
/// scene, compiled compute shader and a bounded actual-frame wait. This does not
/// create an editor/camera, compile shaders, render an alternate native model,
/// approve error tolerances or claim that numeric checks establish appearance.
/// </summary>
public static class RiggedSimpleGpuProbe
{
	public static async Task<IReadOnlyList<RiggedSimpleGpuProbeRow>> Run( GltfDocument document,
		RiggedSimpleReference reference, SceneWorld world, Material computeMaterial,
		Func<Task> waitForActualFrames, Func<int?, Material> materialResolver = null )
	{
		ArgumentNullException.ThrowIfNull( document ); ArgumentNullException.ThrowIfNull( reference );
		ArgumentNullException.ThrowIfNull( world ); ArgumentNullException.ThrowIfNull( waitForActualFrames );
		var result = new List<RiggedSimpleGpuProbeRow>();
		using var asset = GltfGpuAsset.Create( document, computeMaterial, materialResolver );
		using var a = asset.CreateInstance( world );
		using var b = asset.CreateInstance( world );
		b.Placement = Matrix4x4.CreateTranslation( 0, 20, 0 );
		b.SetPose( document.EvaluatePose( 0, reference.KeyTimes[25] ) );
		await waitForActualFrames();
		RequireSubmitted( b );
		var stableB = b.Readback().Select( row => (byte[])row.Bytes.Clone() ).ToArray();
		result.Add( Row( "instance_b_initial", b, reference, reference.KeyTimes[25], b.Placement ) );
		var samples = new List<float?> { null, 0f, reference.KeyTimes[0] - 0.01f, reference.KeyTimes[^1] + 0.01f };
		foreach ( var key in reference.KeyTimes ) samples.Add( key );
		for ( var key = 1; key < reference.KeyTimes.Length; key++ ) samples.Add( (reference.KeyTimes[key - 1] + reference.KeyTimes[key]) * 0.5f );
		for ( var index = 0; index < samples.Count; index++ )
		{
			var time = samples[index];
			a.SetPose( document.EvaluatePose( time.HasValue ? 0 : null, time.GetValueOrDefault() ) );
			await waitForActualFrames();
			RequireSubmitted( a ); RequireSubmitted( b );
			result.Add( Row( "pose_" + index, a, reference, time, Matrix4x4.Identity ) );
			var other = b.Readback();
			if ( other.Count != stableB.Length || other.Where( (row, rowIndex) => !row.Bytes.SequenceEqual( stableB[rowIndex] ) ).Any() )
				throw new InvalidDataException( "Updating RiggedSimple instance A mutated instance B's GPU output." );
		}
		var placementCases = new[]
		{
			Matrix4x4.CreateTranslation( 3, -4, 5 ),
			Matrix4x4.CreateScale( 2, 0.5f, 1.25f ) * Matrix4x4.CreateTranslation( 3, -4, 5 ),
			new Matrix4x4( 2, 0.2f, 0, 0, 0, 0.5f, 0.3f, 0, 0, 0, -1, 0, 3, -4, 5, 1 )
		};
		for ( var index = 0; index < placementCases.Length; index++ )
		{
			a.Placement = placementCases[index];
			a.SetPose( document.EvaluatePose( 0, reference.KeyTimes[25] ) );
			await waitForActualFrames(); RequireSubmitted( a );
			result.Add( Row( "placement_" + index, a, reference, reference.KeyTimes[25], placementCases[index] ) );
		}
		// Deleting B must detach its callback/buffers without damaging A.
		b.Dispose();
		a.SetPose( document.EvaluatePose( 0, reference.KeyTimes[0] ) );
		await waitForActualFrames(); RequireSubmitted( a );
		result.Add( Row( "instance_a_after_b_disposal", a, reference, reference.KeyTimes[0], a.Placement ) );
		return result;
	}

	private static RiggedSimpleGpuProbeRow Row( string name, GltfGpuInstance instance,
		RiggedSimpleReference reference, float? time, Matrix4x4 placement ) => new()
	{
		Name = name, PoseRevision = instance.PoseRevision, SubmittedRevision = instance.SubmittedRevision,
		DrawCalls = instance.DrawCalls, Comparison = RiggedSimpleGpuChecks.Measure( reference, time, placement, instance.Readback() )
	};

	private static void RequireSubmitted( GltfGpuInstance instance )
	{
		if ( instance.DrawCalls <= 0 || instance.SubmittedRevision != instance.PoseRevision )
			throw new InvalidDataException( "RiggedSimple GPU revision was not actually submitted by a draw callback." );
		if ( instance.CallbackErrors is null || instance.CallbackErrors.Length != 0 )
			throw new InvalidDataException( "RiggedSimple GPU callback reported errors or no diagnostic array." );
	}
}

public sealed class RiggedSimpleGpuProbeRow
{
	public string Name { get; init; }
	public long PoseRevision { get; init; }
	public long SubmittedRevision { get; init; }
	public int DrawCalls { get; init; }
	public RiggedSimpleGpuComparison Comparison { get; init; }
}
