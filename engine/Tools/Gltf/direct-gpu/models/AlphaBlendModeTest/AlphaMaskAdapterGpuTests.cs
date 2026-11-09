using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Sandbox.Gltf.DirectGpu.AlphaBlendModeValidation;
[TestClass]
public sealed class AlphaMaskAdapterGpuTests
{
 public static string CheckoutRoot {get;set;}
 public static string OutputDirectory {get;set;}
 public static string ShaderSha256 {get;set;}
 public static List<object> Measurements {get;}=new();
 [TestMethod]
 public async Task ProjectedSourceAdapterPreservesMaskCoverage()
 {
  var path=Path.Combine(CheckoutRoot,"engine/Tools/Gltf/direct-gpu/models/AlphaBlendModeTest/gltf_lit_mask_candidate.shader");var bytes=File.ReadAllBytes(path);Assert.AreEqual(ShaderSha256,Convert.ToHexStringLower(SHA256.HashData(bytes)));
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
   async Task<float[]> Render(AlphaControlFixtures.Fixture fixture,bool compiled,string label,int referenceSlot=-1,Color? background=null,bool reverseView=false,float translation=0)
   {
    camera.WorldPosition=new global::Vector3(reverseView?-200:200,0,0);camera.WorldRotation=Rotation.FromYaw(reverseView?0:180);var input=compiled?GltfCompiler.Compile(fixture.Bytes,null):fixture.Bytes;using var doc=GltfReader.Read(input);var materials=new List<Material>();GltfGpuAsset asset=null;GltfGpuInstance instance=null;
    try
    {
     Material Select(GltfMaterial source,GltfGpuPrimitivePlan plan){var template=root.CreateCopy();materials.Add(template);template.SetFeature("F_ALPHA_TEST",source.AlphaMode=="MASK"?1:0);template.SetFeature("F_RENDER_BACKFACES",source.DoubleSided?1:0);if(source.AlphaMode=="MASK")Assert.IsTrue(template.Flags.IsAlphaTest&&!template.Flags.IsTranslucent,"Native MASK classification absent.");return template;}
     asset=GltfGpuAsset.PrepareProjected(doc,"shaders/gltf_deform_cs.shader",1024*1024,Select,global::Sandbox.GltfUnlitMaterialPlan.Features.All,sourceLitFeatureSidedness:true);instance=asset.CreateInstance(scene.SceneWorld);var result=await Capture(label,background??Color.Black);Assert.IsTrue(instance.DrawCalls>0&&instance.CallbackErrors.Length==0,"No actual GPU submission: "+string.Join(";",instance.CallbackErrors));return result;
    }
    finally{await MainThread.Wait();instance?.Dispose();asset?.Dispose();foreach(var m in materials)m.Destroy();using(scene.Push())scene.ProcessDeletes();}
   }
   var fixtures=AlphaControlFixtures.All().ToDictionary(f=>f.Name);var empty=await Capture("empty_before",Color.Black);Assert.IsTrue(empty.Take(3).All(x=>Math.Abs(x)<.02));
   var errors=new List<string>();
   for(var form=0;form<2;form++)
   {
    var blackControl=await Capture("empty_black_"+form,Color.Black);var whiteControl=await Capture("empty_white_"+form,Color.White);if(Enumerable.Range(0,3).Any(i=>Math.Abs(blackControl[i])>.02f||Math.Abs(whiteControl[i]-1)>.02f))errors.Add("linear background control "+form);
    AlphaControlFixtures.Fixture Mask(int components,float cutoff)
    {
     var source=fixtures["linear_texture_factor_color"+components];var json=JsonNode.Parse(source.Bytes)!.AsObject();json["materials"]![0]!["alphaMode"]="MASK";json["materials"]![0]!["alphaCutoff"]=cutoff;
     return new AlphaControlFixtures.Fixture(source.Name+"_mask",Encoding.UTF8.GetBytes(json.ToJsonString()),[],"texture/factor/vertex cutoff");
    }
    var rgb=await Render(Mask(3,.1f),form==1,"rgb_pass_"+form);var rgba=await Render(Mask(4,.1f),form==1,"rgba_discard_"+form);var equalRgb=await Render(Mask(3,.1875f),form==1,"rgb_equal_"+form);var equalRgba=await Render(Mask(4,.046875f),form==1,"rgba_equal_"+form);var discardWhite=await Render(Mask(4,.1f),form==1,"rgba_discard_white_"+form,background:Color.White);
    float Diff(float[] a,float[] b)=>Enumerable.Range(0,3).Max(i=>Math.Abs(a[i]-b[i]));
    Measurements.Add(new{phase="mask_texture_factor_vertex_cutoff",form,rgb,rgba,equalRgb,equalRgba,discardWhite});
    if(rgb.Take(3).Sum()<.1f||rgba.Take(3).Any(x=>Math.Abs(x)>.02f)||discardWhite.Take(3).Any(x=>Math.Abs(x-1)>.02f)||Diff(rgb,equalRgb)>.03f||Diff(rgb,equalRgba)>.03f)errors.Add("MASK texture/factor/vertex product or equality form "+form);

   }
   camera.WorldPosition=new global::Vector3(200,0,0);camera.WorldRotation=Rotation.FromYaw(180);var deleted=await Capture("empty_after",Color.Black);Assert.IsTrue(deleted.Take(3).All(x=>Math.Abs(x)<.02));Assert.AreEqual(0,errors.Count,string.Join("; ",errors));
  }
  finally{await MainThread.Wait();if(widget is not null&&widget.IsValid){widget.OnPreFrame-=pre;widget.Close();widget.Destroy();}if(light.IsValid())light.Delete();scene?.Destroy();root?.Destroy();shader?.Destroy();}
 }
}
