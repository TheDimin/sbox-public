using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NMatrix = System.Numerics.Matrix4x4;
using static Sandbox.Gltf.DirectGpu.MorphPrimitivesValidation.MorphPrimitivesReference;

namespace Sandbox.Gltf.DirectGpu.MorphPrimitivesValidation;

/// <summary>Actual public components, named stock shaders and independent raw-source arithmetic.</summary>
[TestClass]
internal sealed class MorphPrimitivesPublicTests
{
 static string checkout;
 public static List<object> Measurements { get; } = new();
 public static void Configure( string root ) { checkout = root; Measurements.Clear(); }

 [TestMethod]
 [DataRow(false)]
 [DataRow(true)]
 public async Task OriginalDefaultSignedInterpolatedIndependentInstancesAndDeletion( bool compiled )
 {
  await MainThread.Wait();
  var form = new MorphPrimitivesReference(checkout, new[]{"glTF"}).Forms.Single(x=>x.Compiled==compiled);
  MorphPrimitivesCoreTests.Uploads(form);
  var output = Path.Combine(checkout,"Temp/gltf-validation/direct-gpu/MorphPrimitivesTest/public",Guid.NewGuid().ToString("N")); Directory.CreateDirectory(output);
  using var mounted = new Mounted(form.Bytes, compiled, checkout);
  using var capture = new GltfGpuTriangleGateTests.NativeCapture(output,new global::Vector3(65,70,45),new global::Vector3(0,0,1.5f),58);
  Model model=null; SkinnedModelRenderer a=null,b=null; MorphPrimitivesReferenceScene reference=null;
  var frames=new List<object>(); var groups=new List<object>();
  try
  {
   var empty=await Take("empty_before"); Require(empty.Coverage==0,"Fresh scene not empty.");
   model=Model.Load(mounted.Name);
   Require(model is {IsValid:true,IsError:false} && model.native.IsNull && model.GltfResource is not null,"Public load is not managed native-free model.");
   Require(model.MeshCount==2 && model.Materials.Length==2 && model.MorphCount==1 && model.GltfSelectedScene==0,"Public primitive/material/morph/default-scene identity changed.");
   var target=model.GetMorphName(0); Require(!string.IsNullOrWhiteSpace(target),"Logical source target needs a public name.");
   Require(ReferenceEquals(Model.Load(mounted.Name),model),"Repeated loads did not share resource identity.");
   a=Create(model); var instance=a.GltfInstance;
   Require(instance is not null && a.SceneModel is null && a.SceneObject is SceneCustomObject && a.SceneObject.Model is null,"Public SkinnedModelRenderer did not create actual native-free GPU instance.");
   var actual=await Take("actual_public_default_half_source_material"); await Check(a,.5f,"authored_mesh_default");
   Require(actual.Coverage>100 && actual.Pixels.Count(x=>x.r>x.g+.03f)>20 && actual.Pixels.Count(x=>x.g>x.r+.03f)>20,"Source red and green primitive materials not visibly present.");
   a.Enabled=false; Require((await Take("empty_disabled")).Coverage==0,"Disabled renderer still draws.");
   reference=new(form,capture.Scene.SceneWorld,.5f,NMatrix.Identity,i=>model.Materials[i.Value]);
   var expected=await Take("independent_raw_source_half_reference"); Require(reference.DrawCalls>0&&reference.CallbackErrors.Length==0,"Independent reference failed."); reference.Dispose(); reference=null;
   Require((await Take("empty_after_reference")).Coverage==0,"Deleted reference remains visible.");
   reference=new(form,capture.Scene.SceneWorld,0,NMatrix.Identity,i=>model.Materials[i.Value]);
   var flatReference=await Take("independent_flat_negative_reference"); reference.Dispose(); reference=null;
   var changed=Differences(flatReference,expected); Require(changed>16,"Camera does not expose missing morph.");
   Require((await Take("empty_after_flat_reference")).Coverage==0,"Deleted negative reference remains visible.");
   a.Enabled=true; await Check(a,.5f,"reenabled_default");
   // A deliberately incorrect public pose must be rejected by the independent
   // default oracle, and visibly differ under the same source materials.
   a.Morphs.Set(target,0,0); Tick(); var flat=await Take("actual_public_flat_negative"); await Check(a,0,"manual_zero");
   RequireRejected(()=>MorphPrimitivesGpuChecks.AssertReadback(form,a.GltfInstance.Readback(),.5f,NMatrix.Identity),"Ignored default morph was accepted.");
   Require(Differences(actual,flat)>16,"Actual weight negative did not change pixels.");
   a.Morphs.Clear(target,0); Tick(); await Check(a,.5f,"clear_restores_authored_default");
   b=Create(model); Require(!ReferenceEquals(a.GltfInstance,b.GltfInstance),"Instances share mutable output state.");
   b.Morphs.Set(target,1.5f,0); Tick(); await Check(b,1.5f,"independent_B_over_one");
   var frozen=BHash();
   foreach(var weight in new[]{-.75f,1.5f}) { a.Morphs.Set(target,weight,0); Tick(); await Check(a,weight,"signed_public_override"); Require(BHash().SequenceEqual(frozen),"Updating A mutated B GPU bytes."); }
   a.Morphs.Set(target,-.75f,0); Tick(); a.Morphs.Set(target,1.5f,2); Tick(.5f);
   await Check(a,-.1875f,"public_linear_fade_quarter"); Require(BHash().SequenceEqual(frozen),"Interpolating A mutated B.");
   Tick(1.5f); await Check(a,1.5f,"public_linear_fade_endpoint");
   a.Morphs.Clear(target,0); Tick(); await Check(a,.5f,"clear_after_fade");
   // Corrupt and restore the actual bound index buffer, rather than testing a
   // synthetic CPU snapshot and calling that a GPU negative control.
   var draw=((IEnumerable)typeof(GltfGpuInstance).GetField("draws",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(a.GltfInstance)).Cast<object>().Single(x=>((GltfGpuAsset.Node)x.GetType().GetField("Node",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(x)).Primitive.Index==1);
   var indices=(GpuBuffer)draw.GetType().GetField("Indices",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(draw);
   var original=a.GltfInstance.Readback().Single(x=>x.Primitive==1).Indices.ToArray(); var bad=original.ToArray(); (bad[1],bad[2])=(bad[2],bad[1]);
   try { indices.SetData<uint>(bad); await capture.WaitFrames(); Require(a.GltfInstance.Readback().Single(x=>x.Primitive==1).Indices.SequenceEqual(bad),"Bound GPU mutation was not observed."); RequireRejected(()=>MorphPrimitivesGpuChecks.AssertReadback(form,a.GltfInstance.Readback(),.5f,NMatrix.Identity),"Reversed GPU indices accepted."); }
   finally { indices.SetData<uint>(original); await capture.WaitFrames(); }
   await Check(a,.5f,"restored_bound_indices");
   var formerB=b.GltfInstance; Destroy(ref b); int prior=formerB.DrawCalls; await capture.WaitFrames(); Require(prior==formerB.DrawCalls,"Deleted B callback remains active."); await Check(a,.5f,"survives_other_instance_deletion");
   var restored=await Take("restored_public_default_source_material"); Require(restored.Coverage>100,"Restored image empty.");
   var retained=a.GltfInstance; Destroy(ref a); int calls=retained.DrawCalls;
   Require((await Take("empty_after_public_delete")).Coverage==0 && retained.DrawCalls==calls && model.IsValid,"Public deletion/lifetime failed.");
   Measurements.Add(new{form=form.Name,input_sha256=Hash(form.Bytes),canonical_path=compiled?mounted.CanonicalPath:null,target,source_material_shaders=model.Materials.Select(x=>x.ShaderName).ToArray(),groups,frames,independent_reference_flat_changed_pixels=changed,actual_vs_independent_geometry=Compare(actual,expected),actual_flat_changed_pixels=Differences(actual,flat),bound_gpu_index_negative_rejected=true,scope="Public Model.Load/SkinnedModelRenderer; original source and one canonical compiled package; all primitive-local GPU position/normal/UV/tangent-basis/index checks; signed overrides and scene-time interpolation; no authored animation or normal/tangent deltas, no wall-clock or PBR equivalence claim."});
  }
  finally { await MainThread.Wait(); reference?.Dispose(); Destroy(ref b); Destroy(ref a); model?.Destroy(); Measurements.Add(new{form=form.Name,output,frames,groups}); }
  SkinnedModelRenderer Create(Model value) { using var scope=capture.Scene.Push(); var r=capture.Scene.CreateObject().Components.Create<SkinnedModelRenderer>(false); r.UseAnimGraph=false; r.PlayAnimationsInEditorScene=true; r.PlaybackRate=0; r.Model=value; r.Enabled=true; capture.Scene.EditorTick(0,0); return r; }
  void Tick(float delta=0) { using var scope=capture.Scene.Push(); capture.Scene.EditorTick(delta,delta); }
  void Destroy(ref SkinnedModelRenderer r) { if(r is null)return; using var scope=capture.Scene.Push(); r.GameObject.Destroy(); r=null; capture.Scene.EditorTick(0,0); capture.Scene.ProcessDeletes(); }
  string[] BHash()=>b.GltfInstance.Readback().OrderBy(x=>x.Primitive).Select(x=>Hash(x.Bytes)).ToArray();
  async Task Check(SkinnedModelRenderer renderer,float weight,string phase) { await capture.WaitFrames(); var i=renderer.GltfInstance; Require(i.DrawCalls>0&&i.PoseRevision==i.SubmittedRevision&&i.CallbackErrors.Length==0,"Pose not submitted: "+string.Join("\n",i.CallbackErrors)); var rows=i.Readback(); var measurement=MorphPrimitivesGpuChecks.AssertReadback(form,rows,weight,NMatrix.Identity); string path=Path.Combine(output,phase+"_"+groups.Count+"_gpu.json"); var data=JsonSerializer.SerializeToUtf8Bytes(rows.Select(x=>new{x.Node,x.Mesh,x.Primitive,x.VertexCount,x.VertexStride,x.Fields,x.Bytes,x.SourceVertices,x.Indices,x.IndexDataOrigin}).ToArray()); File.WriteAllBytes(path,data); groups.Add(new{phase,weight,measurement,path,sha256=Hash(data),i.PoseRevision,i.SubmittedRevision,i.DrawCalls}); }
  async Task<GltfGpuTriangleGateTests.NativeCapture.Frame> Take(string label) { var f=await capture.Take(label); frames.Add(new{label,f.Path,f.Sha256,f.Coverage,f.CameraStateJson}); return f; }
 }

 [TestMethod]
 public async Task DerivedNodeDefaultZeroFallbackAndSignedAnimationUsePublicRenderer()
 {
  await MainThread.Wait();
  var form=new MorphPrimitivesReference(checkout,new[]{"glTF"}).Forms.Single(x=>!x.Compiled);
  var output=Path.Combine(checkout,"Temp/gltf-validation/direct-gpu/MorphPrimitivesTest/derived",Guid.NewGuid().ToString("N")); Directory.CreateDirectory(output);
  using var capture=new GltfGpuTriangleGateTests.NativeCapture(output,new global::Vector3(65,70,45),new global::Vector3(0,0,1.5f),58);
  foreach(var mode in new[]{"node_override","zero_fallback","signed_animation"})
  {
   using var document=form.Control(nodeWeight:mode=="node_override"?-.75f:null,zeroFallback:mode=="zero_fallback",animated:mode=="signed_animation");
   using var mounted=new Mounted(System.Text.Encoding.UTF8.GetBytes(document.Root.GetRawText()),false,checkout);
   Model model=null; SkinnedModelRenderer renderer=null; var groups=new List<object>();
   try
   {
    model=Model.Load(mounted.Name); Require(model.IsValid&&!model.IsError&&model.native.IsNull,"Derived public model failed.");
    using(capture.Scene.Push()) { renderer=capture.Scene.CreateObject().Components.Create<SkinnedModelRenderer>(false); renderer.UseAnimGraph=false; renderer.PlayAnimationsInEditorScene=true; renderer.PlaybackRate=0; renderer.Model=model; renderer.Enabled=true; }
    var samples=mode=="signed_animation"?new (float Time,float Weight)[]{(0f,-.75f),(.5f,-.125f),(1f,.5f),(2f,1.5f)}:new (float Time,float Weight)[]{(0f,mode=="node_override"?-.75f:0f)};
    if(mode=="signed_animation") { renderer.Sequence.Name=renderer.Sequence.SequenceNames.Single(); renderer.Sequence.Looping=false; }
    foreach(var sample in samples) { if(mode=="signed_animation") renderer.Sequence.Time=sample.Time; using(capture.Scene.Push())capture.Scene.EditorTick(0,0); await capture.WaitFrames(); var i=renderer.GltfInstance; Require(i is not null && renderer.SceneModel is null && i.DrawCalls>0&&i.PoseRevision==i.SubmittedRevision&&i.CallbackErrors.Length==0,"Derived pose did not draw."); groups.Add(new{sample.Time,sample.Weight,measurement=MorphPrimitivesGpuChecks.AssertReadback(form,i.Readback(),sample.Weight,NMatrix.Identity)}); }
    Measurements.Add(new{form="derived_source_control",mode,groups,input_sha256=Hash(System.Text.Encoding.UTF8.GetBytes(document.Root.GetRawText())),scope="Labeled derived fixture, original asset bytes untouched; actual public clip seeks, not authored sample animation or wall-clock cadence."});
   }
   finally { if(renderer is not null) { using var scope=capture.Scene.Push(); renderer.GameObject.Destroy(); capture.Scene.EditorTick(0,0); capture.Scene.ProcessDeletes(); } model?.Destroy(); }
   Require((await capture.Take(mode+"_deleted_empty")).Coverage==0,"Derived renderer failed deletion.");
  }
 }

 static int Differences(GltfGpuTriangleGateTests.NativeCapture.Frame a,GltfGpuTriangleGateTests.NativeCapture.Frame b)=>a.Pixels.Zip(b.Pixels,(x,y)=>Math.Max(Math.Abs(x.r-y.r),Math.Max(Math.Abs(x.g-y.g),Math.Abs(x.b-y.b)))>8f/255).Count(x=>x);
 static object Compare(GltfGpuTriangleGateTests.NativeCapture.Frame a,GltfGpuTriangleGateTests.NativeCapture.Frame b) { int intersection=0,union=0; foreach(var pair in a.Pixels.Zip(b.Pixels)) {bool x=GltfGpuTriangleGateTests.NativeCapture.Foreground(pair.First),y=GltfGpuTriangleGateTests.NativeCapture.Foreground(pair.Second); if(x&&y)intersection++;if(x||y)union++;} return new{intersection,union,iou=union==0?0:(double)intersection/union,pixels_different_beyond_8_bytes=Differences(a,b),scope="Measured independent literal geometry comparison; human visual review remains pending."}; }
 static void RequireRejected(Action action,string message) { bool rejected=false; try{action();}catch(InvalidOperationException){rejected=true;} Require(rejected,message); }
 sealed class Mounted : IDisposable
 {
  readonly string directory; readonly LocalFileSystem mount;
  public string Name {get;} public string CanonicalPath {get;}
  public Mounted(byte[] bytes,bool compiled,string root)
  {
   directory=Path.Combine(Path.GetTempPath(),"morph_public_"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory); Name="morph_"+Guid.NewGuid().ToString("N")+(compiled?".glb_c":".gltf");
   File.WriteAllBytes(Path.Combine(directory,Name),bytes);
   var source=Path.Combine(root,"Temp/gltf-reference/glTF-Sample-Assets/Models/MorphPrimitivesTest/glTF");
   File.Copy(Path.Combine(source,"uv_texture.jpg"),Path.Combine(directory,"uv_texture.jpg"));
   if(!compiled)File.Copy(Path.Combine(source,"MorphPrimitivesTest.bin"),Path.Combine(directory,"MorphPrimitivesTest.bin"));
   if(compiled) { CanonicalPath=Path.Combine(root,"Temp/gltf-validation/direct-gpu/MorphPrimitivesTest/canonical/MorphPrimitivesTest.glb_c"); Directory.CreateDirectory(Path.GetDirectoryName(CanonicalPath)); File.WriteAllBytes(CanonicalPath,bytes); }
   mount=new LocalFileSystem(directory); FileSystem.Mounted.Mount(mount);
  }
  public void Dispose() { FileSystem.Mounted.UnMount(mount); mount.Dispose(); Directory.Delete(directory,true); }
 }
}

/// <summary>Targeted retry excludes the unchanged derived suite that already passed.</summary>
[TestClass]
internal sealed class MorphPrimitivesPublicOriginalRetryTests
{
 public static List<object> Measurements => MorphPrimitivesPublicTests.Measurements;
 public static void Configure(string root) => MorphPrimitivesPublicTests.Configure(root);
 [TestMethod]
 [DataRow(false)]
 [DataRow(true)]
 public Task OriginalForms(bool compiled) => new MorphPrimitivesPublicTests().OriginalDefaultSignedInterpolatedIndependentInstancesAndDeletion(compiled);
}
