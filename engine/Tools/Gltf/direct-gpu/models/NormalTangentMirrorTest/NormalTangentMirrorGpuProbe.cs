using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

namespace Sandbox.Gltf.DirectGpu.NormalTangentMirrorValidation;
using static NormalTangentMirrorReference;

internal static class NormalTangentMirrorGpuProbe
{
	public const int ExpectedMeasurements = 16;
	public sealed record Measurement( int Vertices, int NegativeW, double PositionErrorInches, double NormalError, double TangentError, double BitangentError, double UvError, string IndexOrigin );
	public sealed record Result( string Form, string Case, Measurement Readback, long RequestedRevision, long SubmittedRevision, int DrawCalls, string MaterialScope );
	public static async Task<IReadOnlyList<Result>> Run( string checkoutRoot, SceneWorld world, Material compute, Func<Task> waitForActualFrames, Func<int?,Material> resolver = null )
	{
		var reference = new NormalTangentMirrorReference( checkoutRoot ); var results = new List<Result>();
		foreach ( var form in reference.Forms )
		{
			using var document = form.Read(); using var asset = GltfGpuAsset.Create( document,compute,resolver ); using var instance = asset.CreateInstance( world );
			foreach ( var placement in new[] { ("identity",Matrix4x4.Identity),("full_affine",Affine),("reflected_affine",Mirrored),("recovered_identity",Matrix4x4.Identity) } )
			{
				instance.Placement = placement.Item2; await waitForActualFrames(); Require( instance.DrawCalls > 0 && instance.SubmittedRevision == instance.PoseRevision && instance.CallbackErrors.Length == 0,"Current revision did not draw successfully: " + string.Join( "\n",instance.CallbackErrors ) );
				results.Add( new( form.Name,placement.Item1,Assert( reference,instance.Readback(),placement.Item2 ),instance.PoseRevision,instance.SubmittedRevision,instance.DrawCalls,resolver is null ? "source material; image fidelity separate" : "diagnostic override; source normal sampling and image fidelity unproven" ) );
			}
		}
		Require( results.Count == ExpectedMeasurements,"GPU inventory changed." ); return results;
	}
	internal static Measurement Assert( NormalTangentMirrorReference reference, IReadOnlyList<GltfGpuPrimitiveSnapshot> snapshots, Matrix4x4 placement )
	{
		Require( snapshots.Count == 1,"Actual model has one selected draw." ); var row = snapshots[0];
		Require( row.Node == 0 && row.Mesh == 0 && row.Primitive == 0 && row.VertexCount == 2770 && row.SourceVertices.Length == 2770 && row.Bytes.Length == row.VertexCount*row.VertexStride,"Actual draw identity or output shape changed." );
		Require( row.IndexDataOrigin == "gpu-readback","CPU submission indices are not bound GPU index readback." );
		var pField = Field( "POSITION",3 ); var nField = Field( "NORMAL",3 ); var tField = Field( "TANGENT",4 ); var uvField = Field( "TEXCOORD",2 );
		var maxP = 0d; var maxN = 0d; var maxT = 0d; var maxB = 0d; var maxUv = 0d; var negative = 0;
		for ( var output = 0; output < row.VertexCount; output++ )
		{
			Require( row.SourceVertices[output] == output,"Authored-normal source mapping changed." ); var p = Read( row,pField,output ); var n = Read( row,nField,output ); var t = Read( row,tField,output ); var uv = Read( row,uvField,output );
			var en = reference.Normal( output,placement ); var et = reference.Tangent( output,placement ); var eb = Cross( en,et ).Select( x => x*et[3] ).ToArray(); var b = Cross( n,t ).Select( x => x*t[3] ).ToArray();
			maxP = Math.Max( maxP,Error( reference.Position( output,placement ),p,3 ) ); maxN = Math.Max( maxN,Error( en,n,3 ) ); maxT = Math.Max( maxT,Error( et,t,3 ) ); maxB = Math.Max( maxB,Error( eb,b,3 ) ); maxUv = Math.Max( maxUv,Error( reference.Values( 4,output ),uv,2 ) );
			Close( et[3],t[3],0,"Authored tangentW times determinant sign" ); if ( t[3] < 0 ) negative++;
		}
		Require( row.Indices.Length == 15720,"Bound index count changed." );
		for ( var i = 0; i < row.Indices.Length; i++ )
		{
			var sourceIndex = Det( placement ) < 0 && i%3 != 0 ? i + (i%3 == 1 ? 1 : -1) : i;
			Require( row.Indices[i] == reference.Values( 0,sourceIndex )[0],"Bound triangle winding did not follow reflected placement." );
		}
		Require( maxP <= .001 && maxN <= .00002 && maxT <= .00002 && maxB <= .00004 && maxUv == 0,$"GPU frame mismatch: P={maxP}, N={maxN}, T={maxT}, B={maxB}, UV={maxUv}." );
		Require( negative == (Det( placement ) < 0 ? 2690 : 80),"Handedness population changed." );
		return new( 2770,negative,maxP,maxN,maxT,maxB,maxUv,"gpu-readback" );
		GltfGpuField Field( string semantic, int components )
		{
			var field = row.Fields.Single( f => f.Semantic == semantic && f.SemanticIndex == 0 ); Require( field.Components == components && field.ComponentType == 5126 && field.Offset >= 0 && field.Offset+components*4 <= row.VertexStride,"Invalid runtime field: " + semantic ); return field;
		}
	}
	private static double[] Read( GltfGpuPrimitiveSnapshot row, GltfGpuField field, int vertex )
	{
		var result = new double[field.Components]; for ( var c = 0; c < result.Length; c++ ) { result[c] = BinaryPrimitives.ReadSingleLittleEndian( row.Bytes.AsSpan( vertex*row.VertexStride+field.Offset+c*4,4 ) ); Require( double.IsFinite( result[c] ),"Nonfinite GPU value." ); } return result;
	}
	private static double Error( double[] a, double[] b, int count ) => Enumerable.Range( 0,count ).Max( i => Math.Abs( a[i]-b[i] ) );
}
