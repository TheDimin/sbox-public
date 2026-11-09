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
namespace Sandbox.Gltf.DirectGpu.CompilerImagesValidation;
[TestClass]
internal sealed class CompiledOnlyImageTests
{
 public static string CheckoutRoot {get;set;}
 public static List<object> Measurements {get;}=new();
 [TestMethod]
 public async Task RelocatedCompiledOnlyPublicLoadAndSourceMaterialDraw()
 {
  await MainThread.Wait();
  string sourceDirectory=Path.Combine(CheckoutRoot,"Temp/gltf-reference/glTF-Sample-Assets/Models/BoxTextured/glTF");
  byte[] source=File.ReadAllBytes(Path.Combine(sourceDirectory,"BoxTextured.gltf"));
  byte[] image=File.ReadAllBytes(Path.Combine(sourceDirectory,"CesiumLogoFlat.png"));
  byte[] Resolve(string uri)=>File.ReadAllBytes(Path.Combine(sourceDirectory,uri));
  byte[] compiled=GltfCompiler.CompileWithEmbeddedImages(source,Resolve,GltfModelLoader.SupportedExtensions);
  using(var document=GltfModelLoader.ReadDocument(compiled,_=>throw new InvalidOperationException("Compiled GLB resolved an external file")))
  {
   CollectionAssert.AreEqual(image,document.ReadImage(0).Bytes);
   Assert.IsFalse(document.Root.GetProperty("images")[0].TryGetProperty("uri",out _));
  }
  string directory=Path.Combine(Path.GetTempPath(),"gltf_compiled_only_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
  string logical="relocated_"+Guid.NewGuid().ToString("N")+".gltf";
  string captureDir=Path.Combine(CheckoutRoot,"Temp/gltf-validation/direct-gpu/CompilerImages",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(captureDir);
  File.WriteAllBytes(Path.Combine(directory,logical+".glb_c"),compiled);
  var filesystem=new LocalFileSystem(directory);var aggregate=FileSystem.Mounted;Model model=null;ModelRenderer renderer=null;
  try
  {
   aggregate.Mount(filesystem);
   Assert.AreEqual(1,Directory.GetFiles(directory).Length,"Only compiled GLB must be mounted");
   model=Model.Load(logical);
   Assert.IsTrue(model is {IsValid:true,IsError:false} && model.native.IsNull && model.GltfResource is not null);
   CollectionAssert.AreEqual(image,model.GltfResource.Asset.Document.ReadImage(0).Bytes);
   Assert.AreEqual(1,model.Materials.Length);Assert.IsTrue(model.Materials[0].IsValid && !model.Materials[0].IsError);
   using var capture=new GltfGpuTriangleGateTests.NativeCapture(captureDir,new global::Vector3(140,160,100),global::Vector3.Zero,85);
   using(capture.Scene.Push()) {renderer=capture.Scene.CreateObject().Components.Create<ModelRenderer>(false);renderer.WorldTransform=Transform.Zero;renderer.Model=model;renderer.Enabled=true;capture.Scene.EditorTick(0,0);}
   var frame=await capture.Take("compiled_only_source_material");
   Assert.IsTrue(frame.Coverage>0,"Compiled-only textured model did not draw");
   var instance=renderer.GltfInstance;Assert.IsNotNull(instance);Assert.IsTrue(instance.DrawCalls>0);Assert.AreEqual(0,instance.CallbackErrors.Length);
   Measurements.Add(new{compiled_sha256=Convert.ToHexStringLower(SHA256.HashData(compiled)),image_sha256=Convert.ToHexStringLower(SHA256.HashData(image)),compiled_bytes=compiled.Length,frame.Path,frame.Sha256,frame.Coverage,scope="Relocated mounted compiled GLB only, public Model.Load, exact encoded PNG and source-material drawing; no external source or texture"});
   renderer.Destroy();renderer=null;using(capture.Scene.Push())capture.Scene.ProcessDeletes();
  }
  finally {renderer?.Destroy();model?.Destroy();aggregate.UnMount(filesystem);filesystem.Dispose();Directory.Delete(directory,true);}
 }
}
