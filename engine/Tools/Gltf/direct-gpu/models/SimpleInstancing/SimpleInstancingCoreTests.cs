using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sandbox.Gltf.DirectGpu.SimpleInstancingValidation;

using NVector = System.Numerics.Vector3;
using static SimpleInstancingReference;

/// <summary>40 CPU/data cases. Extension rejection is never a successful rendering case.</summary>
public static class SimpleInstancingCoreTests
{
	public const int ExpectedCases = 40;
	public sealed record Result( string Name, string Form, bool Passed, string Error );
	public static Result[] RunAll( string checkoutRoot )
	{
		var reference = new SimpleInstancingReference( checkoutRoot ); var rows = new List<Result>();
		foreach ( var form in reference.Forms )
		{
			Run( "source_descriptors_all_six_actual_formats_offsets_normalization", form.Name, () => Inventory( form, reference ) );
			Run( "every_accessor_value_matches_independent_raw_oracle", form.Name, () => Accessors( form, reference ) );
			Run( "all125_instance_transforms_and_identity_node", form.Name, () => { using var d = form.Read(); Transforms( d, reference ); } );
			Run( "literal_cube_winding_and_absent_default_material_binding", form.Name, () => Geometry( form, reference ) );
			Run( "owned_upload_preserves_actual_descriptors_view_bytes", form.Name, () => { using var d = form.Read(); Upload( d.MaterializePrimitiveUpload( 0, 0 ), reference ); } );
			Run( "owned_upload_lifetime_and_exact_budget", form.Name, () => Ownership( form, reference ) );
			Run( "portable_package_preserves_extension_no_external_bin", form.Name, () => Packaging( form ) );
		}
		var source = reference.Forms.Single( f => f.Name == "glTF/source" );
		foreach ( var omitted in new[] { "TRANSLATION", "ROTATION", "SCALE" } )
			Run( "omitted_" + omitted + "_uses_standard_default", "mutated source fixture", () => { using var d = Mutate( source, root => Attributes( root ).Remove( omitted ) ); Transforms( d, reference, omitted ); } );
		Run( "all_TRS_omitted_custom_attribute_supplies125_identity_instances", "mutated source fixture", () =>
		{
			using var d = Mutate( source, root => { var a = Attributes( root ); a.Clear(); a["_ID"] = 3; } );
			Transforms( d, reference, "ALL" );
		} );
		Run( "absent_extension_is_distinct_from125_identity_instances", "mutated source fixture", () =>
		{
			using var d = Mutate( source, root => root["nodes"][0].AsObject().Remove( "extensions" ) ); Require( d.ReadInstances( 0 ).Length == 0, "Ordinary mesh must be distinguishable from an extension batch." );
		} );
		Run( "mismatched_instance_attribute_count_rejected", "negative source fixture", () => ExpectGltf( () => { using var d = Mutate( source, root => root["accessors"][4]["count"] = 124 ); _ = d.ReadInstances( 0 ); }, "count" ) );
		Run( "zero_length_rotation_rejected", "negative source fixture", () =>
		{
			var bad = source.Resolve( "SimpleInstancing.bin" ).ToArray(); Array.Clear( bad, 2220, 16 );
			ExpectGltf( () => { using var d = GltfReader.Read( source.Bytes, _ => bad, Selected ); _ = d.ReadInstances( 0 ); }, "rotation" );
		} );
		Run( "unsupported_optional_extension_is_reported_separate_fallback", "capability fixture", () =>
		{
			using var d = source.Read( false ); Require( d.UnsupportedOptionalExtensions.SequenceEqual( new[] { Extension } ) && d.ReadInstances( 0 ).Length == 0, "Optional unsupported capability must be reported; this is fallback data evidence only." );
		} );
		Run( "unsupported_required_extension_clearly_rejected", "capability fixture", () => ExpectGltf( () => { using var d = Mutate( source, root => root["extensionsRequired"] = new JsonArray( Extension ), false ); }, Extension ) );
		Run( "instances_are_local_then_node_world_applies_once", "mutated source fixture", () =>
		{
			// Core2.0.1 requires each local matrix to decompose to TRS. Valid
			// rotation under a nonuniformly scaled parent creates world shear.
			using var d = Mutate( source, root =>
			{
				root["nodes"][0]["matrix"] = new JsonArray( .6, .8, 0, 0, -.8, .6, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 );
				root["nodes"].AsArray().Add( new JsonObject { ["children"] = new JsonArray( 0 ), ["scale"] = new JsonArray( 2, 3, 4 ), ["translation"] = new JsonArray( 3, -5, 7 ) } );
				root["scenes"][0]["nodes"] = new JsonArray( 1 );
			} );
			var expectedNode = new Matrix4x4( .6f * 2, .8f * 3, 0, 0, -.8f * 2, .6f * 3, 0, 0, 0, 0, 4, 0, 3, -5, 7, 1 ); Require( d.GetWorldTransform( 0 ) == expectedNode, "Full-affine hierarchy world matrix was transposed or decomposed." );
			var transforms = d.ReadInstances( 0 );
			for ( var i = 0; i < InstanceCount; i++ ) for ( var v = 0; v < VertexCount; v++ )
			{
				var expected = ApplyPlacement( reference.World( i, v ), expectedNode, false );
				var actual = NVector.Transform( Vertex( reference.Value( 1, v ) ), transforms[i] * d.GetWorldTransform( 0 ) ); Check( expected, actual, 7e-6, "instance-before-node" );
			}
		} );
		foreach ( var type in new[] { 5120, 5122 } ) Run( "signed_normalized_rotation_" + type, "synthetic legal extension format", () => NormalizedRotation( source, type ) );
		Require( rows.Count == ExpectedCases, "Frozen core case inventory changed." ); return rows.ToArray();
		void Run( string name, string form, Action body ) { try { body(); rows.Add( new( name, form, true, null ) ); } catch ( Exception error ) { rows.Add( new( name, form, false, error.ToString() ) ); } }
	}
	private static void Inventory( Form form, SimpleInstancingReference reference )
	{
		using var d = form.Read(); var root = d.Root;
		Require( d.GetDefaultScene() == 0 && root.GetProperty( "scenes" ).GetArrayLength() == 1 && root.GetProperty( "scenes" )[0].GetProperty( "nodes" ).EnumerateArray().Single().GetInt32() == 0, "Pinned default scene/root changed." );
		Require( root.GetProperty( "nodes" ).GetArrayLength() == 1 && root.GetProperty( "meshes" ).GetArrayLength() == 1 && root.GetProperty( "accessors" ).GetArrayLength() == 6 && root.GetProperty( "bufferViews" ).GetArrayLength() == 6, "Pinned inventory changed." );
		Require( root.GetProperty( "extensionsUsed" ).EnumerateArray().Single().GetString() == Extension && !root.TryGetProperty( "extensionsRequired", out _ ) && d.UnsupportedOptionalExtensions.Count == 0, "Actual sample marks instancing optional; selected support must remain explicit." );
		Require( d.GetWorldTransform( 0 ) == Matrix4x4.Identity && d.EvaluatePose().GetWorldTransform( 0 ) == Matrix4x4.Identity, "Authored node uses default identity TRS." );
		var attributes = root.GetProperty( "nodes" )[0].GetProperty( "extensions" ).GetProperty( Extension ).GetProperty( "attributes" );
		Require( attributes.GetProperty( "TRANSLATION" ).GetInt32() == 3 && attributes.GetProperty( "ROTATION" ).GetInt32() == 4 && attributes.GetProperty( "SCALE" ).GetInt32() == 5 && attributes.EnumerateObject().Count() == 3, "Instance semantic/accessor binding changed." );
		for ( var i = 0; i < 6; i++ )
		{
			var expected = reference.Accessor( i ); var accessor = root.GetProperty( "accessors" )[i]; var view = root.GetProperty( "bufferViews" )[i];
			Require( accessor.GetProperty( "bufferView" ).GetInt32() == i && accessor.GetProperty( "byteOffset" ).GetInt32() == 0 && accessor.GetProperty( "componentType" ).GetInt32() == expected.GetProperty( "component_type" ).GetInt32() && accessor.GetProperty( "count" ).GetInt32() == expected.GetProperty( "count" ).GetInt32(), "Accessor representation changed: " + i );
			Require( accessor.GetProperty( "type" ).GetString() == (i == 0 ? "SCALAR" : i == 4 ? "VEC4" : "VEC3") && (!accessor.TryGetProperty( "normalized", out var normalized ) || !normalized.GetBoolean()), "Actual accessors are nonnormalized UINT/FLOAT." );
			Require( view.GetProperty( "buffer" ).GetInt32() == 0 && view.GetProperty( "byteOffset" ).GetInt32() == expected.GetProperty( "view_offset" ).GetInt32() && view.GetProperty( "byteLength" ).GetInt32() == expected.GetProperty( "length" ).GetInt32() && !view.TryGetProperty( "byteStride", out _ ), "Actual view origin/length/tightly packed stride changed: " + i );
		}
	}
	private static void Accessors( Form form, SimpleInstancingReference reference )
	{
		using var d = form.Read(); for ( var i = 0; i < 6; i++ )
		{
			var expected = reference.Accessor( i ).GetProperty( "values" ).EnumerateArray().SelectMany( Numbers ).ToArray(); var actual = d.ReadFloatAccessor( i ); Require( actual.Length == expected.Length, "Accessor scalar count changed." );
			for ( var element = 0; element < actual.Length; element++ ) Close( expected[element], actual[element], 0, $"accessor {i}/{element}" );
		}
	}
	private static void Transforms( GltfDocument d, SimpleInstancingReference reference, string omitted = null )
	{
		var transforms = d.ReadInstances( 0 ); Require( transforms.Length == InstanceCount, "Expected all 125 authored instances." );
		for ( var i = 0; i < InstanceCount; i++ ) for ( var vertex = 0; vertex < VertexCount; vertex++ )
		{
			Check( reference.World( i, vertex, false, omitted ), NVector.Transform( Vertex( reference.Value( 1, vertex ) ), transforms[i] ), 4e-6, $"instance {i} vertex {vertex}" );
			Require( Matrix4x4.Invert( transforms[i], out var inverse ), "Instance matrix is singular." );
			var actual = NVector.Normalize( NVector.TransformNormal( Vertex( reference.Value( 2, vertex ) ), Matrix4x4.Transpose( inverse ) ) );
			Check( reference.World( i, vertex, true, omitted ), actual, 4e-7, $"instance {i} normal {vertex}" );
		}
	}
	private static void Geometry( Form form, SimpleInstancingReference reference )
	{
		using var d = form.Read(); var primitive = d.ReadPrimitive( 0, 0 );
		Require( primitive.Mode == 4 && primitive.Material is null && primitive.Indices.SequenceEqual( reference.Indices ), "Literal cube topology/winding or default material changed." );
		Require( !d.Root.TryGetProperty( "materials", out _ ) && !d.Root.TryGetProperty( "textures", out _ ) && !d.Root.TryGetProperty( "animations", out _ ) && !d.Root.TryGetProperty( "skins", out _ ), "Actual asset has no materials, textures, animations or skins." );
	}
	internal static void Upload( GltfPrimitiveUploadData upload, SimpleInstancingReference reference )
	{
		Require( upload.Attributes.Count == 2 && upload.Layout.VertexCount == VertexCount && upload.Layout.SourceMode == 4 && !upload.Layout.RequiresTopologyExpansion && upload.OwnedBytes == 720, "Primitive upload inventory/owned cost changed." );
		foreach ( var (semantic, index, stream) in new[] { ("INDICES", 0, upload.Indices), ("POSITION", 1, upload.Attributes["POSITION"]), ("NORMAL", 2, upload.Attributes["NORMAL"]) } )
		{
			var expected = reference.Accessor( index ); var binding = stream.Binding;
			Require( binding.AccessorIndex == index && binding.SourceBufferView == index && binding.SourceBuffer == 0 && binding.ViewByteOffset == expected.GetProperty( "view_offset" ).GetInt32() && binding.AccessorByteOffset == 0 && binding.ByteStride == expected.GetProperty( "stride" ).GetInt32(), "Authored upload descriptor changed: " + semantic );
			Require( stream.ByteOffset == 0 && stream.ByteStride == expected.GetProperty( "stride" ).GetInt32() && stream.ComponentType == expected.GetProperty( "component_type" ).GetInt32() && stream.Components == expected.GetProperty( "components" ).GetInt32() && !stream.Normalized && !stream.IsMaterialized && stream.Count == expected.GetProperty( "count" ).GetInt32(), "Owned stream representation changed: " + semantic );
			Require( Hash( stream.Storage.Bytes.Span ) == expected.GetProperty( "sha256" ).GetString() && stream.Storage.Bytes.Length == expected.GetProperty( "length" ).GetInt32(), "Owned raw view differs: " + semantic );
		}
		Require( !ReferenceEquals( upload.Attributes["POSITION"].Storage, upload.Attributes["NORMAL"].Storage ), "Distinct source views must remain distinct owned storage." );
	}
	private static void Ownership( Form form, SimpleInstancingReference reference )
	{
		GltfPrimitiveUploadData upload; using ( var d = form.Read() ) { upload = d.MaterializePrimitiveUpload( 0, 0, 720 ); ExpectGltf( () => d.MaterializePrimitiveUpload( 0, 0, 719 ), "budget" ); }
		Upload( upload, reference );
	}
	private static void Packaging( Form form )
	{
		var resolved = new List<string>(); using var d = GltfReader.Read( form.Bytes, uri => { resolved.Add( uri ); return form.Resolve( uri ); }, Selected ); _ = d.ReadInstances( 0 ); _ = d.ReadPrimitive( 0, 0 );
		if ( !form.Compiled ) return;
		Require( BinaryPrimitives.ReadUInt32LittleEndian( form.Bytes ) == 0x46546c67 && BinaryPrimitives.ReadUInt32LittleEndian( form.Bytes.AsSpan( 4 ) ) == 2 && BinaryPrimitives.ReadUInt32LittleEndian( form.Bytes.AsSpan( 8 ) ) == form.Bytes.Length, "Compiled artifact must be a portable valid GLB." );
		Require( resolved.Count == 0 && !d.Root.GetProperty( "buffers" )[0].TryGetProperty( "uri", out _ ), "Compiled source still depends on external geometry." );
	}
	private static void NormalizedRotation( Form source, int type )
	{
		var bytes = source.Resolve( "SimpleInstancing.bin" ).ToArray(); var width = type == 5120 ? 1 : 2;
		for ( var i = 0; i < InstanceCount; i++ ) for ( var c = 0; c < 4; c++ )
		{
			var offset = 2220 + (i * 4 + c) * width;
			if ( width == 1 ) bytes[offset] = c == 2 ? (byte)127 : (byte)0; else BinaryPrimitives.WriteInt16LittleEndian( bytes.AsSpan( offset ), c == 2 ? (short)32767 : (short)0 );
		}
		using var d = Mutate( source, root => { root["accessors"][4]["componentType"] = type; root["accessors"][4]["normalized"] = true; root["accessors"][4].AsObject().Remove( "min" ); root["accessors"][4].AsObject().Remove( "max" ); root["bufferViews"][4]["byteLength"] = InstanceCount * 4 * width; }, true, bytes );
		var transforms = d.ReadInstances( 0 ); Require( transforms.Length == InstanceCount, "Normalized rotation dropped instances." ); Check( new double[] { -1, 0, 0 }, NVector.Transform( NVector.UnitX, transforms[0] ), 0, "normalized quaternion Z half turn" );
	}
	private static JsonObject Attributes( JsonNode root ) => root["nodes"][0]["extensions"][Extension]["attributes"].AsObject();
	private static GltfDocument Mutate( Form source, Action<JsonNode> edit, bool selected = true, byte[] bin = null ) { var root = JsonNode.Parse( source.Bytes ); edit( root ); return GltfReader.Read( Encoding.UTF8.GetBytes( root.ToJsonString() ), bin is null ? source.Resolve : _ => bin, selected ? Selected : null ); }
	private static NVector Vertex( double[] p ) => new( (float)p[0], (float)p[1], (float)p[2] );
	private static void Check( double[] expected, NVector actual, double tolerance, string label ) { Close( expected[0], actual.X, tolerance, label + " X" ); Close( expected[1], actual.Y, tolerance, label + " Y" ); Close( expected[2], actual.Z, tolerance, label + " Z" ); }
	private static void ExpectGltf( Action body, string word ) { try { body(); } catch ( GltfException error ) { Require( error.Message.Contains( word, StringComparison.OrdinalIgnoreCase ), "Unhelpful rejection: " + error.Message ); return; } throw new InvalidOperationException( "Expected explicit GltfException rejection." ); }
}
