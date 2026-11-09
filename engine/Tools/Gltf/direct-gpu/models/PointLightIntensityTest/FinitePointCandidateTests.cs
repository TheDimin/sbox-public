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

// Isolated model-local candidate, no public or extension-capability claim.
[TestClass]
internal sealed class FinitePointCandidateTests
{
 public static string CheckoutRoot {get;set;}
 public static List<object> Measurements {get;}=new();
 [TestMethod]
 public async Task FiniteCandelaOnUnitLambertReceiver()
 {
  await MainThread.Wait();string dir=Path.Combine(CheckoutRoot,"research/gltf-validation/direct-gpu/models/PointLightIntensityTest");
  byte[] shaderSource=File.ReadAllBytes(Path.Combine(dir,"finite-point-candidate.shader"));byte[] plane=File.ReadAllBytes(Path.Combine(dir,"receiver.gltf"));
  Assert.AreEqual("e16522a16b0ed0b54ec9bbc41ef34006c50a325bc137bc0a0b8c3656adb707ad",Hash(shaderSource));Assert.AreEqual("d83fbbde05a3071d54ceabf4aac34901a2a79bb52165ec5cd22404dbc5e0543f",Hash(plane));
  var json=System.Text.Json.Nodes.JsonNode.Parse(plane);json["materials"][0].AsObject().Remove("extensions");json.AsObject().Remove("extensionsUsed");
  string output=Path.Combine(CheckoutRoot,"Temp/gltf-validation/direct-gpu/point-finite-candidate",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);
  using var cap=new GltfGpuTriangleGateTests.NativeCapture(output,new Vector3(200,0,0),Vector3.Zero,100);cap.Camera.EnablePostProcessing=false;cap.Scene.SceneWorld.AmbientLightColor=Color.Black;
  var background=(ScenePointLight)typeof(GltfGpuTriangleGateTests.NativeCapture).GetField("light",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(cap);background.LightColor=Color.Black;
  Shader shader=null;Material material=null;Texture marker=null;GltfDocument doc=null;GltfGpuAsset asset=null;GltfGpuInstance instance=null;ScenePointLight light=null,ordinary=null;
  try {
   using(var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(90)))shader=await Shader.CompileAsync(System.Text.Encoding.UTF8.GetString(shaderSource),timeout.Token);
   await MainThread.Wait();Assert.IsTrue(shader is {IsValid:true,IsAnonymous:true});material=Material.Create("finite_point_"+Guid.NewGuid().ToString("N"),shader);
   marker=Texture.Create(1,1,ImageFormat.RGBA8888).WithMips(1).WithData(new byte[]{255,255,255,255}).WithStaticUsage().Finish();
   doc=GltfReader.Read(System.Text.Encoding.UTF8.GetBytes(json.ToJsonString()));asset=GltfGpuAsset.Prepare(doc,GltfManagedModelResource.ComputeShaderPath,_=>material);instance=asset.CreateInstance(cap.Scene.SceneWorld);
   await cap.WaitFrames();using(cap.Scene.Push())cap.Camera.InitializeRendering(false);
   cap.Camera.SceneCamera.Attributes.Set("ProbeMarkerIndex",marker.Index);
   light=new ScenePointLight(cap.Scene.SceneWorld,new Vector3(-1/.0254f,0,0),4/.0254f,Color.White){ShadowsEnabled=false,LightCookie=marker};
   // Stock direct-light term at unit response is compared with Lambert's
   // analytical 1/pi, rather than treating native units as candela by fiat.
   cap.Camera.SceneCamera.Attributes.Set("ProbeNativePerCandela",1f);
   var nativeUnit=await Sample("unit_native_response",1,Color.White);
   double expectedLux=.99609375, nativePerLux=nativeUnit[0]/expectedLux;
   Assert.IsTrue(Math.Abs(nativePerLux-1)<.005,"Native normalized Lambert measurement differs: "+nativePerLux);
   float lambertScale=(float)(1/Math.PI);cap.Camera.SceneCamera.Attributes.Set("ProbeNativePerCandela",lambertScale);
   var half=await Sample("half_m",.5,Color.White);var one=await Sample("one_m",1,Color.White);var two=await Sample("two_m",2,Color.White);var edge=await Sample("edge",4,Color.White);var beyond=await Sample("beyond",5,Color.White);
   var doubleIntensity=await Sample("double",1,Color.White*2);var gray=await Sample("gray",1,new Color(.5f,.5f,.5f));var red=await Sample("red",1,new Color(1,0,0));var zero=await Sample("zero",1,Color.Black);
   ordinary=new ScenePointLight(cap.Scene.SceneWorld,new Vector3(-1/.0254f,0,0),4/.0254f,Color.White){ShadowsEnabled=false};
   var ordinaryAlone=await Read("ordinary_with_marker_enabled");
   var both=await Sample("ordinary_plus_marked",1,Color.White);
   light.Delete();light=null;
   cap.Camera.SceneCamera.Attributes.Set("ProbeMarkerIndex",-1);
   var ordinaryMarkerDisabled=await Read("ordinary_with_marker_disabled");
   ordinary.Delete();ordinary=null;var deleted=await Read("deleted");
   Measurements.Add(new{output,nativeUnit,nativePerLux,lambertScale,half,one,two,edge,beyond,doubleIntensity,gray,red,zero,ordinaryAlone,both,ordinaryMarkerDisabled,deleted,scope="Anonymous finite point candidate on unit Lambert diagnostic receiver with postprocessing disabled. Fixed 1/pi reference and ordinary-light controls. Production materials, shadows, public lifecycle and unbounded range remain separate gates."});
   Close(half[0],1.2729286952369363,.01,"0.5m 1cd Lambert");Close(one[0],.31706648819088523,.005,"1m 1cd Lambert");Close(two[0],.07460387957432595,.002,"2m 1cd Lambert");
   Assert.IsTrue(edge.Max()<.0001 && beyond.Max()<.0001 && zero.Max()<.0001 && deleted.Max()<.0001,"Range/zero/deletion control failed");
   Assert.IsTrue(ordinaryAlone.Min()>.0001,"Ordinary light control must produce a signal");
   for(int c=0;c<3;c++){
    Close(doubleIntensity[c],2*one[c],.001,"Intensity linearity");
    Close(gray[c],.5*one[c],.001,"Gray linearity");
    Close(ordinaryAlone[c],ordinaryMarkerDisabled[c],.001,"Ordinary light response");
    Close(both[c],ordinaryAlone[c]+one[c],.002,"Mixed marked and ordinary lights");
   }
   Assert.IsTrue(red[0]>.1 && red[1]<.0001 && red[2]<.0001,"Linear color filter failed");
   Assert.AreEqual(0,instance.CallbackErrors.Length,string.Join("\n",instance.CallbackErrors));Assert.IsTrue(instance.DrawCalls>0);
  } finally {await MainThread.Wait();light?.Delete();ordinary?.Delete();instance?.Dispose();asset?.Dispose();doc?.Dispose();marker?.Dispose();material?.Destroy();shader?.Destroy();}
  async Task<double[]> Sample(string label,double distance,Color color){light.Position=new Vector3((float)(-distance/.0254),0,0);light.LightColor=color;return await Read(label);}
  async Task<double[]> Read(string label){
   await cap.WaitFrames();using(cap.Scene.Push()){cap.Scene.PreCameraRender();cap.Camera.InitializeRendering(false);}
   using var target=Texture.CreateRenderTarget().WithSize(192,192).WithFormat(ImageFormat.RGBA32323232F).Create("finite_point_float_"+Guid.NewGuid().ToString("N"));target.Clear(Color.Magenta);await cap.WaitFrames();
   var clear=new float[192*192*4];Array.Fill(clear,float.NaN);target.GetPixels<float>((0,0,192,192),0,0,clear,ImageFormat.RGBA32323232F);Assert.IsTrue(clear.All(float.IsFinite)&&clear.Select((v,i)=>Math.Abs(v-(i%4==1?0:1))).Max()<.000001f);
   using(cap.Scene.Push())Assert.IsTrue(cap.Camera.RenderToTexture(target));await cap.WaitFrames();var px=new float[clear.Length];Array.Fill(px,float.NaN);target.GetPixels<float>((0,0,192,192),0,0,px,ImageFormat.RGBA32323232F);Assert.IsTrue(px.All(float.IsFinite)&&px.Select((v,i)=>Math.Abs(v-clear[i])).Max()>.01f);
   var rgb=Enumerable.Range(0,3).Select(c=>(
    (double)px[(95*192+95)*4+c]+px[(95*192+96)*4+c]+px[(96*192+95)*4+c]+px[(96*192+96)*4+c])/4).ToArray();
   Measurements.Add(new{label,rgb});return rgb;
  }
 }
 static string Hash(byte[] b)=>Convert.ToHexStringLower(SHA256.HashData(b));
 static void Close(double actual,double expected,double tolerance,string label)=>Assert.IsTrue(Math.Abs(actual-expected)<tolerance,label+" observed "+actual+" expected "+expected);
}
