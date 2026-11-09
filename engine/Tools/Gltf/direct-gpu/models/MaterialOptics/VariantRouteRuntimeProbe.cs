using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sandbox.Gltf.DirectGpu.MaterialOpticsValidation;

/// <summary>
/// Actual submission/readback probe with borrowed diagnostics. The coordinator
/// supplies a live world, compiled deformation material and real frame pump.
/// This does not prove authored material pixels or shader conformance.
/// </summary>
public static class VariantRouteRuntimeProbe
{
	public static async Task Run( SceneWorld world, Material compute, Material original, Material alternate,
		Material replacement, Func<Task> waitForFrames )
	{
		Require( original.IsValid && alternate.IsValid && replacement.IsValid && !ReferenceEquals( original, alternate ) &&
			!ReferenceEquals( alternate, replacement ) && !ReferenceEquals( original, replacement ), "Distinct live diagnostic materials required." );
		using var document = GltfReader.Read( Encoding.UTF8.GetBytes( Source ), supportedExtensions:
			new HashSet<string> { "KHR_materials_variants", "KHR_node_visibility" } );
		Material Resolve( int? index ) => index == 1 ? alternate : original;
		using var asset = GltfGpuAsset.Create( document, compute, Resolve );
		asset.ConfigureProjectedVariants( 0, (index, _, _) => new GltfMaterialLease<Material>( Resolve( index ), () => { }, null ) );
		using var a = asset.CreateInstance( world ); using var b = asset.CreateInstance( world );
		await waitForFrames(); Check( a, 0, original, false ); Check( b, 0, original, false );
		a.SetVariant( 0 ); await waitForFrames(); Check( a, 1, alternate, false ); Check( b, 0, original, false );
		a.SetMaterialOverride( replacement ); await waitForFrames(); Check( a, 1, replacement, true );
		a.SetVariant( null ); await waitForFrames(); Check( a, 0, replacement, true ); Check( b, 0, original, false );
		a.SetNodeVisibilityOverrides( new Dictionary<int, bool> { [0] = false } ); await waitForFrames();
		Require( !a.Readback().Single().Visible, "Hidden row remained visible." );
		a.SetNodeVisibilityOverrides( new Dictionary<int, bool> { [0] = true } ); await waitForFrames(); Check( a, 0, replacement, true );
		a.ClearMaterialOverrides(); await waitForFrames(); Check( a, 0, original, false ); Check( b, 0, original, false );
	}

	private static void Check( GltfGpuInstance instance, int materialIndex, Material material, bool overridden )
	{
		Require( instance.CallbackErrors.Length == 0 && instance.SubmittedRevision == instance.PoseRevision, "Current draw did not complete." );
		var row = instance.Readback().Single();
		Require( row.Visible && row.SubmittedMaterialIndex == materialIndex && ReferenceEquals( row.SubmittedMaterial, material ) &&
			row.SubmittedMaterialOverride == overridden && row.SubmittedMaterialFrame != 0 && row.SubmittedVariantRevision.HasValue &&
			row.ComputedRevision == instance.PoseRevision && row.Bytes is { Length: > 0 }, "Actual submitted material/route or compute revision differs." );
	}
	private static void Require( bool value, string message ) { if ( !value ) throw new InvalidOperationException( message ); }
	private const string Source = """
		{"asset":{"version":"2.0"},"extensionsUsed":["KHR_materials_variants"],
		 "extensions":{"KHR_materials_variants":{"variants":[{"name":"alternate"}]}},"materials":[{},{}],
		 "buffers":[{"byteLength":36,"uri":"data:application/octet-stream;base64,AAAAAAAAAAAAAAAAAACAPwAAAAAAAAAAAAAAAAAAgD8AAAAA"}],
		 "bufferViews":[{"buffer":0,"byteLength":36}],"accessors":[{"bufferView":0,"componentType":5126,"count":3,"type":"VEC3","min":[0,0,0],"max":[1,1,0]}],
		 "meshes":[{"primitives":[{"attributes":{"POSITION":0},"material":0,"extensions":{"KHR_materials_variants":{"mappings":[{"material":1,"variants":[0]}]}}}]}],
		 "nodes":[{"mesh":0}],"scenes":[{"nodes":[0]}],"scene":0}
		""";
}
