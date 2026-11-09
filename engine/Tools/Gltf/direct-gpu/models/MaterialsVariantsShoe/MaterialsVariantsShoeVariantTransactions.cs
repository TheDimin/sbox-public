using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Sandbox.Gltf.DirectGpu.MaterialsVariantsShoeValidation;

using static MaterialsVariantsShoeReference;

/// <summary>
/// Additive proposed selection-transaction tests. Not part of the original 59-case
/// freeze. Observation must come from the exact material passed to a completed
/// draw's SetRenderState, never reconstructed from ActiveVariant or the reader.
/// </summary>
public static class MaterialsVariantsShoeVariantTransactions
{
	public sealed record Binding( int Node, int Mesh, int Primitive, int? MaterialIndex,
		object MaterialIdentity, string Origin, long Frame, long SelectionRevision );
	public sealed record Result( string Name, bool Passed, string Error );
	private static readonly (int Node, int Mesh)[] Slots = { (0, 0), (3, 1), (4, 2) };

	public static void AssertBindings( IReadOnlyList<Binding> actual, IReadOnlyList<int> expectedMaterialIndices,
		IReadOnlyDictionary<int, object> expectedIdentities, long afterFrame )
	{
		Require( actual.Count == Slots.Length && expectedMaterialIndices.Count == Slots.Length, "Expected exactly the three stable primitive slots." );
		Require( actual.Select( x => x.SelectionRevision ).Distinct().Count() == 1 && actual.All( x => x.SelectionRevision >= 0 ), "A completed draw mixed two selection revisions or has no committed revision." );
		for ( var i = 0; i < Slots.Length; i++ )
		{
			var slot = Slots[i]; var row = actual.Single( x => x.Node == slot.Node && x.Mesh == slot.Mesh && x.Primitive == 0 ); var expected = expectedMaterialIndices[i];
			Require( row.Origin == "draw-submission" && row.Frame > afterFrame, "Material observation is stale or reconstructed selection metadata." );
			Require( row.MaterialIndex == expected && ReferenceEquals( row.MaterialIdentity, expectedIdentities[expected] ), "Actual submitted material differs from independent literal mapping." );
		}
	}

	/// <summary>Checks the three-slot synthetic fixture against literal standard mappings.</summary>
	public static Result[] RunFixtureCases( string checkoutRoot )
	{
		var inputs = new Inputs( checkoutRoot ); using var document = inputs.ReadFixture( MakeThreeSlotFixture ); var result = new List<Result>();
		foreach ( var (variant, expected) in new[] { ((int?)null, new[] { 0, 0, 0 }), ((int?)0, new[] { 1, 2, 0 }), ((int?)1, new[] { 0, 0, 0 }), ((int?)2, new[] { 2, 1, 0 }) } )
		{
			try { for ( var mesh = 0; mesh < 3; mesh++ ) Require( document.GetMaterialForVariant( mesh, 0, variant ) == expected[mesh], "Synthetic fixture is not the intended independent mapping matrix." ); result.Add( new( "three_slot_fixture_variant_" + (variant?.ToString() ?? "none"), true, null ) ); }
			catch ( Exception error ) { result.Add( new( "three_slot_fixture_variant_" + (variant?.ToString() ?? "none"), false, error.ToString() ) ); }
		}
		return result.ToArray();
	}

	/// <summary>Checker controls only; object tokens are deliberately not native materials.</summary>
	public static Result[] RunVerifierControls()
	{
		var identities = new Dictionary<int, object> { [0] = new(), [1] = new(), [2] = new() };
		var rows = Slots.Select( (x, i) => new Binding( x.Node, x.Mesh, 0, new[] { 1, 2, 0 }[i], identities[new[] { 1, 2, 0 }[i]], "draw-submission", 12, 4 ) ).ToArray();
		var result = new List<Result>();
		Run( "binding_checker_accepts_three_literal_slots", () => AssertBindings( rows, new[] { 1, 2, 0 }, identities, 11 ) );
		Run( "binding_checker_rejects_material_index_alias", () => Reject( () => AssertBindings( rows.Select( x => x.Node == 0 ? x with { MaterialIndex = 0 } : x ).ToArray(), new[] { 1, 2, 0 }, identities, 11 ) ) );
		Run( "binding_checker_rejects_wrong_actual_material", () => Reject( () => AssertBindings( rows.Select( x => x.Node == 0 ? x with { MaterialIdentity = identities[0] } : x ).ToArray(), new[] { 1, 2, 0 }, identities, 11 ) ) );
		Run( "binding_checker_rejects_partial_revision_commit", () => Reject( () => AssertBindings( rows.Select( x => x.Node == 0 ? x with { SelectionRevision = 3 } : x ).ToArray(), new[] { 1, 2, 0 }, identities, 11 ) ) );
		Run( "binding_checker_rejects_stale_frame", () => Reject( () => AssertBindings( rows, new[] { 1, 2, 0 }, identities, 12 ) ) );
		Run( "binding_checker_rejects_reader_resolution_as_draw_proof", () => Reject( () => AssertBindings( rows.Select( x => x with { Origin = "reader-resolution" } ).ToArray(), new[] { 1, 2, 0 }, identities, 11 ) ) );
		return result.ToArray();
		void Run( string name, Action body ) { try { body(); result.Add( new( name, true, null ) ); } catch ( Exception error ) { result.Add( new( name, false, error.ToString() ) ); } }
		void Reject( Action body ) { try { body(); } catch ( InvalidOperationException ) { return; } throw new Exception( "Broken binding observation was accepted." ); }
	}

