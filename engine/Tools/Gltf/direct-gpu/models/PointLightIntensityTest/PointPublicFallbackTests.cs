using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Sandbox.Gltf.DirectGpu.PointLightIntensityValidation;

// Execute the current canonical API with the untouched optional-extension
// asset and record the absence of imported lights; this is a blocker probe.
[TestClass]
internal sealed class PointPublicFallbackTests
{
 public static string CheckoutRoot {get;set;}
 public static System.Collections.Generic.List<object> Measurements {get;}=new();
 [TestMethod]
 public async Task CanonicalLoadWithSourceMaterialsRecordsCurrentLightFallback()
 {
  await MainThread.Wait();string dir=Path.Combine(CheckoutRoot,"Temp/gltf-reference/glTF-Sample-Assets/Models/PointLightIntensityTest/glTF");
  string temp=Path.Combine(Path.GetTempPath(),"gltf_point_public_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
  string name="PointLightIntensityTest.gltf";foreach(string f in new[]{name,"PointLightIntensityTest.bin","LampColorNames.png"})File.Copy(Path.Combine(dir,f),Path.Combine(temp,f));
  string output=Path.Combine(CheckoutRoot,"Temp/gltf-validation/direct-gpu/point-public-fallback",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);
  using var cap=new GltfGpuTriangleGateTests.NativeCapture(output,new global::Vector3(300,0,-50),new global::Vector3(0,0,-50),260);
  ((ScenePointLight)typeof(GltfGpuTriangleGateTests.NativeCapture).GetField("light",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(cap)).LightColor=Color.Black;
  cap.Scene.SceneWorld.AmbientLightColor=Color.Black;
  LocalFileSystem mount=null;Model model=null;ModelRenderer renderer=null;
  try {
   mount=new LocalFileSystem(temp);FileSystem.Mounted.Mount(mount);model=Model.Load(name);
   Assert.IsTrue(model is {IsValid:true,IsError:false} && model.native.IsNull && model.GltfResource is not null);
   using(cap.Scene.Push()){renderer=cap.Scene.CreateObject().Components.Create<ModelRenderer>(false);renderer.Model=model;renderer.Enabled=true;cap.Scene.EditorTick(0,0);}
   var image=await cap.Take("canonical_source_no_imported_lights");
   Assert.IsNotNull(renderer.GltfInstance);Assert.IsTrue(renderer.GltfInstance.DrawCalls>0);Assert.AreEqual(0,renderer.GltfInstance.CallbackErrors.Length,string.Join("\n",renderer.GltfInstance.CallbackErrors));
   int lights=cap.Scene.SceneWorld.SceneObjects.OfType<ScenePointLight>().Count();Assert.AreEqual(1,lights,"The only point light should be the black capture control; imported point bindings require implementation.");
   Measurements.Add(new{image.Path,image.Sha256,image.Coverage,source_sha256=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(dir,name)))),scene_point_lights=lights,scope="Current canonical Model.Load and source materials, optional punctual extension falls back without authored lights; not sample acceptance."});
  }
  finally {await MainThread.Wait();using(cap.Scene.Push()){renderer?.GameObject.Destroy();cap.Scene.EditorTick(0,0);cap.Scene.ProcessDeletes();}model?.Destroy();if(mount is not null){FileSystem.Mounted.UnMount(mount);mount.Dispose();}Directory.Delete(temp,true);}
 }
}
