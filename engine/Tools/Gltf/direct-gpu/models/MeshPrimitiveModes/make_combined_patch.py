#!/usr/bin/env python3
from pathlib import Path
import difflib
root=Path(__file__).resolve().parents[6]; dest=Path(__file__).with_name('public-combined.proposed.patch');changes={}
def get(p):return (root/p).read_text()
def rep(s,a,b):
 assert s.count(a)==1,(a,s.count(a));return s.replace(a,b)
p='engine/Sandbox.Engine/Resources/Model/Gltf/GltfPublicMaterialRoute.cs';s=get(p).replace('using Sandbox.Gltf;','using Sandbox.Gltf;\nusing Sandbox.Gltf.DirectGpu;')
needle='\tinternal static bool RequiresProjected( GltfDocument document, int? sceneIndex )'
addition='''	internal static bool NoNormalPointLine( GltfGpuPrimitivePlan plan ) =>
		plan.Mode is 0 or 1 && !plan.Upload.Attributes.ContainsKey( "NORMAL" );

	// Select by material identity, not draw identity: all users of one source
	// slot/variant use the same program and public mutable material handle.
	internal static IReadOnlySet<int?> NoNormalPointLineMaterials( GltfDocument document, int? sceneIndex )
	{
		var result = new HashSet<int?>();
		if ( !sceneIndex.HasValue ) return result;
		var root = document.Root;
		if ( !root.TryGetProperty( "scenes", out var scenes ) || sceneIndex.Value < 0 || sceneIndex.Value >= scenes.GetArrayLength() ) throw new ArgumentOutOfRangeException( nameof( sceneIndex ) );
		var pending = new Stack<int>(); var visited = new HashSet<int>();
		var scene = scenes[sceneIndex.Value];
		if ( scene.TryGetProperty( "nodes", out var roots ) ) foreach ( var node in roots.EnumerateArray() ) pending.Push( node.GetInt32() );
		var variants = document.ReadMaterialVariantNames();
		while ( pending.TryPop( out var nodeIndex ) )
		{
			if ( !visited.Add( nodeIndex ) ) throw new GltfException( "Selected node is repeated." );
			var node = root.GetProperty( "nodes" )[nodeIndex];
			if ( node.TryGetProperty( "children", out var children ) ) foreach ( var child in children.EnumerateArray() ) pending.Push( child.GetInt32() );
			if ( !node.TryGetProperty( "mesh", out var meshValue ) ) continue;
			var mesh = meshValue.GetInt32(); var primitives = root.GetProperty( "meshes" )[mesh].GetProperty( "primitives" );
			for ( var primitive = 0; primitive < primitives.GetArrayLength(); primitive++ )
			{
				var item = primitives[primitive]; var attributes = item.GetProperty( "attributes" );
				if ( !attributes.TryGetProperty( "POSITION", out _ ) || attributes.TryGetProperty( "NORMAL", out _ ) ) continue;
				var mode = item.TryGetProperty( "mode", out var authoredMode ) ? authoredMode.GetInt32() : 4;
				if ( mode is < 0 or > 3 ) continue;
				for ( var variant = -1; variant < variants.Count; variant++ ) result.Add( document.GetMaterialForVariant( mesh, primitive, variant < 0 ? null : variant ) );
			}
		}
		return result;
	}

'''+needle
s=rep(s,needle,addition)
# Legacy-only assets retain the original non-projected source route.
changes[p]=s
p='engine/Sandbox.Engine/Resources/Model/Gltf/GltfManagedModelResource.cs';s=get(p)
s=rep(s,'\t\tif ( GltfPublicMaterialRoute.RequiresProjected( document, sceneIndex ?? document.GetDefaultScene() ) )','\t\tvar pointLineMaterials = GltfPublicMaterialRoute.NoNormalPointLineMaterials( document, sceneIndex ?? document.GetDefaultScene() );\n\t\tif ( GltfPublicMaterialRoute.RequiresProjected( document, sceneIndex ?? document.GetDefaultScene() ) )')
s=rep(s,'Material SelectTemplate( GltfMaterial material, GltfGpuPrimitivePlan _ )','Material SelectTemplate( GltfMaterial material, GltfGpuPrimitivePlan plan )')
s=rep(s,'if ( material is null ) throw new GltfException( "A projected source material must be explicit." );\n\t\t\t\tvar mode = material.AlphaMode;','''var combined = GltfPublicMaterialRoute.NeedsSourceLit( material ) && pointLineMaterials.Contains( plan.Material );
				if ( material is null ) throw new GltfException( "A projected source material must be explicit." );
				var mode = material.AlphaMode;''')
