using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;

namespace Sandbox.Gltf.DirectGpu.SimpleInstancingValidation;

using static SimpleInstancingReference;

/// <summary>All125 actual completed vertex/index readbacks; synthetic controls have a separate scope.</summary>
internal static class SimpleInstancingGpuChecks
{
	public const int ExpectedContractCases = 1;
	public const int ExpectedVerifierCases = 10;
	public sealed record Measurement( int Instances, int Vertices, int Indices, double MaxPositionErrorInches, double MaxNormalError, bool Mirrored, string Scope );
	public static SimpleInstancingCoreTests.Result[] RunContract() => new[] { Result( "nullable_source_InstanceIndex_readback_identity", "pending backend API contract", () => Require( IdentityProperty() is not null, "Pending: GltfGpuPrimitiveSnapshot requires public int? InstanceIndex; null for ordinary nodes, 0..count-1 for EXT instances." ) ) };
	public static Measurement AssertSnapshots( SimpleInstancingReference reference, IReadOnlyList<GltfGpuPrimitiveSnapshot> snapshots, Matrix4x4 placement )
	{
		Require( snapshots.Count == InstanceCount, "Expected exactly125 extension instances; missing instances or extra ordinary base mesh are failures." );
		var ids = snapshots.Select( Identity ).ToArray(); Require( ids.All( x => x.HasValue ) && ids.Select( x => x.Value ).Order().SequenceEqual( Enumerable.Range( 0, InstanceCount ) ), "Instance identities are missing, duplicated or out of range." );
		var maxPosition = 0d; var maxNormal = 0d; var mirrored = placement.GetDeterminant() < 0;
		foreach ( var snapshot in snapshots )
		{
			var instance = Identity( snapshot ).Value; Require( snapshot.Node == 0 && snapshot.Mesh == 0 && snapshot.Primitive == 0 && snapshot.VertexCount == VertexCount, "Lost source node/mesh/primitive identity." );
			Require( snapshot.VertexStride > 0 && snapshot.Bytes.Length == VertexCount * snapshot.VertexStride && snapshot.SourceVertices.SequenceEqual( Enumerable.Range( 0, VertexCount ).Select( x => (uint)x ) ), "Authored normals require all24 unchanged source correspondences." );
			Require( snapshot.IndexDataOrigin == "gpu-readback", "Indices must come from actual bound GPU buffer; CPU topology metadata is insufficient." );
			var expectedIndices = reference.Indices; if ( mirrored ) for ( var i = 0; i < expectedIndices.Length; i += 3 ) (expectedIndices[i + 1], expectedIndices[i + 2]) = (expectedIndices[i + 2], expectedIndices[i + 1]);
			Require( snapshot.Indices.SequenceEqual( expectedIndices ), "Actual bound topology or mirrored winding differs." );
			var position = Field( snapshot, "POSITION" ); var normal = Field( snapshot, "NORMAL" );
			for ( var vertex = 0; vertex < VertexCount; vertex++ )
			{
				maxPosition = Math.Max( maxPosition, Check( position, reference.Engine( instance, vertex, placement ) ) );
				maxNormal = Math.Max( maxNormal, Check( normal, reference.Engine( instance, vertex, placement, true ) ) );
				double Check( GltfGpuField field, double[] expected )
				{
					var error = 0d; for ( var c = 0; c < 3; c++ ) { var observed = BinaryPrimitives.ReadSingleLittleEndian( snapshot.Bytes.AsSpan( vertex * snapshot.VertexStride + field.Offset + c * 4 ) ); Require( float.IsFinite( observed ), "Nonfinite GPU output." ); error = Math.Max( error, Math.Abs( observed - expected[c] ) ); } return error;
				}
			}
		}
		// Analytic double expected versus several FP32 matrix products at <30m.
		// Tight enough to reject millimeter offsets; no threshold auto-widening.
		Require( maxPosition <= .0005 && maxNormal <= 2e-6, $"GPU instancing error exceeds reviewed candidate bounds: position {maxPosition:R}in, normal {maxNormal:R}." );
		return new( InstanceCount, InstanceCount * VertexCount, InstanceCount * IndexCount, maxPosition, maxNormal, mirrored, "actual_gpu_readback_requested; receipt determines execution; no pixel/material/performance proof" );
	}

