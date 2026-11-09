using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;

namespace Sandbox.Gltf.DirectGpu.SimpleInstancingValidation;

using NVector = System.Numerics.Vector3;
using static SimpleInstancingReference;

/// <summary>Authored before GltfGpuInstancingPlan; 29 focused CPU plan cases, no GPU claim.</summary>
public static class SimpleInstancingExpansionTests
{
	public const int ExpectedCases = 29;
	public static SimpleInstancingCoreTests.Result[] RunAll( string checkoutRoot )
	{
		var reference = new SimpleInstancingReference( checkoutRoot ); var rows = new List<SimpleInstancingCoreTests.Result>();
		foreach ( var form in reference.Forms )
		{
			Run( "selected_node_expands_exact125_ordered_source_identities_no_base", form.Name, () =>
			{
				using var d = form.Read(); var plan = GltfGpuInstancingPlan.Create( d, 0 );
				Require( plan.Node == 0 && plan.Mesh == 0 && plan.HasExtension && plan.AppliesInstancing && plan.Instances.Count == InstanceCount && plan.Instances.Select( x => x.InstanceIndex ).SequenceEqual( Enumerable.Range( 0, InstanceCount ).Select( x => (int?)x ) ), "Selected instancing plan must keep source IDs 0..124 and omit ordinary base row." );
				Require( plan.Instances.All( x => x.Node == 0 ), "Instancing must preserve source node identity." );
			} );
			Run( "raw_instance_attribute_provenance_formats_offsets_counts", form.Name, () =>
			{
				using var d = form.Read(); Descriptors( GltfGpuInstancingPlan.Create( d, 0 ), reference );
			} );
			Run( "every_instance_local_transform_matches_independent_raw_geometry", form.Name, () =>
			{
				using var d = form.Read(); CheckAllPositions( GltfGpuInstancingPlan.Create( d, 0 ), reference );
			} );
			Run( "local_then_affine_node_then_basis_then_placement", form.Name, () =>
			{
				using var d = form.Read(); var plan = GltfGpuInstancingPlan.Create( d, 0 );
				var node = new Matrix4x4( -1.5f, .2f, 0, 0, 0, .75f, .3f, 0, .1f, 0, 1.25f, 0, 3, -5, 7, 1 );
				var basis = new Matrix4x4( 0, 1f / .0254f, 0, 0, 0, 0, 1f / .0254f, 0, 1f / .0254f, 0, 0, 0, 0, 0, 0, 1 );
				for ( var i = 0; i < InstanceCount; i++ ) for ( var v = 0; v < VertexCount; v++ )
				{
					var world = ApplyPlacement( reference.World( i, v ), node, false ); var engine = new[] { world[2] / .0254, world[0] / .0254, world[1] / .0254 }; var expected = ApplyPlacement( engine, AffinePlacement, false );
					var actual = NVector.Transform( Vertex( reference.Value( 1, v ) ), plan.Instances[i].Compose( node ) * basis * AffinePlacement ); Check( expected, actual, .0007, "full-affine instance/node/basis/placement order" );
				}
			} );
			Run( "owned_transform_rows_and_descriptor_JSON_survive_document_disposal", form.Name, () =>
			{
				GltfGpuInstancingPlan plan; using ( var d = form.Read() ) plan = GltfGpuInstancingPlan.Create( d, 0 );
				Descriptors( plan, reference ); CheckAllPositions( plan, reference );
				Require( plan.Instances is not GltfGpuInstancingRow[] && plan.Attributes is not Dictionary<string, GltfGpuInstancingAttribute>, "Plan must not expose mutable backing collections." );
			} );
		}
		var source = reference.Forms.Single( x => x.Name == "glTF/source" );
		Run( "unsupported_optional_malformed_extension_keeps_one_ordinary_row", "capability fixture", () =>
		{
			using var d = Mutate( source, root => Attributes( root )["ROTATION"] = 999, false ); var plan = GltfGpuInstancingPlan.Create( d, 0 );
			Require( plan.HasExtension && !plan.AppliesInstancing && plan.Attributes.Count == 0 && plan.Instances.Count == 1 && plan.Instances[0].InstanceIndex is null && plan.Instances[0].LocalTransform == Matrix4x4.Identity, "Unsupported optional extension is ordinary fallback, not selected extension success." );
		} );
		Run( "absent_extension_keeps_one_null_identity_row", "ordinary-node fixture", () =>
		{
			using var d = Mutate( source, root => root["nodes"][0].AsObject().Remove( "extensions" ) ); var plan = GltfGpuInstancingPlan.Create( d, 0 );
			Require( !plan.HasExtension && !plan.AppliesInstancing && plan.Attributes.Count == 0 && plan.Instances.Count == 1 && plan.Instances[0].InstanceIndex is null && plan.Instances[0].LocalTransform == Matrix4x4.Identity, "Ordinary source node identity changed." );
		} );
		Run( "ordinary_fallback_requires_numeric_index_and_mesh_object", "negative mesh fixtures", () =>
		{
			foreach ( var invalidIndex in new[] { true, false } )
			{
				try
				{
					using var d = Mutate( source, root => { root["nodes"][0].AsObject().Remove( "extensions" ); if ( invalidIndex ) root["nodes"][0]["mesh"] = "0"; else root["meshes"][0] = 7; } ); _ = GltfGpuInstancingPlan.Create( d, 0 );
				}
				catch ( GltfException e ) { Require( e.Message.Contains( "mesh", StringComparison.OrdinalIgnoreCase ), "Invalid mesh rejection needs a useful reason." ); continue; }
				throw new InvalidOperationException( "Malformed ordinary mesh silently became a valid instancing plan." );
			}
		} );
		foreach ( var omitted in new[] { "TRANSLATION", "ROTATION", "SCALE" } ) Run( "omitted_" + omitted + "_plan_default", "default fixture", () =>
		{
			using var d = Mutate( source, root => Attributes( root ).Remove( omitted ) ); var plan = GltfGpuInstancingPlan.Create( d, 0 ); Require( plan.Attributes.Count == 2 && !plan.Attributes.ContainsKey( omitted ), "Omitted attributes must not acquire invented source descriptors." ); CheckAllPositions( plan, reference, omitted );
		} );
		Run( "selected_invalid_count_rejects_instead_of_ordinary_fallback", "negative fixture", () =>
		{
			try { using var d = Mutate( source, root => root["accessors"][4]["count"] = 124 ); _ = GltfGpuInstancingPlan.Create( d, 0 ); } catch ( GltfException e ) { Require( e.Message.Contains( "count", StringComparison.OrdinalIgnoreCase ), "Useful count failure required." ); return; } throw new InvalidOperationException( "Invalid selected extension silently became an ordinary mesh." );
		} );
		Run( "custom_count_only_is125_identity_instances_not_ordinary_node", "custom attribute fixture", () =>
		{
			using var d = Mutate( source, root => { Attributes( root ).Clear(); Attributes( root )["_ID"] = 3; } ); var plan = GltfGpuInstancingPlan.Create( d, 0 );
			Require( plan.AppliesInstancing && plan.Instances.Count == InstanceCount && plan.Instances.All( x => x.InstanceIndex.HasValue && x.LocalTransform == Matrix4x4.Identity ) && plan.Attributes.Single().Key == "_ID", "Custom attribute count must preserve selected instance identity with default TRS." );
		} );
		Run( "normalized_short_interleaved_rotation_nonzero_offsets_preserved", "synthetic legal source format", () =>
		{
			var bytes = new byte[40]; System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian( bytes.AsSpan( 14 ), 32767 ); System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian( bytes.AsSpan( 28 ), 32767 );
			using var d = Mutate( source, root =>
			{
				Attributes( root ).Clear(); Attributes( root )["ROTATION"] = 4;
				root["buffers"].AsArray().Add( new JsonObject { ["uri"] = "fixture.bin", ["byteLength"] = 40 } );
				root["bufferViews"].AsArray().Add( new JsonObject { ["buffer"] = 1, ["byteOffset"] = 4, ["byteLength"] = 36, ["byteStride"] = 16 } );
				root["accessors"][4] = new JsonObject { ["bufferView"] = 6, ["byteOffset"] = 4, ["componentType"] = 5122, ["normalized"] = true, ["type"] = "VEC4", ["count"] = 2 };
			}, true, uri => uri == "fixture.bin" ? bytes : source.Resolve( uri ) );
			var plan = GltfGpuInstancingPlan.Create( d, 0 ); var a = plan.Attributes["ROTATION"];
			Require( a.AccessorIndex == 4 && a.BufferViewIndex == 6 && a.BufferIndex == 1 && a.BufferViewByteOffset == 4 && a.AccessorByteOffset == 4 && a.ByteStride == 16 && a.ComponentType == 5122 && a.Components == 4 && a.Count == 2 && a.Normalized, "Original signed-normalized interleaved descriptors were guessed as packed FLOAT." );
			Require( a.Accessor.GetProperty( "normalized" ).GetBoolean() && a.BufferView.Value.GetProperty( "byteStride" ).GetInt32() == 16 && plan.Instances.Count == 2, "Original JSON descriptor or count lost." );
			Check( new double[] { 1, 0, 0 }, NVector.Transform( NVector.UnitX, plan.Instances[0].LocalTransform ), 0, "identity quaternion" ); Check( new double[] { -1, 0, 0 }, NVector.Transform( NVector.UnitX, plan.Instances[1].LocalTransform ), 0, "Z half turn" );
		} );
		Require( rows.Count == ExpectedCases, "Focused expansion inventory changed." ); return rows.ToArray();
		void Run( string name, string form, Action body ) { try { body(); rows.Add( new( name, form, true, null ) ); } catch ( Exception error ) { rows.Add( new( name, form, false, error.ToString() ) ); } }
	}
	private static void Descriptors( GltfGpuInstancingPlan plan, SimpleInstancingReference reference )
	{
		Require( plan.Attributes.Count == 3, "All three original source instance attribute descriptors required." );
		foreach ( var (semantic, index) in new[] { ("TRANSLATION", 3), ("ROTATION", 4), ("SCALE", 5) } )
		{
			var a = plan.Attributes[semantic]; var expected = reference.Accessor( index );
			Require( a.Semantic == semantic && a.AccessorIndex == index && a.BufferViewIndex == index && a.BufferIndex == 0 && a.BufferViewByteOffset == expected.GetProperty( "view_offset" ).GetInt32() && a.AccessorByteOffset == 0 && a.ByteStride == expected.GetProperty( "stride" ).GetInt32() && a.ComponentType == 5126 && a.Components == expected.GetProperty( "components" ).GetInt32() && a.Count == InstanceCount && !a.Normalized, "Original instance source representation differs: " + semantic );
			Require( a.Accessor.GetProperty( "bufferView" ).GetInt32() == index && a.BufferView.Value.GetProperty( "byteOffset" ).GetInt32() == a.BufferViewByteOffset, "Cloned source descriptor differs." );
		}
	}
	private static void CheckAllPositions( GltfGpuInstancingPlan plan, SimpleInstancingReference reference, string omitted = null )
	{
		Require( plan.Instances.Count == InstanceCount, "All125 rows required." );
		for ( var i = 0; i < InstanceCount; i++ ) for ( var v = 0; v < VertexCount; v++ ) Check( reference.World( i, v, false, omitted ), NVector.Transform( Vertex( reference.Value( 1, v ) ), plan.Instances[i].LocalTransform ), 4e-6, $"source instance {i} vertex {v}" );
	}
	private static JsonObject Attributes( JsonNode root ) => root["nodes"][0]["extensions"][Extension]["attributes"].AsObject();
	private static GltfDocument Mutate( Form source, Action<JsonNode> edit, bool selected = true, Func<string, byte[]> resolve = null ) { var root = JsonNode.Parse( source.Bytes ); edit( root ); return GltfReader.Read( Encoding.UTF8.GetBytes( root.ToJsonString() ), resolve ?? source.Resolve, selected ? Selected : null ); }
	private static NVector Vertex( double[] p ) => new( (float)p[0], (float)p[1], (float)p[2] );
	private static void Check( double[] expected, NVector actual, double tolerance, string label ) { Close( expected[0], actual.X, tolerance, label + " X" ); Close( expected[1], actual.Y, tolerance, label + " Y" ); Close( expected[2], actual.Z, tolerance, label + " Z" ); }
}
