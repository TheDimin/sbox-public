using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Nodes;

namespace Sandbox.Gltf.DirectGpu.UnlitTestValidation;
using NVector4=System.Numerics.Vector4;

/// <summary>
/// Independent supplemental fixture for the shared mixed-packet integration.
/// No upstream model form is invented. UV0=(.25,.5) samples right texel under
/// default lit offset(.5,0), left texel under alternate offset(0,0); raw stock
/// overrides must receive the original .25, never either baked material view.
/// </summary>
internal static class UnlitMixedMaterialFixture
{
 internal static readonly IReadOnlySet<string> Extensions=new HashSet<string>{"KHR_materials_unlit","KHR_texture_transform","KHR_materials_variants"};
 internal static byte[] Build(bool mixed=true,bool alternateAsDefault=false)
 {
  var root=JsonNode.Parse(UnlitFeatureFixtures.Build(new("mixed","OPAQUE",NVector4.One,NVector4.One,Texture:true))).AsObject();
  JsonObject Material(string name,bool unlit,NVector4 factor,float? offset)
  {
   var mat=new JsonObject{{"name",name},{"pbrMetallicRoughness",new JsonObject{{"baseColorFactor",new JsonArray(factor.X,factor.Y,factor.Z,factor.W)},{"metallicFactor",0},{"roughnessFactor",1}}}};
   if(unlit)mat["extensions"]=new JsonObject{{"KHR_materials_unlit",new JsonObject()}};
   if(offset.HasValue)mat["pbrMetallicRoughness"]["baseColorTexture"]=new JsonObject{{"index",0},{"extensions",new JsonObject{{"KHR_texture_transform",new JsonObject{{"offset",new JsonArray(offset.Value,0)}}}}}};
   return mat;
  }
  root["materials"]=new JsonArray(Material("unlit_orange",true,UnlitTestReference.BaseColors[0],null),Material("lit_transformed_right",false,NVector4.One,.5f),Material("unlit_blue",true,UnlitTestReference.BaseColors[1],null),Material("lit_alternate_left",false,NVector4.One,0));
  var first=root["meshes"][0].DeepClone();var second=first.DeepClone();
  first["primitives"][0]["material"]=alternateAsDefault?2:0;second["primitives"][0]["material"]=alternateAsDefault?3:1;
  first["primitives"][0]["extensions"]=JsonNode.Parse("{\"KHR_materials_variants\":{\"mappings\":[{\"material\":2,\"variants\":[0]}]}}");
  second["primitives"][0]["extensions"]=JsonNode.Parse("{\"KHR_materials_variants\":{\"mappings\":[{\"material\":3,\"variants\":[0]}]}}");
  root["meshes"]=new JsonArray(first,second);root["nodes"]=JsonNode.Parse("[{\"mesh\":0,\"translation\":[-1.5,0,0]},{\"mesh\":1,\"translation\":[1.5,0,0]}]");
  root["scenes"]=JsonNode.Parse(mixed?"[{\"nodes\":[0,1]}]":"[{\"nodes\":[1]}]");
  root["extensionsUsed"]=new JsonArray("KHR_materials_unlit","KHR_texture_transform","KHR_materials_variants");
  root["extensions"]=JsonNode.Parse("{\"KHR_materials_variants\":{\"variants\":[{\"name\":\"alternate\"}]}}");
  return Encoding.UTF8.GetBytes(root.ToJsonString());
 }
 internal static NVector4 ExpectedUnlit(int? variant)=>UnlitTestReference.BaseColors[variant.HasValue?1:0];
 internal static NVector4 ExpectedLitBaseColor(int? variant)=>UnlitFeatureFixtures.TextureValue(variant.HasValue ? .25f : .75f);
 internal const float RawOverrideUv=.25f;
}
