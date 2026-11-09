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
public sealed class AlphaNativeVmdlMutationTests
{
 public static string CheckoutRoot {get;set;}
 public static string OutputDirectory {get;set;}
 public static string ShaderSha256 {get;set;}
 public static List<object> Measurements {get;}=new();
 [TestMethod]
 public async Task ObserveNativeVmdlMaterialFeatureMutation()
 {
  
  Assert.IsFalse(Directory.Exists(OutputDirectory));Directory.CreateDirectory(OutputDirectory);
  Shader shader=null;Material root=null;Scene scene=null;Editor.SceneRenderingWidget widget=null;SceneDirectionalLight light=null;Action pre=null;int frames=0;
  try
  {
   root=Material.Load("materials/default/white.vmat").CreateCopy();Assert.IsTrue(root is {IsValid:true,IsError:false});
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
   var model=Model.Load("models/dev/box.vmdl");Assert.IsTrue(model is {IsValid:true,IsError:false}&&!model.native.IsNull&&model.HasRenderMeshes());
   var opaque=root.CreateCopy();var changed=root.CreateCopy();var blend=root.CreateCopy();SceneModel obj=null;
   try
   {
    opaque.SetFeature("F_TRANSLUCENT",0);changed.SetFeature("F_TRANSLUCENT",0);blend.SetFeature("F_TRANSLUCENT",1);
    Assert.IsFalse(opaque.Flags.IsTranslucent);Assert.IsFalse(changed.Flags.IsTranslucent);Assert.IsTrue(blend.Flags.IsTranslucent);
    obj=new SceneModel(scene.SceneWorld,model,Transform.Zero);obj.ColorTint=new Color(1,1,1,.5f);obj.Flags.CastShadows=false;
    object State()=>new{material=changed.Flags.IsTranslucent,sceneOpaque=obj.Flags.IsOpaque,sceneTranslucent=obj.Flags.IsTranslucent};
    obj.SetMaterialOverride(opaque);var opaqueReference=await Capture("vmdl_opaque_reference",Color.Black);
    obj.SetMaterialOverride(blend);var blendReference=await Capture("vmdl_blend_reference",Color.Black);
    Assert.IsTrue(opaqueReference.Take(3).Sum()>.1f,"Opaque native vmdl must be visible");
    var separation=Enumerable.Range(0,3).Max(i=>Math.Abs(opaqueReference[i]-blendReference[i]));Assert.IsTrue(separation>.05f,"Native vmdl controls do not distinguish passes");
    obj.SetMaterialOverride(changed);var before=await Capture("vmdl_before_mutation",Color.Black);var beforeState=State();
    changed.SetFeature("F_TRANSLUCENT",1);Assert.IsTrue(changed.Flags.IsTranslucent);var mutated=await Capture("vmdl_after_mutation",Color.Black);var mutatedState=State();
    obj.SetMaterialOverride(null);obj.SetMaterialOverride(changed);var reassigned=await Capture("vmdl_after_reassign",Color.Black);var reassignedState=State();
    changed.SetFeature("F_TRANSLUCENT",0);var reset=await Capture("vmdl_after_reset",Color.Black);var resetState=State();
    float Diff(float[] x,float[] y)=>Enumerable.Range(0,3).Max(i=>Math.Abs(x[i]-y[i]));
    Measurements.Add(new{phase="vmdl_feature_mutation",opaqueReference,blendReference,before,mutated,reassigned,reset,beforeState,mutatedState,reassignedState,resetState,separation,mutatedToBlend=Diff(mutated,blendReference),mutatedToOpaque=Diff(mutated,opaqueReference),reassignedToBlend=Diff(reassigned,blendReference),resetToOpaque=Diff(reset,opaqueReference),scope="Native vmdl direct scene object; observations do not claim glTF parity."});
    Assert.IsTrue(Diff(before,opaqueReference)<.03f,"Initial native opaque assignment differs from control");Assert.IsTrue(Diff(reassigned,blendReference)<.03f,"Reassignment positive control did not select blended material");
   }
   finally{if(obj.IsValid())obj.Delete();using(scene.Push())scene.ProcessDeletes();opaque.Destroy();changed.Destroy();blend.Destroy();}

  }
  finally{await MainThread.Wait();if(widget is not null&&widget.IsValid){widget.OnPreFrame-=pre;widget.Close();widget.Destroy();}if(light.IsValid())light.Delete();scene?.Destroy();root?.Destroy();shader?.Destroy();}
 }
}
