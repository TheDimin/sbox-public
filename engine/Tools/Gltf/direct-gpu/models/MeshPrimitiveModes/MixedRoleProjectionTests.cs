using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Sandbox.Gltf.DirectGpu.MeshPrimitiveModesValidation.MeshPrimitiveModesReference;
namespace Sandbox.Gltf.DirectGpu.MeshPrimitiveModesValidation;
[TestClass]
internal sealed class MixedRoleProjectionTests
{
 static string checkout;public static List<object> Measurements{get;}=new();public static void Configure(string root){checkout=root;Measurements.Clear();}
 [TestMethod]
 public void SharedMaterialMissingIgnoredNormalUvProducesDifferentCompactSlots()
 {
  var dir=Path.Combine(checkout,"Temp/gltf-reference/glTF-Sample-Assets/Models/MeshPrimitiveModes/glTF");var json=JsonNode.Parse(File.ReadAllBytes(Path.Combine(dir,"MeshPrimitiveModes.gltf")));var original=File.ReadAllBytes(Path.Combine(dir,"buffer.bin"));var data=new byte[216+7*16];original.CopyTo(data,0);
  for(int v=0;v<7;v++)for(int c=0;c<4;c++)BitConverter.GetBytes(c%2==0?.25f:.5f).CopyTo(data,216+v*16+c*4);
  var view=json["bufferViews"].AsArray().Count;json["bufferViews"].AsArray().Add(new JsonObject{["buffer"]=0,["byteOffset"]=216,["byteLength"]=112,["byteStride"]=16,["target"]=34962});
  int first=json["accessors"].AsArray().Count;foreach(int offset in new[]{0,8})json["accessors"].AsArray().Add(new JsonObject{["bufferView"]=view,["byteOffset"]=offset,["componentType"]=5126,["count"]=7,["type"]="VEC2"});
  json["meshes"][1]["primitives"][0]["attributes"]["TEXCOORD_1"]=first+1;json["meshes"][4]["primitives"][0]["attributes"]["TEXCOORD_0"]=first;json["meshes"][4]["primitives"][0]["attributes"]["TEXCOORD_1"]=first+1;
  json["materials"]=JsonNode.Parse("[{\"pbrMetallicRoughness\":{\"baseColorTexture\":{\"index\":0,\"texCoord\":1}},\"normalTexture\":{\"index\":0,\"texCoord\":0}}]");json["textures"]=JsonNode.Parse("[{\"source\":0}]");json["images"]=JsonNode.Parse("[{\"uri\":\"data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAIAAAABCAYAAAD0In+KAAAAEUlEQVR4nGNocFBoUGhwcAAAC8kCgZHWr1kAAAAASUVORK5CYII=\"}]");
  json["buffers"][0]["uri"]="data:application/octet-stream;base64,"+Convert.ToBase64String(data);json["buffers"][0]["byteLength"]=data.Length;
  using var doc=GltfReader.Read(Encoding.UTF8.GetBytes(json.ToJsonString()),null);var material=doc.ReadMaterial(0);var line=doc.MaterializePrimitiveUpload(1,0);var triangle=doc.MaterializePrimitiveUpload(4,0);
  var lp=GltfMaterialUvProjection.Create(GltfMaterialUvPlan.CreateForRoles(material,line,[GltfMaterialTextureRole.BaseColor,GltfMaterialTextureRole.Emissive]),5);
  var tp=GltfMaterialUvProjection.Create(GltfMaterialUvPlan.Create(material,triangle),5);
  Require(lp.Slots[GltfMaterialTextureRole.BaseColor]==0 && tp.Slots[GltfMaterialTextureRole.BaseColor]==1 && tp.Slots[GltfMaterialTextureRole.Normal]==0,"Independent expected compact projections changed.");
  bool rejected=false;try{GltfMaterialUvPlan.Create(material,line);}catch(GltfException){rejected=true;}Require(rejected,"Full lit role selection accepted absent line normal UV.");
  Measurements.Add(new{line_base_slot=0,triangle_base_slot=1,triangle_normal_slot=0,full_lit_line_rejected=rejected,scope="CPU projection planning only; same authored material, missing line normal UV, distinct compact slots. Not a public render or native attribute proof."});
 }
}
