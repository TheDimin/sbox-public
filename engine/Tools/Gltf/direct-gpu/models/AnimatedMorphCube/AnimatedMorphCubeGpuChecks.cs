using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Sandbox.Gltf.DirectGpu.Validation;

using NMatrix = System.Numerics.Matrix4x4;
using NVector = System.Numerics.Vector3;

/// <summary>Independent source-oracle vs completed GPU bytes; never a replacement mesh producer.</summary>
internal static class AnimatedMorphCubeGpuChecks
{
	public sealed record Measurement( int OutputVertices, int Indices, double SourceTime, float MaximumPositionErrorInches,
		float MaximumNormalError, bool SourceTopologyMatches, bool Finite );
	public sealed record TangentMeasurement( bool AuthoredDirectionCompared, int Vertices, int DirectionComparedVertices,
		int UndefinedAuthoredDirectionVertices, float MaximumDirectionError,
		float MaximumLengthError, float MaximumNormalDot, float MaximumHandednessError, bool Finite );

	public static Measurement Measure( IReadOnlyList<GltfGpuPrimitiveSnapshot> snapshots, JsonElement form, JsonElement sample, NMatrix placement )
	{
		Require( snapshots is not null && snapshots.Count == 1, "AnimatedMorphCube needs one drawn node/primitive." );
		var row = snapshots[0];
		Require( row.Node == form.GetProperty( "node" ).GetInt32() && row.Mesh == form.GetProperty( "mesh" ).GetInt32() && row.Primitive == 0,
			"Readback lost source node/mesh/primitive identity." );
		var sourceCount = form.GetProperty( "vertexCount" ).GetInt32();
		Require( row.VertexCount > 0 && row.VertexStride > 0 && row.Bytes.Length == checked(row.VertexCount * row.VertexStride), "Readback extent disagrees with declared output layout." );
		Require( row.SourceVertices.Length == row.VertexCount && row.SourceVertices.All( value => value < sourceCount ), "Missing/out-of-range original vertex mapping." );
		Require( row.Indices.All( value => value < row.VertexCount ), "Out-of-range output index." );
		var position = row.Fields.Single( field => field.Semantic == "POSITION" && field.SemanticIndex == 0 );
		var normal = row.Fields.Single( field => field.Semantic == "NORMAL" && field.SemanticIndex == 0 );
		Require( position.ComponentType == 5126 && position.Components >= 3 && position.Offset >= 0 && position.Offset + 12 <= row.VertexStride,
			"Position output is not its declared FLOAT3 representation." );
		Require( normal.ComponentType == 5126 && normal.Components >= 3 && normal.Offset >= 0 && normal.Offset + 12 <= row.VertexStride,
			"Normal output is not its declared FLOAT3 representation." );
		Require( NMatrix.Invert( placement, out var inverse ), "This reference comparison needs invertible placement." );
		var normalMatrix = NMatrix.Transpose( inverse );
		var positions = sample.GetProperty( "positionsInches" );
		var normals = sample.GetProperty( "normals" );
		var maximumPosition = 0f; var maximumNormal = 0f; var finite = true;
		for ( var vertex = 0; vertex < row.VertexCount; vertex++ )
		{
			var sourceVertex = checked((int)row.SourceVertices[vertex]);
			var expectedPosition = NVector.Transform( ReadJsonVector( positions[sourceVertex] ), placement );
			var expectedNormal = NVector.Normalize( NVector.TransformNormal( ReadJsonVector( normals[sourceVertex] ), normalMatrix ) );
			var actualPosition = ReadVector( row.Bytes, vertex * row.VertexStride + position.Offset );
			var actualNormal = ReadVector( row.Bytes, vertex * row.VertexStride + normal.Offset );
			finite &= IsFinite( actualPosition ) && IsFinite( actualNormal );
			maximumPosition = MathF.Max( maximumPosition, NVector.Abs( expectedPosition - actualPosition ).MaxComponent() );
			maximumNormal = MathF.Max( maximumNormal, NVector.Distance( expectedNormal, actualNormal ) );
		}
		var expectedIndices = form.GetProperty( "sourceIndices" ).EnumerateArray().Select( value => value.GetUInt32() ).ToArray();
		var mappedIndices = row.Indices.Select( index => row.SourceVertices[index] ).ToArray();
		var topology = CanonicalTriangles( mappedIndices ).SequenceEqual( CanonicalTriangles( expectedIndices ) );
		return new Measurement( row.VertexCount, row.Indices.Length, sample.GetProperty( "time" ).GetDouble(), maximumPosition, maximumNormal, topology, finite );
	}

