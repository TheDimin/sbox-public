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
public sealed class AlphaNativeVmdlRegressionTests
{
 public static string CheckoutRoot {get;set;}
 public static string OutputDirectory {get;set;}
 public static string ShaderSha256 {get;set;}
 public static List<object> Measurements {get;}=new();
 [TestMethod]
 public async Task OrdinaryNativeVmdlFramebufferFlagDoesNotChangeRendering()
 {
  
  Assert.IsFalse(Directory.Exists(OutputDirectory));Directory.CreateDirectory(OutputDirectory);
  Shader shader=null;Material root=null;Scene scene=null;Editor.SceneRenderingWidget widget=null;SceneDirectionalLight light=null;Action pre=null;int frames=0;
  try
  {
   root=Material.Load("materials/default/white.vmat").CreateCopy();Assert.IsTrue(root is {IsValid:true,IsError:false});
   scene=Scene.CreateEditorScene();CameraComponent camera;using(scene.Push())camera=scene.CreateObject().Components.Create<CameraComponent>();
   camera.WorldPosition=new global::Vector3(200,0,0);camera.WorldRotation=Rotation.FromYaw(180);camera.Orthographic=true;camera.OrthographicHeight=60;camera.ZNear=1;camera.ZFar=1000;camera.CustomSize=new global::Vector2(192,192);camera.ClearFlags=ClearFlags.All;camera.EnablePostProcessing=false;camera.DebugMode=SceneCameraDebugMode.Normal;camera.AutoExposure.Enabled=false;camera.AutoExposure.MinimumExposure=1;camera.AutoExposure.MaximumExposure=1;camera.AutoExposure.Compensation=0;
   light=new SceneDirectionalLight(scene.SceneWorld,camera.WorldRotation,Color.White){ShadowsEnabled=false};widget=new Editor.SceneRenderingWidget{Scene=scene,Camera=camera,WindowTitle="glTF alpha candidate controls",Size=new global::Vector2(192,192)};pre=()=>frames++;widget.OnPreFrame+=pre;widget.Show();
   async Task Wait(){await MainThread.Wait();var start=frames;var watch=Stopwatch.StartNew();while(frames<start+3){if(watch.Elapsed>TimeSpan.FromSeconds(10))throw new InvalidOperationException("Capture preview did not render.");await Task.Delay(20);await MainThread.Wait();}}
   async Task<float[]> Capture(string label,Color background)
   {
    await MainThread.Wait();camera.BackgroundColor=background;await Wait();using(scene.Push()){scene.PreCameraRender();camera.InitializeRendering(false);if(camera.SceneCamera is not null)camera.SceneCamera.Tonemap.Enabled=false;}
    using var target=Texture.CreateRenderTarget().WithSize(192,192).WithFormat(ImageFormat.RGBA32323232F).WithInitialColor(Color.Magenta).Create("alpha_lit_"+Guid.NewGuid().ToString("N"));
    bool submitted;using(scene.Push()){target.Clear(Color.Magenta);submitted=camera.RenderToTexture(target);}Assert.IsTrue(submitted);await Wait();Graphics.FlushGPU();var pixels=new float[192*192*4];target.GetPixels((0,0,192,192),0,0,pixels.AsSpan(),ImageFormat.RGBA32323232F);using var bitmap=target.GetBitmap();var png=bitmap.ToPng();var pngPath=Path.Combine(OutputDirectory,label+".png");File.WriteAllBytes(pngPath,png);var center=pixels.Skip((96*192+96)*4).Take(4).ToArray();Assert.IsTrue(pixels.All(float.IsFinite));Measurements.Add(new{phase="float_capture",label,path=pngPath,sha256=Convert.ToHexStringLower(SHA256.HashData(png)),center});return pixels;
   }
   var model=Model.Load("models/dev/box.vmdl");Assert.IsTrue(model is {IsValid:true,IsError:false}&&!model.native.IsNull&&model.HasRenderMeshes());SceneModel obj=null;
   try
   {
    obj=new SceneModel(scene.SceneWorld,model,Transform.Zero);obj.SetMaterialOverride(root);obj.Flags.CastShadows=false;obj.Flags.WantsFrameBufferCopy=false;
    var before=await Capture("native_vmdl_before",Color.Black);int Coverage(float[] x)=>Enumerable.Range(0,192*192).Count(i=>Math.Max(x[4*i],Math.Max(x[4*i+1],x[4*i+2]))>.02f);Assert.IsTrue(Coverage(before)>100,"Native model positive control missing");
    obj.Flags.WantsFrameBufferCopy=true;Assert.IsTrue(obj.Flags.WantsFrameBufferCopy);var enabled=await Capture("native_vmdl_copy_flag_on",Color.Black);
    obj.Flags.WantsFrameBufferCopy=false;Assert.IsFalse(obj.Flags.WantsFrameBufferCopy);var after=await Capture("native_vmdl_copy_flag_off",Color.Black);
    (double Max,double Mean) Difference(float[] a,float[] b){double max=0,sum=0;for(int i=0;i<192*192;i++)for(int c=0;c<3;c++){double d=Math.Abs(a[4*i+c]-b[4*i+c]);max=Math.Max(max,d);sum+=d;}return(max,sum/(192*192*3));}
    var onDifference=Difference(before,enabled);var offDifference=Difference(before,after);var coverages=new[]{Coverage(before),Coverage(enabled),Coverage(after)};Measurements.Add(new{phase="ordinary_vmdl_regression",coverages,onDifference,offDifference,scope="Whole 192x192 float RGB image, stock opaque vmdl/white material, postprocessing disabled; no glTF/native-model substitution."});Assert.IsTrue(onDifference.Max<.03&&offDifference.Max<.03&&onDifference.Mean<.001&&offDifference.Mean<.001,"Native opaque vmdl whole-image RGB changed when toggling framebuffer-copy flag");
    obj.Delete();obj=null;using(scene.Push())scene.ProcessDeletes();await Wait();using(scene.Push())scene.ProcessDeletes();var deleted=await Capture("native_vmdl_deleted",Color.Black);Assert.AreEqual(0,Coverage(deleted),"Native vmdl deletion retained foreground");
   }
   finally{if(obj.IsValid())obj.Delete();using(scene.Push())scene.ProcessDeletes();}

  }
  finally{await MainThread.Wait();if(widget is not null&&widget.IsValid){widget.OnPreFrame-=pre;widget.Close();widget.Destroy();}if(light.IsValid())light.Delete();scene?.Destroy();root?.Destroy();shader?.Destroy();}
 }
}
