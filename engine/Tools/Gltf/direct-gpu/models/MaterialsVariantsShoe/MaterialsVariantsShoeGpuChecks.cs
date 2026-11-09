using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Sandbox.Gltf.DirectGpu.MaterialsVariantsShoeValidation;

using static MaterialsVariantsShoeReference;

/// <summary>Consumes completed real GPU readbacks; never synthesizes actual output.</summary>
public static class MaterialsVariantsShoeGpuChecks
{
	public static object AssertSnapshot( GltfGpuPrimitiveSnapshot snapshot, JsonElement oracle, double translateX = 0, double translateY = 0, double translateZ = 0 )
	{
		Require( snapshot.Node == 0 && snapshot.Mesh == 0 && snapshot.Primitive == 0 && snapshot.VertexCount == VertexCount, "Readback belongs to another primitive or source correspondence." );
		Require( snapshot.VertexStride > 0 && snapshot.Bytes.Length == snapshot.VertexCount * snapshot.VertexStride && snapshot.SourceVertices.Length == VertexCount, "Readback length/stride differs." );
		Require( snapshot.SourceVertices.Select( (x, i) => x == (uint)i ).All( x => x ), "Authored normals require no flat-shading expansion/reordering." );
		Require( snapshot.Indices.Length == IndexCount, "Readback topology count changed." );
		var indexBytes = new byte[IndexCount * 4];
		for ( var i = 0; i < IndexCount; i++ ) BinaryPrimitives.WriteUInt32LittleEndian( indexBytes.AsSpan( i * 4 ), snapshot.Indices[i] );
		Require( Hash( indexBytes ) == oracle.GetProperty( "streams" ).GetProperty( "INDICES" ).GetProperty( "sha256" ).GetString(), "GPU draw topology differs from independent source bytes." );
		var position = Field( "POSITION", 3 ); var normal = Field( "NORMAL", 3 ); var uv = Field( "TEXCOORD", 2 );
		foreach ( var sample in oracle.GetProperty( "samples" ).EnumerateArray() )
		{
			var vertex = sample.GetProperty( "vertex" ).GetInt32(); var expectedPosition = Numbers( sample.GetProperty( "engine" ) );
			expectedPosition[0] += translateX; expectedPosition[1] += translateY; expectedPosition[2] += translateZ;
			Check( position, vertex, expectedPosition, 2e-5 ); Check( normal, vertex, Numbers( sample.GetProperty( "engine_normal" ) ), 2e-5 );
			Check( uv, vertex, Numbers( sample.GetProperty( "uv" ) ), 3e-7 );
		}
		return new { snapshot.Node, snapshot.Mesh, snapshot.Primitive, snapshot.VertexCount, snapshot.VertexStride, sampledVertices = oracle.GetProperty( "samples" ).GetArrayLength(), positionToleranceInches = 2e-5, normalTolerance = 2e-5, uvTolerance = 3e-7, materialFidelityProven = false };
		GltfGpuField Field( string semantic, int count )
		{
			var field = snapshot.Fields.Single( x => x.Semantic == semantic && x.SemanticIndex == 0 );
			Require( field.ComponentType == 5126 && field.Components == count && field.Offset >= 0 && field.Offset + count * 4 <= snapshot.VertexStride, "Invalid readback field: " + semantic ); return field;
		}
		void Check( GltfGpuField field, int vertex, double[] expected, double tolerance )
		{
			for ( var c = 0; c < expected.Length; c++ )
			{
				var observed = BinaryPrimitives.ReadSingleLittleEndian( snapshot.Bytes.AsSpan( vertex * snapshot.VertexStride + field.Offset + c * 4 ) );
				Require( float.IsFinite( observed ) && Math.Abs( observed - expected[c] ) <= tolerance, $"GPU {field.Semantic} vertex {vertex} component {c}: observed {observed}, expected {expected[c]}." );
			}
		}
	}

	/// <summary>Reference sensitivity controls; passing these proves only that the checker rejects faults.</summary>
	public static MaterialsVariantsShoeCoreTests.Result[] RunVerifierControls( string checkoutRoot )
	{
		var inputs = new Inputs( checkoutRoot ); var form = inputs.Forms[0]; var oracle = form.Oracle;
		using var document = form.Read(); var topology = document.ReadPrimitive( 0, 0 ).Indices;
		// These deliberately synthetic bytes exercise checker sensitivity only and
		// are never reported as a completed GPU readback.
		var fields = new[] { new GltfGpuField( "TEXCOORD", 0, 0, 2 ), new GltfGpuField( "NORMAL", 0, 12, 3 ), new GltfGpuField( "POSITION", 0, 28, 3 ) };
		var bytes = new byte[VertexCount * 48];
		foreach ( var sample in oracle.GetProperty( "samples" ).EnumerateArray() )
		{
			var vertex = sample.GetProperty( "vertex" ).GetInt32();
			foreach ( var (field, property) in new[] { (fields[0], "uv"), (fields[1], "engine_normal"), (fields[2], "engine") } )
			{
				var values = Numbers( sample.GetProperty( property ) );
				for ( var c = 0; c < values.Length; c++ ) BinaryPrimitives.WriteSingleLittleEndian( bytes.AsSpan( vertex * 48 + field.Offset + c * 4 ), (float)values[c] );
			}
		}
		var rows = new List<MaterialsVariantsShoeCoreTests.Result>();
		Run( "synthetic_reference_accepts_runtime_reordered_fields", () => AssertSnapshot( Snapshot( bytes, topology ), oracle ) );
		Run( "checker_rejects_wrong_position_offset", () => Reject( () => AssertSnapshot( Snapshot( bytes, topology, fields.Select( f => f.Semantic == "POSITION" ? f with { Offset = 24 } : f ).ToArray() ), oracle ) ) );
		Run( "checker_rejects_v_flip", () =>
		{
			var changed = (byte[])bytes.Clone(); foreach ( var sample in oracle.GetProperty( "samples" ).EnumerateArray() ) { var offset = sample.GetProperty( "vertex" ).GetInt32() * 48 + 4; BinaryPrimitives.WriteSingleLittleEndian( changed.AsSpan( offset ), 1 - BinaryPrimitives.ReadSingleLittleEndian( changed.AsSpan( offset ) ) ); }
			Reject( () => AssertSnapshot( Snapshot( changed, topology ), oracle ) );
		} );
		Run( "checker_rejects_swapped_winding", () => { var changed = (uint[])topology.Clone(); (changed[0], changed[1]) = (changed[1], changed[0]); Reject( () => AssertSnapshot( Snapshot( bytes, changed ), oracle ) ); } );
		Run( "checker_rejects_double_placement", () => Reject( () => AssertSnapshot( Snapshot( bytes, topology ), oracle, 3, -5, 7 ) ) );
		return rows.ToArray();
		GltfGpuPrimitiveSnapshot Snapshot( byte[] data, uint[] indices, GltfGpuField[] layout = null ) => new() { Node = 0, Mesh = 0, Primitive = 0, VertexCount = VertexCount, VertexStride = 48, Fields = layout ?? fields, Bytes = data, Indices = indices, SourceVertices = Enumerable.Range( 0, VertexCount ).Select( x => (uint)x ).ToArray() };
		void Run( string name, Action body ) { try { body(); rows.Add( new( name, "synthetic verifier control", true, null ) ); } catch ( Exception error ) { rows.Add( new( name, "synthetic verifier control", false, error.ToString() ) ); } }
		void Reject( Action body ) { try { body(); } catch ( InvalidOperationException ) { return; } throw new Exception( "Faulty snapshot was accepted." ); }
	}
}
