using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Sandbox.Gltf.DirectGpu.NormalTangentMirrorValidation;
using static NormalTangentMirrorReference;

/// <summary>Mutation tests for acceptance sensitivity; synthetic bytes are not GPU evidence.</summary>
internal static class NormalTangentMirrorCheckerTests
{
	public const int ExpectedCases = 10;
	public sealed record Result( string Name, bool Passed, string Error );
	public static Result[] RunAll( string checkoutRoot )
	{
		var reference = new NormalTangentMirrorReference( checkoutRoot ); var rows = new List<Result>();
		Run( "independent_source_identity_passes",() => NormalTangentMirrorGpuProbe.Assert( reference,[Literal( reference,Matrix4x4.Identity )],Matrix4x4.Identity ) );
		Run( "independent_source_reflected_affine_passes",() => NormalTangentMirrorGpuProbe.Assert( reference,[Literal( reference,Mirrored )],Mirrored ) );
		var mirroredVertex = Enumerable.Range( 0,2770 ).First( i => reference.Values( 3,i )[3] == -1 );
		Run( "discarded_authored_negative_w_rejected",() => Reject( row => Write( row,mirroredVertex,36,1 ) ) );
		Run( "flipped_tangent_direction_rejected",() => Reject( row => { for ( var c = 0; c < 3; c++ ) Write( row,0,24+c*4,-Read( row,0,24+c*4 ) ); } ) );
		Run( "wrong_normal_basis_rejected",() => Reject( row => Write( row,0,12,Read( row,0,12 )+.05f ) ) );
		Run( "wrong_uv_origin_rejected",() => Reject( row => Write( row,0,44,1-Read( row,0,44 ) ) ) );
		Run( "wrong_bound_winding_rejected",() => Reject( row => (row.Indices[1],row.Indices[2]) = (row.Indices[2],row.Indices[1]) ) );
		Run( "source_mapping_corruption_rejected",() => Reject( row => row.SourceVertices[1] = 0 ) );
		Run( "nonfinite_tangent_rejected",() => Reject( row => Write( row,0,24,float.NaN ) ) );
		Run( "wrong_units_rejected",() => Reject( row => Write( row,0,0,Read( row,0,0 )*.0254f ) ) );
		Require( rows.Count == ExpectedCases,"Checker case inventory changed." ); return rows.ToArray();
		void Run( string name, Action test ) { try { test(); rows.Add( new( name,true,null ) ); } catch ( Exception e ) { rows.Add( new( name,false,e.ToString() ) ); } }
		void Reject( Action<GltfGpuPrimitiveSnapshot> mutate )
		{
			var row = Literal( reference,Matrix4x4.Identity ); mutate( row );
			try { NormalTangentMirrorGpuProbe.Assert( reference,[row],Matrix4x4.Identity ); } catch ( InvalidOperationException ) { return; }
			throw new InvalidOperationException( "Acceptance checker accepted corrupted observation." );
		}
	}
	private static GltfGpuPrimitiveSnapshot Literal( NormalTangentMirrorReference reference, Matrix4x4 placement )
	{
		var indices = Enumerable.Range( 0,15720 ).Select( i => (uint)reference.Values( 0,i )[0] ).ToArray();
		if ( Det( placement ) < 0 ) for ( var i = 0; i < indices.Length; i += 3 ) (indices[i+1],indices[i+2]) = (indices[i+2],indices[i+1]);
		var row = new GltfGpuPrimitiveSnapshot { Node = 0,Mesh = 0,Primitive = 0,VertexCount = 2770,VertexStride = 48,SourceVertices = Enumerable.Range( 0,2770 ).Select( i => (uint)i ).ToArray(),Indices = indices,IndexDataOrigin = "gpu-readback",Bytes = new byte[2770*48],Fields = [new( "POSITION",0,0,3 ),new( "NORMAL",0,12,3 ),new( "TANGENT",0,24,4 ),new( "TEXCOORD",0,40,2 )] };
		for ( var i = 0; i < 2770; i++ )
		{
			var p = reference.Position( i,placement ); var n = reference.Normal( i,placement ); var t = reference.Tangent( i,placement ); var uv = reference.Values( 4,i );
			for ( var c = 0; c < 3; c++ ) { Write( row,i,c*4,(float)p[c] ); Write( row,i,12+c*4,(float)n[c] ); }
			for ( var c = 0; c < 4; c++ ) Write( row,i,24+c*4,(float)t[c] ); for ( var c = 0; c < 2; c++ ) Write( row,i,40+c*4,(float)uv[c] );
		}
		return row;
	}
	private static float Read( GltfGpuPrimitiveSnapshot row, int vertex, int offset ) => BinaryPrimitives.ReadSingleLittleEndian( row.Bytes.AsSpan( vertex*row.VertexStride+offset,4 ) );
	private static void Write( GltfGpuPrimitiveSnapshot row, int vertex, int offset, float value ) => BinaryPrimitives.WriteSingleLittleEndian( row.Bytes.AsSpan( vertex*row.VertexStride+offset,4 ),value );
}
