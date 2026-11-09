using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.Gltf.DirectGpu.Lighting;
using Sandbox.SceneRenderer.Gpu;
using NMatrix=System.Numerics.Matrix4x4;
namespace Sandbox.Gltf.DirectGpu.DirectionalLightValidation;
[TestClass]
public sealed class DirectionalBindingTests
{
 [TestMethod]
 public void SelectedAndUnselectedOwnOnlyTheirLightsAndRejectUnsupportedType()
 {
  const string json="""{"asset":{"version":"2.0"},"extensionsUsed":["KHR_lights_punctual"],"scenes":[{"nodes":[0]},{"nodes":[1]},{"nodes":[0,1]}],"nodes":[{"extensions":{"KHR_lights_punctual":{"light":0}}},{"extensions":{"KHR_lights_punctual":{"light":1}}}],"extensions":{"KHR_lights_punctual":{"lights":[{"type":"directional","intensity":2,"color":[0.9,0.8,0.1]},{"type":"point","intensity":3}]}}}""";
  using var doc=GltfReader.Read(Encoding.UTF8.GetBytes(json),supportedExtensions:new HashSet<string>{"KHR_lights_punctual"});
  var scene=Scene.CreateEditorScene(); GltfDirectionalLightBinding selected=null,empty=null;
  try
  {
   var world=scene.SceneWorld;int before=world.SceneObjects.Count();
   empty=GltfDirectionalLightBinding.Create(doc,null,world,.5f,null,NMatrix.Identity,[true,true]);
   Assert.AreEqual(0,empty.AuthoredLights.Count);Assert.AreEqual(before,world.SceneObjects.Count());
   selected=GltfDirectionalLightBinding.Create(doc,0,world,.5f,null,NMatrix.Identity,[true,true]);
   Assert.AreEqual(1,selected.AuthoredLights.Count);var authored=selected.AuthoredLights.Single();
   Assert.AreEqual(2f,authored.Intensity);Assert.AreEqual(GltfPunctualLightPlan.IntensityUnit.Lux,authored.Unit);Assert.AreEqual(.9f,authored.LinearColor.X,1e-6f);
   var light=world.SceneObjects.OfType<SceneDirectionalLight>().Single();
   selected.Update(null,NMatrix.CreateRotationZ(MathF.PI/2),[true,true]);
   Assert.IsTrue(RenderContext.PackSceneLight(light,out var rotated,out _,out _));Assert.AreEqual(1f,rotated.Row1.x,1e-5f);
   selected.Update(null,NMatrix.Identity,[false,true]);Assert.IsTrue(RenderContext.PackSceneLight(light,out var hidden,out _,out _));Assert.AreEqual(0f,hidden.Color.x,1e-6f);
   Assert.ThrowsException<GltfException>(()=>GltfDirectionalLightBinding.Create(doc,2,world,1,null,NMatrix.Identity,[true,true]));
   Assert.AreEqual(before+1,world.SceneObjects.Count(),"Unsupported type preflight must allocate nothing.");
   selected.Dispose();selected.Dispose();selected=null;Assert.AreEqual(before,world.SceneObjects.Count());
  }
  finally {selected?.Dispose();empty?.Dispose();scene.Destroy();}
 }

 [TestMethod]
 public void TwoBindingsRetainIndependentStateAndZeroIntensity()
 {
  const string json="""{"asset":{"version":"2.0"},"extensionsUsed":["KHR_lights_punctual"],"scene":0,"scenes":[{"nodes":[0,1]}],"nodes":[{"extensions":{"KHR_lights_punctual":{"light":0}}},{"extensions":{"KHR_lights_punctual":{"light":1}}}],"extensions":{"KHR_lights_punctual":{"lights":[{"type":"directional","intensity":4},{"type":"directional","intensity":0}]}}}""";
  using var doc=GltfReader.Read(Encoding.UTF8.GetBytes(json),supportedExtensions:new HashSet<string>{"KHR_lights_punctual"});
  var scene=Scene.CreateEditorScene();GltfDirectionalLightBinding a=null,b=null;
  try
  {
   var world=scene.SceneWorld;int before=world.SceneObjects.Count();
   Assert.ThrowsException<GltfException>(()=>GltfDirectionalLightBinding.Create(doc,0,world,1,null,NMatrix.Identity,[true]));
   Assert.AreEqual(before,world.SceneObjects.Count(),"Invalid visibility must fail before light creation.");
   a=GltfDirectionalLightBinding.Create(doc,0,world,.25f,null,NMatrix.Identity,[true,true]);
   var first=world.SceneObjects.OfType<SceneDirectionalLight>().ToArray();Assert.AreEqual(2,first.Length);
   b=GltfDirectionalLightBinding.Create(doc,0,world,.5f,null,NMatrix.CreateRotationZ(MathF.PI/2),[true,true]);
   var second=world.SceneObjects.OfType<SceneDirectionalLight>().Except(first).ToArray();Assert.AreEqual(2,second.Length);
   Assert.IsTrue(RenderContext.PackSceneLight(first.Single(x=>x.LightColor.r>0),out var ap,out _,out _));Assert.AreEqual(1f,ap.Color.x,1e-6f);
   Assert.IsTrue(RenderContext.PackSceneLight(second.Single(x=>x.LightColor.r>0),out var bp,out _,out _));Assert.AreEqual(2f,bp.Color.x,1e-6f);Assert.AreEqual(1f,bp.Row1.x,1e-5f);
   Assert.IsTrue(RenderContext.PackSceneLight(first.Single(x=>x.LightColor.r==0),out var zero,out _,out _));Assert.AreEqual(0f,zero.Color.x,1e-6f);
   a.Dispose();a=null;Assert.AreEqual(before+2,world.SceneObjects.Count());Assert.IsTrue(second.All(x=>x.IsValid()));
   b.Dispose();b=null;Assert.AreEqual(before,world.SceneObjects.Count());
  }
  finally {a?.Dispose();b?.Dispose();scene.Destroy();}
 }
}