	/// <summary>Measured error alone is not a pass; tolerances require explicit review.</summary>
	public static void RequireWithin( Measurement measurement, float positionToleranceInches, float normalTolerance )
	{
		Require( float.IsFinite( positionToleranceInches ) && positionToleranceInches >= 0 && float.IsFinite( normalTolerance ) && normalTolerance >= 0, "Invalid explicitly selected tolerances." );
		Require( measurement.Finite && measurement.SourceTopologyMatches, "Readback is nonfinite or changed source topology/winding." );
		Require( measurement.MaximumPositionErrorInches <= positionToleranceInches, $"Position error {measurement.MaximumPositionErrorInches:R} exceeds {positionToleranceInches:R} inches." );
		Require( measurement.MaximumNormalError <= normalTolerance, $"Normal error {measurement.MaximumNormalError:R} exceeds {normalTolerance:R}." );
	}

	/// <summary>Authored tangent deltas/handedness, or validity of generated tangents when absent.</summary>
	public static TangentMeasurement MeasureTangents( IReadOnlyList<GltfGpuPrimitiveSnapshot> snapshots, JsonElement sample, NMatrix placement )
	{
		Require( snapshots is not null && snapshots.Count == 1, "AnimatedMorphCube needs one tangent output row." );
		var row = snapshots[0];
		var tangent = row.Fields.Single( field => field.Semantic == "TANGENT" && field.SemanticIndex == 0 );
		var normal = row.Fields.Single( field => field.Semantic == "NORMAL" && field.SemanticIndex == 0 );
		Require( tangent.ComponentType == 5126 && tangent.Components == 4 && tangent.Offset >= 0 && tangent.Offset + 16 <= row.VertexStride, "Tangent is not its declared FLOAT4 representation." );
		Require( normal.ComponentType == 5126 && normal.Components >= 3 && normal.Offset >= 0 && normal.Offset + 12 <= row.VertexStride, "Normal is not its declared FLOAT3 representation." );
		var authored = sample.GetProperty( "tangents" );
		var compare = authored.ValueKind == JsonValueKind.Array;
		var undefinedReferences = compare ? sample.GetProperty( "undefinedTangentDirections" ).EnumerateArray()
			.ToDictionary( value => value.GetProperty( "vertex" ).GetInt32(), value => value.GetProperty( "reason" ).GetString() )
			: new Dictionary<int, string>();
		var directionError = 0f; var lengthError = 0f; var normalDot = 0f; var handednessError = 0f; var finite = true;
		var comparedDirections = 0; var undefinedDirections = 0;
		var placementSign = placement.GetDeterminant() < 0 ? -1f : 1f;
		Require( NMatrix.Invert( placement, out var inverse ), "Tangent reference needs invertible placement." );
		var normalMatrix = NMatrix.Transpose( inverse );
		for ( var vertex = 0; vertex < row.VertexCount; vertex++ )
		{
			var actual = ReadVector( row.Bytes, vertex * row.VertexStride + tangent.Offset );
			var actualW = BinaryPrimitives.ReadSingleLittleEndian( row.Bytes.AsSpan( vertex * row.VertexStride + tangent.Offset + 12, 4 ) );
			var observedNormal = ReadVector( row.Bytes, vertex * row.VertexStride + normal.Offset );
			finite &= IsFinite( actual ) && float.IsFinite( actualW ) && IsFinite( observedNormal );
			lengthError = MathF.Max( lengthError, MathF.Abs( actual.Length() - 1 ) );
			normalDot = MathF.Max( normalDot, MathF.Abs( NVector.Dot( actual, observedNormal ) ) );
			var expectedW = placementSign * sample.GetProperty( "nodeHandedness" ).GetSingle();
			if ( compare )
			{
				var source = checked((int)row.SourceVertices[vertex]);
				expectedW = placementSign * sample.GetProperty( "tangentHandedness" )[source].GetSingle();
				if ( authored[source].ValueKind == JsonValueKind.Null )
				{
					// Source N/T may be parallel or a signed morph may cancel T.
					// No direction is invented; finite/unit/orthogonal and W gates
					// above/below remain strict for this exact vertex.
					Require( undefinedReferences.TryGetValue( source, out var reason ) && reason is "zero_morphed_tangent" or "morphed_tangent_parallel_to_normal" or "projected_world_tangent_zero",
						"An undefined direction needs an explicit independent source diagnosis." );
					undefinedDirections++;
				}
				else
				{
					Require( authored[source].ValueKind == JsonValueKind.Array, "Malformed authored tangent reference." );
					Require( !undefinedReferences.ContainsKey( source ), "A defined direction must not be marked undefined." );
					var expectedNormal = NVector.Normalize( NVector.TransformNormal( ReadJsonVector( sample.GetProperty( "normals" )[source] ), normalMatrix ) );
					var expected = NVector.TransformNormal( ReadJsonVector( authored[source] ), placement );
					expected = NVector.Normalize( expected - expectedNormal * NVector.Dot( expected, expectedNormal ) );
					directionError = MathF.Max( directionError, NVector.Distance( expected, actual ) );
					comparedDirections++;
				}
			}
			handednessError = MathF.Max( handednessError, MathF.Abs( expectedW - actualW ) );
		}
		return new TangentMeasurement( comparedDirections > 0, row.VertexCount, comparedDirections, undefinedDirections,
			directionError, lengthError, normalDot, handednessError, finite );
	}

