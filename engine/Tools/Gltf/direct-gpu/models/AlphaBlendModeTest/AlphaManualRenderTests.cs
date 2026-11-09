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
public sealed class AlphaManualRenderTests
{
 public static string CheckoutRoot {get;set;}
 public static string OutputDirectory {get;set;}
 public static string ShaderSha256 {get;set;}
 public static List<object> Measurements {get;}=new();
 [TestMethod]
 public async Task DirectGraphicsRenderIncludesEveryDrawAndMaterialOverride()
 {
  var path=Path.Combine(CheckoutRoot,"engine/Tools/Gltf/direct-gpu/models/AlphaBlendModeTest/gltf_lit_alpha_candidate.shader");var bytes=File.ReadAllBytes(path);Assert.AreEqual(ShaderSha256,Convert.ToHexStringLower(SHA256.HashData(bytes)));
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
   var fixture=AlphaControlFixtures.All().Single(x=>x.Name=="two_separated_blend_layers_near_first");var input=GltfCompiler.Compile(fixture.Bytes,null);using var doc=GltfReader.Read(input);var materials=new List<Material>();GltfGpuAsset asset=null;GltfGpuInstance instance=null;SceneCustomObject driver=null;
   try
   {
    Material Resolve(int? index){var source=doc.ReadMaterial(index.Value);var m=root.CreateCopy();materials.Add(m);m.SetFeature("F_TRANSLUCENT",1);var c=source.BaseColorFactor;m.Set("g_vGltfBaseColor",new global::Vector4(c.X,c.Y,c.Z,c.W));m.Set("g_flGltfMetallicFactor",0);m.Set("g_flGltfRoughnessFactor",1);return m;}
    asset=GltfGpuAsset.Create(doc,"shaders/gltf_deform_cs.shader",Resolve);instance=asset.CreateInstance(scene.SceneWorld);
    var automatic=await Capture("automatic",Color.Black);Assert.IsTrue(automatic.Take(3).Sum()>.1f,"Automatic source control has no foreground.");
    instance.SceneObject.RenderingEnabled=false;var calls=0;Exception callbackError=null;Material manualOverride=null;
    driver=new SceneCustomObject(scene.SceneWorld);driver.Flags.IsOpaque=true;driver.Flags.IsTranslucent=false;driver.Flags.CastShadows=false;driver.Bounds=instance.SceneObject.Bounds;driver.RenderOverride=_=>{try{Graphics.Render(instance.SceneObject,material:manualOverride);calls++;}catch(Exception ex){callbackError=ex;}};
    var manual=await Capture("direct_manual",Color.Black);Assert.IsTrue(calls>0&&callbackError is null,"Direct manual render failed: "+callbackError);var diff=Enumerable.Range(0,3).Max(i=>Math.Abs(manual[i]-automatic[i]));Assert.IsTrue(diff<.03f,"Direct manual path omitted/duplicated/reordered a source draw: "+diff);
    manualOverride=Material.Load("materials/default/white.vmat");var overridden=await Capture("direct_manual_override",Color.Black);Assert.IsTrue(callbackError is null,"Direct manual override failed: "+callbackError);var change=Enumerable.Range(0,3).Max(i=>Math.Abs(overridden[i]-manual[i]));Assert.IsTrue(change>.05f,"Direct material override did not alter actual pixels.");
    Measurements.Add(new{phase="direct_graphics_render",automatic,manual,overridden,diff,change,calls,scope="direct Graphics.Render of disabled primary; native driver callback; material override; canonical compiled fixture"});
    driver.RenderOverride=null;driver.Delete();driver=null;instance.Dispose();instance=null;asset.Dispose();asset=null;foreach(var m in materials)m.Destroy();materials.Clear();using(scene.Push())scene.ProcessDeletes();await Wait();using(scene.Push())scene.ProcessDeletes();var deleted=await Capture("manual_deleted",Color.Black);Assert.IsTrue(deleted.Take(3).All(x=>Math.Abs(x)<.02),"Manual control retained pixels after delete.");
   }
   finally{await MainThread.Wait();if(driver.IsValid()){driver.RenderOverride=null;driver.Delete();}instance?.Dispose();asset?.Dispose();foreach(var m in materials)m.Destroy();using(scene.Push())scene.ProcessDeletes();}
  }
  finally{await MainThread.Wait();if(widget is not null&&widget.IsValid){widget.OnPreFrame-=pre;widget.Close();widget.Destroy();}if(light.IsValid())light.Delete();scene?.Destroy();root?.Destroy();shader?.Destroy();}
 }
}
