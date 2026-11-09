using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Sandbox.Gltf.DirectGpu.MeshPrimitiveModesValidation;

using NMatrix = global::System.Numerics.Matrix4x4;
using NVector3 = global::System.Numerics.Vector3;
using static MeshPrimitiveModesReference;

/// <summary>Actual output checker with explicit GPU index provenance and dynamic output declarations.</summary>
internal static class MeshPrimitiveModesGpuChecks
{
	public sealed record Measurement( string Status, string Form, int SourceMode, int Vertices, int Indices,
		double MaximumPositionComponentErrorInches, double MaximumTriangleNormalComponentError, string NormalScope );
	public static Measurement Measure( Form form, int mode, NMatrix placement, IReadOnlyList<GltfGpuPrimitiveSnapshot> snapshots )
	{
		Require( snapshots is { Count: 1 }, "Selected-mode probe requires exactly one actual primitive." );
		var row = snapshots[0];
		Require( row.Node == mode && row.Mesh == mode && row.Primitive == 0, "GPU output identifies a different source primitive." );
		Require( row.IndexDataOrigin == "gpu-readback", "Bound indices must come from actual GPU readback." );
		var expectedCount = mode >= 4 ? DrawHexagonIndices[mode].Length : form.LocalVertexCount( mode );
		Require( row.VertexCount == expectedCount && row.VertexStride > 0 && row.Bytes is not null &&
			row.Bytes.LongLength == (long)row.VertexCount * row.VertexStride && row.SourceVertices?.Length == row.VertexCount,
			"Actual output byte/count/source-map declaration is incomplete." );
		Require( row.Indices?.Length == DrawHexagonIndices[mode].Length, "Actual bound index count differs from the independent topology." );
		Require( row.SourceVertices.All( source => source < form.LocalVertexCount( mode ) ), "Output source provenance exceeds the accessor." );
		var expectedTopology = (uint[])DrawHexagonIndices[mode].Clone();
		if ( mode >= 4 && placement.GetDeterminant() < 0 ) for ( var i = 0; i < expectedTopology.Length; i += 3 )
			(expectedTopology[i + 1], expectedTopology[i + 2]) = (expectedTopology[i + 2], expectedTopology[i + 1]);
		for ( var index = 0; index < row.Indices.Length; index++ )
		{
			var output = row.Indices[index]; Require( output < row.VertexCount, "Actual bound index exceeds output buffer." );
			Require( form.HexagonVertex( mode, row.SourceVertices[output] ) == expectedTopology[index],
				"Actual bound indices change a point/line, omit loop closure, reverse strip/fan winding or mishandle reflection." );
		}
		var position = Field( row, "POSITION" );
		var normal = mode >= 4 ? Field( row, "NORMAL" ) : null;
		double maximumPosition = 0, maximumNormal = 0;
		for ( var output = 0; output < row.VertexCount; output++ )
		{
			var actual = Read( row, output, position.Offset );
			var expected = ExpectedPosition( mode, form.HexagonVertex( mode, row.SourceVertices[output] ), placement );
			maximumPosition = Math.Max( maximumPosition, Maximum( expected - actual ) );
			if ( mode >= 4 ) maximumNormal = Math.Max( maximumNormal, Maximum( ExpectedTriangleNormal( placement ) - Read( row, output, normal.Offset ) ) );
		}
		return new( "measured-not-accepted", form.Name, mode, row.VertexCount, row.Indices.Length, maximumPosition, maximumNormal,
			mode >= 4 ? "Independent flat triangle +Z source normal, inverse transpose after basis/placement." : "No authored normal: fallback direction is not a model conformance assertion; no-normal unlit material behavior is a separate gate." );
	}
	public static void AcceptNumeric( Measurement result )
	{
		// Bounded arithmetic error, 32 float eps × the model's largest placed
		// coordinate (< 256 inches). Coordinator must review before acceptance.
		Require( result.MaximumPositionComponentErrorInches <= 32 * 1.1920928955078125e-7 * 256,
			"Position exceeds the declared finite-float arithmetic bound." );
		Require( result.MaximumTriangleNormalComponentError <= 32 * 1.1920928955078125e-7,
			"Flat triangle normal exceeds the declared float arithmetic bound." );
	}
	private static double Maximum( NVector3 v ) => Math.Max( Math.Abs( (double)v.X ), Math.Max( Math.Abs( (double)v.Y ), Math.Abs( (double)v.Z ) ) );
	private static GltfGpuField Field( GltfGpuPrimitiveSnapshot row, string semantic )
	{
		Require( row.Fields is not null, "Runtime output declaration is missing." );
		var fields = row.Fields.Where( x => x.Semantic == semantic && x.SemanticIndex == 0 ).ToArray();
		Require( fields.Length == 1, "Missing or ambiguous runtime field: " + semantic ); var field = fields[0];
		Require( field.ComponentType == 5126 && field.Components == 3 && field.Offset >= 0 && field.Offset % 4 == 0 && field.Offset <= row.VertexStride - 12,
			"Invalid runtime field representation/offset: " + semantic ); return field;
	}
	private static NVector3 Read( GltfGpuPrimitiveSnapshot row, int vertex, int offset )
	{
		var at = vertex * row.VertexStride + offset;
		var value = new NVector3( BinaryPrimitives.ReadSingleLittleEndian( row.Bytes.AsSpan( at ) ), BinaryPrimitives.ReadSingleLittleEndian( row.Bytes.AsSpan( at + 4 ) ), BinaryPrimitives.ReadSingleLittleEndian( row.Bytes.AsSpan( at + 8 ) ) );
		Require( float.IsFinite( value.X ) && float.IsFinite( value.Y ) && float.IsFinite( value.Z ), "Nonfinite actual output value." ); return value;
	}

