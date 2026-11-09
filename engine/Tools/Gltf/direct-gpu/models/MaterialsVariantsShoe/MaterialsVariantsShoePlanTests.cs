using System;
using System.Collections.Generic;
using System.Linq;

namespace Sandbox.Gltf.DirectGpu.MaterialsVariantsShoeValidation;

using static MaterialsVariantsShoeReference;

/// <summary>Staging plan checks only; creating a plan is not a GPU submission.</summary>
public static class MaterialsVariantsShoePlanTests
{
	public sealed record Result( string Name, string Form, bool Passed, string Error );
	public static Result[] RunAll( string checkoutRoot )
	{
		var inputs = new Inputs( checkoutRoot ); var rows = new List<Result>();
		foreach ( var form in inputs.Forms )
		{
			Run( "staging_preserves_all_source_descriptors_and_bytes", () =>
			{
				using var document = form.Read(); var plan = GltfGpuPrimitivePlan.Create( document, 0, 0 );
				Require( plan.Sources.Count == 3 && plan.InputBytes.Length == 433280 && !plan.GenerateFlatNormals, "Staging duplicated views or replaced authored normals." );
				foreach ( var pair in plan.Sources )
				{
					var expected = form.Oracle.GetProperty( "streams" ).GetProperty( pair.Key ); var stream = pair.Value.Stream;
					Require( stream.ByteOffset == 0 && stream.ComponentType == 5126 && !stream.Normalized && stream.Count == VertexCount && stream.ByteStride == expected.GetProperty( "stride" ).GetInt32(), "GPU input descriptor ignores source stream properties." );
					var bytes = plan.InputBytes.AsSpan( pair.Value.ArenaByteOffset, expected.GetProperty( "length" ).GetInt32() );
					Require( Hash( bytes ) == expected.GetProperty( "sha256" ).GetString(), "GPU arena contains wrong source view: " + pair.Key );
				}
			} );
			Run( "staging_topology_retains_unsigned_int_order", () =>
			{
				using var document = form.Read(); var plan = GltfGpuPrimitivePlan.Create( document, 0, 0 );
				Require( plan.Mode == 4 && plan.Indices.Length == IndexCount && plan.SourceVertices.Length == VertexCount && plan.SourceVertices.Select( (x, i) => x == (uint)i ).All( x => x ), "Topology or source correspondence changed." );
				var bytes = new byte[plan.Indices.Length * 4];
				for ( var i = 0; i < plan.Indices.Length; i++ ) System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian( bytes.AsSpan( i * 4 ), plan.Indices[i] );
				Require( Hash( bytes ) == form.Oracle.GetProperty( "streams" ).GetProperty( "INDICES" ).GetProperty( "sha256" ).GetString(), "Every original triangle must retain winding/order." );
			} );
			Run( "staging_runtime_output_fields_and_authored_default", () =>
			{
				using var document = form.Read(); var plan = GltfGpuPrimitivePlan.Create( document, 0, 0 );
				Require( plan.Material == 0 && plan.VertexStride > 0 && plan.VertexStride % 4 == 0, "Initial draw must use primitive default." );
				foreach ( var (semantic, components) in new[] { ("POSITION", 3), ("NORMAL", 3), ("TANGENT", 4), ("COLOR", 4), ("TEXCOORD", 2) } )
				{
					var field = plan.Fields.Single( x => x.Semantic == semantic && x.SemanticIndex == 0 );
					Require( field.Components == components && field.ComponentType == 5126 && field.Offset >= 0 && field.Offset + components * 4 <= plan.VertexStride, "Invalid declared output field: " + semantic );
				}
				Require( plan.Fields.SelectMany( f => Enumerable.Range( f.Offset, f.Components * 4 ) ).Distinct().Count() == plan.Fields.Sum( f => f.Components * 4 ), "Output fields overlap." );
			} );
			void Run( string name, Action body ) { try { body(); rows.Add( new( name, form.Name, true, null ) ); } catch ( Exception error ) { rows.Add( new( name, form.Name, false, error.ToString() ) ); } }
		}
		return rows.ToArray();
	}
}
