using System;
using System.Collections.Generic;
using System.Linq;

namespace Sandbox.Gltf.DirectGpu.Validation;

/// <summary>Pure tests of this sample's actual direct GPU staging plan, before any device allocation.</summary>
public static class SimpleSkinPlanTests
{
	public sealed record Result( string Name, string Form, bool Passed, string Error );

	public static Result[] RunAll( string checkoutRoot )
	{
		var results = new List<Result>();
		foreach ( var form in SimpleSkinReference.OpenForms( checkoutRoot ) )
		{
			Run( "arena_keeps_shared_view_offset_and_stride", TestArena );
			Run( "missing_normals_expand_flat_corners_with_runtime_fields", TestFlatPlan );
			Run( "staging_snapshots_have_independent_owned_bytes", TestOwnership );

			void Run( string name, Action<GltfGpuPrimitivePlan> test )
			{
				try
				{
					using var document = form.Read(); test( GltfGpuPrimitivePlan.Create( document, 0, 0 ) );
					results.Add( new Result( name, form.Name, true, null ) );
				}
				catch ( Exception error ) { results.Add( new Result( name, form.Name, false, error.ToString() ) ); }
			}

			void TestOwnership( GltfGpuPrimitivePlan first )
			{
				using var secondDocument = form.Read(); var second = GltfGpuPrimitivePlan.Create( secondDocument, 0, 0 );
				SimpleSkinCoreTests.Require( !ReferenceEquals( first.InputBytes, second.InputBytes ) && first.InputBytes.SequenceEqual( second.InputBytes ), "Separate staging plans must own independent identical input arenas." );
				SimpleSkinCoreTests.Require( !ReferenceEquals( first.SourceVertices, second.SourceVertices ) && !ReferenceEquals( first.Indices, second.Indices ), "Topology snapshots are unexpectedly mutable shared arrays." );
			}
		}
		return results.ToArray();
	}

	private static void TestArena( GltfGpuPrimitivePlan plan )
	{
		var joints = plan.Sources["JOINTS_0"]; var weights = plan.Sources["WEIGHTS_0"];
		SimpleSkinCoreTests.Require( ReferenceEquals( joints.Stream.Storage, weights.Stream.Storage ), "Source binding lost the shared skinning bufferView." );
		SimpleSkinCoreTests.Require( joints.Stream.ByteStride == 16 && weights.Stream.ByteStride == 16 && joints.Stream.ComponentType == 5123 && weights.Stream.ComponentType == 5126, "Source formats/strides were repacked by assumption." );
		SimpleSkinCoreTests.Require( weights.ArenaByteOffset - joints.ArenaByteOffset == 160 && weights.DescriptorIndex != joints.DescriptorIndex, "Shared raw bytes need distinct offset-aware descriptors." );
		foreach ( var binding in plan.Sources.Values )
		{
			var storageBase = binding.ArenaByteOffset - binding.Stream.ByteOffset;
			SimpleSkinCoreTests.Require( storageBase >= 0 && storageBase % 4 == 0 && storageBase + binding.Stream.Storage.Bytes.Length <= plan.InputBytes.Length, "Storage arena origin/range is invalid." );
			SimpleSkinCoreTests.Require( plan.InputBytes.AsSpan( storageBase, binding.Stream.Storage.Bytes.Length ).SequenceEqual( binding.Stream.Storage.Bytes.Span ), "Source view payload changed while constructing the raw GPU arena." );
		}
	}

	private static void TestFlatPlan( GltfGpuPrimitivePlan plan )
	{
		SimpleSkinCoreTests.Require( plan.Mode == 4 && plan.GenerateFlatNormals, "This normal-less triangle sample must request generated flat normals." );
		SimpleSkinCoreTests.Require( plan.SourceVertices.Length == 24 && plan.SourceVertices.All( source => source < 10 )
			&& plan.Indices.Length == 24 && plan.Indices.All( index => index < 24 ) && plan.Indices.Distinct().Count() == 24, "Flat triangles need distinct output corners with valid source mappings." );
		var mapped = plan.Indices.Select( index => plan.SourceVertices[index] ).ToArray();
		SimpleSkinCoreTests.Require( OrientedTriangles( mapped ).SequenceEqual( OrientedTriangles( SimpleSkinReference.TriangleIndices ) ), "Expanded output changed the source triangles or winding." );
		SimpleSkinCoreTests.Require( plan.VertexStride > 0 && plan.VertexStride % 4 == 0, "Runtime output stride is invalid." );
		foreach ( var field in plan.Fields )
			SimpleSkinCoreTests.Require( field.ComponentType == 5126 && field.Components is >= 1 and <= 4 && field.Offset >= 0 && field.Offset % 4 == 0 && field.Offset + field.Components * 4 <= plan.VertexStride, "Runtime output field exceeds its actual declared stride." );
		var ranges = plan.Fields.OrderBy( field => field.Offset ).ToArray();
		for ( var field = 1; field < ranges.Length; field++ )
			SimpleSkinCoreTests.Require( ranges[field - 1].Offset + ranges[field - 1].Components * 4 <= ranges[field].Offset, "Runtime output fields overlap." );
		SimpleSkinCoreTests.Require( plan.Fields.Single( field => field.Semantic == "POSITION" && field.SemanticIndex == 0 ).Components == 3
			&& plan.Fields.Single( field => field.Semantic == "NORMAL" && field.SemanticIndex == 0 ).Components == 3, "Position/generated normal output declarations are missing." );
	}

	private static string[] OrientedTriangles( uint[] indices ) => Enumerable.Range( 0, indices.Length / 3 ).Select( triangle =>
	{
		var a = indices[triangle * 3]; var b = indices[triangle * 3 + 1]; var c = indices[triangle * 3 + 2];
		if ( b < a && b < c ) (a, b, c) = (b, c, a);
		else if ( c < a && c < b ) (a, b, c) = (c, a, b);
		return $"{a}/{b}/{c}";
	} ).OrderBy( triangle => triangle, StringComparer.Ordinal ).ToArray();
}
