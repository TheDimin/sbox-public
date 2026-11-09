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
public sealed class AlphaCrossInstanceViewTests
{
 public static string CheckoutRoot {get;set;}
 public static string OutputDirectory {get;set;}
 public static string ShaderSha256 {get;set;}
 public static List<object> Measurements {get;}=new();
 [TestMethod]
 public async Task IndependentModelsInterleavedLayers()
 {
  var path=Path.Combine(CheckoutRoot,"engine/Tools/Gltf/direct-gpu/models/AlphaBlendModeTest/gltf_lit_alpha_current_candidate.shader");var bytes=File.ReadAllBytes(path);Assert.AreEqual(ShaderSha256,Convert.ToHexStringLower(SHA256.HashData(bytes)));
  Assert.IsFalse(Directory.Exists(OutputDirectory));Directory.CreateDirectory(OutputDirectory);
  Shader shader=null;Material root=null;Scene scene=null;Editor.SceneRenderingWidget widget=null;SceneDirectionalLight light=null;Action pre=null;int frames=0;
  try
  {
   using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(90));shader=await Shader.CompileAsync(Encoding.UTF8.GetString(bytes),timeout.Token);await MainThread.Wait();Assert.IsTrue(shader is {IsValid:true,IsAnonymous:true});root=Material.Create("lit_alpha_render_"+Guid.NewGuid().ToString("N"),shader);Assert.IsTrue(root is {IsValid:true,IsError:false});
   scene=Scene.CreateEditorScene();CameraComponent camera;using(scene.Push())camera=scene.CreateObject().Components.Create<CameraComponent>();
   camera.WorldPosition=new global::Vector3(200,0,0);camera.WorldRotation=Rotation.FromYaw(180);camera.Orthographic=true;camera.OrthographicHeight=60;camera.ZNear=1;camera.ZFar=1000;camera.CustomSize=new global::Vector2(192,192);camera.ClearFlags=ClearFlags.All;camera.EnablePostProcessing=true;camera.DebugMode=SceneCameraDebugMode.Normal;camera.AutoExposure.Enabled=false;camera.AutoExposure.MinimumExposure=1;camera.AutoExposure.MaximumExposure=1;camera.AutoExposure.Compensation=0;
   light=new SceneDirectionalLight(scene.SceneWorld,camera.WorldRotation,Color.White){ShadowsEnabled=false};widget=new Editor.SceneRenderingWidget{Scene=scene,Camera=camera,WindowTitle="glTF alpha candidate controls",Size=new global::Vector2(192,192)};pre=()=>frames++;widget.OnPreFrame+=pre;widget.Show();
   async Task Wait(){await MainThread.Wait();var start=frames;var watch=Stopwatch.StartNew();while(frames<start+3){if(watch.Elapsed>TimeSpan.FromSeconds(10))throw new InvalidOperationException("Capture preview did not render.");await Task.Delay(20);await MainThread.Wait();}}
   async Task<float[]> Capture(string label,Color background)
   {
    await MainThread.Wait();camera.BackgroundColor=background;await Wait();using(scene.Push()){scene.PreCameraRender();camera.InitializeRendering(false);if(camera.SceneCamera is not null)camera.SceneCamera.Tonemap.Enabled=false;}
    using var target=Texture.CreateRenderTarget().WithSize(192,192).WithFormat(ImageFormat.RGBA32323232F).WithInitialColor(Color.Magenta).Create("alpha_lit_"+Guid.NewGuid().ToString("N"));
    bool submitted;using(scene.Push()){target.Clear(Color.Magenta);submitted=camera.RenderToTexture(target);}Assert.IsTrue(submitted);await Wait();Graphics.FlushGPU();var pixels=new float[192*192*4];target.GetPixels((0,0,192,192),0,0,pixels.AsSpan(),ImageFormat.RGBA32323232F);using var bitmap=target.GetBitmap();var png=bitmap.ToPng();var pngPath=Path.Combine(OutputDirectory,label+".png");File.WriteAllBytes(pngPath,png);var center=pixels.Skip((96*192+96)*4).Take(4).ToArray();Assert.IsTrue(center.All(float.IsFinite));Measurements.Add(new{phase="float_capture",label,path=pngPath,sha256=Convert.ToHexStringLower(SHA256.HashData(png)),center});return center;
   }
   var sources=AlphaCrossModelOracle.Sources();
   async Task<float[]> Cross(bool compiled,string only,string label,bool reversedCreation)
   {
    var documents=new List<GltfDocument>();var assets=new List<GltfGpuAsset>();var instances=new List<GltfGpuInstance>();var materials=new List<Material>();
    try
    {
     for(var model=0;model<2;model++)
     {
      var modelIndex=reversedCreation?1-model:model;var input=compiled?GltfCompiler.Compile(sources[modelIndex].Bytes,null):sources[modelIndex].Bytes;var document=GltfReader.Read(input);documents.Add(document);
      Material Resolve(int? slot){var source=document.ReadMaterial(slot.Value);var m=root.CreateCopy();materials.Add(m);m.SetFeature("F_TRANSLUCENT",1);m.SetFeature("F_RENDER_BACKFACES",1);var c=source.BaseColorFactor;var emission=source.EmissiveFactor;var channel=modelIndex==1?"blue":slot.Value==0?"green":"red";var alpha=only=="all"?c.W:only==channel?1f:0f;m.Set("g_vGltfBaseColor",new global::Vector4(c.X,c.Y,c.Z,alpha));m.Set("g_vGltfEmissiveFactor",new global::Vector4(emission.X,emission.Y,emission.Z,0));m.Set("g_flGltfMetallicFactor",0);m.Set("g_flGltfRoughnessFactor",1);return m;}
      var asset=GltfGpuAsset.Create(document,"shaders/gltf_deform_cs.shader",Resolve);assets.Add(asset);instances.Add(asset.CreateInstance(scene.SceneWorld));
     }
     var result=await Capture(label,Color.Black);Assert.IsTrue(instances.All(i=>i.DrawCalls>0&&i.CallbackErrors.Length==0),"Both instances must actually submit");return result;
    }
    finally{await MainThread.Wait();var objects=instances.Select(i=>i.SceneObject).ToArray();var before=instances.Select(i=>i.DrawCalls).ToArray();foreach(var i in instances)i.Dispose();foreach(var a in assets)a.Dispose();foreach(var m in materials)m.Destroy();foreach(var d in documents)d.Dispose();using(scene.Push())scene.ProcessDeletes();await Wait();using(scene.Push())scene.ProcessDeletes();var valid=objects.Select(o=>o.IsValid()).ToArray();var after=instances.Select(i=>i.DrawCalls).ToArray();Measurements.Add(new{phase="cross_retirement",label,valid,drawsBefore=before,drawsAfter=after});Assert.IsTrue(valid.All(v=>!v),"Disposed scene objects remain valid");CollectionAssert.AreEqual(before,after,"Disposed instances submitted another draw");}
   }
   var empty=await Capture("cross_empty_before",Color.Black);Assert.IsTrue(empty.Take(3).All(x=>Math.Abs(x)<.02));var errors=new List<string>();
   for(var view=0;view<2;view++) for(var creation=0;creation<2;creation++)
   {
    bool reversedView=view==1, reversedCreation=creation==1;
    camera.WorldPosition=new global::Vector3(reversedView?-200:200,0,0);camera.WorldRotation=Rotation.FromYaw(reversedView?0:180);light.Rotation=camera.WorldRotation;
    var suffix=view+"_"+creation;
    var red=await Cross(true,"red","cross_red_"+suffix,reversedCreation);var delRed=await Capture("cross_red_deleted_"+suffix,Color.Black);Assert.IsTrue(delRed.Take(3).All(x=>Math.Abs(x)<.02),"red reference cleanup failed");
    var green=await Cross(true,"green","cross_green_"+suffix,reversedCreation);var delGreen=await Capture("cross_green_deleted_"+suffix,Color.Black);Assert.IsTrue(delGreen.Take(3).All(x=>Math.Abs(x)<.02),"green reference cleanup failed");
    var blue=await Cross(true,"blue","cross_blue_"+suffix,reversedCreation);var delBlue=await Capture("cross_blue_deleted_"+suffix,Color.Black);Assert.IsTrue(delBlue.Take(3).All(x=>Math.Abs(x)<.02),"blue reference cleanup failed");
    var actual=await Cross(true,"all","cross_all_"+suffix,reversedCreation);
    if(red[0]<.1f||green[1]<.1f||blue[2]<.1f)errors.Add("Independent positive emission control absent "+suffix);
    var expected=Enumerable.Range(0,3).Select(i=>reversedView?green[i]*.125f+blue[i]*.25f+red[i]*.5f:red[i]*.125f+blue[i]*.25f+green[i]*.5f).ToArray();
    var error=Enumerable.Range(0,3).Max(i=>Math.Abs(actual[i]-expected[i]));
    Measurements.Add(new{phase="cross_instance_view_interleaving",view,creation,red,green,blue,actual,expected,maximumError=error});if(error>=.03f)errors.Add("Cross-instance plane interleaving differs from independent alpha-over oracle "+suffix);
   }
   var deleted=await Capture("cross_empty_after",Color.Black);Assert.IsTrue(deleted.Take(3).All(x=>Math.Abs(x)<.02));Assert.AreEqual(0,errors.Count,string.Join("; ",errors));
  }
  finally{await MainThread.Wait();if(widget is not null&&widget.IsValid){widget.OnPreFrame-=pre;widget.Close();widget.Destroy();}if(light.IsValid())light.Delete();scene?.Destroy();root?.Destroy();shader?.Destroy();}
 }
}
