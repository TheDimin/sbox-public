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
internal sealed class PreparedWarmup
{
 static string checkout;
 public static List<object> Measurements {get;}=new();
 public static void Configure(string root){checkout=root;Measurements.Clear();}
 [TestMethod]
 public void ExplicitlyPrepareAndRecordCost()
 {
  var input=File.ReadAllBytes(Path.Combine(checkout,"Temp/gltf-reference/glTF-Sample-Assets/Models/ABeautifulGame/glTF-Binary/ABeautifulGame.glb"));
  Assert.AreEqual("bd7133b4b322aae97c589b8839dae8155ad2546acb35ae32a127e722a959d007",Convert.ToHexStringLower(SHA256.HashData(input)));
  using var doc=GltfReader.Read(input,null,new HashSet<string>{"KHR_draco_mesh_compression","KHR_texture_basisu"});
  var before=GltfPreparedTextureCache.Shared.GetStats();var watch=Stopwatch.StartNew();
  for(int i=0;i<15;i++) _=GltfStockMaterialAdapter.PrepareSource(doc,doc.ReadMaterial(i));
  var after=GltfPreparedTextureCache.Shared.GetStats();
  Measurements.Add(new{preparation_ms=watch.Elapsed.TotalMilliseconds,cache_hits=after.Hits-before.Hits,cache_misses=after.Misses-before.Misses,cache_bypassed=after.Bypassed-before.Bypassed,cache_bytes_written=after.Bytes-before.Bytes,cache_entries_written=after.Count-before.Count,scope="Explicit preparation before the separate warm-load diagnostic. This time is excluded from warm-load timing and must be reported alongside it."});
 }
}
