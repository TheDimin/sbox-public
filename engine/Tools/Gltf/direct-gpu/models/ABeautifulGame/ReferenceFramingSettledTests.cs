using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox;
using Sandbox.Gltf;
using Assert=Microsoft.VisualStudio.TestTools.UnitTesting.Assert;
namespace Sandbox.Gltf.DirectGpu.ABeautifulGameValidation;
[TestClass]
internal sealed class ReferenceFramingSettledTests
{
 static string checkout,configPath;public static List<object> Measurements {get;}=new();public static void Configure(string root,string path){checkout=root;configPath=path;Measurements.Clear();}
 [TestMethod]
 public async Task CanonicalPublicModelReferenceCameraSweepWithActualHdrEnvironment()
 {
  await MainThread.Wait();using var config=JsonDocument.Parse(File.ReadAllBytes(configPath));var c=config.RootElement;string texturePath=c.GetProperty("texturePath").GetString(),sourcePath=c.GetProperty("sourcePath").GetString(),compiledPath=c.GetProperty("compiledPath").GetString();Assert.AreEqual(c.GetProperty("sourceSha256").GetString(),Hash(File.ReadAllBytes(sourcePath)));Assert.AreEqual(c.GetProperty("compiledSha256").GetString(),Hash(File.ReadAllBytes(compiledPath)));float yaw=c.GetProperty("yawDegrees").GetSingle();
  string sourceDir=Path.Combine(checkout,"Temp/gltf-reference/glTF-Sample-Assets/Models/ABeautifulGame/glTF");byte[] source=File.ReadAllBytes(Path.Combine(sourceDir,"ABeautifulGame.gltf"));Assert.AreEqual("2e1b63ae34173c6e3251f749b1b8b97a32e92517aab2cfd4bfffd9158f4dd278",Hash(source));byte[] Resolve(string uri)=>File.ReadAllBytes(Path.Combine(sourceDir,uri));byte[] compiled=GltfCompiler.Compile(source,Resolve);
  string output=Path.Combine(checkout,"Temp/gltf-validation/direct-gpu/ABeautifulGame/reference-framing-settled",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);string dir=Path.Combine(Path.GetTempPath(),"gltf_beautiful_reference_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
  LocalFileSystem mount=null;Model model=null;ModelRenderer renderer=null;SceneCubemap probe=null;using var capture=new Capture(output);
  try
  {
   string bundle="beautiful_reference_"+Guid.NewGuid().ToString("N");string bundleDir=Path.Combine(dir,bundle);Directory.CreateDirectory(bundleDir);foreach(var path in Directory.GetFiles(sourceDir))if(Path.GetExtension(path)==".jpg")File.Copy(path,Path.Combine(bundleDir,Path.GetFileName(path)));string name=bundle+"/model.glb_c";File.WriteAllBytes(Path.Combine(bundleDir,"model.glb_c"),compiled);mount=new LocalFileSystem(dir);FileSystem.Mounted.Mount(mount);model=Model.Load(name);Assert.IsTrue(model is {IsValid:true,IsError:false} && model.native.IsNull && model.MeshCount==49);Assert.AreEqual(15,model.Materials.Length);
   var texture=Texture.Load(texturePath);Assert.IsTrue(texture is {IsValid:true,IsError:false} && texture.Depth==6 && texture.Mips>1);
   capture.View(new Vector3(-15.74548f,-12.19278f,4.90470f),new Vector3(1.22496f,-2.72452f,.17646f),38.54507f);
   var projected=capture.ProjectLandmarks();Measurements.Add(new{phase="native_projection",size="800x620",axis="vertical",vertical_fov_degrees=38.54507,points=projected,expected_reference_scale_pixels=new[]{new[]{220.65f,71.76f},new[]{218.51f,269.61f},new[]{111.17f,63.06f},new[]{119.61f,244.90f},new[]{373.70f,54.75f},new[]{366.98f,129.07f},new[]{21.18f,18.43f},new[]{39.64f,220.57f}},scope="Native CameraComponent point projection versus independent fit, 400x310 reference coordinate scale; annotated mesh-extrema centers approximate silhouettes"});
   var expected=new[]{new Vector2(220.65f,71.76f),new Vector2(218.51f,269.61f),new Vector2(111.17f,63.06f),new Vector2(119.61f,244.90f),new Vector2(373.70f,54.75f),new Vector2(366.98f,129.07f),new Vector2(21.18f,18.43f),new Vector2(39.64f,220.57f)};for(int i=0;i<expected.Length;i++)Assert.IsTrue((projected[i]*.5f-expected[i]).Length<2,"Native camera projection disagrees with independent pinhole fit at landmark "+i);
   probe=new SceneCubemap(capture.Scene.SceneWorld,texture,BBox.FromPositionAndSize(Vector3.Zero,1000));probe.Projection=SceneCubemap.ProjectionMode.Sphere;probe.Transform=Transform.Zero.WithRotation(Rotation.FromYaw(270));probe.TintColor=Color.White;probe.Feathering=.01f;probe.RenderDirty();Assert.IsTrue(probe.IsValid());
   using(capture.Scene.Push()){renderer=capture.Scene.CreateObject().Components.Create<ModelRenderer>(false);renderer.Model=model;renderer.Enabled=true;capture.Scene.EditorTick(0,0);}var instance=renderer.GltfInstance;Assert.IsNotNull(instance);await capture.Settle();var prior=await capture.Take("warmup_1");for(int warm=2;warm<=4;warm++){await capture.Settle();prior=await capture.Take("warmup_"+warm);}await capture.Settle();var frame=await capture.Take("fitted_candidate");int settledChanges=prior.Pixels.Zip(frame.Pixels,(a,b)=>Math.Abs(a.r-b.r)>.01f||Math.Abs(a.g-b.g)>.01f||Math.Abs(a.b-b.b)>.01f).Count(x=>x);Measurements.Add(new{phase="render_settle",changed_pixels=settledChanges,previous_path=prior.Path,previous_sha256=prior.Sha,scope="Consecutive same-camera post-process settled render target images; differences can include texture streaming and temporal effects"});Assert.IsTrue(instance.DrawCalls>0);Assert.AreEqual(0,instance.CallbackErrors.Length);var state=capture.VerifyExposure();Measurements.Add(new{input_sha256=Hash(compiled),path=frame.Path,sha256=frame.Sha,environment_yaw=270,exposure=state,scope="Canonical public model, fitted camera; optical materials remain unsupported fallback"});
   var actualObject=instance.SceneObject;using(capture.Scene.Push()){renderer.GameObject.Destroy();capture.Scene.EditorTick(0,0);capture.Scene.ProcessDeletes();}renderer=null;Assert.IsFalse(actualObject.IsValid());int calls=instance.DrawCalls;var after=await capture.Take("empty_after_delete");Assert.AreEqual(calls,instance.DrawCalls);int nonuniform=Nonuniform(after.Pixels);Measurements.Add(new{phase="deletion_control",path=after.Path,sha256=after.Sha,nonuniform_pixels=nonuniform});Assert.IsTrue(nonuniform<100);

  }
  finally{await MainThread.Wait();try{if(renderer!=null){using var scope=capture.Scene.Push();renderer.GameObject.Destroy();capture.Scene.EditorTick(0,0);capture.Scene.ProcessDeletes();}if(probe.IsValid())probe.Delete();model?.Destroy();}finally{try{if(mount!=null)FileSystem.Mounted.UnMount(mount);}finally{mount?.Dispose();Directory.Delete(dir,true);}}}
 }
 static int Nonuniform(Color[] pixels){var b=pixels[0];return pixels.Count(a=>Math.Abs(a.r-b.r)>.01f||Math.Abs(a.g-b.g)>.01f||Math.Abs(a.b-b.b)>.01f);}
 static string Hash(byte[] b)=>Convert.ToHexStringLower(SHA256.HashData(b));
 sealed class Capture:IDisposable
 {
  public record Frame(Color[] Pixels,string Path,string Sha);public readonly Scene Scene;readonly CameraComponent camera;readonly Tonemapping tone;readonly Editor.SceneRenderingWidget widget;readonly string output;readonly Action onFrame;int frames;
  public Capture(string path){output=path;Scene=Scene.CreateEditorScene();using(Scene.Push()){camera=Scene.CreateObject().Components.Create<CameraComponent>();camera.Orthographic=false;camera.FovAxis=CameraComponent.Axis.Vertical;camera.ZNear=.1f;camera.ZFar=1000;camera.CustomSize=new Vector2(800,620);camera.BackgroundColor=new Color(.65f,.62f,.58f,1);camera.ClearFlags=ClearFlags.All;camera.EnablePostProcessing=true;camera.DebugMode=SceneCameraDebugMode.Normal;tone=camera.GameObject.Components.Create<Tonemapping>();tone.Mode=Tonemapping.TonemappingMode.ACES;tone.AutoExposureEnabled=true;tone.MinimumExposure=1;tone.MaximumExposure=1;tone.ExposureCompensation=2;Scene.EditorTick(0,0);}widget=new Editor.SceneRenderingWidget{Scene=Scene,Camera=camera,WindowTitle="A Beautiful Game reference reconstruction",Size=new Vector2(800,620)};onFrame=()=>frames++;widget.OnPreFrame+=onFrame;widget.Show();}
  public void SetExposure(float value){using(Scene.Push()){tone.ExposureCompensation=value;Scene.EditorTick(0,0);}}
  public object VerifyExposure(){var e=camera.AutoExposure;Assert.IsTrue(e.Enabled && Math.Abs(e.MinimumExposure-1)<.0001f && Math.Abs(e.MaximumExposure-1)<.0001f && Math.Abs(e.Compensation-2)<.0001f,"ACES exposure settings were overwritten at render time");return new{e.Enabled,e.MinimumExposure,e.MaximumExposure,e.Compensation,e.Rate,scope="Camera settings observed after render; not photometric proof of Babylon tone-curve equivalence"};}
  public void View(Vector3 pos,Vector3 target,float fov){using(Scene.Push()){camera.WorldPosition=pos;camera.WorldRotation=Rotation.LookAt(target-pos,Vector3.Up);camera.FieldOfView=fov;Scene.EditorTick(0,0);}}
  public Vector2[] ProjectLandmarks(){var points=new[]{new Vector3(-8.65f,-8.65f,4.484f),new Vector3(-8.65f,-8.65f,.662f),new Vector3(-8.70f,-6.225f,4.595f),new Vector3(-8.70f,-6.225f,.653f),new Vector3(8.65f,-8.66f,4.53f),new Vector3(8.65f,-8.66f,.709f),new Vector3(-8.568f,-3.555f,5.536f),new Vector3(-8.568f,-3.555f,.653f)};return points.Select(point=>camera.PointToScreenPixels(point,out _)).ToArray();}
  async Task Wait(){await MainThread.Wait();var start=frames;var timer=Stopwatch.StartNew();while(frames<start+3||Graphics.IsActive){Assert.IsTrue(timer.Elapsed<TimeSpan.FromSeconds(15),"Preview frames stalled");await Task.Delay(20);await MainThread.Wait();}}
  public async Task Settle(){for(int i=0;i<20;i++)await Wait();}
  public async Task<Frame> Take(string name){await Wait();using(Scene.Push()){Scene.PreCameraRender();camera.InitializeRendering(false);}using var target=Texture.CreateRenderTarget().WithSize(800,620).WithFormat(ImageFormat.RGBA8888).WithInitialColor(Color.Magenta).Create("beautiful_reference_"+Guid.NewGuid().ToString("N"));bool ok;using(Scene.Push()){target.Clear(Color.Magenta);ok=camera.RenderToTexture(target);}Assert.IsTrue(ok);await Wait();using var bitmap=target.GetBitmap();var pixels=bitmap.GetPixels();Assert.AreEqual(800*620,pixels.Length);byte[] png=bitmap.ToPng();string path=Path.Combine(output,name+".png");File.WriteAllBytes(path,png);return new(pixels,path,Hash(png));}
  public void Dispose(){if(widget.IsValid){widget.OnPreFrame-=onFrame;widget.Close();widget.Destroy();}Scene.Destroy();}
 }
}
