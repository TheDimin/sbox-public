using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sandbox.Gltf.DirectGpu.MorphPrimitivesValidation;

using static MorphPrimitivesReference;

/// <summary>Explicit derived controls: sparse target 0, padded target 1, packed normalized per-primitive attributes, nonindexed primitive 1.</summary>
public static class MorphPrimitivesLayoutFixture
{
	public static GltfDocument Create( Form canonical )
	{
		var root = SourceJson( canonical.Source, canonical.Resolve ); var views = root["bufferViews"].AsArray(); var accessors = root["accessors"].AsArray(); var buffers = root["buffers"].AsArray();
		int View( byte[] bytes, int? stride = null, int? target = null )
		{
			var buffer = buffers.Count; buffers.Add( new JsonObject { ["byteLength"] = bytes.Length, ["uri"] = "data:application/octet-stream;base64," + Convert.ToBase64String( bytes ) } );
			var index = views.Count; var view = new JsonObject { ["buffer"] = buffer, ["byteLength"] = bytes.Length };
			if ( stride.HasValue ) view["byteStride"] = stride.Value; if ( target.HasValue ) view["target"] = target.Value; views.Add( view ); return index;
		}
		// The target is still primitive-local. Sparse has a zero base and replaces
		// just the authored moving vertices, preserving their original identities.
		var moving = Primitive( canonical, 0 ).GetProperty( "morph" ).GetProperty( "values" ).EnumerateArray().Select( (v, i) => (V: v, I: i) ).Where( x => x.V.EnumerateArray().Any( c => c.GetSingle() != 0 ) ).ToArray();
		var sparseIndices = moving.Select( x => (byte)x.I ).ToArray(); var sparseValues = new byte[moving.Length * 12];
		for ( var i = 0; i < moving.Length; i++ ) for ( var c = 0; c < 3; c++ ) BinaryPrimitives.WriteSingleLittleEndian( sparseValues.AsSpan( i * 12 + c * 4 ), moving[i].V[c].GetSingle() );
		var sparseIndexView = View( sparseIndices ); var sparseValueView = View( sparseValues ); var sparse = accessors[4].AsObject(); sparse.Remove( "bufferView" ); sparse.Remove( "byteOffset" );
		sparse["sparse"] = new JsonObject { ["count"] = moving.Length, ["indices"] = new JsonObject { ["bufferView"] = sparseIndexView, ["componentType"] = 5121 }, ["values"] = new JsonObject { ["bufferView"] = sparseValueView } };
		var target = new byte[9 * 20]; Array.Fill( target, (byte)0xa5 );
		for ( var v = 0; v < 9; v++ ) for ( var c = 0; c < 3; c++ ) BinaryPrimitives.WriteSingleLittleEndian( target.AsSpan( v * 20 + 4 + c * 4 ), Primitive( canonical, 1 ).GetProperty( "morph" ).GetProperty( "values" )[v][c].GetSingle() );
		accessors[9]["bufferView"] = View( target, 20, 34962 ); accessors[9]["byteOffset"] = 4;
		// A second primitive with source normalized integer formats is a derived
		// quantization test, not a claim about this upstream FLOAT-only variant.
		var position = new byte[9 * 8 + 4]; Array.Fill( position, (byte)0x71 );
		for ( var v = 0; v < 9; v++ ) for ( var c = 0; c < 3; c++ ) BinaryPrimitives.WriteInt16LittleEndian( position.AsSpan( v * 8 + 4 + c * 2 ), PositionComponent( canonical, v, c ) );
		var a = accessors[7]; a["bufferView"] = View( position, 8, 34962 ); a["byteOffset"] = 4; a["componentType"] = 5122; a["normalized"] = true;
		a["min"] = new JsonArray( 0, 0, -16384 ); a["max"] = new JsonArray( 16384, 0, 0 );
		var normal = new byte[9 * 4]; for ( var v = 0; v < 9; v++ ) normal[v * 4 + 1] = 127;
		a = accessors[6]; a["bufferView"] = View( normal, 4, 34962 ); a["componentType"] = 5120; a["normalized"] = true; a["byteOffset"] = 0;
		var uv = new byte[9 * 4]; Array.Fill( uv, (byte)0xee );
		for ( var v = 0; v < 9; v++ ) for ( var c = 0; c < 2; c++ ) uv[v * 4 + c] = UvComponent( canonical, v, c );
		a = accessors[8]; a["bufferView"] = View( uv, 4, 34962 ); a["componentType"] = 5121; a["normalized"] = true; a["byteOffset"] = 0;
		root["extensionsUsed"] = new JsonArray( "KHR_mesh_quantization" ); root["extensionsRequired"] = new JsonArray( "KHR_mesh_quantization" );
		root["meshes"][0]["primitives"][1].AsObject().Remove( "indices" ); root["nodes"][1]["weights"] = new JsonArray( -.75f );
		return GltfReader.Read( JsonSerializer.SerializeToUtf8Bytes( root ), canonical.Resolve, Selected );
	}
	public static short PositionComponent( Form canonical, int vertex, int component ) => checked((short)MathF.Round( Primitive( canonical, 1 ).GetProperty( "attributes" ).GetProperty( "POSITION" ).GetProperty( "values" )[vertex][component].GetSingle() * 32767 ));
	public static byte UvComponent( Form canonical, int vertex, int component ) => checked((byte)MathF.Round( Uv( canonical, 1, vertex, component ) * 255 ));
	public static MorphPrimitivesCoreTests.Result[] RunAll( string checkoutRoot )
	{
		var reference = new MorphPrimitivesReference( checkoutRoot ); var canonical = reference.Forms.Single( x => x.Name == "glTF/source" ); var rows = new List<MorphPrimitivesCoreTests.Result>();
		Run( "sparse_zero_base_target_materializes_in_original_vertex_order", () =>
		{
			using var d = Create( canonical ); var u = d.MaterializeMorphTargetsUpload( 0, 0 ); var stream = u.Targets[0]["POSITION"];
			Require( stream.IsMaterialized && stream.ByteOffset == 0 && stream.ByteStride == 12 && (stream.Binding.Materialization & (GltfUploadMaterialization.Sparse | GltfUploadMaterialization.ZeroInitialized)) == (GltfUploadMaterialization.Sparse | GltfUploadMaterialization.ZeroInitialized), "Sparse/zero provenance or packed materialized shape changed." );
			for ( var v = 0; v < 21; v++ ) for ( var c = 0; c < 3; c++ ) Close( Primitive( canonical, 0 ).GetProperty( "morph" ).GetProperty( "values" )[v][c].GetSingle(), BinaryPrimitives.ReadSingleLittleEndian( stream.Storage.Bytes.Span.Slice( v * 12 + c * 4, 4 ) ), 0, "sparse target component" );
		} );
		Run( "padded_target_keeps_nonzero_accessor_offset_stride_and_owned_sentinels", () =>
		{
			using var d = Create( canonical ); var s = d.MaterializeMorphTargetsUpload( 0, 1 ).Targets[0]["POSITION"];
			Require( !s.IsMaterialized && s.ByteOffset == 4 && s.ByteStride == 20 && s.Storage.Bytes.Length == 180 && s.Storage.Bytes.Span[0] == 0xa5, "Target padding/offset/stride was inferred from primitive 0." );
			for ( var v = 0; v < 9; v++ ) for ( var c = 0; c < 3; c++ ) Close( Primitive( canonical, 1 ).GetProperty( "morph" ).GetProperty( "values" )[v][c].GetSingle(), BinaryPrimitives.ReadSingleLittleEndian( s.Storage.Bytes.Span.Slice( 4 + v * 20 + c * 4, 4 ) ), 0, "padded target component" );
		} );
		Run( "indexed_and_nonindexed_primitive_topology_are_local", () =>
		{
			using var d = Create( canonical ); var a = d.MaterializePrimitiveUpload( 0, 0 ); var b = d.MaterializePrimitiveUpload( 0, 1 );
			Require( !a.Layout.RequiresGeneratedIndices && a.Indices.Count == 72 && b.Layout.RequiresGeneratedIndices && b.Indices.Count == 9, "Indexed topology leaked across primitive boundary." );
			var plan = GltfGpuPrimitivePlan.Create( d, 0, 1 ); Require( plan.Indices.SequenceEqual( Enumerable.Range( 0, 9 ).Select( x => (uint)x ) ), "Nonindexed local triangle order changed." );
		} );
		Run( "mixed_normalized_formats_are_not_replaced_by_float_vertex_input", () =>
		{
			using var d = Create( canonical ); var a = d.MaterializePrimitiveUpload( 0, 0 ); var b = d.MaterializePrimitiveUpload( 0, 1 );
			Require( a.Attributes["POSITION"].ComponentType == 5126 && a.Attributes["POSITION"].ByteStride == 12, "Primitive 0 FLOAT input changed." );
			foreach ( var item in new[] { (Name: "POSITION", Type: 5122, Stride: 8, Offset: 4), (Name: "NORMAL", Type: 5120, Stride: 4, Offset: 0), (Name: "TEXCOORD_0", Type: 5121, Stride: 4, Offset: 0) } )
			{
				var s = b.Attributes[item.Name]; Require( s.ComponentType == item.Type && s.Normalized && s.ByteStride == item.Stride && s.ByteOffset == item.Offset, "Derived normalized source descriptor changed: " + item.Name );
			}
		} );
		Run( "signed_sparse_and_strided_morphs_evaluate_before_conversion", () =>
		{
			using var d = Create( canonical ); var pose = d.EvaluatePose();
			for ( var p = 0; p < 2; p++ ) for ( var v = 0; v < Count( p ); v++ )
			{
				var expected = Position( canonical, p, v, -.75f );
				if ( p == 1 ) expected = new System.Numerics.Vector3( PositionComponent( canonical, v, 0 ) / 32767f, PositionComponent( canonical, v, 1 ) / 32767f, PositionComponent( canonical, v, 2 ) / 32767f ) + Vector( Primitive( canonical, p ).GetProperty( "morph" ).GetProperty( "values" )[v] ) * -.75f;
				var actual = d.EvaluateVertexPosition( 1, p, v, pose ); Close( expected.X, actual.X, 2e-6f, "X" ); Close( expected.Y, actual.Y, 2e-6f, "Y" ); Close( expected.Z, actual.Z, 2e-6f, "Z" );
			}
		} );
		return rows.ToArray();
		void Run( string name, Action body ) => MorphPrimitivesCoreTests.RunCase( rows, name, "derived_layout_control", body );
	}
}
