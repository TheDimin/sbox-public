using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Sandbox.Gltf.DirectGpu.Tests;

using NM = System.Numerics.Matrix4x4;
using NV3 = System.Numerics.Vector3;

internal static class FoxGpuChecks
{
	public static object Measure( FoxReference reference, int? clip, float time, NM placement, IReadOnlyList<GltfGpuPrimitiveSnapshot> snapshots )
	{
		Require( snapshots.Count == 1, "Fox must draw exactly one primitive." ); var row = snapshots[0];
		Require( row.Node == reference.MeshNode && row.Mesh == reference.Mesh && row.Primitive == 0, "Fox GPU source provenance changed." );
		Require( row.VertexCount == reference.VertexCount && row.Bytes.Length == row.VertexCount * row.VertexStride && row.SourceVertices.Length == row.VertexCount, "Fox GPU ranges/source map are invalid." );
		Require( row.Indices.Length == reference.VertexCount, "Fox output topology count changed." );
		var reflected = placement.GetDeterminant() < 0;
		for ( var index = 0; index < row.Indices.Length; index++ )
		{
			var expected = reflected && index % 3 != 0 ? index + (index % 3 == 1 ? 1 : -1) : index;
			Require( row.Indices[index] < row.VertexCount && row.SourceVertices[row.Indices[index]] == expected, "Fox output changes triangle source identity/winding." );
		}
		var fields = row.Fields.ToDictionary( x => (x.Semantic, x.SemanticIndex) );
		GltfGpuField Field( string semantic, int components )
		{
			var field = fields[(semantic, 0)]; Require( field.ComponentType == 5126 && field.Components == components && field.Offset >= 0 && field.Offset + components * 4 <= row.VertexStride, "Invalid actual GPU field: " + semantic ); return field;
		}
		var position = Field( "POSITION", 3 ); var normal = Field( "NORMAL", 3 ); var uv = Field( "TEXCOORD", 2 ); var color = Field( "COLOR", 4 ); var tangent = Field( "TANGENT", 4 );
		var expectedPose = reference.ToEngine( reference.Evaluate( clip, time ), placement );
		var maximumPosition = 0d; var positionSum = 0d; var maximumNormal = 0d; var normalSum = 0d; var maximumAngle = 0d;
		var maximumUv = 0d; var maximumNormalLength = 0d; var maximumTangentOrthogonality = 0d; var maximumTangentLength = 0d;
		for ( var output = 0; output < row.VertexCount; output++ )
		{
			var source = row.SourceVertices[output]; Require( source < reference.VertexCount, "Fox GPU source vertex map is out of range." );
			var p = Vector( row, output, position ); var n = Vector( row, output, normal ); var t = Vector( row, output, tangent );
			var pError = Distance( p, expectedPose.Positions[source] ); var nError = Distance( n, expectedPose.Normals[source] );
			maximumPosition = Math.Max( maximumPosition, pError ); positionSum += pError * pError; maximumNormal = Math.Max( maximumNormal, nError ); normalSum += nError * nError;
			var nLength = Length( n ); var expectedLength = Length( expectedPose.Normals[source] ); Require( nLength > 0 && expectedLength > 0, "Fox GPU generated a zero normal." );
			var dot = (double)n.X * expectedPose.Normals[source].X + (double)n.Y * expectedPose.Normals[source].Y + (double)n.Z * expectedPose.Normals[source].Z;
			maximumAngle = Math.Max( maximumAngle, Math.Acos( Math.Clamp( dot / (nLength * expectedLength), -1, 1 ) ) * 180 / Math.PI );
			maximumNormalLength = Math.Max( maximumNormalLength, Math.Abs( nLength - 1 ) ); maximumTangentLength = Math.Max( maximumTangentLength, Math.Abs( Length( t ) - 1 ) );
			maximumTangentOrthogonality = Math.Max( maximumTangentOrthogonality, Math.Abs( (double)t.X * n.X + (double)t.Y * n.Y + (double)t.Z * n.Z ) );
			for ( var c = 0; c < 2; c++ ) maximumUv = Math.Max( maximumUv, Math.Abs( Scalar( row, output, uv, c ) - reference.TexCoords[source * 2 + c] ) );
			for ( var c = 0; c < 4; c++ ) Require( Scalar( row, output, color, c ) == 1, "Absent Fox color must default to linear white." );
			Require( Math.Abs( Scalar( row, output, tangent, 3 ) ) == 1, "Generated Fox tangent handedness is invalid." );
		}
		return new { status = "measured-not-accepted", clip, time, vertex_count = row.VertexCount, vertex_stride = row.VertexStride, row.Fields,
			position_maximum_inches = maximumPosition, position_rms_inches = Math.Sqrt( positionSum / row.VertexCount ), normal_maximum_distance = maximumNormal,
			normal_rms_distance = Math.Sqrt( normalSum / row.VertexCount ), normal_maximum_angle_degrees = maximumAngle, normal_maximum_length_error = maximumNormalLength,
			uv_maximum_component_error = maximumUv, generated_tangent_maximum_length_error = maximumTangentLength, generated_tangent_maximum_normal_dot = maximumTangentOrthogonality,
			scope = "Actual GPU output versus independent source oracle. Source material texture/lighting, rendered appearance and wallclock timing require separate evidence; no arbitrary numeric tolerance approved here." };
	}
	private static NV3 Vector( GltfGpuPrimitiveSnapshot row, int vertex, GltfGpuField field ) => new( Scalar( row, vertex, field, 0 ), Scalar( row, vertex, field, 1 ), Scalar( row, vertex, field, 2 ) );
	private static float Scalar( GltfGpuPrimitiveSnapshot row, int vertex, GltfGpuField field, int component )
	{
		var result = BinaryPrimitives.ReadSingleLittleEndian( row.Bytes.AsSpan( vertex * row.VertexStride + field.Offset + component * 4, 4 ) ); Require( float.IsFinite( result ), "Fox GPU emitted nonfinite field data." ); return result;
	}
	private static double Distance( NV3 a, NV3 b ) => Math.Sqrt( Math.Pow( (double)a.X - b.X, 2 ) + Math.Pow( (double)a.Y - b.Y, 2 ) + Math.Pow( (double)a.Z - b.Z, 2 ) );
	private static double Length( NV3 a ) => Math.Sqrt( (double)a.X * a.X + (double)a.Y * a.Y + (double)a.Z * a.Z );
	private static void Require( bool value, string message ) { if ( !value ) throw new InvalidDataException( message ); }
}
