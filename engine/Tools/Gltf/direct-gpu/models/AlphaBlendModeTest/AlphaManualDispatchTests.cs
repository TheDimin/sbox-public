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
public sealed class AlphaManualDispatchTests
{
 public static string CheckoutRoot {get;set;}
 public static string OutputDirectory {get;set;}
 public static string ShaderSha256 {get;set;}
 public static List<object> Measurements {get;}=new();
 [TestMethod]
 public async Task ObserveNativeManualRenderCallbackThreadAndLifetime()
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
   var rows=new System.Collections.Concurrent.ConcurrentQueue<object>();var errors=new System.Collections.Concurrent.ConcurrentQueue<string>();var target=new SceneCustomObject(scene.SceneWorld);target.Tags.Add("alpha_manual_excluded");camera.RenderExcludeTags.Add("alpha_manual_excluded");var driver=new SceneCustomObject(scene.SceneWorld);var inCall=0;var callerThread=0;var calls=0;var nested=0;var sameThread=0;var driverGate=new object();
   var vertices=new[]{new Vertex(new global::Vector3(0,-20,-20),global::Vector3.Forward,global::Vector3.Left,new global::Vector4(0,0,0,0)),new Vertex(new global::Vector3(0,20,-20),global::Vector3.Forward,global::Vector3.Left,new global::Vector4(1,0,0,0)),new Vertex(new global::Vector3(0,20,20),global::Vector3.Forward,global::Vector3.Left,new global::Vector4(1,1,0,0)),new Vertex(new global::Vector3(0,-20,-20),global::Vector3.Forward,global::Vector3.Left,new global::Vector4(0,0,0,0)),new Vertex(new global::Vector3(0,20,20),global::Vector3.Forward,global::Vector3.Left,new global::Vector4(1,1,0,0)),new Vertex(new global::Vector3(0,-20,20),global::Vector3.Forward,global::Vector3.Left,new global::Vector4(0,1,0,0))};
   target.RenderingEnabled=true;target.Flags.IsOpaque=true;target.Bounds=new BBox(new global::Vector3(-.01f,-20,-20),new global::Vector3(.01f,20,20));target.RenderOverride=_=>{var tid=Environment.CurrentManagedThreadId;Interlocked.Increment(ref calls);if(Volatile.Read(ref inCall)!=0)Interlocked.Increment(ref nested);if(Volatile.Read(ref inCall)!=0&&tid==Volatile.Read(ref callerThread))Interlocked.Increment(ref sameThread);rows.Enqueue(new{phase="target",tid,inCall=Volatile.Read(ref inCall),frame=Application.FrameCount});try{Graphics.Draw(vertices.AsSpan(),vertices.Length,root);}catch(Exception e){errors.Enqueue(e.ToString());}};
   driver.Flags.IsOpaque=true;driver.Flags.IsTranslucent=false;driver.Bounds=target.Bounds;driver.RenderOverride=_=>{lock(driverGate){try{Volatile.Write(ref callerThread,Environment.CurrentManagedThreadId);Volatile.Write(ref inCall,1);Graphics.Render(target);Volatile.Write(ref inCall,0);rows.Enqueue(new{phase="return",tid=Environment.CurrentManagedThreadId,frame=Application.FrameCount});}catch(Exception e){Volatile.Write(ref inCall,0);errors.Enqueue(e.ToString());}}};
   try{var actual=await Capture("manual_dispatch",Color.Black);Measurements.Add(new{phase="manual_dispatch_observation",calls,nested,sameThread,rows=rows.ToArray(),errors=errors.ToArray(),actual});Assert.IsTrue(errors.IsEmpty,"Manual callbacks reported errors");Assert.IsTrue(calls>0&&actual.Take(3).Sum()>.1f,"Direct manual call failed to submit positive target");Assert.IsTrue(nested>0,"No target callback was observed inside Graphics.Render; target is excluded from the camera collection");Assert.AreEqual(nested,sameThread,"A nested callback ran on a different thread; a held lock can deadlock");}
   finally{target.RenderOverride=null;driver.RenderOverride=null;if(target.IsValid())target.Delete();if(driver.IsValid())driver.Delete();using(scene.Push())scene.ProcessDeletes();}
  }
  finally{await MainThread.Wait();if(widget is not null&&widget.IsValid){widget.OnPreFrame-=pre;widget.Close();widget.Destroy();}if(light.IsValid())light.Delete();scene?.Destroy();root?.Destroy();shader?.Destroy();}
 }
}
