using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Sandbox.Gltf.DirectGpu.MeshPrimitiveModesValidation.MeshPrimitiveModesReference;
namespace Sandbox.Gltf.DirectGpu.MeshPrimitiveModesValidation;
[TestClass]
internal sealed class SharedSlotMutationBaseline
{
 static string checkout;public static List<object> Measurements{get;}=new();public static void Configure(string root){checkout=root;MixedSlotPublicTests.Configure(root);Measurements.Clear();}
 [TestMethod]
 public async Task PublicSharedSourceSlotDirectMutationBeforeNewRoute()
 {
  await MainThread.Wait();var bytes=MixedSlotPublicTests.Fixture();var dir=Path.Combine(Path.GetTempPath(),"modes_mutation_baseline_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);var name="baseline_"+Guid.NewGuid().ToString("N")+".gltf";File.WriteAllBytes(Path.Combine(dir,name),bytes);
  var output=Path.Combine(checkout,"Temp/gltf-validation/direct-gpu/MeshPrimitiveModes/mutation-baseline",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);LocalFileSystem mount=null;Model model=null;ModelRenderer renderer=null;using var capture=new GltfGpuTriangleGateTests.NativeCapture(output,new Vector3(500,0,0),Vector3.Zero,360);
  try
  {
   mount=new LocalFileSystem(dir);FileSystem.Mounted.Mount(mount);model=Model.Load(name);Require(model is {IsValid:true,IsError:false} && model.native.IsNull,"Public model load failed.");Require(model.Materials.Length==1 && model.GltfResource.MaterialSlotSourceIndices.SequenceEqual(new int?[]{0}),"Shared slot identity differs.");
   using(capture.Scene.Push()){renderer=capture.Scene.CreateObject().Components.Create<ModelRenderer>(false);renderer.Model=model;renderer.Enabled=true;capture.Scene.EditorTick(0,0);}
   var instance=renderer.GltfInstance;var before=await capture.Take("baseline_before");var rows=instance.Readback();var shared=model.Materials[0];Require(rows.Count==3 && rows.All(x=>ReferenceEquals(shared,x.SubmittedMaterial)),"Baseline source slot is not one native material realization.");
   Require(shared.HasParameter("g_vColorTint"),"Baseline stock shader missing tint parameter.");var original=shared.GetVector4("g_vColorTint");
   try{shared.Set("g_vColorTint",new Vector4(.1f,.1f,.1f,1));var after=await capture.Take("baseline_mutated");var changed=Regions(before.Pixels,after.Pixels);Measurements.Add(new{changed,rows=rows.Select(x=>new{x.Node,x.SubmittedMaterial.ShaderName}).ToArray(),shared=true,before.Path,after=after.Path,scope="Existing public projected route; one source slot shared by three primitives, direct mutation of first returned native Material."});Require(changed.All(x=>x>0),"Direct shared-slot mutation did not visibly affect all primitive regions.");}
   finally{shared.Set("g_vColorTint",original);}
  }
  finally{await MainThread.Wait();if(renderer is not null){using var s=capture.Scene.Push();renderer.GameObject.Destroy();capture.Scene.EditorTick(0,0);capture.Scene.ProcessDeletes();}if(model is not null && model.ResourcePath==Resource.FixPath(name))model.Destroy();if(mount is not null){FileSystem.Mounted.UnMount(mount);mount.Dispose();}Directory.Delete(dir,true);}
 }
 static int[] Regions(Color[] a,Color[] b)
 {
  var rectangles=new[]{(33,73,75,119),(75,73,117,119),(33,135,75,182)};return rectangles.Select(q=>{int changed=0;for(int y=q.Item2;y<q.Item4;y++)for(int x=q.Item1;x<q.Item3;x++){var p=a[y*192+x];var r=b[y*192+x];if(Math.Abs(p.r-r.r)+Math.Abs(p.g-r.g)+Math.Abs(p.b-r.b)>.01f)changed++;}return changed;}).ToArray();
 }
}
