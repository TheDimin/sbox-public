using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.SceneRenderer.Gpu;

namespace Sandbox.Gltf.DirectGpu.PointLightIntensityValidation;

// Private scene packing only, no render or production cookie marker.
[TestClass]
internal sealed class NativeCookieProbe
{
 public static List<object> Measurements {get;}=new();
 [TestMethod]
 public async Task CookieBindingAndTextureViewIdentity()
 {
  await MainThread.Wait();
  Assert.IsTrue(Application.IsEditor && !Game.IsPlaying && !Graphics.IsActive);
  var scene=Scene.CreateEditorScene();Texture marker=null,other=null;ScenePointLight point=null;SceneSpotLight spot=null,ordinary=null;
  try {
   int before=scene.SceneWorld.SceneObjects.Count();
   marker=Texture.Create(1,1,ImageFormat.RGBA8888).WithMips(1).WithData(new byte[]{255,255,255,255}).WithStaticUsage().Finish();
   other=Texture.Create(1,1,ImageFormat.RGBA8888).WithMips(1).WithData(new byte[]{128,128,128,255}).WithStaticUsage().Finish();
   Assert.IsTrue(marker.IsValid && other.IsValid);Assert.AreNotEqual(marker.Index,other.Index,"Distinct live texture views must not alias");
   point=new ScenePointLight(scene.SceneWorld,Vector3.Zero,100,Color.White){ShadowsEnabled=false};
   spot=new SceneSpotLight(scene.SceneWorld,Vector3.Zero,Color.White){Radius=100,ConeInner=30,ConeOuter=60,ShadowsEnabled=false};
   ordinary=new SceneSpotLight(scene.SceneWorld,Vector3.Zero,Color.White){Radius=100,ConeInner=30,ConeOuter=60,ShadowsEnabled=false,LightCookie=other};
   Assert.IsTrue(RenderContext.PackSceneLight(point,out var pointBefore,out _,out _));Assert.IsTrue(RenderContext.PackSceneLight(spot,out var spotBefore,out _,out _));
   Assert.IsTrue(RenderContext.PackSceneLight(ordinary,out var otherBefore,out _,out _));
   point.LightCookie=marker;spot.LightCookie=marker;
   Assert.IsTrue(RenderContext.PackSceneLight(point,out var pointAfter,out _,out _));Assert.IsTrue(RenderContext.PackSceneLight(spot,out var spotAfter,out _,out _));Assert.IsTrue(RenderContext.PackSceneLight(ordinary,out var otherAfter,out _,out _));
   int markerFirst=marker.Index;ulong frameBefore=Application.FrameCount;
   await Task.Delay(150);await MainThread.Wait();ulong frameAfter=Application.FrameCount;
   point.Delete();point=null;
   Assert.IsTrue(RenderContext.PackSceneLight(spot,out var surviving,out _,out _));
   spot.LightCookie=null;Assert.IsTrue(RenderContext.PackSceneLight(spot,out var cleared,out _,out _));
   spot.LightCookie=other;Assert.IsTrue(RenderContext.PackSceneLight(spot,out var replaced,out _,out _));
   spot.LightCookie=marker;Assert.IsTrue(RenderContext.PackSceneLight(spot,out var restored,out _,out _));
   int markerLast=marker.Index;
   Measurements.Add(new{marker_index=markerFirst,marker_index_after=markerLast,frame_before=frameBefore,frame_after=frameAfter,other_index=other.Index,point_before=pointBefore.LightCookieTextureIndex,point_after=pointAfter.LightCookieTextureIndex,point_flags_before=pointBefore.Flags,point_flags_after=pointAfter.Flags,point_cone_w=pointAfter.SpotLightInnerOuterConeCosines.w,spot_before=spotBefore.LightCookieTextureIndex,spot_after=spotAfter.LightCookieTextureIndex,spot_flags_before=spotBefore.Flags,spot_flags_after=spotAfter.Flags,ordinary_before=otherBefore.LightCookieTextureIndex,ordinary_after=otherAfter.LightCookieTextureIndex,surviving=surviving.LightCookieTextureIndex,cleared=cleared.LightCookieTextureIndex,replaced=replaced.LightCookieTextureIndex,restored=restored.LightCookieTextureIndex,scope="Private-scene native packing and live resource rebinding/owner deletion only; no rendering or general index lifetime guarantee."});
   Assert.IsTrue(frameAfter>frameBefore,"Editor did not advance frames during live texture identity control");
   Assert.AreEqual(otherBefore.LightCookieTextureIndex,otherAfter.LightCookieTextureIndex,"Other light's cookie changed");
   Assert.AreEqual(markerFirst,markerLast,"Live marker index changed during owner and binding changes");
   Assert.AreEqual(spotAfter.LightCookieTextureIndex,surviving.LightCookieTextureIndex,"Deleting point changed surviving spot cookie");
   Assert.AreEqual(spotBefore.LightCookieTextureIndex,cleared.LightCookieTextureIndex,"Clearing spot cookie did not restore baseline");
   Assert.AreEqual(otherBefore.LightCookieTextureIndex,replaced.LightCookieTextureIndex,"Replacing spot cookie failed");
   Assert.AreEqual(spotAfter.LightCookieTextureIndex,restored.LightCookieTextureIndex,"Restoring spot cookie failed");
   Assert.AreEqual((uint)marker.Index,spotAfter.LightCookieTextureIndex,"Spot cookie does not pack its view index");
   Assert.AreEqual((uint)marker.Index,pointAfter.LightCookieTextureIndex,"Point cookie does not pack its view index");
   spot.Delete();spot=null;ordinary.Delete();ordinary=null;Assert.AreEqual(before,scene.SceneWorld.SceneObjects.Count());
  } finally {point?.Delete();spot?.Delete();ordinary?.Delete();marker?.Dispose();other?.Dispose();scene.Destroy();}
 }
}
