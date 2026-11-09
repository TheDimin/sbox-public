using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox;
using Sandbox.Gltf;

namespace Sandbox.Gltf.DirectGpu.TextureBenchmark;

[TestClass]
internal sealed class PublicCompiledReadback
{
 static string checkout;
 public static List<object> Measurements {get;}=new();
 public static void Configure(string root){checkout=root;Measurements.Clear();}
 [TestMethod]
 public async Task CompiledOnlyPublicModelTextures()
 {
  await MainThread.Wait();
  string source=Path.Combine(checkout,"Temp/gltf-reference/glTF-Sample-Assets/Models/ABeautifulGame/glTF-Binary/ABeautifulGame.glb");
  var input=File.ReadAllBytes(source); Assert.AreEqual("bd7133b4b322aae97c589b8839dae8155ad2546acb35ae32a127e722a959d007",Convert.ToHexStringLower(SHA256.HashData(input)));
  var extensions=new HashSet<string>{"KHR_draco_mesh_compression","KHR_texture_basisu"};
  var package=GltfCompiler.Compile(input,null,extensions);
  using var doc=GltfReader.Read(package,null,extensions);
  var expected=GltfStockMaterialAdapter.PrepareSource(doc,doc.ReadMaterial(0));
  string directory=Path.Combine(Path.GetTempPath(),"gltf_compiled_only_"+Guid.NewGuid().ToString("N"));
  string name="gltf_compiled_only_"+Guid.NewGuid().ToString("N")+".glb_c";
  Model model=null; LocalFileSystem mount=null;
  try
  {
   Directory.CreateDirectory(directory); File.WriteAllBytes(Path.Combine(directory,name),package);
   Assert.AreEqual(1,Directory.GetFiles(directory).Length,"Only the compiled file is mounted.");
   mount=new LocalFileSystem(directory); FileSystem.Mounted.Mount(mount); model=Model.Load(name);
   Assert.IsTrue(model is {IsValid:true,IsError:false} && model.GltfResource is not null && model.MeshCount==49);
   var materials=model.Materials; Assert.AreEqual(15,materials.Length); var material=materials[0]; Assert.IsTrue(material is {IsValid:true,IsError:false});
   foreach(var pair in expected.Resources)
   {
    string key="g_tGltf"+pair.Key+"Texture"; var texture=material.GetTexture(key);
    Assert.IsTrue(texture is {IsValid:true,IsError:false},"Missing bound public texture "+key);
    Assert.AreEqual(12,texture.Mips); var timer=Stopwatch.StartNew(); Color32[] pixels; int attempts=0;
    do { texture.MarkUsed(2048); await Task.Delay(100); await MainThread.Wait(); pixels=texture.GetPixels(0); attempts++; }
    while(pixels.Length!=2048*2048 && timer.Elapsed<TimeSpan.FromSeconds(45));
    Assert.AreEqual(2048*2048,pixels.Length,"Bound public texture did not reach full residency");
    byte[] resource=pair.Value.Resource; int payload=checked((int)BinaryPrimitives.ReadUInt32LittleEndian(resource)); int offset=resource.Length;
    var samples=new List<object>();
    for(int mip=0;mip<12;mip++)
    {
     int w=Math.Max(1,2048>>mip),h=w; offset-=w*h*4; var actual=texture.GetPixels(mip); Assert.AreEqual(w*h,actual.Length); long changed=0;
     for(int i=0;i<actual.Length;i++) { var p=actual[i]; int at=offset+i*4; if(p.r!=resource[at] || p.g!=resource[at+1] || p.b!=resource[at+2] || p.a!=resource[at+3]) changed++; }
     samples.Add(new{mip,pixels=actual.Length,different_pixels=changed}); Assert.AreEqual(0L,changed,"Bound material texture bytes differ");
    }
    Assert.AreEqual(payload,offset);
    Measurements.Add(new{role=pair.Key.ToString(),compiled_sha256=Convert.ToHexStringLower(SHA256.HashData(package)),resource_sha256=Convert.ToHexStringLower(SHA256.HashData(resource)),attempts,seconds=timer.Elapsed.TotalSeconds,mips=samples,scope="Actual public compiled-only Model.Load material texture, no source files mounted; bounded residency and serialized-byte oracle, not independent codec or sampler proof."});
   }
  }
  finally { await MainThread.Wait(); try { model?.Destroy(); } finally { try { if(mount!=null)FileSystem.Mounted.UnMount(mount); } finally { mount?.Dispose(); if(Directory.Exists(directory))Directory.Delete(directory,true); } } }
 }
}
