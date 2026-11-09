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
internal sealed class MandarinOrangePublicCaptureTests
{
 static string checkout;
 public static List<object> Measurements {get;}=new();
 public static void Configure(string root){checkout=root;Measurements.Clear();}
 [TestMethod]
 [DataRow(false)]
 [DataRow(true)]
 public async Task ActualSelectedSourceMaterialThroughPublicModelRenderer(bool compiled)
 {
  await MainThread.Wait();
  string form=compiled ? "glTF/derived-glb_c" : "glTF/source";
  string sourceDirectory=Path.Combine(checkout,"Temp/gltf-reference/glTF-Sample-Assets/Models/MandarinOrange/glTF");
  byte[] source=File.ReadAllBytes(Path.Combine(sourceDirectory,"MandarinOrange.gltf"));
  byte[] Resolve(string uri)=>File.ReadAllBytes(Path.Combine(sourceDirectory,uri));
  byte[] input=compiled ? GltfCompiler.Compile(source,Resolve) : source;
  string inputHash=Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(input));
  string output=Path.Combine(checkout,"Temp/gltf-validation/direct-gpu/MandarinOrange/public",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);
  string directory=Path.Combine(Path.GetTempPath(),"gltf_mandarin_public_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
  LocalFileSystem mount=null;Model model=null;ModelRenderer renderer=null;
  GltfGpuTriangleGateTests.Measurements.Clear();
  using var capture=new GltfGpuTriangleGateTests.NativeCapture(output,new global::Vector3(4,0,0),global::Vector3.Zero,1.25f);
  try
  {
   foreach(var path in Directory.GetFiles(sourceDirectory))File.Copy(path,Path.Combine(directory,Path.GetFileName(path)));
   string name="mandarin_public_"+Guid.NewGuid().ToString("N")+(compiled ? ".glb_c" : ".gltf");File.WriteAllBytes(Path.Combine(directory,name),input);
   mount=new LocalFileSystem(directory);FileSystem.Mounted.Mount(mount);
   // This must remain the actual public API. No stripping source material fields,
   // borrowed shader resolver or diagnostic asset constructor is permitted here.
   model=Model.Load(name,0);
   Assert.IsTrue(model.IsValid && !model.IsError && model.native.IsNull && model.GltfSelectedScene==0 && model.GltfDefaultScene==null && model.MeshCount==1);
   Assert.AreEqual(0,(await capture.Take("empty_before")).Coverage);
   using(capture.Scene.Push()){renderer=capture.Scene.CreateObject().Components.Create<ModelRenderer>(false);renderer.Model=model;renderer.Enabled=true;capture.Scene.EditorTick(0,0);}
   var instance=renderer.GltfInstance;Assert.IsNotNull(instance);Assert.IsTrue(renderer.SceneObject is SceneCustomObject && renderer.SceneObject.Model==null);
   // Instance creation must realize its own materials; no getter is called first.
   Assert.IsTrue(model.GltfResource.MaterialSlotSourceIndices.SequenceEqual(new int?[]{0}) && model.Materials.Length==1 && model.Materials[0].IsValid && !model.Materials[0].IsError,"Actual authored material slot must be realized");
   var actual=await capture.Take("actual_public_source_material_scene0");
   Assert.IsTrue(actual.Coverage>100,"Public source-material view is empty");
   Assert.IsTrue(actual.Pixels.Count(p=>Math.Abs(p.r-p.g)>.02f || Math.Abs(p.g-p.b)>.02f || Math.Abs(p.r-p.b)>.02f)>100,"Source base-color texture produced no chromatic foreground");
   Assert.AreEqual(0,instance.CallbackErrors.Length,string.Join("\n",instance.CallbackErrors));Assert.IsTrue(instance.DrawCalls>0 && instance.SubmittedRevision==instance.PoseRevision);
   var row=instance.Readback().Single();Assert.AreEqual("gpu-readback",row.IndexDataOrigin);Assert.AreEqual(122508,row.Indices.Length);Assert.AreEqual(row.VertexCount,row.SourceVertices.Length);
   var position=row.Fields.Single(x=>x.Semantic=="POSITION");var normal=row.Fields.Single(x=>x.Semantic=="NORMAL");var tangent=row.Fields.Single(x=>x.Semantic=="TANGENT");
   byte[] binary=File.ReadAllBytes(Path.Combine(sourceDirectory,"MandarinOrange.bin"));float maxPosition=0,maxNormal=0,maxOrthogonality=0;
   for(int i=0;i<row.VertexCount;i++)
   {
    int v=(int)row.SourceVertices[i];Assert.IsTrue(v>=0 && v<24138);var p=MandarinOrangeGpuTests.Expected(binary,v*32,true);var n=MandarinOrangeGpuTests.Expected(binary,v*32+12,true);double length=Math.Sqrt(n.Sum(x=>(double)x*x));Assert.IsTrue(length>0);
    var actualNormal=new float[3];var actualTangent=new float[3];
    for(int c=0;c<3;c++){float pp=BitConverter.ToSingle(row.Bytes,i*row.VertexStride+position.Offset+c*4);actualNormal[c]=BitConverter.ToSingle(row.Bytes,i*row.VertexStride+normal.Offset+c*4);actualTangent[c]=BitConverter.ToSingle(row.Bytes,i*row.VertexStride+tangent.Offset+c*4);Assert.IsTrue(float.IsFinite(pp) && float.IsFinite(actualNormal[c]) && float.IsFinite(actualTangent[c]));maxPosition=Math.Max(maxPosition,Math.Abs(pp-p[c]));maxNormal=Math.Max(maxNormal,Math.Abs(actualNormal[c]-(float)(n[c]/length)));}
    maxOrthogonality=Math.Max(maxOrthogonality,Math.Abs(actualNormal.Zip(actualTangent,(a,b)=>a*b).Sum()));Assert.IsTrue(Math.Abs(actualTangent.Sum(x=>x*x)-1)<.00002f);float handedness=BitConverter.ToSingle(row.Bytes,i*row.VertexStride+tangent.Offset+12);Assert.IsTrue(handedness==1 || handedness==-1);
   }
   for(int i=0;i<row.Indices.Length;i++)Assert.AreEqual(BitConverter.ToUInt32(binary,772416+i*4),row.SourceVertices[(int)row.Indices[i]],"Public submitted topology differs from original source");
   Assert.IsTrue(maxPosition<=.000002f && maxNormal<=.000002f && maxOrthogonality<=.00002f,"Public independent geometry/normal/tangent-frame bounds failed");
   // A same-camera material-only negative must visibly remove the source color.
   using(capture.Scene.Push())renderer.MaterialOverride=capture.Material;
   var white=await capture.Take("borrowed_white_material_negative");Assert.IsTrue(white.Coverage>100);
   int changed=actual.Pixels.Zip(white.Pixels,(a,b)=>Math.Abs(a.r-b.r)>.02f || Math.Abs(a.g-b.g)>.02f || Math.Abs(a.b-b.b)>.02f).Count(x=>x);Assert.IsTrue(changed>100,"Public source-material capture is indistinguishable from the borrowed white override");
   using(capture.Scene.Push())renderer.MaterialOverride=null;
   var restored=await capture.Take("restored_public_source_material");Assert.IsTrue(restored.Coverage>100);Assert.AreEqual(0,instance.CallbackErrors.Length);
   Measurements.Add(new{form,input_sha256=inputHash,output,path=actual.Path,sha256=actual.Sha256,actual.Coverage,public_resource=model.ResourcePath,material_realization="ordinary renderer lazy instance; getter read only after creation",source_material_shader_names=model.Materials.Select(m=>m.ShaderName).ToArray(),compute_shader=model.GltfResource.Asset.ComputeMaterial?.ShaderName,selected_scene=model.GltfSelectedScene,authored_default=model.GltfDefaultScene,vertex_count=row.VertexCount,source_vertex_count=24138,index_count=row.Indices.Length,maximum_position_error_inches=maxPosition,maximum_normal_error=maxNormal,maximum_normal_tangent_dot=maxOrthogonality,white_material_negative_changed_pixels=changed,restored_path=restored.Path,scope="Actual public Model.Load(path,0), ordinary ModelRenderer, original core material textures and GPU readbacks. Optional diffuse transmission fallback; inferred camera/light/exposure, no calibrated pixel oracle or user visual approval."});
   var retained=instance;using(capture.Scene.Push()){renderer.GameObject.Destroy();capture.Scene.EditorTick(0,0);capture.Scene.ProcessDeletes();}renderer=null;int calls=retained.DrawCalls;Assert.AreEqual(0,(await capture.Take("empty_after_public_delete")).Coverage);Assert.AreEqual(calls,retained.DrawCalls);Assert.IsTrue(model.IsValid);
  }
  finally
  {
   await MainThread.Wait();try{if(renderer!=null){using var scope=capture.Scene.Push();renderer.GameObject.Destroy();capture.Scene.EditorTick(0,0);capture.Scene.ProcessDeletes();}model?.Destroy();}finally{try{if(mount!=null)FileSystem.Mounted.UnMount(mount);}finally{mount?.Dispose();Directory.Delete(directory,true);}}
   Measurements.Add(new{form,native_captures=GltfGpuTriangleGateTests.Measurements.ToArray()});
  }
 }
}
