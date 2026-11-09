using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox;
using Sandbox.Gltf;
using Assert=Microsoft.VisualStudio.TestTools.UnitTesting.Assert;
namespace Sandbox.Gltf.DirectGpu.ABeautifulGameValidation;
[TestClass]
internal sealed class PresentationTests
{
 static string checkout;public static List<object> Measurements {get;}=new();public static void Configure(string root){checkout=root;Measurements.Clear();}
 [TestMethod]
 public async Task InferredReferenceViewSourceAndCompiled()
 {
  await MainThread.Wait();string sourceDir=Path.Combine(checkout,"Temp/gltf-reference/glTF-Sample-Assets/Models/ABeautifulGame/glTF");byte[] source=File.ReadAllBytes(Path.Combine(sourceDir,"ABeautifulGame.gltf"));byte[] Resolve(string uri)=>File.ReadAllBytes(Path.Combine(sourceDir,uri));byte[] compiled=GltfCompiler.Compile(source,Resolve);
  string output=Path.Combine(checkout,"Temp/gltf-validation/direct-gpu/ABeautifulGame/presentation",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);string dir=Path.Combine(Path.GetTempPath(),"gltf_beautiful_presentation_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
  LocalFileSystem mount=null;Model first=null,second=null;ModelRenderer renderer=null;using var capture=new Capture(output);
  try
  {
   foreach(var f in Directory.GetFiles(sourceDir))File.Copy(f,Path.Combine(dir,Path.GetFileName(f)));string id=Guid.NewGuid().ToString("N");string sourceName="beautiful_presentation_"+id+".gltf";string compiledName="beautiful_presentation_"+id+".glb_c";File.WriteAllBytes(Path.Combine(dir,sourceName),source);File.WriteAllBytes(Path.Combine(dir,compiledName),compiled);
   mount=new LocalFileSystem(dir);FileSystem.Mounted.Mount(mount);first=Model.Load(sourceName);Assert.IsTrue(first is {IsValid:true,IsError:false} && first.native.IsNull && first.MeshCount==49);Assert.AreEqual(15,first.Materials.Length);
   Assert.AreEqual(0,(await capture.Take("empty_before")).Coverage);
   using(capture.Scene.Push()){renderer=capture.Scene.CreateObject().Components.Create<ModelRenderer>(false);renderer.Model=first;renderer.Enabled=true;capture.Scene.EditorTick(0,0);}
   var initial=renderer.GltfInstance;Assert.IsNotNull(initial);Assert.IsTrue(renderer.SceneObject is SceneCustomObject && renderer.SceneObject.Model is null);var a=await capture.Take("source_reference_inspired");Assert.IsTrue(a.Coverage>1000);Assert.AreEqual(0,initial.CallbackErrors.Length);Assert.IsTrue(initial.DrawCalls>0);
   using(capture.Scene.Push())renderer.MaterialOverride=capture.White;var white=await capture.Take("white_override_negative");int changed=a.Pixels.Zip(white.Pixels,(x,y)=>Math.Abs(x.r-y.r)>.02f||Math.Abs(x.g-y.g)>.02f||Math.Abs(x.b-y.b)>.02f).Count(x=>x);Assert.IsTrue(changed>1000,"Source-material pixels not distinguishable from white override");
   using(capture.Scene.Push())renderer.MaterialOverride=null;var restored=await capture.Take("source_restored");Assert.IsTrue(restored.Coverage>1000);
   // Keep the first model alive so both resources can share the engine's native texture cache.
   second=Model.Load(compiledName);Assert.IsTrue(second is {IsValid:true,IsError:false} && second.native.IsNull && second.MeshCount==49);Assert.AreEqual(15,second.Materials.Length);
   var stable=await capture.Take("source_after_compiled_prepare");Assert.IsTrue(stable.Coverage>1000);
   using(capture.Scene.Push()){renderer.Model=second;capture.Scene.EditorTick(0,0);}var other=renderer.GltfInstance;Assert.IsNotNull(other);var b=await capture.Take("compiled_reference_inspired");Assert.IsTrue(b.Coverage>1000);Assert.AreEqual(0,other.CallbackErrors.Length);Assert.IsTrue(other.DrawCalls>0);
   int differences=stable.Pixels.Zip(b.Pixels,(x,y)=>Math.Abs(x.r-y.r)>.001f||Math.Abs(x.g-y.g)>.001f||Math.Abs(x.b-y.b)>.001f).Count(x=>x);Assert.AreEqual(0,differences,"Matched source and compiled images changed");
   Measurements.Add(new{source_sha256=Convert.ToHexStringLower(SHA256.HashData(source)),compiled_sha256=Convert.ToHexStringLower(SHA256.HashData(compiled)),source_path=stable.Path,source_sha256_capture=stable.Sha,compiled_path=b.Path,compiled_sha256_capture=b.Sha,a.Coverage,white_negative_changed_pixels=changed,matched_source_compiled_differing_pixels=differences,camera_position="26,-35,18",camera_target="0,0,4",field_of_view=48,width=640,height=480,light="directional (-.4,.5,-1), white x 3; point (10,-15,40), white x 8",scope="Actual public Model.Load and ModelRenderer, named source-lit materials; camera/light inferred from upstream references, no HDRI/ACES/+2EV and no user visual approval. Optional transmission/volume uses core fallback. Source/compiled agreement is not an independent appearance oracle."});
   using(capture.Scene.Push()){renderer.GameObject.Destroy();capture.Scene.EditorTick(0,0);capture.Scene.ProcessDeletes();}renderer=null;Assert.AreEqual(0,(await capture.Take("empty_after_delete")).Coverage);
  }
  finally{await MainThread.Wait();try{if(renderer!=null){using var scope=capture.Scene.Push();renderer.GameObject.Destroy();capture.Scene.EditorTick(0,0);capture.Scene.ProcessDeletes();}second?.Destroy();first?.Destroy();}finally{try{if(mount!=null)FileSystem.Mounted.UnMount(mount);}finally{mount?.Dispose();Directory.Delete(dir,true);}}}
 }
 sealed class Capture:IDisposable
 {
  public record Frame(Color[] Pixels,int Coverage,string Path,string Sha);public readonly Scene Scene;public readonly Material White;readonly CameraComponent camera;readonly Editor.SceneRenderingWidget widget;readonly SceneDirectionalLight direct;readonly ScenePointLight point;readonly string output;readonly Action onFrame;int frames;
  public Capture(string path){output=path;Scene=Scene.CreateEditorScene();White=Material.Load("materials/default/white.vmat").CreateCopy("beautiful_white_"+Guid.NewGuid().ToString("N"));using(Scene.Push()){camera=Scene.CreateObject().Components.Create<CameraComponent>();camera.WorldPosition=new global::Vector3(26,-35,18);camera.WorldRotation=Rotation.LookAt(new global::Vector3(0,0,4)-camera.WorldPosition,global::Vector3.Up);camera.Orthographic=false;camera.FieldOfView=48;camera.ZNear=1;camera.ZFar=1000;camera.CustomSize=new global::Vector2(640,480);camera.BackgroundColor=Color.Black;camera.ClearFlags=ClearFlags.All;camera.EnablePostProcessing=true;camera.DebugMode=SceneCameraDebugMode.Normal;}direct=new SceneDirectionalLight(Scene.SceneWorld,Rotation.LookAt(new global::Vector3(-.4f,.5f,-1)),Color.White*3){ShadowsEnabled=false};point=new ScenePointLight(Scene.SceneWorld,new global::Vector3(10,-15,40),180,Color.White*8);widget=new Editor.SceneRenderingWidget{Scene=Scene,Camera=camera,WindowTitle="A Beautiful Game reference-inspired presentation",Size=new global::Vector2(640,480)};onFrame=()=>frames++;widget.OnPreFrame+=onFrame;widget.Show();}
  async Task Wait(){await MainThread.Wait();var start=frames;var timer=Stopwatch.StartNew();while(frames<start+3||Graphics.IsActive){Assert.IsTrue(timer.Elapsed<TimeSpan.FromSeconds(15),"Preview stopped receiving frames");await Task.Delay(20);await MainThread.Wait();}}
  public async Task<Frame> Take(string name){await Wait();using(Scene.Push()){Scene.PreCameraRender();camera.InitializeRendering(false);}using var target=Texture.CreateRenderTarget().WithSize(640,480).WithFormat(ImageFormat.RGBA8888).WithInitialColor(Color.Magenta).Create("beautiful_view_"+Guid.NewGuid().ToString("N"));bool submitted;using(Scene.Push()){target.Clear(Color.Magenta);submitted=camera.RenderToTexture(target);}Assert.IsTrue(submitted);await Wait();using var bitmap=target.GetBitmap();var pixels=bitmap.GetPixels();Assert.AreEqual(640*480,pixels.Length);var bytes=bitmap.ToPng();var path=Path.Combine(output,name+".png");File.WriteAllBytes(path,bytes);return new(pixels,pixels.Count(p=>p.r>.03f||p.g>.03f||p.b>.03f),path,Convert.ToHexStringLower(SHA256.HashData(bytes)));}
  public void Dispose(){if(widget.IsValid){widget.OnPreFrame-=onFrame;widget.Close();widget.Destroy();}if(point.IsValid())point.Delete();if(direct.IsValid())direct.Delete();Scene.Destroy();White.Destroy();}
 }
}
