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
public sealed class AlphaNativeCallbackContractTests
{
 public static string CheckoutRoot {get;set;}
 public static string OutputDirectory {get;set;}
 public static string ShaderSha256 {get;set;}
 public static List<object> Measurements {get;}=new();
 [TestMethod]
 public async Task ObserveCallbacksOnSeparateNativeSortUnits()
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
   var events=new System.Collections.Concurrent.ConcurrentQueue<CallbackEvent>();var errors=new System.Collections.Concurrent.ConcurrentQueue<string>();long sequence=0;var objects=new List<SceneCustomObject>();var counters=new List<CallbackCounter>();
   root.SetFeature("F_TRANSLUCENT",1);Assert.IsTrue(root.Flags.IsTranslucent);
   var vertices=new[]{new Vertex(new global::Vector3(0,-20,-20),global::Vector3.Forward,global::Vector3.Left,new global::Vector4(0,0,0,0)),new Vertex(new global::Vector3(0,20,-20),global::Vector3.Forward,global::Vector3.Left,new global::Vector4(1,0,0,0)),new Vertex(new global::Vector3(0,20,20),global::Vector3.Forward,global::Vector3.Left,new global::Vector4(1,1,0,0)),new Vertex(new global::Vector3(0,-20,-20),global::Vector3.Forward,global::Vector3.Left,new global::Vector4(0,0,0,0)),new Vertex(new global::Vector3(0,20,20),global::Vector3.Forward,global::Vector3.Left,new global::Vector4(1,1,0,0)),new Vertex(new global::Vector3(0,-20,20),global::Vector3.Forward,global::Vector3.Left,new global::Vector4(0,1,0,0))};
   void Record(int id,string kind){var view=Graphics.NativeViewIdentity;events.Enqueue(new(Interlocked.Increment(ref sequence),Application.FrameCount,view?.CameraId,view?.ViewId,Graphics.LayerType.ToString(),id,kind));}
   SceneCustomObject Add(int id,float x,bool callbacks){var obj=new SceneCustomObject(scene.SceneWorld);objects.Add(obj);obj.Flags.IsOpaque=false;obj.Flags.IsTranslucent=true;obj.Flags.CastShadows=false;obj.Flags.WantsExecuteBefore=callbacks;obj.Flags.WantsExecuteAfter=callbacks;Assert.AreEqual(callbacks,obj.Flags.WantsExecuteBefore);Assert.AreEqual(callbacks,obj.Flags.WantsExecuteAfter);obj.Bounds=new BBox(new global::Vector3(x-.01f,-20,-20),new global::Vector3(x+.01f,20,20));var counter=new CallbackCounter(()=>Record(id,"before"),()=>Record(id,"after"));counters.Add(counter);obj.CallbackTarget=counter;obj.RenderOverride=_=>{Record(id,"draw");try{Graphics.Draw(vertices.AsSpan(),vertices.Length,root);}catch(Exception e){errors.Enqueue(e.ToString());}};return obj;}
   async Task<object> Observe(string label,bool childrenCallbacks)
   {
    events.Clear();Add(0,0,true);Add(1,10,childrenCallbacks);Add(2,-10,childrenCallbacks);var image=await Capture(label,Color.Black);Assert.IsTrue(image.Take(3).Sum()>.1f,"Callback control requires positive geometry");var rows=events.ToArray();Assert.IsTrue(errors.IsEmpty);var groups=rows.Where(e=>e.Layer=="Translucent"&&e.Camera.HasValue&&e.View.HasValue).GroupBy(e=>(e.Frame,e.Camera,e.View)).Where(g=>g.Any(e=>e.Kind=="draw"&&e.Id==0)&&g.Any(e=>e.Kind=="draw"&&e.Id==1)&&g.Any(e=>e.Kind=="draw"&&e.Id==2)).ToArray();Assert.IsTrue(groups.Length>0,"No common native view contained all three callbacks");var measured=groups.Select(g=>new{g.Key,events=g.OrderBy(e=>e.Sequence).ToArray(),before=g.Count(e=>e.Kind=="before"),after=g.Count(e=>e.Kind=="after")}).ToArray();var expected=childrenCallbacks?3:1;Measurements.Add(new{phase="native_callback_events_before_assert",label,childrenCallbacks,expected,groups=measured,allRows=rows});foreach(var obj in objects){obj.RenderOverride=null;obj.Delete();}objects.Clear();using(scene.Push())scene.ProcessDeletes();await Wait();using(scene.Push())scene.ProcessDeletes();var empty=await Capture(label+"_deleted",Color.Black);Assert.IsTrue(empty.Take(3).All(x=>Math.Abs(x)<.02),"Callback control deletion was not empty");return new{label,childrenCallbacks,groups=measured};
   }
   try{var parentOnly=await Observe("callbacks_primary_only",false);var copied=await Observe("callbacks_copied",true);Measurements.Add(new{phase="native_object_callback_contract",parentOnly,copied,scope="Native calls once per flagged object, and event order records whether parent-only callbacks bracket children; not a production design acceptance."});}
   finally{foreach(var obj in objects){obj.RenderOverride=null;if(obj.IsValid())obj.Delete();}using(scene.Push())scene.ProcessDeletes();}
  }
  finally{await MainThread.Wait();if(widget is not null&&widget.IsValid){widget.OnPreFrame-=pre;widget.Close();widget.Destroy();}if(light.IsValid())light.Delete();scene?.Destroy();root?.Destroy();shader?.Destroy();}
 }
 private sealed record CallbackEvent(long Sequence,ulong Frame,int? Camera,int? View,string Layer,int Id,string Kind);
 private sealed class CallbackCounter : SceneObject.SceneObjectCallbacks
 {
  private readonly Action before,after;public CallbackCounter(Action before,Action after){this.before=before;this.after=after;}
  public bool IsValid=>true;public void OnBeforeObjectRender()=>before();public void OnAfterObjectRender()=>after();
 }

}
