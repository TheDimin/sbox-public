#!/usr/bin/env python3
"""Prepare a reviewable patch only; never mutate frozen shared source."""
from pathlib import Path
import difflib, hashlib, json

root=Path(__file__).resolve().parents[6]
owned=Path(__file__).resolve().parent
changes={}
def change(path, replacements):
    before=(root/path).read_text()
    after=before
    for old,new in replacements:
        assert after.count(old)==1,(path,old[:100],after.count(old))
        after=after.replace(old,new,1)
    changes[path]=(before,after)

gltf='engine/Sandbox.Gltf/'
engine='engine/Sandbox.Engine/Resources/Model/Gltf/'
change(gltf+'GltfContracts.cs',[
 ('\tpublic bool Unlit { get; init; }','\tpublic bool Unlit { get; init; }\n\t/// <summary>Supported thin-surface diffuse transmission; null for absent or unsupported extension.</summary>\n\tpublic GltfDiffuseTransmission DiffuseTransmission { get; init; }'),
 ('public sealed partial class GltfTextureInfo','''/// <summary>Standard KHR_materials_diffuse_transmission Release Candidate parameters.</summary>
public sealed class GltfDiffuseTransmission
{
\tpublic float Factor { get; init; }
\tpublic Vector3 ColorFactor { get; init; } = Vector3.One;
\tpublic GltfTextureInfo Texture { get; init; }
\tpublic GltfTextureInfo ColorTexture { get; init; }
}

public sealed partial class GltfTextureInfo''')])
change(gltf+'GltfDocument.Materials.cs',[
 ('\t\t\tUnlit = unlit,','\t\t\tUnlit = unlit,\n\t\t\tDiffuseTransmission = MaterialDiffuseTransmission( value ),')])
new='''using System.Numerics;
using System.Text.Json;

namespace Sandbox.Gltf;

public sealed partial class GltfDocument
{
\tprivate GltfDiffuseTransmission MaterialDiffuseTransmission( JsonElement material )
\t{
\t\tconst string name = "KHR_materials_diffuse_transmission";
\t\tif ( !SupportsExtension( name ) ) return null;
\t\tvar value = MaterialExtension( material, name );
\t\tif ( value.ValueKind == JsonValueKind.Undefined ) return null;
\t\tif ( !ExtensionNames( "extensionsUsed" ).Contains( name, StringComparer.Ordinal ) )
\t\t\tthrow new GltfException( $"{name} must be listed in extensionsUsed." );
\t\tMaterialRequireObject( value, name );
\t\tforeach ( var excluded in new[] { "KHR_materials_unlit", "KHR_materials_pbrSpecularGlossiness" } )
\t\t\tif ( MaterialExtension( material, excluded ).ValueKind != JsonValueKind.Undefined )
\t\t\t\tthrow new GltfException( $"{name} cannot be used with {excluded}." );
\t\tvar color = MaterialVector( value, "diffuseTransmissionColorFactor", [1, 1, 1], 0, 1 );
\t\treturn new GltfDiffuseTransmission
\t\t{
\t\t\tFactor = MaterialOptionalNumber( value, "diffuseTransmissionFactor", 0, 0, 1 ),
\t\t\tColorFactor = new Vector3( color[0], color[1], color[2] ),
\t\t\tTexture = MaterialTexture( value, "diffuseTransmissionTexture", false ),
\t\t\tColorTexture = MaterialTexture( value, "diffuseTransmissionColorTexture", true )
\t\t};
\t}
}
'''
changes[gltf+'GltfDocument.DiffuseTransmission.cs']=('',new)
change(gltf+'GltfDocument.MaterialImages.cs',[
 ('\t\t\tAdd( material, "occlusionTexture" ); Add( material, "emissiveTexture" );','''\t\t\tAdd( material, "occlusionTexture" ); Add( material, "emissiveTexture" );
\t\t\tif ( SupportsExtension( "KHR_materials_diffuse_transmission" ) )
\t\t\t{
\t\t\t\tvar transmission = MaterialExtension( material, "KHR_materials_diffuse_transmission" );
\t\t\t\tAdd( transmission, "diffuseTransmissionTexture" );
\t\t\t\tAdd( transmission, "diffuseTransmissionColorTexture" );
\t\t\t}''')])
change(engine+'Materials/GltfMaterialUvPlan.cs',[
 ('\tEmissive\n}','\tEmissive,\n\tDiffuseTransmission,\n\tDiffuseTransmissionColor\n}'),
 ('\t\tGltfMaterialTextureRole.Emissive\n','\t\tGltfMaterialTextureRole.Emissive,\n\t\tGltfMaterialTextureRole.DiffuseTransmission,\n\t\tGltfMaterialTextureRole.DiffuseTransmissionColor\n'),
 ('\t\tGltfMaterialTextureRole.Emissive => material?.EmissiveTexture,','\t\tGltfMaterialTextureRole.Emissive => material?.EmissiveTexture,\n\t\tGltfMaterialTextureRole.DiffuseTransmission => material?.DiffuseTransmission?.Texture,\n\t\tGltfMaterialTextureRole.DiffuseTransmissionColor => material?.DiffuseTransmission?.ColorTexture,')])
