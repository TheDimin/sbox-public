using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;

namespace Sandbox.Gltf.DirectGpu.MorphPrimitivesValidation;

using NMatrix = System.Numerics.Matrix4x4;
using NVector = System.Numerics.Vector3;
using static MorphPrimitivesReference;

/// <summary>Independent frozen raw-source oracle versus completed GPU vertex/index bytes.</summary>
internal static class MorphPrimitivesGpuChecks
{
	public sealed record Measurement( int Primitive, int Vertices, int Indices, float Weight, float MaximumPositionErrorInches,
		float MaximumNormalComponentError, float MaximumUvError, float MaximumDefaultColorError, float MaximumTangentLengthError, float MaximumTangentNormalDot );
	public static Measurement[] AssertReadback( Form form, IReadOnlyList<GltfGpuPrimitiveSnapshot> snapshots, float weight, NMatrix placement, bool derivedLayout = false )
	{
		Require( snapshots is not null && snapshots.Count == 2 && snapshots.Select( x => x.Primitive ).Order().SequenceEqual( new[] { 0, 1 } ), "Both red and green primitive rows must occur exactly once; missing primitives are failures." );
		return snapshots.OrderBy( x => x.Primitive ).Select( row => AssertRow( form, row, weight, placement, derivedLayout ) ).ToArray();
	}
	public static Measurement AssertRow( Form form, GltfGpuPrimitiveSnapshot row, float weight, NMatrix placement, bool derivedLayout = false )
	{
		var p = row.Primitive; Require( p is 0 or 1 && row.Node == 1 && row.Mesh == 0, "Readback source identity changed." );
		Require( row.IndexDataOrigin == "gpu-readback", "Topology must be actual bound GPU-index readback, never CPU plan metadata." );
		Require( row.VertexCount == Count( p ) && row.SourceVertices.SequenceEqual( Enumerable.Range( 0, Count( p ) ).Select( x => (uint)x ) ), "Authored-normal primitive vertex identity/count changed." );
		var indices = derivedLayout && p == 1 ? Enumerable.Range( 0, 9 ).Select( x => (uint)x ).ToArray() : Indices( form, p ); var reversed = placement.GetDeterminant() < 0;
		if ( reversed ) for ( var i = 0; i < indices.Length; i += 3 ) (indices[i + 1], indices[i + 2]) = (indices[i + 2], indices[i + 1]);
		Require( row.Indices.SequenceEqual( indices ) && row.Indices.All( x => x < row.VertexCount ), "Primitive-local topology/winding differs from independently decoded source." );
		Require( row.VertexStride > 0 && row.Bytes.Length == checked(row.VertexCount * row.VertexStride), "Output byte extent does not match runtime declaration." );
		var fields = row.Fields.OrderBy( x => x.Offset ).ToArray(); var occupied = new HashSet<int>();
		foreach ( var f in fields )
		{
			Require( f.ComponentType == 5126 && f.Components is >= 1 and <= 4 && f.Offset >= 0 && f.Offset % 4 == 0 && f.Offset + f.Components * 4 <= row.VertexStride, "Invalid runtime output field." );
			foreach ( var offset in Enumerable.Range( f.Offset, f.Components * 4 ) ) Require( occupied.Add( offset ), "Overlapping output fields." );
		}
		var position = Field( "POSITION", 3 ); var normal = Field( "NORMAL", 3 ); var uv = Field( "TEXCOORD", 2 ); var color = Field( "COLOR", 4 ); var tangent = Field( "TANGENT", 4 );
		var maxPosition = 0f; var maxNormal = 0f; var maxUv = 0f; var maxColor = 0f; var maxTangentLength = 0f; var maxTangentDot = 0f;
		for ( var v = 0; v < row.VertexCount; v++ )
		{
			foreach ( var f in fields ) for ( var c = 0; c < f.Components; c++ ) _ = Read( f, v, c );
			var actualPosition = Read3( position, v ); var actualNormal = Read3( normal, v ); var actualTangent = Read3( tangent, v );
			maxPosition = MathF.Max( maxPosition, Error( actualPosition, ExpectedPosition( form, p, v, weight, placement, derivedLayout ) ) );
			// The source explicitly authors normals; omitted target NORMAL must
			// preserve those values even though the position morph raises a hill.
			maxNormal = MathF.Max( maxNormal, Error( actualNormal, EngineNormal( form, p, v, placement ) ) );
			for ( var c = 0; c < 2; c++ ) maxUv = MathF.Max( maxUv, MathF.Abs( Read( uv, v, c ) - ExpectedUv( form, p, v, c, derivedLayout ) ) );
			for ( var c = 0; c < 4; c++ ) maxColor = MathF.Max( maxColor, MathF.Abs( Read( color, v, c ) - 1 ) );
			maxTangentLength = MathF.Max( maxTangentLength, MathF.Abs( actualTangent.Length() - 1 ) );
			maxTangentDot = MathF.Max( maxTangentDot, MathF.Abs( NVector.Dot( actualNormal, actualTangent ) ) );
			// There is no authored tangent or normal texture here. The generated
			// direction/sign is not a unique core conformance oracle; require a
			// valid signed basis value without inventing a universal +1 policy.
			Require( MathF.Abs( Read( tangent, v, 3 ) ) == 1, "Generated tangent basis handedness must be finite and signed unit magnitude." );
		}
		var measurement = new Measurement( p, row.VertexCount, row.Indices.Length, weight, maxPosition, maxNormal, maxUv, maxColor, maxTangentLength, maxTangentDot );
		Require( maxPosition <= .0002f && maxNormal <= .00002f && maxUv <= (derivedLayout ? .00000012f : 0) && maxColor == 0 && maxTangentLength <= .00002f && maxTangentDot <= .00002f,
			$"Primitive {p} GPU errors exceed frozen arithmetic thresholds: {measurement}." );
		return measurement;
		GltfGpuField Field( string semantic, int components ) { var f = fields.Single( f => f.Semantic == semantic && f.SemanticIndex == 0 ); Require( f.Components == components, "Output dimension changed: " + semantic ); return f; }
		float Read( GltfGpuField f, int v, int c ) { var value = BinaryPrimitives.ReadSingleLittleEndian( row.Bytes.AsSpan( v * row.VertexStride + f.Offset + c * 4, 4 ) ); Require( float.IsFinite( value ), "Nonfinite output component." ); return value; }
		NVector Read3( GltfGpuField f, int v ) => new( Read( f, v, 0 ), Read( f, v, 1 ), Read( f, v, 2 ) );
	}
	internal static NVector ExpectedPosition( Form form, int primitive, int vertex, float weight, NMatrix placement, bool derivedLayout )
	{
		if ( !derivedLayout || primitive == 0 ) return EnginePosition( form, primitive, vertex, weight, placement );
		var source = new NVector( MorphPrimitivesLayoutFixture.PositionComponent( form, vertex, 0 ) / 32767f, MorphPrimitivesLayoutFixture.PositionComponent( form, vertex, 1 ) / 32767f, MorphPrimitivesLayoutFixture.PositionComponent( form, vertex, 2 ) / 32767f );
		var delta = Vector( Primitive( form, primitive ).GetProperty( "morph" ).GetProperty( "values" )[vertex] ); var local = source + delta * weight;
		return NVector.Transform( new NVector( local.Z, local.X, local.Y ) / .0254f, placement );
	}
	internal static float ExpectedUv( Form form, int primitive, int vertex, int component, bool derivedLayout ) => derivedLayout && primitive == 1 ? MorphPrimitivesLayoutFixture.UvComponent( form, vertex, component ) / 255f : Uv( form, primitive, vertex, component );
	private static float Error( NVector a, NVector b ) => MathF.Max( MathF.Abs( a.X - b.X ), MathF.Max( MathF.Abs( a.Y - b.Y ), MathF.Abs( a.Z - b.Z ) ) );
}

