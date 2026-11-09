using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Sandbox.Gltf.DirectGpu.MorphStressTestValidation;

using static MorphStressTestReference;

/// <summary>All source vertices and both primitives vs completed device bytes, with declared runtime offsets.</summary>
internal static class MorphStressTestGpuChecks
{
	public sealed record Measurement( int Primitives, int Vertices, int Indices, int DefinedNormalDirections, int UndefinedNormalDirections,
		double MaximumPositionErrorInches, double MaximumNormalError, double MaximumNormalLengthError, double MaximumUvError,
		double MaximumTangentLengthError, double MaximumTangentNormalDot, double MaximumTangentHandednessError, bool Topology, bool Finite );
	public static Measurement Measure( IReadOnlyList<GltfGpuPrimitiveSnapshot> snapshots, MorphStressTestReference r, double[] weights, Matrix4x4 placement )
	{
		Require( snapshots is { Count: 2 } && snapshots.Select( x => x.Primitive ).Order().SequenceEqual( new[] { 0, 1 } ), "Both distinct MorphStressTest primitives must be drawn exactly once." );
		double positionError = 0, normalError = 0, normalLengthError = 0, uvError = 0, tangentLength = 0, tangentDot = 0, tangentW = 0; var finite = true; var topology = true; var defined = 0; var undefined = 0; var vertices = 0; var indices = 0;
		foreach ( var row in snapshots )
		{
			var p = row.Primitive; var count = r.VertexCount( p );
			Require( row.Node == 0 && row.Mesh == 0 && row.VertexCount == count && row.VertexStride > 0 && row.Bytes.Length == checked(row.VertexStride * count), "Draw identity/extent differs from authored source." );
			Require( row.SourceVertices.Length == count && row.SourceVertices.Order().SequenceEqual( Enumerable.Range( 0, count ).Select( x => (uint)x ) ), "Source vertex identity mapping omitted/duplicated a vertex." );
			Require( row.IndexDataOrigin == "gpu-readback", "Index values must come from the actual bound GPU index buffer." );
			Require( row.Indices.All( x => x < count ), "Out-of-range GPU index." );
			var pos = Field( row, "POSITION", 0, 3 ); var norm = Field( row, "NORMAL", 0, 3 ); var tangent = Field( row, "TANGENT", 0, 4 );
			var uv = new[] { Field( row, "TEXCOORD", 0, 2 ), Field( row, "TEXCOORD", 1, 2 ) };
			var expectedIndices = r.Indices( p ); if ( placement.GetDeterminant() < 0 ) for ( var i = 0; i < expectedIndices.Length; i += 3 ) (expectedIndices[i + 1], expectedIndices[i + 2]) = (expectedIndices[i + 2], expectedIndices[i + 1]);
			topology &= Canonical( expectedIndices ).SequenceEqual( Canonical( row.Indices.Select( i => row.SourceVertices[i] ).ToArray() ) );
			for ( var v = 0; v < count; v++ )
			{
				var source = checked((int)row.SourceVertices[v]); var expectedPosition = r.Engine( p, source, weights, placement ); var expectedNormal = r.Engine( p, source, weights, placement, true );
				var actualPosition = Read( row, v, pos ); var actualNormal = Read( row, v, norm ); var actualTangent = Read( row, v, tangent );
				finite &= actualPosition.All( double.IsFinite ) && actualNormal.All( double.IsFinite ) && actualTangent.All( double.IsFinite );
				for ( var c = 0; c < 3; c++ ) positionError = Math.Max( positionError, Math.Abs( actualPosition[c] - expectedPosition[c] ) );
				if ( expectedNormal is null ) undefined++; else { defined++; for ( var c = 0; c < 3; c++ ) normalError = Math.Max( normalError, Math.Abs( actualNormal[c] - expectedNormal[c] ) ); }
				normalLengthError = Math.Max( normalLengthError, Math.Abs( Math.Sqrt( actualNormal.Sum( x => x * x ) ) - 1 ) );
				tangentLength = Math.Max( tangentLength, Math.Abs( Math.Sqrt( actualTangent.Take( 3 ).Sum( x => x * x ) ) - 1 ) );
				tangentDot = Math.Max( tangentDot, Math.Abs( Enumerable.Range( 0, 3 ).Sum( c => actualTangent[c] * actualNormal[c] ) ) );
				tangentW = Math.Max( tangentW, Math.Abs( actualTangent[3] - (placement.GetDeterminant() < 0 ? -1 : 1) ) );
				// Source has no tangent accessor or deltas. No authored XYZ direction is claimed.
				for ( var set = 0; set < 2; set++ )
				{
					var actual = Read( row, v, uv[set] ); finite &= actual.All( double.IsFinite );
					var expected = r.AccessorValues( r.Primitive( p ).GetProperty( "attributes" ).GetProperty( "TEXCOORD_" + set ).GetInt32() )[source];
					for ( var c = 0; c < 2; c++ ) uvError = Math.Max( uvError, Math.Abs( actual[c] - expected[c] ) );
				}
			}
			vertices += count; indices += row.Indices.Length;
		}
		return new( snapshots.Count, vertices, indices, defined, undefined, positionError, normalError, normalLengthError, uvError, tangentLength, tangentDot, tangentW, topology, finite );
	}
	public static void RequireWithin( Measurement m, double positionToleranceInches, double normalTolerance, double tangentValidityTolerance )
	{
		Require( new[] { positionToleranceInches, normalTolerance, tangentValidityTolerance }.All( x => double.IsFinite( x ) && x >= 0 ), "Explicit reviewed tolerances are required." );
		Require( m.Finite && m.Topology && m.Primitives == 2 && m.Vertices == 1528 && m.Indices == 7236, "Topology, source geometry or finite-value gate failed." );
		Require( m.MaximumPositionErrorInches <= positionToleranceInches && m.MaximumNormalError <= normalTolerance && m.MaximumNormalLengthError <= normalTolerance && m.MaximumUvError == 0,
			$"Morph geometry/normal/UV errors exceed selected limits: {m}." );
		Require( m.MaximumTangentLengthError <= tangentValidityTolerance && m.MaximumTangentNormalDot <= tangentValidityTolerance && m.MaximumTangentHandednessError == 0, "Generated tangent validity gate failed." );
	}
	private static GltfGpuField Field( GltfGpuPrimitiveSnapshot row, string name, int index, int components )
	{
		var fields = row.Fields.Where( f => f.Semantic == name && f.SemanticIndex == index ).ToArray(); Require( fields.Length == 1, "Output field missing/ambiguous: " + name + index ); var f = fields[0];
		Require( f.ComponentType == 5126 && f.Components == components && f.Offset >= 0 && f.Offset % 4 == 0 && f.Offset + components * 4 <= row.VertexStride, "Invalid runtime output representation." ); return f;
	}
	private static double[] Read( GltfGpuPrimitiveSnapshot row, int vertex, GltfGpuField field ) => Enumerable.Range( 0, field.Components ).Select( c => (double)BinaryPrimitives.ReadSingleLittleEndian( row.Bytes.AsSpan( vertex * row.VertexStride + field.Offset + c * 4, 4 ) ) ).ToArray();
	private static string[] Canonical( uint[] indices )
	{
		Require( indices.Length % 3 == 0, "Triangle index count must be divisible by three." ); var triangles = new string[indices.Length / 3];
		for ( var i = 0; i < triangles.Length; i++ ) { var a = indices[i * 3]; var b = indices[i * 3 + 1]; var c = indices[i * 3 + 2]; if ( b < a && b < c ) (a, b, c) = (b, c, a); else if ( c < a && c < b ) (a, b, c) = (c, a, b); triangles[i] = $"{a}/{b}/{c}"; }
		Array.Sort( triangles, StringComparer.Ordinal ); return triangles;
	}
}
