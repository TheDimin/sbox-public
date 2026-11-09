using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;
using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace Sandbox.Gltf.DirectGpu.SimpleSparseAccessorValidation;

[TestClass]
internal sealed class InputArenaTests
{
 public static string CheckoutRoot { get; set; }
 public static List<object> Measurements { get; } = new();
 [TestMethod]
 public void SharedViewsHaveLiteralBytesAndOffsets()
 {
  using var document=GltfGpuBackendContractTests.InterleavedTriangle();
  var plan=GltfGpuPrimitivePlan.Create(document,0,0);
  Assert.AreEqual(96,plan.InputBytes.Length);
  Assert.AreEqual(4,plan.Sources["POSITION"].ArenaByteOffset);
  Assert.AreEqual(16,plan.Sources["NORMAL"].ArenaByteOffset);
  Assert.AreEqual(28,plan.Sources["COLOR_0"].ArenaByteOffset);
  Assert.AreEqual(128,plan.InputBytes[29]);
  Assert.AreEqual(1f,BitConverter.ToSingle(plan.InputBytes,36));
  Check(document,plan);
 }
 [TestMethod]
 [DataRow("SimpleSparseAccessor")]
 [DataRow("SimpleSkin")]
 [DataRow("AnimatedMorphCube")]
 public void ArenaAndDescriptorsMatchIndependentOrderedStorageCopy(string model)
 {
  string dir=Path.Combine(CheckoutRoot,"Temp/gltf-reference/glTF-Sample-Assets/Models",model,"glTF");
  var source=File.ReadAllBytes(Path.Combine(dir,model+".gltf"));
  using var doc=GltfReader.Read(source,uri=>File.ReadAllBytes(Path.Combine(dir,uri)));
  Check(doc,GltfGpuPrimitivePlan.Create(doc,0,0));
 }
 static void Check(GltfDocument document,GltfGpuPrimitivePlan plan)
 {
  var targets=document.MaterializeMorphTargetsUpload(plan.Mesh,plan.Primitive);
  var streams=plan.Upload.Attributes.OrderBy(x=>x.Key,StringComparer.Ordinal).Select(x=>x.Value)
   .Concat(targets.Targets.SelectMany(t=>t.OrderBy(x=>x.Key,StringComparer.Ordinal).Select(x=>x.Value))).ToArray();
  var targetIds=Enumerable.Repeat(uint.MaxValue,plan.Upload.Attributes.Count)
   .Concat(targets.Targets.SelectMany((target,index)=>Enumerable.Repeat((uint)index,target.Count))).ToArray();
  var offsets=new Dictionary<GltfUploadStorage,int>(ReferenceEqualityComparer.Instance);
  var expected=new List<byte>();
  for(int i=0;i<streams.Length;i++)
  {
   var stream=streams[i];
   if(!offsets.TryGetValue(stream.Storage,out int start))
   {
    while(expected.Count%4!=0)expected.Add(0);
    start=expected.Count; offsets.Add(stream.Storage,start);
    foreach(byte b in stream.Storage.Bytes.Span)expected.Add(b);
   }
   var descriptor=plan.InputDescriptors[i];
   Assert.AreEqual((uint)(start+stream.ByteOffset),descriptor.ByteOffset);
   Assert.AreEqual((uint)stream.ByteStride,descriptor.ByteStride);
   Assert.AreEqual((uint)stream.ComponentType,descriptor.ComponentType);
   Assert.AreEqual((uint)stream.Components,descriptor.Components);
   Assert.AreEqual(stream.Normalized?1u:0u,descriptor.Normalized);
   Assert.AreEqual(targetIds[i],descriptor.Target);
  }
  while(expected.Count%4!=0)expected.Add(0);
  CollectionAssert.AreEqual(expected.ToArray(),plan.InputBytes);
 }
 [TestMethod]
 public void BuilderAlignsDistinctStorageAndRejectsNull()
 {
		using var document=GltfGpuBackendContractTests.InterleavedTriangle();
  var upload=document.MaterializePrimitiveUpload(0,0);
		var first=upload.Indices.Storage;
  var second=upload.Attributes["POSITION"].Storage;
  using var otherDocument=GltfGpuBackendContractTests.InterleavedTriangle();
  var third=otherDocument.MaterializePrimitiveUpload(0,0).Indices.Storage;
  var builder=new GltfGpuInputArenaBuilder();
  Assert.ThrowsException<ArgumentNullException>(()=>builder.Add(null));
  Assert.AreEqual(0,builder.Add(first));
  Assert.AreEqual(0,builder.Add(first));
		Assert.AreEqual(4,builder.Add(second));
  Assert.AreEqual(100,builder.Add(third));
  var bytes=builder.Build();
  Assert.AreEqual(104,bytes.Length);
  CollectionAssert.AreEqual(first.Bytes.ToArray().Concat(new byte[]{0}).Concat(second.Bytes.ToArray()).Concat(third.Bytes.ToArray()).Concat(new byte[]{0}).ToArray(),bytes);
  bytes[0]^=255;
  Assert.AreNotEqual(bytes[0],first.Bytes.Span[0],"Arena must not alias immutable storage");
 }
 [TestMethod]
 public void WarmBuilderAllocationComparisonOnSimpleSkin()
 {
  string dir=Path.Combine(CheckoutRoot,"Temp/gltf-reference/glTF-Sample-Assets/Models/SimpleSkin/glTF");
  using var document=GltfReader.Read(File.ReadAllBytes(Path.Combine(dir,"SimpleSkin.gltf")),uri=>File.ReadAllBytes(Path.Combine(dir,uri)));
  var upload=document.MaterializePrimitiveUpload(0,0);
  var streams=upload.Attributes.OrderBy(x=>x.Key,StringComparer.Ordinal).Select(x=>x.Value.Storage).ToArray();
  byte[] Previous()
  {
   var seen=new HashSet<GltfUploadStorage>(ReferenceEqualityComparer.Instance);var bytes=new List<byte>();
   foreach(var storage in streams)if(seen.Add(storage)){while(bytes.Count%4!=0)bytes.Add(0);bytes.AddRange(storage.Bytes.ToArray());}
   while(bytes.Count%4!=0)bytes.Add(0);return bytes.ToArray();
  }
  byte[] Current(){var arena=new GltfGpuInputArenaBuilder();foreach(var storage in streams)arena.Add(storage);return arena.Build();}
  CollectionAssert.AreEqual(Previous(),Current());
  foreach(bool previous in new[]{true,false,false,true})
  {
   for(int i=0;i<50;i++)_ = previous?Previous():Current();
   long before=GC.GetAllocatedBytesForCurrentThread();var timer=Stopwatch.StartNew();
   for(int i=0;i<1000;i++)_ = previous?Previous():Current();
   timer.Stop();long allocated=GC.GetAllocatedBytesForCurrentThread()-before;
   Measurements.Add(new{route=previous?"previous-list":"current-single-array",iterations=1000,elapsed_ms=timer.Elapsed.TotalMilliseconds,allocated_bytes=allocated,scope="Warm CPU arena build only; previous algorithm reproduced in test, no GPU timing"});
  }
 }
}