change(engine+'Materials/GltfMaterialTextureEncodingPlan.cs',[
 ('internal bool HasLinearAlpha => Role == GltfMaterialTextureRole.BaseColor;','internal bool HasLinearAlpha => Role is GltfMaterialTextureRole.BaseColor or GltfMaterialTextureRole.DiffuseTransmission;'),
 ('GltfMaterialTextureRole.Occlusion, GltfMaterialTextureRole.Emissive] );','GltfMaterialTextureRole.Occlusion, GltfMaterialTextureRole.Emissive, GltfMaterialTextureRole.DiffuseTransmission, GltfMaterialTextureRole.DiffuseTransmissionColor] );'),
 ('\t\tGltfMaterialTextureRole.Emissive => (material?.EmissiveTexture, true, GltfMaterialTextureChannels.Rgb),','\t\tGltfMaterialTextureRole.Emissive => (material?.EmissiveTexture, true, GltfMaterialTextureChannels.Rgb),\n\t\tGltfMaterialTextureRole.DiffuseTransmission => (material?.DiffuseTransmission?.Texture, false, GltfMaterialTextureChannels.Alpha),\n\t\tGltfMaterialTextureRole.DiffuseTransmissionColor => (material?.DiffuseTransmission?.ColorTexture, true, GltfMaterialTextureChannels.Rgb),')])
change(engine+'GltfStockMaterialAdapter.cs',[
 ('\t\tinternal float AlphaCutoff { get; init; }','\t\tinternal float AlphaCutoff { get; init; }\n\t\tinternal GltfDiffuseTransmission DiffuseTransmission { get; init; }'),
 ('\t\tvar normalScale = material?.NormalTexture?.Scale ?? 1;', '''\t\tif ( material?.DiffuseTransmission is { } transmission )
\t\t{
\t\t\tforeach ( var value in new[] { transmission.Factor, transmission.ColorFactor.X, transmission.ColorFactor.Y, transmission.ColorFactor.Z } )
\t\t\t\tif ( !float.IsFinite( value ) || value < 0 || value > 1 ) throw new GltfException( "Diffuse transmission factors must contain finite linear values in [0,1]." );
\t\t}
\t\tvar normalScale = material?.NormalTexture?.Scale ?? 1;'''),
 ('\t\tvar textures = GltfMaterialTextureEncodingPlan.CreateForRoles( document, material, roles );','\t\tif ( material?.DiffuseTransmission is not null ) roles = [.. roles, GltfMaterialTextureRole.DiffuseTransmission, GltfMaterialTextureRole.DiffuseTransmissionColor];\n\t\tvar textures = GltfMaterialTextureEncodingPlan.CreateForRoles( document, material, roles );'),
 ('\t\t\tAlphaMask = material?.AlphaMode == "MASK", AlphaCutoff = material?.AlphaCutoff ?? .5f','\t\t\tAlphaMask = material?.AlphaMode == "MASK", AlphaCutoff = material?.AlphaCutoff ?? .5f, DiffuseTransmission = material?.DiffuseTransmission'),
 ('projection.Sources.Count > 5','projection.Sources.Count > (prepared.DiffuseTransmission is null ? 5 : 7)'),
 ('\t\tif ( prepared.AlphaMask ) scalars.Add','''\t\tif ( prepared.DiffuseTransmission is { } transmission )
\t\t{
\t\t\tscalars.Add( new( "g_flGltfDiffuseTransmissionFactor", transmission.Factor ) );
\t\t\tvectors.Add( new( "g_vGltfDiffuseTransmissionColorFactor", new( transmission.ColorFactor.X, transmission.ColorFactor.Y, transmission.ColorFactor.Z, 0 ) ) );
\t\t\tforeach ( var role in new[] { GltfMaterialTextureRole.DiffuseTransmission, GltfMaterialTextureRole.DiffuseTransmissionColor } )
\t\t\t\tscalars.Add( new( "g_flGltfHas" + role + "Texture", prepared.Resources.ContainsKey( role ) ? 1f : 0f ) );
\t\t}
\t\tif ( prepared.AlphaMask ) scalars.Add'''),
 ('slot is < 0 or > 4 || slot >= projection.Sources.Count','slot < 0 || slot >= (prepared.DiffuseTransmission is null ? 5 : 7) || slot >= projection.Sources.Count')])
