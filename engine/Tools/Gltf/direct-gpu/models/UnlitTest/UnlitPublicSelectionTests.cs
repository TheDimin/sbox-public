using System;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Sandbox.Gltf.DirectGpu.UnlitTestValidation;

/// <summary>Scene-aware public dispatch controls, including independent lit/unlit material slots.</summary>
[TestClass]
public sealed class UnlitPublicSelectionTests
{
 public static string CheckoutRoot {get;set;}
 private static UnlitTestReference.Form Fixture(bool compiled,bool noDefault=false)
 {
  var bytes=UnlitFeatureFixtures.Build(new("selection","OPAQUE",new(1,.217637640824031f,0,1),System.Numerics.Vector4.One));
  var json=JsonNode.Parse(bytes).AsObject();var lit=json["materials"][0].DeepClone().AsObject();lit.Remove("extensions");lit["pbrMetallicRoughness"]["metallicFactor"]=0;lit["pbrMetallicRoughness"]["roughnessFactor"]=1;json["materials"].AsArray().Add(lit);
  var mesh=json["meshes"][0].DeepClone();mesh["primitives"][0]["material"]=1;json["meshes"].AsArray().Add(mesh);json["nodes"].AsArray().Add(new JsonObject{{"mesh",1}});
  json["scenes"]=JsonNode.Parse("[{\"nodes\":[0]},{\"nodes\":[1]},{\"nodes\":[0,1]},{\"nodes\":[]}]");
  if(noDefault)json.Remove("scene");else json["scene"]=1;
  bytes=Encoding.UTF8.GetBytes(json.ToJsonString());if(compiled)bytes=GltfCompiler.Compile(bytes,null,UnlitTestReference.Selected);
  return new(){Name="derived_selection",Compiled=compiled,Bytes=bytes};
 }
 [DataTestMethod][DataRow(false)][DataRow(true)]
 public void SelectedSceneIgnoresUnusedUnlitAndExplicitUnlitUsesSourceShader(bool compiled)
 {
  using var mount=new UnlitPublicMount(Fixture(compiled));var defaultModel=mount.Load();
  Assert.IsTrue(defaultModel.IsValid&&!defaultModel.IsError&&defaultModel.native.IsNull);Assert.AreEqual(1,defaultModel.GltfSelectedScene);Assert.AreEqual(1,defaultModel.GltfDefaultScene);Assert.AreEqual(4,defaultModel.GltfSceneCount);
  Assert.AreEqual(1,defaultModel.Materials.Length);Assert.IsFalse(defaultModel.Materials[0].ShaderName.Contains("gltf_unlit",StringComparison.OrdinalIgnoreCase),"Unused unlit must not switch a lit selected scene's material pipeline.");
  var unlit=mount.Load(0);Assert.AreEqual(0,unlit.GltfSelectedScene);Assert.AreEqual(1,unlit.Materials.Length);Assert.IsTrue(unlit.Materials[0].ShaderName.Contains("gltf_unlit",StringComparison.OrdinalIgnoreCase));Assert.IsFalse(ReferenceEquals(defaultModel,unlit));
  var empty=mount.Load(3);Assert.IsTrue(empty.IsValid&&!empty.IsError&&!empty.HasRenderMeshes());Assert.AreEqual(3,empty.GltfSelectedScene);Assert.AreEqual(0,empty.Materials.Length);
 }
 [DataTestMethod][DataRow(false)][DataRow(true)]
 public void MixedReachabilityPreservesIndependentLitAndUnlitSourceSlots(bool compiled)
 {
  using var mount=new UnlitPublicMount(Fixture(compiled));var mixed=mount.Load(2);
  Assert.IsTrue(mixed.IsValid&&!mixed.IsError&&mixed.native.IsNull);Assert.AreEqual(2,mixed.GltfSelectedScene);Assert.AreEqual(2,mixed.Materials.Length);
  Assert.IsTrue(mixed.Materials[0].ShaderName.Contains("gltf_unlit",StringComparison.OrdinalIgnoreCase),"Unlit source slot lost its source shader.");Assert.IsFalse(mixed.Materials[1].ShaderName.Contains("gltf_unlit",StringComparison.OrdinalIgnoreCase),"Ordinary lit slot was silently treated as unlit.");
  var stillUnlit=mount.Load(0);Assert.IsTrue(stillUnlit.IsValid&&!stillUnlit.IsError&&stillUnlit.Materials[0].ShaderName.Contains("gltf_unlit",StringComparison.OrdinalIgnoreCase));
  var stillLit=mount.Load();Assert.IsFalse(stillLit.Materials[0].ShaderName.Contains("gltf_unlit",StringComparison.OrdinalIgnoreCase));
 }
 [DataTestMethod][DataRow(false)][DataRow(true)]
 public void NoDefaultSceneKeepsUnlitUnselectedUntilExplicitSelection(bool compiled)
 {
  using var mount=new UnlitPublicMount(Fixture(compiled,true));var model=mount.Load();Assert.IsTrue(model.IsValid&&!model.IsError&&!model.HasRenderMeshes());Assert.IsNull(model.GltfSelectedScene);Assert.IsNull(model.GltfDefaultScene);Assert.AreEqual(0,model.Materials.Length);
  var unlit=mount.Load(0);Assert.IsTrue(unlit.IsValid&&!unlit.IsError&&unlit.HasRenderMeshes());Assert.AreEqual(0,unlit.GltfSelectedScene);Assert.IsTrue(unlit.Materials[0].ShaderName.Contains("gltf_unlit",StringComparison.OrdinalIgnoreCase));
 }
}
