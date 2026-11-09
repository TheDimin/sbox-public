using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox;
using Sandbox.Engine;
using Sandbox.Internal;
using Sandbox.Gltf;

namespace Sandbox.Gltf.DirectGpu.TextureBenchmark;

// Diagnostic only: exercises supported resource operations against the loaded
// producer, never replaces the production cache or modifies engine sources.
[TestClass]
internal sealed class BatchAcquisitionExperiment
{
 static string checkout;
 public static List<object> Measurements { get; }=new();
 public static void Configure(string root) { checkout=root; Measurements.Clear(); }
 [TestMethod]
 public void FreshAliasesSerialVersusBatchRegistration()
 {
  var path=Path.Combine(checkout,"Temp/gltf-reference/glTF-Sample-Assets/Models/ABeautifulGame/glTF-Binary/ABeautifulGame.glb");
  byte[] input=File.ReadAllBytes(path);
  Assert.AreEqual("bd7133b4b322aae97c589b8839dae8155ad2546acb35ae32a127e722a959d007",Convert.ToHexStringLower(SHA256.HashData(input)));
  using var doc=GltfReader.Read(input,null,new HashSet<string>{"KHR_draco_mesh_compression","KHR_texture_basisu"});
  var prepared=GltfStockMaterialAdapter.PrepareSource(doc,doc.ReadMaterial(0));
  var payloads=prepared.Resources.Where(x=>x.Key!=GltfMaterialTextureRole.Occlusion).OrderBy(x=>x.Key).Select(x=>x.Value.Resource).ToArray();
  Assert.AreEqual(3,payloads.Length);
  for(int iteration=0;iteration<3;iteration++) foreach(bool batch in new[]{false,true})
  {
   string dir=Path.Combine(Path.GetTempPath(),"gltf_batch_"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
   var redirects=new List<RedirectFileSystem>(); var textures=new List<Texture>(); var watch=Stopwatch.StartNew();
   try
   {
    var paths=new string[3]; var aliases=new string[3];
    for(int i=0;i<3;i++) { string id=Guid.NewGuid().ToString("N"); paths[i]=Path.Combine(dir,id+".vtex_c"); aliases[i]="gltf-native-textures/"+id+".vtex"; }
    if(batch)
    {
     for(int i=0;i<3;i++) { File.WriteAllBytes(paths[i],payloads[i]); var redirect=RedirectFileSystem.Create(dir); redirects.Add(redirect); redirect.AddAbsFile("/"+aliases[i]+"_c",paths[i]); }
     NativeEngine.g_pResourceSystem.InvalidateDatabase();
     for(int i=0;i<3;i++) { var texture=Texture.Load(aliases[i],false); textures.Add(texture); Assert.IsTrue(texture is {IsValid:true,IsError:false}); }
    }
    else for(int i=0;i<3;i++)
    {
     File.WriteAllBytes(paths[i],payloads[i]); var redirect=RedirectFileSystem.Create(dir); redirects.Add(redirect);
     redirect.AddAbsFile("/"+aliases[i]+"_c",paths[i]); NativeEngine.g_pResourceSystem.InvalidateDatabase();
     var texture=Texture.Load(aliases[i],false); textures.Add(texture); Assert.IsTrue(texture is {IsValid:true,IsError:false});
    }
    watch.Stop(); Measurements.Add(new { iteration,batch,elapsed_ms=watch.Elapsed.TotalMilliseconds,count=textures.Count,scope="Fresh alias; sequential resource operations; timing excludes preparation/hash and cleanup; IsValid does not prove streamed pixels." });
   }
   finally { foreach(var texture in textures) texture?.Dispose(); foreach(var redirect in redirects) redirect.Dispose(); Directory.Delete(dir,true); }
  }
 }
}