s=rep(s,'var key = (material.Unlit ? "unlit:" : "lit:") + mode','var key = (material.Unlit ? "unlit:" : combined ? "combined:" : "lit:") + mode')
s=rep(s,'material.Unlit ? "shaders/gltf_unlit_five_uv.shader" : material.DiffuseTransmission','material.Unlit ? "shaders/gltf_unlit_five_uv.shader" : combined ? (material.DiffuseTransmission is not null ? "shaders/gltf_point_line_combined_transmission.shader" : "shaders/gltf_point_line_combined.shader") : material.DiffuseTransmission')
s=rep(s,'sourceLitFeatureSidedness: true );','sourceLitFeatureSidedness: true, pointLineMaterials: pointLineMaterials );')
changes[p]=s
p='engine/Sandbox.Engine/Resources/Model/Gltf/DirectGpu/GltfGpuAsset.Variants.cs';s=get(p)
s=rep(s,'Action finalRelease = null, int? sceneIndex = null, bool allowUnselected = false, bool legacyLit = false, bool sourceLitFeatureSidedness = false )','Action finalRelease = null, int? sceneIndex = null, bool allowUnselected = false, bool legacyLit = false, bool sourceLitFeatureSidedness = false, IReadOnlySet<int?> pointLineMaterials = null )')
s=rep(s,'asset.ConfigureProjectedSourceMaterials( encodedImageBudget, selectTemplate, unlitCapabilities, legacyLit, sourceLitFeatureSidedness )','asset.ConfigureProjectedSourceMaterials( encodedImageBudget, selectTemplate, unlitCapabilities, legacyLit, sourceLitFeatureSidedness, pointLineMaterials )')
s=rep(s,'GltfUnlitMaterialPlan.Features unlitCapabilities, bool legacyLit = false, bool sourceLitFeatureSidedness = false )','GltfUnlitMaterialPlan.Features unlitCapabilities, bool legacyLit = false, bool sourceLitFeatureSidedness = false, IReadOnlySet<int?> pointLineMaterials = null )')
# Legacy-compatible slots keep the stock shader and cache path.


s=rep(s,'var tangent = GltfGpuGeneratedTangentRoute.Prepare( projected, hasSkin );','var tangent = pointLineMaterials?.Contains( materialIndex ) == true && GltfPublicMaterialRoute.NoNormalPointLine( projected ) ? null : GltfGpuGeneratedTangentRoute.Prepare( projected, hasSkin );')
changes[p]=s
p='engine/Sandbox.Engine/Resources/Model/Gltf/DirectGpu/GltfGpuBackend.cs';s=get(p)
s=rep(s,'draw.RenderAttributes.Clear(); material.Attributes.MergeTo( draw.RenderAttributes ); Graphics.Attributes.MergeTo( draw.RenderAttributes );','''draw.RenderAttributes.Clear(); material.Attributes.MergeTo( draw.RenderAttributes ); Graphics.Attributes.MergeTo( draw.RenderAttributes );
				// Explicit overrides retain their own shader semantics. The named combined
				// source program alone receives the glTF topology-specific policy.
				if ( overridden is null && (material.ShaderName.Equals( "shaders/gltf_stock_sampler.shader", StringComparison.OrdinalIgnoreCase ) || material.ShaderName.Equals( "shaders/gltf_point_line_combined.shader", StringComparison.OrdinalIgnoreCase ) || material.ShaderName.Equals( "shaders/gltf_point_line_combined_transmission.shader", StringComparison.OrdinalIgnoreCase )) )
					draw.RenderAttributes.Set( "GltfPointLineNoNormal", GltfPublicMaterialRoute.NoNormalPointLine( plan ) ? 1f : 0f );''')
changes[p]=s
changes['game/core/shaders/gltf_stock_sampler.shader']=Path(__file__).with_name('stock_draw_attribute_candidate.shader').read_text()
changes['game/core/shaders/gltf_point_line_combined.shader']=Path(__file__).with_name('combined_draw_attribute.shader').read_text()
changes['game/core/shaders/gltf_point_line_combined_transmission.shader']=Path(__file__).with_name('combined_transmission_candidate.shader').read_text()
result=[]
for p,s in changes.items():
 old=get(p) if (root/p).exists() else ''
 result.extend(difflib.unified_diff(old.splitlines(True),s.splitlines(True),fromfile='a/'+p if old else '/dev/null',tofile='b/'+p))
dest.write_text(''.join(result))
