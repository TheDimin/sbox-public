using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;

namespace Sandbox.Gltf.DirectGpu.BoxVertexColorsValidation;

using NMatrix = System.Numerics.Matrix4x4;
using NVector3 = System.Numerics.Vector3;
using NVector4 = System.Numerics.Vector4;
using static BoxVertexColorsReference;

/// <summary>Independent literal expectations; completed actual GPU readback is supplied by the coordinator.</summary>
internal static class BoxVertexColorsGpuChecks
{
	public sealed record Measurement( string Form, int Vertices, int ColorSets, float PositionErrorInches, float NormalError,
		float ColorZeroError, float ColorOneError, bool BoundIndexReadback, bool TopologyMatches, bool Finite );
	public static Measurement Measure( BoxVertexColorsReference reference, Form form, IReadOnlyList<GltfGpuPrimitiveSnapshot> rows, NMatrix placement )
	{
		Require( rows is { Count: 1 }, "Cube must produce one node/primitive output." ); var row = rows[0];
		Require( row.Node == 0 && row.Mesh == 0 && row.Primitive == 0 && row.VertexCount == 24 && row.Bytes.Length == row.VertexCount * row.VertexStride, "Output provenance/count/byte extent changed." );
		Require( row.SourceVertices.Length == 24 && row.SourceVertices.Order().SequenceEqual( Enumerable.Range( 0, 24 ).Select( value => (uint)value ) ), "Source vertices must identify the authored cube exactly once." );
		Require( row.Fields.Select( field => (field.Semantic, field.SemanticIndex) ).Distinct().Count() == row.Fields.Count, "Duplicate output semantics." );
		var ordered = row.Fields.OrderBy( field => field.Offset ).ToArray();
		Require( ordered.All( field => field.ComponentType == 5126 && field.Offset >= 0 && field.Offset % 4 == 0 && field.Offset + field.Components * 4 <= row.VertexStride ), "Invalid output range/format." );
		for ( var index = 1; index < ordered.Length; index++ ) Require( ordered[index - 1].Offset + ordered[index - 1].Components * 4 <= ordered[index].Offset, "Output fields overlap." );
		GltfGpuField Field( string semantic, int semanticIndex, int components )
		{
			var field = row.Fields.Single( field => field.Semantic == semantic && field.SemanticIndex == semanticIndex ); Require( field.Components == components, "Wrong output dimensions: " + semantic ); return field;
		}
		var p = Field( "POSITION", 0, 3 ); var n = Field( "NORMAL", 0, 3 ); var c0 = Field( "COLOR", 0, 4 );
		var c1 = form.Color1.HasValue ? Field( "COLOR", 1, 4 ) : null;
		Require( row.Fields.Count( field => field.Semantic == "COLOR" ) == (form.Color1.HasValue ? 2 : 1), "Color sets were merged/lost." );
		Require( NMatrix.Invert( placement, out var inverse ), "Reference placement is singular." );
		var normalMatrix = NMatrix.Transpose( inverse ); var maximumP = 0f; var maximumN = 0f; var maximumC0 = 0f; var maximumC1 = 0f; var finite = true;
		for ( var outputVertex = 0; outputVertex < row.VertexCount; outputVertex++ )
		{
			var source = (int)row.SourceVertices[outputVertex]; var expectedP = NVector3.Transform( reference.EnginePosition( source ), placement );
			var expectedN = NVector3.Normalize( NVector3.TransformNormal( reference.EngineNormal( source ), normalMatrix ) );
			var actualP = Read( row, p, outputVertex ); var actualN = Read( row, n, outputVertex );
			maximumP = MathF.Max( maximumP, Maximum( actualP - new NVector4( expectedP, 0 ) ) ); maximumN = MathF.Max( maximumN, Maximum( actualN - new NVector4( expectedN, 0 ) ) );
			var actualColor0 = Read( row, c0, outputVertex ); maximumC0 = MathF.Max( maximumC0, Maximum( actualColor0 - form.DrawColor( source ) ) );
			finite &= IsFinite( actualP ) && IsFinite( actualN ) && IsFinite( actualColor0 );
			if ( c1 is not null ) { var actual = Read( row, c1, outputVertex ); maximumC1 = MathF.Max( maximumC1, Maximum( actual - form.Color1.Value ) ); finite &= IsFinite( actual ); }
		}
		var expectedIndices = (uint[])reference.Indices.Clone();
		if ( placement.GetDeterminant() < 0 ) for ( var triangle = 0; triangle < expectedIndices.Length; triangle += 3 ) (expectedIndices[triangle], expectedIndices[triangle + 1]) = (expectedIndices[triangle + 1], expectedIndices[triangle]);
		Require( row.Indices.Length == 36 && row.Indices.All( index => index < 24 ), "Bound index extent/range invalid." );
		var topology = Triangles( expectedIndices ).SequenceEqual( Triangles( row.Indices.Select( index => row.SourceVertices[index] ).ToArray() ) );
		return new( form.Name, row.VertexCount, c1 is null ? 1 : 2, maximumP, maximumN, maximumC0, maximumC1, row.IndexDataOrigin == "gpu-readback", topology, finite );
	}

