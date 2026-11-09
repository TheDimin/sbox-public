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
internal sealed class BeautifulTextureBenchmark
{
 static string checkout;
 public static List<object> Measurements { get; } = new();
 public static void Configure(string root) { checkout = root; Measurements.Clear(); }

 [TestMethod]
 public void WarmFileCacheFreshResourceGenerations()
 {
  string file = Path.Combine(checkout,"Temp/gltf-reference/glTF-Sample-Assets/Models/ABeautifulGame/glTF-Binary/ABeautifulGame.glb");
  byte[] input = File.ReadAllBytes(file);
  Assert.AreEqual("bd7133b4b322aae97c589b8839dae8155ad2546acb35ae32a127e722a959d007",Convert.ToHexStringLower(SHA256.HashData(input)));
  // Input is held in memory, OS file cache is warmed, and each public load gets
  // a fresh alias after the previous model has been destroyed. No global cache is cleared.
  for (int iteration=0; iteration<3; iteration++)
  {
   GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
   long startAlloc=GC.GetTotalAllocatedBytes(true), imageTicks=0;
   var watch=Stopwatch.StartNew();
   using var doc=GltfReader.Read(input,null,new HashSet<string>{"KHR_draco_mesh_compression","KHR_texture_basisu"});
   double parseMs=watch.Elapsed.TotalMilliseconds;
   var imageCache=new Dictionary<int,GltfImage>();
   GltfImage ReadImage(int index)
   {
    if(imageCache.TryGetValue(index,out var cached)) return cached;
    long start=Stopwatch.GetTimestamp(); var image=doc.ReadImage(index); imageTicks+=Stopwatch.GetTimestamp()-start;
    imageCache.Add(index,image); return image;
   }
   var first=doc.ReadMaterial(0);
   var colorImage=doc.ReadTexture(first.BaseColorTexture.Index).Image.Value;
   var normalImage=doc.ReadTexture(first.NormalTexture.Index).Image.Value;
   var colorEncoded=ReadImage(colorImage); var normalEncoded=ReadImage(normalImage);
   watch.Restart(); var colorProbe=GltfTextureCompiler.CompileWithMipmaps(colorEncoded,true); double colorCompileMs=watch.Elapsed.TotalMilliseconds;
   watch.Restart(); var normalProbe=GltfTextureCompiler.CompileWithMipmaps(normalEncoded,false); double normalCompileMs=watch.Elapsed.TotalMilliseconds;
   string colorHash=Convert.ToHexStringLower(SHA256.HashData(colorProbe)), normalHash=Convert.ToHexStringLower(SHA256.HashData(normalProbe));
   colorProbe=null; normalProbe=null;
   startAlloc=GC.GetTotalAllocatedBytes(true);
   var hashes=new List<string>(); long bytes=0, prepareTicks=0;
   for(int material=0; material<15; material++)
   {
    long start=Stopwatch.GetTimestamp();
    var prepared=GltfStockMaterialAdapter.PrepareSource(doc,doc.ReadMaterial(material),ReadImage);
    prepareTicks+=Stopwatch.GetTimestamp()-start;
    foreach(var pair in prepared.Resources.OrderBy(x=>x.Key)) { bytes+=pair.Value.Resource.Length; hashes.Add(Convert.ToHexStringLower(SHA256.HashData(pair.Value.Resource))); }
   }
   double prepareMs=prepareTicks*1000.0/Stopwatch.Frequency;
   long prepAlloc=GC.GetTotalAllocatedBytes(true)-startAlloc;
   Assert.AreEqual(33,imageCache.Count);
   string dir=Path.Combine(Path.GetTempPath(),"gltf_texture_bench_"+Guid.NewGuid().ToString("N"));
   string name="texture_bench_"+Guid.NewGuid().ToString("N")+".glb";
   LocalFileSystem mount=null; Model model=null;
   try
   {
    Directory.CreateDirectory(dir); File.WriteAllBytes(Path.Combine(dir,name),input);
    mount=new LocalFileSystem(dir); FileSystem.Mounted.Mount(mount);
    watch.Restart(); model=Model.Load(name); double loadMs=watch.Elapsed.TotalMilliseconds;
    Assert.IsTrue(model is { IsValid:true, IsError:false } && model.GltfResource is not null && model.MeshCount==49);
    watch.Restart(); var materials=model.Materials; double materialsMs=watch.Elapsed.TotalMilliseconds;
    Assert.AreEqual(15,materials.Length); Assert.IsTrue(materials.All(x=>x is {IsValid:true,IsError:false}));
    Measurements.Add(new { iteration, parse_ms=parseMs, image_read_ms=imageTicks*1000.0/Stopwatch.Frequency, color_compile_ms=colorCompileMs, normal_compile_ms=normalCompileMs, color_sha256=colorHash, normal_sha256=normalHash, prepare_ms=prepareMs, prepared_resource_bytes=bytes, prep_allocated_bytes=prepAlloc, model_load_ms=loadMs, materials_ms=materialsMs, prepared_sha256=hashes.ToArray(), process_working_set_bytes=Process.GetCurrentProcess().WorkingSet64 });
   }
   finally { try { model?.Destroy(); } finally { try { if(mount!=null) FileSystem.Mounted.UnMount(mount); } finally { mount?.Dispose(); if(Directory.Exists(dir)) Directory.Delete(dir,true); } } }
  }
 }
}
