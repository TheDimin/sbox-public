using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox;
using Sandbox.Gltf;
using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace Sandbox.Gltf.DirectGpu.SimpleSparseAccessorValidation;

[TestClass]
internal sealed class CompilerDenseGpuTests
{
 public static string CheckoutRoot { get; set; }
 public static List<object> Measurements { get; } = new();
 [TestMethod]
 [DataRow(0)] [DataRow(1)] [DataRow(2)]
 public async Task SourcePackagedAndDenseHaveIndependentGpuPositions(int form)
 {
  await MainThread.Wait();
  string dir=Path.Combine(CheckoutRoot,"Temp/gltf-reference/glTF-Sample-Assets/Models/SimpleSparseAccessor/glTF");
  byte[] source=File.ReadAllBytes(Path.Combine(dir,"SimpleSparseAccessor.gltf"));
  byte[] binary=File.ReadAllBytes(Path.Combine(dir,"SimpleSparseAccessor.bin"));
  byte[] Resolve(string uri)=>File.ReadAllBytes(Path.Combine(dir,uri));
  byte[] input=form==0?source:form==1?GltfCompiler.Compile(source,Resolve):GltfCompiler.CompileWithDenseAccessors(source,Resolve);
  var timer=Stopwatch.StartNew();
  using var document=GltfModelLoader.ReadDocument(input,Resolve);
  double readMs=timer.Elapsed.TotalMilliseconds;
  var stream=document.MaterializePrimitiveUpload(0,0).Attributes["POSITION"];
  Assert.AreEqual(form!=2,stream.IsMaterialized);
  string output=Path.Combine(CheckoutRoot,"Temp/gltf-validation/direct-gpu/SimpleSparseAccessor",Guid.NewGuid().ToString("N"));
  Directory.CreateDirectory(output);
  using var capture=new GltfGpuTriangleGateTests.NativeCapture(output,new global::Vector3(400,118,79),new global::Vector3(0,118,79),240);
  timer.Restart();
  using var asset=GltfGpuAsset.Prepare(document,GltfGpuAsset.DefaultComputeShaderPath,_=>capture.Material);
  double prepareMs=timer.Elapsed.TotalMilliseconds;
  timer.Restart();
  using var instance=asset.CreateInstance(capture.Scene.SceneWorld);
  double instanceMs=timer.Elapsed.TotalMilliseconds;
  timer.Restart();
  while(instance.DrawCalls==0 && timer.Elapsed<TimeSpan.FromSeconds(10)) { await Task.Delay(10); await MainThread.Wait(); }
  double firstDrawObservedMs=timer.Elapsed.TotalMilliseconds;
  var frame=await capture.Take("actual");
  Assert.IsTrue(frame.Coverage>0,"No rasterized geometry");
  Assert.AreEqual(0,instance.CallbackErrors.Length,string.Join("\n",instance.CallbackErrors));
  Assert.IsTrue(instance.DrawCalls>0 && instance.PoseRevision==instance.SubmittedRevision);
  var row=instance.Readback().Single();
  Assert.AreEqual("gpu-readback",row.IndexDataOrigin);
  var pos=row.Fields.Single(x=>x.Semantic=="POSITION");
  float max=0;
  for(int vertex=0;vertex<row.VertexCount;vertex++)
  {
   int index=(int)row.SourceVertices[vertex], offset=72+index*12;
   for(int s=0;s<3;s++)if(BitConverter.ToUInt16(binary,240+s*2)==index)offset=248+s*12;
   float x=BitConverter.ToSingle(binary,offset),y=BitConverter.ToSingle(binary,offset+4),z=BitConverter.ToSingle(binary,offset+8);
   float[] expected={z/.0254f,x/.0254f,y/.0254f};
   for(int c=0;c<3;c++)max=Math.Max(max,Math.Abs(expected[c]-BitConverter.ToSingle(row.Bytes,vertex*row.VertexStride+pos.Offset+c*4)));
  }
  Assert.IsTrue(max<.001f,"GPU positions differ from independent sparse byte oracle: "+max);
  Measurements.Add(new{form,input_bytes=input.Length,input_sha256=Convert.ToHexStringLower(SHA256.HashData(input)),frame.Path,frame.Coverage,maximum_position_error_inches=max,position_materialized=stream.IsMaterialized,read_ms=readMs,prepare_ms=prepareMs,instance_ms=instanceMs,first_draw_observed_ms=firstDrawObservedMs,scope="Actual GPU readback and diagnostic material frame, independent sparse source bytes; single warm sequential samples, first draw is CPU polling observation including scheduling, not GPU timing"});
 }
}
