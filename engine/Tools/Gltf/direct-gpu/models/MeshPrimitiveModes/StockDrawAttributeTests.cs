using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Sandbox.Gltf.DirectGpu.MeshPrimitiveModesValidation.MeshPrimitiveModesReference;
namespace Sandbox.Gltf.DirectGpu.MeshPrimitiveModesValidation;
[TestClass]
internal sealed class StockDrawAttributeTests
{
 static string checkout;public static List<object> Measurements{get;}=new();public static void Configure(string root){checkout=root;Measurements.Clear();}
 [TestMethod]
 public async Task LegacyStockParametersAndLitTriangleArePreserved()
 {
  await MainThread.Wait();var stock=File.ReadAllBytes(Path.Combine(checkout,"game/core/shaders/gltf_stock_sampler.shader"));var candidateBytes=File.ReadAllBytes(Path.Combine(checkout,"engine/Tools/Gltf/direct-gpu/models/MeshPrimitiveModes/stock_draw_attribute_candidate.shader"));Require(Hash(candidateBytes)=="f6796331fc5b4d941b767c389581b72a07a9d30617106d2629e27d20cec46cf8","Candidate source changed.");
  var output=Path.Combine(checkout,"Temp/gltf-validation/direct-gpu/MeshPrimitiveModes/stock-flag",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);Shader originalShader=null,candidateShader=null;Material originalTemplate=null,candidateTemplate=null,current=null;GltfMaterialLease<Material> originalLease=null,candidateLease=null;Texture green=null;SceneCustomObject obj=null;var frames=new List<object>();var errors=new ConcurrentQueue<string>();int draws=0;
  using var capture=new GltfGpuTriangleGateTests.NativeCapture(output,new Vector3(200,0,0),Vector3.Zero,140);capture.Camera.AutoExposure.Enabled=false;capture.Camera.AutoExposure.MinimumExposure=1;capture.Camera.AutoExposure.MaximumExposure=1;capture.Camera.AutoExposure.Compensation=0;
  try
  {
   async Task<Shader> Compile(byte[] b){using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(90));var shader=await Shader.CompileAsync(Encoding.UTF8.GetString(b),timeout.Token);await MainThread.Wait();Require(shader is {IsValid:true,IsAnonymous:true},"Stock shader compile failed.");return shader;}
   originalShader=await Compile(stock);candidateShader=await Compile(candidateBytes);originalTemplate=Material.Create("stock_original_"+Guid.NewGuid().ToString("N"),originalShader);candidateTemplate=Material.Create("stock_flag_"+Guid.NewGuid().ToString("N"),candidateShader);
   using var doc=new Inputs(checkout).Get("glTF/source").Read();var source=new GltfMaterial{BaseColorFactor=new System.Numerics.Vector4(.6f,.8f,.4f,1),MetallicFactor=0,RoughnessFactor=1,AlphaMode="OPAQUE",AlphaCutoff=.5f};var prepared=GltfStockMaterialAdapter.Prepare(doc,source);
   originalLease=GltfStockMaterialAdapter.Acquire(prepared,originalTemplate,linearVertexColor:true);candidateLease=GltfStockMaterialAdapter.Acquire(prepared,candidateTemplate,linearVertexColor:true);var original=originalLease.Value;var candidate=candidateLease.Value;
   Require(original.HasParameter("g_vColorTint") && candidate.HasParameter("g_vColorTint") && original.HasParameter("g_tColor2") && candidate.HasParameter("g_tColor2"),"Legacy parameter surface missing.");
   Vertex V(float y,float z)=>new(new Vector3(0,y,z)){Normal=Vector3.Forward,Tangent=new Vector4(0,1,0,1),Color=Color32.White};var line=new[]{V(-30,-25),V(-30,25)};var tri=new[]{V(15,-25),V(55,-25),V(35,25)};var lineAttrs=new RenderAttributes();var triAttrs=new RenderAttributes();current=original;
   obj=new SceneCustomObject(capture.Scene.SceneWorld);obj.Flags.IsOpaque=true;obj.Bounds=BBox.FromPoints(line.Concat(tri).Select(v=>v.Position));obj.RenderOverride=_=>{try{lineAttrs.Clear();current.Attributes.MergeTo(lineAttrs);Graphics.Attributes.MergeTo(lineAttrs);lineAttrs.Set("GltfPointLineNoNormal",1f);Graphics.Draw(line.AsSpan(),line.Length,current,lineAttrs,Graphics.PrimitiveType.Lines);triAttrs.Clear();current.Attributes.MergeTo(triAttrs);Graphics.Attributes.MergeTo(triAttrs);triAttrs.Set("GltfPointLineNoNormal",0f);Graphics.Draw(tri.AsSpan(),tri.Length,current,triAttrs,Graphics.PrimitiveType.Triangles);Interlocked.Increment(ref draws);}catch(Exception e){errors.Enqueue(e.ToString());}};
   var baseline=await Take("original_stock_lit");current=candidate;var combined=await Take("candidate_stock_lit");var max=MaxDiff(baseline,combined,96,192);Require(Coverage(baseline,96,192)>64 && max<=1f/255f,"Candidate lit triangle changed from original stock shader.");
   var light=(ScenePointLight)typeof(GltfGpuTriangleGateTests.NativeCapture).GetField("light",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(capture);var previous=light.LightColor;light.LightColor=Color.Black;var dark=await Take("candidate_light_off");light.LightColor=previous;var restored=await Take("candidate_light_restored");
   Require(Coverage(combined,0,96)>0 && Coverage(dark,0,96)==Coverage(combined,0,96) && Coverage(combined,96,192)>64 && Coverage(dark,96,192)==0,"Stock candidate per-draw lighting separation failed.");
   var tint=candidate.GetVector4("g_vColorTint");candidate.Set("g_vColorTint",new Vector4(.1f,.1f,.1f,1));var tintChange=await Take("legacy_tint_mutation");var tintLine=Changed(restored,tintChange,0,96);var tintTriangle=Changed(restored,tintChange,96,192);Require(tintLine>0 && tintTriangle>64,"Legacy g_vColorTint mutation missed a primitive.");candidate.Set("g_vColorTint",tint);
   var oldTexture=candidate.GetTexture("g_tColor2");green=Texture.Create(1,1,ImageFormat.RGBA8888).WithMips(1).WithData(new byte[]{0,255,0,255}).WithStaticUsage().Finish();candidate.Set("g_tColor2",green);var textureChange=await Take("legacy_texture_mutation");var texLine=Changed(restored,textureChange,0,96);var texTriangle=Changed(restored,textureChange,96,192);Require(texLine>0 && texTriangle>64,"Legacy g_tColor2 mutation missed a primitive.");candidate.Set("g_tColor2",oldTexture);var reset=await Take("legacy_material_restored");Require(Changed(restored,reset,0,96)==0 && Changed(restored,reset,96,192)==0,"Legacy material restore differed.");
   line[0].Color=new Color32(128,255,64,255);line[1].Color=new Color32(128,255,64,255);var authoredColor=await Take("linear_vertex_color");
   line[0].Color=Color32.White;line[1].Color=Color32.White;
   candidate.Set("g_vColorTint",new Vector4(GltfStockMaterialAdapter.EncodeTint(.6f*128f/255f),GltfStockMaterialAdapter.EncodeTint(.8f),GltfStockMaterialAdapter.EncodeTint(.4f*64f/255f),1));var colorExpected=await Take("linear_vertex_color_literal_product");var colorError=MaxDiff(authoredColor,colorExpected,0,96);Require(colorError<=1f/255f,"Authored linear vertex color disagrees with literal product.");
   candidate.Set("g_vColorTint",tint);var missingColor=await Take("wrong_missing_vertex_color");Require(Changed(authoredColor,missingColor,0,96)>0,"Missing vertex-color negative did not alter line.");Measurements.Add(new{vertexColorError=colorError,scope="Actual normalized COLOR0 compared against independently calculated encoded-tint product; same stock shader."});
   Require(draws>0 && errors.IsEmpty,"Draw callback failed: "+string.Join("\n",errors));obj.RenderOverride=null;obj.Delete();obj=null;using(capture.Scene.Push())capture.Scene.ProcessDeletes();Require((await Take("empty_after_delete")).Coverage==0,"Deletion failed.");
   Measurements.Add(new{passed=true,stock_sha256=Hash(stock),candidate_sha256=Hash(candidateBytes),max_lit_triangle_error=max,tintLine,tintTriangle,texLine,texTriangle,frames,scope="Literal direct line and triangle share one anonymous legacy stock adapter material; per-draw attributes, old shader parameter mutations and original stock triangle control. Not public route or full vertex/texture semantics."});
  }
  finally{await MainThread.Wait();if(obj.IsValid()){obj.RenderOverride=null;obj.Delete();}originalLease?.Dispose();candidateLease?.Dispose();originalTemplate?.Destroy();candidateTemplate?.Destroy();green?.Dispose();originalShader?.Destroy();candidateShader?.Destroy();Measurements.Add(new{frames,output});}
  async Task<GltfGpuTriangleGateTests.NativeCapture.Frame> Take(string label){var f=await capture.Take(label);frames.Add(new{label,f.Path,f.Sha256,f.Coverage});return f;}
 }
 static int Coverage(GltfGpuTriangleGateTests.NativeCapture.Frame f,int x0,int x1){int n=0;for(int y=0;y<192;y++)for(int x=x0;x<x1;x++){var p=f.Pixels[y*192+x];if(p.r>.03f||p.g>.03f||p.b>.03f)n++;}return n;}
 static int Changed(GltfGpuTriangleGateTests.NativeCapture.Frame a,GltfGpuTriangleGateTests.NativeCapture.Frame b,int x0,int x1){int n=0;for(int y=0;y<192;y++)for(int x=x0;x<x1;x++){var p=a.Pixels[y*192+x];var q=b.Pixels[y*192+x];if(Math.Abs(p.r-q.r)+Math.Abs(p.g-q.g)+Math.Abs(p.b-q.b)>.02f)n++;}return n;}
 static float MaxDiff(GltfGpuTriangleGateTests.NativeCapture.Frame a,GltfGpuTriangleGateTests.NativeCapture.Frame b,int x0,int x1){float n=0;for(int y=0;y<192;y++)for(int x=x0;x<x1;x++){var p=a.Pixels[y*192+x];var q=b.Pixels[y*192+x];n=Math.Max(n,Math.Max(Math.Abs(p.r-q.r),Math.Max(Math.Abs(p.g-q.g),Math.Abs(p.b-q.b))));}return n;}
}