	public static void RequireWithin( Measurement value, float positionToleranceInches = .0001f, float normalTolerance = .000002f )
	{
		Require( float.IsFinite( positionToleranceInches ) && positionToleranceInches >= 0 && float.IsFinite( normalTolerance ) && normalTolerance >= 0, "Invalid explicit arithmetic thresholds." );
		Require( value.Finite && value.BoundIndexReadback && value.TopologyMatches && value.Vertices == 24, "Nonfinite, CPU-only indices or wrong topology." );
		Require( value.PositionErrorInches <= positionToleranceInches && value.NormalError <= normalTolerance, "GPU geometry differs from independent literal cube." );
		// Two float32 ulps at unit scale bounds integer normalization and the
		// float stores. This is not a general image/material tolerance.
		Require( value.ColorZeroError <= 2.384185791015625e-7f && value.ColorOneError <= 2.384185791015625e-7f, "Linear color, RGB alpha one, or COLOR_0-only clamp disagrees with the independent fixture." );
	}
	internal static NVector4 Read( GltfGpuPrimitiveSnapshot row, GltfGpuField field, int vertex )
	{
		var result = NVector4.Zero;
		for ( var channel = 0; channel < field.Components; channel++ ) result[channel] = BinaryPrimitives.ReadSingleLittleEndian( row.Bytes.AsSpan( vertex * row.VertexStride + field.Offset + channel * 4, 4 ) ); return result;
	}
	private static float Maximum( NVector4 value ) => MathF.Max( MathF.Max( MathF.Abs( value.X ), MathF.Abs( value.Y ) ), MathF.Max( MathF.Abs( value.Z ), MathF.Abs( value.W ) ) );
	private static bool IsFinite( NVector4 value ) => float.IsFinite( value.X ) && float.IsFinite( value.Y ) && float.IsFinite( value.Z ) && float.IsFinite( value.W );
	private static string[] Triangles( uint[] indices ) => Enumerable.Range( 0, indices.Length / 3 ).Select( triangle =>
	{
		var a = indices[triangle * 3]; var b = indices[triangle * 3 + 1]; var c = indices[triangle * 3 + 2];
		if ( b < a && b < c ) (a, b, c) = (b, c, a); else if ( c < a && c < b ) (a, b, c) = (c, a, b); return $"{a}/{b}/{c}";
	} ).OrderBy( value => value, StringComparer.Ordinal ).ToArray();
}

