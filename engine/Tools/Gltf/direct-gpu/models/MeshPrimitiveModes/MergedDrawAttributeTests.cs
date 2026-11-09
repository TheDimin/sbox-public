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
internal sealed class MergedDrawAttributeTests
{
 static string checkout;public static List<object> Measurements{get;}=new();public static void Configure(string root){checkout=root;Measurements.Clear();}
 [TestMethod]
 public async Task SharedMaterialDistinctDrawAttributeAndMutation()
 {
  await MainThread.Wait();var bytes=File.ReadAllBytes(Path.Combine(checkout,"engine/Tools/Gltf/direct-gpu/models/MeshPrimitiveModes/combined_alpha_pointline_candidate.shader"));Require(Hash(bytes)=="bf6c5e6f16c801a9892a485a53bc4c6b6bef85d4a86c98232af8139187630ed9","Combined shader changed.");
  var output=Path.Combine(checkout,"Temp/gltf-validation/direct-gpu/MeshPrimitiveModes/combined-attribute",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);var frames=new List<object>();Shader shader=null;Material material=null;SceneCustomObject sceneObject=null;var errors=new ConcurrentQueue<string>();int draws=0;
  using var capture=new GltfGpuTriangleGateTests.NativeCapture(output,new Vector3(200,0,0),Vector3.Zero,140);
  capture.Camera.AutoExposure.Enabled=false;capture.Camera.AutoExposure.MinimumExposure=1;capture.Camera.AutoExposure.MaximumExposure=1;capture.Camera.AutoExposure.Compensation=0;
  try
  {
   using(var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(90)))shader=await Shader.CompileAsync(Encoding.UTF8.GetString(bytes),timeout.Token);await MainThread.Wait();Require(shader is {IsValid:true,IsAnonymous:true},"Combined shader compile failed.");material=Material.Create("mixed_draw_attribute_"+Guid.NewGuid().ToString("N"),shader);Require(material is {IsValid:true,IsError:false},"Combined material invalid.");
   material.Set("g_vGltfBaseColor",new Vector4(1,1,1,1));material.Set("g_flGltfMetallicFactor",0f);material.Set("g_flGltfRoughnessFactor",1f);material.Attributes.Set("GltfPointLineNoNormal",0f);
   Vertex V(float y,float z)=>new(new Vector3(0,y,z)){Normal=Vector3.Forward,Tangent=new Vector4(0,1,0,1),Color=Color32.White};
   var line=new[]{V(-30,-25),V(-30,25)};var tri=new[]{V(15,-25),V(55,-25),V(35,25)};var lineAttrs=new RenderAttributes();var triAttrs=new RenderAttributes();
   sceneObject=new SceneCustomObject(capture.Scene.SceneWorld);sceneObject.Flags.IsOpaque=true;sceneObject.Bounds=BBox.FromPoints(line.Concat(tri).Select(v=>v.Position));
   sceneObject.RenderOverride=_=>{try{lineAttrs.Clear();material.Attributes.MergeTo(lineAttrs);Graphics.Attributes.MergeTo(lineAttrs);lineAttrs.Set("GltfPointLineNoNormal",1f);Graphics.Draw(line.AsSpan(),line.Length,material,lineAttrs,Graphics.PrimitiveType.Lines);triAttrs.Clear();material.Attributes.MergeTo(triAttrs);Graphics.Attributes.MergeTo(triAttrs);triAttrs.Set("GltfPointLineNoNormal",0f);Graphics.Draw(tri.AsSpan(),tri.Length,material,triAttrs,Graphics.PrimitiveType.Triangles);Interlocked.Increment(ref draws);}catch(Exception e){errors.Enqueue(e.ToString());}};
   var light=(ScenePointLight)typeof(GltfGpuTriangleGateTests.NativeCapture).GetField("light",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(capture);var originalLight=light.LightColor;
   var on=await Take("shared_material_light_on");light.LightColor=Color.Black;var off=await Take("shared_material_light_off");light.LightColor=originalLight;var restored=await Take("shared_material_light_restored");
   Require(draws>0 && errors.IsEmpty,"Literal callbacks failed: "+string.Join("\n",errors));
   var onLine=Coverage(on,0,96);var offLine=Coverage(off,0,96);var onTri=Coverage(on,96,192);var offTri=Coverage(off,96,192);
   Measurements.Add(new{onLine,offLine,onTri,offTri,draws,frames,scope="Two literal draws share one anonymous combined Material. Each draw owns distinct persistent RenderAttributes after material/global merge, with a final per-draw flag. No glTF packet or public route is exercised."});
   Require(onLine>0 && offLine==onLine && onTri>64 && offTri==0,"Draw flag did not isolate unlit line from lit triangle.");Require(Coverage(restored,0,96)==onLine && Coverage(restored,96,192)==onTri,"Light restoration failed.");
   material.Set("g_vGltfBaseColor",new Vector4(1,0,0,1));var red=await Take("shared_material_mutated_red");var lineChanges=Changed(on,red,0,96);var triangleChanges=Changed(on,red,96,192);Measurements.Add(new{lineChanges,triangleChanges});Require(lineChanges>0 && triangleChanges>64,"Direct shared Material mutation did not affect both draws.");
   material.Set("g_vGltfBaseColor",new Vector4(1,1,1,1));Require(material.GetVector4("g_vGltfBaseColor")==new Vector4(1,1,1,1),"Material reset did not persist.");
   var resets=new List<GltfGpuTriangleGateTests.NativeCapture.Frame>();for(int i=0;i<4;i++)resets.Add(await Take("shared_material_reset_"+i));
   var resetDiff=resets.Select(r=>new{line=Changed(on,r,0,96),triangle=Changed(on,r,96,192)}).ToArray();Measurements.Add(new{resetDiff});
   Require(resetDiff[^1].line==0 && resetDiff[^1].triangle==0,"Shared material reset did not converge to original pixels after four captures.");
   sceneObject.RenderOverride=null;sceneObject.Delete();sceneObject=null;using(capture.Scene.Push())capture.Scene.ProcessDeletes();Require((await Take("empty_after_delete")).Coverage==0,"Delete failed.");Measurements.Add(new{passed=true,frames});
  }
  finally{await MainThread.Wait();if(sceneObject.IsValid()){sceneObject.RenderOverride=null;sceneObject.Delete();}material?.Destroy();shader?.Destroy();Measurements.Add(new{frames,output});}
  async Task<GltfGpuTriangleGateTests.NativeCapture.Frame> Take(string label){var f=await capture.Take(label);frames.Add(new{label,f.Path,f.Sha256,f.Coverage,f.CameraStateJson});return f;}
 }
 static int Coverage(GltfGpuTriangleGateTests.NativeCapture.Frame f,int x0,int x1){int count=0;for(int y=0;y<192;y++)for(int x=x0;x<x1;x++){var p=f.Pixels[y*192+x];if(p.r>.03f||p.g>.03f||p.b>.03f)count++;}return count;}
 static int Changed(GltfGpuTriangleGateTests.NativeCapture.Frame a,GltfGpuTriangleGateTests.NativeCapture.Frame b,int x0,int x1){int count=0;for(int y=0;y<192;y++)for(int x=x0;x<x1;x++){var p=a.Pixels[y*192+x];var q=b.Pixels[y*192+x];if(Math.Abs(p.r-q.r)+Math.Abs(p.g-q.g)+Math.Abs(p.b-q.b)>.03f)count++;}return count;}
}
