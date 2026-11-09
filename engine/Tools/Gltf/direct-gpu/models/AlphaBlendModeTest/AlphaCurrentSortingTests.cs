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
public sealed class AlphaCurrentSortingTests
{
 public static string CheckoutRoot {get;set;}
 public static string OutputDirectory {get;set;}
 public static string ShaderSha256 {get;set;}
 public static List<object> Measurements {get;}=new();
 [TestMethod]
 public async Task ViewLocalTransparentOrderAndTransformedInstances()
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
   async Task<float[]> Render(AlphaControlFixtures.Fixture fixture,bool compiled,string label,int referenceSlot=-1,Color? background=null,bool reverseView=false,float translation=0,bool overrideBlend=false)
   {
    camera.WorldPosition=new global::Vector3(reverseView?-200:200,0,0);camera.WorldRotation=Rotation.FromYaw(reverseView?0:180);light.Rotation=camera.WorldRotation;var input=compiled?GltfCompiler.Compile(fixture.Bytes,null):fixture.Bytes;using var doc=GltfReader.Read(input);var materials=new List<Material>();var textures=new List<global::Sandbox.GltfMaterialLease<Texture>>();GltfGpuAsset asset=null;GltfGpuInstance instance=null;
    try
    {
     Material Resolve(int? index){var source=doc.ReadMaterial(index.Value);var m=root.CreateCopy();materials.Add(m);m.SetFeature("F_ALPHA_TEST",source.AlphaMode=="MASK"?1:0);m.SetFeature("F_TRANSLUCENT",source.AlphaMode=="BLEND"?1:0);m.SetFeature("F_RENDER_BACKFACES",source.DoubleSided||reverseView?1:0);if(source.DoubleSided||reverseView)m.Set("g_flGltfDoubleSided",1);var c=source.BaseColorFactor;m.Set("g_vGltfBaseColor",new global::Vector4(c.X,c.Y,c.Z,referenceSlot<0?c.W:(index.Value==referenceSlot?1:0)));m.Set("g_flGltfAlphaCutoff",source.AlphaCutoff);m.Set("g_flGltfMetallicFactor",0);m.Set("g_flGltfRoughnessFactor",1);if(source.BaseColorTexture is not null){var lease=global::Sandbox.GltfNativeTextureCache.Acquire(global::Sandbox.GltfTextureCompiler.Compile(doc.ReadImage(0),true));textures.Add(lease);m.Set("g_tGltfBaseColorTexture",lease.Value);m.Set("g_flGltfHasBaseColorTexture",1);}return m;}
     asset=GltfGpuAsset.Create(doc,"shaders/gltf_deform_cs.shader",Resolve);instance=asset.CreateInstance(scene.SceneWorld);if(translation!=0)instance.Placement=System.Numerics.Matrix4x4.CreateTranslation(translation,0,0);if(overrideBlend)instance.SetMaterialOverride(0,materials[0]);if(label=="blend_near_first_1"){var pose=doc.EvaluatePose();instance.SetPose(pose);var timer=Stopwatch.StartNew();for(var i=0;i<50;i++)instance.SetPose(pose);timer.Stop();Measurements.Add(new{phase="pose_update_small_fixture_timing",iterations=50,total_ms=timer.Elapsed.TotalMilliseconds,draws=2,scope="wall clock including pose evaluation bounds and GPU buffer updates; no before baseline or general performance claim"});}var result=await Capture(label,background??Color.Black);Assert.IsTrue(instance.DrawCalls>0&&instance.CallbackErrors.Length==0,"No actual GPU submission: "+string.Join(";",instance.CallbackErrors));if(overrideBlend)Assert.IsTrue(instance.Readback().Any(row=>row.SubmittedMaterialOverride&&ReferenceEquals(row.SubmittedMaterial,materials[0])),"Explicit effective blend override was not submitted.");return result;
    }
    finally{await MainThread.Wait();instance?.Dispose();asset?.Dispose();foreach(var m in materials)m.Destroy();foreach(var t in textures)t.Dispose();using(scene.Push())scene.ProcessDeletes();}
   }
   var fixtures=AlphaControlFixtures.All().ToDictionary(f=>f.Name);var empty=await Capture("empty_before",Color.Black);Assert.IsTrue(empty.Take(3).All(x=>Math.Abs(x)<.02));
   var errors=new List<string>();
   for(var form=1;form<2;form++)
   {
    static float Difference(float[] a,float[] b)=>Enumerable.Range(0,3).Max(i=>Math.Abs(a[i]-b[i]));
    var nearFirst=await Render(fixtures["two_separated_blend_layers_near_first"],form==1,"blend_near_first_"+form);var farFirst=await Render(fixtures["two_separated_blend_layers_far_first"],form==1,"blend_far_first_"+form);var nearReference=await Render(fixtures["two_separated_blend_layers_near_first"],form==1,"blend_near_reference_"+form,0);var farReference=await Render(fixtures["two_separated_blend_layers_near_first"],form==1,"blend_far_reference_"+form,1);var expected=Enumerable.Range(0,3).Select(i=>nearReference[i]*.5f+farReference[i]*.125f).ToArray();
    Measurements.Add(new{phase="literal_layer_oracle",form,expected,nearFirst,farFirst,nearReference,farReference});
    foreach(var reverse in new[]{false,true}) foreach(var shifted in new[]{false,true})
    {
     var label="view_"+(reverse?"reverse":"forward")+"_"+(shifted?"translated":"identity")+"_"+form;var delta=shifted?20f:0f;
     var a=await Render(fixtures["two_separated_blend_layers_near_first"],form==1,label+"_near_first",reverseView:reverse,translation:delta);
     var b=await Render(fixtures["two_separated_blend_layers_far_first"],form==1,label+"_far_first",reverseView:reverse,translation:delta);
     var red=await Render(fixtures["two_separated_blend_layers_near_first"],form==1,label+"_red",0,reverseView:reverse,translation:delta);
     var green=await Render(fixtures["two_separated_blend_layers_near_first"],form==1,label+"_green",1,reverseView:reverse,translation:delta);
     var oracle=Enumerable.Range(0,3).Select(i=>reverse?red[i]*.375f+green[i]*.25f:red[i]*.5f+green[i]*.125f).ToArray();
     var overridden=await Render(fixtures["two_separated_blend_layers_near_first"],form==1,label+"_override",reverseView:reverse,translation:delta,overrideBlend:true);
     Measurements.Add(new{phase="view_local_sorted_alpha",form,reverse,translation=delta,nearFirst=a,farFirst=b,red,green,oracle,overridden});
     if(red.Take(3).Sum()<.1f||green.Take(3).Sum()<.1f)errors.Add("Independent positive foreground missing "+label);
     if(Difference(a,b)>=.03f||Difference(a,oracle)>=.03f||Difference(b,oracle)>=.03f||Difference(overridden,oracle)>=.03f)errors.Add("View-local alpha-over order "+label);
    }
    if(Difference(nearFirst,farFirst)>=.03f)errors.Add("Separated transparent planes changed with source node order "+form);if(Difference(farFirst,expected)>=.03f)errors.Add("Far-first composition differs from independent alpha-over references "+form);if(Difference(nearFirst,expected)>=.03f)errors.Add("Near-first composition differs from independent alpha-over references "+form);
   }
   camera.WorldPosition=new global::Vector3(200,0,0);camera.WorldRotation=Rotation.FromYaw(180);var deleted=await Capture("empty_after",Color.Black);Assert.IsTrue(deleted.Take(3).All(x=>Math.Abs(x)<.02));Assert.AreEqual(0,errors.Count,string.Join("; ",errors));
  }
  finally{await MainThread.Wait();if(widget is not null&&widget.IsValid){widget.OnPreFrame-=pre;widget.Close();widget.Destroy();}if(light.IsValid())light.Delete();scene?.Destroy();root?.Destroy();shader?.Destroy();}
 }
}
