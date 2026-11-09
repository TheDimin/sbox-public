using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using V3 = System.Numerics.Vector3;

namespace Sandbox.Gltf.DirectGpu.BrainStemValidation;

[TestClass]
public sealed class BrainStemCanonicalAnimationTests
{
 public static string CheckoutRoot { get; set; }
 public static List<object> Measurements { get; } = new();
 static void Check(bool ok,string message) { if(!ok) throw new InvalidOperationException(message); }
 static string Hash(byte[] bytes)=>Convert.ToHexStringLower(SHA256.HashData(bytes));
 [TestMethod] public Task PublicCanonicalCompiledDense() => Run(true);

 async Task Run(bool compiled)
 {
  await MainThread.Wait(); var corpus=Path.Combine(CheckoutRoot,"Temp/gltf-reference/glTF-Sample-Assets"); var source=Path.Combine(corpus,"Models/BrainStem/glTF-Meshopt-EXT"); var bytes=File.ReadAllBytes(Path.Combine(source,"BrainStem.gltf"));
  using var oracle=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(CheckoutRoot,"Temp/gltf-validation/direct-gpu/BrainStem/reference.json"))); var root=oracle.RootElement; var input=root.GetProperty("assets").GetProperty("glTF-Meshopt-EXT");
  Check(Hash(bytes)==input.GetProperty("sha256").GetString(),"Pinned source changed"); foreach(var dep in input.GetProperty("dependencies").EnumerateObject()) Check(Hash(File.ReadAllBytes(Path.Combine(corpus,dep.Name)))==dep.Value.GetString(),"Pinned dependency changed");
  byte[] Resolve(string uri)=>File.ReadAllBytes(Path.Combine(source,Uri.UnescapeDataString(uri)));
  var output=Path.Combine(CheckoutRoot,"Temp/gltf-validation/direct-gpu/BrainStem/public-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(output);
  using var capture=new GltfGpuTriangleGateTests.NativeCapture(output,new Vector3(180f,0f,42f),new Vector3(-1.68637f,-1.76414f,36.0988f),115f);
  var light=new SceneDirectionalLight(capture.Scene.SceneWorld,Rotation.LookAt(new Vector3(-.8248628f,-.5155393f,-.2319927f)),Color.White*2);
  var point=(ScenePointLight)typeof(GltfGpuTriangleGateTests.NativeCapture).GetField("light",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(capture); point.LightColor=Color.White; light.LightColor=Color.White;
  var directory=Path.Combine(Path.GetTempPath(),"brainstem_"+Guid.NewGuid().ToString("N")); var path="brainstem_"+Guid.NewGuid().ToString("N")+(compiled?".glb_c":".gltf"); BaseFileSystem aggregate=null; LocalFileSystem mount=null; Model model=null; SkinnedModelRenderer renderer=null; var phases=new List<object>(); var captures=new List<object>(); string error=null; string compiledHash=null;
  try {
   Directory.CreateDirectory(directory); var package=GltfCompiler.Compile(bytes,Resolve,new HashSet<string> { "KHR_mesh_quantization", "EXT_meshopt_compression" }); compiledHash=Hash(package); File.WriteAllBytes(Path.Combine(directory,path),package); aggregate=Sandbox.FileSystem.Mounted; mount=new LocalFileSystem(directory); aggregate.Mount(mount);
   var empty=await capture.Take("empty_before"); Check(empty.Coverage==0,"empty before not empty"); capture.Camera.BackgroundColor=new Color(.35f,.35f,.35f,1); var background=await capture.Take("background_before");
   model=Model.Load(path); Check(model is {IsValid:true,IsError:false} && model.native.IsNull && model.GltfResource is not null,"public load not managed");
   using(capture.Scene.Push()) { renderer=capture.Scene.CreateObject().Components.Create<SkinnedModelRenderer>(false); renderer.UseAnimGraph=false; renderer.PlayAnimationsInEditorScene=true; renderer.PlaybackRate=0; renderer.Model=model; renderer.Sequence.Name="animation_0"; renderer.Enabled=true; }
   var instance=renderer.GltfInstance; Check(instance is not null && renderer.SceneModel is null && renderer.SceneObject is SceneCustomObject && renderer.SceneObject.Model is null,"not actual native-free skinned component");
   renderer.Sequence.Name="animation_0"; renderer.Sequence.Looping=false; renderer.PlaybackRate=0;
   for(int i=0;i<=60;i++) { float time=i*.1f; renderer.Sequence.Time=time; using(capture.Scene.Push()) capture.Scene.EditorTick(0,0); var frame=await capture.Take("sample_"+i.ToString("D4")); await MainThread.Wait(); var changed=frame.Pixels.Zip(background.Pixels,(a,b)=>Math.Abs(a.r-b.r)+Math.Abs(a.g-b.g)+Math.Abs(a.b-b.b)>.02f).Count(x=>x); captures.Add(new {frame.Path,frame.Sha256,frame.Coverage,frame.EngineFrame,frame.ObservedWallclockSeconds,frame.CameraStateJson,sequenceTime=renderer.Sequence.Time,changedPixels=changed}); Check(changed>64 && renderer.Sequence.Time==time && instance.SubmittedRevision==instance.PoseRevision && instance.CallbackErrors.Length==0,"sample was not submitted at its requested time"); }
   phases.Add(new { sampledStart=0, sampledEnd=6, step=.1, frames=captures.Count, playback="deterministic seeks; not wall-clock playback" });
   using(capture.Scene.Push()) { renderer.GameObject.Destroy(); capture.Scene.EditorTick(0,0); capture.Scene.ProcessDeletes(); } renderer=null; int calls=instance.DrawCalls; capture.Camera.BackgroundColor=Color.Black; var deleted=await capture.Take("empty_after"); Check(deleted.Coverage==0&&instance.DrawCalls==calls,"renderer deletion failed"); Check(model.IsValid,"resource lost after component deletion");
  } catch(Exception e) { error=e.ToString(); }
  finally { await MainThread.Wait(); void Clean(Action action,string label) { try { action(); } catch(Exception e) { error+="\ncleanup "+label+": "+e; } } Clean(()=> { if(renderer is not null) { using var scope=capture.Scene.Push(); renderer.GameObject.Destroy(); capture.Scene.EditorTick(0,0); capture.Scene.ProcessDeletes(); } },"renderer"); Clean(()=>model?.Destroy(),"model"); Clean(()=> { if(mount is not null) aggregate.UnMount(mount); },"unmount"); Clean(()=>mount?.Dispose(),"mount"); Clean(()=> { if(Directory.Exists(directory)) Directory.Delete(directory,true); },"directory"); Clean(()=> { if(light.IsValid()) light.Delete(); },"light"); }
  Measurements.Add(new {form="glTF-Meshopt-EXT/canonical-compiled",compiledHash,output,phases,captures,error,scope="Public Model.Load and SkinnedModelRenderer, 61 actual sequential deterministic seeks at 0.1-second source-time steps; this is not wall-clock playback or a matched appearance approval."}); Assert.IsNull(error,error);
 }
}
