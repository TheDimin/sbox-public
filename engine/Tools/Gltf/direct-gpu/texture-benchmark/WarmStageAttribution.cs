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
internal sealed class WarmStageAttribution
{
 static string checkout;
 public static List<object> Measurements {get;}=new();
 public static void Configure(string root){checkout=root;Measurements.Clear();}
 [TestMethod]
 public void MeasurePreparedAndNativeAcquisitionStages()
 {
  var input=File.ReadAllBytes(Path.Combine(checkout,"Temp/gltf-reference/glTF-Sample-Assets/Models/ABeautifulGame/glTF-Binary/ABeautifulGame.glb"));
  Assert.AreEqual("bd7133b4b322aae97c589b8839dae8155ad2546acb35ae32a127e722a959d007",Convert.ToHexStringLower(SHA256.HashData(input)));
  using var document=GltfReader.Read(input,null,new HashSet<string>{"KHR_draco_mesh_compression","KHR_texture_basisu"});
  var images=new Dictionary<int,GltfImage>(); GltfImage Read(int i){if(!images.TryGetValue(i,out var image))images.Add(i,image=document.ReadImage(i));return image;}
  var before=GltfPreparedTextureCache.Shared.GetStats(); var watch=Stopwatch.StartNew();
  var prepared=Enumerable.Range(0,15).Select(i=>GltfStockMaterialAdapter.PrepareSource(document,document.ReadMaterial(i),Read)).ToArray();
  double preparation=watch.Elapsed.TotalMilliseconds; var after=GltfPreparedTextureCache.Shared.GetStats();
  var resources=prepared.SelectMany(x=>x.Resources.Values).Select(x=>x.Resource).ToArray(); Assert.AreEqual(56,resources.Length);
  watch.Restart(); foreach(var bytes in resources)_=SHA256.HashData(bytes); double separateHash=watch.Elapsed.TotalMilliseconds;
  GltfMaterialLease<Texture>[] initial=null;
  try
  {
   watch.Restart(); initial=GltfNativeTextureCache.AcquireBatch(resources); double batch=watch.Elapsed.TotalMilliseconds;
   watch.Restart();
   int ordinal=0,retainedCount=0;
   foreach(var item in prepared)
   {
    var leases=new List<GltfMaterialLease<Texture>>();
    try { foreach(var role in item.Resources) { leases.Add(GltfNativeTextureCache.Retain(initial[ordinal++])); retainedCount++; } }
    finally { foreach(var lease in leases)lease.Dispose(); }
   }
   double repeated=watch.Elapsed.TotalMilliseconds;
   Assert.AreEqual(resources.Length,ordinal);
   Measurements.Add(new{prepare_ms=preparation,cache_hits=after.Hits-before.Hits,cache_misses=after.Misses-before.Misses,cache_bypassed=after.Bypassed-before.Bypassed,prepared_roles=resources.Length,batch_hashed_bytes=resources.Sum(x=>(long)x.Length),retained_role_leases=retainedCount,independent_hash_ms=separateHash,all_resources_batch_ms=batch,per_material_live_lease_retain_ms=repeated,scope="Single diagnostic pass on actual ABeautifulGame prepared data; separate SHA pass warms bytes and is not part of user loading. Batch may invalidate on fallback; no GPU completion or whole-frame timing."});
  }
  finally { if(initial!=null)foreach(var lease in initial)lease.Dispose(); }
 }
}
