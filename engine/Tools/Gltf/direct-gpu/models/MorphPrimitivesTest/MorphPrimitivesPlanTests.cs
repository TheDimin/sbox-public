using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Sandbox.Gltf.DirectGpu.MorphPrimitivesValidation;

using static MorphPrimitivesReference;

/// <summary>CPU GPU-staging contract assertions; no GPU submission is implied.</summary>
public static class MorphPrimitivesPlanTests
{
	public const int ExpectedCases = 19;
	public static MorphPrimitivesCoreTests.Result[] RunAll( string checkoutRoot )
	{
		var reference = new MorphPrimitivesReference( checkoutRoot ); var rows = new List<MorphPrimitivesCoreTests.Result>();
		foreach ( var form in reference.Forms )
		{
			Run( "both_primitive_source_arenas_keep_actual_bytes_and_formats", () =>
			{
				using var d = form.Read();
				for ( var p = 0; p < 2; p++ )
				{
					var plan = GltfGpuPrimitivePlan.Create( d, 0, p ); Require( plan.Sources.Count == 3, "Base attributes dropped." );
					foreach ( var (semantic, binding) in plan.Sources )
					{
						MorphPrimitivesCoreTests.CheckStream( binding.Stream, Primitive( form, p ).GetProperty( "attributes" ).GetProperty( semantic ) ); var origin = binding.ArenaByteOffset - binding.Stream.ByteOffset;
						Require( origin >= 0 && origin % 4 == 0 && plan.InputBytes.AsSpan( origin, binding.Stream.Storage.Bytes.Length ).SequenceEqual( binding.Stream.Storage.Bytes.Span ), "Packed arena changed source bytes." );
					}
					CheckMorphDescriptor( d, plan, p );
				}
			} );
			Run( "topology_vertex_mapping_material_and_missing_morph_semantics", () =>
			{
				using var d = form.Read();
				for ( var p = 0; p < 2; p++ )
				{
					var plan = GltfGpuPrimitivePlan.Create( d, 0, p ); Require( plan.Mode == 4 && !plan.GenerateFlatNormals && plan.Material == p, "Authored normals or material context lost." );
					Require( plan.Indices.SequenceEqual( Indices( form, p ) ) && plan.SourceVertices.SequenceEqual( Enumerable.Range( 0, Count( p ) ).Select( x => (uint)x ) ), "Primitive-local source topology/identity changed." );
					Require( Descriptors( plan ).Length == 4 && Number( plan, "MorphTargetCount" ) == 1 && Number( plan, "BaseAttributeCount" ) == 3, "Missing NORMAL/TANGENT target deltas were invented or target identity lost." );
				}
			} );
			Run( "runtime_output_field_declarations_do_not_assume_source_layout", () =>
			{
				using var d = form.Read(); for ( var p = 0; p < 2; p++ )
				{
					var plan = GltfGpuPrimitivePlan.Create( d, 0, p ); var occupied = new HashSet<int>();
					foreach ( var field in plan.Fields ) { Require( field.ComponentType == 5126 && field.Offset >= 0 && field.Offset % 4 == 0 && field.Offset + field.Components * 4 <= plan.VertexStride, "Invalid declared output representation." ); foreach ( var b in Enumerable.Range( field.Offset, field.Components * 4 ) ) Require( occupied.Add( b ), "Output fields overlap." ); }
					foreach ( var field in new[] { (Name: "POSITION", Components: 3), (Name: "NORMAL", Components: 3), (Name: "TANGENT", Components: 4), (Name: "COLOR", Components: 4), (Name: "TEXCOORD", Components: 2) } ) Require( plan.Fields.Single( f => f.Semantic == field.Name && f.SemanticIndex == 0 ).Components == field.Components, "Missing runtime stock-shader field." );
				}
			} );
			void Run( string name, Action body ) => MorphPrimitivesCoreTests.RunCase( rows, name, form.Name, body );
		}
		MorphPrimitivesCoreTests.RunCase( rows, "sparse_stride_normalized_and_nonindexed_descriptors_stay_primitive_local", "derived_layout_control", () =>
		{
			var form = reference.Forms.Single( x => x.Name == "glTF/source" ); using var d = MorphPrimitivesLayoutFixture.Create( form );
			for ( var p = 0; p < 2; p++ )
			{
				var plan = GltfGpuPrimitivePlan.Create( d, 0, p ); CheckMorphDescriptor( d, plan, p );
				foreach ( var pair in plan.Sources ) { var descriptor = Descriptors( plan ).GetValue( pair.Value.DescriptorIndex ); Require( Read( descriptor, "ByteOffset" ) == pair.Value.ArenaByteOffset && Read( descriptor, "ByteStride" ) == pair.Value.Stream.ByteStride && Read( descriptor, "ComponentType" ) == pair.Value.Stream.ComponentType && Read( descriptor, "Normalized" ) == (pair.Value.Stream.Normalized ? 1 : 0), "GPU descriptor ignored actual source representation." ); }
			}
		} );
		Require( rows.Count == ExpectedCases, "Frozen staging case count changed." ); return rows.ToArray();
	}
	private static void CheckMorphDescriptor( GltfDocument d, GltfGpuPrimitivePlan plan, int primitive )
	{
		var target = d.MaterializeMorphTargetsUpload( 0, primitive ).Targets[0]["POSITION"]; var descriptors = Descriptors( plan ); var index = Number( plan, "BaseAttributeCount" ); var descriptor = descriptors.GetValue( index );
		Require( Read( descriptor, "Target" ) == 0 && Read( descriptor, "Role" ) == 0 && Read( descriptor, "Components" ) == 3 && Read( descriptor, "ComponentType" ) == target.ComponentType && Read( descriptor, "Normalized" ) == (target.Normalized ? 1 : 0) && Read( descriptor, "ByteStride" ) == target.ByteStride, "Morph descriptor semantic/target/format changed." );
		var origin = checked((int)Read( descriptor, "ByteOffset" )) - target.ByteOffset; Require( origin >= 0 && origin % 4 == 0 && plan.InputBytes.AsSpan( origin, target.Storage.Bytes.Length ).SequenceEqual( target.Storage.Bytes.Span ), "Morph arena changed target bytes or reused other primitive's origin." );
	}
	private static object Property( object value, string name ) => value.GetType().GetProperty( name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic )?.GetValue( value ) ?? throw new InvalidOperationException( "Missing staging diagnostic " + name );
	private static Array Descriptors( GltfGpuPrimitivePlan plan ) => (Array)Property( plan, "InputDescriptors" );
	private static int Number( object value, string name ) => Convert.ToInt32( Property( value, name ) );
	private static uint Read( object descriptor, string field ) => Convert.ToUInt32( descriptor.GetType().GetField( field, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic )?.GetValue( descriptor ) ?? throw new InvalidOperationException( "Missing descriptor field " + field ) );
}
