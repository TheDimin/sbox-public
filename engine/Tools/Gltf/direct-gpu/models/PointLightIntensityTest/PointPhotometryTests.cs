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

// A controlled native Lambert receiver, not the sample's material or public API.
[TestClass]
internal sealed class PointPhotometryTests
{
 public static string CheckoutRoot { get; set; }
 public static List<object> Measurements { get; } = new();
 [TestMethod]
 public async Task NativeFloatDistanceColorRangeAndDeletion()
 {
  await MainThread.Wait();
  string owned=Path.Combine(CheckoutRoot,"research/gltf-validation/direct-gpu/models/PointLightIntensityTest");
  string output=Path.Combine(CheckoutRoot,"Temp/gltf-validation/direct-gpu/point-photometry",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);
  byte[] shaderSource=File.ReadAllBytes(Path.Combine(owned,"native-lambert-diagnostic.shader"));
  byte[] receiver=File.ReadAllBytes(Path.Combine(owned,"receiver.gltf"));
  Assert.AreEqual("f5dd82885f64329726906d9124c4b8bc940a830cd2f43482d0862bfa19fee4f2",Hash(shaderSource));
  Assert.AreEqual("d83fbbde05a3071d54ceabf4aac34901a2a79bb52165ec5cd22404dbc5e0543f",Hash(receiver));
  var json=System.Text.Json.Nodes.JsonNode.Parse(receiver);
  json["materials"][0].AsObject().Remove("extensions");json.AsObject().Remove("extensionsUsed");
  using var cap=new GltfGpuTriangleGateTests.NativeCapture(output,new global::Vector3(200,0,0),global::Vector3.Zero,100);
  cap.Camera.EnablePostProcessing=false;cap.Scene.SceneWorld.AmbientLightColor=Color.Black;
  var light=(ScenePointLight)typeof(GltfGpuTriangleGateTests.NativeCapture).GetField("light",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(cap);
  Shader shader=null;Material material=null;GltfDocument doc=null;GltfGpuAsset asset=null;GltfGpuInstance instance=null;
  try
  {
   using(var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(90)))shader=await Shader.CompileAsync(System.Text.Encoding.UTF8.GetString(shaderSource),timeout.Token);
   await MainThread.Wait();Assert.IsTrue(shader is {IsValid:true,IsAnonymous:true});
   material=Material.Create("point_lambert_"+Guid.NewGuid().ToString("N"),shader);
   doc=GltfReader.Read(System.Text.Encoding.UTF8.GetBytes(json.ToJsonString()));
   asset=GltfGpuAsset.Prepare(doc,GltfManagedModelResource.ComputeShaderPath,_=>material);instance=asset.CreateInstance(cap.Scene.SceneWorld);
   light.ShadowsEnabled=false;light.ConstantAttenuation=0;light.LinearAttenuation=0;light.QuadraticAttenuation=6.4516f;light.Radius=1000;
   var zero=await Sample("zero",1,Color.Black);
   var half=await Sample("half_m",.5,Color.White);var one=await Sample("one_m",1,Color.White);var two=await Sample("two_m",2,Color.White);
   // A larger existing quadratic control avoids saturating at these distances;
   // the corresponding intensity compensation still needs SI calibration.
   light.QuadraticAttenuation=1000;
   var tunedHalf=await Sample("tuned_half_m",.5,Color.White*40);var tunedOne=await Sample("tuned_one_m",1,Color.White*40);var tunedTwo=await Sample("tuned_two_m",2,Color.White*40);
   var hdr=await Sample("hdr_shader_output",1,Color.White*4000);var hdrHalf=await Sample("hdr_half_intensity",1,Color.White*2000);
   light.QuadraticAttenuation=6.4516f;
   var quarter=await Sample("quarter_intensity",1,Color.White*.25f);
   var red=await Sample("red",1,new Color(1,0,0));var green=await Sample("green",1,new Color(0,1,0));var blue=await Sample("blue",1,new Color(0,0,1));var gray=await Sample("gray",1,new Color(.5f,.5f,.5f));
   light.LightColor=new Color(1,0,0);
   ScenePointLight extraGreen=null,extraBlue=null;double[] triple;
   try {
    extraGreen=new ScenePointLight(cap.Scene.SceneWorld,light.Position,1000,new Color(0,1,0)){ShadowsEnabled=false,ConstantAttenuation=0,LinearAttenuation=0,QuadraticAttenuation=6.4516f};
    extraBlue=new ScenePointLight(cap.Scene.SceneWorld,light.Position,1000,new Color(0,0,1)){ShadowsEnabled=false,ConstantAttenuation=0,LinearAttenuation=0,QuadraticAttenuation=6.4516f};
    triple=await Read();
   } finally {extraGreen?.Delete();extraBlue?.Delete();}
   light.Radius=(float)(1.125/0.0254);
   var near=await Sample("finite_half_m",.5,Color.White);var inside=await Sample("finite_one_m",1,Color.White);var edge=await Sample("finite_1_125_m",1.125,Color.White);var beyond=await Sample("finite_1_25_m",1.25,Color.White);
   light.Delete();var deleted=await Read();
   var data=new {output,zero,half,one,two,tunedHalf,tunedOne,tunedTwo,hdr,hdrHalf,quarter,red,green,blue,gray,triple,near,inside,edge,beyond,deleted,ratio_half_to_one=(half[0]-zero[0])/(one[0]-zero[0]),ratio_one_to_two=(one[0]-zero[0])/(two[0]-zero[0]),tuned_ratio_half_to_one=(tunedHalf[0]-zero[0])/(tunedOne[0]-zero[0]),tuned_ratio_one_to_two=(tunedOne[0]-zero[0])/(tunedTwo[0]-zero[0]),scope="Actual float native Lambert receiver with postprocessing disabled; candidate native units, not public or calibrated glTF photometry.",shader_sha256=Hash(shaderSource)};
   File.WriteAllText(Path.Combine(output,"observations.json"),System.Text.Json.JsonSerializer.Serialize(data));Measurements.Add(data);
   Assert.AreEqual(0,instance.CallbackErrors.Length,string.Join("\n",instance.CallbackErrors));Assert.IsTrue(instance.DrawCalls>0);
   Assert.IsTrue(one[0]-zero[0]>.0001,"Positive light response absent");
   Assert.IsTrue(hdr[0]-zero[0]>1,"Float shader output did not preserve HDR above one");
   Assert.IsTrue(Math.Abs((hdrHalf[0]-zero[0])-.5*(hdr[0]-zero[0]))<.0001,"HDR response must scale linearly");
   for(int c=0;c<3;c++) {
    Assert.IsTrue(Math.Abs((quarter[c]-zero[c])-.25*(one[c]-zero[c]))<.0001,"Intensity must be linear");
    Assert.IsTrue(Math.Abs((gray[c]-zero[c])-.5*(one[c]-zero[c]))<.0001,"Gray color must scale linearly");
    Assert.IsTrue(Math.Abs((red[c]+green[c]+blue[c]-3*zero[c])-(one[c]-zero[c]))<.0001,"Separate RGB lights must add to white");
    Assert.IsTrue(Math.Abs(triple[c]-one[c])<.0001,"Three co-located RGB lights must render like white");
    Assert.IsTrue(Math.Abs(deleted[c]-zero[c])<.0001,"Deleted light must match zero");
   }
   Assert.IsTrue(near[0]>inside[0] && inside[0]>edge[0] && Math.Abs(beyond[0]-zero[0])<.0001,"Finite-radius response/cutoff control failed");
  }
  finally {await MainThread.Wait();instance?.Dispose();asset?.Dispose();doc?.Dispose();material?.Destroy();shader?.Destroy();}
  async Task<double[]> Sample(string label,double meters,Color color) {
   light.Position=new global::Vector3((float)(-meters/0.0254),0,0);light.LightColor=color;
   var v=await Read();File.WriteAllText(Path.Combine(output,label+".json"),System.Text.Json.JsonSerializer.Serialize(v));return v;
  }
  async Task<double[]> Read() {
   await cap.WaitFrames();using(cap.Scene.Push()){cap.Scene.PreCameraRender();cap.Camera.InitializeRendering(false);}
   using var target=Texture.CreateRenderTarget().WithSize(192,192).WithFormat(ImageFormat.RGBA32323232F).Create("point_hdr_"+Guid.NewGuid().ToString("N"));
   // Texture.Clear uses native packed color conversion. Endpoint colors are
   // exact there; test the whole float readback and wait for the queued clear.
   target.Clear(Color.Magenta);await cap.WaitFrames();var sentinel=new float[192*192*4];Array.Fill(sentinel,float.NaN);target.GetPixels<float>((0,0,192,192),0,0,sentinel,ImageFormat.RGBA32323232F);
   Measurements.Add(new {phase="float_clear",first=sentinel.Take(4).Select(x=>x.ToString("R",System.Globalization.CultureInfo.InvariantCulture)).ToArray(),minimum=sentinel.Min().ToString("R",System.Globalization.CultureInfo.InvariantCulture),maximum=sentinel.Max().ToString("R",System.Globalization.CultureInfo.InvariantCulture)});
   Assert.IsTrue(sentinel.All(float.IsFinite) && sentinel.Select((v,i)=>Math.Abs(v-(i%4==1?0f:1f))).Max()<.000001f,"Float clear control failed");
   using(cap.Scene.Push())Assert.IsTrue(cap.Camera.RenderToTexture(target));await cap.WaitFrames();
   var pixels=new float[sentinel.Length];Array.Fill(pixels,float.NaN);target.GetPixels<float>((0,0,192,192),0,0,pixels,ImageFormat.RGBA32323232F);Assert.IsTrue(pixels.All(float.IsFinite));
   Assert.IsTrue(pixels.Select((v,i)=>Math.Abs(v-sentinel[i])).Max()>.01f,"Camera render did not replace clear sentinel");
   return Enumerable.Range(0,3).Select(c=>new[]{95,96}.Sum(y=>new[]{95,96}.Sum(x=>(double)pixels[(y*192+x)*4+c]))/4).ToArray();
  }
 }
 static string Hash(byte[] bytes)=>Convert.ToHexStringLower(SHA256.HashData(bytes));
}
