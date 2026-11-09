using System;
using System.Collections.Generic;
using System.Text;

namespace Sandbox.Gltf.DirectGpu.AlphaBlendModeValidation;

/// <summary>Realizes actual unlit source packets before the existing two-instance override probe.</summary>
internal static class UnlitMaterialRoutingProbe
{
	public static AlphaMaterialRoutingProbe.Result[] Run( SceneWorld world, Material compute, Material opaqueTemplate, Material blendTemplate )
	{
		const string fixture = """
			{"asset":{"version":"2.0"},"extensionsUsed":["KHR_materials_unlit"],
			 "accessors":[{"componentType":5126,"count":3,"type":"VEC3","min":[0,0,0],"max":[0,0,0]}],
			 "meshes":[{"primitives":[{"attributes":{"POSITION":0}}]}],
			 "materials":[
			  {"pbrMetallicRoughness":{"baseColorFactor":[0.8,0.4,0.2,0.5]},"extensions":{"KHR_materials_unlit":{}}},
			  {"alphaMode":"BLEND","pbrMetallicRoughness":{"baseColorFactor":[0.2,0.4,0.8,0.25]},"extensions":{"KHR_materials_unlit":{}}}]}
			""";
		using var document = GltfReader.Read( Encoding.UTF8.GetBytes( fixture ), supportedExtensions: new HashSet<string> { "KHR_materials_unlit" } );
		var upload = document.MaterializePrimitiveUpload( 0, 0 );
		GltfMaterialLease<Material> Acquire( int index, Material template )
		{
			var material = document.ReadMaterial( index );
			var prepared = GltfUnlitSourceMaterialAdapter.Prepare( document, material, index );
			var uv = GltfMaterialUvPlan.CreateForRoles( material, upload, [GltfMaterialTextureRole.BaseColor], index );
			var projection = GltfMaterialUvProjection.Create( uv, 5 );
			return GltfUnlitSourceMaterialAdapter.Acquire( prepared, template, uv, projection );
		}
		using var opaque = Acquire( 0, opaqueTemplate );
		using var blend = Acquire( 1, blendTemplate );
		return AlphaMaterialRoutingProbe.Run( world, compute, opaque.Value, blend.Value );
	}
}
