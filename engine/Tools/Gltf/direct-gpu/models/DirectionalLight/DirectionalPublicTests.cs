using System;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using Sandbox.Engine;
using Sandbox.SceneRenderer.Gpu;

namespace Sandbox.Gltf.DirectGpu.DirectionalLightValidation;
/// <summary>Provisional explicit directional opt-in API; production entrypoints must be installed before execution.</summary>
[TestClass]
public sealed class DirectionalPublicTests
{
 static string root; public static List<object> Measurements {get;}=new();
 public static void Configure(string checkout){root=checkout;Measurements.Clear();}
 [DataTestMethod]
 [DataRow("glTF",false)] [DataRow("glTF",true)] [DataRow("glTF-Binary",false)] [DataRow("glTF-Binary",true)]
 public async Task PublicOptInKeepsIndependentRendererLights(string variant,bool compiled)
 {
  await MainThread.Wait();
  string dir=Path.Combine(root,"Temp/gltf-reference/glTF-Sample-Assets/Models/DirectionalLight",variant);
  var source=File.ReadAllBytes(Path.Combine(dir,variant=="glTF"?"DirectionalLight.gltf":"DirectionalLight.glb"));
  Assert.AreEqual(variant=="glTF"?"71fbfe89e98201b70f5de027604d3745be41e900b8cb03ccfd446c3896c6b559":"82e50feb151203f431a3b43985e9f4b2a54131b61a7a8677954922630d90f3de",Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(source)),"Pinned model source changed.");
  if(variant=="glTF")Assert.AreEqual("43aea71c4cbe518a9aa34e04ea2acd0b779c189213aee70cec3d80d1a8e72741",Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(dir,"DirectionalLight.bin")))),"Pinned buffer changed.");
  byte[] Resolve(string uri)=>File.ReadAllBytes(Path.Combine(dir,uri));
  var bytes=compiled?GltfCompiler.Compile(source,Resolve,new HashSet<string>{"KHR_lights_punctual"}):source;
  var temporary=Path.Combine(Path.GetTempPath(),"directional_public_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temporary);
  string name="directional_"+Guid.NewGuid().ToString("N")+(compiled?".glb_c":variant=="glTF"?".gltf":".glb");
  string output=Path.Combine(root,"Temp/gltf-validation/direct-gpu/DirectionalLight/public",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);
  LocalFileSystem mount=null;Model model=null;ModelRenderer a=null;FailingRenderer b=null;
  using var cap=new GltfGpuTriangleGateTests.NativeCapture(output,new global::Vector3(100,0,0),global::Vector3.Zero,75);
  try
  {
   File.WriteAllBytes(Path.Combine(temporary,name),bytes);if(!compiled && variant=="glTF")File.Copy(Path.Combine(dir,"DirectionalLight.bin"),Path.Combine(temporary,"DirectionalLight.bin"));
   mount=new LocalFileSystem(temporary);FileSystem.Mounted.Mount(mount);
   Assert.ThrowsException<GltfException>(()=>Model.Load(name),"Default loading must still reject required punctual-light support.");
   model=Model.LoadGltf(name,0,true);
   Assert.IsTrue(model is {IsValid:true,IsError:false} && model.native.IsNull && model.GltfResource is not null);
   Assert.AreEqual(3,model.MeshCount);Assert.AreEqual(0,model.GltfSelectedScene);
   var authored=model.GltfResource.Asset.Document.ReadLight(0);Assert.AreEqual("directional",authored.Type);Assert.AreEqual(1f,authored.Intensity);Assert.AreEqual(.9f,authored.Color.X,1e-6f);Assert.AreEqual(.8f,authored.Color.Y,1e-6f);Assert.AreEqual(.1f,authored.Color.Z,1e-6f);
   var published=model.GltfDirectionalLights.Single();Assert.AreEqual(3,published.Node);Assert.AreEqual(0,published.Light);Assert.AreEqual(1f,published.IntensityLux);Assert.AreEqual(.9f,published.LinearColor.x,1e-6f);Assert.AreEqual(.8f,published.LinearColor.y,1e-6f);Assert.AreEqual(.1f,published.LinearColor.z,1e-6f);
   using(cap.Scene.Push())
   {
    a=cap.Scene.CreateObject().Components.Create<ModelRenderer>(false);a.GltfDirectionalLightScale=1;a.Model=model;a.Enabled=true;
    b=cap.Scene.CreateObject().Components.Create<FailingRenderer>(false);b.GltfDirectionalLightScale=.25f;b.Model=model;b.GameObject.WorldRotation=Rotation.FromYaw(90);b.Enabled=true;cap.Scene.EditorTick(0,0);
   }
   var full=await cap.Take("two_public_instances");
   Assert.IsTrue(a.GltfInstance is not null && b.GltfInstance is not null && !ReferenceEquals(a.GltfInstance,b.GltfInstance));
   Assert.AreEqual(3,a.GltfInstance.Readback().Count);Assert.AreEqual(3,b.GltfInstance.Readback().Count);
   var lights=cap.Scene.SceneWorld.SceneObjects.OfType<SceneDirectionalLight>().ToArray();Assert.AreEqual(2,lights.Length,"Each enabled renderer must own one light.");
   Assert.IsTrue(lights.Any(x=>Math.Abs(x.LightColor.r-.9f)<1e-5f) && lights.Any(x=>Math.Abs(x.LightColor.r-.225f)<1e-5f));
   var first=lights.Single(x=>Math.Abs(x.LightColor.r-.9f)<1e-5f);var second=lights.Single(x=>Math.Abs(x.LightColor.r-.225f)<1e-5f);
   Assert.IsTrue(RenderContext.PackSceneLight(first,out var firstPacked,out _,out _));Assert.IsTrue(RenderContext.PackSceneLight(second,out var secondPacked,out _,out _));
   Assert.IsTrue(Math.Abs(firstPacked.Row0.x-secondPacked.Row0.x)+Math.Abs(firstPacked.Row1.x-secondPacked.Row1.x)>.5f,"Independent renderer rotation did not reach its light.");
   // A failed candidate reimport must retain the last valid resource, GPU
   // instances, and native lights. This exercises preparation, not an editor asset write.
   var originalDir=Path.Combine(root,"Temp/gltf-reference/glTF-Sample-Assets/Models/DirectionalLight/glTF");
   var invalid=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(originalDir,"DirectionalLight.gltf")));
   invalid["extensions"]["KHR_lights_punctual"]["lights"][0]["type"]="point";
   byte[] ResolveOriginal(string uri)=>File.ReadAllBytes(Path.Combine(originalDir,uri));
   var invalidBytes=GltfCompiler.Compile(System.Text.Encoding.UTF8.GetBytes(invalid.ToJsonString()),ResolveOriginal,new HashSet<string>{"KHR_lights_punctual"});
   var previousResource=model.GltfResource;var previousA=a.GltfInstance;var previousB=b.GltfInstance;
   Assert.ThrowsException<GltfException>(()=>GltfModelLoader.PrepareReloads(name,invalidBytes));
   Assert.AreSame(previousResource,model.GltfResource);Assert.AreSame(previousA,a.GltfInstance);Assert.AreSame(previousB,b.GltfInstance);
   Assert.IsTrue(first.IsValid() && second.IsValid(),"Failed reimport candidate destroyed the previous lights.");
   var validBytes=GltfCompiler.Compile(File.ReadAllBytes(Path.Combine(originalDir,"DirectionalLight.gltf")),ResolveOriginal,new HashSet<string>{"KHR_lights_punctual"});
   var failedReplacement=GltfModelLoader.PrepareReloads(name,validBytes);
   if(failedReplacement.Count!=1){foreach(var entry in failedReplacement)entry.Resource.ReleaseOwner();Assert.Fail("Opt-in identity was lost from reimport discovery.");}
   b.FailNextCreation=true;Assert.ThrowsException<InvalidOperationException>(()=>Model.ReplaceGltfResources(failedReplacement));
   Assert.AreSame(previousResource,model.GltfResource);Assert.AreSame(previousA,a.GltfInstance);Assert.AreSame(previousB,b.GltfInstance);
   Assert.IsTrue(first.IsValid() && second.IsValid(),"Late renderer failure did not restore both previous lights.");
   Assert.AreEqual(2,cap.Scene.SceneWorld.SceneObjects.OfType<SceneDirectionalLight>().Count(),"Late rollback leaked candidate lights.");
   var replacement=GltfModelLoader.PrepareReloads(name,validBytes);
   if(replacement.Count!=1){foreach(var entry in replacement)entry.Resource.ReleaseOwner();Assert.Fail("Opt-in identity was lost from reimport discovery.");}
   Assert.IsNull(Model.ReplaceGltfResources(replacement));Assert.AreNotSame(previousResource,model.GltfResource);
   Assert.AreNotSame(previousA,a.GltfInstance);Assert.AreNotSame(previousB,b.GltfInstance);
   Assert.IsFalse(first.IsValid() || second.IsValid(),"Committed replacement retained stale native lights.");
   Assert.AreEqual(2,cap.Scene.SceneWorld.SceneObjects.OfType<SceneDirectionalLight>().Count(),"Reload must retain exactly one light per active renderer.");
   using(cap.Scene.Push()){a.GltfDirectionalLightScale=null;cap.Scene.EditorTick(0,0);}
   await cap.WaitFrames();Assert.AreEqual(1,cap.Scene.SceneWorld.SceneObjects.OfType<SceneDirectionalLight>().Count(),"Disabling one binding must preserve the other.");
   using(cap.Scene.Push()){b.GameObject.Destroy();cap.Scene.EditorTick(0,0);cap.Scene.ProcessDeletes();}b=null;
   await cap.WaitFrames();Assert.AreEqual(0,cap.Scene.SceneWorld.SceneObjects.OfType<SceneDirectionalLight>().Count(),"Destroy must remove owned light.");
   Measurements.Add(new{variant,compiled,input_sha256=Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)),capture=full.Path,sha256=full.Sha256,scope="Provisional public directional opt-in; explicit arbitrary native scales, no absolute lux or reference parity claim."});
  }
  finally
  {
   await MainThread.Wait();using(cap.Scene.Push()){a?.GameObject.Destroy();b?.GameObject.Destroy();cap.Scene.EditorTick(0,0);cap.Scene.ProcessDeletes();}
   model?.Destroy();if(mount is not null){FileSystem.Mounted.UnMount(mount);mount.Dispose();}Directory.Delete(temporary,true);
  }
 }

 [Expose]
 public sealed class FailingRenderer:ModelRenderer
 {
  internal bool FailNextCreation;
  internal override void OnSceneObjectCreated(SceneObject value)
  {
   base.OnSceneObjectCreated(value);
   if(FailNextCreation){FailNextCreation=false;throw new InvalidOperationException("Injected late directional renderer replacement failure.");}
  }
 }

 [DataTestMethod]
 [DataRow("point")] [DataRow("spot")]
 public void DirectionalOptInRejectsOtherRequiredLightTypes(string kind)
 {
  string dir=Path.Combine(root,"Temp/gltf-reference/glTF-Sample-Assets/Models/DirectionalLight/glTF");
  var json=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(dir,"DirectionalLight.gltf")));
  var light=json["extensions"]["KHR_lights_punctual"]["lights"][0];light["type"]=kind;
  if(kind=="spot")light["spot"]=new System.Text.Json.Nodes.JsonObject();
  var temporary=Path.Combine(Path.GetTempPath(),"directional_reject_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temporary);
  string name="reject_"+Guid.NewGuid().ToString("N")+".gltf";LocalFileSystem mount=null;Model model=null;
  try
  {
   File.WriteAllText(Path.Combine(temporary,name),json.ToJsonString());File.Copy(Path.Combine(dir,"DirectionalLight.bin"),Path.Combine(temporary,"DirectionalLight.bin"));
   mount=new LocalFileSystem(temporary);FileSystem.Mounted.Mount(mount);
   Assert.ThrowsException<GltfException>(()=>model=Model.LoadGltf(name,0,true),"Directional-only mode must reject "+kind+" rather than drop it.");
  }
  finally {model?.Destroy();if(mount is not null){FileSystem.Mounted.UnMount(mount);mount.Dispose();}Directory.Delete(temporary,true);}
 }
}
