using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox;
using Sandbox.Gltf;
using Assert=Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace Sandbox.Gltf.DirectGpu.MandarinOrangeValidation;

[TestClass]
internal sealed class MandarinOrangeGpuTests
{
 static string checkout;
 public static List<object> Measurements {get;}=new();
 public static void Configure(string root){checkout=root;Measurements.Clear();}
 [TestMethod]
 [DataRow(false)]
 [DataRow(true)]
 public async Task ExplicitAuthoredSceneRealGpuPositionsAndDiagnosticCapture(bool compiled)
 {
  await MainThread.Wait();
  string form=compiled ? "glTF/derived-glb_c" : "glTF/source";
  string directory=Path.Combine(checkout,"Temp/gltf-reference/glTF-Sample-Assets/Models/MandarinOrange/glTF");
  byte[] source=File.ReadAllBytes(Path.Combine(directory,"MandarinOrange.gltf"));
  byte[] Resolve(string uri)=>File.ReadAllBytes(Path.Combine(directory,uri));
  byte[] bytes=compiled ? GltfCompiler.Compile(source,Resolve) : source;
  string inputHash=Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));
  using var document=GltfModelLoader.ReadDocument(bytes,Resolve);
  string output=Path.Combine(checkout,"Temp/gltf-validation/direct-gpu/MandarinOrange/diagnostic",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);
  GltfGpuTriangleGateTests.Measurements.Clear();
  using var capture=new GltfGpuTriangleGateTests.NativeCapture(output,new global::Vector3(4,0,0),new global::Vector3(0,0,0),1.25f);
  // The source material path is a separate public acceptance test. A borrowed
  // diagnostic material isolates actual source buffers, node transforms and GPU.
  using var asset=GltfGpuAsset.Prepare(document,GltfGpuAsset.DefaultComputeShaderPath,_=>capture.Material,sceneIndex:0);
  GltfGpuInstance instance=null;
  try
  {
   Assert.AreEqual(0,(await capture.Take("empty_before")).Coverage);
   instance=asset.CreateInstance(capture.Scene.SceneWorld);
   var frame=await capture.Take("actual_scene0_diagnostic_normal");
   Assert.IsTrue(frame.Coverage>100,"Real Mandarin geometry produced no visible pixels");
   Assert.AreEqual(0,instance.CallbackErrors.Length,string.Join("\n",instance.CallbackErrors));
   Assert.IsTrue(instance.DrawCalls>0 && instance.PoseRevision==instance.SubmittedRevision,"Current pose was not drawn");
   var row=instance.Readback().Single();
   Assert.AreEqual("gpu-readback",row.IndexDataOrigin);
   Assert.AreEqual(24138,row.VertexCount);Assert.AreEqual(122508,row.Indices.Length);
   var position=row.Fields.Single(x=>x.Semantic=="POSITION");
   var binary=File.ReadAllBytes(Path.Combine(directory,"MandarinOrange.bin"));
   float maximum=0,wrongStrideError=0,wrongRotationError=0;
   for(int outputVertex=0;outputVertex<row.VertexCount;outputVertex++)
   {
    int sourceVertex=(int)row.SourceVertices[outputVertex];
    var expected=Expected(binary,sourceVertex*32,true);
    var wrongStride=Expected(binary,sourceVertex*28,true);
    var wrongRotation=Expected(binary,sourceVertex*32,false);
    for(int c=0;c<3;c++)
    {
     float actual=BitConverter.ToSingle(row.Bytes,outputVertex*row.VertexStride+position.Offset+c*4);
     maximum=Math.Max(maximum,Math.Abs(actual-expected[c]));wrongStrideError=Math.Max(wrongStrideError,Math.Abs(actual-wrongStride[c]));wrongRotationError=Math.Max(wrongRotationError,Math.Abs(actual-wrongRotation[c]));
    }
   }
   for(int i=0;i<row.Indices.Length;i++)Assert.AreEqual(BitConverter.ToUInt32(binary,772416+i*4),row.Indices[i],"Bound GPU index mismatch");
   // Mutate an actual owned bound GPU index, leaving CPU source topology intact.
   // The readback oracle must observe and reject the corruption, then recover.
   var boundDraws=(System.Collections.IEnumerable)typeof(GltfGpuInstance).GetField("draws",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(instance);
   var draw=boundDraws.Cast<object>().Single();
   var indexBuffer=(GpuBuffer)draw.GetType().GetField("Indices",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(draw);
   var corrupted=(uint[])row.Indices.Clone();corrupted[0]=(corrupted[0]+1)%(uint)row.VertexCount;
   try{indexBuffer.SetData<uint>(corrupted);var negative=instance.Readback().Single();Assert.AreEqual("gpu-readback",negative.IndexDataOrigin);Assert.AreNotEqual(BitConverter.ToUInt32(binary,772416),negative.Indices[0],"Actual bound-index corruption was hidden by CPU metadata");}
   finally{indexBuffer.SetData<uint>(row.Indices);}
   Assert.AreEqual(BitConverter.ToUInt32(binary,772416),instance.Readback().Single().Indices[0],"Bound GPU index restore failed");
   Assert.IsTrue(maximum<=.000002f,"Independent source-byte/quaternion oracle error: "+maximum);
   Assert.IsTrue(wrongStrideError>.1f && wrongRotationError>.1f,"Plausible wrong-stride/omitted-parent controls lack sensitivity");
   Assert.IsNull(document.GetDefaultScene(),"Explicit scene import mutated the source default");
   Measurements.Add(new{form,input_sha256=inputHash,output,frame.Path,frame.Sha256,frame.Coverage,selected_scene=asset.SelectedSceneIndex,vertex_count=row.VertexCount,index_count=row.Indices.Length,position_samples=row.VertexCount*3,maximum_position_error_inches=maximum,wrong_stride_error_inches=wrongStrideError,omitted_parent_rotation_error_inches=wrongRotationError,draws=instance.DrawCalls,scope="Actual source geometry GPU readback and Lit camera-mode borrowed-white diagnostic capture; borrowed white material, inferred camera. Not public source-material appearance."});
   var retained=instance;int draws=instance.DrawCalls;instance.Dispose();instance=null;using(capture.Scene.Push())capture.Scene.ProcessDeletes();
   Assert.AreEqual(0,(await capture.Take("empty_after_delete")).Coverage);Assert.AreEqual(draws,retained.DrawCalls);
  }
  finally{await MainThread.Wait();instance?.Dispose();Measurements.Add(new{form,native_captures=GltfGpuTriangleGateTests.Measurements.ToArray()});}
 }
 // Independent literal source oracle: authored parent q=(-.5,0,0,sqrt(.75))
 // is -60 degrees around X, then glTF meters -> engine (z,x,y) inches.
 internal static float[] Expected(byte[] bytes,int offset,bool parent)
 {
  double x=BitConverter.ToSingle(bytes,offset),y=BitConverter.ToSingle(bytes,offset+4),z=BitConverter.ToSingle(bytes,offset+8);
  double yy=parent ? .5*y+Math.Sqrt(.75)*z : y;
  double zz=parent ? -Math.Sqrt(.75)*y+.5*z : z;
  return new[]{(float)(zz/.0254),(float)(x/.0254),(float)(yy/.0254)};
 }
}
