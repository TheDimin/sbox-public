using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;

namespace Sandbox.Gltf.DirectGpu.PointLightIntensityValidation;

// Anonymous per-test shader only; not a production light factory or photometry.
[TestClass]
internal sealed class CookieShaderTests
{
 public static string CheckoutRoot {get;set;}
 public static List<object> Measurements {get;}=new();
 [TestMethod]
 public async Task MarkerBypassesCookieForPointAndSpotAndPreservesOrdinaryLights()
 {
  await MainThread.Wait();string owned=Path.Combine(CheckoutRoot,"research/gltf-validation/direct-gpu/models/PointLightIntensityTest");
  byte[] source=File.ReadAllBytes(Path.Combine(owned,"cookie-identity-diagnostic.shader"));
  byte[] receiver=File.ReadAllBytes(Path.Combine(owned,"receiver.gltf"));
  Assert.AreEqual("4a68fd07ab9fa7b8f83e8347f5567fb1d6a77e4869580afb6f103a9c8871e137",Hash(source));
  Assert.AreEqual("d83fbbde05a3071d54ceabf4aac34901a2a79bb52165ec5cd22404dbc5e0543f",Hash(receiver));
  var json=System.Text.Json.Nodes.JsonNode.Parse(receiver);json["materials"][0].AsObject().Remove("extensions");json.AsObject().Remove("extensionsUsed");
  string output=Path.Combine(CheckoutRoot,"Temp/gltf-validation/direct-gpu/point-cookie-shader",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);
  using var cap=new GltfGpuTriangleGateTests.NativeCapture(output,new Vector3(200,0,0),Vector3.Zero,100);
  cap.Camera.EnablePostProcessing=false;cap.Scene.SceneWorld.AmbientLightColor=Color.Black;
  var background=(ScenePointLight)typeof(GltfGpuTriangleGateTests.NativeCapture).GetField("light",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(cap);background.LightColor=Color.Black;
  Shader shader=null;Material material=null;Texture marker=null,white=null,red=null;GltfDocument doc=null;GltfGpuAsset asset=null;GltfGpuInstance instance=null;ScenePointLight point=null;SceneSpotLight spot=null;CameraComponent second=null;
  try {
   using(var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(90)))shader=await Shader.CompileAsync(System.Text.Encoding.UTF8.GetString(source),timeout.Token);
   await MainThread.Wait();Assert.IsTrue(shader is {IsValid:true,IsAnonymous:true});material=Material.Create("cookie_probe_"+Guid.NewGuid().ToString("N"),shader);
   marker=Tex(255,255,255);white=Tex(255,255,255);red=Tex(255,0,0);Assert.AreNotEqual(marker.Index,white.Index);Assert.AreNotEqual(marker.Index,red.Index);
   doc=GltfReader.Read(System.Text.Encoding.UTF8.GetBytes(json.ToJsonString()));asset=GltfGpuAsset.Prepare(doc,GltfManagedModelResource.ComputeShaderPath,_=>material);instance=asset.CreateInstance(cap.Scene.SceneWorld);
   using(cap.Scene.Push()) {second=cap.Scene.CreateObject().Components.Create<CameraComponent>();second.WorldPosition=new Vector3(200,0,0);second.WorldRotation=Rotation.LookAt(new Vector3(-1,0,0),Vector3.Up);second.Orthographic=true;second.OrthographicHeight=100;second.ZNear=1;second.ZFar=1000;second.CustomSize=new Vector2(192,192);second.BackgroundColor=Color.Black;second.ClearFlags=ClearFlags.All;second.EnablePostProcessing=false;}
   await cap.WaitFrames();using(cap.Scene.Push()){cap.Camera.InitializeRendering(false);second.InitializeRendering(false);}
   cap.Camera.SceneCamera.Attributes.Set("ProbeMarkerIndex",marker.Index);second.SceneCamera.Attributes.Set("ProbeMarkerIndex",-1);
   point=new ScenePointLight(cap.Scene.SceneWorld,new Vector3(-100,0,0),1000,Color.White*4){ShadowsEnabled=false};
   var noCookie=await Read(cap.Camera);point.LightCookie=marker;var marked=await Read(cap.Camera);
   Assert.IsTrue(noCookie[0]>.0001 && Math.Abs(marked[0]-2*noCookie[0])<.0002,"Marked point did not bypass cookie sampling and apply marker factor");
   point.Delete();point=null;
   spot=new SceneSpotLight(cap.Scene.SceneWorld,new Vector3(-100,0,0),Color.White*4){Rotation=Rotation.LookAt(new Vector3(1,0,0)),Radius=1000,ConeInner=30,ConeOuter=60,ShadowsEnabled=false};
   var spotPlain=await Read(cap.Camera);spot.LightCookie=marker;var spotMarked=await Read(cap.Camera);var spotOtherView=await Read(second);
   Assert.IsTrue(spotPlain[0]>.0001 && Math.Abs(spotMarked[0]-2*spotPlain[0])<.0002,"Marked spot did not bypass cookie sampling and apply marker factor");
   Assert.IsTrue(spotOtherView[0]>.0001 && spotMarked[0]>1.5*spotOtherView[0],"Second camera did not retain its distinct disabled marker attribute");
   spot.LightCookie=white;var whiteMarkedView=await Read(cap.Camera);var whiteOtherView=await Read(second);
   Assert.IsTrue(whiteMarkedView[0]>.0001 && Error(whiteMarkedView,whiteOtherView)<.0002,"Ordinary white cookie changed under unrelated marker/view");
   spot.LightCookie=red;var redMarkedView=await Read(cap.Camera);var redOtherView=await Read(second);
   Assert.IsTrue(redMarkedView[0]>.0001 && redMarkedView[1]<.0001 && redMarkedView[2]<.0001 && Error(redMarkedView,redOtherView)<.0002,"Ordinary colored cookie changed under unrelated marker/view");
   spot.LightCookie=marker;var markedAgain=await Read(cap.Camera);Assert.IsTrue(Error(markedAgain,spotMarked)<.0002,"Restored marker changed response");
   spot.LightCookie=white;var whiteAgain=await Read(cap.Camera);Assert.IsTrue(Error(whiteAgain,whiteMarkedView)<.0002,"Restored ordinary cookie changed response");
   int markerIndex=marker.Index;spot.Delete();spot=null;var deleted=await Read(cap.Camera);Assert.IsTrue(deleted.Max()<.0001,"Deleted light still contributes");
   Assert.AreEqual(0,instance.CallbackErrors.Length,string.Join("\n",instance.CallbackErrors));Assert.IsTrue(instance.DrawCalls>0);
   Measurements.Add(new {output,shader_sha256=Hash(source),markerIndex,noCookie,marked,spotPlain,spotMarked,spotOtherView,whiteMarkedView,whiteOtherView,redMarkedView,redOtherView,markedAgain,whiteAgain,deleted,scope="Anonymous candidate copies each clustered native light, bypasses cookie for exact private marker and applies diagnostic x2; stock ComputeDirectLightingForLight/BRDF/shadow function, unshadowed test. Two camera attributes, float output. Not public glTF, physical photometry, shadow fidelity or global binding acceptance."});
  }
  finally {await MainThread.Wait();point?.Delete();spot?.Delete();using(cap.Scene.Push()){second?.GameObject.Destroy();cap.Scene.EditorTick(0,0);cap.Scene.ProcessDeletes();}instance?.Dispose();asset?.Dispose();doc?.Dispose();marker?.Dispose();white?.Dispose();red?.Dispose();material?.Destroy();shader?.Destroy();}
  Texture Tex(byte r,byte g,byte b)=>Texture.Create(1,1,ImageFormat.RGBA8888).WithMips(1).WithData(new[]{r,g,b,(byte)255}).WithStaticUsage().Finish();
  async Task<double[]> Read(CameraComponent camera) {
   await cap.WaitFrames();using(cap.Scene.Push()){cap.Scene.PreCameraRender();camera.InitializeRendering(false);}
   using var target=Texture.CreateRenderTarget().WithSize(192,192).WithFormat(ImageFormat.RGBA32323232F).Create("cookie_probe_float_"+Guid.NewGuid().ToString("N"));
   target.Clear(Color.Magenta);await cap.WaitFrames();var clear=new float[192*192*4];Array.Fill(clear,float.NaN);target.GetPixels<float>((0,0,192,192),0,0,clear,ImageFormat.RGBA32323232F);Assert.IsTrue(clear.All(float.IsFinite) && clear.Select((v,i)=>Math.Abs(v-(i%4==1?0:1))).Max()<.000001f);
   using(cap.Scene.Push())Assert.IsTrue(camera.RenderToTexture(target));await cap.WaitFrames();var px=new float[clear.Length];Array.Fill(px,float.NaN);target.GetPixels<float>((0,0,192,192),0,0,px,ImageFormat.RGBA32323232F);Assert.IsTrue(px.All(float.IsFinite) && px.Select((v,i)=>Math.Abs(v-clear[i])).Max()>.01f);
   var result=Enumerable.Range(0,3).Select(c=>new[]{95,96}.Sum(y=>new[]{95,96}.Sum(x=>(double)px[(y*192+x)*4+c]))/4).ToArray();Measurements.Add(new{phase="float_readback",camera=ReferenceEquals(camera,cap.Camera)?"first":"second",rgb=result});return result;
  }
 }
 static string Hash(byte[] bytes)=>Convert.ToHexStringLower(SHA256.HashData(bytes));
 static double Error(double[] a,double[] b)=>a.Zip(b,(x,y)=>Math.Abs(x-y)).Max();
}
