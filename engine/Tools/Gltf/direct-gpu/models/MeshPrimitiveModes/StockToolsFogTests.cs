using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Rendering;
using static Sandbox.Gltf.DirectGpu.MeshPrimitiveModesValidation.MeshPrimitiveModesReference;
namespace Sandbox.Gltf.DirectGpu.MeshPrimitiveModesValidation;
[TestClass]
internal sealed class StockToolsFogTests
{
 static string checkout;public static List<object> Measurements{get;}=new();public static void Configure(string root){checkout=root;Measurements.Clear();}
 [TestMethod]
 public async Task ToolsAndFogMatchOriginalStock()
 {
  await MainThread.Wait();var old=File.ReadAllBytes(Path.Combine(checkout,"game/core/shaders/gltf_stock_sampler.shader"));var modified=File.ReadAllBytes(Path.Combine(checkout,"engine/Tools/Gltf/direct-gpu/models/MeshPrimitiveModes/stock_draw_attribute_candidate.shader"));Require(Hash(modified)=="f6796331fc5b4d941b767c389581b72a07a9d30617106d2629e27d20cec46cf8","Candidate changed.");var output=Path.Combine(checkout,"Temp/gltf-validation/direct-gpu/MeshPrimitiveModes/stock-tools",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);
  Shader a=null,b=null;Material at=null,bt=null,current=null;GltfMaterialLease<Material> al=null,bl=null;SceneCustomObject obj=null;var frames=new List<object>();var errors=new ConcurrentQueue<string>();
  using var capture=new GltfGpuTriangleGateTests.NativeCapture(output,new Vector3(200,0,0),Vector3.Zero,140);
  try
  {
   async Task<Shader> Compile(byte[] bytes){using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(90));var sh=await Shader.CompileAsync(Encoding.UTF8.GetString(bytes),timeout.Token);await MainThread.Wait();Require(sh is {IsValid:true,IsAnonymous:true},"Shader compile failed.");return sh;}
   a=await Compile(old);b=await Compile(modified);at=Material.Create("tools_stock_old_"+Guid.NewGuid().ToString("N"),a);bt=Material.Create("tools_stock_new_"+Guid.NewGuid().ToString("N"),b);using var doc=new Inputs(checkout).Get("glTF/source").Read();var material=new GltfMaterial{BaseColorFactor=new System.Numerics.Vector4(.6f,.8f,.4f,1),MetallicFactor=0,RoughnessFactor=1,AlphaMode="OPAQUE",AlphaCutoff=.5f};var prep=GltfStockMaterialAdapter.Prepare(doc,material);al=GltfStockMaterialAdapter.Acquire(prep,at,true);bl=GltfStockMaterialAdapter.Acquire(prep,bt,true);
   Vertex V(float y,float z)=>new(new Vector3(0,y,z)){Normal=Vector3.Forward,Tangent=new Vector4(0,1,0,1),Color=Color32.White};var line=new[]{V(-30,-25),V(-30,25)};var tri=new[]{V(15,-25),V(55,-25),V(35,25)};var la=new RenderAttributes();var ta=new RenderAttributes();obj=new SceneCustomObject(capture.Scene.SceneWorld);obj.Flags.IsOpaque=true;obj.Bounds=BBox.FromPoints(line.Concat(tri).Select(v=>v.Position));current=al.Value;
   obj.RenderOverride=_=>{try{la.Clear();current.Attributes.MergeTo(la);Graphics.Attributes.MergeTo(la);la.Set("GltfPointLineNoNormal",1f);Graphics.Draw(line.AsSpan(),line.Length,current,la,Graphics.PrimitiveType.Lines);ta.Clear();current.Attributes.MergeTo(ta);Graphics.Attributes.MergeTo(ta);ta.Set("GltfPointLineNoNormal",0f);Graphics.Draw(tri.AsSpan(),tri.Length,current,ta,Graphics.PrimitiveType.Triangles);}catch(Exception e){errors.Enqueue(e.ToString());}};
   foreach(var mode in new[]{SceneCameraDebugMode.Albedo,SceneCameraDebugMode.NormalMap,SceneCameraDebugMode.Roughness})
   {
    capture.Camera.DebugMode=mode;current=al.Value;var before=await Take(mode+"_original");current=bl.Value;var after=await Take(mode+"_candidate");var error=Max(before.Pixels,after.Pixels);Measurements.Add(new{mode=mode.ToString(),error,before=before.Path,after=after.Path,before.Coverage});Require(before.Coverage>0 && error<=1f/255f,"Candidate changes stock tools pass: "+mode);
   }
   capture.Camera.DebugMode=SceneCameraDebugMode.Normal;current=bl.Value;
   var noFog=await Take("candidate_no_fog");
   capture.Scene.SceneWorld.GradientFog=new GradientFogSetup{Enabled=true,StartDistance=0,EndDistance=100,StartHeight=-1000,EndHeight=1000,MaximumOpacity=1,Color=Color.Red,DistanceFalloffExponent=1,VerticalFalloffExponent=1};
   var withFog=await Take("candidate_fog");var fogLine=Changed(noFog.Pixels,withFog.Pixels,0,96);Require(fogLine>0,"Owned fog control did not visibly change the no-normal line.");
   current=al.Value;var originalFog=await Take("original_fog");var fogTriangle=MaxRegion(originalFog.Pixels,withFog.Pixels,96,192);Require(fogTriangle<=1f/255f,"Candidate fog triangle differs from original.");Measurements.Add(new{fogLine,fogTriangle,scope="Native gradient fog sensitivity for candidate line and same-scene original triangle comparison."});
   capture.Scene.SceneWorld.GradientFog=default;
   Require(errors.IsEmpty,"Draw callback failed: "+string.Join("\n",errors));obj.RenderOverride=null;obj.Delete();obj=null;using(capture.Scene.Push())capture.Scene.ProcessDeletes();Require((await Take("empty_after")).Coverage==0,"Delete failed.");Measurements.Add(new{passed=true,scope="Anonymous stock adapter shader Albedo, NormalMap and Roughness native camera modes; literal line+triangle and persistent per-draw flags. Not public route or depth-pass validation."});
  }
  finally{await MainThread.Wait();if(obj.IsValid()){obj.RenderOverride=null;obj.Delete();}al?.Dispose();bl?.Dispose();at?.Destroy();bt?.Destroy();a?.Destroy();b?.Destroy();Measurements.Add(new{frames,output});}
  async Task<GltfGpuTriangleGateTests.NativeCapture.Frame> Take(string name){var f=await capture.Take(name);frames.Add(new{name,f.Path,f.Sha256,f.Coverage});return f;}
 }
 static int Changed(Color[] a,Color[] b,int x0,int x1){int n=0;for(int y=0;y<192;y++)for(int x=x0;x<x1;x++){var p=a[y*192+x];var q=b[y*192+x];if(Math.Abs(p.r-q.r)+Math.Abs(p.g-q.g)+Math.Abs(p.b-q.b)>.02f)n++;}return n;}
 static float MaxRegion(Color[] a,Color[] b,int x0,int x1){float n=0;for(int y=0;y<192;y++)for(int x=x0;x<x1;x++){var p=a[y*192+x];var q=b[y*192+x];n=Math.Max(n,Math.Max(Math.Abs(p.r-q.r),Math.Max(Math.Abs(p.g-q.g),Math.Abs(p.b-q.b))));}return n;}
 static float Max(Color[] a,Color[] b){Require(a.Length==b.Length,"Image dimensions differ.");float n=0;for(int i=0;i<a.Length;i++)n=Math.Max(n,Math.Max(Math.Abs(a[i].r-b[i].r),Math.Max(Math.Abs(a[i].g-b[i].g),Math.Abs(a[i].b-b[i].b))));return n;}
}
