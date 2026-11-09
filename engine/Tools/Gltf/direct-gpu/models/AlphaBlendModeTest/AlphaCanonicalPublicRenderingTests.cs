using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Sandbox.Engine;
using Sandbox.Gltf;
namespace Sandbox.Gltf.DirectGpu.AlphaBlendModeValidation;
[TestClass]
public sealed class AlphaCanonicalPublicRenderingTests
{
 public static string CheckoutRoot {get;set;}
 public static List<object> Measurements {get;}=new();
 [DataTestMethod]
 [DataRow("glTF",false)]
 [DataRow("glTF",true)]
 public async Task SourceMaterialModelRenderer(string variant,bool compiled)
 {
  await MainThread.Wait();var sourceDir=Path.Combine(CheckoutRoot,"Temp/gltf-reference/glTF-Sample-Assets/Models/AlphaBlendModeTest",variant);var name=variant=="glTF"?"AlphaBlendModeTest.gltf":"AlphaBlendModeTest.glb";var source=File.ReadAllBytes(Path.Combine(sourceDir,name));byte[] Resolve(string uri)=>File.ReadAllBytes(Path.Combine(sourceDir,uri));var bytes=compiled?(variant == "glTF" ? AlphaStandaloneCompilation.Compile(source, Resolve) : GltfCompiler.Compile(source, Resolve)):source;
  var temp=Path.Combine(Path.GetTempPath(),"alpha_public_capture_"+Guid.NewGuid().ToString("N"));var prefix="alpha_"+Guid.NewGuid().ToString("N");Directory.CreateDirectory(Path.Combine(temp,prefix));var relative=prefix+"/model"+(compiled?".glb_c":variant=="glTF"?".gltf":".glb");File.WriteAllBytes(Path.Combine(temp,relative),bytes);
  if(!compiled&&variant=="glTF")foreach(var dep in new[]{"AlphaBlendModeTest.bin","AlphaBlendLabels.png","MatBed_baseColor.jpg","MatBed_normal.jpg","MatBed_occlusionRoughnessMetallic.jpg"})File.Copy(Path.Combine(sourceDir,dep),Path.Combine(temp,prefix,dep));
  var aggregate=Sandbox.FileSystem.Mounted;var mount=new LocalFileSystem(temp);aggregate.Mount(mount);Model model=null;ModelRenderer renderer=null;GltfGpuTriangleGateTests.NativeCapture capture=null;
  try
  {
   model=Model.Load(relative);Assert.IsTrue(model is {IsValid:true,IsError:false}&&model.native.IsNull&&model.GltfResource is not null,"Public load must realize source-material managed model.");var materials=model.Materials;Assert.AreEqual(6,materials.Length);Assert.AreEqual(1,materials.Count(m=>m.Flags.IsTranslucent));Assert.AreEqual(3,materials.Count(m=>m.Flags.IsAlphaTest));
   var bounds=model.Bounds;var center=bounds.Center;var height=Math.Max(bounds.Size.y,bounds.Size.z)*1.2f;Assert.IsTrue(height>0&&float.IsFinite(height));var output=Path.Combine(CheckoutRoot,"Temp/gltf-validation/direct-gpu/alpha-public-render",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);capture=new GltfGpuTriangleGateTests.NativeCapture(output,center+global::Vector3.Forward*500,center,height);
   Assert.AreEqual(0,(await capture.Take("empty_before")).Coverage);
   using(capture.Scene.Push()){renderer=capture.Scene.CreateObject().Components.Create<ModelRenderer>(false);renderer.Model=model;renderer.Enabled=true;capture.Scene.EditorTick(0,0);}
   var actual=await capture.Take("public_source_material");var instance=renderer.GltfInstance;Assert.IsNotNull(instance);Assert.IsTrue(actual.Coverage>100&&instance.DrawCalls>0&&instance.CallbackErrors.Length==0,"Source geometry failed to render.");
   var rows=instance.Readback();Assert.AreEqual(9,rows.Count);Assert.IsTrue(rows.All(x=>x.IndexDataOrigin=="gpu-readback"));
   var white=Material.Load("materials/default/white.vmat");renderer.MaterialOverride=white;var overridden=await capture.Take("whole_override");Assert.IsTrue(instance.Readback().All(x=>x.SubmittedMaterialOverride&&ReferenceEquals(x.SubmittedMaterial,white)));Assert.IsFalse(actual.Pixels.SequenceEqual(overridden.Pixels));renderer.ClearMaterialOverrides();var restored=await capture.Take("source_restored");Assert.IsTrue(actual.Pixels.SequenceEqual(restored.Pixels),"Clearing whole override did not restore source pixels.");
   using(capture.Scene.Push()){renderer.GameObject.Destroy();capture.Scene.EditorTick(0,0);capture.Scene.ProcessDeletes();}renderer=null;Assert.AreEqual(0,(await capture.Take("empty_after_delete")).Coverage);
   Measurements.Add(new{variant,compiled,actual.Path,actual.Sha256,actual.Coverage,bounds=bounds.ToString(),camera="front source +Z, orthographic fit; source screenshot lighting/camera unknown",materials=materials.Select(m=>new{m.ShaderName,m.Flags.IsTranslucent,m.Flags.IsAlphaTest}).ToArray(),drawCalls=instance.DrawCalls,overridden=overridden.Path,overriddenSha256=overridden.Sha256,restored=restored.Path,restoredSha256=restored.Sha256});
  }
  finally{await MainThread.Wait();if(renderer is not null){using(capture.Scene.Push()){renderer.GameObject.Destroy();capture.Scene.EditorTick(0,0);capture.Scene.ProcessDeletes();}}capture?.Dispose();if(model is not null&&model.ResourcePath==Resource.FixPath(relative))model.Destroy();aggregate.UnMount(mount);mount.Dispose();if(Directory.Exists(temp))Directory.Delete(temp,true);}
 }
}
