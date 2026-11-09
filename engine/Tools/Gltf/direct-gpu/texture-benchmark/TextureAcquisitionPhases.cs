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

[TestClass]
internal sealed class TextureAcquisitionPhases
{
 static string checkout;
 public static List<object> Measurements { get; }=new();
 public static void Configure(string root) { checkout=root; Measurements.Clear(); }
 [TestMethod]
 public void TimeActualNativeResourceOperations()
 {
  string source=Path.Combine(checkout,"Temp/gltf-reference/glTF-Sample-Assets/Models/ABeautifulGame/glTF-Binary/ABeautifulGame.glb");
  byte[] input=File.ReadAllBytes(source);
  Assert.AreEqual("bd7133b4b322aae97c589b8839dae8155ad2546acb35ae32a127e722a959d007",Convert.ToHexStringLower(SHA256.HashData(input)));
  using var doc=GltfReader.Read(input,null,new HashSet<string>{"KHR_draco_mesh_compression","KHR_texture_basisu"});
  var prepared=GltfStockMaterialAdapter.PrepareSource(doc,doc.ReadMaterial(0));
  var watch=new Stopwatch(); var dir=Path.Combine(Path.GetTempPath(),"gltf_phase_"+Guid.NewGuid().ToString("N"));
  Directory.CreateDirectory(dir);
  try
  {
   foreach(var pair in prepared.Resources.Where(x=>x.Key!=GltfMaterialTextureRole.Occlusion).OrderBy(x=>x.Key))
   for(int iteration=0;iteration<3;iteration++)
   {
    var id=Guid.NewGuid().ToString("N"); string path=Path.Combine(dir,id+".vtex_c"),alias="gltf-native-textures/"+id+".vtex";
    RedirectFileSystem redirect=null; Texture texture=null;
    try
    {
     watch.Restart(); _=SHA256.HashData(pair.Value.Resource); double hash=watch.Elapsed.TotalMilliseconds;
     watch.Restart(); File.WriteAllBytes(path,pair.Value.Resource); double write=watch.Elapsed.TotalMilliseconds;
     watch.Restart(); redirect=RedirectFileSystem.Create(dir); redirect.AddAbsFile("/"+alias+"_c",path); double map=watch.Elapsed.TotalMilliseconds;
     watch.Restart(); NativeEngine.g_pResourceSystem.InvalidateDatabase(); double invalidate=watch.Elapsed.TotalMilliseconds;
     watch.Restart(); texture=Texture.Load(alias,false); double load=watch.Elapsed.TotalMilliseconds;
     Assert.IsTrue(texture is {IsValid:true,IsError:false});
     Measurements.Add(new { role=pair.Key.ToString(),iteration,hash_ms=hash,write_ms=write,redirect_ms=map,invalidate_ms=invalidate,texture_load_ms=load,bytes=pair.Value.Resource.Length,scope="Same synchronous operations as native texture cache miss; no assertion that GPU streaming has completed." });
    }
    finally { try { texture?.Dispose(); } finally { try { redirect?.Dispose(); } finally { if(File.Exists(path)) File.Delete(path); } } }
   }
  }
  finally { Directory.Delete(dir,true); }
 }
}
