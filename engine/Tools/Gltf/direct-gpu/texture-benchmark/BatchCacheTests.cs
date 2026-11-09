using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox;
using Sandbox.Engine;
using Sandbox.Gltf;

namespace Sandbox.Gltf.DirectGpu.TextureBenchmark;

[TestClass]
internal sealed class BatchCacheTests
{
 static string checkout;
 public static void Configure(string root) { checkout=root; }
 [TestMethod]
 public async Task MixedHitsDuplicatesIndependentLeasesAndPartialRollback()
 {
  await MainThread.Wait();
  string path=Path.Combine(checkout,"Temp/gltf-reference/glTF-Sample-Assets/Models/ABeautifulGame/glTF-Binary/ABeautifulGame.glb");
  var input=File.ReadAllBytes(path);
  Assert.AreEqual("bd7133b4b322aae97c589b8839dae8155ad2546acb35ae32a127e722a959d007",Convert.ToHexStringLower(SHA256.HashData(input)));
  using var doc=GltfReader.Read(input,null,new HashSet<string>{"KHR_draco_mesh_compression","KHR_texture_basisu"});
  var prepared=GltfStockMaterialAdapter.PrepareSource(doc,doc.ReadMaterial(0));
  var color=prepared.Resources[GltfMaterialTextureRole.BaseColor].Resource;
  var normal=prepared.Resources[GltfMaterialTextureRole.Normal].Resource;
  var directory=Path.Combine(Path.GetTempPath(),"sbox-gltf-textures",Environment.ProcessId.ToString());
  string[] Files()=>Directory.Exists(directory)?Directory.GetFiles(directory).Order().ToArray():Array.Empty<string>();
  var before=Files(); GltfMaterialLease<Texture> held=null; GltfMaterialLease<Texture>[] group=null;
  try
  {
   held=GltfNativeTextureCache.Acquire(color); var sameColor=held.Value;
   group=GltfNativeTextureCache.AcquireBatch(new[]{color,normal,normal});
   Assert.AreSame(sameColor,group[0].Value); Assert.AreSame(group[1].Value,group[2].Value);
   Assert.AreNotSame(group[0].Value,group[1].Value);
   var fork=GltfNativeTextureCache.Retain(group[1]);
   var owners=typeof(GltfNativeTextureCache).GetField("leaseOwners",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
   bool Tracked(GltfMaterialLease<Texture> lease) => (bool)owners.GetType().GetMethod("TryGetValue").Invoke(owners,new object[]{lease,null});
   try
   {
    Assert.AreSame(group[1].Value,fork.Value);
    var other=ReferenceEquals(GlobalContext.Current,GlobalContext.Game)?GlobalContext.Menu:GlobalContext.Game;
    using(new GlobalContext.GlobalContextScope(other)) Assert.ThrowsException<InvalidOperationException>(()=>GltfNativeTextureCache.Retain(group[1]));
    using(var foreign=new GltfMaterialLease<Texture>(Texture.White,()=>{},null)) Assert.ThrowsException<InvalidOperationException>(()=>GltfNativeTextureCache.Retain(foreign));
    held.Dispose(); held=null; Assert.IsTrue(group[0].Value.IsValid);
    Assert.IsTrue(Tracked(group[1])); group[1].Dispose(); Assert.IsFalse(Tracked(group[1]),"Explicit disposal must release cache ownership metadata.");
    Assert.ThrowsException<ObjectDisposedException>(()=>GltfNativeTextureCache.Retain(group[1]));
    Assert.IsTrue(fork.Value.IsValid && group[2].Value.IsValid);
   }
   finally { fork.Dispose(); }
   Assert.IsTrue(group[2].Value.IsValid);
   Assert.ThrowsException<ArgumentNullException>(()=>GltfNativeTextureCache.AcquireBatch(new byte[][]{normal,null}));
   Assert.IsTrue(group[2].Value.IsValid,"Failed mixed batch must preserve already held entries.");
   group[0].Dispose(); group[2].Dispose(); group=null;
   CollectionAssert.AreEqual(before,Files(),"Released resources must delete their unique files.");
   // Stage one new resource, then reject a second input before invalidation.
   Assert.ThrowsException<ArgumentNullException>(()=>GltfNativeTextureCache.AcquireBatch(new byte[][]{color,null}));
   CollectionAssert.AreEqual(before,Files(),"Partial staging failure must remove its file and redirect.");
   int calls=0;
   Assert.ThrowsException<InvalidOperationException>(()=>GltfNativeTextureCache.AcquireBatch(new[]{color,normal},alias=>
   {
    if(++calls==2) throw new InvalidOperationException("injected second texture load failure");
    return Texture.Load(alias,false);
   }));
   Assert.AreEqual(2,calls); CollectionAssert.AreEqual(before,Files(),"Partial native load failure must remove both generations.");
   calls=0;
   Assert.ThrowsException<GltfException>(()=>GltfNativeTextureCache.AcquireBatch(new[]{color,normal},alias=>
   {
    if(++calls>=2) return null;
    return Texture.Load(alias,false);
   }));
   Assert.AreEqual(3,calls); CollectionAssert.AreEqual(before,Files(),"Null-resource fallback failure must remove both staged generations.");
   calls=0; var attempted=new List<string>();
   var recovered=GltfNativeTextureCache.AcquireBatch(new[]{color,normal},alias=>
   {
    attempted.Add(alias); if(++calls==2)return null;
    return Texture.Load(alias,false);
   });
   try
   {
    Assert.AreEqual(3,calls); Assert.AreNotEqual(attempted[1],attempted[2],"A missed alias must never be retried under the same name.");
    Assert.IsTrue(recovered.All(x=>x.Value is {IsValid:true,IsError:false}));
    var retained=recovered[0].Value; Assert.AreEqual(12,retained.Mips); var timer=Stopwatch.StartNew(); Color32[] pixels;
    do { retained.MarkUsed(2048); await Task.Delay(100); await MainThread.Wait(); pixels=retained.GetPixels(0); }
    while(pixels.Length!=2048*2048 && timer.Elapsed<TimeSpan.FromSeconds(45));
    Assert.AreEqual(2048*2048,pixels.Length,"Texture loaded before fallback invalidation must reach full residency");
    int offset=color.Length;
    for(int mip=0;mip<12;mip++)
    {
     int size=Math.Max(1,2048>>mip); offset-=size*size*4;var actual=retained.GetPixels(mip);Assert.AreEqual(size*size,actual.Length);long changed=0;
     for(int i=0;i<actual.Length;i++){var p=actual[i];int at=offset+i*4;if(p.r!=color[at]||p.g!=color[at+1]||p.b!=color[at+2]||p.a!=color[at+3])changed++;}
     Assert.AreEqual(0L,changed,"Fallback invalidation changed an already-successful texture mip");
    }
   }
   finally { foreach(var lease in recovered)lease.Dispose(); }
   CollectionAssert.AreEqual(before,Files(),"Successful fallback must release both generations.");
   using(var retired=GltfNativeTextureCache.Acquire(normal))
   {
    retired.Value.Dispose();
    using(var replacement=GltfNativeTextureCache.Acquire(normal))
    {
     Assert.IsTrue(replacement.Value.IsValid);
     Assert.ThrowsException<InvalidOperationException>(()=>GltfNativeTextureCache.Retain(retired),"A retired generation must not be forked into its replacement.");
    }
   }
   CollectionAssert.AreEqual(before,Files(),"Retired and replacement generations must clean up independently.");
   using(var fresh=GltfNativeTextureCache.Acquire(color))
   { Assert.IsTrue(fresh.Value.IsValid); Assert.AreNotSame(sameColor,fresh.Value,"A released generation must not be returned again."); }
  }
  finally { held?.Dispose(); if(group!=null) foreach(var item in group) item?.Dispose(); }
  CollectionAssert.AreEqual(before,Files(),"Reacquired generation must release its file.");
 }
}
