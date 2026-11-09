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
public sealed class BrainStemPublicTests
{
 public static string CheckoutRoot { get; set; }
 public static List<object> Measurements { get; } = new();
 static void Check(bool ok,string message) { if(!ok) throw new InvalidOperationException(message); }
 static string Hash(byte[] bytes)=>Convert.ToHexStringLower(SHA256.HashData(bytes));
 [DataTestMethod]
 [DataRow("glTF",true)] [DataRow("glTF-Embedded",false)] [DataRow("glTF-Embedded",true)] [DataRow("glTF-Binary",false)] [DataRow("glTF-Binary",true)]
 public Task RemainingCoreForms(string variant,bool compiled) => Run(variant,compiled);

 async Task Run(string variant,bool compiled)
 {
  await MainThread.Wait(); var corpus=Path.Combine(CheckoutRoot,"Temp/gltf-reference/glTF-Sample-Assets"); var source=Path.Combine(corpus,"Models/BrainStem",variant); var bytes=File.ReadAllBytes(Path.Combine(source,variant=="glTF-Binary"?"BrainStem.glb":"BrainStem.gltf"));
  using var oracle=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(CheckoutRoot,"Temp/gltf-validation/direct-gpu/BrainStem/reference.json"))); var root=oracle.RootElement; var input=root.GetProperty("assets").GetProperty(variant); var reference=root.GetProperty("variants").GetProperty(variant);
  Check(Hash(bytes)==input.GetProperty("sha256").GetString(),"Pinned source changed"); foreach(var dep in input.GetProperty("dependencies").EnumerateObject()) Check(Hash(File.ReadAllBytes(Path.Combine(corpus,dep.Name)))==dep.Value.GetString(),"Pinned dependency changed");
  byte[] Resolve(string uri)=>File.ReadAllBytes(Path.Combine(source,Uri.UnescapeDataString(uri)));
  var output=Path.Combine(CheckoutRoot,"Temp/gltf-validation/direct-gpu/BrainStem/public-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(output);
  using var capture=new GltfGpuTriangleGateTests.NativeCapture(output,new Vector3(149.1369f,92.5004f,78.5178f),new Vector3(-1.68637f,-1.76414f,36.0988f),125f);
  var light=new SceneDirectionalLight(capture.Scene.SceneWorld,Rotation.LookAt(new Vector3(-.8248628f,-.5155393f,-.2319927f)),Color.White*2);
  var directory=Path.Combine(Path.GetTempPath(),"brainstem_"+Guid.NewGuid().ToString("N")); var path="brainstem_"+Guid.NewGuid().ToString("N")+(compiled?".glb_c":variant=="glTF-Binary"?".glb":".gltf"); BaseFileSystem aggregate=null; LocalFileSystem mount=null; Model model=null; SkinnedModelRenderer renderer=null; var phases=new List<object>(); var captures=new List<object>(); string error=null;
  try {
   Directory.CreateDirectory(directory); File.WriteAllBytes(Path.Combine(directory,path),compiled?GltfCompiler.Compile(bytes,Resolve):bytes); if(!compiled && variant=="glTF") File.Copy(Path.Combine(source,"BrainStem0.bin"),Path.Combine(directory,"BrainStem0.bin")); aggregate=Sandbox.FileSystem.Mounted; mount=new LocalFileSystem(directory); aggregate.Mount(mount);
   var empty=await capture.Take("empty_before"); Check(empty.Coverage==0,"empty before not empty");
   model=Model.Load(path); Check(model is {IsValid:true,IsError:false} && model.native.IsNull && model.GltfResource is not null,"public load not managed");
   using(capture.Scene.Push()) { renderer=capture.Scene.CreateObject().Components.Create<SkinnedModelRenderer>(false); renderer.UseAnimGraph=false; renderer.PlayAnimationsInEditorScene=true; renderer.PlaybackRate=0; renderer.Model=model; renderer.Sequence.Name="animation_0"; renderer.Enabled=true; }
   var instance=renderer.GltfInstance; Check(instance is not null && renderer.SceneModel is null && renderer.SceneObject is SceneCustomObject && renderer.SceneObject.Model is null,"not actual native-free skinned component");
   foreach(var sample in reference.GetProperty("samples").EnumerateArray().Skip(1)) {
    float time=sample.GetProperty("time").GetSingle(); renderer.Sequence.Time=time; using(capture.Scene.Push()) capture.Scene.EditorTick(0,0); var frame=await capture.Take("pose_"+sample.GetProperty("label").GetString()); await MainThread.Wait(); captures.Add(new {frame.Path,frame.Sha256,frame.Coverage,frame.EngineFrame,frame.ObservedWallclockSeconds,frame.CameraStateJson});
    Check(frame.Coverage>64,"positive frame missing"); Check(instance.DrawCalls>0 && instance.SubmittedRevision==instance.PoseRevision && instance.CallbackErrors.Length==0,"pose not submitted"); var rows=instance.Readback(); Check(rows.Count==59,"unexpected primitive count"); float maxp=0,maxn=0; int checkedVertices=0;
    foreach(var row in rows) { int p=row.Primitive; Check(row.Node==1 && row.Mesh==0 && row.IndexDataOrigin=="gpu-readback","wrong GPU row identity"); var expected=reference.GetProperty("primitives")[p].GetProperty("index_values"); Check(row.Indices.Length==expected.GetArrayLength(),"index count"); for(int i=0;i<row.Indices.Length;i+=3) { var a=row.SourceVertices[row.Indices[i]]; var b=row.SourceVertices[row.Indices[i+1]]; var c=row.SourceVertices[row.Indices[i+2]]; var x=expected[i].GetUInt32(); var y=expected[i+1].GetUInt32(); var z=expected[i+2].GetUInt32(); Check((a==x&&b==y&&c==z)||(a==x&&b==z&&c==y),$"index topology p{p} i{i}"); }
     var pos=row.Fields.Single(x=>x.Semantic=="POSITION"&&x.SemanticIndex==0); var norm=row.Fields.Single(x=>x.Semantic=="NORMAL"&&x.SemanticIndex==0); Check(pos.ComponentType==5126&&norm.ComponentType==5126,"unexpected output type");
     V3 Read(int vertex,int offset) { var b=row.Bytes.AsSpan(vertex*row.VertexStride+offset,12); return new V3(BinaryPrimitives.ReadSingleLittleEndian(b),BinaryPrimitives.ReadSingleLittleEndian(b[4..]),BinaryPrimitives.ReadSingleLittleEndian(b[8..])); }
     foreach(var item in sample.GetProperty("primitives")[p].EnumerateArray()) { int sourceVertex=item.GetProperty("vertex").GetInt32(); int v=Array.FindIndex(row.SourceVertices,x=>x==sourceVertex); Check(v>=0,"source vertex missing"); var ep=item.GetProperty("position"); var en=item.GetProperty("normal"); var xp=new V3(ep[2].GetSingle(),ep[0].GetSingle(),ep[1].GetSingle())/.0254f; var xn=V3.Normalize(new V3(en[2].GetSingle(),en[0].GetSingle(),en[1].GetSingle())); float pe=V3.Distance(xp,Read(v,pos.Offset)),ne=V3.Distance(xn,Read(v,norm.Offset)); maxp=Math.Max(maxp,pe); maxn=Math.Max(maxn,ne); Check(float.IsFinite(pe)&&float.IsFinite(ne)&&pe<=.001f&&ne<=.0002f,$"GPU p{p} vertex{sourceVertex} errors {pe} {ne}"); checkedVertices++; }
    }
    phases.Add(new {time,checkedVertices,maxPositionErrorInches=maxp,maxNormalError=maxn,rows=rows.Count,instance.DrawCalls,instance.PoseRevision,instance.SubmittedRevision});
   }
   using(capture.Scene.Push()) { renderer.GameObject.Destroy(); capture.Scene.EditorTick(0,0); capture.Scene.ProcessDeletes(); } renderer=null; int calls=instance.DrawCalls; var deleted=await capture.Take("empty_after"); Check(deleted.Coverage==0&&instance.DrawCalls==calls,"renderer deletion failed"); Check(model.IsValid,"resource lost after component deletion");
  } catch(Exception e) { error=e.ToString(); }
  finally { await MainThread.Wait(); void Clean(Action action,string label) { try { action(); } catch(Exception e) { error+="\ncleanup "+label+": "+e; } } Clean(()=> { if(renderer is not null) { using var scope=capture.Scene.Push(); renderer.GameObject.Destroy(); capture.Scene.EditorTick(0,0); capture.Scene.ProcessDeletes(); } },"renderer"); Clean(()=>model?.Destroy(),"model"); Clean(()=> { if(mount is not null) aggregate.UnMount(mount); },"unmount"); Clean(()=>mount?.Dispose(),"mount"); Clean(()=> { if(Directory.Exists(directory)) Directory.Delete(directory,true); },"directory"); Clean(()=> { if(light.IsValid()) light.Delete(); },"light"); }
  Measurements.Add(new {form=variant+(compiled?"/compiled":"/source"),output,phases,captures,error,scope="Public Model.Load and SkinnedModelRenderer, sampled independent GPU positions/normals and triangle connectivity; deterministic poses do not prove real-time cadence or appearance parity."}); Assert.IsNull(error,error);
 }
}
