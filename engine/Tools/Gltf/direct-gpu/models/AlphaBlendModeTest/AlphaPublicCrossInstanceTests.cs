using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Sandbox.Gltf.DirectGpu.AlphaBlendModeValidation;
[TestClass]
public sealed class AlphaPublicCrossInstanceTests
{
 public static string CheckoutRoot {get;set;}
 public static string OutputDirectory {get;set;}
 public static string ShaderSha256 {get;set;}
 public static List<object> Measurements {get;}=new();
 [DataTestMethod]
 [DataRow(false)]
 [DataRow(true)]
 public async Task PublicModelsInterleavedLayers(bool compiled)
 {
  var caseOutput=Path.Combine(OutputDirectory,compiled?"compiled":"source");Assert.IsFalse(Directory.Exists(caseOutput));Directory.CreateDirectory(caseOutput);
  Shader shader=null;Material root=null;Scene scene=null;Editor.SceneRenderingWidget widget=null;SceneDirectionalLight light=null;Action pre=null;int frames=0;
  try
  {
   scene=Scene.CreateEditorScene();CameraComponent camera;using(scene.Push())camera=scene.CreateObject().Components.Create<CameraComponent>();
   camera.WorldPosition=new global::Vector3(200,0,0);camera.WorldRotation=Rotation.FromYaw(180);camera.Orthographic=true;camera.OrthographicHeight=60;camera.ZNear=1;camera.ZFar=1000;camera.CustomSize=new global::Vector2(192,192);camera.ClearFlags=ClearFlags.All;camera.EnablePostProcessing=true;camera.DebugMode=SceneCameraDebugMode.Normal;camera.AutoExposure.Enabled=false;camera.AutoExposure.MinimumExposure=1;camera.AutoExposure.MaximumExposure=1;camera.AutoExposure.Compensation=0;
   light=new SceneDirectionalLight(scene.SceneWorld,camera.WorldRotation,Color.White){ShadowsEnabled=false};widget=new Editor.SceneRenderingWidget{Scene=scene,Camera=camera,WindowTitle="glTF alpha candidate controls",Size=new global::Vector2(192,192)};pre=()=>frames++;widget.OnPreFrame+=pre;widget.Show();
   async Task Wait(){await MainThread.Wait();var start=frames;var watch=Stopwatch.StartNew();while(frames<start+3){if(watch.Elapsed>TimeSpan.FromSeconds(10))throw new InvalidOperationException("Capture preview did not render.");await Task.Delay(20);await MainThread.Wait();}}
   async Task<float[]> Capture(string label,Color background)
   {
    await MainThread.Wait();camera.BackgroundColor=background;await Wait();using(scene.Push()){scene.PreCameraRender();camera.InitializeRendering(false);if(camera.SceneCamera is not null)camera.SceneCamera.Tonemap.Enabled=false;}
    using var target=Texture.CreateRenderTarget().WithSize(192,192).WithFormat(ImageFormat.RGBA32323232F).WithInitialColor(Color.Magenta).Create("alpha_lit_"+Guid.NewGuid().ToString("N"));
    bool submitted;using(scene.Push()){target.Clear(Color.Magenta);submitted=camera.RenderToTexture(target);}Assert.IsTrue(submitted);await Wait();Graphics.FlushGPU();var pixels=new float[192*192*4];target.GetPixels((0,0,192,192),0,0,pixels.AsSpan(),ImageFormat.RGBA32323232F);using var bitmap=target.GetBitmap();var png=bitmap.ToPng();var pngPath=Path.Combine(caseOutput,label+".png");File.WriteAllBytes(pngPath,png);var center=pixels.Skip((96*192+96)*4).Take(4).ToArray();Assert.IsTrue(center.All(float.IsFinite));Measurements.Add(new{phase="float_capture",label,path=pngPath,sha256=Convert.ToHexStringLower(SHA256.HashData(png)),center});return center;
   }
   var sources=AlphaCrossModelOracle.Sources();
   async Task<float[]> Cross(bool compiled,string only,string label,bool reversedCreation)
   {
    var temp=Path.Combine(Path.GetTempPath(),"alpha_public_cross_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);var prefix="alpha_cross_"+Guid.NewGuid().ToString("N");Directory.CreateDirectory(Path.Combine(temp,prefix));var mount=new LocalFileSystem(temp);var models=new List<Model>();var renderers=new List<ModelRenderer>();FileSystem.Mounted.Mount(mount);
    try
    {
     for(var model=0;model<2;model++)
     {
      var modelIndex=reversedCreation?1-model:model;var json=System.Text.Json.Nodes.JsonNode.Parse(sources[modelIndex].Bytes).AsObject();var materials=json["materials"].AsArray();
      for(int slot=0;slot<materials.Count;slot++) {var material=materials[slot];material["doubleSided"]=true;var channel=modelIndex==1?"blue":slot==0?"green":"red";material["pbrMetallicRoughness"]["baseColorFactor"][3]=only=="all"?.5:only==channel?1.0:0.0;}
      var alternate=materials[0].DeepClone();alternate["alphaMode"]="OPAQUE";alternate["pbrMetallicRoughness"]["baseColorFactor"][3]=1.0;alternate["emissiveFactor"]=System.Text.Json.Nodes.JsonNode.Parse("[1,0,1]");var alternateIndex=materials.Count;materials.Add(alternate);
      json["extensionsUsed"]=System.Text.Json.Nodes.JsonNode.Parse("[\"KHR_materials_variants\"]");json["extensions"]=System.Text.Json.Nodes.JsonNode.Parse("{\"KHR_materials_variants\":{\"variants\":[{\"name\":\"opaque-alternate\"}]}}");
      json["meshes"][0]["primitives"][0]["extensions"]=System.Text.Json.Nodes.JsonNode.Parse("{\"KHR_materials_variants\":{\"mappings\":[{\"material\":"+alternateIndex+",\"variants\":[0]}]}}");
      var source=Encoding.UTF8.GetBytes(json.ToJsonString());var input=compiled?GltfCompiler.Compile(source,null):source;var relative=prefix+"/model"+modelIndex+(compiled?".glb_c":".gltf");File.WriteAllBytes(Path.Combine(temp,relative),input);
      var loaded=Model.Load(relative);models.Add(loaded);Assert.IsTrue(loaded is {IsValid:true,IsError:false}&&loaded.native.IsNull&&loaded.GltfResource is not null,"Ordinary Model.Load did not realize this authored BLEND source.");
      ModelRenderer renderer;using(scene.Push()){renderer=scene.CreateObject().Components.Create<ModelRenderer>(false);renderer.Model=loaded;renderer.Enabled=true;scene.EditorTick(0,0);}renderers.Add(renderer);
     }
     var result=await Capture(label,Color.Black);Assert.IsTrue(renderers.All(r=>r.GltfInstance is not null&&r.GltfInstance.DrawCalls>0&&r.GltfInstance.CallbackErrors.Length==0),"Both public ModelRenderer instances must submit");
     if(only=="all")
     {
      var renderer=renderers[0];var white=Material.Load("materials/default/white.vmat");renderer.MaterialOverride=white;using(scene.Push())scene.EditorTick(0,0);var overridden=await Capture(label+"_override",Color.Black);Assert.IsTrue(renderer.GltfInstance.Readback().All(row=>row.SubmittedMaterialOverride&&ReferenceEquals(row.SubmittedMaterial,white)),"Public whole-material override was not submitted");
      var delta=Enumerable.Range(0,3).Max(i=>Math.Abs(overridden[i]-result[i]));Assert.IsTrue(delta>.03f,"Material override did not alter pixels");renderer.ClearMaterialOverrides();using(scene.Push())scene.EditorTick(0,0);var restored=await Capture(label+"_restored",Color.Black);var difference=Enumerable.Range(0,3).Max(i=>Math.Abs(restored[i]-result[i]));Assert.IsTrue(difference<.03f,"Clearing material override did not restore original composition");Measurements.Add(new{phase="public_cross_override",label,result,overridden,restored,delta,difference});
      renderer.Materials.SetOverride(0,white);using(scene.Push())scene.EditorTick(0,0);var slotOverride=await Capture(label+"_slot_override",Color.Black);Assert.AreSame(white,renderer.Materials.GetOverride(0));
      var invalid=white.CreateCopy();invalid.Destroy();Assert.ThrowsException<GltfException>(()=>renderer.Materials.SetOverride(0,invalid));Assert.AreSame(white,renderer.Materials.GetOverride(0),"Invalid override failed to roll back component state");var afterFailure=await Capture(label+"_invalid_override",Color.Black);Assert.IsTrue(Enumerable.Range(0,3).Max(i=>Math.Abs(afterFailure[i]-slotOverride[i]))<.03f,"Invalid override changed the GPU output");
      renderer.Materials.SetOverride(0,null);using(scene.Push())scene.EditorTick(0,0);var slotRestored=await Capture(label+"_slot_restored",Color.Black);Assert.IsTrue(Enumerable.Range(0,3).Max(i=>Math.Abs(slotRestored[i]-result[i]))<.03f,"Clearing slot override failed to restore source output");Measurements.Add(new{phase="public_slot_override_rollback",label,slotOverride,afterFailure,slotRestored});
      renderer.SceneObject.Flags.WantsFrameBufferCopy=true;Assert.IsTrue(renderer.SceneObject.Flags.WantsFrameBufferCopy);
      var injected=false;void FailVariantOnce(Sandbox.Rendering.SceneObjectChange change){if(!injected&&(change&Sandbox.Rendering.SceneObjectChange.Flags)!=0){injected=true;throw new InvalidOperationException("injected variant publication failure");}}
      renderer.SceneObject.DependentStateChanged+=FailVariantOnce;try{Assert.ThrowsException<InvalidOperationException>(()=>renderer.MaterialVariant=0);}finally{renderer.SceneObject.DependentStateChanged-=FailVariantOnce;}Assert.IsTrue(injected,"Variant publication did not hit failure injection");Assert.IsNull(renderer.MaterialVariant,"Failed variant publication changed selected state");var failedPublication=await Capture(label+"_failed_variant_publication",Color.Black);Assert.IsTrue(Enumerable.Range(0,3).Max(i=>Math.Abs(failedPublication[i]-result[i]))<.03f,"Failed variant publication did not restore the original draw state");
      renderer.MaterialVariant=0;using(scene.Push())scene.EditorTick(0,0);var variant=await Capture(label+"_variant",Color.Black);Assert.AreEqual<int?>(0,renderer.MaterialVariant);Assert.IsTrue(Enumerable.Range(0,3).Max(i=>Math.Abs(variant[i]-result[i]))>.03f,"Authored alternate variant did not alter output");
      Assert.ThrowsException<ArgumentOutOfRangeException>(()=>renderer.MaterialVariant=1);Assert.AreEqual<int?>(0,renderer.MaterialVariant,"Invalid variant changed selected state");var variantFailure=await Capture(label+"_invalid_variant",Color.Black);Assert.IsTrue(Enumerable.Range(0,3).Max(i=>Math.Abs(variantFailure[i]-variant[i]))<.03f,"Invalid variant changed GPU output");
      renderer.MaterialVariant=null;using(scene.Push())scene.EditorTick(0,0);Assert.IsTrue(renderer.SceneObject.Flags.WantsFrameBufferCopy,"Explicit framebuffer request must survive variant transitions");renderer.SceneObject.Flags.WantsFrameBufferCopy=false;var variantRestored=await Capture(label+"_variant_restored",Color.Black);Assert.IsTrue(Enumerable.Range(0,3).Max(i=>Math.Abs(variantRestored[i]-result[i]))<.03f,"Variant reset failed to restore source output");Measurements.Add(new{phase="public_variant_rollback",label,variant,variantFailure,variantRestored});
     }
     return result;
    }
    finally
    {
     await MainThread.Wait();var instances=renderers.Select(r=>r.GltfInstance).ToArray();var objects=renderers.Select(r=>r.SceneObject).ToArray();var before=instances.Select(i=>i.DrawCalls).ToArray();using(scene.Push()){foreach(var r in renderers)r.GameObject.Destroy();scene.EditorTick(0,0);scene.ProcessDeletes();}await Wait();using(scene.Push())scene.ProcessDeletes();var after=instances.Select(i=>i.DrawCalls).ToArray();Assert.IsTrue(objects.All(o=>!o.IsValid()));CollectionAssert.AreEqual(before,after);foreach(var model in models)if(model.IsValid)model.Destroy();FileSystem.Mounted.UnMount(mount);mount.Dispose();Directory.Delete(temp,true);
    }
   }
   var empty=await Capture("cross_empty_before",Color.Black);Assert.IsTrue(empty.Take(3).All(x=>Math.Abs(x)<.02));var errors=new List<string>();
   for(var view=0;view<2;view++) for(var creation=0;creation<2;creation++)
   {
    bool reversedView=view==1, reversedCreation=creation==1;
    camera.WorldPosition=new global::Vector3(reversedView?-200:200,0,0);camera.WorldRotation=Rotation.FromYaw(reversedView?0:180);light.Rotation=camera.WorldRotation;
    var suffix=view+"_"+creation;
    var red=await Cross(compiled,"red","cross_red_"+suffix,reversedCreation);var delRed=await Capture("cross_red_deleted_"+suffix,Color.Black);Assert.IsTrue(delRed.Take(3).All(x=>Math.Abs(x)<.02),"red reference cleanup failed");
    var green=await Cross(compiled,"green","cross_green_"+suffix,reversedCreation);var delGreen=await Capture("cross_green_deleted_"+suffix,Color.Black);Assert.IsTrue(delGreen.Take(3).All(x=>Math.Abs(x)<.02),"green reference cleanup failed");
    var blue=await Cross(compiled,"blue","cross_blue_"+suffix,reversedCreation);var delBlue=await Capture("cross_blue_deleted_"+suffix,Color.Black);Assert.IsTrue(delBlue.Take(3).All(x=>Math.Abs(x)<.02),"blue reference cleanup failed");
    var actual=await Cross(compiled,"all","cross_all_"+suffix,reversedCreation);
    if(red[0]<.1f||green[1]<.1f||blue[2]<.1f)errors.Add("Independent positive emission control absent "+suffix);
    var expected=Enumerable.Range(0,3).Select(i=>reversedView?green[i]*.125f+blue[i]*.25f+red[i]*.5f:red[i]*.125f+blue[i]*.25f+green[i]*.5f).ToArray();
    var error=Enumerable.Range(0,3).Max(i=>Math.Abs(actual[i]-expected[i]));
    Measurements.Add(new{phase="public_cross_instance_view_interleaving",compiled,view,creation,red,green,blue,actual,expected,maximumError=error});if(error>=.03f)errors.Add("Cross-instance plane interleaving differs from independent alpha-over oracle "+suffix);
   }
   var deleted=await Capture("cross_empty_after",Color.Black);Assert.IsTrue(deleted.Take(3).All(x=>Math.Abs(x)<.02));Assert.AreEqual(0,errors.Count,string.Join("; ",errors));
  }
  finally{await MainThread.Wait();if(widget is not null&&widget.IsValid){widget.OnPreFrame-=pre;widget.Close();widget.Destroy();}if(light.IsValid())light.Delete();scene?.Destroy();root?.Destroy();shader?.Destroy();}
 }
}
