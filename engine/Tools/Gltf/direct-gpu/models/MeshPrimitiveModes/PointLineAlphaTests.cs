using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Sandbox.Gltf.DirectGpu.MeshPrimitiveModesValidation.MeshPrimitiveModesReference;
namespace Sandbox.Gltf.DirectGpu.MeshPrimitiveModesValidation;
[TestClass]
internal sealed class PointLineAlphaTests
{
 static string checkout;public static List<object> Measurements{get;}=new();public static void Configure(string root){checkout=root;Measurements.Clear();}
 static byte[] Fixture()
 {
  var dir=Path.Combine(checkout,"Temp/gltf-reference/glTF-Sample-Assets/Models/MeshPrimitiveModes/glTF");var json=JsonNode.Parse(File.ReadAllBytes(Path.Combine(dir,"MeshPrimitiveModes.gltf")));var bin=File.ReadAllBytes(Path.Combine(dir,"buffer.bin"));var data=new byte[216+8+7*28];bin.CopyTo(data,0);Array.Fill(data,(byte)0xcd,216,data.Length-216);
  for(int v=0;v<7;v++)for(int c=0;c<4;c++)BitConverter.GetBytes(c==3?.5f:1f).CopyTo(data,216+8+v*28+c*4);
  int view=json["bufferViews"].AsArray().Count;json["bufferViews"].AsArray().Add(new JsonObject{["buffer"]=0,["byteOffset"]=216,["byteLength"]=data.Length-216,["byteStride"]=28,["target"]=34962});
  int index=json["accessors"].AsArray().Count;json["accessors"].AsArray().Add(new JsonObject{["bufferView"]=view,["byteOffset"]=8,["componentType"]=5126,["count"]=7,["type"]="VEC4"});
  foreach(var m in json["meshes"].AsArray())m["primitives"][0]["attributes"]["COLOR_0"]=index;
  json["buffers"][0]["uri"]="data:application/octet-stream;base64,"+Convert.ToBase64String(data);json["buffers"][0]["byteLength"]=data.Length;return Encoding.UTF8.GetBytes(json.ToJsonString());
 }
 [TestMethod] [DataRow(false)] [DataRow(true)]
 public async Task OpaqueIgnoresAlphaMaskMultipliesVertexAndFactor(bool compiled)
 {
  await MainThread.Wait();var output=Path.Combine(checkout,"Temp/gltf-validation/direct-gpu/MeshPrimitiveModes/alpha-candidate",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);var bytes=File.ReadAllBytes(Path.Combine(checkout,"engine/Tools/Gltf/direct-gpu/models/MeshPrimitiveModes/point_line_candidate.shader"));Require(Hash(bytes)=="9b3342209cf8a51f520c998bbf62e65852cfc59a87637fc4e2d679c83212a76c","Candidate changed.");
  Shader shader=null;Material opaque=null,mask=null;Texture alphaTexture=null;GltfGpuAsset asset=null;GltfGpuInstance instance=null;var frames=new List<object>();using var capture=new GltfGpuTriangleGateTests.NativeCapture(output,new Vector3(500,0,0),Vector3.Zero,360);
  try
  {
   using(var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(90)))shader=await Shader.CompileAsync(Encoding.UTF8.GetString(bytes),timeout.Token);await MainThread.Wait();Require(shader is {IsValid:true,IsAnonymous:true},"Candidate compile failed.");opaque=Material.Create("point_line_opaque_"+Guid.NewGuid().ToString("N"),shader);mask=Material.Create("point_line_mask_"+Guid.NewGuid().ToString("N"),shader);mask.SetFeature("F_ALPHA_TEST",1);Require(mask.Flags.IsAlphaTest && !opaque.Flags.IsAlphaTest,"Native material flags incorrect.");
   var data=Fixture();if(compiled)data=GltfCompiler.Compile(data,null);using var doc=GltfReader.Read(data,null);
   opaque.Set("g_vGltfBaseColor",new Vector4(1,1,1,0));asset=GltfGpuAsset.Create(doc,GltfManagedModelResource.ComputeShaderPath,_=>opaque);instance=asset.CreateInstance(capture.Scene.SceneWorld);var visible=await Take("opaque_zero_factor_alpha");Require(visible.Coverage>0,"Opaque unexpectedly discarded zero alpha.");
   instance.Dispose();instance=null;asset.Dispose();asset=null;Require((await Take("empty_after_opaque")).Coverage==0,"Opaque delete failed.");
   mask.Set("g_vGltfBaseColor",new Vector4(1,1,1,.5f));mask.Set("g_flGltfAlphaCutoff",.5f);asset=GltfGpuAsset.Create(doc,GltfManagedModelResource.ComputeShaderPath,_=>mask);instance=asset.CreateInstance(capture.Scene.SceneWorld);var below=await Take("mask_factor_half_vertex_half_cutoff_half");Require(below.Coverage==0,"MASK ignored factor×vertex alpha.");
   mask.Set("g_flGltfAlphaCutoff",.25f);var equal=await Take("mask_alpha_cutoff_equal");Require(equal.Coverage==visible.Coverage && equal.Coverage>0,"MASK equality should survive.");
   mask.Set("g_flGltfAlphaCutoff",.26f);var above=await Take("mask_cutoff_above_product");Require(above.Coverage==0,"MASK cutoff above product did not discard.");
   mask.Set("g_flGltfAlphaCutoff",.25f);var restored=await Take("mask_cutoff_restored");Require(restored.Coverage==equal.Coverage,"MASK restore failed.");Require(instance.DrawCalls>0 && instance.CallbackErrors.Length==0,"Candidate draw callback failed.");
   alphaTexture=Texture.Create(1,1,ImageFormat.RGBA8888).WithMips(1).WithData(new byte[]{255,255,255,128}).WithStaticUsage().Finish();mask.Set("g_flGltfAlphaCutoff",.2f);mask.Set("g_flGltfHasBaseColorTexture",1f);mask.Set("g_tGltfBaseColorTexture",alphaTexture);
   var texturedBelow=await Take("mask_texture_alpha_cutoff_point_two");Require(texturedBelow.Coverage==0,"MASK ignored base texture alpha in the product.");
   mask.Set("g_flGltfAlphaCutoff",.1f);var texturedAbove=await Take("mask_texture_alpha_cutoff_point_one");Require(texturedAbove.Coverage==visible.Coverage,"MASK texture alpha lower-cutoff control failed.");
   instance.Dispose();instance=null;Require((await Take("empty_after_mask")).Coverage==0,"MASK deletion failed.");Measurements.Add(new{compiled,passed=true,input_sha256=Hash(data),frames,scope="Candidate direct material OPAQUE and MASK permutations, padded COLOR_0 alpha × factor × injected constant texture alpha; equality and discard negative controls. Does not test source material routing or BLEND sorting."});
  }
  finally{await MainThread.Wait();instance?.Dispose();asset?.Dispose();opaque?.Destroy();mask?.Destroy();alphaTexture?.Dispose();shader?.Destroy();Measurements.Add(new{compiled,frames,output});}
  async Task<GltfGpuTriangleGateTests.NativeCapture.Frame> Take(string name){var f=await capture.Take(name);frames.Add(new{name,f.Path,f.Sha256,f.Coverage});return f;}
 }
}
