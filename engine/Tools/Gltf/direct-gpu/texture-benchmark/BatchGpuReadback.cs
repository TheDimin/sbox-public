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
using Sandbox.Rendering;

namespace Sandbox.Gltf.DirectGpu.TextureBenchmark;

[TestClass]
internal sealed class BatchGpuReadback
{
 static string checkout;
 public static List<object> Measurements { get; }=new();
 public static void Configure(string root) { checkout=root; Measurements.Clear(); }
 [TestMethod]
 [DataRow(false)]
 [DataRow(true)]
 public async Task SourceAndDerivedCompiledTextureMips(bool compiled)
 {
  await MainThread.Wait();
  var input=File.ReadAllBytes(Path.Combine(checkout,"Temp/gltf-reference/glTF-Sample-Assets/Models/ABeautifulGame/glTF-Binary/ABeautifulGame.glb"));
  Assert.AreEqual("bd7133b4b322aae97c589b8839dae8155ad2546acb35ae32a127e722a959d007",Convert.ToHexStringLower(SHA256.HashData(input)));
  var extensions=new HashSet<string>{"KHR_draco_mesh_compression","KHR_texture_basisu"};
  if(compiled) input=GltfCompiler.Compile(input,null,extensions);
  string hash=Convert.ToHexStringLower(SHA256.HashData(input));
  using var doc=GltfReader.Read(input,null,extensions);
  var prepared=GltfStockMaterialAdapter.PrepareSource(doc,doc.ReadMaterial(0));
  var rows=prepared.Resources.ToArray();
  Assert.AreEqual(4,rows.Length);
  GltfMaterialLease<Texture>[] leases=null;
  try
  {
   // Scoped preload makes this an exact pixel readback control, not a claim
   // about normal streaming latency or readiness.
   TextureStreaming.ExecuteWithDisabled(()=>leases=GltfNativeTextureCache.AcquireBatch(rows.Select(x=>x.Value.Resource).ToArray()));
   var orm=Array.FindIndex(rows,x=>x.Key==GltfMaterialTextureRole.MetallicRoughness);
   var ao=Array.FindIndex(rows,x=>x.Key==GltfMaterialTextureRole.Occlusion);
   Assert.AreSame(leases[orm].Value,leases[ao].Value);
   for(int i=0;i<rows.Length;i++)
   {
    var role=rows[i].Key; var resource=rows[i].Value.Resource; var payload=Parse(resource);
    Assert.AreEqual(2048,payload.Width); Assert.AreEqual(2048,payload.Height); Assert.AreEqual(12,payload.Mips);
    Assert.AreEqual(role==GltfMaterialTextureRole.BaseColor,(payload.Flags & 0x0200)==0,"Role transfer flag changed");
    var texture=leases[i].Value; Assert.IsTrue(texture is {IsValid:true,IsError:false}); Assert.AreEqual(payload.Mips,texture.Mips);
    var timer=Stopwatch.StartNew(); int attempts=0,initial=0; Color32[] basePixels;
    do
    {
     texture.MarkUsed(2048); await Task.Delay(100); await MainThread.Wait();
     basePixels=texture.GetPixels(0); if(attempts++==0) initial=basePixels.Length;
    } while(basePixels.Length!=payload.Width*payload.Height && timer.Elapsed<TimeSpan.FromSeconds(45));
    Measurements.Add(new{compiled,role=role.ToString(),initial_pixels=initial,resident_pixels=basePixels.Length,attempts,residency_seconds=timer.Elapsed.TotalSeconds,scope="Scoped preload plus bounded MarkUsed/frame wait; not a normal-streaming timing comparison."});
    Assert.AreEqual(payload.Width*payload.Height,basePixels.Length,"Texture did not reach full base-mip residency within 45 seconds");
    int offset=resource.Length; var counts=new List<object>();
    for(int mip=0;mip<payload.Mips;mip++)
    {
     int w=Math.Max(1,payload.Width>>mip),h=Math.Max(1,payload.Height>>mip),length=checked(w*h*4); offset-=length;
     var actual=texture.GetPixels(mip); Assert.AreEqual(w*h,actual.Length,"Scoped preload must return the full mip"); long differences=0;
     for(int p=0;p<actual.Length;p++) { var value=actual[p]; int start=offset+p*4; if(value.r!=resource[start] || value.g!=resource[start+1] || value.b!=resource[start+2] || value.a!=resource[start+3]) differences++; }
     counts.Add(new{mip,pixels=actual.Length,different_pixels=differences}); Assert.AreEqual(0L,differences,"Native texture readback differs from exact compiled resource payload");
    }
    Assert.AreEqual(payload.Offset,offset);
    Measurements.Add(new{compiled,input_sha256=hash,role=role.ToString(),resource_sha256=Convert.ToHexStringLower(SHA256.HashData(resource)),mips=counts,scope="Actual native texture readback vs serialized prepared bytes; scoped preload, not normal-streaming timing or independent codec oracle."});
   }
  }
  finally { if(leases!=null) foreach(var lease in leases) lease?.Dispose(); }
 }
 static (int Width,int Height,int Mips,int Flags,int Offset) Parse(byte[] data)
 {
  Assert.IsTrue(data.Length>=28 && BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(4))==12);
  int offset=checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data)), table=checked(8+BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(8)));
  uint count=BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(12));
  Assert.IsTrue(count>0 && count<=32 && table>=16 && table+count*12<=offset);
  for(int i=0;i<count;i++) { int entry=table+i*12; if(BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(entry))!=0x41544144) continue; int info=checked(entry+4+BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(entry+4))); Assert.IsTrue(info>=0 && info+32<=offset); return(BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(info+20)),BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(info+22)),data[info+27],BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(info+2)),offset); }
  throw new InvalidOperationException("Missing texture data header");
 }
}
