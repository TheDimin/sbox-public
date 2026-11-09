using System;
using System.Collections.Generic;
using Sandbox.SceneRenderer.Gpu;

namespace Sandbox.Gltf.DirectGpu.DirectionalLightValidation;
/// <summary>Native light-packing experiment only: no lux or pixel calibration is implied.</summary>
[TestClass]
public sealed class DirectionalNativeTests
{
 public static List<object> Measurements { get; } = new();
 [TestMethod]
 public void DirectionColorZeroAndDeletionAreObservedInNativePacking()
 {
  Assert.IsTrue(Application.IsEditor && !Game.IsPlaying && !Graphics.IsActive);
  var scene=Scene.CreateEditorScene(); SceneDirectionalLight light=null;
  try
  {
   var world=scene.SceneWorld; var before=world.SceneObjects.Count();
   light=new SceneDirectionalLight(world,Rotation.LookAt(new global::Vector3(-1,0,0)),new global::Color(.9f,.8f,.1f)){ShadowsEnabled=false};
   Assert.IsTrue(RenderContext.PackSceneLight(light,out var first,out _,out _));
   Measurements.Add(new {label="authored_rgb_candidate", color=new[]{first.Color.x,first.Color.y,first.Color.z}, forward=new[]{first.Row0.x,first.Row1.x,first.Row2.x}, type=first.Type, scope="packed state; candidate one native color unit, not measured lux"});
   Assert.AreEqual(.9f,first.Color.x,1e-6f); Assert.AreEqual(.8f,first.Color.y,1e-6f); Assert.AreEqual(.1f,first.Color.z,1e-6f);
   Assert.AreEqual(1f,first.Row0.x,1e-5f); Assert.AreEqual(0f,first.Row1.x,1e-5f); Assert.AreEqual(0f,first.Row2.x,1e-5f);
   light.LightColor=global::Color.Black;
   Assert.IsTrue(RenderContext.PackSceneLight(light,out var zero,out _,out _));
   Measurements.Add(new {label="zero",color=new[]{zero.Color.x,zero.Color.y,zero.Color.z}});
   Assert.AreEqual(0f,zero.Color.x,1e-6f); Assert.AreEqual(0f,zero.Color.y,1e-6f); Assert.AreEqual(0f,zero.Color.z,1e-6f);
   light.Delete(); light=null; Assert.AreEqual(before,world.SceneObjects.Count(),"Owned light must be deleted.");
  }
  finally {light?.Delete();scene.Destroy();}
 }
}