	public static SimpleInstancingCoreTests.Result[] RunVerifierControls( string checkoutRoot )
	{
		var reference = new SimpleInstancingReference( checkoutRoot ); var rows = new List<SimpleInstancingCoreTests.Result>();
		Run( "synthetic_accepts_all125_reordered_runtime_fields", () => AssertSnapshots( reference, Synthetic( reference, Matrix4x4.Identity ), Matrix4x4.Identity ) );
		Run( "synthetic_accepts_affine_inverse_transpose_and_mirror", () => AssertSnapshots( reference, Synthetic( reference, MirroredPlacement ), MirroredPlacement ) );
		Run( "rejects_missing_instance", () => Reject( () => AssertSnapshots( reference, Synthetic( reference, Matrix4x4.Identity ).Take( 124 ).ToArray(), Matrix4x4.Identity ) ) );
		Run( "rejects_extra_ordinary_base_mesh", () => { var s = Synthetic( reference, Matrix4x4.Identity ); Reject( () => AssertSnapshots( reference, s.Append( s[0] ).ToArray(), Matrix4x4.Identity ) ); } );
		Run( "rejects_duplicate_instance_identity", () => { var s = Synthetic( reference, Matrix4x4.Identity ); IdentityProperty().SetValue( s[124], 0 ); Reject( () => AssertSnapshots( reference, s, Matrix4x4.Identity ) ); } );
		Run( "rejects_wrong_position_offset", () => { var s = Synthetic( reference, Matrix4x4.Identity ); var row = s[124]; s[124] = Copy( row, fields: row.Fields.Select( f => f.Semantic == "POSITION" ? f with { Offset = 24 } : f ).ToArray() ); Reject( () => AssertSnapshots( reference, s, Matrix4x4.Identity ) ); } );
		Run( "rejects_ignored_instance_transform", () => { var s = Synthetic( reference, Matrix4x4.Identity ); s[124] = Copy( s[124], bytes: s[0].Bytes.ToArray() ); Reject( () => AssertSnapshots( reference, s, Matrix4x4.Identity ) ); } );
		Run( "rejects_double_or_missing_placement", () => Reject( () => AssertSnapshots( reference, Synthetic( reference, Matrix4x4.Identity ), AffinePlacement ) ) );
		Run( "rejects_forward_normal_under_shear", () =>
		{
			var s = Synthetic( reference, AffinePlacement ); var field = Field( s[0], "NORMAL" );
			for ( var i = 0; i < InstanceCount; i++ ) for ( var v = 0; v < VertexCount; v++ )
			{
				var n = reference.Engine( i, v, Matrix4x4.Identity, true ); var wrong = ApplyPlacement( n, new Matrix4x4( AffinePlacement.M11, AffinePlacement.M12, AffinePlacement.M13, 0, AffinePlacement.M21, AffinePlacement.M22, AffinePlacement.M23, 0, AffinePlacement.M31, AffinePlacement.M32, AffinePlacement.M33, 0, 0, 0, 0, 1 ), false ); Normalize( wrong ); Write( s[i], field, v, wrong );
			}
			Reject( () => AssertSnapshots( reference, s, AffinePlacement ) );
		} );
		Run( "rejects_wrong_actual_bound_index_or_CPU_origin", () =>
		{
			var s = Synthetic( reference, Matrix4x4.Identity ); (s[0].Indices[1], s[0].Indices[2]) = (s[0].Indices[2], s[0].Indices[1]); Reject( () => AssertSnapshots( reference, s, Matrix4x4.Identity ) );
			s = Synthetic( reference, Matrix4x4.Identity ); s[0] = Copy( s[0], indexOrigin: "cpu-submission" ); Reject( () => AssertSnapshots( reference, s, Matrix4x4.Identity ) );
		} );
		Require( rows.Count == ExpectedVerifierCases, "Frozen verifier control inventory changed." ); return rows.ToArray();
		void Run( string name, Action body ) => rows.Add( Result( name, "synthetic_checker_sensitivity_only", () =>
		{
			// Precondition failure is a failed/pending control, never an expected
			// negative rejection swallowed by Reject().
			Require( IdentityProperty() is not null, "Pending InstanceIndex API; checker sensitivity is unverified." ); body();
		} ) );
	}
	private static GltfGpuPrimitiveSnapshot[] Synthetic( SimpleInstancingReference reference, Matrix4x4 placement )
	{
		Require( IdentityProperty() is not null, "Pending InstanceIndex API prevents synthetic identity controls; remain incomplete/failed." );
		return Enumerable.Range( 0, InstanceCount ).Select( instance =>
		{
			var indices = reference.Indices; if ( placement.GetDeterminant() < 0 ) for ( var i = 0; i < IndexCount; i += 3 ) (indices[i + 1], indices[i + 2]) = (indices[i + 2], indices[i + 1]);
			var row = new GltfGpuPrimitiveSnapshot { Node = 0, Mesh = 0, Primitive = 0, VertexCount = VertexCount, VertexStride = 48, Bytes = new byte[VertexCount * 48], Fields = new[] { new GltfGpuField( "NORMAL", 0, 4, 3 ), new GltfGpuField( "POSITION", 0, 28, 3 ) }, SourceVertices = Enumerable.Range( 0, VertexCount ).Select( v => (uint)v ).ToArray(), Indices = indices, IndexDataOrigin = "gpu-readback" };
			IdentityProperty().SetValue( row, instance ); for ( var v = 0; v < VertexCount; v++ ) { Write( row, Field( row, "POSITION" ), v, reference.Engine( instance, v, placement ) ); Write( row, Field( row, "NORMAL" ), v, reference.Engine( instance, v, placement, true ) ); } return row;
		} ).ToArray();
	}
	private static GltfGpuPrimitiveSnapshot Copy( GltfGpuPrimitiveSnapshot row, byte[] bytes = null, IReadOnlyList<GltfGpuField> fields = null, string indexOrigin = null ) { var result = new GltfGpuPrimitiveSnapshot { Node = row.Node, Mesh = row.Mesh, Primitive = row.Primitive, VertexCount = row.VertexCount, VertexStride = row.VertexStride, Bytes = bytes ?? row.Bytes, Fields = fields ?? row.Fields, SourceVertices = row.SourceVertices, Indices = row.Indices, IndexDataOrigin = indexOrigin ?? row.IndexDataOrigin }; IdentityProperty().SetValue( result, Identity( row ) ); return result; }
	private static void Write( GltfGpuPrimitiveSnapshot row, GltfGpuField field, int vertex, double[] value ) { for ( var c = 0; c < 3; c++ ) BinaryPrimitives.WriteSingleLittleEndian( row.Bytes.AsSpan( vertex * row.VertexStride + field.Offset + c * 4 ), (float)value[c] ); }
	private static GltfGpuField Field( GltfGpuPrimitiveSnapshot row, string name ) { var field = row.Fields.Single( f => f.Semantic == name && f.SemanticIndex == 0 ); Require( field.ComponentType == 5126 && field.Components == 3 && field.Offset >= 0 && field.Offset % 4 == 0 && field.Offset + 12 <= row.VertexStride, "Invalid runtime output field: " + name ); return field; }
	private static PropertyInfo IdentityProperty() { var p = typeof( GltfGpuPrimitiveSnapshot ).GetProperty( "InstanceIndex", BindingFlags.Instance | BindingFlags.Public ); return p?.PropertyType == typeof( int? ) && p.GetMethod is { IsPublic: true } && p.SetMethod is { IsPublic: true } ? p : null; }
	private static int? Identity( GltfGpuPrimitiveSnapshot row ) { var p = IdentityProperty(); Require( p is not null, "Pending: no unambiguous EXT instance identity API." ); return (int?)p.GetValue( row ); }
	private static void Reject( Action body ) { try { body(); } catch ( InvalidOperationException ) { return; } throw new Exception( "Faulty readback accepted by independent checker." ); }
	private static SimpleInstancingCoreTests.Result Result( string name, string form, Action body ) { try { body(); return new( name, form, true, null ); } catch ( Exception error ) { return new( name, form, false, error.ToString() ); } }
}
