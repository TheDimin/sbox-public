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
internal sealed class BasisSourcePresentationTests
{
 static string checkout;public static List<object> Measurements {get;}=new();public static void Configure(string root){checkout=root;Measurements.Clear();}
 [TestMethod]
 public async Task RequiredBasisDracoThroughPublicModelRenderer()
 {
  await MainThread.Wait();string input=Path.Combine(checkout,"Temp/gltf-reference/glTF-Sample-Assets/Models/ABeautifulGame/glTF-Binary-KTX-ETC1S-Draco/ABeautifulGame.glb");byte[] source=File.ReadAllBytes(input);string sha=Convert.ToHexStringLower(SHA256.HashData(source));Assert.AreEqual("d950162d41e0064be9dfac0c09d80c7a23719a37af37f08cd79385b8af0af0c1",sha);
  string output=Path.Combine(checkout,"Temp/gltf-validation/direct-gpu/ABeautifulGame/basis-source-presentation",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);string dir=Path.Combine(Path.GetTempPath(),"gltf_beautiful_basis_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
  LocalFileSystem mount=null;Model model=null;ModelRenderer renderer=null;using var capture=new Capture(output);
  try
  {
   string name="beautiful_basis_"+Guid.NewGuid().ToString("N")+".glb";File.WriteAllBytes(Path.Combine(dir,name),source);mount=new LocalFileSystem(dir);FileSystem.Mounted.Mount(mount);model=Model.Load(name);Assert.IsTrue(model is {IsValid:true,IsError:false} && model.native.IsNull && model.MeshCount==49,"Public BasisU+Draco model must be native-free and preserve 49 instances");
   var materials=model.Materials;Assert.AreEqual(15,materials.Length);Assert.IsTrue(materials.All(m=>m is {IsValid:true,IsError:false}));capture.SetAmbient(.25f);capture.SetCamera(new global::Vector3(-17,22,11),new global::Vector3(0,0,3));Assert.AreEqual(0,(await capture.Take("empty_before")).Coverage);
   using(capture.Scene.Push()){renderer=capture.Scene.CreateObject().Components.Create<ModelRenderer>(false);renderer.Model=model;renderer.Enabled=true;capture.Scene.EditorTick(0,0);}var instance=renderer.GltfInstance;Assert.IsNotNull(instance);Assert.IsTrue(renderer.SceneObject is SceneCustomObject && renderer.SceneObject.Model is null);await capture.Settle();var actual=await capture.Take("source_basis_draco_public");Assert.IsTrue(actual.Coverage>1000);Assert.AreEqual(0,instance.CallbackErrors.Length);Assert.IsTrue(instance.DrawCalls>0 && instance.SubmittedRevision==instance.PoseRevision);
   var rows=instance.Readback();Assert.AreEqual(49,rows.Count);Assert.IsTrue(rows.All(r=>r.IndexDataOrigin=="gpu-readback" && r.Indices.Length>0 && r.Fields.Any(f=>f.Semantic=="POSITION") && r.Fields.Any(f=>f.Semantic=="NORMAL") && r.Fields.Any(f=>f.Semantic=="TEXCOORD")),"Expected GPU positions, normals, UV and actual index readbacks");
   using(capture.Scene.Push())renderer.MaterialOverride=capture.White;var white=await capture.Take("white_override_negative");int changed=actual.Pixels.Zip(white.Pixels,(x,y)=>Math.Abs(x.r-y.r)>.02f||Math.Abs(x.g-y.g)>.02f||Math.Abs(x.b-y.b)>.02f).Count(x=>x);Assert.IsTrue(changed>1000);using(capture.Scene.Push())renderer.MaterialOverride=null;var restored=await capture.Take("restored_source_material");Assert.IsTrue(restored.Coverage>1000);
   var retained=instance;using(capture.Scene.Push()){renderer.GameObject.Destroy();capture.Scene.EditorTick(0,0);capture.Scene.ProcessDeletes();}renderer=null;int calls=retained.DrawCalls;Assert.AreEqual(0,(await capture.Take("empty_after_delete")).Coverage);Assert.AreEqual(calls,retained.DrawCalls);
   Measurements.Add(new{form="glTF-Binary-KTX-ETC1S-Draco/source",input_sha256=sha,path=actual.Path,sha256=actual.Sha,actual.Coverage,width=640,height=480,draw_rows=rows.Count,white_negative_changed_pixels=changed,shaders=materials.Select(m=>m.ShaderName).ToArray(),camera_position="-17,22,11",camera_target="0,0,3",field_of_view=48,light="directional (-.4,.5,-1), white x1; point (10,-15,40), white x2; constant ambient .25",scope="Actual public Model.Load and ordinary ModelRenderer using required BasisU+Draco source with actual index/GPU rows, source materials and negative controls. Camera and lighting inferred; no original HDRI, ACES or +2EV; optional transmission/volume core fallback, no independent optical pixel oracle or user approval."});
  }
  finally{await MainThread.Wait();try{if(renderer!=null){using var scope=capture.Scene.Push();renderer.GameObject.Destroy();capture.Scene.EditorTick(0,0);capture.Scene.ProcessDeletes();}model?.Destroy();}finally{try{if(mount!=null)FileSystem.Mounted.UnMount(mount);}finally{mount?.Dispose();Directory.Delete(dir,true);}}}
 }
 sealed class Capture:IDisposable
 {
  public record Frame(Color[] Pixels,int Coverage,string Path,string Sha);public readonly Scene Scene;public readonly Material White;readonly CameraComponent camera;readonly AmbientLight ambient;readonly Editor.SceneRenderingWidget widget;readonly SceneDirectionalLight direct;readonly ScenePointLight point;readonly string output;readonly Action onFrame;int frames;
  public Capture(string path){output=path;Scene=Scene.CreateEditorScene();White=Material.Load("materials/default/white.vmat").CreateCopy("beautiful_white_"+Guid.NewGuid().ToString("N"));using(Scene.Push()){camera=Scene.CreateObject().Components.Create<CameraComponent>();camera.WorldPosition=new global::Vector3(-26,35,15);camera.WorldRotation=Rotation.LookAt(new global::Vector3(0,0,3)-camera.WorldPosition,global::Vector3.Up);camera.Orthographic=false;camera.FieldOfView=48;camera.ZNear=1;camera.ZFar=1000;camera.CustomSize=new global::Vector2(640,480);camera.BackgroundColor=Color.Black;camera.ClearFlags=ClearFlags.All;camera.EnablePostProcessing=true;camera.DebugMode=SceneCameraDebugMode.Normal;ambient=Scene.CreateObject().Components.Create<AmbientLight>();ambient.Color=Color.Transparent;}direct=new SceneDirectionalLight(Scene.SceneWorld,Rotation.LookAt(new global::Vector3(-.4f,.5f,-1)),Color.White*1){ShadowsEnabled=false};point=new ScenePointLight(Scene.SceneWorld,new global::Vector3(10,-15,40),180,Color.White*2);widget=new Editor.SceneRenderingWidget{Scene=Scene,Camera=camera,WindowTitle="A Beautiful Game reference-inspired presentation",Size=new global::Vector2(640,480)};onFrame=()=>frames++;widget.OnPreFrame+=onFrame;widget.Show();}
  async Task Wait(){await MainThread.Wait();var start=frames;var timer=Stopwatch.StartNew();while(frames<start+3||Graphics.IsActive){Assert.IsTrue(timer.Elapsed<TimeSpan.FromSeconds(15),"Preview stopped receiving frames");await Task.Delay(20);await MainThread.Wait();}}
  public void SetCamera(global::Vector3 position,global::Vector3 target){using(Scene.Push()){camera.WorldPosition=position;camera.WorldRotation=Rotation.LookAt(target-position,global::Vector3.Up);Scene.EditorTick(0,0);}}
  public void SetAmbient(float value){using(Scene.Push()){ambient.Color=new Color(value,value,value,1);Scene.EditorTick(0,0);}}
  public async Task Settle(){for(int i=0;i<12;i++)await Wait();}
  public async Task<Frame> Take(string name){await Wait();using(Scene.Push()){Scene.PreCameraRender();camera.InitializeRendering(false);}using var target=Texture.CreateRenderTarget().WithSize(640,480).WithFormat(ImageFormat.RGBA8888).WithInitialColor(Color.Magenta).Create("beautiful_view_"+Guid.NewGuid().ToString("N"));bool submitted;using(Scene.Push()){target.Clear(Color.Magenta);submitted=camera.RenderToTexture(target);}Assert.IsTrue(submitted);await Wait();using var bitmap=target.GetBitmap();var pixels=bitmap.GetPixels();Assert.AreEqual(640*480,pixels.Length);var bytes=bitmap.ToPng();var path=Path.Combine(output,name+".png");File.WriteAllBytes(path,bytes);return new(pixels,pixels.Count(p=>p.r>.03f||p.g>.03f||p.b>.03f),path,Convert.ToHexStringLower(SHA256.HashData(bytes)));}
  public void Dispose(){if(widget.IsValid){widget.OnPreFrame-=onFrame;widget.Close();widget.Destroy();}if(point.IsValid())point.Delete();if(direct.IsValid())direct.Delete();Scene.Destroy();White.Destroy();}
 }
}
