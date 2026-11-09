using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sandbox.Gltf.DirectGpu.MorphPrimitivesValidation;

using static MorphPrimitivesReference;

/// <summary>CPU/data assertions only; no native model, mesh or GPU allocation.</summary>
public static class MorphPrimitivesCoreTests
{
	public sealed record Result( string Name, string Form, bool Passed, string Error );
	public const int ExpectedCases = 55;
	public static Result[] RunAll( string checkoutRoot )
	{
		var reference = new MorphPrimitivesReference( checkoutRoot ); var rows = new List<Result>();
		foreach ( var form in reference.Forms )
		{
			Run( "actual_two_primitive_target_material_default_inventory", () => Inventory( form ) );
			Run( "all_base_upload_bytes_formats_offsets_stride_are_variant_local", () => Uploads( form ) );
			Run( "both_position_only_targets_have_distinct_exact_source_views", () => Morphs( form ) );
			Run( "mesh_default_half_strength_elevates_both_primitives", () => Poses( form, .5f ) );
			Run( "signed_and_over_one_node_weights_override_mesh_defaults", () => { foreach ( var weight in Weights ) using ( var d = form.Control( weight ) ) CheckPose( form, d, weight ); } );
			Run( "missing_mesh_and_node_weights_default_to_zero", () => { using var d = form.Control( zeroFallback: true ); CheckPose( form, d, 0 ); } );
			Run( "morph_occurs_before_full_affine_node_transform", () => { using var d = form.Control( -.75f, affine: true ); CheckPose( form, d, -.75f, true ); } );
			Run( "morph_upload_exact_budget_ownership_and_omitted_semantics", () => Ownership( form ) );
			void Run( string name, Action body ) => RunCase( rows, name, form.Name, body );
		}
		var canonical = reference.Forms.Single( x => x.Name == "glTF/source" );
		RunCase( rows, "unequal_target_counts_across_primitives_rejected", "derived_invalid_core", () => MutatedReject( canonical, r => r["meshes"][0]["primitives"][1].AsObject().Remove( "targets" ), d => d.EvaluatePose() ) );
		RunCase( rows, "primitive_local_wrong_target_vertex_count_rejected", "derived_invalid_core", () => MutatedReject( canonical, r => r["accessors"][9]["count"] = 8, d => d.MaterializeMorphTargetsUpload( 0, 1 ) ) );
		RunCase( rows, "node_override_wrong_target_count_rejected", "derived_invalid_core", () => MutatedReject( canonical, r => r["nodes"][1]["weights"] = new JsonArray( 0, 1 ), d => d.EvaluatePose() ) );
		RunCase( rows, "morph_semantic_without_corresponding_base_rejected", "derived_invalid_core", () => MutatedReject( canonical, r => r["meshes"][0]["primitives"][1]["targets"][0]["TANGENT"] = 9, d => d.MaterializeMorphTargetsUpload( 0, 1 ) ) );
		RunCase( rows, "required_draco_has_clear_unsupported_error", "glTF-Draco/source", () =>
		{
			var draco = reference.Forms.Single( x => x.Name == "glTF-Draco/source" ); ExpectReject( () => { using var d = GltfReader.Read( draco.Bytes, draco.Resolve ); }, "KHR_draco_mesh_compression" );
		} );
		RunCase( rows, "derived_animation_keeps_signed_weights_and_shared_target_identity", "derived_signed_animation", () =>
		{
			using var d = canonical.Control( animated: true );
			foreach ( var pair in new[] { (Time: 0f, Weight: -.75f), (Time: .5f, Weight: -.125f), (Time: 1f, Weight: .5f), (Time: 2f, Weight: 1.5f) } ) CheckPose( canonical, d, pair.Weight, pose: d.EvaluatePose( 0, pair.Time ) );
		} );
		RunCase( rows, "reference_detects_wrong_primitive_offset_ignored_weight_and_post_transform_delta", "oracle_sensitivity", () =>
		{
			var vertex = Enumerable.Range( 0, 9 ).First( v => Primitive( canonical, 1 ).GetProperty( "morph" ).GetProperty( "values" )[v][1].GetSingle() > .1f );
			Require( System.Numerics.Vector3.Distance( Position( canonical, 1, vertex, .5f ), Position( canonical, 1, vertex, 0 ) ) > .09f, "Control cannot expose omitted morph." );
			Require( System.Numerics.Vector3.Distance( Position( canonical, 1, vertex, -.75f ), Position( canonical, 1, vertex, 0 ) ) > .14f, "Control cannot expose signed clamp." );
			var correct = Position( canonical, 1, vertex, .5f, true ); var wrong = Position( canonical, 1, vertex, 0, true ) + Vector( Primitive( canonical, 1 ).GetProperty( "morph" ).GetProperty( "values" )[vertex] ) * .5f;
			Require( System.Numerics.Vector3.Distance( correct, wrong ) > .1f, "Control cannot expose morph-after-transform." );
		} );
		Require( rows.Count == ExpectedCases, "Frozen CPU case inventory changed." ); return rows.ToArray();
	}
	private static void Inventory( Form form )
	{
		using var d = form.Read(); var r = d.Root;
		Require( d.GetDefaultScene() == 0 && r.GetProperty( "nodes" ).GetArrayLength() == 2 && r.GetProperty( "meshes" ).GetArrayLength() == 1 && r.GetProperty( "meshes" )[0].GetProperty( "primitives" ).GetArrayLength() == 2, "All two primitives must remain applicable." );
		Require( !r.TryGetProperty( "animations", out _ ) && !r.TryGetProperty( "skins", out _ ), "Actual sample is static morph-default evidence, not animation/skinning." );
		Require( d.EvaluatePose().GetMorphWeights( 1 ).SequenceEqual( new[] { .5f } ), "Authored mesh default was omitted." );
		for ( var p = 0; p < 2; p++ ) { var geometry = d.ReadPrimitive( 0, p ); Require( geometry.Mode == 4 && geometry.VertexCount == Count( p ) && geometry.Indices.Length == (p == 0 ? 72 : 24) && geometry.Material == p, "Primitive counts, topology or red/green material binding changed." ); }
	}
	internal static void Uploads( Form form )
	{
		using var d = form.Read();
		for ( var p = 0; p < 2; p++ )
		{
			var upload = d.MaterializePrimitiveUpload( 0, p ); var expected = Primitive( form, p );
			Require( upload.Layout.VertexCount == Count( p ) && upload.Attributes.Keys.Order().SequenceEqual( new[] { "NORMAL", "POSITION", "TEXCOORD_0" } ) && !upload.Layout.RequiresGeneratedIndices && !upload.Layout.RequiresTopologyExpansion, "Actual source is indexed triangles with three attributes." );
			foreach ( var pair in upload.Attributes ) CheckStream( pair.Value, expected.GetProperty( "attributes" ).GetProperty( pair.Key ) );
			Require( upload.Indices.Count == (p == 0 ? 72 : 24) && upload.Indices.ComponentType == 5123 && !upload.Indices.Normalized, "Index representation changed." );
			for ( var i = 0; i < upload.Indices.Count; i++ ) Require( BinaryPrimitives.ReadUInt16LittleEndian( upload.Indices.Storage.Bytes.Span.Slice( upload.Indices.ByteOffset + i * upload.Indices.ByteStride, 2 ) ) == Indices( form, p )[i], "Primitive-local GPU index staging changed." );
		}
	}
	internal static void CheckStream( GltfUploadStream actual, JsonElement expected )
	{
		Require( actual.Binding.AccessorIndex == expected.GetProperty( "accessor" ).GetInt32() && actual.Count == expected.GetProperty( "count" ).GetInt32() && actual.ComponentType == expected.GetProperty( "componentType" ).GetInt32() && actual.Components == expected.GetProperty( "components" ).GetInt32() && actual.Normalized == expected.GetProperty( "normalized" ).GetBoolean(), "Source provenance/component representation changed." );
		Require( actual.ByteOffset == expected.GetProperty( "offset" ).GetInt32() && actual.ByteStride == expected.GetProperty( "stride" ).GetInt32(), "Actual accessor offset/stride changed." );
		Require( actual.Storage.Bytes.Length == expected.GetProperty( "viewLength" ).GetInt32() && Hash( actual.Storage.Bytes.Span ) == expected.GetProperty( "viewSha256" ).GetString(), "Owned bytes differ from independently decoded source." );
		var draco = expected.TryGetProperty( "materializedDraco", out var flag ) && flag.GetBoolean(); Require( actual.IsMaterialized == draco, "Codec materialization provenance changed." );
		if ( !draco ) Require( actual.Binding.SourceBufferView == expected.GetProperty( "view" ).GetInt32() && actual.Binding.ViewByteOffset == expected.GetProperty( "viewOffset" ).GetInt32(), "Buffer view origin was mixed between primitives." );
	}
	private static void Morphs( Form form )
	{
		using var d = form.Read();
		for ( var p = 0; p < 2; p++ )
		{
			var u = d.MaterializeMorphTargetsUpload( 0, p ); Require( u.VertexCount == Count( p ) && u.Targets.Count == 1 && u.Targets[0].Keys.SequenceEqual( new[] { "POSITION" } ), "POSITION-only per-primitive morph contract changed." );
			CheckStream( u.Targets[0]["POSITION"], Primitive( form, p ).GetProperty( "morph" ) );
			var plan = GltfGpuPrimitivePlan.Create( d, 0, p ); var descriptor = plan.InputDescriptors[plan.BaseAttributeCount];
			Require( plan.MorphTargetCount == 1 && descriptor.Target == 0 && descriptor.Role == 0 && descriptor.ByteStride == u.Targets[0]["POSITION"].ByteStride, "Primitive-local target identity or stride changed." );
		}
	}
	private static void Poses( Form form, float weight ) { using var d = form.Read(); CheckPose( form, d, weight ); }
	private static void CheckPose( Form form, GltfDocument d, float weight, bool affine = false, GltfPose pose = null )
	{
		pose ??= d.EvaluatePose(); Require( pose.GetMorphWeights( 1 ).SequenceEqual( new[] { weight } ), "Mesh/node/animation weight precedence changed or values clamped." );
		for ( var p = 0; p < 2; p++ ) for ( var v = 0; v < Count( p ); v++ )
		{
			var expected = Position( form, p, v, weight, affine ); var actual = d.EvaluateVertexPosition( 1, p, v, pose );
			Close( expected.X, actual.X, 2e-6f, "X" ); Close( expected.Y, actual.Y, 2e-6f, "Y" ); Close( expected.Z, actual.Z, 2e-6f, "Z" );
		}
	}
	private static void Ownership( Form form )
	{
		for ( var p = 0; p < 2; p++ )
		{
			GltfMorphTargetsUploadData u; using ( var d = form.Read() )
			{
				u = d.MaterializeMorphTargetsUpload( 0, p ); Require( u.OwnedBytes == Count( p ) * 12 && u.Targets[0].Count == 1, "Missing target NORMAL/UV must remain omitted; owned budget counts exact bytes." );
				Require( d.MaterializeMorphTargetsUpload( 0, p, u.OwnedBytes ).OwnedBytes == u.OwnedBytes, "Exact morph budget refused." ); ExpectReject( () => d.MaterializeMorphTargetsUpload( 0, p, u.OwnedBytes - 1 ), "budget" );
			}
			CheckStream( u.Targets[0]["POSITION"], Primitive( form, p ).GetProperty( "morph" ) );
		}
	}
	private static void MutatedReject( Form form, Action<JsonObject> change, Action<GltfDocument> action )
	{
		var root = SourceJson( form.Source, form.Resolve ); change( root ); ExpectReject( () => { using var d = GltfReader.Read( JsonSerializer.SerializeToUtf8Bytes( root ), form.Resolve, Selected ); action( d ); } );
	}
	internal static void ExpectReject( Action body, string reason = null )
	{
		try { body(); } catch ( GltfException error ) { Require( reason is null || error.Message.Contains( reason, StringComparison.OrdinalIgnoreCase ), "Rejection lacks precise reason: " + error.Message ); return; }
		throw new InvalidOperationException( "Invalid/unsupported control was silently accepted." );
	}
	internal static void RunCase( List<Result> rows, string name, string form, Action body ) { try { body(); rows.Add( new( name, form, true, null ) ); } catch ( Exception error ) { rows.Add( new( name, form, false, error.ToString() ) ); } }
}