/// <summary>CPU sensitivity controls for the checker. Synthetic bytes are never reported as GPU evidence.</summary>
public static class BoxVertexColorsGpuVerifierTests
{
	public sealed record Result( string Name, string Form, bool Passed, string Error );
	public const int ExpectedCases = 10;
	public static Result[] RunAll( string checkoutRoot )
	{
		var reference = new BoxVertexColorsReference( checkoutRoot ); var form = reference.Forms.Single( value => value.Name == "derived/float3/source" ); var results = new List<Result>();
		Run( "nonstandard_runtime_field_order_accepts_literal_cube", null );
		Run( "reject_position_alias_as_color", row => Write( row, "COLOR", 0, 0, new NVector4( reference.Positions[0], 1 ) ) );
		Run( "reject_all_white_color_output", row => Write( row, "COLOR", 0, 0, NVector4.One ) );
		Run( "reject_srgb_decode_of_linear_vertex_color", row => Write( row, "COLOR", 0, 0, new NVector4( .05087609f, .21404114f, .01434988f, 1 ) ) );
		Run( "reject_rgb_alpha_zero", row => Write( row, "COLOR", 0, 0, new NVector4( form.RawColors[0].X, form.RawColors[0].Y, form.RawColors[0].Z, 0 ) ) );
		Run( "reject_material_factor_baked_into_vertex_color", row => Write( row, "COLOR", 0, 0, form.RawColors[0] * new NVector4( .5f, .25f, .75f, .5f ) ) );
		Run( "reject_clamped_color_one", row => Write( row, "COLOR", 1, 0, NVector4.Clamp( form.Color1.Value, NVector4.Zero, NVector4.One ) ) );
		Run( "reject_nan_color", row => Write( row, "COLOR", 0, 0, new NVector4( float.NaN ) ) );
		Run( "reject_reversed_bound_indices", row => (row.Indices[0], row.Indices[1]) = (row.Indices[1], row.Indices[0]) );
		Run( "reject_wrong_source_map", row => (row.SourceVertices[0], row.SourceVertices[1]) = (row.SourceVertices[1], row.SourceVertices[0]) );
		Require( results.Count == ExpectedCases, "Frozen checker inventory changed." ); return results.ToArray();
		void Run( string name, Action<GltfGpuPrimitiveSnapshot> mutate )
		{
			try
			{
				var row = Literal( reference, form ); mutate?.Invoke( row ); var rejected = false;
				try { BoxVertexColorsGpuChecks.RequireWithin( BoxVertexColorsGpuChecks.Measure( reference, form, new[] { row }, NMatrix.Identity ) ); } catch ( InvalidOperationException ) { rejected = true; }
				Require( rejected == (mutate is not null), "Checker sensitivity had the wrong outcome." ); results.Add( new( name, "synthetic_checker_control", true, null ) );
			}
			catch ( Exception error ) { results.Add( new( name, "synthetic_checker_control", false, error.ToString() ) ); }
		}
	}
	private static GltfGpuPrimitiveSnapshot Literal( BoxVertexColorsReference reference, Form form )
	{
		var row = new GltfGpuPrimitiveSnapshot { Node = 0, Mesh = 0, Primitive = 0, VertexCount = 24, VertexStride = 80, Bytes = new byte[24 * 80],
			Fields = new[] { new GltfGpuField( "COLOR", 0, 0, 4 ), new( "COLOR", 1, 16, 4 ), new( "NORMAL", 0, 32, 3 ), new( "POSITION", 0, 44, 3 ), new( "TEXCOORD", 0, 56, 2 ), new( "TANGENT", 0, 64, 4 ) },
			SourceVertices = Enumerable.Range( 0, 24 ).Select( value => (uint)value ).ToArray(), Indices = (uint[])reference.Indices.Clone(), IndexDataOrigin = "gpu-readback" };
		for ( var vertex = 0; vertex < 24; vertex++ ) { Write( row, "POSITION", 0, vertex, new NVector4( reference.EnginePosition( vertex ), 0 ) ); Write( row, "NORMAL", 0, vertex, new NVector4( reference.EngineNormal( vertex ), 0 ) ); Write( row, "COLOR", 0, vertex, form.DrawColor( vertex ) ); Write( row, "COLOR", 1, vertex, form.Color1.Value ); }
		return row;
	}
	private static void Write( GltfGpuPrimitiveSnapshot row, string semantic, int semanticIndex, int vertex, NVector4 value )
	{
		var field = row.Fields.Single( field => field.Semantic == semantic && field.SemanticIndex == semanticIndex );
		for ( var channel = 0; channel < field.Components; channel++ ) BinaryPrimitives.WriteSingleLittleEndian( row.Bytes.AsSpan( vertex * row.VertexStride + field.Offset + channel * 4, 4 ), value[channel] );
	}
}