	public static void RequireTangentsWithin( TangentMeasurement measurement, float directionTolerance, float lengthTolerance, float orthogonalityTolerance )
	{
		Require( float.IsFinite( directionTolerance ) && directionTolerance >= 0 && float.IsFinite( lengthTolerance ) && lengthTolerance >= 0 && float.IsFinite( orthogonalityTolerance ) && orthogonalityTolerance >= 0, "Invalid explicitly selected tangent tolerances." );
		Require( measurement.Finite && measurement.MaximumHandednessError == 0, "Tangent is nonfinite or its handedness changed." );
		Require( measurement.MaximumDirectionError <= directionTolerance && measurement.MaximumLengthError <= lengthTolerance && measurement.MaximumNormalDot <= orthogonalityTolerance,
			$"Tangent direction/length/normal-dot errors {measurement.MaximumDirectionError:R}/{measurement.MaximumLengthError:R}/{measurement.MaximumNormalDot:R} exceed reviewed tolerances." );
	}

	/// <summary>Literal diagnostic reference draw data, not implementation input or a GPU result.</summary>
	public static Sandbox.Vertex[] ReferenceTriangles( JsonElement form, JsonElement sample, NMatrix placement )
	{
		Require( NMatrix.Invert( placement, out var inverse ), "Reference needs invertible placement." );
		var normalMatrix = NMatrix.Transpose( inverse );
		var positions = sample.GetProperty( "positionsInches" ); var normals = sample.GetProperty( "normals" );
		var indices = form.GetProperty( "sourceIndices" ).EnumerateArray().Select( value => value.GetInt32() ).ToArray();
		var output = new Sandbox.Vertex[indices.Length];
		for ( var corner = 0; corner < output.Length; corner++ )
		{
			var index = indices[corner];
			var position = NVector.Transform( ReadJsonVector( positions[index] ), placement );
			var normal = NVector.Normalize( NVector.TransformNormal( ReadJsonVector( normals[index] ), normalMatrix ) );
			var tangent = NVector.Normalize( NVector.Cross( MathF.Abs( normal.Z ) < .9f ? NVector.UnitZ : NVector.UnitY, normal ) );
			output[corner] = new Sandbox.Vertex( new global::Vector3( position.X, position.Y, position.Z ) )
			{
				Normal = new global::Vector3( normal.X, normal.Y, normal.Z ),
				Tangent = new global::Vector4( tangent.X, tangent.Y, tangent.Z, 1 ), Color = global::Color32.White
			};
		}
		return output;
	}

	private static NVector ReadJsonVector( JsonElement vector ) => new( vector[0].GetSingle(), vector[1].GetSingle(), vector[2].GetSingle() );
	private static NVector ReadVector( byte[] bytes, int offset ) => new( BinaryPrimitives.ReadSingleLittleEndian( bytes.AsSpan( offset, 4 ) ),
		BinaryPrimitives.ReadSingleLittleEndian( bytes.AsSpan( offset + 4, 4 ) ), BinaryPrimitives.ReadSingleLittleEndian( bytes.AsSpan( offset + 8, 4 ) ) );
	private static bool IsFinite( NVector value ) => float.IsFinite( value.X ) && float.IsFinite( value.Y ) && float.IsFinite( value.Z );
	private static float MaxComponent( this NVector value ) => MathF.Max( value.X, MathF.Max( value.Y, value.Z ) );
	private static void Require( bool condition, string message ) { if ( !condition ) throw new InvalidOperationException( message ); }

	private static string[] CanonicalTriangles( uint[] indices )
	{
		Require( indices.Length % 3 == 0, "Triangle indices must come in triples." );
		var output = new string[indices.Length / 3];
		for ( var triangle = 0; triangle < output.Length; triangle++ )
		{
			var offset = triangle * 3; var a = indices[offset]; var b = indices[offset + 1]; var c = indices[offset + 2];
			if ( b < a && b < c ) (a, b, c) = (b, c, a);
			else if ( c < a && c < b ) (a, b, c) = (c, a, b);
			output[triangle] = $"{a}/{b}/{c}";
		}
		Array.Sort( output, StringComparer.Ordinal ); return output;
	}
}
