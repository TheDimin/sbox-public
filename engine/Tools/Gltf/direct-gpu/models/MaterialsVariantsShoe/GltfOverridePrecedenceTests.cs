using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
namespace Sandbox.Gltf.DirectGpu.MaterialsVariantsShoeValidation;
[TestClass]
public sealed class GltfOverridePrecedenceTests
{
 static string root;
 public static List<object> Measurements {get;}=new();
 public static void Configure(string checkout){root=checkout;Measurements.Clear();}
 [TestMethod]
 public async Task WholeSlotVariantAndLifecycleMatchNativePrecedence()
 {
  await MainThread.Wait(); var dir=Path.Combine(Path.GetTempPath(),"gltf_override_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
  var name="override_"+Guid.NewGuid().ToString("N")+".gltf";File.WriteAllBytes(Path.Combine(dir,name),Encoding.UTF8.GetBytes(Source));
  var output=Path.Combine(root,"Temp/gltf-validation/direct-gpu/MaterialsVariantsShoe/override",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);
  LocalFileSystem mount=null;Model model=null;ModelRenderer renderer=null;Material slot=null;
  using var capture=new GltfGpuTriangleGateTests.NativeCapture(output,new Vector3(140,80,60),Vector3.Zero,90);
  try
  {
   mount=new LocalFileSystem(dir);FileSystem.Mounted.Mount(mount);model=Model.Load(name);Assert.IsTrue(model.IsValid&&!model.IsError);
   slot=Material.Load("materials/default/white.vmat").CreateCopy("gltf_slot_"+Guid.NewGuid().ToString("N"));slot.Set("g_vColorTint",new Color(.02f,.85f,.85f));var whole=capture.Material;whole.Set("g_vColorTint",new Color(.85f,.02f,.02f));
   Assert.AreEqual(0,(await capture.Take("empty_before")).Coverage);
   using(capture.Scene.Push()){var go=capture.Scene.CreateObject();var bounds=model.Bounds;var size=bounds.Size;float extent=MathF.Max(size.x,MathF.Max(size.y,size.z));Assert.IsTrue(extent>0);float scale=32/extent;go.WorldScale=Vector3.One*scale;go.WorldPosition=-bounds.Center*scale;renderer=go.Components.Create<ModelRenderer>(false);renderer.Model=model;renderer.Enabled=true;capture.Scene.EditorTick(0,0);Assert.AreEqual(2,renderer.Materials.Count);}
   async Task<GltfGpuTriangleGateTests.NativeCapture.Frame> Take(string label){using(capture.Scene.Push())capture.Scene.EditorTick(0,0);var f=await capture.Take(label);Assert.IsTrue(f.Coverage>64);return f;}
   int Difference(GltfGpuTriangleGateTests.NativeCapture.Frame a,GltfGpuTriangleGateTests.NativeCapture.Frame b)=>a.Pixels.Zip(b.Pixels,(x,y)=>Math.Abs(x.r-y.r)+Math.Abs(x.g-y.g)+Math.Abs(x.b-y.b)>.06f).Count(x=>x);
   void Same(GltfGpuTriangleGateTests.NativeCapture.Frame a,GltfGpuTriangleGateTests.NativeCapture.Frame b,string label)=>Assert.IsTrue(Difference(a,b)<64,label+" must match effective material control");
   var defaultFrame=await Take("default");renderer.MaterialVariant=0;var variantA=await Take("variant_a");renderer.MaterialVariant=1;var variantB=await Take("variant_b");Assert.IsTrue(Difference(variantA,variantB)>64 && Difference(defaultFrame,variantA)>64);
   renderer.MaterialOverride=whole;var wholeRef=await Take("whole_ref");renderer.MaterialOverride=null;renderer.Materials.SetOverride(0,slot);var slotRef=await Take("slot_ref");Assert.IsTrue(Difference(wholeRef,slotRef)>64);
   renderer.MaterialOverride=whole;Same(wholeRef,await Take("slot_then_whole"),"whole wins");renderer.MaterialVariant=0;Same(wholeRef,await Take("variant_during_whole"),"variant cannot supersede whole");
   renderer.MaterialOverride=null;Same(slotRef,await Take("clear_whole"),"clear whole reveals slot");renderer.Materials.SetOverride(0,null);Same(variantA,await Take("clear_slot"),"clear slot reveals selected variant");
   renderer.MaterialOverride=whole;renderer.Materials.SetOverride(0,slot);Same(wholeRef,await Take("whole_then_slot"),"whole wins later slot");renderer.Materials.SetOverride(0,null);Same(wholeRef,await Take("both_clear_slot"),"clear slot preserves whole");
   renderer.Materials.SetOverride(0,slot);renderer.ClearMaterialOverrides();Same(slotRef,await Take("clear_all"),"clear all reveals saved slot as native");
   renderer.Enabled=false;capture.Scene.EditorTick(0,0);renderer.Enabled=true;Same(slotRef,await Take("reenabled"),"saved slot survives reenable");renderer.Materials.SetOverride(0,null);Same(variantA,await Take("variant_restored"),"selected variant restored");renderer.MaterialVariant=null;Same(defaultFrame,await Take("default_restored"),"default restored");
   // A failed replacement must preserve both effective pixels and saved state, including after recreation.
   renderer.MaterialVariant=0;renderer.Materials.SetOverride(0,slot);var beforeFailure=await Take("before_invalid_override");
   var invalid=Material.Load("materials/default/white.vmat").CreateCopy("gltf_invalid_"+Guid.NewGuid().ToString("N"));invalid.Destroy();
   Assert.ThrowsException<Sandbox.Gltf.GltfException>(()=>renderer.MaterialOverride=invalid);Assert.IsNull(renderer.MaterialOverride);Assert.AreSame(slot,renderer.Materials.GetOverride(0));Same(beforeFailure,await Take("invalid_whole_rollback"),"invalid whole rollback");
   Assert.ThrowsException<Sandbox.Gltf.GltfException>(()=>renderer.Materials.SetOverride(0,invalid));Assert.AreSame(slot,renderer.Materials.GetOverride(0));Same(beforeFailure,await Take("invalid_slot_rollback"),"invalid slot rollback");
   renderer.Enabled=false;capture.Scene.EditorTick(0,0);renderer.Enabled=true;Same(beforeFailure,await Take("invalid_rollback_reenabled"),"rollback persists after reenable");
   renderer.MaterialOverride=whole;
   var staleSlot=Material.Load("materials/default/white.vmat").CreateCopy("gltf_stale_"+Guid.NewGuid().ToString("N"));renderer.Materials.SetOverride(0,staleSlot);staleSlot.Destroy();
   Assert.ThrowsException<Sandbox.Gltf.GltfException>(()=>renderer.ClearMaterialOverrides());Assert.AreSame(whole,renderer.MaterialOverride);Assert.AreSame(staleSlot,renderer.Materials.GetOverride(0));Same(wholeRef,await Take("failed_clear_rollback"),"failed clear preserves whole");
   renderer.Materials.SetOverride(0,slot);renderer.MaterialOverride=null;Same(slotRef,await Take("slot_after_failed_clear"),"recovery after failed clear");
   // A distinct implicit-default slot must survive mixed null/non-null vectors.
   renderer.Materials.SetOverride(0,null);renderer.MaterialVariant=null;renderer.Materials.SetOverride(1,slot);var secondSlotOnly=await Take("second_slot_only");Assert.IsTrue(Difference(defaultFrame,secondSlotOnly)>64);
   renderer.MaterialOverride=whole;Same(wholeRef,await Take("whole_over_two_slots"),"whole covers both slots");renderer.MaterialVariant=1;Same(wholeRef,await Take("variant_while_whole_two_slots"),"whole masks both slots");
   renderer.MaterialOverride=null;var secondVariant=await Take("variant_and_second_slot");Assert.IsTrue(Difference(secondSlotOnly,secondVariant)>64,"variant changes mapped primitive while second slot remains overridden");
   renderer.MaterialVariant=null;Same(secondSlotOnly,await Take("second_slot_after_variant_reset"),"unmapped slot retained");renderer.Materials.SetOverride(1,null);Same(defaultFrame,await Take("two_slots_cleared"),"both original slots restored");
   var instance=renderer.GltfInstance;Assert.AreEqual(0,instance.CallbackErrors.Length);Assert.IsTrue(instance.DrawCalls>0);
   Measurements.Add(new{output,scope="Actual public glTF fixture and rendered native-camera pixels; whole/slot/variant ordered operations and component lifecycle. Two slots including an implicit default; no group/tag inference."});
   using(capture.Scene.Push()){renderer.GameObject.Destroy();capture.Scene.EditorTick(0,0);capture.Scene.ProcessDeletes();}renderer=null;Assert.AreEqual(0,(await capture.Take("empty_after")).Coverage);
  }
  finally {await MainThread.Wait();if(renderer!=null){using var scope=capture.Scene.Push();renderer.GameObject.Destroy();capture.Scene.EditorTick(0,0);capture.Scene.ProcessDeletes();}model?.Destroy();slot?.Destroy();if(mount!=null){FileSystem.Mounted.UnMount(mount);mount.Dispose();}Directory.Delete(dir,true);}
 }
 const string Source="""
 {"asset":{"version":"2.0"},"extensionsUsed":["KHR_materials_variants","KHR_materials_unlit"],"extensionsRequired":["KHR_materials_variants"],"extensions":{"KHR_materials_variants":{"variants":[{"name":"green"},{"name":"blue"}]}},"buffers":[{"byteLength":36,"uri":"data:application/octet-stream;base64,AAAAAAAAAAAAAAAAAACAPwAAAAAAAAAAAAAAAAAAgD8AAAAA"}],"bufferViews":[{"buffer":0,"byteLength":36}],"accessors":[{"bufferView":0,"componentType":5126,"count":3,"type":"VEC3","min":[0,0,0],"max":[1,1,0]}],"materials":[{"extensions":{"KHR_materials_unlit":{}},"pbrMetallicRoughness":{"baseColorFactor":[1,1,1,1]}},{"extensions":{"KHR_materials_unlit":{}},"pbrMetallicRoughness":{"baseColorFactor":[0,1,0,1]}},{"extensions":{"KHR_materials_unlit":{}},"pbrMetallicRoughness":{"baseColorFactor":[0,0,1,1]}}],"meshes":[{"primitives":[{"attributes":{"POSITION":0},"material":0,"extensions":{"KHR_materials_variants":{"mappings":[{"material":1,"variants":[0]},{"material":2,"variants":[1]}]}}}]},{"primitives":[{"attributes":{"POSITION":0}}]}],"nodes":[{"mesh":0},{"mesh":1,"translation":[1.2,0,0]}],"scenes":[{"nodes":[0,1]}],"scene":0}
 """;
}
