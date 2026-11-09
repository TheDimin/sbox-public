using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Sandbox.Gltf;
using NV3=System.Numerics.Vector3;
using NV4=System.Numerics.Vector4;
using NM=System.Numerics.Matrix4x4;
namespace Sandbox.Gltf.DirectGpu.NormalTangentTestValidation;
[TestClass]
public sealed class MissingTangentGpuRegression
{
 public static string CheckoutRoot {get;set;}
 public static List<object> Measurements {get;}=new();
 [TestMethod]
 public async Task OldShaderFailsParallelFallbackCandidatePreservesAllFrameRoutes()
 {
  await MainThread.Wait();Measurements.Clear();var output=Path.Combine(CheckoutRoot,"Temp/gltf-validation/direct-gpu/missing-tangent-candidate",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);
  var folder=Path.Combine(CheckoutRoot,"research/gltf-validation/direct-gpu/models/NormalTangentTest");var old=File.ReadAllBytes(Path.Combine(CheckoutRoot,"game/core/shaders/gltf_deform_cs.shader"));var candidate=File.ReadAllBytes(Path.Combine(folder,"gltf_deform_missing_tangent_candidate.shader"));
  Assert.AreEqual("5b7cc9ef0a2f46999d50762036cedd348a1e243a6701633e860c7700909afd93",Hash(old));Assert.AreEqual("5f02c8ca37cfbb0296f5deb3c26d4c6f431641c0145251314c5ec7afcae9968d",Hash(candidate));
  Shader oldShader=null,newShader=null;Material oldCompute=null,newCompute=null;
  using var capture=new GltfGpuTriangleGateTests.NativeCapture(output);
  try
  {
   using(var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(90)))oldShader=await Shader.CompileAsync(Encoding.UTF8.GetString(old),timeout.Token);
   using(var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(90)))newShader=await Shader.CompileAsync(Encoding.UTF8.GetString(candidate),timeout.Token);
   await MainThread.Wait();Assert.IsTrue(oldShader is {IsValid:true,IsAnonymous:true} && newShader is {IsValid:true,IsAnonymous:true});
   oldCompute=Material.Create("missing_old_"+Guid.NewGuid().ToString("N"),oldShader);newCompute=Material.Create("missing_new_"+Guid.NewGuid().ToString("N"),newShader);
   using var missing=Fixture(false);using var authored=Fixture(true);
   var oldRow=await Raw(missing,oldCompute);var newRow=await Raw(missing,newCompute);var authoredOld=await Raw(authored,oldCompute);var authoredNew=await Raw(authored,newCompute);
   var bad=Frame(oldRow);var good=Frame(newRow);var authoredBefore=Frame(authoredOld);var authoredAfter=Frame(authoredNew);
   var oldDot=MathF.Abs(NV3.Dot(bad.n,bad.t));var newDot=MathF.Abs(NV3.Dot(good.n,good.t));
   Measurements.Add(new{oldDot,newDot,oldNormal=bad.n.ToString(),oldTangent=bad.t.ToString(),newNormal=good.n.ToString(),newTangent=good.t.ToString(),candidateHash=Hash(candidate),oldHash=Hash(old)});
   Assert.IsTrue(oldDot>.01f,$"Old shader negative control did not reproduce: dot={oldDot}");
   Assert.IsTrue(newDot<.000003f && MathF.Abs(good.t.Length()-1)<.000003f && good.w==1,"Missing tangent candidate must return a finite orthogonal unit frame.");
   Assert.IsTrue(NV3.Distance(authoredBefore.t,authoredAfter.t)<.000003f && authoredBefore.w==-1 && authoredAfter.w==-1,"Authored tangent frame changed.");
   Assert.IsTrue(NV3.Distance(authoredAfter.t,NV3.UnitZ)<.000003f,"Authored source +Y maps to world +Z under Z rotation.");
   using var generated=(GltfDocument)typeof(NormalCornerPublicComputeTests).GetMethod("Fixture",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{true});
   using(var asset=Projected(generated,newCompute,capture.Material))using(var instance=asset.CreateInstance(capture.Scene.SceneWorld))
   {
    await capture.WaitFrames();Assert.IsTrue(instance.SubmittedRevision==instance.PoseRevision && instance.DrawCalls>0 && instance.CallbackErrors.Length==0);var row=instance.Readback().Single();
    CollectionAssert.AreEqual(new uint[]{0,1,2,0,2,3},row.SourceVertices);var field=row.Fields.Single(x=>x.Semantic=="TANGENT");
    for(var i=0;i<6;i++){var t=Read4(row,i,field.Offset);var expected=i<3?new NV4(0,1,0,-1):new NV4(0,-1,0,1);Assert.IsTrue(NV4.Distance(t,expected)<.0001f,$"Generated corner {i}: {t}");}
   }
   using var flat=(GltfDocument)typeof(GeneratedTangentSkinProbe).GetMethod("Fixture",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{false,true,true,true});
   using(var asset=Projected(flat,newCompute,capture.Material))using(var instance=asset.CreateInstance(capture.Scene.SceneWorld))
   {
    foreach(var placement in new[]{NM.Identity,NM.CreateScale(-1,1,1),NM.CreateScale(2,3,4)})
    {
     instance.Placement=placement;await capture.WaitFrames();var row=instance.Readback().Single();var nf=row.Fields.Single(x=>x.Semantic=="NORMAL");var tf=row.Fields.Single(x=>x.Semantic=="TANGENT");
     for(var i=0;i<3;i++){var n=Read4(row,i,nf.Offset);var t=Read4(row,i,tf.Offset);Assert.IsTrue(NV3.Distance(new(n.X,n.Y,n.Z),-NV3.UnitZ)<.0001f);Assert.IsTrue(NV4.Distance(t,new NV4(0,1,0,placement.M11<0?1:-1))<.0001f);}
    }
   }
   async Task<GltfGpuPrimitiveSnapshot> Raw(GltfDocument doc,Material compute)
   {
    using var asset=GltfGpuAsset.Create(doc,compute,_=>capture.Material);using var instance=asset.CreateInstance(capture.Scene.SceneWorld);instance.Placement=NM.CreateRotationZ(.5890486225f);await capture.WaitFrames();Assert.IsTrue(instance.SubmittedRevision==instance.PoseRevision && instance.DrawCalls>0 && instance.CallbackErrors.Length==0);return instance.Readback().Single();
   }
  }
  finally {await MainThread.Wait();oldCompute?.Destroy();newCompute?.Destroy();oldShader?.Destroy();newShader?.Destroy();}
 }
 private static GltfGpuAsset Projected(GltfDocument doc,Material compute,Material material)
 {
  var a=GltfGpuAsset.Create(doc,compute,_=>null);try {a.ConfigureProjectedVariants(1024*1024,(_,_,_)=>new GltfMaterialLease<Material>(material,()=>{},null));return a;}catch{a.Dispose();throw;}
 }
 private static (NV3 n,NV3 t,float w) Frame(GltfGpuPrimitiveSnapshot row){var n=row.Fields.Single(x=>x.Semantic=="NORMAL");var t=row.Fields.Single(x=>x.Semantic=="TANGENT");var a=Read4(row,0,n.Offset);var b=Read4(row,0,t.Offset);return(new(a.X,a.Y,a.Z),new(b.X,b.Y,b.Z),b.W);}
 private static NV4 Read4(GltfGpuPrimitiveSnapshot row,int i,int offset){var p=i*row.VertexStride+offset;float F(int c)=>BinaryPrimitives.ReadSingleLittleEndian(row.Bytes.AsSpan(p+c*4));return new(F(0),F(1),F(2),F(3));}
 private static string Hash(byte[] bytes)=>Convert.ToHexStringLower(SHA256.HashData(bytes));
 private static GltfDocument Fixture(bool authored)
 {
  var data=new byte[120];float[] pos=[0,0,0, 0,1,0, 0,0,1];for(int i=0;i<pos.Length;i++)BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(i*4),pos[i]);
  for(int i=0;i<3;i++){BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(36+i*12),1);BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(72+i*16+4),1);BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(72+i*16+12),-1);}
  var tangent=authored?",\"TANGENT\":2":"";
  var json=$$$"""
  {"asset":{"version":"2.0"},"scene":0,"scenes":[{"nodes":[0]}],"nodes":[{"mesh":0}],"buffers":[{"uri":"data:application/octet-stream;base64,{{{Convert.ToBase64String(data)}}}","byteLength":120}],"bufferViews":[{"buffer":0,"byteOffset":0,"byteLength":36},{"buffer":0,"byteOffset":36,"byteLength":36},{"buffer":0,"byteOffset":72,"byteLength":48}],"accessors":[{"bufferView":0,"componentType":5126,"count":3,"type":"VEC3","min":[0,0,0],"max":[0,1,1]},{"bufferView":1,"componentType":5126,"count":3,"type":"VEC3"},{"bufferView":2,"componentType":5126,"count":3,"type":"VEC4"}],"meshes":[{"primitives":[{"attributes":{"POSITION":0,"NORMAL":1{{{tangent}}}}}]}]}
  """;
  return GltfReader.Read(GltfCompiler.Compile(Encoding.UTF8.GetBytes(json)));
 }
}
