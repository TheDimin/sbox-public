using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox;
using Sandbox.Gltf;

namespace Sandbox.Gltf.DirectGpu.TextureBenchmark;

[TestClass]
internal sealed class SelectedBatchTests
{
 sealed record Packet(int Mesh,int? Material);
 sealed class Scope : IDisposable { readonly Action action; public Scope(Action action)=>this.action=action; public void Dispose()=>action(); }
 const string Fixture="""
 {"asset":{"version":"2.0"},"extensionsUsed":["KHR_materials_variants"],"extensions":{"KHR_materials_variants":{"variants":[{"name":"changed"}]}},"materials":[{},{},{}],"meshes":[{"primitives":[{"attributes":{"POSITION":0},"material":0,"extensions":{"KHR_materials_variants":{"mappings":[{"material":1,"variants":[0]}]}}}]},{"primitives":[{"attributes":{"POSITION":0},"material":0,"extensions":{"KHR_materials_variants":{"mappings":[{"material":2,"variants":[0]}]}}}]}]}
 """;
 [TestMethod]
 public void SelectedOnlyAndTransactionalFailure()
 {
  using var doc=GltfReader.Read(Encoding.UTF8.GetBytes(Fixture),supportedExtensions:new HashSet<string>{"KHR_materials_variants"});
  GltfMaterialVariantSlot[] slots=[new(0,0,0),new(1,1,0),new(2,0,0)];
  var batches=new List<int?[]>(); var releases=new List<int?>(); int scopes=0; bool fail=false;
  using var asset=new GltfVariantDrawAsset<Packet>(doc,slots,(slot,index)=>
  {
   if(fail && index==2)throw new InvalidOperationException("injected");
   return new GltfMaterialLease<Packet>(new Packet(slot.Mesh,index),()=>releases.Add(index),null);
  },_=>true,null,pending=>{batches.Add(pending.Select(x=>x.Material).ToArray()); return new Scope(()=>scopes++);});
  using var state=asset.CreateState(); CollectionAssert.AreEqual(new int?[]{0,0},batches[0]); Assert.AreEqual(1,scopes);
  var before=state.Current; fail=true; Assert.ThrowsException<InvalidOperationException>(()=>state.SetVariant(0));
  CollectionAssert.AreEqual(new int?[]{1,2},batches[1]); Assert.AreSame(before,state.Current); Assert.AreEqual(2,scopes); CollectionAssert.AreEqual(new int?[]{1},releases);
  fail=false; state.SetVariant(0); CollectionAssert.AreEqual(new int?[]{1,2},batches[2]); Assert.AreEqual(3,scopes);
  state.SetVariant(null); Assert.AreEqual(4,scopes); Assert.AreEqual(0,batches[3].Length,"Existing packets must not cause eager alternate acquisition.");
 }
}