	/// <summary>
	/// Eight sequential real-runtime checks. Root must approve the lazy transactional
	/// resolver design and the coordinator must provide audited completed-draw
	/// observations before running this probe. Missing APIs stop before allocation.
	/// </summary>
	public static async Task<IReadOnlyList<Result>> RunBorrowedRuntime( string checkoutRoot, SceneWorld world, Material computeMaterial,
		IReadOnlyDictionary<int, Material> diagnostics, Func<Task> waitForActualFrames,
		Func<GltfGpuInstance, IReadOnlyList<Binding>> observeCompletedBindings )
	{
		var setter = typeof( GltfGpuInstance ).GetMethod( "SetVariant", new[] { typeof( int? ) } );
		var active = typeof( GltfGpuInstance ).GetProperty( "ActiveVariant" );
		Require( setter?.ReturnType == typeof( void ) && active?.PropertyType == typeof( int? ), "Known pending SetVariant/ActiveVariant API; no shared backend handoff yet." );
		Require( diagnostics.Count == 3 && Enumerable.Range( 0, 3 ).All( x => diagnostics.TryGetValue( x, out var value ) && value is { IsValid: true, IsError: false } ), "Supply three distinct valid borrowed diagnostic materials." );
		Require( diagnostics.Values.Distinct( ReferenceEqualityComparer.Instance ).Count() == 3, "Diagnostics must expose a wrong material identity." );
		var inputs = new Inputs( checkoutRoot ); var counts = new int[3]; var injected = new InvalidOperationException( "injected material-2 acquisition failure" ); var fail = false;
		using var document = inputs.ReadFixture( MakeThreeSlotFixture );
		var identities = diagnostics.ToDictionary( x => x.Key, x => (object)x.Value );
		using var asset = GltfGpuAsset.Create( document, computeMaterial, index =>
		{
			Require( index is >= 0 and < 3, "Resolver received a material outside the selected scene's finite set." );
			counts[index.Value]++; if ( index == 2 && fail ) { fail = false; throw injected; } return diagnostics[index.Value];
		} );
		using var a = asset.CreateInstance( world ); using var b = asset.CreateInstance( world );
		b.Placement = System.Numerics.Matrix4x4.CreateTranslation( 0, 20, 0 );
		var result = new List<Result>(); var frames = new Dictionary<GltfGpuInstance, long> { [a] = -1, [b] = -1 };
		await waitForActualFrames();
		var geometryA = Geometry( a ); var geometryB = Geometry( b ); var poseA = a.PoseRevision; var poseB = b.PoseRevision;
		await Check( "new_instances_use_shared_borrowed_default_once", async () => { Require( Active( a ) is null && Active( b ) is null && counts.SequenceEqual( new[] { 1, 0, 0 } ), "Creation eagerly resolved variants or duplicated default acquisition." ); Observe( a, new[] { 0, 0, 0 } ); Observe( b, new[] { 0, 0, 0 } ); await Task.CompletedTask; } );
		await Check( "failed_late_material_resolution_preserves_whole_previous_selection", async () =>
		{
			fail = true; try { Set( a, 0 ); throw new Exception( "Injected resolver failure was not observed." ); } catch ( Exception error ) when ( CausedBy( error, injected ) ) { }
			await waitForActualFrames(); Require( Active( a ) is null && Active( b ) is null, "Failed preflight published active state." ); Observe( a, new[] { 0, 0, 0 } ); Observe( b, new[] { 0, 0, 0 } ); Unchanged();
		} );
		await Check( "retry_commits_both_mapped_slots_and_keeps_unmapped_slot", async () =>
		{
			Set( a, 0 ); await waitForActualFrames(); Require( Active( a ) == 0 && Active( b ) is null, "Selection leaked between instances." ); Observe( a, new[] { 1, 2, 0 } ); Observe( b, new[] { 0, 0, 0 } );
			Require( counts.SequenceEqual( new[] { 1, 2, 2 } ), "Failed transaction cache entries were published, or successful material acquisition duplicated." ); Unchanged();
		} );
		await Check( "repeated_selection_reuses_finite_asset_material_cache", async () => { var before = counts.ToArray(); Set( a, 0 ); Set( a, 0 ); await waitForActualFrames(); Observe( a, new[] { 1, 2, 0 } ); Require( counts.SequenceEqual( before ), "Repeated selection re-acquired cached resources." ); Unchanged(); } );
		await Check( "second_instance_selects_different_material_combination", async () => { Set( b, 2 ); await waitForActualFrames(); Observe( a, new[] { 1, 2, 0 } ); Observe( b, new[] { 2, 1, 0 } ); Require( Active( a ) == 0 && Active( b ) == 2, "Active state is asset-global." ); Unchanged(); } );
		await Check( "unmapped_then_null_selection_restore_primitive_fallback", async () => { Set( a, 1 ); await waitForActualFrames(); Observe( a, new[] { 0, 0, 0 } ); Require( Active( a ) == 1, "Valid unmapped selection must retain its active variant index." ); Set( a, null ); await waitForActualFrames(); Observe( a, new[] { 0, 0, 0 } ); Observe( b, new[] { 2, 1, 0 } ); Require( Active( a ) is null && Active( b ) == 2, "Null reset changed another instance." ); Unchanged(); } );
		await Check( "invalid_index_does_not_resolve_or_publish", async () =>
		{
			var before = counts.ToArray(); foreach ( var index in new[] { -1, 3 } ) { try { Set( a, index ); throw new Exception( "Invalid variant index was accepted." ); } catch ( GltfException ) { } }
			await waitForActualFrames(); Observe( a, new[] { 0, 0, 0 } ); Observe( b, new[] { 2, 1, 0 } ); Require( Active( a ) is null && counts.SequenceEqual( before ), "Invalid request touched material cache or selection." ); Unchanged();
		} );
		await Check( "instance_and_asset_disposal_never_destroy_borrowed_materials", async () => { b.Dispose(); Set( a, 2 ); await waitForActualFrames(); Observe( a, new[] { 2, 1, 0 } ); a.Dispose(); asset.Dispose(); a.Dispose(); asset.Dispose(); Require( diagnostics.Values.All( x => x.IsValid && !x.IsError ), "Borrowed diagnostics were destroyed during rollback or disposal." ); } );
		return result.AsReadOnly();
		void Set( GltfGpuInstance instance, int? index ) { try { setter.Invoke( instance, new object[] { index } ); } catch ( TargetInvocationException error ) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture( error.InnerException ?? error ).Throw(); } }
		int? Active( GltfGpuInstance instance ) => (int?)active.GetValue( instance );
		void Observe( GltfGpuInstance instance, int[] expected ) { Require( instance.CallbackErrors.Length == 0 && instance.DrawCalls > 0 && instance.SubmittedRevision == instance.PoseRevision, "No successful current geometry submission." ); var rows = observeCompletedBindings( instance ); AssertBindings( rows, expected, identities, frames[instance] ); frames[instance] = rows.Max( x => x.Frame ); }
		byte[][] Geometry( GltfGpuInstance instance ) => instance.Readback().Select( x => x.Bytes.ToArray() ).ToArray();
		void Unchanged() { Require( a.PoseRevision == poseA && b.PoseRevision == poseB, "Material selection dirtied pose/deformation state." ); var observedA = Geometry( a ); var observedB = Geometry( b ); Require( observedA.Length == geometryA.Length && observedB.Length == geometryB.Length && observedA.Zip( geometryA, (x, y) => x.SequenceEqual( y ) ).All( x => x ) && observedB.Zip( geometryB, (x, y) => x.SequenceEqual( y ) ).All( x => x ), "Material selection changed geometry." ); }
		async Task Check( string name, Func<Task> body ) { try { await body(); result.Add( new( name, true, null ) ); } catch ( Exception error ) { result.Add( new( name, false, error.ToString() ) ); } }
	}
	private static bool CausedBy( Exception error, Exception cause ) { for ( var current = error; current is not null; current = current.InnerException ) if ( ReferenceEquals( current, cause ) ) return true; return false; }

	public static void MakeThreeSlotFixture( JsonNode root )
	{
		var mesh = root["meshes"][0]; var first = mesh["primitives"][0];
		first["extensions"][Extension]["mappings"] = JsonNode.Parse( "[{\"material\":1,\"variants\":[0]},{\"material\":2,\"variants\":[2]}]" );
		var second = mesh.DeepClone(); second["primitives"][0]["extensions"][Extension]["mappings"] = JsonNode.Parse( "[{\"material\":2,\"variants\":[0]},{\"material\":1,\"variants\":[2]}]" );
		var third = mesh.DeepClone(); third["primitives"][0].AsObject().Remove( "extensions" );
		root["meshes"].AsArray().Add( second ); root["meshes"].AsArray().Add( third );
		root["nodes"].AsArray().Add( JsonNode.Parse( "{\"mesh\":1,\"translation\":[1.5,0,0]}" ) );
		root["nodes"].AsArray().Add( JsonNode.Parse( "{\"mesh\":2,\"translation\":[3,0,0]}" ) );
		root["nodes"][1]["children"] = new JsonArray( 0, 3, 4 );
	}
}
