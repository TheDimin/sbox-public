using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;

namespace Sandbox.Gltf.DirectGpu.TextureCoordinateTestValidation;

using NMatrix = System.Numerics.Matrix4x4;
using NVector3 = System.Numerics.Vector3;

/// <summary>Checks actual own-buffer bytes against frozen independent source values using runtime output declarations.</summary>
public static class TextureCoordinateGpuChecks
{
	public sealed record Measurement( int Primitives, int OutputVertices, int UvStreams, int Indices, float PositionErrorInches, float NormalError, float RawUvError, bool Finite, bool ActualGpuIndices, bool TopologyMatches );
	public static Measurement MeasureCurrent( TextureCoordinateReference.Form form, GltfGpuInstance instance, NMatrix placement )
	{
		TextureCoordinateCoreTests.Require( instance.DrawCalls > 0 && instance.SubmittedRevision == instance.PoseRevision && !instance.CallbackErrors.Any(), "Current GPU pose has not been submitted successfully." );
		return MeasureSnapshots( form, instance.Readback(), placement );
	}
	public static Measurement MeasureSnapshots( TextureCoordinateReference.Form form, IReadOnlyList<GltfGpuPrimitiveSnapshot> snapshots, NMatrix placement )
	{
		TextureCoordinateCoreTests.Require( snapshots.Count == 5 && snapshots.Select( value => value.Node ).Distinct().Count() == 5, "Must observe every independent quad, including the UV-free back plane." );
		TextureCoordinateCoreTests.Require( NMatrix.Invert( placement, out var inverse ), "Placement must be invertible." );
		var normalMatrix = NMatrix.Transpose( inverse ); var positions = 0f; var normals = 0f; var uvs = 0f;
		var vertices = 0; var uvStreams = 0; var indexCount = 0; var finite = true; var gpuIndices = true; var topology = true;
		foreach ( var expected in form.Expected )
		{
			var row = snapshots.Single( value => value.Node == expected.Node && value.Mesh == expected.Mesh && value.Primitive == 0 );
			TextureCoordinateCoreTests.Require( row.VertexCount == 4 && row.SourceVertices.Length == row.VertexCount && row.Bytes.Length == checked(row.VertexCount * row.VertexStride), "Output extent or source provenance changed." );
			TextureCoordinateCoreTests.Require( row.SourceVertices.All( value => value < 4 ) && row.Indices.Length == 6 && row.Indices.All( value => value < row.VertexCount ), "Topology is missing or out of range." );
			var position = Field( "POSITION", 0, 3 ); var normal = Field( "NORMAL", 0, 3 );
			foreach ( var semantic in expected.Streams.Keys.Where( value => value.StartsWith( "TEXCOORD_", StringComparison.Ordinal ) ) ) { _ = Field( "TEXCOORD", int.Parse( semantic[9..] ), 2 ); uvStreams++; }
			for ( var output = 0; output < row.VertexCount; output++ )
			{
				var source = (int)row.SourceVertices[output]; var baseOffset = output * row.VertexStride;
				var desiredPosition = NVector3.Transform( TextureCoordinateReference.EnginePosition( expected.Streams["POSITION"].Values[source] ), placement );
				var desiredNormal = NVector3.Normalize( NVector3.TransformNormal( TextureCoordinateReference.EngineNormal( expected.Streams["NORMAL"].Values[source] ), normalMatrix ) );
				var p = Read3( row.Bytes, baseOffset + position.Offset ); var n = Read3( row.Bytes, baseOffset + normal.Offset );
				finite &= IsFinite( p ) && IsFinite( n ); positions = MathF.Max( positions, Maximum( NVector3.Abs( p - desiredPosition ) ) ); normals = MathF.Max( normals, NVector3.Distance( n, desiredNormal ) );
				foreach ( var (semantic, stream) in expected.Streams.Where( value => value.Key.StartsWith( "TEXCOORD_", StringComparison.Ordinal ) ) )
				{
					var field = Field( "TEXCOORD", int.Parse( semantic[9..] ), 2 );
					for ( var component = 0; component < 2; component++ )
					{
						var value = BinaryPrimitives.ReadSingleLittleEndian( row.Bytes.AsSpan( baseOffset + field.Offset + component * 4 ) ); finite &= float.IsFinite( value );
						uvs = MathF.Max( uvs, MathF.Abs( value - (float)stream.Values[source][component] ) );
					}
				}
			}
			var actualIndices = row.Indices.Select( value => row.SourceVertices[value] ).ToArray(); var desiredIndices = expected.Indices.Values.Select( value => (uint)value[0] ).ToArray();
			if ( placement.GetDeterminant() < 0 ) for ( var index = 0; index < desiredIndices.Length; index += 3 ) (desiredIndices[index + 1], desiredIndices[index + 2]) = (desiredIndices[index + 2], desiredIndices[index + 1]);
			topology &= actualIndices.SequenceEqual( desiredIndices ); gpuIndices &= row.IndexDataOrigin == "gpu-readback"; vertices += row.VertexCount; indexCount += row.Indices.Length;
			GltfGpuField Field( string semantic, int index, int components )
			{
				var field = row.Fields.Single( value => value.Semantic == semantic && value.SemanticIndex == index );
				TextureCoordinateCoreTests.Require( field.ComponentType == 5126 && field.Components == components && field.Offset >= 0 && field.Offset + components * 4 <= row.VertexStride, "Invalid runtime field declaration." ); return field;
			}
		}
		return new( 5, vertices, uvStreams, indexCount, positions, normals, uvs, finite, gpuIndices, topology );
	}
	public static void RequireWithin( Measurement result, float maximumPositionErrorInches, float maximumNormalError, float maximumRawUvError )
	{
		TextureCoordinateCoreTests.Require( float.IsFinite( maximumPositionErrorInches ) && maximumPositionErrorInches >= 0 && float.IsFinite( maximumNormalError ) && maximumNormalError >= 0 && float.IsFinite( maximumRawUvError ) && maximumRawUvError >= 0, "Explicit reviewed numeric thresholds are required." );
		TextureCoordinateCoreTests.Require( result.Primitives == 5 && result.OutputVertices == 20 && result.Indices == 30 && result.UvStreams is 4 or 8 && result.Finite && result.ActualGpuIndices && result.TopologyMatches &&
			result.PositionErrorInches <= maximumPositionErrorInches && result.NormalError <= maximumNormalError && result.RawUvError <= maximumRawUvError, "GPU result differs from the independent source accessor oracle: " + result );
	}
	private static bool IsFinite( NVector3 value ) => float.IsFinite( value.X ) && float.IsFinite( value.Y ) && float.IsFinite( value.Z );
	private static float Maximum( NVector3 value ) => MathF.Max( value.X, MathF.Max( value.Y, value.Z ) );
	private static NVector3 Read3( byte[] bytes, int offset ) => new( BinaryPrimitives.ReadSingleLittleEndian( bytes.AsSpan( offset ) ), BinaryPrimitives.ReadSingleLittleEndian( bytes.AsSpan( offset + 4 ) ), BinaryPrimitives.ReadSingleLittleEndian( bytes.AsSpan( offset + 8 ) ) );
}