change(engine+'GltfPublicMaterialRoute.cs',[
 ('(material.AlphaMode == "MASK" || material.NormalTexture','(material.DiffuseTransmission is not null || material.AlphaMode == "MASK" || material.NormalTexture')])
change(engine+'DirectGpu/GltfGpuAsset.Variants.cs',[
 ('\t\tvar projections = Primitives.ToDictionary( x => x, x => rawPlans[x].ProjectMaterial( rawPlans[x].Material, 5, Roles( rawPlans[x].Material ), [0, 1, 4, 5, 6] ) );','''\t\t// Every reachable variant uses one compatible vertex declaration. Core
\t\t// templates ignore the two additional fields when a diffuse-transmission
\t\t// variant needs all seven independently selected source UV sets.
\t\tvar uvCount = requested.Any( index => index is int source && Document.ReadMaterial( source ).DiffuseTransmission is not null ) ? 7 : 5;
\t\tint[] uvSemantics = uvCount == 7 ? [0, 1, 4, 5, 6, 7, 8] : [0, 1, 4, 5, 6];
\t\tvar projections = Primitives.ToDictionary( x => x, x => rawPlans[x].ProjectMaterial( rawPlans[x].Material, uvCount, Roles( rawPlans[x].Material ), uvSemantics ) );'''),
 ('rawPlans[primitive].ProjectMaterial( materialIndex, 5, Roles( materialIndex ), [0, 1, 4, 5, 6] )','rawPlans[primitive].ProjectMaterial( materialIndex, uvCount, Roles( materialIndex ), uvSemantics )')])
change(engine+'GltfManagedModelResource.cs',[
 ('(material.DoubleSided ? ":double" : ":single");','(material.DoubleSided ? ":double" : ":single") + (material.DiffuseTransmission is null ? "" : ":diffuse-transmission");'),
 ('material.Unlit ? "shaders/gltf_unlit_five_uv.shader" : "shaders/gltf_surface_five_uv.shader"','material.Unlit ? "shaders/gltf_unlit_five_uv.shader" : material.DiffuseTransmission is not null ? "shaders/gltf_diffuse_transmission.shader" : "shaders/gltf_surface_five_uv.shader"')])
patch=''.join(''.join(difflib.unified_diff(a.splitlines(True),b.splitlines(True),fromfile='a/'+p,tofile='b/'+p)) for p,(a,b) in changes.items())
(owned/'diffuse-transmission-integration.patch').write_text(patch)
((Path(__file__).resolve().parents[6]/'research/gltf-validation/direct-gpu/models/MandarinOrange') / 'diffuse-transmission-integration-inputs.json').write_text(json.dumps({'status':'proposal_only_shared_files_unchanged','capability_list':'GltfModelLoader.SupportedExtensions deliberately unchanged until actual shader acceptance','files':[{'path':p,'before_sha256':hashlib.sha256(a.encode()).hexdigest() if a else None,'after_sha256':hashlib.sha256(b.encode()).hexdigest()} for p,(a,b) in changes.items()]},indent=2)+'\n')
print(json.dumps({'files':len(changes),'patch_sha256':hashlib.sha256(patch.encode()).hexdigest(),'shared_source_mutated':False}))
