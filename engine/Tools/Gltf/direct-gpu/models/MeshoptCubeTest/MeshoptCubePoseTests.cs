using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Sandbox.Gltf.DirectGpu.MeshoptCubeValidation;
using static MeshoptCubeReference;

/// <summary>Target only previously unexecuted owned-generation poses and actual GPU index negatives.</summary>
[TestClass]
internal sealed class MeshoptCubePoseTests
{
 static string checkout;
 public static List<object> Measurements {get;}=new();
 public static void Configure(string root){checkout=root;Measurements.Clear();}
 [TestMethod]
 [DataRow("glTF-Meshopt/compiled_glb_c")]
 public Task OwnedPublicResourcePosesAndGpuIndexNegatives(string form)=>Run(checkout,form,false);
 internal static async Task Run(string root,string formName,bool smoke)
 {
  await MainThread.Wait();var reference=new MeshoptCubeReference(root);var form=reference.Forms.Single(f=>f.Name==formName);
  var output=Path.Combine(root,"Temp/gltf-validation/direct-gpu/MeshoptCubeTest/poses",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);
  var directory=Path.Combine(Path.GetTempPath(),"meshopt_pose_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
  LocalFileSystem mount=null;Model model=null;ModelRenderer renderer=null;
  using var capture=new GltfGpuTriangleGateTests.NativeCapture(output,new global::Vector3(1600,250,370),new global::Vector3(0,250,370),1100);capture.Camera.ZFar=3000;
  try
  {
   foreach(var file in Directory.EnumerateFiles(Path.Combine(root,"Temp/gltf-reference/glTF-Sample-Assets/Models/MeshoptCubeTest",form.Variant)))
    if(Path.GetExtension(file)==".png" || (!form.Compiled&&Path.GetExtension(file)==".bin"))File.Copy(file,Path.Combine(directory,Path.GetFileName(file)));
   var name="meshopt_pose_"+Guid.NewGuid().ToString("N")+(form.Compiled?".glb_c":".gltf");File.WriteAllBytes(Path.Combine(directory,name),form.Bytes);
   mount=new LocalFileSystem(directory);FileSystem.Mounted.Mount(mount);model=Model.Load(name);
   Require(model is {IsValid:true,IsError:false}&&model.native.IsNull&&model.GltfResource is not null,"Pose target must be actual public managed resource.");
   using(capture.Scene.Push()){renderer=capture.Scene.CreateObject().Components.Create<ModelRenderer>(false);renderer.Model=model;renderer.Enabled=true;capture.Scene.EditorTick(0,0);}
   var instance=renderer.GltfInstance;Require(instance is not null&&renderer.SceneObject is SceneCustomObject&&renderer.SceneObject.Model is null,"Pose target must own buffer renderer.");
   // Borrow the exact public resource generation. Never substitute the independently
   // parsed reference document, and never weaken the backend ownership guard.
   var document=model.GltfResource.Asset.Document;
   var times=smoke?new[]{.375f}:new[]{.375f,1,1.625f,2,2.25f};
   foreach(var time in times)
   {
    instance.SetPose(document.EvaluatePose(0,time));await capture.WaitFrames();
    Require(instance.PoseRevision==instance.SubmittedRevision&&instance.DrawCalls>0&&instance.CallbackErrors.Length==0,"Owned public pose did not submit.");
    var rows=instance.Readback();
    var payload=System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(rows.Select(r=>new{r.Node,r.Mesh,r.Primitive,r.VertexCount,r.VertexStride,r.Fields,r.Bytes,r.SourceVertices,r.Indices,r.IndexDataOrigin}).ToArray());
    var path=Path.Combine(output,"gpu_pose_"+time.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+".json");File.WriteAllBytes(path,payload);
    Measurements.Add(new{form=formName,source_time=time,actual_gpu_readback=path,sha256=Hash(payload),scope="Retained actual posed GPU bytes before independent assertions; not an acceptance pass."});
    var comparison=MeshoptCubeGpuChecks.AssertReadback(reference,rows,time);
    Measurements.Add(new{form=formName,source_time=time,comparison,actual_gpu_readback=path,sha256=Hash(payload),instance.PoseRevision,instance.SubmittedRevision,instance.DrawCalls,smoke});
   }
   if(!smoke)
   {
    var draws=(System.Collections.IEnumerable)typeof(GltfGpuInstance).GetField("draws",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(instance);
    var draw=draws.Cast<object>().First();var indices=(GpuBuffer)draw.GetType().GetField("Indices",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(draw);
    var original=new uint[reference.Indices(0).Length];indices.GetData<uint>(original);
    foreach(var control in new[]{"reversed_winding","changed_index"})
    {
     var revision=instance.PoseRevision;
     try
     {
      var bad=original.ToArray();if(control=="reversed_winding")(bad[0],bad[1])=(bad[1],bad[0]);else bad[0]=original[2];
      indices.SetData<uint>(bad);await capture.WaitFrames();var rows=instance.Readback();var label=rows.Single(r=>r.Node==0);
      Require(label.IndexDataOrigin=="gpu-readback"&&label.Indices.SequenceEqual(bad)&&instance.PoseRevision==revision,"GPU negative must change exactly the bound index data.");
      bool rejected=false;try{MeshoptCubeGpuChecks.AssertReadback(reference,rows,2.25f);}catch(InvalidOperationException){rejected=true;}Require(rejected,"Checker accepted actual wrong winding/index.");
      Measurements.Add(new{form=formName,actual_gpu_negative=control,original,actual=label.Indices,label.IndexDataOrigin,rejected,revision});
     }
     finally{indices.SetData<uint>(original);await capture.WaitFrames();}
     MeshoptCubeGpuChecks.AssertReadback(reference,instance.Readback(),2.25f);
    }
   }
   var retained=instance;Destroy();int calls=retained.DrawCalls;await capture.WaitFrames();Require(retained.DrawCalls==calls,"Destroyed pose renderer kept drawing.");
   if(!smoke)Require((await capture.Take("empty_after_pose_renderer_delete")).Coverage==0,"Deleted pose renderer retained pixels.");
   string canonical=null;
   if(!smoke)
   {
    Require(formName=="glTF-Meshopt/compiled_glb_c","Exactly one compiled canonical input is retained for this model.");
    var package=Path.Combine(root,"Temp/gltf-validation/direct-gpu/MeshoptCubeTest/canonical");Directory.CreateDirectory(package);canonical=Path.Combine(package,"MeshoptCubeTest.glb_c");File.WriteAllBytes(canonical,form.Bytes);
    foreach(var file in Directory.EnumerateFiles(directory,"*.png"))File.Copy(file,Path.Combine(package,Path.GetFileName(file)),true);
   }
   Measurements.Add(new{form=formName,completed=true,smoke,pose_samples=times.Length,resource=model.ResourcePath,input_sha256=Hash(form.Bytes),canonical_compiled_path=canonical,output,cleanup_draw_stopped=true,scope="Targeted public-resource-owned poses and independent actual GPU readbacks; no repeated rest image gate. Two real index negatives on full runs. No public playback claim from seeks."});
  }
  finally
  {
   await MainThread.Wait();try{Destroy();model?.Destroy();}finally{try{if(mount!=null)FileSystem.Mounted.UnMount(mount);}finally{mount?.Dispose();Directory.Delete(directory,true);}}
  }
  void Destroy(){if(renderer==null)return;using var scope=capture.Scene.Push();renderer.GameObject.Destroy();capture.Scene.EditorTick(0,0);capture.Scene.ProcessDeletes();renderer=null;}
 }
}
[TestClass]
internal sealed class MeshoptCubePoseSmokeTests
{
 static string checkout;
 public static List<object> Measurements=>MeshoptCubePoseTests.Measurements;
 public static void Configure(string root){checkout=root;MeshoptCubePoseTests.Configure(root);}
 [TestMethod]public Task PublicOwnedGenerationMidpointSmoke()=>MeshoptCubePoseTests.Run(checkout,"glTF/source",true);
}
