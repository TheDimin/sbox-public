using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Sandbox.Gltf;
using NV4=System.Numerics.Vector4;

namespace Sandbox.Gltf.DirectGpu.NormalTangentTestValidation;
[TestClass]
public sealed class NormalCornerPublicComputeTests
{
 public static string CheckoutRoot {get;set;}
 [TestMethod]
 [DataRow(true)]
 public async Task SharedSourceVerticesRetainDistinctGeneratedCornerTangents(bool compiled)
 {
  await MainThread.Wait(); var output=Path.Combine(CheckoutRoot,"Temp/gltf-validation/direct-gpu/normal-corner-public-compute",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);
  var source=File.ReadAllText(Path.Combine(CheckoutRoot,"engine/Tools/Gltf/direct-gpu/models/NormalTangentTest/gltf_surface_five_uv.shader"));Shader shader=null;Material template=null;
  using var capture=new GltfGpuTriangleGateTests.NativeCapture(output,new global::Vector3(150,0,0),global::Vector3.Zero,120);
  try
  {
   using(var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(90))) shader=await Shader.CompileAsync(source,timeout.Token);
   await MainThread.Wait();Assert.IsTrue(shader is {IsValid:true,IsAnonymous:true});template=Material.Create("corner_control_"+Guid.NewGuid().ToString("N"),shader);
   using var doc=Fixture(compiled);using var asset=GltfGpuAsset.PrepareProjected(doc,GltfManagedModelResource.ComputeShaderPath,1024*1024,(_,_)=>template,GltfUnlitMaterialPlan.Features.None);
   using(var instance=asset.CreateInstance(capture.Scene.SceneWorld))
   {
    await capture.WaitFrames();Assert.IsTrue(instance.DrawCalls>0 && instance.CallbackErrors.Length==0,string.Join("\n",instance.CallbackErrors));
    var row=instance.Readback().Single();Assert.AreEqual(6,row.VertexCount);CollectionAssert.AreEqual(new uint[]{0,1,2,0,2,3},row.SourceVertices);CollectionAssert.AreEqual(new uint[]{0,1,2,3,4,5},row.Indices);
    var tangent=row.Fields.Single(f=>f.Semantic=="TANGENT" && f.SemanticIndex==0);
    for(var corner=0;corner<6;corner++)
    {
     int offset=corner*row.VertexStride+tangent.Offset; float F(int k)=>BinaryPrimitives.ReadSingleLittleEndian(row.Bytes.AsSpan(offset+k*4));var actual=new NV4(F(0),F(1),F(2),F(3));
     var expected=corner<3?new NV4(0,1,0,-1):new NV4(0,-1,0,1);
     Assert.IsTrue(NV4.Distance(expected,actual)<.0001f,$"GPU corner {corner} expected {expected} got {actual}; shared source 0 must preserve opposite tangent frames.");
    }
   }
  }
  finally {await MainThread.Wait();template?.Destroy();shader?.Destroy();}
 }
 private static GltfDocument Fixture(bool compiled)
 {
  var data=new byte[124];float[] p=[0,0,0, 1,0,0, 0,1,0, -1,0,0];for(int i=0;i<p.Length;i++)BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(i*4),p[i]);
  for(int i=0;i<4;i++)BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(48+i*12+8),1);
  byte[] uv=[0,0,0,0,255,0,0,0,0,255,0,0,255,0,0,0];uv.CopyTo(data,96);ushort[] indices=[0,1,2,0,2,3];for(int i=0;i<6;i++)BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(112+i*2),indices[i]);
  const string pixel="iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAABGdBTUEAAE4g3rEiDgAAAAxJREFUCNdjYOj4DwACEwGItIREUAAAAABJRU5ErkJggg==";
  var json=$$$"""
  {"asset":{"version":"2.0"},"scene":0,"scenes":[{"nodes":[0]}],"nodes":[{"mesh":0}],
   "buffers":[{"uri":"data:application/octet-stream;base64,{{{Convert.ToBase64String(data)}}}","byteLength":124}],
   "bufferViews":[{"buffer":0,"byteOffset":0,"byteLength":48},{"buffer":0,"byteOffset":48,"byteLength":48},{"buffer":0,"byteOffset":96,"byteLength":16,"byteStride":4},{"buffer":0,"byteOffset":112,"byteLength":12}],
   "accessors":[{"bufferView":0,"componentType":5126,"count":4,"type":"VEC3","min":[-1,0,0],"max":[1,1,0]},{"bufferView":1,"componentType":5126,"count":4,"type":"VEC3"},{"bufferView":2,"componentType":5121,"normalized":true,"count":4,"type":"VEC2"},{"bufferView":3,"componentType":5123,"count":6,"type":"SCALAR"}],
   "images":[{"uri":"data:image/png;base64,{{{pixel}}}"}],"textures":[{"source":0}],"materials":[{"normalTexture":{"index":0}}],
   "meshes":[{"primitives":[{"attributes":{"POSITION":0,"NORMAL":1,"TEXCOORD_0":2},"indices":3,"material":0}]}]}
  """;
  var source=Encoding.UTF8.GetBytes(json);return GltfReader.Read(compiled?GltfCompiler.Compile(source):source);
 }
}
