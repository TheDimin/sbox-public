using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

namespace Sandbox.Gltf.DirectGpu.InterpolationTestValidation;

/// <summary>
/// Model-owned real GPU submission plan. Coordinator supplies the editor scene,
/// compiled shader and actual-frame wait. Numeric readback and image acceptance
/// remain separate; this does not launch anything or select error tolerances.
/// </summary>
public static class InterpolationGpuProbe
{
	public sealed record Row( string Name, long PoseRevision, long SubmittedRevision, int DrawCalls,
		InterpolationGpuChecks.Measurement Comparison );

	public static async Task<IReadOnlyList<Row>> Run( string checkout, GltfDocument document,
		InterpolationReference reference, Sandbox.SceneWorld world, Sandbox.Material computeMaterial,
		Func<Task> waitForActualFrames, Func<int?, Sandbox.Material> materialResolver = null,
		Action<Row> completedRow = null )
	{
		ArgumentNullException.ThrowIfNull( document ); ArgumentNullException.ThrowIfNull( reference );
		ArgumentNullException.ThrowIfNull( world ); ArgumentNullException.ThrowIfNull( waitForActualFrames );
		ArgumentNullException.ThrowIfNull( computeMaterial );
		var result = new List<Row>();
		using var asset = GltfGpuAsset.Create( document, computeMaterial, materialResolver );
		using var first = asset.CreateInstance( world );
		using var second = asset.CreateInstance( world );
		second.Placement = Matrix4x4.CreateTranslation( 20, 0, 0 );
		second.SetPose( document.EvaluatePose( 8, .375f ) );
		await waitForActualFrames();
		RequireSubmitted( first ); RequireSubmitted( second );
		var stableSecond = second.Readback().Select( snapshot => (byte[])snapshot.Bytes.Clone() ).ToArray();
		var secondRevision = second.PoseRevision;
		Add( "independent_second_initial", second, 8, .375f );
		Add( "authored_rest", first, null, 0 );

		for ( var clip = 0; clip < reference.ClipCount; clip++ )
		{
			var times = reference.SampleTimes( clip ).ToArray();
			for ( var sample = 0; sample < times.Length; sample++ )
			{
				var time = times[sample];
				first.SetPose( document.EvaluatePose( clip, time ) );
				var revisionBeforeDraw = first.PoseRevision;
				await waitForActualFrames(); RequireSubmitted( first ); RequireSubmitted( second );
				if ( first.PoseRevision != revisionBeforeDraw || second.PoseRevision != secondRevision ) throw new InvalidDataException( "Rendering mutated a controlled pose revision." );
				Add( $"clip_{clip}_sample_{sample}", first, clip, time );
				var other = second.Readback();
				if ( other.Count != stableSecond.Length || other.Where( (snapshot, index) => !snapshot.Bytes.SequenceEqual( stableSecond[index] ) ).Any() ) throw new InvalidDataException( "Updating one model instance changed the other's GPU output." );
			}
		}

		var placements = new[]
		{
			Matrix4x4.CreateTranslation( 3, -4, 5 ),
			Matrix4x4.CreateScale( 2, .5f, 1.25f ) * Matrix4x4.CreateTranslation( 3, -4, 5 ),
			Matrix4x4.CreateScale( -1, 1, 1 ) * Matrix4x4.CreateTranslation( 3, -4, 5 ),
			new Matrix4x4( 2,.2f,0,0, 0,.5f,.3f,0, 0,0,-1,0, 3,-4,5,1 )
		};
		for ( var placement = 0; placement < placements.Length; placement++ )
		{
			first.Placement = placements[placement];
			for ( var clip = 0; clip < reference.ClipCount; clip++ )
			{
				first.SetPose( document.EvaluatePose( clip, .375f ) );
				await waitForActualFrames(); RequireSubmitted( first );
				Add( $"placement_{placement}_clip_{clip}", first, clip, .375f );
			}
		}
		second.Dispose();
		first.SetPose( document.EvaluatePose( 0, 0 ) );
		await waitForActualFrames(); RequireSubmitted( first );
		Add( "first_after_second_disposal", first, 0, 0 );
		if ( result.Count != 372 ) throw new InvalidDataException( "InterpolationTest GPU submission inventory changed." );
		return result;

		void Add( string name, GltfGpuInstance instance, int? clip, float time )
		{
			var row = new Row( name, instance.PoseRevision, instance.SubmittedRevision, instance.DrawCalls,
				InterpolationGpuChecks.Measure( checkout, reference, clip, time, instance.Placement, instance.Readback() ) );
			result.Add( row ); completedRow?.Invoke( row );
		}
	}

	private static void RequireSubmitted( GltfGpuInstance instance )
	{
		if ( instance.DrawCalls <= 0 || instance.SubmittedRevision != instance.PoseRevision || instance.CallbackErrors is null || instance.CallbackErrors.Length != 0 ) throw new InvalidDataException( "Current direct GPU revision was not submitted successfully: " + string.Join( ";", instance.CallbackErrors ?? Array.Empty<string>() ) );
	}
}
