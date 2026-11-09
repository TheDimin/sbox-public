using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NMatrix = System.Numerics.Matrix4x4;
using static Sandbox.Gltf.DirectGpu.MeshPrimitiveModesValidation.MeshPrimitiveModesReference;

namespace Sandbox.Gltf.DirectGpu.MeshPrimitiveModesValidation;

[TestClass]
internal sealed class MeshPrimitiveModesRasterTests
{
 private static string checkout;
 public static List<object> Measurements { get; } = new();
 public static void Configure(string root) { checkout = root; Measurements.Clear(); }
 [TestMethod] [DataRow(false)] [DataRow(true)]
 public async Task SourceMaterialAndIndependentModeControls(bool compiled)
 {
  await MainThread.Wait(); var root=Path.Combine(checkout,"Temp/gltf-reference/glTF-Sample-Assets/Models/MeshPrimitiveModes/glTF");
  var source=File.ReadAllBytes(Path.Combine(root,"MeshPrimitiveModes.gltf")); var bin=File.ReadAllBytes(Path.Combine(root,"buffer.bin"));
  Require(Hash(source)=="2a8b3209c25e2fca5105539136a05f42d170767f0eb16148cd3825758e5ee977" && Hash(bin)=="4369522e5929bed650242fea069c687aa2ea1230caa61c378610b2154c1048d7","Source changed.");
  byte[] Resolve(string uri) { Require(uri=="buffer.bin","Unexpected dependency."); return bin; }
  var bytes=compiled?GltfCompiler.Compile(source,Resolve):source;
  var output=Path.Combine(checkout,"Temp/gltf-validation/direct-gpu/MeshPrimitiveModes/raster",Guid.NewGuid().ToString("N")); Directory.CreateDirectory(output);
  var dir=Path.Combine(Path.GetTempPath(),"gltf_modes_raster_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
  var name="modes_"+Guid.NewGuid().ToString("N")+(compiled?".glb_c":".gltf");File.WriteAllBytes(Path.Combine(dir,name),bytes);if(!compiled)File.WriteAllBytes(Path.Combine(dir,"buffer.bin"),bin);
  LocalFileSystem mount=null;Model model=null;ModelRenderer renderer=null;var refs=new List<Literal>();var frames=new List<object>();
  using var capture=new GltfGpuTriangleGateTests.NativeCapture(output,new Vector3(500,0,0),Vector3.Zero,360);
  capture.Camera.AutoExposure.Enabled=false;capture.Camera.AutoExposure.MinimumExposure=1;capture.Camera.AutoExposure.MaximumExposure=1;capture.Camera.AutoExposure.Compensation=0;
  var light=(ScenePointLight)typeof(GltfGpuTriangleGateTests.NativeCapture).GetField("light",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(capture);
  try
  {
   mount=new LocalFileSystem(dir);FileSystem.Mounted.Mount(mount); var empty=await Take("empty_before");Require(empty.Coverage==0,"Empty capture not empty.");
   model=Model.Load(name);Require(model is {IsValid:true,IsError:false} && model.native.IsNull && model.GltfResource is not null,"Public loader did not create managed model.");
   var material=model.Materials.Single();Require(material is {IsValid:true,IsError:false},"Missing default material.");
   using(capture.Scene.Push()){renderer=capture.Scene.CreateObject().Components.Create<ModelRenderer>(false);renderer.Model=model;renderer.Enabled=true;capture.Scene.EditorTick(0,0);}
   var instance=renderer.GltfInstance;Require(instance is not null && renderer.SceneObject is SceneCustomObject && renderer.SceneObject.Model is null,"Missing public native-free renderer.");
   var lit=await Take("public_default_material_lit");Require(instance.DrawCalls>0 && instance.CallbackErrors.Length==0,"Public draw callback failed.");
   var originalLight=light.LightColor;light.LightColor=Color.Black;var dark=await Take("public_default_material_no_direct_light");light.LightColor=originalLight;var restored=await Take("public_light_restored");
   var lightRows=Regions(lit.Pixels,dark.Pixels);var restoreRows=Regions(lit.Pixels,restored.Pixels);
   Measurements.Add(new{compiled,input_sha256=Hash(bytes),material=material.ShaderName,lightRows,restoreRows,frames,scope="Same source material and camera with direct light on/off/on; fixed camera exposure. Background/ambient and native postprocessing remain part of scene."});
   Require(lightRows.Skip(4).Any(x=>x.rgbChanged>0),"Direct-light control did not affect any triangle pixels.");
   var draws=((IEnumerable)typeof(GltfGpuInstance).GetField("draws",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(instance)).Cast<object>().ToArray();
   Require(draws.Length==7,"Expected seven public draw buffers.");
   foreach(var draw in draws)
   {
    var node=((GltfGpuAsset.Node)draw.GetType().GetField("Node",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(draw)).Index;
    var indices=(GpuBuffer)draw.GetType().GetField("Indices",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(draw);
    var original=instance.Readback().Single(x=>x.Node==node).Indices;var bad=original.ToArray();int width=node==0?1:node<4?2:3;
    if(node==0)bad[^1]=bad[0];else for(int i=1;i<width;i++)bad[bad.Length-i]=bad[bad.Length-width];
    try
    {
     indices.SetData<uint>(bad);var negative=await Take("public_corrupt_bound_index_mode_"+node);var delta=Regions(lit.Pixels,negative.Pixels);
     Measurements.Add(new{compiled,node,public_raster_negative=delta,negative.Path,negative.Sha256});
     Require(delta[node].different>0 && delta[node].second>0,"Public bound-index negative must visibly change its mode without removing all pixels.");
     Require(delta.Where((x,i)=>i!=node).All(x=>x.different==0),"Public bound-index negative changed other regions.");
    }
    finally {indices.SetData<uint>(original);await capture.WaitFrames();}
   }
   DestroyRenderer();Require((await Take("empty_after_public")).Coverage==0,"Public renderer deletion failed.");
   for(int i=0;i<7;i++)refs.Add(new Literal(capture.Scene.SceneWorld,i,material,false));
   var positive=await Take("independent_literal_positive");Require(refs.All(x=>x.DrawCalls>0 && x.Errors.Count==0),"Literal callback failed.");
   for(int mode=0;mode<7;mode++)
   {
    refs[mode].Dispose();refs[mode]=new Literal(capture.Scene.SceneWorld,mode,material,true);
    var negative=await Take("remove_one_primitive_mode_"+mode);var comparisons=Regions(positive.Pixels,negative.Pixels);
    Measurements.Add(new{compiled,mode,comparisons,negative.Path,negative.Sha256});
    Require(comparisons[mode].different>0 && comparisons[mode].second>0,"Nondegenerate mode negative failed to change visible pixels or lost the entire shape.");
    Require(comparisons.Where((x,i)=>i!=mode).All(x=>x.different==0),"Mode-specific negative changed another mode region.");
    refs[mode].Dispose();refs[mode]=new Literal(capture.Scene.SceneWorld,mode,material,false);
   }
   var comparison=Regions(lit.Pixels,positive.Pixels);Measurements.Add(new{compiled,comparison,frames});
   Require(comparison.All(x=>x.first>0 && x.second>0 && x.different==0),"Public/literal foreground geometry differs in a mode region.");
   ClearRefs();Require((await Take("empty_after_literal")).Coverage==0,"Literal delete failed.");
   Measurements.Add(new{compiled,completed=true,frames,scope="Lit source material; independent mode-specific removal controls and positive exact foreground masks. No full appearance parity claim."});
  }
  finally {await MainThread.Wait();ClearRefs();DestroyRenderer();if(model is not null && model.ResourcePath==Resource.FixPath(name))model.Destroy();if(mount is not null){FileSystem.Mounted.UnMount(mount);mount.Dispose();}Directory.Delete(dir,true);Measurements.Add(new{compiled,frames,output});}
  async Task<GltfGpuTriangleGateTests.NativeCapture.Frame> Take(string label){var f=await capture.Take(label);frames.Add(new{label,f.Path,f.Sha256,f.Coverage,f.CameraStateJson});return f;}
  void ClearRefs(){foreach(var x in refs)x.Dispose();refs.Clear();using var scope=capture.Scene.Push();capture.Scene.ProcessDeletes();}
  void DestroyRenderer(){if(renderer is null)return;using var scope=capture.Scene.Push();renderer.GameObject.Destroy();capture.Scene.EditorTick(0,0);capture.Scene.ProcessDeletes();renderer=null;}
 }
 private sealed record Region(int mode,int first,int second,int different,int rgbChanged);
 private static Region[] Regions(Color[] a,Color[] b)
 {
  // Independent orthographic projection of the authored hexagon bounds. Each
  // center is separated by two meters; the prior 64px grid clipped two shapes.
  const double pixelsPerMeter=192.0/(360.0*.0254);
  var rects=Translations.Select(t=>{double cx=96+t.X*pixelsPerMeter,cy=96-t.Y*pixelsPerMeter;
   return ((int)Math.Floor(cx-.866*pixelsPerMeter-2),(int)Math.Floor(cy-pixelsPerMeter-2),(int)Math.Ceiling(cx+.866*pixelsPerMeter+2),(int)Math.Ceiling(cy+pixelsPerMeter+2));}).ToArray();
  return rects.Select((rect,i)=>{int first=0,second=0,different=0,rgb=0;for(int y=rect.Item2;y<rect.Item4;y++)for(int x=rect.Item1;x<rect.Item3;x++){var p=a[y*192+x];var q=b[y*192+x];bool pfg=MeshPrimitiveModesImageChecks.Foreground(p),qfg=MeshPrimitiveModesImageChecks.Foreground(q);if(pfg)first++;if(qfg)second++;if(pfg!=qfg)different++;if(Math.Abs(p.r-q.r)+Math.Abs(p.g-q.g)+Math.Abs(p.b-q.b)>.01f)rgb++;}return new Region(i,first,second,different,rgb);}).ToArray();
 }
 private sealed class Literal:IDisposable
 {
  readonly SceneCustomObject obj;public int DrawCalls;public List<string> Errors=new();
  public Literal(SceneWorld world,int mode,Material material,bool removeLast)
  {
   var indices=DrawHexagonIndices[mode];int take=indices.Length-(removeLast?(mode==0?1:mode<4?2:3):0);
   var n=ExpectedTriangleNormal(NMatrix.Identity);var normal=new Vector3(n.X,n.Y,n.Z);var tangent=Vector3.Cross(Vector3.Up,normal).Normal;
   Vertex VertexAt(uint index){var p=ExpectedPosition(mode,index,NMatrix.Identity);return new Vertex(new Vector3(p.X,p.Y,p.Z)){Normal=normal,Tangent=new Vector4(tangent.x,tangent.y,tangent.z,1),Color=Color32.White};}
   var vertices=indices.Take(take).Select(VertexAt).ToArray();var bounds=Enumerable.Range(0,7).Select(i=>VertexAt((uint)i).Position).ToArray();
   var type=mode==0?Graphics.PrimitiveType.Points:mode<4?Graphics.PrimitiveType.Lines:Graphics.PrimitiveType.Triangles;
   obj=new SceneCustomObject(world);obj.Flags.IsOpaque=true;obj.Flags.IsTranslucent=false;obj.Bounds=BBox.FromPoints(bounds);
   obj.RenderOverride=_=>{try{Graphics.Draw(vertices.AsSpan(),vertices.Length,material,primitiveType:type);DrawCalls++;}catch(Exception e){Errors.Add(e.ToString());}};
  }
  public void Dispose(){if(obj.IsValid()){obj.RenderOverride=null;obj.Delete();}}
 }
}
