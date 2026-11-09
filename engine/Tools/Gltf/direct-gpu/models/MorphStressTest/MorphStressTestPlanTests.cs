using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Sandbox.Gltf.DirectGpu.MorphStressTestValidation;

using static MorphStressTestReference;

/// <summary>18 pure staging cases, including synthetic nine-target layouts. No GPU device evidence.</summary>
public static class MorphStressTestPlanTests
{
	public const int ExpectedCases = 18;
	public static MorphStressTestCoreTests.Result[] RunAll( string checkoutRoot )
	{
		var r = new MorphStressTestReference( checkoutRoot ); var rows = new List<MorphStressTestCoreTests.Result>();
		var forms = r.Forms.Concat( new[] { r.Fixture( true, true, false ), r.Fixture( true, true, true ) } );
		foreach ( var form in forms )
		{
			Run( "every_target_descriptor_keeps_its_source_bytes_and_target_index", () =>
			{
				using var d = form.Read();
				for ( var primitive = 0; primitive < 2; primitive++ )
				{
					var plan = GltfGpuPrimitivePlan.Create( d, 0, primitive ); var upload = d.MaterializeMorphTargetsUpload( 0, primitive ); var count = upload.Targets.Count;
					Require( Count( plan, "MorphTargetCount" ) == count && Count( plan, "BaseAttributeCount" ) == 4, "Target truncation/base semantic omission." );
					var descriptors = Member( plan, "InputDescriptors" ).Cast<object>().ToArray(); Require( descriptors.Length == 4 + count * 2, "Missing or extra target descriptor." );
					for ( var target = 0; target < count; target++ )
					foreach ( var (name, stream) in upload.Targets[target].OrderBy( x => x.Key, StringComparer.Ordinal ) )
					{
						var role = name == "POSITION" ? 0u : 1u; var descriptor = descriptors.Single( x => UInt( x, "Target" ) == target && UInt( x, "Role" ) == role );
						var offset = checked((int)UInt( descriptor, "ByteOffset" ));
						Require( UInt( descriptor, "ByteStride" ) == stream.ByteStride && UInt( descriptor, "ComponentType" ) == stream.ComponentType && UInt( descriptor, "Components" ) == stream.Components && UInt( descriptor, "Normalized" ) == (stream.Normalized ? 1 : 0), "GPU load descriptor inferred a fixed layout." );
						Require( offset >= stream.ByteOffset && offset - stream.ByteOffset + stream.Storage.Bytes.Length <= plan.InputBytes.Length && plan.InputBytes.AsSpan( offset - stream.ByteOffset, stream.Storage.Bytes.Length ).SequenceEqual( stream.Storage.Bytes.Span ), "Target arena raw bytes/origin lost." );
					}
				}
			} );
			Run( "runtime_output_preserves_two_uv_sets_and_authored_topology", () =>
			{
				using var d = form.Read();
				for ( var p = 0; p < 2; p++ )
				{
					var plan = GltfGpuPrimitivePlan.Create( d, 0, p );
					Require( plan.Mode == 4 && !plan.GenerateFlatNormals && plan.Material == p && plan.Indices.SequenceEqual( r.Indices( p ) ) && plan.SourceVertices.SequenceEqual( Enumerable.Range( 0, r.VertexCount( p ) ).Select( x => (uint)x ) ), "Authored primitive/material/topology changed." );
					Require( plan.Fields.Count == 6 && plan.Fields.Select( f => (f.Semantic, f.SemanticIndex) ).Distinct().Count() == 6, "Declared fields missing or ambiguous." );
					foreach ( var (name, index, components) in new[] { ("POSITION", 0, 3), ("NORMAL", 0, 3), ("TANGENT", 0, 4), ("COLOR", 0, 4), ("TEXCOORD", 0, 2), ("TEXCOORD", 1, 2) } )
					{
						var f = plan.Fields.Single( x => x.Semantic == name && x.SemanticIndex == index ); Require( f.ComponentType == 5126 && f.Components == components && f.Offset >= 0 && f.Offset % 4 == 0 && f.Offset + components * 4 <= plan.VertexStride, "Runtime field representation invalid." );
					}
					var used = plan.Fields.SelectMany( x => Enumerable.Range( x.Offset, x.Components * 4 ) ).ToArray(); Require( used.Distinct().Count() == used.Length, "Runtime fields overlap." );
				}
			} );
			Run( "separate_plans_own_equal_immutable_raw_arenas", () =>
			{
				using var a = form.Read(); using var b = form.Read(); var first = GltfGpuPrimitivePlan.Create( a, 0, 1 ); var second = GltfGpuPrimitivePlan.Create( b, 0, 1 );
				Require( !ReferenceEquals( first.InputBytes, second.InputBytes ) && first.InputBytes.SequenceEqual( second.InputBytes ) && !ReferenceEquals( first.Indices, second.Indices ), "Separately created assets share mutable staging snapshots." );
			} );
			void Run( string name, Action body ) { try { body(); rows.Add( new( name, form.Name, true, null ) ); } catch ( Exception error ) { rows.Add( new( name, form.Name, false, error.ToString() ) ); } }
		}
		Require( rows.Count == ExpectedCases, "Frozen staging inventory changed." ); return rows.ToArray();
	}
	private static Array Member( object value, string name ) => (Array)value.GetType().GetProperty( name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic ).GetValue( value );
	private static int Count( object value, string name ) => (int)value.GetType().GetProperty( name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic ).GetValue( value );
	private static uint UInt( object value, string name ) => (uint)value.GetType().GetField( name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic ).GetValue( value );
}
