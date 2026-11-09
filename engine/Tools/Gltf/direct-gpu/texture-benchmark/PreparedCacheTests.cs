using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox;
using Sandbox.Gltf;

namespace Sandbox.Gltf.DirectGpu.TextureBenchmark;

[TestClass]
internal sealed class PreparedCacheTests
{
 [TestMethod]
 public void IdentityCorruptionCapacityAndFailure()
 {
  string dir=Path.Combine(Path.GetTempPath(),"gltf_prepared_test_"+Guid.NewGuid().ToString("N")); string version="v1";
  try
  {
   var cache=new GltfPreparedTextureCache(dir,104,()=>version); var first=new GltfImage{MimeType="image/jpeg",Bytes=[1,2,3]}; int created=0;
   byte[] Create(){created++;return [4,5,6,7];}
   CollectionAssert.AreEqual(new byte[]{4,5,6,7},cache.GetOrCreate(first,true,true,Create));
   var second=new GltfPreparedTextureCache(dir,104,()=>version);
   CollectionAssert.AreEqual(new byte[]{4,5,6,7},second.GetOrCreate(first,true,true,Create)); Assert.AreEqual(1,created,"A fresh cache instance must reuse a valid file.");
   File.WriteAllBytes(Directory.GetFiles(dir,"*.gltftex").Single(),[1,2,3]);
   CollectionAssert.AreEqual(new byte[]{4,5,6,7},cache.GetOrCreate(first,true,true,Create)); Assert.AreEqual(2,created,"Corrupt cache data must be rebuilt.");
   _=cache.GetOrCreate(first,false,true,Create); Assert.AreEqual(3,created);
   _=cache.GetOrCreate(first,true,false,Create); Assert.AreEqual(4,created);
   version="v2"; _=cache.GetOrCreate(first,true,true,Create); Assert.AreEqual(5,created,"Producer version must partition entries.");
   _=cache.GetOrCreate(new GltfImage{MimeType="image/png",Bytes=[1,2,3]},true,true,Create); Assert.AreEqual(6,created,"MIME must partition entries.");
   _=cache.GetOrCreate(new GltfImage{MimeType="image/jpeg",Bytes=[1,2,4]},true,true,Create); Assert.AreEqual(7,created,"Encoded bytes must partition entries.");
   Assert.IsTrue(Directory.GetFiles(dir,"*.gltftex").Sum(x=>new FileInfo(x).Length)<=104,"Persistent cache exceeded its byte budget.");
   var failure=new GltfImage{MimeType="image/jpeg",Bytes=[9]};
   Assert.ThrowsException<InvalidOperationException>(()=>cache.GetOrCreate(failure,true,true,()=>throw new InvalidOperationException()));
   _=cache.GetOrCreate(failure,true,true,Create); Assert.AreEqual(8,created,"Failures must not be cached.");
   var disabled=new GltfPreparedTextureCache(Path.Combine(dir,"disabled"),0,()=>version); _=disabled.GetOrCreate(first,true,true,Create); _=disabled.GetOrCreate(first,true,true,Create); Assert.AreEqual(10,created);
  }
  finally { if(Directory.Exists(dir))Directory.Delete(dir,true); }
 }
}
