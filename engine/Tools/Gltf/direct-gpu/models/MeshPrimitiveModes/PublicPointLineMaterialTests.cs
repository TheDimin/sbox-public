using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Sandbox.Gltf.DirectGpu.MeshPrimitiveModesValidation.MeshPrimitiveModesReference;
namespace Sandbox.Gltf.DirectGpu.MeshPrimitiveModesValidation;
[TestClass]
internal sealed class PublicPointLineMaterialTests
{
 static string checkout;public static List<object> Measurements{get;}=new();public static void Configure(string root){checkout=root;Measurements.Clear();}
 [TestMethod] [DataRow(false)] [DataRow(true)]
 public async Task PublicSourceMaterialNoNormalModesAreLightIndependent(bool compiled)
 {
  await MainThread.Wait();var dir=Path.Combine(checkout,"Temp/gltf-reference/glTF-Sample-Assets/Models/MeshPrimitiveModes/glTF");var source=File.ReadAllBytes(Path.Combine(dir,"MeshPrimitiveModes.gltf"));var bin=File.ReadAllBytes(Path.Combine(dir,"buffer.bin"));Require(Hash(source)=="2a8b3209c25e2fca5105539136a05f42d170767f0eb16148cd3825758e5ee977"&&Hash(bin)=="4369522e5929bed650242fea069c687aa2ea1230caa61c378610b2154c1048d7","Pinned source changed.");
  byte[] Resolve(string uri){Require(uri=="buffer.bin","Unexpected dependency.");return bin;}var bytes=compiled?GltfCompiler.Compile(source,Resolve):source;
  var temp=Path.Combine(Path.GetTempPath(),"gltf_modes_material_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);var name="modes_"+Guid.NewGuid().ToString("N")+(compiled?".glb_c":".gltf");File.WriteAllBytes(Path.Combine(temp,name),bytes);if(!compiled)File.WriteAllBytes(Path.Combine(temp,"buffer.bin"),bin);
  var output=Path.Combine(checkout,"Temp/gltf-validation/direct-gpu/MeshPrimitiveModes/public-material",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);LocalFileSystem mount=null;Model model=null;ModelRenderer renderer=null;var frames=new List<object>();using var capture=new GltfGpuTriangleGateTests.NativeCapture(output,new Vector3(500,0,0),Vector3.Zero,360);
  capture.Camera.AutoExposure.Enabled=false;capture.Camera.AutoExposure.MinimumExposure=1;capture.Camera.AutoExposure.MaximumExposure=1;capture.Camera.AutoExposure.Compensation=0;
  try
  {
   mount=new LocalFileSystem(temp);FileSystem.Mounted.Mount(mount);Require((await Take("empty_before")).Coverage==0,"Empty scene not empty.");model=Model.Load(name);Require(model is {IsValid:true,IsError:false} && model.native.IsNull && model.GltfResource is not null,"Public model load failed.");Require(model.Materials.Length==1 && model.GltfResource.MaterialSlotSourceIndices.SequenceEqual(new int?[]{null}) && model.Materials[0].ShaderName.Contains("gltf_stock_sampler",StringComparison.OrdinalIgnoreCase),"Implicit source material slot and stock shader changed.");
   using(capture.Scene.Push()){renderer=capture.Scene.CreateObject().Components.Create<ModelRenderer>(false);renderer.Model=model;renderer.Enabled=true;capture.Scene.EditorTick(0,0);}var instance=renderer.GltfInstance;Require(instance is not null && renderer.SceneObject is SceneCustomObject && renderer.SceneObject.Model is null,"Public buffer-backed renderer missing.");
   var light=(ScenePointLight)typeof(GltfGpuTriangleGateTests.NativeCapture).GetField("light",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(capture);var previous=light.LightColor;var lit=await Take("public_material_lit");light.LightColor=Color.Black;var dark=await Take("public_material_light_off");light.LightColor=previous;var restored=await Take("public_material_light_restored");
   var regions=Regions(lit.Pixels,dark.Pixels);var restore=Regions(lit.Pixels,restored.Pixels);Measurements.Add(new{compiled,input_sha256=Hash(bytes),regions,restore,frames});
   Require(instance.DrawCalls>0 && instance.CallbackErrors.Length==0,"Public draw failed.");Require(regions.Take(4).All(x=>x.lit>0 && x.dark==x.lit && x.different==0 && x.rgbChanged==0),"Point/line source pixels depend on light.");Require(regions.Skip(4).All(x=>x.lit>0 && x.dark==0),"Triangle lighting control did not respond.");Require(restore.All(x=>x.different==0),"Light restoration changed coverage.");
   using(capture.Scene.Push()){renderer.GameObject.Destroy();capture.Scene.EditorTick(0,0);capture.Scene.ProcessDeletes();}renderer=null;Require((await Take("empty_after_delete")).Coverage==0,"Public renderer delete failed.");Measurements.Add(new{compiled,passed=true,frames,scope="Actual public Model.Load/ModelRenderer default material, light on/off/on for each authored mode; no geometry/index suite replay. User visual review and other materials remain separate."});
  }
  finally{await MainThread.Wait();if(renderer is not null){using var s=capture.Scene.Push();renderer.GameObject.Destroy();capture.Scene.EditorTick(0,0);capture.Scene.ProcessDeletes();}if(model is not null&&model.ResourcePath==Resource.FixPath(name))model.Destroy();if(mount is not null){FileSystem.Mounted.UnMount(mount);mount.Dispose();}Directory.Delete(temp,true);Measurements.Add(new{compiled,frames,output});}
  async Task<GltfGpuTriangleGateTests.NativeCapture.Frame> Take(string label){var f=await capture.Take(label);frames.Add(new{label,f.Path,f.Sha256,f.Coverage,f.CameraStateJson});return f;}
 }
 private sealed record Region(int mode,int lit,int dark,int different,int rgbChanged);
 static Region[] Regions(Color[] a,Color[] b)
 {
  const double scale=192.0/(360*.0254);return Translations.Select((t,mode)=>{double cx=96+t.X*scale,cy=96-t.Y*scale;int x0=(int)Math.Floor(cx-.866*scale-2),x1=(int)Math.Ceiling(cx+.866*scale+2),y0=(int)Math.Floor(cy-scale-2),y1=(int)Math.Ceiling(cy+scale+2);int l=0,d=0,changed=0,rgb=0;for(int y=y0;y<y1;y++)for(int x=x0;x<x1;x++){var p=a[y*192+x];var q=b[y*192+x];bool aa=p.r>.03f||p.g>.03f||p.b>.03f,bb=q.r>.03f||q.g>.03f||q.b>.03f;if(aa)l++;if(bb)d++;if(aa!=bb)changed++;if(Math.Abs(p.r-q.r)>1f/255f || Math.Abs(p.g-q.g)>1f/255f || Math.Abs(p.b-q.b)>1f/255f)rgb++;}return new Region(mode,l,d,changed,rgb);}).ToArray();
 }
}
