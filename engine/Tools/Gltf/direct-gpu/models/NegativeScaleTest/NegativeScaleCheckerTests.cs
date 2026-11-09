using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;

namespace Sandbox.Gltf.DirectGpu.NegativeScaleValidation;
using static NegativeScaleReference;

/// <summary>Eleven checker controls, using literal independently expected data, not production deformation.</summary>
public static class NegativeScaleCheckerTests
{
	public const int ExpectedCases = 11;
	public sealed record Result( string Name, bool Passed, string Error );
	public static Result[] RunAll( string checkoutRoot )
	{
		var reference = new NegativeScaleReference( checkoutRoot ); var expected = reference.Oracle.GetProperty( "fixture" ).GetProperty( "0" ); var rows = new List<Result>();
		Run( "independent_literal_passes", () => NegativeScaleGpuChecks.Assert( expected,Literal( expected ),Matrix4x4.Identity ) );
		Run( "tiny_uniform_two_reflections_literal_passes", () => NegativeScaleGpuChecks.Assert( expected,Literal( expected,TinyUniformMirroredPlacement ),TinyUniformMirroredPlacement ) );
		Run( "tiny_anisotropic_negative_combined_determinant_literal_passes", () => NegativeScaleGpuChecks.Assert( expected,Literal( expected,TinyAnisotropicPlacement ),TinyAnisotropicPlacement ) );
		Run( "tiny_anisotropic_positive_combined_determinant_literal_passes", () => NegativeScaleGpuChecks.Assert( expected,Literal( expected,TinyAnisotropicMirroredPlacement ),TinyAnisotropicMirroredPlacement ) );
		Run( "wrong_position_basis_or_units_rejected", () => Reject( expected,a => Float( a[0],0,0,17 ) ) );
		Run( "wrong_normal_direction_rejected", () => Reject( expected,a => Float( a[0],0,12,-Read( a[0],0,12 ) ) ) );
		Run( "wrong_tangentW_rejected", () => Reject( expected,a => Float( a[0],0,36,1 ) ) );
		Run( "wrong_bound_index_winding_rejected", () => Reject( expected,a => (a[0].Indices[1],a[0].Indices[2]) = (a[0].Indices[2],a[0].Indices[1]) ) );
		Run( "wrong_source_vertex_map_rejected", () => Reject( expected,a => a[0].SourceVertices[1] = 0 ) );
		Run( "exact_zero_position_collapse_and_finite_fallback_passes", () => { var e = reference.Oracle.GetProperty( "fixture" ).GetProperty( "2" ); var r = NegativeScaleGpuChecks.Assert( e,Literal( e ),Matrix4x4.Identity ); Require( r.DefinedNormals == 0 && r.UndefinedSingularNormals == 3,"Singular directions were incorrectly classified as defined." ); } );
		Run( "singular_nonfinite_fallback_rejected", () => Reject( reference.Oracle.GetProperty( "fixture" ).GetProperty( "2" ),a => Float( a[0],0,12,float.NaN ) ) );
		Require( rows.Count == ExpectedCases,"Checker case inventory changed." ); return rows.ToArray();
		void Run( string name, Action test ) { try { test(); rows.Add( new( name,true,null ) ); } catch ( Exception e ) { rows.Add( new( name,false,e.ToString() ) ); } }
	}
	private static void Reject( JsonElement e, Action<GltfGpuPrimitiveSnapshot[]> corrupt )
	{
		var a = Literal( e ); corrupt( a ); try { NegativeScaleGpuChecks.Assert( e,a,Matrix4x4.Identity ); } catch ( InvalidOperationException ) { return; } throw new InvalidOperationException( "Checker accepted a deliberately wrong GPU observation." );
	}
	private static GltfGpuPrimitiveSnapshot[] Literal( JsonElement expected, Matrix4x4? requestedPlacement = null ) => expected.GetProperty( "rows" ).EnumerateArray().Select( row =>
	{
		var placement = requestedPlacement ?? Matrix4x4.Identity; var count = row.GetProperty( "positions" ).GetArrayLength(); var bytes = new byte[count * 40]; var source = Enumerable.Range( 0,count ).Select( i => (uint)i ).ToArray();
		var indices = row.GetProperty( "indices" ).EnumerateArray().Select( x => x.GetUInt32() ).ToArray(); if ( row.GetProperty( "determinant" ).GetDouble() * Determinant( placement ) < 0 ) for ( var i = 0; i < indices.Length; i += 3 ) (indices[i + 1],indices[i + 2]) = (indices[i + 2],indices[i + 1]);
		var snapshot = new GltfGpuPrimitiveSnapshot { Node = row.GetProperty( "node" ).GetInt32(),Mesh = row.GetProperty( "mesh" ).GetInt32(),Primitive = row.GetProperty( "primitive" ).GetInt32(),VertexCount = count,VertexStride = 40,Fields = new[] { new GltfGpuField( "POSITION",0,0,3 ),new GltfGpuField( "NORMAL",0,12,3 ),new GltfGpuField( "TANGENT",0,24,4 ) },Bytes = bytes,SourceVertices = source,Indices = indices,IndexDataOrigin = "gpu-readback" };
		for ( var v = 0; v < count; v++ )
		{
			var p = Position( row,v,placement ); var n = Defined( row,placement ) ? Normal( row,v,placement ) : new double[] { 1,0,0 };
			var t = row.GetProperty( "tangents" ).ValueKind == JsonValueKind.Null || !Defined( row,placement ) ? new double[] { 0,1,0,1 } : Tangent( row,v,placement );
			for ( var c = 0; c < 3; c++ ) { Float( snapshot,v,c * 4,(float)p[c] ); Float( snapshot,v,12 + c * 4,(float)n[c] ); } for ( var c = 0; c < 4; c++ ) Float( snapshot,v,24 + c * 4,(float)t[c] );
		}
		return snapshot;
	} ).ToArray();
	private static float Read( GltfGpuPrimitiveSnapshot s, int vertex, int offset ) => BinaryPrimitives.ReadSingleLittleEndian( s.Bytes.AsSpan( vertex * s.VertexStride + offset,4 ) );
	private static void Float( GltfGpuPrimitiveSnapshot s, int vertex, int offset, float value ) => BinaryPrimitives.WriteSingleLittleEndian( s.Bytes.AsSpan( vertex * s.VertexStride + offset,4 ),value );
}
