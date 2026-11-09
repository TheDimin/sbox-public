using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.Gltf.DirectGpu.Lighting;
using Sandbox.SceneRenderer.Gpu;
using NMatrix = System.Numerics.Matrix4x4;

namespace Sandbox.Gltf.DirectGpu.DirectionalLightValidation;
/// <summary>Lifecycle and packed native state with explicit experimental calibration; not photometry.</summary>
[TestClass]
public sealed class DirectionalFactoryTests
{
 [TestMethod]
 public void TwoInstancesUpdateVisibilityPlacementAndDisposeIndependently()
 {
  const string json="""{"asset":{"version":"2.0"},"extensionsUsed":["KHR_lights_punctual"],"scene":0,"scenes":[{"nodes":[0]}],"nodes":[{"extensions":{"KHR_lights_punctual":{"light":0}}}],"extensions":{"KHR_lights_punctual":{"lights":[{"type":"directional","intensity":2,"color":[0.9,0.8,0.1]}]}}} """;
  using var doc=GltfReader.Read(Encoding.UTF8.GetBytes(json),supportedExtensions:new HashSet<string>{"KHR_lights_punctual"});
  var plan=GltfPunctualLightPlan.Create(doc); var scene=Scene.CreateEditorScene();
  GltfPunctualLightInstances a=null,b=null;
  try
  {
   var world=scene.SceneWorld; var before=world.SceneObjects.Count();
   var factory=new GltfDirectionalLightFactory(world,.5f); // explicit diagnostic candidate, not measured lux conversion
   a=GltfPunctualLightInstances.Create(plan,factory,null,NMatrix.Identity,[true]);
   var first=world.SceneObjects.OfType<SceneDirectionalLight>().Single();
   Assert.IsTrue(RenderContext.PackSceneLight(first,out var packed,out _,out _));
   Assert.AreEqual(.9f,packed.Color.x,1e-6f); Assert.AreEqual(.8f,packed.Color.y,1e-6f); Assert.AreEqual(.1f,packed.Color.z,1e-6f); Assert.AreEqual(1f,packed.Row0.x,1e-5f);
   b=GltfPunctualLightInstances.Create(plan,factory,null,NMatrix.CreateRotationZ(MathF.PI/2),[true]);
   var second=world.SceneObjects.OfType<SceneDirectionalLight>().Single(x=>!ReferenceEquals(x,first));
   Assert.IsTrue(RenderContext.PackSceneLight(second,out var rotated,out _,out _)); Assert.AreEqual(1f,rotated.Row1.x,1e-5f);
   a.Update(null,NMatrix.Identity,[false]); Assert.IsTrue(RenderContext.PackSceneLight(first,out var hidden,out _,out _)); Assert.AreEqual(0f,hidden.Color.x,1e-6f);
   a.Dispose(); a=null; Assert.IsTrue(second.IsValid()); Assert.AreEqual(before+1,world.SceneObjects.Count());
   b.Dispose(); b=null; Assert.AreEqual(before,world.SceneObjects.Count());
   Assert.ThrowsException<ArgumentOutOfRangeException>(()=>new GltfDirectionalLightFactory(world,0));
  }
  finally {a?.Dispose();b?.Dispose();scene.Destroy();}
 }
}
