using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Sandbox.Gltf.DirectGpu;

using NMatrix = System.Numerics.Matrix4x4;

/// <summary>Actual engine probes; the coordinator supplies the existing world and frame fence.</summary>
internal static class GltfGpuInstanceStateTests
{
	public static async Task<object> PlacementFailureKeepsLastValidState( SceneWorld world, Material borrowedCompute, Func<Task> waitForSubmission )
	{
		using var document = GltfGpuBackendContractTests.InterleavedTriangle();
		using var asset = GltfGpuAsset.Create( document, borrowedCompute );
		using var instance = asset.CreateInstance( world );
		await waitForSubmission();
		var before = instance.Readback().Single(); var beforePlacement = instance.Placement;
		var beforeRevision = instance.PoseRevision; var beforeBounds = instance.SceneObject.Bounds;
		var rejected = false;
		try { instance.Placement = NMatrix.CreateScale( float.MaxValue ); }
		catch ( GltfException ) { rejected = true; }
		Require( rejected, "Finite placement whose transformed positions overflow must fail before publication." );
		Require( instance.Placement == beforePlacement && instance.PoseRevision == beforeRevision,
			"Rejected placement replaced the last valid property or pose revision." );
		Require( instance.SceneObject.Bounds == beforeBounds, "Rejected placement replaced the valid culling bounds." );
		await waitForSubmission();
		var after = instance.Readback().Single();
		Require( after.Bytes.SequenceEqual( before.Bytes ) && after.Indices.SequenceEqual( before.Indices ), "Rejected placement changed bound GPU output/index data." );
		Require( instance.SubmittedRevision == beforeRevision && instance.CallbackErrors.Length == 0 && after.DrawCalls > before.DrawCalls,
			"The surviving pose did not keep submitting after rejection." );
		return new { Status = "passed", beforeRevision, instance.PoseRevision, after.DrawCalls, after.SubmittedRevision };
	}

	public static async Task<object> RetainedInstanceSurvivesOwnerAndSiblingRelease( SceneWorld world, Func<Task> waitForSubmission )
	{
		using var document = GltfGpuBackendContractTests.InterleavedTriangle(); var finalReleases = 0;
		// Exercise the staged compiled resource path; no native Model or Mesh is
		// created for this test.
		var asset = GltfGpuAsset.Prepare( document, GltfGpuAsset.DefaultComputeShaderPath, finalRelease: () => finalReleases++ );
		using var assetScope = asset;
		var cpu = CaptureCpuGeneration( asset );
		using var first = asset.CreateInstance( world ); using var second = asset.CreateInstance( world );
		second.Placement = NMatrix.CreateTranslation( 0, 100, 0 );
		await waitForSubmission(); var before = second.Readback().Single();
		asset.Dispose(); first.Dispose();
		Require( !asset.IsValid && finalReleases == 0, "Resource owner release destroyed a live renderer generation." );
		GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
		Require( IsAlive( cpu.Arena ) && IsAlive( cpu.Rig ), "A live instance lost its legitimately retained source generation." );
		var rejected = false; try { _ = asset.CreateInstance( world ); } catch ( ObjectDisposedException ) { rejected = true; }
		Require( rejected, "Released owner accepted a new instance." );
		second.SetPose( document.EvaluatePose() ); await waitForSubmission();
		var after = second.Readback().Single();
		Require( after.Bytes.SequenceEqual( before.Bytes ) && after.DrawCalls > before.DrawCalls && second.CallbackErrors.Length == 0,
			"Retained instance stopped drawing after sibling/owner release." );
		second.Dispose(); Require( finalReleases == 1, "Final generation release was not exactly once." );
		Require( !asset.HasGpuResources, "Retired asset still reports owned GPU resources." );
		GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
		Require( !IsAlive( cpu.Arena ) && !IsAlive( cpu.Rig ), "Retained invalid owner or disposed instances still hold retired CPU geometry." );
		GC.KeepAlive( asset ); GC.KeepAlive( first ); GC.KeepAlive( second );
		return new { Status = "passed", finalReleases, after.DrawCalls, after.SubmittedRevision };
	}

	[MethodImpl( MethodImplOptions.NoInlining )]
	private static (WeakReference<byte[]> Arena, WeakReference<GltfGpuRig> Rig) CaptureCpuGeneration( GltfGpuAsset asset )
		=> (new( asset.Primitives[0].Plan.InputBytes ), new( asset.Nodes[0].Rig ));
	[MethodImpl( MethodImplOptions.NoInlining )]
	private static bool IsAlive<T>( WeakReference<T> value ) where T : class => value.TryGetTarget( out _ );

	private static void Require( bool condition, string message ) { if ( !condition ) throw new InvalidOperationException( message ); }
}
