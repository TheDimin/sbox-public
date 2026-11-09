using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Sandbox.Gltf.DirectGpu.MeshoptCubeValidation;
using static MeshoptCubeReference;

[TestClass]
internal sealed class MeshoptCubePlaybackTests
{
 static string checkout;
 public static List<object> Measurements { get; } = new();
 public static void Configure(string root) { checkout=root; Measurements.Clear(); }
 [TestMethod]
 [DataRow("glTF-Meshopt/compiled_glb_c")]
 public async Task OrdinaryPublicAnimationClockAndSourceMaterialPreview(string formName)
 {
  await MainThread.Wait(); var reference=new MeshoptCubeReference(checkout); var form=reference.Forms.Single(f=>f.Name==formName);
  var canonical=Path.Combine(checkout,"Temp/gltf-validation/direct-gpu/MeshoptCubeTest/canonical/MeshoptCubeTest.glb_c");
  Require(File.Exists(canonical)&&Hash(File.ReadAllBytes(canonical))==Hash(form.Bytes),"Playback must use the exact validated canonical compiled bytes.");
  var output=Path.Combine(checkout,"Temp/gltf-validation/direct-gpu/MeshoptCubeTest/playback",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);
  var directory=Path.Combine(Path.GetTempPath(),"meshopt_clock_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
  LocalFileSystem mount=null; Model model=null;SkinnedModelRenderer renderer=null;Action tick=null;
  using var capture=new MeshoptCapture(output);
  var observations=new List<object>();double elapsed=0,previous=0,maxGap=0,maxError=0,inputBound=0,arithmeticBound=0; int ticks=0;string failure=null;
  try
  {
   foreach(var file in Directory.EnumerateFiles(Path.Combine(checkout,"Temp/gltf-reference/glTF-Sample-Assets/Models/MeshoptCubeTest",form.Variant)))
    if(Path.GetExtension(file)==".png" || (!form.Compiled && Path.GetExtension(file)==".bin"))File.Copy(file,Path.Combine(directory,Path.GetFileName(file)));
   var name="meshopt_clock_"+Guid.NewGuid().ToString("N")+(form.Compiled?".glb_c":".gltf");File.WriteAllBytes(Path.Combine(directory,name),form.Bytes);
   mount=new LocalFileSystem(directory);FileSystem.Mounted.Mount(mount);model=Model.Load(name);
   Require(model is {IsValid:true,IsError:false} && model.native.IsNull && model.GltfResource is not null,"Public animation requires the actual managed model.");
   Require(model.Materials.Length==11 && model.Materials.All(m=>m.IsValid && !m.IsError),"Source materials failed realization.");
   var empty=await capture.Take("empty_before");Require(empty.Coverage==0,"Preview starts with geometry.");
   using(capture.Scene.Push())
   {
    renderer=capture.Scene.CreateObject().Components.Create<SkinnedModelRenderer>(false);renderer.Model=model;renderer.UseAnimGraph=false;renderer.PlayAnimationsInEditorScene=true;renderer.PlaybackRate=0;renderer.Enabled=true;capture.Scene.EditorTick(0,0);
   }
   Require(renderer.SceneModel is null && renderer.SceneObject is SceneCustomObject && renderer.SceneObject.Model is null && renderer.GltfInstance is not null,"Animation must own the direct GPU instance without a native model.");
   var sequenceName=model.SequenceNames.Single();renderer.Sequence.Name=sequenceName;renderer.Sequence.Looping=true;renderer.Sequence.Time=0;renderer.PlaybackRate=1;
   Require(renderer.Sequence.Duration==2,"Pinned meshopt rotation clip must last two seconds.");
   var watch=Stopwatch.StartNew();
   tick=()=>
   {
    if(failure!=null)return;
    try
    {
     elapsed=watch.Elapsed.TotalSeconds;double delta=elapsed-previous;previous=elapsed;float entry=(float)delta;
     using(capture.Scene.Push())capture.Scene.EditorTick((float)elapsed,entry);
     double expected=elapsed%2,actual=renderer.Sequence.Time,diff=Math.Abs(actual-expected),error=Math.Min(diff,Math.Abs(2-diff));
     inputBound+=Math.Abs(delta-entry);double ulp=Math.Max(Math.Abs((double)MathF.BitIncrement((float)actual)-(float)actual),Math.Abs((double)MathF.BitDecrement((float)actual)-(float)actual));
     arithmeticBound+=16*Math.Max(Math.BitIncrement(2d)-2,Math.Max(Math.BitIncrement(elapsed)-elapsed,Math.BitIncrement(delta)-delta));
     Require(error<=inputBound+ulp+arithmeticBound,"Public animation discarded monotonic time beyond explicit float entry/output rounding.");
     Require(capture.Scene.TimeDelta==entry && renderer.PlaybackRate==1 && renderer.Sequence.Name==sequenceName,"Ordinary animation configuration/delta changed.");
     maxGap=Math.Max(maxGap,delta);maxError=Math.Max(maxError,error);ticks++;
     observations.Add(new{elapsed_seconds=elapsed,full_delta_seconds=delta,float_entry_delta=entry,expected_source_time=expected,actual_source_time=actual,phase_error_seconds=error,input_rounding_bound_seconds=inputBound,float_output_ulp_seconds=ulp,double_arithmetic_bound_seconds=arithmeticBound,renderer.GltfInstance.PoseRevision,engine_frame=Application.FrameCount});
    }
    catch(Exception e){failure=e.ToString();renderer.PlaybackRate=0;}
   };
   capture.Widget.OnPreFrame+=tick;
   while(elapsed<4.2 || ticks<12)
   {
    await capture.WaitFrames();Require(failure==null,failure);Require(watch.Elapsed.TotalSeconds<20,"Actual widget did not complete two ordinary playback loops.");
   }
   capture.Widget.OnPreFrame-=tick;tick=null;renderer.PlaybackRate=0;
   await capture.WaitFrames();var instance=renderer.GltfInstance;float sourceTime=renderer.Sequence.Time;
   Require(instance.DrawCalls>0 && instance.PoseRevision==instance.SubmittedRevision && instance.CallbackErrors.Length==0,"Normal playback did not publish and submit its final pose.");
   var numeric=MeshoptCubeGpuChecks.AssertReadback(reference,instance.Readback(),sourceTime);
   var actual=await capture.Take("actual_public_source_material_after_two_wallclock_loops");Require(actual.Coverage>1000,"Public source-material preview is empty.");
   // Public phase control provides a visibly different intermediate rotation;
   // this is a deterministic image sample, distinct from the preceding clock.
   renderer.Sequence.Time=.375f;using(capture.Scene.Push())capture.Scene.EditorTick(0,0);
   var midpoint=await capture.Take("actual_public_source_material_public_midpoint_seek");
   var midpointNumeric=MeshoptCubeGpuChecks.AssertReadback(reference,instance.Readback(),.375f);
   Require(midpoint.Coverage>1000,"Public midpoint source preview is empty.");
   int changed=actual.Pixels.Zip(midpoint.Pixels,(a,b)=>Math.Abs(a.r-b.r)>.01f || Math.Abs(a.g-b.g)>.01f || Math.Abs(a.b-b.b)>.01f).Count(x=>x);
   Require(changed>16,"Public rotation midpoint did not visibly change the five animated cubes.");
   Measurements.Add(new{model="MeshoptCubeTest",form=formName,input_sha256=Hash(form.Bytes),output,public_resource=model.ResourcePath,sequence=sequenceName,duration=renderer.Sequence.Duration,playback_rate=1,completed_loops=elapsed/2,ticks,elapsed_seconds=elapsed,maximum_full_callback_gap_seconds=maxGap,maximum_phase_error_seconds=maxError,input_rounding_bound_seconds=inputBound,double_arithmetic_bound_seconds=arithmeticBound,observations,numeric,midpoint_numeric=midpointNumeric,source_time_final_clock=sourceTime,preview=actual.Path,preview_sha256=actual.Sha256,midpoint_preview=midpoint.Path,midpoint_preview_sha256=midpoint.Sha256,changed_pixels=changed,camera=actual.CameraState,
    scope="Public Model.Load and SkinnedModelRenderer, full monotonic widget gaps passed into normal Scene.EditorTick, initial clock seek only, actual final GPU pose/indices independently checked. Separate public midpoint seek proves source-material motion pixels. Camera/light inferred, final user visual approval pending; not visual FPS, Windows or performance."});
   var retained=instance;Destroy();int calls=retained.DrawCalls;var deleted=await capture.Take("empty_after_public_renderer_delete");Require(deleted.Coverage==0 && retained.DrawCalls==calls,"Deleted public animation continued drawing.");
  }
  finally
  {
   await MainThread.Wait();if(tick!=null)capture.Widget.OnPreFrame-=tick;
   try{Destroy();model?.Destroy();}finally{try{if(mount!=null)FileSystem.Mounted.UnMount(mount);}finally{mount?.Dispose();Directory.Delete(directory,true);}}
   Measurements.Add(new{form=formName,captures=capture.Captures,cleanup_returned=true});
  }
  void Destroy(){if(renderer==null)return;using var scope=capture.Scene.Push();renderer.GameObject.Destroy();capture.Scene.EditorTick(0,0);capture.Scene.ProcessDeletes();renderer=null;}
 }

 // Model-owned large preview using the established native RenderToTexture route.
 // It never changes global rendering mode and constructs no native Model/Mesh.
 sealed class MeshoptCapture:IDisposable
 {
  const int Size=1024;
  public sealed record Frame(Color[] Pixels,int Coverage,string Path,string Sha256,string CameraState);
  public readonly Scene Scene;public readonly CameraComponent Camera;public readonly Editor.SceneRenderingWidget Widget;
  readonly SceneDirectionalLight light;readonly string output;readonly Action count;int frames;
  public readonly List<object> Captures=new();
  public MeshoptCapture(string output)
  {
   this.output=output;RequireNative();Scene=Scene.CreateEditorScene();
   using(Scene.Push())
   {
    Camera=Scene.CreateObject().Components.Create<CameraComponent>();Camera.WorldPosition=new global::Vector3(1600,1000,900);Camera.WorldRotation=Rotation.LookAt(new global::Vector3(0,250,370)-Camera.WorldPosition,global::Vector3.Up);
    Camera.Orthographic=true;Camera.OrthographicHeight=1100;Camera.ZNear=1;Camera.ZFar=4000;Camera.CustomSize=new global::Vector2(Size,Size);Camera.BackgroundColor=Color.Black;Camera.ClearFlags=ClearFlags.All;Camera.EnablePostProcessing=true;Camera.DebugMode=SceneCameraDebugMode.Normal;
   }
   light=new SceneDirectionalLight(Scene.SceneWorld,Camera.WorldRotation,Color.White*2);
   Widget=new Editor.SceneRenderingWidget{Scene=Scene,Camera=Camera,WindowTitle="MeshoptCubeTest source material validation",Size=new global::Vector2(512,512)};
   count=()=>frames++;Widget.OnPreFrame+=count;Widget.Show();
  }
  static void RequireNative(){var t=typeof(Scene).Assembly.GetType("Sandbox.Rendering.ManagedSceneRendering",true);var p=t.GetProperty("Enabled",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic);Require(!(bool)p.GetValue(null),"Keep the existing native presentation renderer.");}
  public async Task WaitFrames(){await MainThread.Wait();Require(Application.IsEditor&&!Game.IsPlaying,"Editor left idle mode.");RequireNative();int start=frames;var watch=Stopwatch.StartNew();while(frames<start+3 || Graphics.IsActive){Require(watch.Elapsed.TotalSeconds<10,"Preview stopped receiving actual frames.");await Task.Delay(20);await MainThread.Wait();}}
  public async Task<Frame> Take(string label)
  {
   await WaitFrames();using(Scene.Push()){Scene.PreCameraRender();Camera.InitializeRendering(false);}
   using var target=Texture.CreateRenderTarget().WithSize(Size,Size).WithFormat(ImageFormat.RGBA8888).WithInitialColor(Color.Magenta).Create("meshopt_preview_"+Guid.NewGuid().ToString("N"));
   bool submitted;using(Scene.Push()){target.Clear(Color.Magenta);submitted=Camera.RenderToTexture(target);}Require(submitted,"Native preview camera rejected render.");
   await WaitFrames();using var bitmap=target.GetBitmap();Require(bitmap!=null,"Native preview has no bitmap.");var pixels=bitmap.GetPixels();Require(pixels.Length==Size*Size,"Unexpected native preview dimensions.");
   Require(!pixels.Any(p=>p.r>.8f&&p.g<.2f&&p.b>.8f),"Magenta render target sentinel survived.");
   var png=bitmap.ToPng();var path=Path.Combine(output,label+".png");File.WriteAllBytes(path,png);
   var camera=JsonSerializer.Serialize(new{position=Camera.WorldPosition.ToString(),rotation=Camera.WorldRotation.ToString(),Camera.Orthographic,Camera.OrthographicHeight,Camera.ZNear,Camera.ZFar,size=Size,Camera.EnablePostProcessing,debug_mode=Camera.DebugMode.ToString(),directional_light="camera-forward White*2",recipe="inferred oblique grid view; no authored upstream camera/lighting"});
   var f=new Frame(pixels,pixels.Count(p=>p.r>.03f||p.g>.03f||p.b>.03f),path,Hash(png),camera);Captures.Add(new{label,path,sha256=f.Sha256,f.Coverage,width=Size,height=Size,camera,engine_frame=Application.FrameCount});return f;
  }
  public void Dispose(){if(Widget.IsValid){Widget.OnPreFrame-=count;Widget.Close();Widget.Destroy();}if(light.IsValid())light.Delete();Scene.Destroy();}
 }
}
