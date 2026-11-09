using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox;
using Sandbox.Gltf;
using Assert=Microsoft.VisualStudio.TestTools.UnitTesting.Assert;
namespace Sandbox.Gltf.DirectGpu.SimpleSparseAccessorValidation;
[TestClass]
internal sealed class InputArenaGpuTests
{
 public static string CheckoutRoot {get;set;}
 public static List<object> Measurements {get;}=new();
 [TestMethod]
 [DataRow("SimpleSkin")]
 [DataRow("AnimatedMorphCube")]
 public async Task AnimatedPoseGpuReadback(string model)
 {
  await MainThread.Wait();
  string dir=Path.Combine(CheckoutRoot,"Temp/gltf-reference/glTF-Sample-Assets/Models",model,"glTF");
  byte[] source=File.ReadAllBytes(Path.Combine(dir,model+".gltf"));
  using var document=GltfModelLoader.ReadDocument(source,uri=>File.ReadAllBytes(Path.Combine(dir,uri)));
  string output=Path.Combine(CheckoutRoot,"Temp/gltf-validation/direct-gpu/SimpleSparseAccessor",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);
  using var capture=new GltfGpuTriangleGateTests.NativeCapture(output);
  using var asset=GltfGpuAsset.Prepare(document,GltfGpuAsset.DefaultComputeShaderPath,_=>capture.Material);
  using var instance=asset.CreateInstance(capture.Scene.SceneWorld);
  instance.SetPose(document.EvaluatePose(0,.5f));
  await capture.WaitFrames();
  Assert.AreEqual(0,instance.CallbackErrors.Length,string.Join("\n",instance.CallbackErrors));
  Assert.IsTrue(instance.DrawCalls>0 && instance.PoseRevision==instance.SubmittedRevision,"Animated pose was not submitted");
  var rows=instance.Readback();Assert.IsTrue(rows.Count>0);
  foreach(var row in rows)
  {
   Assert.AreEqual("gpu-readback",row.IndexDataOrigin);
   Measurements.Add(new{model,row.Node,row.Mesh,row.Primitive,row.VertexCount,row.VertexStride,vertex_sha256=Convert.ToHexStringLower(SHA256.HashData(row.Bytes)),index_sha256=Convert.ToHexStringLower(SHA256.HashData(System.Runtime.InteropServices.MemoryMarshal.AsBytes(row.Indices.AsSpan()))),row.Indices.Length,scope="GPU output at animation 0.5 seconds; compare same fixture and pose across producer stages"});
  }
 }
}
