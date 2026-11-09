using System;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;

namespace Sandbox.Gltf.DirectGpu.BoxVertexColorsValidation;

/// <summary>Actual material realization and instance override routing; coordinator executes in engine.</summary>
internal static class BoxVertexColorsMaterialRoutingTests
{
	public static void Run( string checkoutRoot, SceneWorld world, Material computeMaterial, Material overrideMaterial )
	{
		var source = new BoxVertexColorsReference( checkoutRoot ).Forms.First( x => x.Name == "glTF/source" );
		var json = JsonNode.Parse( source.Bytes ).AsObject();
		var primitives = json["meshes"][0]["primitives"].AsArray();
		var uncolored = primitives[0].DeepClone();
		uncolored["attributes"].AsObject().Remove( "COLOR_0" );
		primitives.Add( uncolored );
		using var document = GltfReader.Read( System.Text.Encoding.UTF8.GetBytes( json.ToJsonString() ), source.Resolve );
		using var asset = GltfGpuAsset.Create( document, computeMaterial );
		var slotMaterial = asset.GetMaterials().Single();
		Require( asset.Primitives.Count == 2 && asset.Primitives.All( p => p.MaterialSlot == 0 ), "Fixture must share one authored slot." );
		var colored = asset.Primitives.Single( p => p.Plan.Sources.ContainsKey( "COLOR_0" ) );
		var plain = asset.Primitives.Single( p => !p.Plan.Sources.ContainsKey( "COLOR_0" ) );
		Require( ReferenceEquals( plain.Material, slotMaterial ), "Uncolored primitive lost its stock material." );
		Require( !ReferenceEquals( colored.Material, plain.Material ) && colored.Material.ShaderName.Contains( "vertex_color", StringComparison.OrdinalIgnoreCase ), "Colored primitive did not select the vertex-color shader." );
		Require( colored.Material.GetFeature( "F_GLTF_LINEAR_VERTEX_COLOR" ) == 1, "Linear color feature not enabled on the realized material." );
		Require( Material.FromShader( "shaders/vertex_color.shader" ).GetFeature( "F_GLTF_LINEAR_VERTEX_COLOR" ) == 0, "Shared stock shader template was mutated." );
		using var first = asset.CreateInstance( world );
		using var second = asset.CreateInstance( world );
		Require( Effective( first ).Contains( colored.Material ) && Effective( first ).Contains( plain.Material ), "Initial draw materials differ from the per-primitive routing." );
		first.SetMaterialOverride( 0, overrideMaterial );
		Require( Effective( first ).All( x => ReferenceEquals( x, overrideMaterial ) ), "Slot override must replace both primitive materials." );
		Require( Effective( second ).Contains( colored.Material ) && Effective( second ).Contains( plain.Material ), "Override leaked to another instance." );
		first.ClearMaterialOverrides();
		Require( Effective( first ).Contains( colored.Material ) && Effective( first ).Contains( plain.Material ), "Clearing the override did not restore both materials." );
	}

	private static Material[] Effective( GltfGpuInstance instance )
	{
		var field = typeof( GltfGpuInstance ).GetField( "draws", BindingFlags.Instance | BindingFlags.NonPublic );
		var draws = (System.Collections.IEnumerable)field.GetValue( instance );
		return draws.Cast<object>().Select( x => (Material)x.GetType().GetField( "EffectiveMaterial", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic ).GetValue( x ) ).ToArray();
	}
	private static void Require( bool value, string message ) { if ( !value ) throw new InvalidOperationException( message ); }
}
