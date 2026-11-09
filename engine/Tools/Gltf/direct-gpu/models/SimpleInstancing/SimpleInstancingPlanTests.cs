using System;
using System.Collections.Generic;
using System.Linq;

namespace Sandbox.Gltf.DirectGpu.SimpleInstancingValidation;

using static SimpleInstancingReference;

/// <summary>12 base-primitive staging cases. These do not prove instance-attribute GPU staging.</summary>
public static class SimpleInstancingPlanTests
{
	public const int ExpectedCases = 12;
	public static SimpleInstancingCoreTests.Result[] RunAll( string checkoutRoot )
	{
		var rows = new List<SimpleInstancingCoreTests.Result>(); var reference = new SimpleInstancingReference( checkoutRoot );
		foreach ( var form in reference.Forms )
		{
			Run( "base_arena_actual_float_vec3_view_offsets_and_bytes", () =>
			{
				using var d = form.Read(); var plan = GltfGpuPrimitivePlan.Create( d, 0, 0 ); Require( plan.Sources.Count == 2 && plan.InputBytes.Length == 576, "Only shared immutable base NORMAL/POSITION views belong to this primitive plan." );
				foreach ( var (name, index) in new[] { ("POSITION", 1), ("NORMAL", 2) } )
				{
					var binding = plan.Sources[name]; var stream = binding.Stream; var expected = reference.Accessor( index );
					Require( stream.Binding.AccessorIndex == index && stream.Binding.ViewByteOffset == expected.GetProperty( "view_offset" ).GetInt32() && stream.ByteOffset == 0 && stream.ByteStride == 12 && stream.ComponentType == 5126 && stream.Components == 3 && !stream.Normalized && !stream.IsMaterialized, "Source descriptor was repacked by assumed vertex layout: " + name );
					Require( binding.ArenaByteOffset % 4 == 0 && binding.ArenaByteOffset >= 0 && binding.ArenaByteOffset + 288 <= plan.InputBytes.Length && plan.InputBytes.AsSpan( binding.ArenaByteOffset, 288 ).SequenceEqual( stream.Storage.Bytes.Span ), "Arena lost raw view bytes or origin: " + name );
					Require( Hash( stream.Storage.Bytes.Span ) == expected.GetProperty( "sha256" ).GetString(), "Base view differs from independent source hash." );
				}
				Require( plan.Sources["POSITION"].ArenaByteOffset != plan.Sources["NORMAL"].ArenaByteOffset && plan.Sources["POSITION"].DescriptorIndex != plan.Sources["NORMAL"].DescriptorIndex, "Distinct views need distinct descriptor origins." );
			} );
			Run( "authored_normals_literal_topology_runtime_output_fields", () =>
			{
				using var d = form.Read(); var plan = GltfGpuPrimitivePlan.Create( d, 0, 0 );
				Require( plan.Mode == 4 && !plan.GenerateFlatNormals && plan.Material is null && plan.Indices.SequenceEqual( reference.Indices ) && plan.SourceVertices.SequenceEqual( Enumerable.Range( 0, VertexCount ).Select( x => (uint)x ) ), "Staging must preserve authored cube corners/winding/default material." );
				Require( plan.Fields.Count == 5 && plan.Fields.Select( x => (x.Semantic, x.SemanticIndex) ).Distinct().Count() == 5, "Output runtime fields must be unambiguous." );
				foreach ( var (name, components) in new[] { ("POSITION", 3), ("NORMAL", 3), ("TANGENT", 4), ("COLOR", 4), ("TEXCOORD", 2) } )
				{
					var field = plan.Fields.Single( x => x.Semantic == name && x.SemanticIndex == 0 ); Require( field.Components == components && field.ComponentType == 5126 && field.Offset >= 0 && field.Offset % 4 == 0 && field.Offset + components * 4 <= plan.VertexStride, "Invalid runtime field: " + name );
				}
				var occupied = plan.Fields.SelectMany( x => Enumerable.Range( x.Offset, x.Components * 4 ) ).ToArray(); Require( occupied.Length == occupied.Distinct().Count(), "Runtime fields overlap." );
			} );
			Run( "independent_asset_staging_owns_equal_immutable_copies", () =>
			{
				using var a = form.Read(); using var b = form.Read(); var first = GltfGpuPrimitivePlan.Create( a, 0, 0 ); var second = GltfGpuPrimitivePlan.Create( b, 0, 0 );
				Require( !ReferenceEquals( first.InputBytes, second.InputBytes ) && first.InputBytes.SequenceEqual( second.InputBytes ) && !ReferenceEquals( first.Indices, second.Indices ) && !ReferenceEquals( first.SourceVertices, second.SourceVertices ), "Separately created assets must own their upload/topology snapshots." );
			} );
			void Run( string name, Action body ) { try { body(); rows.Add( new( name, form.Name, true, null ) ); } catch ( Exception error ) { rows.Add( new( name, form.Name, false, error.ToString() ) ); } }
		}
		Require( rows.Count == ExpectedCases, "Frozen plan inventory changed." ); return rows.ToArray();
	}
}
