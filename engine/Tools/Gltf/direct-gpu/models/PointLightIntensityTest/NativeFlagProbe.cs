using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.SceneRenderer.Gpu;

namespace Sandbox.Gltf.DirectGpu.PointLightIntensityValidation;

// Private-world packing observation only; no flag is assumed to be safe or
// reserved and no shader consumes these experimental bits.
[TestClass]
internal sealed class NativeFlagProbe
{
 public static List<object> Measurements {get;}=new();
 [TestMethod]
 public void NativeFlagsVersusGpuFlags()
 {
  Assert.IsTrue(Application.IsEditor && !Game.IsPlaying && !Graphics.IsActive);
  var scene=Scene.CreateEditorScene();ScenePointLight light=null;
  try {
   var world=scene.SceneWorld;int before=world.SceneObjects.Count();
   light=new ScenePointLight(world,new Vector3(11,23,37),1000,Color.White){ShadowsEnabled=false};
   uint original=light.lightNative.GetLightFlags();
   foreach(uint bit in new uint[]{0,0x10000,0x40000000,0x80000000}) {
    light.lightNative.SetLightFlags(original|bit);
    uint observed=light.lightNative.GetLightFlags();
    Assert.IsTrue(RenderContext.PackSceneLight(light,out var packed,out var returned,out var baked));
    Measurements.Add(new{requested_bit=bit,native=observed,returned,packed_flags=packed.Flags,packed_type=packed.Type,baked,scope="Native pack only; no culling or shader propagation claim"});
    Assert.AreEqual(original|bit,observed,"Native flag setter did not retain experimental bit");
   }
   light.lightNative.SetLightFlags(original);light.Delete();light=null;Assert.AreEqual(before,world.SceneObjects.Count());
  } finally {light?.Delete();scene.Destroy();}
 }
}
