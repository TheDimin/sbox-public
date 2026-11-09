using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;

namespace Sandbox.Gltf.DirectGpu.NegativeScaleValidation;
using static NegativeScaleReference;

/// <summary>Actual bound GPU byte/index oracle. Singular normals have no directional expected value.</summary>
public static class NegativeScaleGpuChecks
{
	public sealed record Measurement( int Draws, int Vertices, int DefinedNormals, int UndefinedSingularNormals, double MaximumPositionErrorInches, double MaximumNormalError, double MaximumAuthoredTangentError, double MaximumTangentWError, string IndexOrigin );
	internal static Measurement Assert( JsonElement expected, IReadOnlyList<GltfGpuPrimitiveSnapshot> snapshots, Matrix4x4 placement )
	{
		var rows = expected.GetProperty( "rows" ).EnumerateArray().ToArray(); Require( snapshots.Count == rows.Length, "Expected every selected mesh node, including reused meshes." );
		Require( snapshots.Select( r => (r.Node,r.Mesh,r.Primitive) ).Distinct().Count() == rows.Length, "Duplicate GPU row identity." );
		var maxP = 0d; var maxN = 0d; var maxT = 0d; var maxW = 0d; var vertices = 0; var defined = 0; var undefined = 0;
		foreach ( var row in rows )
		{
			var actual = snapshots.Single( r => r.Node == row.GetProperty( "node" ).GetInt32() && r.Mesh == row.GetProperty( "mesh" ).GetInt32() && r.Primitive == row.GetProperty( "primitive" ).GetInt32() );
			var count = row.GetProperty( "positions" ).GetArrayLength(); Require( actual.VertexCount == count && actual.SourceVertices.Length == count, "Authored normals must preserve all source vertices." );
			Require( actual.Bytes.Length == count * actual.VertexStride && actual.VertexStride > 0, "Actual GPU output storage size is inconsistent." );
			Require( actual.IndexDataOrigin == "gpu-readback", "Index CPU submission metadata is not GPU readback evidence." );
			var pField = Field( actual,"POSITION",3 ); var nField = Field( actual,"NORMAL",3 ); var tField = Field( actual,"TANGENT",4 );
			var directionDefined = Defined( row,placement ); var authoredTangent = row.GetProperty( "tangents" ).ValueKind != JsonValueKind.Null;
			for ( var output = 0; output < count; output++ )
			{
				var source = checked((int)actual.SourceVertices[output]); Require( source == output, "Authored-normal output source map changed unexpectedly." );
				var p = Read( actual,pField,output ); var n = Read( actual,nField,output ); var t = Read( actual,tField,output );
				maxP = Math.Max( maxP, Error( Position( row,source,placement ),p ) );
				// A finite unit/orthogonal fallback remains required at exact singularity;
				// there is deliberately no inverse-transpose direction or W sign oracle there.
				Close( 1,n.Take( 3 ).Sum( x => x * x ),.00002,"Unit normal/fallback" );
				Close( 1,t.Take( 3 ).Sum( x => x * x ),.00002,"Unit tangent/fallback" );
				Close( 0,n.Take( 3 ).Zip( t,(a,b) => a * b ).Sum(),.00002,"Normal/tangent orthogonality" );
				Close( 1,Math.Abs( t[3] ),0,"Finite signed tangent W/fallback" );
				if ( directionDefined )
				{
					defined++; maxN = Math.Max( maxN,Error( Normal( row,source,placement ),n ) );
					if ( authoredTangent ) { var expectedT = Tangent( row,source,placement ); maxT = Math.Max( maxT,Error( expectedT,t ) ); maxW = Math.Max( maxW,Math.Abs( expectedT[3] - t[3] ) ); }
				}
				else undefined++;
			}
			var indices = row.GetProperty( "indices" ).EnumerateArray().Select( x => x.GetUInt32() ).ToArray();
			if ( row.GetProperty( "determinant" ).GetDouble() * Determinant( placement ) < 0 ) for ( var i = 0; i < indices.Length; i += 3 ) (indices[i + 1],indices[i + 2]) = (indices[i + 2],indices[i + 1]);
			Require( actual.Indices.Length == indices.Length, "Actual bound GPU topology count changed." );
			for ( var i = 0; i < indices.Length; i++ ) { Require( actual.Indices[i] < actual.SourceVertices.Length, "Bound GPU index outside output." ); Require( actual.SourceVertices[actual.Indices[i]] == indices[i], "Determinant-dependent bound GPU front-face winding is wrong." ); }
			vertices += count;
		}
		Require( maxP <= .001,"GPU position error exceeds .001 inch: " + maxP ); Require( maxN <= .00002,"GPU inverse-transpose normal error exceeds .00002: " + maxN );
		Require( maxT <= .00002 && maxW == 0,"GPU authored tangent direction or determinant-dependent W is wrong." );
		return new( rows.Length,vertices,defined,undefined,maxP,maxN,maxT,maxW,"gpu-readback" );
	}
	internal static GltfGpuField Field( GltfGpuPrimitiveSnapshot row, string semantic, int components )
	{
		var field = row.Fields.Single( x => x.Semantic == semantic && x.SemanticIndex == 0 ); Require( field.ComponentType == 5126 && field.Components == components && field.Offset >= 0 && field.Offset + components * 4 <= row.VertexStride,"Actual runtime output declaration changed: " + semantic ); return field;
	}
	internal static double[] Read( GltfGpuPrimitiveSnapshot row, GltfGpuField field, int vertex )
	{
		var values = new double[field.Components]; for ( var c = 0; c < values.Length; c++ ) { values[c] = BinaryPrimitives.ReadSingleLittleEndian( row.Bytes.AsSpan( vertex * row.VertexStride + field.Offset + c * 4,4 ) ); Require( double.IsFinite( values[c] ),"GPU output must be finite even at exact zero scale." ); } return values;
	}
	private static double Error( double[] expected, double[] actual ) => Enumerable.Range( 0,3 ).Max( c => Math.Abs( expected[c] - actual[c] ) );
}
