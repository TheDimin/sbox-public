using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox;
using Sandbox.Gltf;
using Sandbox.Gltf.DirectGpu;

namespace Sandbox.Gltf.DirectGpu.TextureBenchmark;

[TestClass]
internal sealed class WarmRenderedAttribution
{
 static string checkout;
 public static List<object> Measurements {get;}=new();
 public static void Configure(string root){checkout=root;Measurements.Clear();}
 [TestMethod]
 public async Task FirstFrameThenResidencyValidation()
 {
  await MainThread.Wait();
  var path=Path.Combine(checkout,"Temp/gltf-reference/glTF-Sample-Assets/Models/ABeautifulGame/glTF-Binary/ABeautifulGame.glb");
  var input=File.ReadAllBytes(path); Assert.AreEqual("bd7133b4b322aae97c589b8839dae8155ad2546acb35ae32a127e722a959d007",Convert.ToHexStringLower(SHA256.HashData(input)));
  using var document=GltfReader.Read(input,null,new HashSet<string>{"KHR_draco_mesh_compression","KHR_texture_basisu"});
  for(int run=0;run<1;run++)
  {
   await MainThread.Wait(); GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
   var before=GltfPreparedTextureCache.Shared.GetStats(); long allocated=GC.GetTotalAllocatedBytes(true);
   var dir=Path.Combine(Path.GetTempPath(),"gltf_selected_bench_"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
   var name="gltf_selected_bench_"+Guid.NewGuid().ToString("N")+".glb"; File.WriteAllBytes(Path.Combine(dir,name),input);
   var output=Path.Combine(checkout,"Temp/gltf-validation/direct-gpu/texture-benchmark/rendered",Guid.NewGuid().ToString("N")); Directory.CreateDirectory(output);
   LocalFileSystem mount=null; Model model=null; ModelRenderer renderer=null;
   using var capture=new GltfGpuTriangleGateTests.NativeCapture(output,new global::Vector3(26,-35,18),new global::Vector3(0,0,4),38);
   capture.Camera.Orthographic=false; capture.Camera.FieldOfView=48;
   var light=new ScenePointLight(capture.Scene.SceneWorld,new global::Vector3(10,-15,40),180,global::Color.White*12);
   try
   {
    mount=new LocalFileSystem(dir); FileSystem.Mounted.Mount(mount);
    var timer=Stopwatch.StartNew(); model=Model.Load(name); double modelMs=timer.Elapsed.TotalMilliseconds;
    Assert.IsTrue(model is {IsValid:true,IsError:false} && model.GltfResource is not null && model.MeshCount==49);
    var materials=model.Materials; double materialsMs=timer.Elapsed.TotalMilliseconds-modelMs; Assert.AreEqual(15,materials.Length);
    var textures=new HashSet<Texture>(ReferenceEqualityComparer.Instance);
    for(int i=0;i<15;i++)
    {
     var material=document.ReadMaterial(i);
     foreach(var role in new[]{(GltfMaterialTextureRole.BaseColor,material.BaseColorTexture!=null),(GltfMaterialTextureRole.Normal,material.NormalTexture!=null),(GltfMaterialTextureRole.MetallicRoughness,material.MetallicRoughnessTexture!=null),(GltfMaterialTextureRole.Occlusion,material.OcclusionTexture!=null)})
     {
      if(!role.Item2)continue; var texture=materials[i].GetTexture("g_tGltf"+role.Item1+"Texture");
      Assert.IsTrue(texture is {IsValid:true,IsError:false} && texture.Mips==12); textures.Add(texture);
     }
    }
    Assert.AreEqual(33,textures.Count,"Expected all selected distinct source texture resources");
    using(capture.Scene.Push()){renderer=capture.Scene.CreateObject().Components.Create<ModelRenderer>(false);renderer.Model=model;renderer.Enabled=true;capture.Scene.EditorTick(0,0);}
    var first=await capture.Take("first_submitted_frame"); await MainThread.Wait();
    double firstFrameMs=timer.Elapsed.TotalMilliseconds;
    Assert.IsTrue(first.Coverage>100); Assert.IsTrue(renderer.GltfInstance is not null && renderer.GltfInstance.DrawCalls>0 && renderer.GltfInstance.CallbackErrors.Length==0);
    foreach(var texture in textures)texture.MarkUsed(2048);
    var pending=new HashSet<Texture>(textures,ReferenceEqualityComparer.Instance); var residency=Stopwatch.StartNew(); int checks=0;
    while(pending.Count>0 && residency.Elapsed<TimeSpan.FromSeconds(90))
    {
     await capture.WaitFrames(); await MainThread.Wait();
     foreach(var texture in pending.ToArray()) { texture.MarkUsed(2048); checks++; if(texture.GetPixels(0).Length==2048*2048)pending.Remove(texture); }
    }
    double residentMs=timer.Elapsed.TotalMilliseconds-firstFrameMs;
    Assert.AreEqual(0,pending.Count,"All selected texture base mips must reach full residency");
    var frame=await capture.Take("first_full_resident_frame"); await MainThread.Wait();
    Assert.IsTrue(frame.Coverage>100); Assert.IsTrue(renderer.GltfInstance is not null && renderer.GltfInstance.DrawCalls>0 && renderer.GltfInstance.CallbackErrors.Length==0);
    var after=GltfPreparedTextureCache.Shared.GetStats();
    int changed=first.Pixels.Zip(frame.Pixels,(a,b)=>Math.Abs(a.r-b.r)>.001f||Math.Abs(a.g-b.g)>.001f||Math.Abs(a.b-b.b)>.001f).Count(x=>x);
    Measurements.Add(new{run,cache_hits=after.Hits-before.Hits,cache_misses=after.Misses-before.Misses,cache_bypassed=after.Bypassed-before.Bypassed,cache_bytes_written=after.Bytes-before.Bytes,model_load_ms=modelMs,materials_ms=materialsMs,first_submitted_frame_ms=firstFrameMs,first_frame_sha256=first.Sha256,residency_readback_ms=residentMs,validated_frame_ms=timer.Elapsed.TotalMilliseconds,validated_frame_sha256=frame.Sha256,first_vs_resident_changed_pixels=changed,residency_checks=checks,allocated_bytes=GC.GetTotalAllocatedBytes(true)-allocated,working_set_bytes=Process.GetCurrentProcess().WorkingSet64,scope="First submitted frame is captured before explicit full-mip GPU readbacks; a later captured frame follows full residency. Matching frames are not independent appearance approval. Setup and cleanup excluded."});
   }
   finally
   {
    await MainThread.Wait(); if(light.IsValid())light.Delete();
    try { if(renderer!=null){using var scope=capture.Scene.Push();renderer.GameObject.Destroy();capture.Scene.EditorTick(0,0);capture.Scene.ProcessDeletes();} model?.Destroy(); }
    finally { try { if(mount!=null)FileSystem.Mounted.UnMount(mount); } finally { mount?.Dispose(); Directory.Delete(dir,true); } }
   }
  }
 }
}
