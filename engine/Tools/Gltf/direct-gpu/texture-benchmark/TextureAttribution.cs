using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox;
using Sandbox.Gltf;

namespace Sandbox.Gltf.DirectGpu.TextureBenchmark;

[TestClass]
internal sealed class TextureAttribution
{
 static string checkout;
 public static List<object> Measurements { get; } = new();
 public static void Configure(string root) { checkout=root; Measurements.Clear(); }

 [TestMethod]
 public void ActualTextureCacheAndSameModelReuse()
 {
  string source=Path.Combine(checkout,"Temp/gltf-reference/glTF-Sample-Assets/Models/ABeautifulGame/glTF-Binary/ABeautifulGame.glb");
  byte[] input=File.ReadAllBytes(source);
  Assert.AreEqual("bd7133b4b322aae97c589b8839dae8155ad2546acb35ae32a127e722a959d007",Convert.ToHexStringLower(SHA256.HashData(input)));
  using var doc=GltfReader.Read(input,null,new HashSet<string>{"KHR_draco_mesh_compression","KHR_texture_basisu"});
  var images=new Dictionary<int,GltfImage>();
  GltfImage Read(int i) { if(!images.TryGetValue(i,out var image)) images.Add(i,image=doc.ReadImage(i)); return image; }
  var leases=new List<GltfMaterialLease<Texture>>(); var seen=new HashSet<string>();
  var watch=new Stopwatch(); string dir=Path.Combine(Path.GetTempPath(),"gltf_texture_attr_"+Guid.NewGuid().ToString("N"));
  LocalFileSystem mount=null; Model first=null,second=null;
  try
  {
   for(int material=0;material<15;material++)
   {
    watch.Restart(); var prepared=GltfStockMaterialAdapter.PrepareSource(doc,doc.ReadMaterial(material),Read); double prepare=watch.Elapsed.TotalMilliseconds;
    Measurements.Add(new { phase="prepare_material",material,ms=prepare });
    foreach(var pair in prepared.Resources.OrderBy(p=>p.Key))
    {
     var resource=pair.Value.Resource;
     watch.Restart(); var hash=Convert.ToHexStringLower(SHA256.HashData(resource)); double hashMs=watch.Elapsed.TotalMilliseconds;
     bool firstHash=seen.Add(hash);
     watch.Restart(); var lease=GltfNativeTextureCache.Acquire(resource); double acquireMs=watch.Elapsed.TotalMilliseconds;
     leases.Add(lease); Assert.IsTrue(lease.Value.IsValid && !lease.Value.IsError);
     Measurements.Add(new { phase="acquire_texture",material,role=pair.Key.ToString(),first_content=firstHash,bytes=resource.Length,sha256=hash,hash_ms=hashMs,acquire_ms=acquireMs });
    }
   }
   Directory.CreateDirectory(dir); string name="texture_attr_"+Guid.NewGuid().ToString("N")+".glb"; File.WriteAllBytes(Path.Combine(dir,name),input);
   mount=new LocalFileSystem(dir); FileSystem.Mounted.Mount(mount);
   watch.Restart(); first=Model.Load(name); double firstLoad=watch.Elapsed.TotalMilliseconds;
   Assert.IsTrue(first is {IsValid:true,IsError:false} && first.MeshCount==49);
   watch.Restart(); var materials=first.Materials; double firstMaterials=watch.Elapsed.TotalMilliseconds;
   Assert.AreEqual(15,materials.Length); Assert.IsTrue(materials.All(m=>m is {IsValid:true,IsError:false}));
   watch.Restart(); second=Model.Load(name); double secondLoad=watch.Elapsed.TotalMilliseconds;
   Assert.IsTrue(second is {IsValid:true,IsError:false});
   watch.Restart(); var secondMaterials=second.Materials; double secondMaterialMs=watch.Elapsed.TotalMilliseconds;
   Assert.AreEqual(15,secondMaterials.Length);
   Measurements.Add(new { phase="same_asset",first_load_ms=firstLoad,first_materials_ms=firstMaterials,second_load_ms=secondLoad,second_materials_ms=secondMaterialMs,same_wrapper=ReferenceEquals(first,second),texture_leases_held=leases.Count,unique_hashes=seen.Count,scope="All directly acquired texture leases retained across both public loads; same resource path on second load." });
  }
  finally
  {
   try { if(second!=null && !ReferenceEquals(first,second)) second.Destroy(); first?.Destroy(); }
   finally { try { for(int i=leases.Count-1;i>=0;i--) leases[i].Dispose(); } finally { try { if(mount!=null) FileSystem.Mounted.UnMount(mount); } finally { mount?.Dispose(); if(Directory.Exists(dir)) Directory.Delete(dir,true); } } }
  }
 }
}
