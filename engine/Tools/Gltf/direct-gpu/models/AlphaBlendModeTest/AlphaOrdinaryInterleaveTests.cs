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
public sealed class AlphaOrdinaryInterleaveTests
{
 public static string CheckoutRoot {get;set;}
 public static string OutputDirectory {get;set;}
 public static string ShaderSha256 {get;set;}
 public static List<object> Measurements {get;}=new();
 [TestMethod]
 public async Task GltfLayersInterleaveWithOrdinaryNativeObject()
 {
  var path=Path.Combine(CheckoutRoot,"engine/Tools/Gltf/direct-gpu/models/AlphaBlendModeTest/gltf_lit_alpha_candidate.shader");var bytes=File.ReadAllBytes(path);Assert.AreEqual(ShaderSha256,Convert.ToHexStringLower(SHA256.HashData(bytes)));
  Assert.IsFalse(Directory.Exists(OutputDirectory));Directory.CreateDirectory(OutputDirectory);
  Shader shader=null,ordinaryShader=null;Material root=null,ordinaryTemplate=null;Scene scene=null;Editor.SceneRenderingWidget widget=null;SceneDirectionalLight light=null;Action pre=null;int frames=0;
  try
  {
   using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(90));shader=await Shader.CompileAsync(Encoding.UTF8.GetString(bytes),timeout.Token);await MainThread.Wait();Assert.IsTrue(shader is {IsValid:true,IsAnonymous:true});root=Material.Create("lit_alpha_render_"+Guid.NewGuid().ToString("N"),shader);Assert.IsTrue(root is {IsValid:true,IsError:false});
   var ordinaryBytes=File.ReadAllBytes(Path.Combine(CheckoutRoot,"engine/Tools/Gltf/direct-gpu/models/AlphaBlendModeTest/literal_blend_control.shader"));Assert.AreEqual("4eff963e70a48a4a174b138a560cea9a97503536855b1bc17db383ba528d5a2f",Convert.ToHexStringLower(SHA256.HashData(ordinaryBytes)));using(var secondTimeout=new CancellationTokenSource(TimeSpan.FromSeconds(90)))ordinaryShader=await Shader.CompileAsync(Encoding.UTF8.GetString(ordinaryBytes),secondTimeout.Token);await MainThread.Wait();Assert.IsTrue(ordinaryShader is {IsValid:true,IsAnonymous:true});ordinaryTemplate=Material.Create("literal_blend_"+Guid.NewGuid().ToString("N"),ordinaryShader);ordinaryTemplate.SetFeature("F_TRANSLUCENT",1);Assert.IsTrue(ordinaryTemplate.Flags.IsTranslucent);
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
   async Task<float[]> Cross(bool compiled,string only,string label)
   {
    var input=compiled?GltfCompiler.Compile(sources[0].Bytes,null):sources[0].Bytes;using var document=GltfReader.Read(input);var materials=new List<Material>();GltfGpuAsset asset=null;GltfGpuInstance instance=null;SceneCustomObject ordinary=null;
    try
    {
     Material Resolve(int? slot){var source=document.ReadMaterial(slot.Value);var m=root.CreateCopy();materials.Add(m);m.SetFeature("F_TRANSLUCENT",1);var c=source.BaseColorFactor;var emission=source.EmissiveFactor;var channel=slot.Value==0?"green":"red";var alpha=only=="all"?c.W:only==channel?1f:0f;m.Set("g_vGltfBaseColor",new global::Vector4(c.X,c.Y,c.Z,alpha));m.Set("g_vGltfEmissiveFactor",new global::Vector4(emission.X,emission.Y,emission.Z,0));m.Set("g_flGltfMetallicFactor",0);m.Set("g_flGltfRoughnessFactor",1);return m;}
     asset=GltfGpuAsset.Create(document,"shaders/gltf_deform_cs.shader",Resolve);instance=asset.CreateInstance(scene.SceneWorld);
     var literal=ordinaryTemplate.CreateCopy();materials.Add(literal);literal.Set("g_vLiteralColor",new global::Vector4(0,0,1,only=="all"?.5f:only=="blue"?1:0));
     var x=.05f/.0254f;var half=.5f/.0254f;var vertices=new[]{new Vertex(new global::Vector3(x,-half,-half),global::Vector3.Forward,global::Vector3.Left,new global::Vector4(0,0,0,0)),new Vertex(new global::Vector3(x,half,-half),global::Vector3.Forward,global::Vector3.Left,new global::Vector4(1,0,0,0)),new Vertex(new global::Vector3(x,half,half),global::Vector3.Forward,global::Vector3.Left,new global::Vector4(1,1,0,0)),new Vertex(new global::Vector3(x,-half,-half),global::Vector3.Forward,global::Vector3.Left,new global::Vector4(0,0,0,0)),new Vertex(new global::Vector3(x,half,half),global::Vector3.Forward,global::Vector3.Left,new global::Vector4(1,1,0,0)),new Vertex(new global::Vector3(x,-half,half),global::Vector3.Forward,global::Vector3.Left,new global::Vector4(0,1,0,0))};
     var draws=0;ordinary=new SceneCustomObject(scene.SceneWorld);ordinary.Flags.IsOpaque=false;ordinary.Flags.IsTranslucent=true;ordinary.Flags.CastShadows=false;ordinary.Bounds=new BBox(new global::Vector3(x-.001f,-half,-half),new global::Vector3(x+.001f,half,half));ordinary.RenderOverride=_=>{if(Graphics.LayerType==SceneLayerType.Translucent){Graphics.Draw(vertices.AsSpan(),vertices.Length,literal);draws++;}};
     var result=await Capture(label,Color.Black);Assert.IsTrue(instance.DrawCalls>0&&instance.CallbackErrors.Length==0&&draws>0,"Both glTF and independent native object must submit");return result;
    }
    finally{await MainThread.Wait();if(ordinary.IsValid()){ordinary.RenderOverride=null;ordinary.Delete();}instance?.Dispose();asset?.Dispose();foreach(var m in materials)m.Destroy();using(scene.Push())scene.ProcessDeletes();await Wait();using(scene.Push())scene.ProcessDeletes();}
   }
   var empty=await Capture("cross_empty_before",Color.Black);Assert.IsTrue(empty.Take(3).All(x=>Math.Abs(x)<.02));var errors=new List<string>();
   for(var form=1;form<2;form++)
   {
    var red=await Cross(form==1,"red","cross_red_"+form);var delRed=await Capture("cross_red_deleted_"+form,Color.Black);Assert.IsTrue(delRed.Take(3).All(x=>Math.Abs(x)<.02),"red reference cleanup failed");var green=await Cross(form==1,"green","cross_green_"+form);var delGreen=await Capture("cross_green_deleted_"+form,Color.Black);Assert.IsTrue(delGreen.Take(3).All(x=>Math.Abs(x)<.02),"green reference cleanup failed");var blue=await Cross(form==1,"blue","cross_blue_"+form);var delBlue=await Capture("cross_blue_deleted_"+form,Color.Black);Assert.IsTrue(delBlue.Take(3).All(x=>Math.Abs(x)<.02),"blue reference cleanup failed");var actual=await Cross(form==1,"all","cross_all_"+form);
    if(red[0]<.1f||green[1]<.1f||blue[2]<.1f)errors.Add("Independent positive emission control absent "+form);
    var expected=Enumerable.Range(0,3).Select(i=>red[i]*.125f+blue[i]*.25f+green[i]*.5f).ToArray();var error=Enumerable.Range(0,3).Max(i=>Math.Abs(actual[i]-expected[i]));
    Measurements.Add(new{phase="cross_instance_interleaving",form,red,green,blue,actual,expected,maximumError=error});if(error>=.03f)errors.Add("Interleaving with independent native object differs from alpha-over oracle "+form);
   }
   var deleted=await Capture("cross_empty_after",Color.Black);Assert.IsTrue(deleted.Take(3).All(x=>Math.Abs(x)<.02));Assert.AreEqual(0,errors.Count,string.Join("; ",errors));
  }
  finally{await MainThread.Wait();if(widget is not null&&widget.IsValid){widget.OnPreFrame-=pre;widget.Close();widget.Destroy();}if(light.IsValid())light.Delete();scene?.Destroy();ordinaryTemplate?.Destroy();ordinaryShader?.Destroy();root?.Destroy();shader?.Destroy();}
 }
}
