using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Sandbox.Gltf.DirectGpu.MeshPrimitiveModesValidation.MeshPrimitiveModesReference;
namespace Sandbox.Gltf.DirectGpu.MeshPrimitiveModesValidation;

[TestClass]
internal sealed class MixedSlotCombinedPublicTests
{
 static string checkout; public static List<object> Measurements{get;}=new(); public static void Configure(string root){checkout=root;Measurements.Clear();}
 internal static byte[] Fixture()
 {
  var dir=Path.Combine(checkout,"Temp/gltf-reference/glTF-Sample-Assets/Models/MeshPrimitiveModes/glTF");var json=JsonNode.Parse(File.ReadAllBytes(Path.Combine(dir,"MeshPrimitiveModes.gltf")));var bin=File.ReadAllBytes(Path.Combine(dir,"buffer.bin"));
  var data=new byte[216+7*12];bin.CopyTo(data,0);for(int v=0;v<7;v++)BitConverter.GetBytes(1f).CopyTo(data,216+v*12+8);
  int view=json["bufferViews"].AsArray().Count;json["bufferViews"].AsArray().Add(new JsonObject{["buffer"]=0,["byteOffset"]=216,["byteLength"]=84,["target"]=34962});
  int normal=json["accessors"].AsArray().Count;json["accessors"].AsArray().Add(new JsonObject{["bufferView"]=view,["componentType"]=5126,["count"]=7,["type"]="VEC3"});
  // Original node 1 = no-normal LINES, node 2 = normal-bearing LINE_LOOP,
  // node 4 = no-normal TRIANGLES. All use the exact same source material slot.
  json["meshes"][2]["primitives"][0]["attributes"]["NORMAL"]=normal;
  foreach(int i in new[]{1,2,4}){var primitive=json["meshes"][i]["primitives"][0];primitive["material"]=0;primitive["extensions"]=JsonNode.Parse("{\"KHR_materials_variants\":{\"mappings\":[{\"material\":1,\"variants\":[0]}]}}");}
  json["scenes"][0]["nodes"]=new JsonArray(1,2,4);
  json["materials"]=JsonNode.Parse("[{\"pbrMetallicRoughness\":{\"baseColorFactor\":[0.6,0.8,0.4,1],\"metallicFactor\":0,\"roughnessFactor\":1}},{\"pbrMetallicRoughness\":{\"baseColorFactor\":[0.2,0.4,0.9,1],\"metallicFactor\":0,\"roughnessFactor\":1}}]");
  json["extensionsUsed"]=new JsonArray("KHR_materials_variants");json["extensions"]=JsonNode.Parse("{\"KHR_materials_variants\":{\"variants\":[{\"name\":\"alternate\"}]}}");
  json["buffers"][0]["uri"]="data:application/octet-stream;base64,"+Convert.ToBase64String(data);json["buffers"][0]["byteLength"]=data.Length;return Encoding.UTF8.GetBytes(json.ToJsonString());
 }
 [TestMethod] [DataRow(false)] [DataRow(true)]
 public async Task SharedSlotMixedProgramVariantAndOverrides(bool compiled)
 {
  await MainThread.Wait();var bytes=Fixture();if(compiled)bytes=GltfCompiler.Compile(bytes,null,new HashSet<string>{"KHR_materials_variants"});
  var directory=Path.Combine(Path.GetTempPath(),"gltf_mixed_slot_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);var name="mixed_"+Guid.NewGuid().ToString("N")+(compiled?".glb_c":".gltf");File.WriteAllBytes(Path.Combine(directory,name),bytes);
  var output=Path.Combine(checkout,"Temp/gltf-validation/direct-gpu/MeshPrimitiveModes/mixed-slot",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);var frames=new List<object>();LocalFileSystem mount=null;Model model=null;ModelRenderer first=null,second=null;Material whole=null,slot=null;
  using var capture=new GltfGpuTriangleGateTests.NativeCapture(output,new Vector3(500,0,0),Vector3.Zero,360);
  try
  {
   mount=new LocalFileSystem(directory);FileSystem.Mounted.Mount(mount);model=Model.Load(name);Require(model is {IsValid:true,IsError:false} && model.native.IsNull && model.GltfResource is not null,"Public managed Model.Load failed.");
   Require(model.Materials.Length==1 && model.GltfResource.MaterialSlotSourceIndices.SequenceEqual(new int?[]{0}) && model.MaterialVariantNames.SequenceEqual(new[]{"alternate"}),"Source slot identity/order or variant catalog changed.");
   Require((await Take("empty_before")).Coverage==0,"Empty scene not empty.");
   using(capture.Scene.Push()){first=capture.Scene.CreateObject().Components.Create<ModelRenderer>(false);first.Model=model;first.Enabled=true;capture.Scene.EditorTick(0,0);}
   var a=first.GltfInstance;Require(a is not null && first.SceneObject is SceneCustomObject && first.SceneObject.Model is null,"Missing public custom renderer.");
   var original=await Take("default");var rows=a.Readback();Require(rows.Count==3 && rows.All(x=>x.DrawCalls>0),"Expected three submitted primitives.");
   var noNormal=rows.Single(x=>x.Node==1);var normal=rows.Single(x=>x.Node==2);var triangle=rows.Single(x=>x.Node==4);
   Require(noNormal.SubmittedMaterial.ShaderName.Contains("gltf_stock_sampler",StringComparison.OrdinalIgnoreCase),"Legacy source slot did not preserve stock shader.");
   Require(ReferenceEquals(noNormal.SubmittedMaterial,normal.SubmittedMaterial) && ReferenceEquals(noNormal.SubmittedMaterial,triangle.SubmittedMaterial),"Shared legacy slot must retain one actual mutable material for all primitives.");
   Require(ReferenceEquals(first.Materials.GetOriginal(0),model.Materials[0]),"Original accessor and model slot identity differ.");
   var light=(ScenePointLight)typeof(GltfGpuTriangleGateTests.NativeCapture).GetField("light",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(capture);var lightColor=light.LightColor;light.LightColor=Color.Black;var lightOff=await Take("mixed_light_off");light.LightColor=lightColor;await Take("mixed_light_restored");
   int Coverage(GltfGpuTriangleGateTests.NativeCapture.Frame f,int x0,int y0,int x1,int y1){int n=0;for(int y=y0;y<y1;y++)for(int x=x0;x<x1;x++){var p=f.Pixels[y*192+x];if(p.r>.03f||p.g>.03f||p.b>.03f)n++;}return n;}
   var onLine=Coverage(original,33,73,75,119);var offLine=Coverage(lightOff,33,73,75,119);var onNormal=Coverage(original,75,73,117,119);var offNormal=Coverage(lightOff,75,73,117,119);var onTriangle=Coverage(original,33,135,75,182);var offTriangle=Coverage(lightOff,33,135,75,182);
   Measurements.Add(new{compiled,onLine,offLine,onNormal,offNormal,onTriangle,offTriangle});Require(onLine>0 && offLine==onLine && onNormal>0 && offNormal==0 && onTriangle>0 && offTriangle==0,"Missing-normal line must stay lit independently; normal-bearing line and triangle must respond to lighting.");
   var exposed=model.Materials[0];var old=exposed.GetVector4("g_vColorTint");var ownsLine=ReferenceEquals(exposed,noNormal.SubmittedMaterial);var ownsTriangle=ReferenceEquals(exposed,triangle.SubmittedMaterial);
   Require(ownsLine && ownsTriangle && exposed.HasParameter("g_vColorTint"),"Shared actual material handle missing.");
   exposed.Set("g_vColorTint",new Vector4(.1f,.1f,.1f,1));var observed=triangle.SubmittedMaterial.GetVector4("g_vColorTint");Require(observed.x==.1f && observed.y==.1f && observed.z==.1f,"Direct shared material mutation did not reach triangle material.");var mutation=await Take("direct_material_mutation");var afterMutation=a.Readback();
   int Changed(GltfGpuTriangleGateTests.NativeCapture.Frame left,GltfGpuTriangleGateTests.NativeCapture.Frame right,int x0,int y0,int x1,int y1){int n=0;for(int y=y0;y<y1;y++)for(int x=x0;x<x1;x++){var p=left.Pixels[y*192+x];var q=right.Pixels[y*192+x];if(Math.Abs(p.r-q.r)+Math.Abs(p.g-q.g)+Math.Abs(p.b-q.b)>.01f)n++;}return n;}
   var directChanges=new[]{Changed(original,mutation,33,73,75,119),Changed(original,mutation,75,73,117,119),Changed(original,mutation,33,135,75,182)};Require(directChanges.All(x=>x>0),"Direct Material.Set failed to affect every shared-slot primitive.");Measurements.Add(new{compiled,directChanges});
   Measurements.Add(new{compiled,exposed_owns_line=ownsLine,exposed_owns_triangle=ownsTriangle,source_slot_count=model.Materials.Length,first_mutated_parameter=exposed.GetVector4("g_vColorTint").ToString(),line_material=afterMutation.Single(x=>x.Node==1).SubmittedMaterial.ShaderName,triangle_material=afterMutation.Single(x=>x.Node==4).SubmittedMaterial.ShaderName,original=original.Path,mutated=mutation.Path,scope="Direct mutation shares the same native material across all three primitives; pixel-level response must be validated independently."});
   exposed.Set("g_vColorTint",old);await Take("direct_material_restored");
   using(capture.Scene.Push()){second=capture.Scene.CreateObject().Components.Create<ModelRenderer>(false);second.WorldPosition=new Vector3(0,0,10);second.Model=model;second.Enabled=true;capture.Scene.EditorTick(0,0);}
   var b=second.GltfInstance;Require(b is not null && b.ActiveVariant is null,"Second renderer not independent.");
   first.MaterialVariant=0;var variant=await Take("alternate_variant");Require(a.ActiveVariant==0 && b.ActiveVariant is null,"Variant leaked to second instance.");var variantRows=a.Readback();Require(variantRows.All(x=>x.SubmittedMaterialIndex==1 && !x.SubmittedMaterialOverride),"Variant did not reach all three source-slot packets.");
   slot=Material.Load("materials/default/white.vmat").CreateCopy("mixed_slot_"+Guid.NewGuid().ToString("N"));whole=Material.Load("materials/default/white.vmat").CreateCopy("mixed_whole_"+Guid.NewGuid().ToString("N"));
   a.SetMaterialOverride(0,slot);await Take("direct_instance_slot_override");Require(a.Readback().All(x=>x.SubmittedMaterialOverride && ReferenceEquals(x.SubmittedMaterial,slot)),"Instance slot override missed a shared-slot packet.");a.ClearMaterialOverrides();await Take("direct_instance_override_clear");Require(a.Readback().All(x=>!x.SubmittedMaterialOverride && x.SubmittedMaterialIndex==1),"Instance override clear did not restore selected variant.");
   first.Materials.SetOverride(0,slot);await Take("slot_override");Require(a.Readback().All(x=>x.SubmittedMaterialOverride && ReferenceEquals(x.SubmittedMaterial,slot)),"Indexed override did not apply to every packet sharing slot 0.");Require(b.Readback().All(x=>!x.SubmittedMaterialOverride),"Override leaked to second instance.");
   first.MaterialOverride=whole;await Take("whole_override");Require(a.Readback().All(x=>x.SubmittedMaterialOverride && ReferenceEquals(x.SubmittedMaterial,whole)),"Whole override did not take precedence.");
   first.MaterialOverride=null;await Take("slot_after_whole_clear");Require(a.Readback().All(x=>x.SubmittedMaterialOverride && ReferenceEquals(x.SubmittedMaterial,slot)),"Clearing whole override lost indexed override.");
   first.Materials.SetOverride(0,null);await Take("variant_after_slot_clear");Require(a.Readback().All(x=>!x.SubmittedMaterialOverride && x.SubmittedMaterialIndex==1),"Clearing slot override did not restore variant.");
   first.MaterialVariant=null;await Take("default_restored");Require(a.Readback().All(x=>!x.SubmittedMaterialOverride && x.SubmittedMaterialIndex==0),"Default source material did not restore.");
   Measurements.Add(new{compiled,passed=true,input_sha256=Hash(bytes),original=original.Path,variant=variant.Path,frames,scope="Public mixed source-slot three-program routing, variant and whole/indexed overrides, second renderer independence; direct mutable Material handle is measured separately."});
   Destroy(ref first);Destroy(ref second);Require((await Take("empty_after_delete")).Coverage==0,"Renderer deletion failed.");
  }
  finally{await MainThread.Wait();Destroy(ref first);Destroy(ref second);if(model is not null && model.ResourcePath==Resource.FixPath(name))model.Destroy();whole?.Destroy();slot?.Destroy();if(mount is not null){FileSystem.Mounted.UnMount(mount);mount.Dispose();}Directory.Delete(directory,true);Measurements.Add(new{compiled,frames,output});}
  async Task<GltfGpuTriangleGateTests.NativeCapture.Frame> Take(string label){var f=await capture.Take(label);frames.Add(new{label,f.Path,f.Sha256,f.Coverage});return f;}
  void Destroy(ref ModelRenderer r){if(r is null)return;using var scope=capture.Scene.Push();r.GameObject.Destroy();capture.Scene.EditorTick(0,0);capture.Scene.ProcessDeletes();r=null;}
 }
}
