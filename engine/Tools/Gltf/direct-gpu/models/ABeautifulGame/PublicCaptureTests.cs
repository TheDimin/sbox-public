using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox;
using Sandbox.Gltf;
using Assert=Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace Sandbox.Gltf.DirectGpu.ABeautifulGameValidation;
[TestClass]
internal sealed class PublicCaptureTests
{
 static string checkout;
 public static List<object> Measurements {get;}=new();
 public static void Configure(string root){checkout=root;Measurements.Clear();}
 [TestMethod]
 [DataRow("glTF", false)]
 [DataRow("glTF", true)]
 [DataRow("glTF-Binary", false)]
 [DataRow("glTF-Binary", true)]
 public async Task PublicSourceMaterialAndGpu(string variant,bool compiled)
 {
  await MainThread.Wait(); string form=variant+(compiled?"/compiled_glb_c":"/source");
  string sourceDirectory=Path.Combine(checkout,"Temp/gltf-reference/glTF-Sample-Assets/Models/ABeautifulGame",variant);
  string sourceFile=Path.Combine(sourceDirectory,"ABeautifulGame"+(variant=="glTF"?".gltf":".glb"));
  byte[] source=File.ReadAllBytes(sourceFile);byte[] Resolve(string uri)=>File.ReadAllBytes(Path.Combine(sourceDirectory,uri));
  byte[] input=compiled?GltfCompiler.Compile(source,Resolve,new HashSet<string>{"KHR_draco_mesh_compression","KHR_texture_basisu"}):source;
  string inputHash=Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(input));
  string output=Path.Combine(checkout,"Temp/gltf-validation/direct-gpu/ABeautifulGame/public",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);
  string directory=Path.Combine(Path.GetTempPath(),"gltf_beautiful_public_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
  LocalFileSystem mount=null;Model model=null;ModelRenderer renderer=null;GltfGpuTriangleGateTests.Measurements.Clear();
  using var capture=new GltfGpuTriangleGateTests.NativeCapture(output,new global::Vector3(55,-60,35),new global::Vector3(0,0,2),38);
  try
  {
   if(variant=="glTF") foreach(var path in Directory.GetFiles(sourceDirectory))File.Copy(path,Path.Combine(directory,Path.GetFileName(path)));
   string name="beautiful_public_"+Guid.NewGuid().ToString("N")+(compiled?".glb_c":variant=="glTF"?".gltf":".glb");File.WriteAllBytes(Path.Combine(directory,name),input);
   mount=new LocalFileSystem(directory);FileSystem.Mounted.Mount(mount);model=Model.Load(name);
   Assert.IsTrue(model is {IsValid:true,IsError:false} && model.native.IsNull && model.GltfResource is not null && model.MeshCount==49,"Public managed scene must contain 49 node instances");
   var materials=model.Materials;Assert.AreEqual(15,materials.Length);Assert.IsTrue(materials.All(m=>m is {IsValid:true,IsError:false}),"Every authored material must realize");
   Assert.AreEqual(0,(await capture.Take("empty_before")).Coverage);
   using(capture.Scene.Push()){renderer=capture.Scene.CreateObject().Components.Create<ModelRenderer>(false);renderer.Model=model;renderer.Enabled=true;capture.Scene.EditorTick(0,0);}
   var instance=renderer.GltfInstance;Assert.IsNotNull(instance);Assert.IsTrue(renderer.SceneObject is SceneCustomObject && renderer.SceneObject.Model==null);
   var actual=await capture.Take("actual_public_source_material");Assert.IsTrue(actual.Coverage>100,"Public source-material view is empty");
   Assert.AreEqual(0,instance.CallbackErrors.Length,string.Join("\n",instance.CallbackErrors));Assert.IsTrue(instance.DrawCalls>0 && instance.SubmittedRevision==instance.PoseRevision);
   var rows=instance.Readback();Assert.AreEqual(49,rows.Count);Assert.IsTrue(rows.All(r=>r.IndexDataOrigin=="gpu-readback" && r.Indices.Length>0 && r.Fields.Any(f=>f.Semantic=="POSITION") && r.Fields.Any(f=>f.Semantic=="NORMAL") && r.Fields.Any(f=>f.Semantic=="TEXCOORD")),"GPU geometry/indices/normal/UV missing");
   using(capture.Scene.Push())renderer.MaterialOverride=capture.Material;
   var white=await capture.Take("white_override_negative");Assert.IsTrue(white.Coverage>100);int changed=actual.Pixels.Zip(white.Pixels,(a,b)=>Math.Abs(a.r-b.r)>.02f||Math.Abs(a.g-b.g)>.02f||Math.Abs(a.b-b.b)>.02f).Count(x=>x);Assert.IsTrue(changed>100,"Source material must affect visible output");
   using(capture.Scene.Push())renderer.MaterialOverride=null;var restored=await capture.Take("restored_source_material");Assert.IsTrue(restored.Coverage>100);
   Measurements.Add(new{form,input_sha256=inputHash,path=actual.Path,sha256=actual.Sha256,actual.Coverage,public_resource=model.ResourcePath,source_material_shader_names=materials.Select(m=>m.ShaderName).ToArray(),draw_rows=rows.Count,white_negative_changed_pixels=changed,restored_path=restored.Path,scope="Actual public Model.Load, ModelRenderer, source materials and 49 GPU row/index readbacks. Camera/light inferred; optional transmission/volume fallback. No independent sampler, color, lighting or decoded-texel fidelity claim."});
   var retained=instance;using(capture.Scene.Push()){renderer.GameObject.Destroy();capture.Scene.EditorTick(0,0);capture.Scene.ProcessDeletes();}renderer=null;int calls=retained.DrawCalls;Assert.AreEqual(0,(await capture.Take("empty_after_delete")).Coverage);Assert.AreEqual(calls,retained.DrawCalls);
  }
  finally
  {
   await MainThread.Wait();try{if(renderer!=null){using var scope=capture.Scene.Push();renderer.GameObject.Destroy();capture.Scene.EditorTick(0,0);capture.Scene.ProcessDeletes();}model?.Destroy();}finally{try{if(mount!=null)FileSystem.Mounted.UnMount(mount);}finally{mount?.Dispose();Directory.Delete(directory,true);}}
   Measurements.Add(new{form,native_captures=GltfGpuTriangleGateTests.Measurements.ToArray()});
  }
 }
}
