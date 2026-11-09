using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Sandbox.Gltf.DirectGpu.SimpleInstancingValidation;

using static SimpleInstancingReference;

/// <summary>12 CPU integration cases against the actual Engine asset-preparation path. No device allocation.</summary>
internal static class SimpleInstancingBackendTests
{
	public const int ExpectedCases = 12;
	public static SimpleInstancingCoreTests.Result[] RunAll( string checkoutRoot )
	{
		var reference = new SimpleInstancingReference( checkoutRoot ); var rows = new List<SimpleInstancingCoreTests.Result>();
		foreach ( var form in reference.Forms )
		{
			Run( "actual_asset_prepare_expands125_source_instance_rows_no_base", () =>
			{
				using var d = form.Read(); using var asset = GltfGpuAsset.Prepare( d, "shaders/gltf_deform_cs.shader" );
				Require( asset.Nodes.Count == InstanceCount && asset.Nodes.All( x => x.Index == 0 ), "Asset preparation must retain the source node and expand all125 instances without an ordinary base mesh." );
				var property = typeof( GltfGpuAsset.Node ).GetProperty( "InstanceIndex", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance ); Require( property?.PropertyType == typeof( int? ), "Pending: prepared draw rows need nullable source InstanceIndex identity." );
				Require( asset.Nodes.Select( x => (int?)property.GetValue( x ) ).SequenceEqual( Enumerable.Range( 0, InstanceCount ).Select( x => (int?)x ) ), "Prepared rows must preserve source attribute order and exact identities." );
			} );
			Run( "all_prepared_rows_borrow_one_primitive_without_GPU_allocation", () =>
			{
				using var d = form.Read(); using var asset = GltfGpuAsset.Prepare( d, "shaders/gltf_deform_cs.shader" );
				Require( asset.Nodes.Count == InstanceCount && asset.Primitives.Count == 1 && asset.Nodes.All( x => ReferenceEquals( x.Primitive, asset.Primitives[0] ) ), "Instance expansion duplicated immutable source primitives." );
				var primitive = asset.Primitives[0]; Require( !asset.HasGpuResources && primitive.Input is null && primitive.InputDescriptors is null && primitive.OutputDescriptors is null && primitive.SourceVertices is null, "CPU asset preparation must not realize GPU source buffers." );
				Require( asset.MaterialSlotSourceIndices.Count == 1 && asset.MaterialSlotSourceIndices[0] is null && primitive.Plan.Material is null, "All instances must preserve the single default-material slot." );
			} );
			Run( "prepared_rest_bounds_include_every_rotated_instance", () =>
			{
				using var d = form.Read(); using var asset = GltfGpuAsset.Prepare( d, "shaders/gltf_deform_cs.shader" );
				var min = Numbers( reference.Oracle.GetProperty( "world_bounds" ).GetProperty( "min" ) ); var max = Numbers( reference.Oracle.GetProperty( "world_bounds" ).GetProperty( "max" ) );
				var bounds = asset.RestBounds;
				var actualMin = new double[] { bounds.Mins.x, bounds.Mins.y, bounds.Mins.z }; var actualMax = new double[] { bounds.Maxs.x, bounds.Maxs.y, bounds.Maxs.z }; var sourceAxis = new[] { 2, 0, 1 };
				for ( var c = 0; c < 3; c++ ) { Close( min[sourceAxis[c]] / .0254, actualMin[c], .0005, "all-instance rest minimum" ); Close( max[sourceAxis[c]] / .0254, actualMax[c], .0005, "all-instance rest maximum" ); }
			} );
			void Run( string name, Action body ) { try { body(); rows.Add( new( name, form.Name, true, null ) ); } catch ( Exception error ) { rows.Add( new( name, form.Name, false, error.ToString() ) ); } }
		}
		Require( rows.Count == ExpectedCases, "Backend integration inventory changed." ); return rows.ToArray();
	}
}
