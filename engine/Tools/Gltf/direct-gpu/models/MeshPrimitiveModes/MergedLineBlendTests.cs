using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Sandbox.Gltf.DirectGpu.MeshPrimitiveModesValidation.MeshPrimitiveModesReference;
namespace Sandbox.Gltf.DirectGpu.MeshPrimitiveModesValidation;
[TestClass]
internal sealed class MergedLineBlendTests
{
 static string checkout; public static List<object> Measurements { get; } = new();
 public static void Configure(string root) { checkout=root; Measurements.Clear(); }
 [TestMethod]
 public async Task FlaggedLineBlendOpacityAndRestore()
 {
  await MainThread.Wait(); var bytes=File.ReadAllBytes(Path.Combine(checkout,"engine/Tools/Gltf/direct-gpu/models/MeshPrimitiveModes/combined_alpha_pointline_candidate.shader"));
  Require(Hash(bytes)=="bf6c5e6f16c801a9892a485a53bc4c6b6bef85d4a86c98232af8139187630ed9","Merged shader changed.");
  var output=Path.Combine(checkout,"Temp/gltf-validation/direct-gpu/MeshPrimitiveModes/merged-line-blend",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);
  using var capture=new GltfGpuTriangleGateTests.NativeCapture(output,new Vector3(200,0,0),Vector3.Zero,140);
  capture.Camera.AutoExposure.Enabled=false;capture.Camera.AutoExposure.MinimumExposure=1;capture.Camera.AutoExposure.MaximumExposure=1;capture.Camera.AutoExposure.Compensation=0;
  Shader shader=null;Material material=null;SceneCustomObject obj=null;var errors=new ConcurrentQueue<string>();var attrs=new RenderAttributes();int calls=0;
  try
  {
   using(var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(90)))shader=await Shader.CompileAsync(Encoding.UTF8.GetString(bytes),timeout.Token);await MainThread.Wait();Require(shader is {IsValid:true,IsAnonymous:true},"Compile failed.");
   material=Material.Create("merged_line_blend_"+Guid.NewGuid().ToString("N"),shader);material.SetFeature("F_TRANSLUCENT",1);material.Set("g_vGltfBaseColor",new Vector4(1,1,1,1));
   var verts=new[]{new Vertex(new Vector3(0,0,-25)){Normal=Vector3.Forward,Tangent=new Vector4(0,1,0,1),Color=Color32.White},new Vertex(new Vector3(0,0,25)){Normal=Vector3.Forward,Tangent=new Vector4(0,1,0,1),Color=Color32.White}};
   obj=new SceneCustomObject(capture.Scene.SceneWorld);obj.Flags.IsTranslucent=true;obj.Flags.IsOpaque=false;obj.Bounds=new BBox(new Vector3(-1,-1,-26),new Vector3(1,1,26));obj.RenderOverride=_=>{try{attrs.Clear();material.Attributes.MergeTo(attrs);Graphics.Attributes.MergeTo(attrs);attrs.Set("GltfPointLineNoNormal",1f);Graphics.Draw(verts.AsSpan(),verts.Length,material,attrs,Graphics.PrimitiveType.Lines);Interlocked.Increment(ref calls);}catch(Exception e){errors.Enqueue(e.ToString());}};
   async Task<(double sum,string path,string hash)> Take(string name,float alpha){material.Set("g_vGltfBaseColor",new Vector4(1,1,1,alpha));var f=await capture.Take(name);return(f.Pixels.Sum(p=>(double)p.r+p.g+p.b),f.Path,f.Sha256);}
   var full=await Take("full",1);var quarter=await Take("quarter",.25f);var zero=await Take("zero",0);var restore=await Take("restore",1);
   Measurements.Add(new{full,quarter,zero,restore,calls,scope="Anonymous flagged line, translucent feature, factor alpha and restoration; no public routing."});
   Require(calls>0&&errors.IsEmpty,"Callback failed: "+string.Join("\n",errors));Require(full.sum>1 && quarter.sum>0 && quarter.sum<full.sum*.8 && zero.sum<full.sum*.05 && Math.Abs(restore.sum-full.sum)<full.sum*.05,"Flagged translucent line failed alpha controls.");
   material.SetFeature("F_TRANSLUCENT",0);material.SetFeature("F_ALPHA_TEST",1);material.Set("g_flGltfAlphaCutoff",.5f);obj.Flags.IsTranslucent=false;obj.Flags.IsOpaque=true;
   var maskBelow=await Take("mask_below_cutoff",.4f);var maskAbove=await Take("mask_above_cutoff",.6f);Measurements.Add(new{maskBelow,maskAbove});Require(maskAbove.sum>1 && maskBelow.sum<maskAbove.sum*.05,"Flagged line mask cutoff failed.");
   obj.RenderOverride=null;obj.Delete();obj=null;using(capture.Scene.Push())capture.Scene.ProcessDeletes();Require((await capture.Take("empty_after")).Coverage==0,"Delete failed.");
  }
  finally{await MainThread.Wait();if(obj.IsValid()){obj.RenderOverride=null;obj.Delete();}material?.Destroy();shader?.Destroy();}
 }
}
