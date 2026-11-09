using System;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
namespace Sandbox.Gltf.DirectGpu.AlphaBlendModeValidation;
[TestClass]
public sealed class AlphaManualRawPreflightTests
{
 [TestMethod]
 public void OpaqueProjectedUnpreparedManualOverrideRejectsBeforeDrawingAndPreparedRouteAccepts()
 {
  var fixture=AlphaControlFixtures.All().Single(x=>x.Name=="linear_texture_factor_color4");var json=JsonNode.Parse(fixture.Bytes)!.AsObject();json["materials"]![0]!["alphaMode"]="OPAQUE";var bytes=GltfCompiler.Compile(Encoding.UTF8.GetBytes(json.ToJsonString()),null);using var doc=GltfReader.Read(bytes);
  var scene=Scene.CreateEditorScene();var template=Material.FromShader("shaders/gltf_surface_five_uv.shader").CreateCopy();var stock=Material.Load("materials/default/white.vmat");GltfGpuAsset asset=null;GltfGpuInstance instance=null;
  try
  {
   template.SetFeature("F_ALPHA_TEST",0);template.SetFeature("F_RENDER_BACKFACES",0);asset=GltfGpuAsset.PrepareProjected(doc,"shaders/gltf_deform_cs.shader",1024*1024,(_,_)=>template,global::Sandbox.GltfUnlitMaterialPlan.Features.None,sourceLitFeatureSidedness:true);instance=asset.CreateInstance(scene.SceneWorld);
   var calls=0;Assert.ThrowsException<GltfException>(()=>instance.RenderManually(stock,()=>calls++));Assert.AreEqual(0,calls,"Unprepared manual override must fail before any draw.");
   instance.SetMaterialOverride(0,stock);instance.RenderManually(stock,()=>calls++);Assert.AreEqual(1,calls,"Prepared route must allow the manual call.");instance.Dispose();instance=null;asset.Dispose();asset=null;
  }
  finally{instance?.Dispose();asset?.Dispose();template.Destroy();scene.Destroy();}
 }
}
