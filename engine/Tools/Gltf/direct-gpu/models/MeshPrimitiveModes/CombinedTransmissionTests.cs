using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Sandbox.Gltf.DirectGpu.MeshPrimitiveModesValidation.MeshPrimitiveModesReference;
namespace Sandbox.Gltf.DirectGpu.MeshPrimitiveModesValidation;
[TestClass]
internal sealed class CombinedTransmissionTests
{
 static string checkout;public static List<object> Measurements{get;}=new();public static void Configure(string root){checkout=root;Measurements.Clear();}
 [TestMethod]
 public async Task SevenUvTransmissionTriangleUnaffectedAndLineUnlit()
 {
  await MainThread.Wait();var dir=Path.Combine(checkout,"research/gltf-validation/direct-gpu/models");var source=File.ReadAllBytes(Path.Combine(dir,"MandarinOrange/gltf_diffuse_transmission_candidate.shader"));var combined=File.ReadAllBytes(Path.Combine(dir,"MeshPrimitiveModes/combined_transmission_candidate.shader"));
  Require(Hash(source)=="dfbc59486512cab43579aa7e6a12ed14cf10d2667d50ab9e54346e2a655d24e9" && Hash(combined)=="f6d955b7927642f37c639bb49ebaee7831597f52fd430e080cc2666628647d48","Shader source changed.");
  var output=Path.Combine(checkout,"Temp/gltf-validation/direct-gpu/MeshPrimitiveModes/combined-transmission",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);Shader originalShader=null,candidateShader=null;Material original=null,candidate=null,current=null;SceneCustomObject obj=null;var frames=new List<object>();var errors=new ConcurrentQueue<string>();int draws=0;
  using var capture=new GltfGpuTriangleGateTests.NativeCapture(output,new Vector3(200,0,0),Vector3.Zero,140);
  capture.Camera.AutoExposure.Enabled=false;capture.Camera.AutoExposure.MinimumExposure=1;capture.Camera.AutoExposure.MaximumExposure=1;capture.Camera.AutoExposure.Compensation=0;
  try
  {
   async Task<Shader> Compile(byte[] b){using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(90));var shader=await Shader.CompileAsync(Encoding.UTF8.GetString(b),timeout.Token);await MainThread.Wait();Require(shader is {IsValid:true,IsAnonymous:true},"Shader compile failed.");return shader;}
   originalShader=await Compile(source);candidateShader=await Compile(combined);original=Material.Create("dt_original_"+Guid.NewGuid().ToString("N"),originalShader);candidate=Material.Create("dt_combined_"+Guid.NewGuid().ToString("N"),candidateShader);
   foreach(var m in new[]{original,candidate}){m.Set("g_vGltfBaseColor",new Vector4(1,1,1,1));m.Set("g_flGltfMetallicFactor",0f);m.Set("g_flGltfRoughnessFactor",1f);m.Set("g_flGltfDiffuseTransmissionFactor",1f);m.Set("g_vGltfDiffuseTransmissionColorFactor",new Vector4(1,1,1,0));}
   Vertex V(float y,float z)=>new(new Vector3(0,y,z)){Normal=Vector3.Forward,Tangent=new Vector4(0,1,0,1),Color=Color32.White};var line=new[]{V(-30,-25),V(-30,25)};var tri=new[]{V(15,-25),V(55,-25),V(35,25)};var lineAttrs=new RenderAttributes();var triAttrs=new RenderAttributes();current=original;
   obj=new SceneCustomObject(capture.Scene.SceneWorld);obj.Flags.IsOpaque=true;obj.Bounds=BBox.FromPoints(line.Concat(tri).Select(x=>x.Position));obj.RenderOverride=_=>{try{lineAttrs.Clear();current.Attributes.MergeTo(lineAttrs);Graphics.Attributes.MergeTo(lineAttrs);lineAttrs.Set("GltfPointLineNoNormal",1f);Graphics.Draw(line.AsSpan(),line.Length,current,lineAttrs,Graphics.PrimitiveType.Lines);triAttrs.Clear();current.Attributes.MergeTo(triAttrs);Graphics.Attributes.MergeTo(triAttrs);triAttrs.Set("GltfPointLineNoNormal",0f);Graphics.Draw(tri.AsSpan(),tri.Length,current,triAttrs,Graphics.PrimitiveType.Triangles);Interlocked.Increment(ref draws);}catch(Exception e){errors.Enqueue(e.ToString());}};
   var baseline=await Take("original_transmission_triangle");current=candidate;var actual=await Take("combined_transmission_triangle");Require(draws>0 && errors.IsEmpty,"Draw callback failed: "+string.Join("\n",errors));
   var referenceCoverage=Coverage(baseline,96,192);var error=MaxDiff(baseline,actual,96,192);Measurements.Add(new{referenceCoverage,maxTriangleRgbError=error,frames});Require(referenceCoverage>64 && error<=1f/255f,"Combined triangle differs from original DT shader.");
   candidate.Set("g_flGltfDiffuseTransmissionFactor",0f);var wrong=await Take("wrong_zero_transmission");var changed=Changed(actual,wrong,96,192);Measurements.Add(new{wrongFactorChangedPixels=changed});Require(changed>64,"The triangle light setup does not distinguish transmission factor.");candidate.Set("g_flGltfDiffuseTransmissionFactor",1f);
   var light=(ScenePointLight)typeof(GltfGpuTriangleGateTests.NativeCapture).GetField("light",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(capture);var initial=light.LightColor;var on=await Take("combined_light_on");light.LightColor=Color.Black;var off=await Take("combined_light_off");light.LightColor=initial;Require(Coverage(on,0,96)>0 && Coverage(off,0,96)==Coverage(on,0,96) && Coverage(on,96,192)>64 && Coverage(off,96,192)==0,"No-normal line or transmission triangle failed light-separation control.");
   obj.RenderOverride=null;obj.Delete();obj=null;using(capture.Scene.Push())capture.Scene.ProcessDeletes();Require((await Take("empty_after_delete")).Coverage==0,"Deletion failed.");Measurements.Add(new{passed=true,frames,scope="Anonymous source+combined seven-UV shaders, same literal triangle and constant DT factor, direct flag-controlled line. Does not exercise source extension adapter, document textures, public route or texture UV7."});
  }
  finally{await MainThread.Wait();if(obj.IsValid()){obj.RenderOverride=null;obj.Delete();}original?.Destroy();candidate?.Destroy();originalShader?.Destroy();candidateShader?.Destroy();Measurements.Add(new{frames,output});}
  async Task<GltfGpuTriangleGateTests.NativeCapture.Frame> Take(string label){var f=await capture.Take(label);frames.Add(new{label,f.Path,f.Sha256,f.Coverage});return f;}
 }
 static int Coverage(GltfGpuTriangleGateTests.NativeCapture.Frame f,int x0,int x1){int n=0;for(int y=0;y<192;y++)for(int x=x0;x<x1;x++){var p=f.Pixels[y*192+x];if(p.r>.03f||p.g>.03f||p.b>.03f)n++;}return n;}
 static int Changed(GltfGpuTriangleGateTests.NativeCapture.Frame a,GltfGpuTriangleGateTests.NativeCapture.Frame b,int x0,int x1){int n=0;for(int y=0;y<192;y++)for(int x=x0;x<x1;x++){var p=a.Pixels[y*192+x];var q=b.Pixels[y*192+x];if(Math.Abs(p.r-q.r)+Math.Abs(p.g-q.g)+Math.Abs(p.b-q.b)>.02f)n++;}return n;}
 static float MaxDiff(GltfGpuTriangleGateTests.NativeCapture.Frame a,GltfGpuTriangleGateTests.NativeCapture.Frame b,int x0,int x1){float n=0;for(int y=0;y<192;y++)for(int x=x0;x<x1;x++){var p=a.Pixels[y*192+x];var q=b.Pixels[y*192+x];n=Math.Max(n,Math.Max(Math.Abs(p.r-q.r),Math.Max(Math.Abs(p.g-q.g),Math.Abs(p.b-q.b))));}return n;}
}