/// <summary>Synthetic verifier controls; not GPU execution or source-versus-compiled self-comparison.</summary>
public static class MorphPrimitivesGpuVerifierTests
{
	public const int ExpectedCases = 13;
	public static MorphPrimitivesCoreTests.Result[] RunAll( string checkoutRoot )
	{
		var reference = new MorphPrimitivesReference( checkoutRoot ); var form = reference.Forms.Single( x => x.Name == "glTF/source" ); var rows = new List<MorphPrimitivesCoreTests.Result>();
		Run( "nonstandard_output_order_and_source_default_both_primitives", .5f, NMatrix.Identity, null, false );
		Run( "signed_weight_both_primitives", -.75f, NMatrix.Identity, null, false );
		Run( "full_affine_placement_preserves_omitted_normal_and_uv", 1.5f, new NMatrix( 2, .2f, 0, 0, 0, 3, .4f, 0, .3f, 0, 1.2f, 0, 7, -11, 13, 1 ), null, false );
		Run( "missing_second_primitive_rejected", .5f, NMatrix.Identity, s => s.RemoveAt( 1 ), true );
		Run( "primitive_local_target_offset_rejected", .5f, NMatrix.Identity, s => Write( s[1].Bytes, 40, 1000 ), true );
		Run( "omitted_target_normal_must_preserve_authored_normal", .5f, NMatrix.Identity, s => Write( s[1].Bytes, 28, .5f ), true );
		Run( "wrong_uv_view_offset_rejected", .5f, NMatrix.Identity, s => Write( s[0].Bytes, 16, .25f ), true );
		Run( "gpu_bound_index_corruption_rejected", .5f, NMatrix.Identity, s => (s[1].Indices[1], s[1].Indices[2]) = (s[1].Indices[2], s[1].Indices[1]), true );
		Run( "CPU_index_submission_metadata_is_not_GPU_readback", .5f, NMatrix.Identity, s => s[1] = Copy( s[1], "cpu-plan" ), true );
		Run( "missing_morph_clamps_signed_weight_rejected", -.75f, NMatrix.Identity, s => s[1] = Literal( form, 1, 0, NMatrix.Identity ), true );
		Run( "nonfinite_attribute_rejected", .5f, NMatrix.Identity, s => Write( s[1].Bytes, 0, float.NaN ), true );
		Run( "derived_sparse_strided_mixed_normalized_and_nonindexed_positive", -.75f, NMatrix.Identity, null, false, true );
		Run( "derived_normalized_uv_wrong_divisor_rejected", -.75f, NMatrix.Identity, s => Write( s[1].Bytes, 16, 100 ), true, true );
		Require( rows.Count == ExpectedCases, "Verifier case inventory changed." ); return rows.ToArray();
		void Run( string name, float weight, NMatrix placement, Action<List<GltfGpuPrimitiveSnapshot>> mutate, bool reject, bool derivedLayout = false )
		{
			MorphPrimitivesCoreTests.RunCase( rows, name, "synthetic_checker_control", () =>
			{
				var snapshots = new List<GltfGpuPrimitiveSnapshot> { Literal( form, 0, weight, placement, derivedLayout ), Literal( form, 1, weight, placement, derivedLayout ) }; mutate?.Invoke( snapshots ); var rejected = false;
				try { _ = MorphPrimitivesGpuChecks.AssertReadback( form, snapshots, weight, placement, derivedLayout ); } catch ( InvalidOperationException ) { rejected = true; }
				Require( rejected == reject, "Verifier accepted/rejected the wrong control." );
			} );
		}
	}
	private static GltfGpuPrimitiveSnapshot Literal( Form form, int primitive, float weight, NMatrix placement, bool derivedLayout = false )
	{
		var count = Count( primitive ); var bytes = new byte[count * 64];
		for ( var v = 0; v < count; v++ )
		{
			var n = EngineNormal( form, primitive, v, placement ); var p = MorphPrimitivesGpuChecks.ExpectedPosition( form, primitive, v, weight, placement, derivedLayout ); var t = NVector.Normalize( NVector.Cross( n, MathF.Abs( n.X ) < .7f ? NVector.UnitX : NVector.UnitY ) );
			for ( var c = 0; c < 4; c++ ) Write( bytes, v * 64 + c * 4, 1 );
			for ( var c = 0; c < 2; c++ ) Write( bytes, v * 64 + 16 + c * 4, MorphPrimitivesGpuChecks.ExpectedUv( form, primitive, v, c, derivedLayout ) );
			Write3( bytes, v * 64 + 24, n ); Write3( bytes, v * 64 + 36, p ); Write3( bytes, v * 64 + 48, t ); Write( bytes, v * 64 + 60, placement.GetDeterminant() < 0 ? -1 : 1 );
		}
		var indices = derivedLayout && primitive == 1 ? Enumerable.Range( 0, 9 ).Select( x => (uint)x ).ToArray() : Indices( form, primitive ); if ( placement.GetDeterminant() < 0 ) for ( var i = 0; i < indices.Length; i += 3 ) (indices[i + 1], indices[i + 2]) = (indices[i + 2], indices[i + 1]);
		return new() { Node = 1, Mesh = 0, Primitive = primitive, VertexCount = count, VertexStride = 64, Bytes = bytes,
			Fields = new[] { new GltfGpuField( "COLOR", 0, 0, 4 ), new( "TEXCOORD", 0, 16, 2 ), new( "NORMAL", 0, 24, 3 ), new( "POSITION", 0, 36, 3 ), new( "TANGENT", 0, 48, 4 ) },
			Indices = indices, SourceVertices = Enumerable.Range( 0, count ).Select( x => (uint)x ).ToArray(), IndexDataOrigin = "gpu-readback" };
	}
	private static GltfGpuPrimitiveSnapshot Copy( GltfGpuPrimitiveSnapshot r, string origin ) => new() { Node = r.Node, Mesh = r.Mesh, Primitive = r.Primitive, VertexCount = r.VertexCount, VertexStride = r.VertexStride, Bytes = r.Bytes, Fields = r.Fields, Indices = r.Indices, SourceVertices = r.SourceVertices, IndexDataOrigin = origin };
	private static void Write( byte[] bytes, int offset, float value ) => BinaryPrimitives.WriteSingleLittleEndian( bytes.AsSpan( offset, 4 ), value );
	private static void Write3( byte[] bytes, int offset, NVector v ) { Write( bytes, offset, v.X ); Write( bytes, offset + 4, v.Y ); Write( bytes, offset + 8, v.Z ); }
}
