#!/usr/bin/env python3
from pathlib import Path
import difflib
root=Path(__file__).resolve().parents[6]
out=Path(__file__).with_name('public-point-line.proposed.patch')
changes={}
def get(p):return (root/p).read_text()
def replace(s,a,b):
 assert s.count(a)==1,(a,s.count(a))
 return s.replace(a,b)
p='engine/Sandbox.Engine/Resources/Model/Gltf/GltfPublicMaterialRoute.cs';s=get(p)
s=replace(s,'\tinternal static bool RequiresProjected( GltfDocument document, int? sceneIndex )','''\tinternal static bool NoNormalPointLine( GltfGpuPrimitivePlan plan ) =>
		plan.Mode is 0 or 1 && !plan.Upload.Attributes.ContainsKey( "NORMAL" );

	internal static bool RequiresProjected( GltfDocument document, int? sceneIndex )''')
s=s.replace('using Sandbox.Gltf;','using Sandbox.Gltf;\nusing Sandbox.Gltf.DirectGpu;')
s=replace(s,'\t\t\t\tif ( !primitives[primitive].GetProperty( "attributes" ).TryGetProperty( "POSITION", out _ ) ) continue;','''				var item = primitives[primitive]; var attributes = item.GetProperty( "attributes" );
				if ( !attributes.TryGetProperty( "POSITION", out _ ) ) continue;
				var sourceMode = item.TryGetProperty( "mode", out var mode ) ? mode.GetInt32() : 4;
				if ( sourceMode is >= 0 and <= 3 && !attributes.TryGetProperty( "NORMAL", out _ ) ) projected = true;''')
changes[p]=s
p='engine/Sandbox.Engine/Resources/Model/Gltf/GltfManagedModelResource.cs';s=get(p)
s=replace(s,'Material SelectTemplate( GltfMaterial material, GltfGpuPrimitivePlan _ )','Material SelectTemplate( GltfMaterial material, GltfGpuPrimitivePlan plan )')
s=replace(s,'if ( material is null ) throw new GltfException( "A projected source material must be explicit." );\n\t\t\t\tvar mode = material.AlphaMode;','''var pointLine = GltfPublicMaterialRoute.NoNormalPointLine( plan ) && material?.Unlit != true;
				if ( material is null && !pointLine ) throw new GltfException( "A projected source material must be explicit." );
				material ??= new GltfMaterial { BaseColorFactor = System.Numerics.Vector4.One, AlphaMode = "OPAQUE", AlphaCutoff = .5f };
				var mode = material.AlphaMode;''')
s=replace(s,'var key = (material.Unlit ? "unlit:" : "lit:") + mode','var key = (material.Unlit ? "unlit:" : pointLine ? "point-line:" : "lit:") + mode')
s=replace(s,'material.Unlit ? "shaders/gltf_unlit_five_uv.shader" : material.DiffuseTransmission','material.Unlit ? "shaders/gltf_unlit_five_uv.shader" : pointLine ? "shaders/gltf_point_line.shader" : material.DiffuseTransmission')
changes[p]=s
p='engine/Sandbox.Engine/Resources/Model/Gltf/DirectGpu/GltfGpuAsset.Variants.cs';s=get(p)
s=replace(s,'if ( legacyLit && material?.Unlit != true && !GltfPublicMaterialRoute.NeedsSourceLit( material ) )','''if ( legacyLit && material?.Unlit != true && GltfPublicMaterialRoute.NoNormalPointLine( plan ) )
			{
				var template = selectTemplate( material, plan ) ?? throw new GltfException( "No point/line material template is available." );
				var prepared = GltfPointLineSourceMaterialAdapter.Prepare( Document, material, index, images.ReadImage );
				return GltfPointLineSourceMaterialAdapter.Acquire( prepared, template, plan.MaterialUv, plan.UvProjection );
			}
			if ( legacyLit && material?.Unlit != true && !GltfPublicMaterialRoute.NeedsSourceLit( material ) )''')
s=replace(s,'}, legacyLit ? index => index is not int source || (Document.ReadMaterial( source ) is var selected && !selected.Unlit && !GltfPublicMaterialRoute.NeedsSourceLit( selected )) : null,','''}, legacyLit ? (index, plan) => !GltfPublicMaterialRoute.NoNormalPointLine( plan ) && (index is not int source || (Document.ReadMaterial( source ) is var selected && !selected.Unlit && !GltfPublicMaterialRoute.NeedsSourceLit( selected ))) : null,''')
s=replace(s,'Func<int?, bool> useLegacy = null,','Func<int?, GltfGpuPrimitivePlan, bool> useLegacy = null,')
s=replace(s,'Func<IReadOnlyList<(GltfMaterialVariantSlot Slot, int? Material)>, GltfMaterialImageSnapshot, IDisposable> prepareBatch = null )','Func<IReadOnlyList<(GltfMaterialVariantSlot Slot, int? Material)>, GltfMaterialImageSnapshot, IDisposable> prepareBatch = null, bool pointLinePolicy = false )')
s=replace(s,'\t\t} );\n\t}\n\n\t/// <summary>\n\t/// Explicit candidate setup', '\t\t}, pointLinePolicy: legacyLit );\n\t}\n\n\t/// <summary>\n\t/// Explicit candidate setup')
s=replace(s,'IEnumerable<GltfMaterialTextureRole> Roles( int? materialIndex ) => useLegacy?.Invoke( materialIndex ) == true\n\t\t\t? [GltfMaterialTextureRole.BaseColor, GltfMaterialTextureRole.MetallicRoughness]\n\t\t\t: materialIndex is int index && Document.ReadMaterial( index ).Unlit ? [GltfMaterialTextureRole.BaseColor] : null;','''IEnumerable<GltfMaterialTextureRole> Roles( int? materialIndex, GltfGpuPrimitivePlan plan ) =>
			materialIndex is int source && Document.ReadMaterial( source ).Unlit ? [GltfMaterialTextureRole.BaseColor] :
			pointLinePolicy && GltfPublicMaterialRoute.NoNormalPointLine( plan ) ? GltfPointLineSourceMaterialAdapter.Roles :
			useLegacy?.Invoke( materialIndex, plan ) == true ? [GltfMaterialTextureRole.BaseColor, GltfMaterialTextureRole.MetallicRoughness] : null;''')
s=s.replace('Roles( rawPlans[x].Material ), uvSemantics','Roles( rawPlans[x].Material, rawPlans[x] ), uvSemantics')
s=s.replace('Roles( materialIndex ), uvSemantics','Roles( materialIndex, rawPlans[primitive] ), uvSemantics')
s=s.replace('useLegacy?.Invoke( materialIndex ) == true','useLegacy?.Invoke( materialIndex, projected ) == true')
changes[p]=s
changes['engine/Sandbox.Engine/Resources/Model/Gltf/Materials/GltfPointLineSourceMaterialAdapter.cs']=Path(__file__).with_name('GltfPointLineSourceMaterialAdapter.proposed.cs').read_text()
changes['game/core/shaders/gltf_point_line.shader']=Path(__file__).with_name('point_line_candidate.shader').read_text()
patch=[]
for p,v in changes.items():
 old=get(p) if (root/p).exists() else ''
 patch.extend(difflib.unified_diff(old.splitlines(True),v.splitlines(True),fromfile=('a/'+p if old else '/dev/null'),tofile='b/'+p))
out.write_text(''.join(patch))
