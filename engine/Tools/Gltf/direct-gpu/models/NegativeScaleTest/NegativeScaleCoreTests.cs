using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;

namespace Sandbox.Gltf.DirectGpu.NegativeScaleValidation;
using static NegativeScaleReference;

/// <summary>38 frozen CPU/data assertions; no GPU/rendering claim.</summary>
public static class NegativeScaleCoreTests
{
	public const int ExpectedCases = 38;
	public sealed record Result( string Name, string Form, bool Passed, string Error );
	public static Result[] RunAll( string checkoutRoot )
	{
		var reference = new NegativeScaleReference( checkoutRoot ); var results = new List<Result>();
		foreach ( var form in reference.Forms.Where( f => !f.Fixture ) )
		{
			Run( "actual14nodes11draws8meshes22accessors", form, () => { using var d = form.Read(); Require( d.GetDefaultScene() == 0 && d.Root.GetProperty( "nodes" ).GetArrayLength() == 14 && d.Root.GetProperty( "meshes" ).GetArrayLength() == 8 && d.Root.GetProperty( "accessors" ).GetArrayLength() == 22, "Pinned source inventory changed." ); Require( !d.Root.TryGetProperty( "animations", out _ ) && !d.Root.TryGetProperty( "skins", out _ ), "Actual asset is static and unskinned." ); } );
			Run( "all_raw_accessor_values_offsets_and_formats", form, () => Accessors( form ) );
			Run( "full_world_and_pose_hierarchy_double_mirror_cancels", form, () => World( form, 0 ) );
			Run( "positions_source_units_match_independent_oracle", form, () => Positions( form, 0 ) );
			Run( "single_sided0to2_authored_double_sided3to5", form, () => { using var d = form.Read(); for ( var i = 0; i < 6; i++ ) { var m = d.Root.GetProperty( "materials" )[i]; Require( (m.TryGetProperty( "doubleSided", out var side ) && side.GetBoolean()) == (i >= 3), "Authored culling policy changed." ); } } );
			Run( "owned_upload_source_descriptors_and_topology", form, () => Upload( form ) );
			Run( "portable_compiled_glb_geometry_and_images_no_external_io", form, () => Package( form ) );
		}
		foreach ( var form in reference.Forms.Where( f => f.Fixture ) )
		{
			Run( "affine_hierarchy_and_authored_tangent_raw_values", form, () => { Accessors( form ); World( form, 0 ); } );
			foreach ( var key in new[] { 0,1,2,3 } ) Run( "negative_rank2zero_rank0zero_recovery_key" + key, form, () => { World( form, key ); Positions( form, key ); } );
		}
		Require( results.Count == ExpectedCases, "Frozen core case inventory changed." ); return results.ToArray();
		void Run( string name, Form form, Action test ) { try { test(); results.Add( new( name, form.Name, true, null ) ); } catch ( Exception error ) { results.Add( new( name, form.Name, false, error.ToString() ) ); } }
	}
	private static void Accessors( Form form )
	{
		using var d = form.Read(); var oracle = form.Expected();
		for ( var i = 0; i < oracle.GetProperty( "accessors" ).GetArrayLength(); i++ )
		{
			var expected = oracle.GetProperty( "accessors" )[i]; var source = d.Root.GetProperty( "accessors" )[i]; var view = d.Root.GetProperty( "bufferViews" )[source.GetProperty( "bufferView" ).GetInt32()];
			Require( source.GetProperty( "componentType" ).GetInt32() == expected.GetProperty( "component_type" ).GetInt32() && source.GetProperty( "count" ).GetInt32() == expected.GetProperty( "count" ).GetInt32(), "Accessor format/count changed." );
			Require( (!source.TryGetProperty( "normalized", out var normalized ) || !normalized.GetBoolean()) && (!source.TryGetProperty( "byteOffset", out var offset ) ? 0 : offset.GetInt32()) == expected.GetProperty( "accessor_offset" ).GetInt32(), "Accessor normalized/offset changed." );
			// Packaging may relocate bufferViews to embed images; raw accessor values remain authoritative.
			if ( !form.Compiled ) Require( (!view.TryGetProperty( "byteOffset", out var vo ) ? 0 : vo.GetInt32()) == expected.GetProperty( "view_offset" ).GetInt32(), "Source view origin changed." );
			var actual = d.ReadFloatAccessor( i ); var values = expected.GetProperty( "values" ).EnumerateArray().SelectMany( Numbers ).ToArray(); Require( actual.Length == values.Length, "Accessor length changed." );
			for ( var k = 0; k < actual.Length; k++ ) Close( values[k], actual[k], 0, $"accessor {i}/{k}" );
		}
	}
	private static void World( Form form, int key )
	{
		using var d = form.Read(); var pose = form.Fixture ? d.EvaluatePose( 0,key ) : d.EvaluatePose();
		foreach ( var row in form.Expected( key ).GetProperty( "rows" ).EnumerateArray() )
		{
			var expected = Numbers( row.GetProperty( "world_column_major" ) ); var node = row.GetProperty( "node" ).GetInt32(); var actual = pose.GetWorldTransform( node );
			var values = new[] { actual.M11,actual.M12,actual.M13,actual.M14, actual.M21,actual.M22,actual.M23,actual.M24, actual.M31,actual.M32,actual.M33,actual.M34, actual.M41,actual.M42,actual.M43,actual.M44 };
			for ( var i = 0; i < 16; i++ ) Close( expected[i], values[i], 2e-6, $"world node{node} component{i}" );
			Close( row.GetProperty( "determinant" ).GetDouble(), Determinant( actual ), 5e-6, "Full affine determinant" );
			if ( !form.Fixture ) Require( actual == d.GetWorldTransform( node ), "Rest pose and static hierarchy disagree." );
		}
	}
	private static void Positions( Form form, int key )
	{
		using var d = form.Read(); var pose = form.Fixture ? d.EvaluatePose( 0,key ) : d.EvaluatePose();
		foreach ( var row in form.Expected( key ).GetProperty( "rows" ).EnumerateArray() )
		{
			var positions = row.GetProperty( "positions" ); var count = positions.GetArrayLength();
			foreach ( var vertex in new[] { 0,count / 2,count - 1 }.Distinct() )
			{
				var p = d.EvaluateVertexPosition( row.GetProperty( "node" ).GetInt32(),row.GetProperty( "primitive" ).GetInt32(),vertex,pose );
				var actual = new[] { p.Z / .0254,p.X / .0254,p.Y / .0254 }; var expected = Numbers( positions[vertex] ); for ( var c = 0; c < 3; c++ ) Close( expected[c],actual[c],.00015,"CPU source hierarchy→basis/units exactly once" );
			}
		}
	}
	private static void Upload( Form form )
	{
		using var d = form.Read();
		foreach ( var row in form.Expected().GetProperty( "rows" ).EnumerateArray().GroupBy( r => r.GetProperty( "mesh" ).GetInt32() ).Select( g => g.First() ) )
		{
			var mesh = row.GetProperty( "mesh" ).GetInt32(); var upload = d.MaterializePrimitiveUpload( mesh,0 ); var primitive = d.ReadPrimitive( mesh,0 );
			Require( primitive.Mode == 4 && !upload.Layout.RequiresTopologyExpansion && upload.Layout.VertexCount == row.GetProperty( "positions" ).GetArrayLength(), "Unexpected topology rewrite." );
			Require( primitive.Indices.SequenceEqual( row.GetProperty( "indices" ).EnumerateArray().Select( v => v.GetUInt32() ) ), "Raw source winding changed." );
			foreach ( var attr in row.GetProperty( "attributes" ).EnumerateObject() )
			{
				var expected = form.Expected().GetProperty( "accessors" )[attr.Value.GetInt32()]; var stream = upload.Attributes[attr.Name];
				Require( stream.Binding.AccessorIndex == attr.Value.GetInt32() && stream.ComponentType == expected.GetProperty( "component_type" ).GetInt32() && stream.Components == expected.GetProperty( "components" ).GetInt32() && stream.ByteStride == expected.GetProperty( "stride" ).GetInt32() && !stream.Normalized, "Actual source descriptor was inferred or changed." );
			}
		}
	}
	private static void Package( Form form )
	{
		if ( !form.Compiled ) return;
		Require( BinaryPrimitives.ReadUInt32LittleEndian( form.Bytes ) == 0x46546c67 && BinaryPrimitives.ReadUInt32LittleEndian( form.Bytes.AsSpan( 4 ) ) == 2 && BinaryPrimitives.ReadUInt32LittleEndian( form.Bytes.AsSpan( 8 ) ) == form.Bytes.Length, "Compiled artifact is not a valid GLB container." );
		var calls = 0; using var d = GltfReader.Read( form.Bytes,_ => { calls++; throw new InvalidOperationException( "Compiled artifact still uses external I/O." ); } );
		for ( var mesh = 0; mesh < 8; mesh++ ) _ = d.MaterializePrimitiveUpload( mesh,0 );
		for ( var image = 0; image < 2; image++ ) _ = d.ReadImage( image ); Require( calls == 0, "Portable compiled artifact invoked resolver." );
	}
}
