using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;

namespace Sandbox.Gltf.DirectGpu.NegativeScaleValidation;
using static NegativeScaleReference;

/// <summary>22 pure staging-plan groups. These are not actual GPU or rendered passes.</summary>
public static class NegativeScalePlanTests
{
	public const int ExpectedCases = 22;
	public sealed record Result( string Name, string Form, bool Passed, string Error );
	public static Result[] RunAll( string checkoutRoot )
	{
		var reference = new NegativeScaleReference( checkoutRoot ); var results = new List<Result>();
		foreach ( var form in reference.Forms )
		{
			Run( "raw_descriptor_arena_and_runtime_layout",form,() => Descriptors( form ) );
			Run( "rest_full_affine_palette_basis_units_once",form,() => Palette( form,0 ) );
			Run( "global_determinant_parent_cancellation_tiny_and_singular_placement",form,() => Winding( form ) );
			if ( form.Fixture ) foreach ( var key in new[] { 1,2 } ) Run( "authored_zero_scale_palette_key" + key,form,() => Palette( form,key ) );
		}
		Require( results.Count == ExpectedCases,"Frozen staging-plan case inventory changed." ); return results.ToArray();
		void Run( string name, Form form, Action body ) { try { body(); results.Add( new( name,form.Name,true,null ) ); } catch ( Exception e ) { results.Add( new( name,form.Name,false,e.ToString() ) ); } }
	}
	private static void Descriptors( Form form )
	{
		using var d = form.Read(); var expected = form.Expected();
		foreach ( var row in expected.GetProperty( "rows" ).EnumerateArray().GroupBy( r => r.GetProperty( "mesh" ).GetInt32() ).Select( g => g.First() ) )
		{
			var plan = GltfGpuPrimitivePlan.Create( d,row.GetProperty( "mesh" ).GetInt32(),0 ); var count = row.GetProperty( "positions" ).GetArrayLength();
			Require( plan.Mode == 4 && !plan.GenerateFlatNormals && plan.SourceVertices.SequenceEqual( Enumerable.Range( 0,count ).Select( i => (uint)i ) ),"Authored-normal geometry map was rewritten." );
			Require( plan.Indices.SequenceEqual( row.GetProperty( "indices" ).EnumerateArray().Select( x => x.GetUInt32() ) ),"Primitive plan must preserve authored source winding until per-node submission." );
			Require( plan.Fields.All( f => f.ComponentType == 5126 && f.Offset >= 0 && f.Offset + 4 * f.Components <= plan.VertexStride ) && plan.Fields.Select( f => f.Offset ).Distinct().Count() == plan.Fields.Count,"Runtime output declaration overlap or invalid field." );
			foreach ( var attr in row.GetProperty( "attributes" ).EnumerateObject() )
			{
				var reference = expected.GetProperty( "accessors" )[attr.Value.GetInt32()]; var binding = plan.Sources[attr.Name]; var descriptor = plan.InputDescriptors[binding.DescriptorIndex]; var values = reference.GetProperty( "values" );
				Require( descriptor.ByteOffset == binding.ArenaByteOffset && descriptor.ByteStride == reference.GetProperty( "stride" ).GetUInt32() && descriptor.ComponentType == reference.GetProperty( "component_type" ).GetUInt32() && descriptor.Components == reference.GetProperty( "components" ).GetUInt32() && descriptor.Normalized == 0,"Raw source descriptor changed during arena packing." );
				for ( var v = 0; v < values.GetArrayLength(); v++ ) for ( var c = 0; c < descriptor.Components; c++ )
					Close( values[v][c].GetDouble(),BinaryPrimitives.ReadSingleLittleEndian( plan.InputBytes.AsSpan( checked((int)descriptor.ByteOffset + v * (int)descriptor.ByteStride + c * 4),4 ) ),0,"Direct arena raw component" );
			}
		}
	}
	private static void Palette( Form form, int key )
	{
		using var d = form.Read(); var pose = form.Fixture ? d.EvaluatePose( 0,key ) : d.EvaluatePose(); var expected = form.Expected( key );
		foreach ( var row in expected.GetProperty( "rows" ).EnumerateArray() )
		{
			var plan = GltfGpuPrimitivePlan.Create( d,row.GetProperty( "mesh" ).GetInt32(),0 ); var rig = new GltfGpuRig( d,row.GetProperty( "node" ).GetInt32(),null,0,plan ); var matrix = rig.Evaluate( pose ); Require( matrix.Length == 1 && rig.JointCount == 0,"Static nodes must use one full-affine palette, without native skin controllers." );
			var accessor = row.GetProperty( "attributes" ).GetProperty( "POSITION" ).GetInt32(); var source = expected.GetProperty( "accessors" )[accessor].GetProperty( "values" );
			for ( var v = 0; v < source.GetArrayLength(); v++ )
			{
				var p = Numbers( source[v] ); var output = matrix[0].Position( new System.Numerics.Vector3( (float)p[0],(float)p[1],(float)p[2] ) ); var actual = new[] { output.X,output.Y,output.Z }; var e = Numbers( row.GetProperty( "positions" )[v] );
				for ( var c = 0; c < 3; c++ ) Close( e[c],actual[c],.00015,"Palette full hierarchy and one basis/units conversion" );
			}
		}
	}
	private static void Winding( Form form )
	{
		using var d = form.Read();
		foreach ( var key in form.Fixture ? new[] { 0,1,2,3 } : new[] { 0 } )
		{
			var pose = form.Fixture ? d.EvaluatePose( 0,key ) : d.EvaluatePose();
			foreach ( var row in form.Expected( key ).GetProperty( "rows" ).EnumerateArray() )
			{
				var plan = GltfGpuPrimitivePlan.Create( d,row.GetProperty( "mesh" ).GetInt32(),0 ); var rig = new GltfGpuRig( d,row.GetProperty( "node" ).GetInt32(),null,0,plan );
				foreach ( var placement in new[] { Matrix4x4.Identity,AffinePlacement,MirroredPlacement,Matrix4x4.CreateScale( 0,2,.75f ),Matrix4x4.CreateScale( 0,0,0 ),TinyUniformMirroredPlacement,TinyAnisotropicPlacement,TinyAnisotropicMirroredPlacement } )
				{
					var expected = row.GetProperty( "determinant" ).GetDouble() * Determinant( placement ) < 0;
					Require( rig.ReverseWinding( pose,placement ) == expected,"Full-affine determinant sign lost, including tiny invertible placement or mirrored parents; source node " + row.GetProperty( "node" ) + " key " + key );
				}
			}
		}
	}
}