	/// <summary>17 verifier sensitivity cases only. Manufactured bytes are never submitted or claimed as GPU evidence.</summary>
	public static MeshPrimitiveModesCoreTests.Result[] RunVerifierControls( string checkoutRoot )
	{
		var form = new Inputs( checkoutRoot ).Get( "glTF/source" ); var rows = new List<MeshPrimitiveModesCoreTests.Result>();
		for ( var mode = 0; mode < 7; mode++ )
		{
			var m = mode;
			Run( "padded_output_declaration_" + ModeNames[m], () => AcceptNumeric( Measure( form, m, NMatrix.Identity, [Fixture( form, m )] ) ) );
			Run( "reject_corrupt_bound_index_" + ModeNames[m], () =>
			{
				var fixture = Fixture( form, m ); fixture.Indices[0] = fixture.Indices[1];
				MustReject( () => Measure( form, m, NMatrix.Identity, [fixture] ) );
			} );
		}
		Run( "reject_cpu_index_origin", () => MustReject( () => Measure( form, 0, NMatrix.Identity, [Fixture( form, 0, "cpu-submission-metadata" )] ) ) );
		Run( "reject_wrong_dynamic_output_offset", () =>
		{
			var row = Fixture( form, 0, wrongField: true );
			MustReject( () => AcceptNumeric( Measure( form, 0, NMatrix.Identity, [row] ) ) );
		} );
		Run( "reject_missing_output_primitive", () => MustReject( () => Measure( form, 0, NMatrix.Identity, [] ) ) );
		return rows.ToArray();
		void Run( string name, Action body ) { try { body(); rows.Add( new( name, "verifier-only synthetic", true, null ) ); } catch ( Exception error ) { rows.Add( new( name, "verifier-only synthetic", false, error.ToString() ) ); } }
	}
	private static void MustReject( Action body ) { try { body(); } catch ( InvalidDataException ) { return; } throw new InvalidOperationException( "The negative verifier control was accepted." ); }
	private static GltfGpuPrimitiveSnapshot Fixture( Form form, int mode, string origin = "gpu-readback", bool wrongField = false )
	{
		var sources = mode >= 4 ? (uint[])DrawHexagonIndices[mode].Clone() : Enumerable.Range( 0, 7 ).Select( x => (uint)x ).ToArray();
		var indices = mode >= 4 ? Enumerable.Range( 0, sources.Length ).Select( x => (uint)x ).ToArray() : (uint[])DrawHexagonIndices[mode].Clone();
		const int stride = 52; var bytes = new byte[sources.Length * stride]; Array.Fill( bytes, (byte)0xcd );
		for ( var output = 0; output < sources.Length; output++ )
		{
			Write( output, 8, ExpectedPosition( mode, sources[output], NMatrix.Identity ) ); Write( output, 32, NVector3.UnitX );
		}
		return new GltfGpuPrimitiveSnapshot { Node = mode, Mesh = mode, Primitive = 0, VertexCount = sources.Length, VertexStride = stride,
			Fields = [new( "POSITION", 0, wrongField ? 4 : 8, 3 ), new( "NORMAL", 0, 32, 3 )], Bytes = bytes, SourceVertices = sources, Indices = indices, IndexDataOrigin = origin };
		void Write( int vertex, int offset, NVector3 value )
		{
			var at = vertex * stride + offset; BinaryPrimitives.WriteSingleLittleEndian( bytes.AsSpan( at ), value.X );
			BinaryPrimitives.WriteSingleLittleEndian( bytes.AsSpan( at + 4 ), value.Y ); BinaryPrimitives.WriteSingleLittleEndian( bytes.AsSpan( at + 8 ), value.Z );
		}
	}
}
