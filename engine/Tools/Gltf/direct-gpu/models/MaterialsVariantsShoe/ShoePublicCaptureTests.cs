using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using Sandbox.Gltf;
using Microsoft.VisualStudio.TestTools.UnitTesting;
namespace Sandbox.Gltf.DirectGpu.MaterialsVariantsShoeValidation;
[TestClass]
public sealed class ShoePublicCaptureTests
{
 static string root;
 public static List<object> Measurements {get;}=new();
 public static void Configure(string checkout){root=checkout;Measurements.Clear();}
 [TestMethod,DataRow(false),DataRow(true)]
 public async Task PublicVariantDrawAndReset(bool compiled)
 {
  await MainThread.Wait();var sourceDir=Path.Combine(root,"Temp/gltf-reference/glTF-Sample-Assets/Models/MaterialsVariantsShoe/glTF");
  byte[] input=File.ReadAllBytes(Path.Combine(sourceDir,"MaterialsVariantsShoe.gltf")); byte[] Resolve(string uri)=>File.ReadAllBytes(Path.Combine(sourceDir,uri));
  if(compiled)input=GltfCompiler.Compile(input,Resolve);
  var temp=Path.Combine(Path.GetTempPath(),"shoe_capture_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
  var output=Path.Combine(root,"Temp/gltf-validation/direct-gpu/MaterialsVariantsShoe/public",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);
  var name="shoe_"+Guid.NewGuid().ToString("N")+(compiled?".glb_c":".gltf"); File.WriteAllBytes(Path.Combine(temp,name),input);
  foreach(var f in Directory.GetFiles(sourceDir).Where(p=>!p.EndsWith(".gltf",StringComparison.OrdinalIgnoreCase)))File.Copy(f,Path.Combine(temp,Path.GetFileName(f)));
  LocalFileSystem mount=null;Model model=null;ModelRenderer renderer=null;
  using var capture=new GltfGpuTriangleGateTests.NativeCapture(output,new Vector3(140,80,60),Vector3.Zero,90);
  try
  {
   mount=new LocalFileSystem(temp);FileSystem.Mounted.Mount(mount); model=Model.Load(name);Assert.IsTrue(model.IsValid&&!model.IsError && model.native.IsNull);
   Assert.AreEqual(0,(await capture.Take("empty_before")).Coverage);
   using(capture.Scene.Push()) {var go=capture.Scene.CreateObject();var bounds=model.Bounds;var size=bounds.Size;float extent=MathF.Max(size.x,MathF.Max(size.y,size.z));Assert.IsTrue(float.IsFinite(extent)&&extent>0);float scale=32/extent;go.WorldScale=Vector3.One*scale;go.WorldPosition=-bounds.Center*scale;renderer=go.Components.Create<ModelRenderer>(false);renderer.Model=model;renderer.Enabled=true;capture.Scene.EditorTick(0,0);}
   var instance=renderer.GltfInstance; Assert.IsNotNull(instance);
   var original=await capture.Take("default");Assert.IsTrue(original.Coverage>64);
   using(capture.Scene.Push()){renderer.MaterialVariant=1;capture.Scene.EditorTick(0,0);}var beach=await capture.Take("beach");
   using(capture.Scene.Push()){renderer.MaterialVariant=2;capture.Scene.EditorTick(0,0);}var street=await capture.Take("street");
   using(capture.Scene.Push()){renderer.MaterialVariant=null;capture.Scene.EditorTick(0,0);}var restored=await capture.Take("default_restored");
   int Different(GltfGpuTriangleGateTests.NativeCapture.Frame a,GltfGpuTriangleGateTests.NativeCapture.Frame b)=>a.Pixels.Zip(b.Pixels,(x,y)=>Math.Abs(x.r-y.r)+Math.Abs(x.g-y.g)+Math.Abs(x.b-y.b)>.06f).Count(x=>x);
   Assert.IsTrue(beach.Coverage>64&&street.Coverage>64);Assert.IsTrue(Different(original,beach)>64&&Different(beach,street)>64,"variant textures must visibly differ");Assert.IsTrue(Different(original,restored)<64,"default did not restore");
   Assert.AreEqual(0,instance.CallbackErrors.Length,string.Join("\n",instance.CallbackErrors));Assert.IsTrue(instance.DrawCalls>0 && instance.SubmittedRevision==instance.PoseRevision);var row=instance.Readback().Single();Assert.AreEqual("gpu-readback",row.IndexDataOrigin);Assert.AreEqual(68100,row.Indices.Length);
   Measurements.Add(new{form=compiled?"glTF/derived-glb_c":"glTF/source",input_sha256=Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(input)),captures=new[]{original,beach,street,restored}.Select(x=>new{x.Path,x.Sha256,x.Coverage,x.CameraStateJson}).ToArray(),scope="Public source material, rendered selection and GPU index readback; inferred camera and lighting, visual review pending."});
   using(capture.Scene.Push()){renderer.GameObject.Destroy();capture.Scene.EditorTick(0,0);capture.Scene.ProcessDeletes();}renderer=null; Assert.AreEqual(0,(await capture.Take("empty_after")).Coverage);
  }
  finally{await MainThread.Wait();if(renderer!=null){using var scope=capture.Scene.Push();renderer.GameObject.Destroy();capture.Scene.EditorTick(0,0);capture.Scene.ProcessDeletes();}model?.Destroy();if(mount!=null){FileSystem.Mounted.UnMount(mount);mount.Dispose();}Directory.Delete(temp,true);}
 }
}
