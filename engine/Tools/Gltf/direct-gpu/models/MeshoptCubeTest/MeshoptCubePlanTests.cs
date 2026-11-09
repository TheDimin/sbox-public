using System;
using System.Collections.Generic;
using System.Linq;

namespace Sandbox.Gltf.DirectGpu.MeshoptCubeValidation;

using static MeshoptCubeReference;

/// <summary>Pure staging evidence; none of these assertions submits GPU work.</summary>
public static class MeshoptCubePlanTests
{
	public static MeshoptCubeCoreTests.Result[] RunAll( string checkoutRoot )
	{
		var reference = new MeshoptCubeReference( checkoutRoot ); var rows = new List<MeshoptCubeCoreTests.Result>();
		foreach ( var form in reference.Forms )
		{
			Run( "all_source_formats_offsets_strides_and_arena_bytes", () =>
			{
				using var d = form.Read();
				for ( var mesh = 0; mesh < PrimitiveCount; mesh++ )
				{
					var plan = GltfGpuPrimitivePlan.Create( d, mesh, 0 ); Require( plan.Sources.Count == reference.Primitive( mesh ).GetProperty( "attributes" ).EnumerateObject().Count(), "Plan lost source attributes." );
					foreach ( var (semantic, source) in plan.Sources )
					{
						MeshoptCubeCoreTests.CheckStream( d, reference, mesh, semantic, source.Stream );
						var start = source.ArenaByteOffset - source.Stream.ByteOffset; Require( start >= 0 && start % 4 == 0, "Input arena origin/alignment changed." );
						Require( plan.InputBytes.AsSpan( start, source.Stream.Storage.Bytes.Length ).SequenceEqual( source.Stream.Storage.Bytes.Span ), "GPU arena failed to preserve independent source view bytes." );
					}
					var names = plan.Sources.Keys.ToArray();
					for ( var i = 0; i < names.Length; i++ ) for ( var j = i + 1; j < names.Length; j++ )
					{
						var a = plan.Sources[names[i]]; var b = plan.Sources[names[j]];
						if ( ReferenceEquals( a.Stream.Storage, b.Stream.Storage ) ) Require( a.ArenaByteOffset - a.Stream.ByteOffset == b.ArenaByteOffset - b.Stream.ByteOffset, "Interleaved view was duplicated or rebased inconsistently." );
					}
				}
			} );
			Run( "all_topology_and_source_correspondence", () =>
			{
				using var d = form.Read();
				for ( var mesh = 0; mesh < PrimitiveCount; mesh++ )
				{
					var plan = GltfGpuPrimitivePlan.Create( d, mesh, 0 ); var count = reference.Accessor( reference.AccessorFor( mesh, "POSITION" ) ).GetProperty( "count" ).GetInt32();
					Require( !plan.GenerateFlatNormals && plan.Mode == 4 && plan.Indices.SequenceEqual( reference.Indices( mesh ) ), "Plan altered authored winding/topology/normals." );
					Require( plan.SourceVertices.SequenceEqual( Enumerable.Range( 0, count ).Select( x => (uint)x ) ), "Plan source map changed without a justified expansion." );
				}
			} );
			Run( "runtime_output_declaration_and_default_material", () =>
			{
				using var d = form.Read();
				for ( var mesh = 0; mesh < PrimitiveCount; mesh++ )
				{
					var plan = GltfGpuPrimitivePlan.Create( d, mesh, 0 ); Require( plan.Material == (mesh < 10 ? mesh : (int?)null), "Default cube or label material changed." );
					foreach ( var (semantic, components) in new[] { ("POSITION", 3), ("NORMAL", 3), ("TANGENT", 4), ("COLOR", 4), ("TEXCOORD", 2) } )
					{
						var field = plan.Fields.Single( x => x.Semantic == semantic && x.SemanticIndex == 0 ); Require( field.Components == components && field.ComponentType == 5126 && field.Offset >= 0 && field.Offset + components * 4 <= plan.VertexStride, "Invalid runtime field declaration." );
					}
					Require( plan.Fields.SelectMany( f => Enumerable.Range( f.Offset, f.Components * 4 ) ).Distinct().Count() == plan.Fields.Sum( f => f.Components * 4 ), "Runtime output fields overlap." );
				}
			} );
			void Run( string name, Action body ) { try { body(); rows.Add( new( name, form.Name, true, null ) ); } catch ( Exception e ) { rows.Add( new( name, form.Name, false, e.ToString() ) ); } }
		}
		return rows.ToArray();
	}
}
