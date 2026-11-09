using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;

namespace Sandbox.Gltf.DirectGpu.SimpleSparseAccessorValidation;

[TestClass]
internal sealed class PublicSparseTests
{
 public static string CheckoutRoot { get; set; }
 public static List<object> Measurements { get; } = new();
 // Literal expectations independently transcribed from the pinned asset, not the reader.
 static readonly uint[] Indices={0,8,7,0,1,8,1,9,8,1,2,9,2,10,9,2,3,10,3,11,10,3,4,11,4,12,11,4,5,12,5,13,12,5,6,13};
 static readonly float[,] Positions={{0,0,0},{1,0,0},{2,0,0},{3,0,0},{4,0,0},{5,0,0},{6,0,0},{0,1,0},{1,2,0},{2,1,0},{3,3,0},{4,1,0},{5,4,0},{6,1,0}};
 [TestMethod] [DataRow(false)] [DataRow(true)]
 public async Task PublicSourceAndCanonicalCompiled(bool compiled)
 {
  await MainThread.Wait();
  var dir=Path.Combine(CheckoutRoot,"Temp/gltf-reference/glTF-Sample-Assets/Models/SimpleSparseAccessor/glTF");
  var source=File.ReadAllBytes(Path.Combine(dir,"SimpleSparseAccessor.gltf"));
  byte[] Resolve(string uri)=>File.ReadAllBytes(Path.Combine(dir,uri));
  var bytes=compiled?GltfCompiler.Compile(source,Resolve):source;
  var output=Path.Combine(CheckoutRoot,"Temp/gltf-validation/direct-gpu/SimpleSparseAccessor/public-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(output);
  var mountDir=Path.Combine(Path.GetTempPath(),"sparse_public_"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(mountDir);
  string path="sparse_"+Guid.NewGuid().ToString("N")+(compiled?".glb_c":".gltf");
  File.WriteAllBytes(Path.Combine(mountDir,path),bytes); if(!compiled)File.WriteAllBytes(Path.Combine(mountDir,"SimpleSparseAccessor.bin"),Resolve("SimpleSparseAccessor.bin"));
  var fs=FileSystem.Mounted; var mount=new LocalFileSystem(mountDir); Model model=null; ModelRenderer renderer=null;
  using var capture=new GltfGpuTriangleGateTests.NativeCapture(output,new Vector3(400,118,79),new Vector3(0,118,79),240);
  try
  {
   fs.Mount(mount); model=Model.Load(path);
   Assert.IsTrue(model is {IsValid:true,IsError:false} && model.native.IsNull && model.GltfResource is not null,"Public managed model load failed");
   var empty=await capture.Take("empty_before"); Assert.AreEqual(0,empty.Coverage);
   using(capture.Scene.Push()){renderer=capture.Scene.CreateObject().Components.Create<ModelRenderer>(false);renderer.WorldTransform=Transform.Zero;renderer.Model=model;renderer.Enabled=true;capture.Scene.EditorTick(0,0);}
   var instance=renderer.GltfInstance; Assert.IsTrue(instance is not null && renderer.SceneObject is SceneCustomObject && renderer.SceneObject.Model is null);
   var frame=await capture.Take("actual_public_source_material"); Assert.IsTrue(frame.Coverage>64,"No visible geometry");
   Assert.IsTrue(instance.DrawCalls>0 && instance.CallbackErrors.Length==0 && instance.PoseRevision==instance.SubmittedRevision);
   var row=instance.Readback().Single(); Assert.AreEqual("gpu-readback",row.IndexDataOrigin); Assert.AreEqual(36,row.Indices.Length);
   var actual=row.Indices.Select(i=>row.SourceVertices[i]).ToArray(); CollectionAssert.AreEqual(Indices,actual);
   var field=row.Fields.Single(x=>x.Semantic=="POSITION"); float max=0;
   for(int v=0;v<row.VertexCount;v++) { int i=(int)row.SourceVertices[v]; Assert.IsTrue(i>=0&&i<14); float[] expected={Positions[i,2]/.0254f,Positions[i,0]/.0254f,Positions[i,1]/.0254f}; for(int c=0;c<3;c++)max=Math.Max(max,Math.Abs(expected[c]-BitConverter.ToSingle(row.Bytes,v*row.VertexStride+field.Offset+c*4))); }
   Assert.IsTrue(max<.001f,"Sparse positions mismatch: "+max);
   using(capture.Scene.Push()){renderer.GameObject.Destroy();capture.Scene.EditorTick(0,0);capture.Scene.ProcessDeletes();} renderer=null; var stopped=instance.DrawCalls;
   var deleted=await capture.Take("empty_after"); Assert.AreEqual(0,deleted.Coverage); Assert.AreEqual(stopped,instance.DrawCalls);
   Measurements.Add(new{form=compiled?"canonical-glb_c":"gltf-source",input_sha256=Convert.ToHexStringLower(SHA256.HashData(bytes)),frame.Path,frame.Sha256,frame.Coverage,frame.CameraStateJson,max_position_error_inches=max,indices=actual,index_origin=row.IndexDataOrigin,scope="Public Model.Load and ordinary ModelRenderer, source default material, actual GPU positions and indices, empty/deletion controls; not independent material parity or user visual approval"});
  }
  finally { await MainThread.Wait(); if(renderer is not null){using(capture.Scene.Push()){renderer.GameObject.Destroy();capture.Scene.EditorTick(0,0);capture.Scene.ProcessDeletes();}} if(model is not null && model.ResourcePath==Resource.FixPath(path))model.Destroy(); fs.UnMount(mount);mount.Dispose();Directory.Delete(mountDir,true); }
 }
}
