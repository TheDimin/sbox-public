using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Sandbox.Gltf.DirectGpu.MeshPrimitiveModesValidation.MeshPrimitiveModesReference;
namespace Sandbox.Gltf.DirectGpu.MeshPrimitiveModesValidation;
[TestClass]
internal sealed class PointLineUvTests
{
 static string checkout;public static List<object> Measurements{get;}=new();public static void Configure(string root){checkout=root;Measurements.Clear();}
 static byte[] Fixture()
 {
  var root=Path.Combine(checkout,"Temp/gltf-reference/glTF-Sample-Assets/Models/MeshPrimitiveModes/glTF");var json=JsonNode.Parse(File.ReadAllBytes(Path.Combine(root,"MeshPrimitiveModes.gltf")));var original=File.ReadAllBytes(Path.Combine(root,"buffer.bin"));
  var data=new byte[216+8+7*28];original.CopyTo(data,0);Array.Fill(data,(byte)0xcd,216,data.Length-216);
  // Interleaved UV0=(.25,.5), UV1=(.75,.5) at nonzero view/accessor offsets.
  for(int v=0;v<7;v++)for(int c=0;c<4;c++)BitConverter.GetBytes(new[]{.25f,.5f,.75f,.5f}[c]).CopyTo(data,216+8+v*28+c*4);
  int view=json["bufferViews"].AsArray().Count;json["bufferViews"].AsArray().Add(new JsonObject{["buffer"]=0,["byteOffset"]=216,["byteLength"]=data.Length-216,["byteStride"]=28,["target"]=34962});
  int first=json["accessors"].AsArray().Count;foreach(int offset in new[]{8,16})json["accessors"].AsArray().Add(new JsonObject{["bufferView"]=view,["byteOffset"]=offset,["componentType"]=5126,["count"]=7,["type"]="VEC2"});
  foreach(var m in json["meshes"].AsArray()){m["primitives"][0]["attributes"]["TEXCOORD_0"]=first;m["primitives"][0]["attributes"]["TEXCOORD_1"]=first+1;}
  json["buffers"][0]["uri"]="data:application/octet-stream;base64,"+Convert.ToBase64String(data);json["buffers"][0]["byteLength"]=data.Length;return Encoding.UTF8.GetBytes(json.ToJsonString());
 }
 [TestMethod] [DataRow(false)] [DataRow(true)]
 public async Task CandidateUsesIndependentUvAndSamplerPerTextureRole(bool compiled)
 {
  await MainThread.Wait();var output=Path.Combine(checkout,"Temp/gltf-validation/direct-gpu/MeshPrimitiveModes/uv-candidate",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);
  var bytes=File.ReadAllBytes(Path.Combine(checkout,"engine/Tools/Gltf/direct-gpu/models/MeshPrimitiveModes/point_line_candidate.shader"));Require(Hash(bytes)=="9b3342209cf8a51f520c998bbf62e65852cfc59a87637fc4e2d679c83212a76c","Candidate changed.");
  Shader shader=null;Material material=null;Texture texture=null;GltfGpuAsset asset=null;GltfGpuInstance instance=null;var frames=new List<object>();
  using var capture=new GltfGpuTriangleGateTests.NativeCapture(output,new Vector3(500,0,0),Vector3.Zero,360);
  capture.Camera.AutoExposure.Enabled=false;capture.Camera.AutoExposure.MinimumExposure=1;capture.Camera.AutoExposure.MaximumExposure=1;capture.Camera.AutoExposure.Compensation=0;
  try
  {
   using(var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(90)))shader=await Shader.CompileAsync(Encoding.UTF8.GetString(bytes),timeout.Token);await MainThread.Wait();Require(shader is {IsValid:true,IsAnonymous:true},"Candidate shader failed.");
   material=Material.Create("point_line_uv_"+Guid.NewGuid().ToString("N"),shader);texture=Texture.Create(2,1,ImageFormat.RGBA8888).WithMips(1).WithData(new byte[]{255,0,0,255,0,255,0,255}).WithStaticUsage().Finish();
   var input=Fixture();if(compiled)input=GltfCompiler.Compile(input,null);using var doc=GltfReader.Read(input,null);var layout=doc.DescribePrimitiveUpload(0,0);Require(layout.Attributes.ContainsKey("TEXCOORD_0") && layout.Attributes.ContainsKey("TEXCOORD_1"),"UV accessors lost.");
   asset=GltfGpuAsset.Create(doc,GltfManagedModelResource.ComputeShaderPath,_=>material);instance=asset.CreateInstance(capture.Scene.SceneWorld);
   var clamp=GltfSamplerMapping.Map(new GltfSampler{MinFilter=9728,MagFilter=9728,WrapS=33071,WrapT=33071},9987,9729).State;
   var repeat=GltfSamplerMapping.Map(new GltfSampler{MinFilter=9728,MagFilter=9728,WrapS=10497,WrapT=33071},9987,9729).State;
   material.Attributes.Set("GltfBaseColorSampler",clamp);material.Attributes.Set("GltfEmissiveSampler",clamp);material.Set("g_tGltfBaseColorTexture",texture);material.Set("g_tGltfEmissiveTexture",texture);
   material.Set("g_vGltfBaseColor",new Vector4(1,1,1,1));material.Set("g_vGltfEmissiveFactor",new Vector4(0,0,0,0));material.Set("g_flGltfHasBaseColorTexture",0f);material.Set("g_flGltfHasEmissiveTexture",0f);
   material.Set("g_vGltfBaseColor",new Vector4(1,0,0,1));var red=await Take("literal_red");material.Set("g_vGltfBaseColor",new Vector4(0,1,0,1));var green=await Take("literal_green");Require(MaxDiff(red.Pixels,green.Pixels)>.2f,"Reference colors are not discriminating.");
   material.Set("g_vGltfBaseColor",new Vector4(1,1,1,1));material.Set("g_flGltfHasBaseColorTexture",1f);material.Set("g_flGltfBaseColorUvSlot",0f);var base0=await Take("base_uv0_red");Match(red,base0,"base UV0");
   material.Set("g_flGltfBaseColorUvSlot",1f);var base1=await Take("base_uv1_green");Match(green,base1,"base UV1");
   material.Set("g_vGltfBaseColorUvRowU",new Vector4(1,0,.5f,0));material.Set("g_flGltfBaseColorUvSlot",0f);var transform=await Take("base_transform_green");Match(green,transform,"base transform");
   material.Set("g_vGltfBaseColorUvRowU",new Vector4(1,0,1f,0));material.Attributes.Set("GltfBaseColorSampler",repeat);var wrapped=await Take("base_repeat_red");Match(red,wrapped,"base repeat");
   material.Attributes.Set("GltfBaseColorSampler",clamp);var clamped=await Take("base_clamp_green");Match(green,clamped,"base clamp");
   material.Set("g_flGltfHasBaseColorTexture",0f);material.Set("g_vGltfBaseColor",new Vector4(0,0,0,1));material.Set("g_vGltfEmissiveFactor",new Vector4(1,1,1,0));material.Set("g_flGltfHasEmissiveTexture",1f);
   material.Set("g_flGltfEmissiveUvSlot",0f);var emissive0=await Take("emissive_uv0_red");Match(red,emissive0,"emissive UV0");
   material.Set("g_flGltfEmissiveUvSlot",1f);var emissive1=await Take("emissive_uv1_green");Match(green,emissive1,"emissive UV1");
   material.Set("g_flGltfEmissiveUvSlot",0f);material.Set("g_vGltfEmissiveUvRowU",new Vector4(1,0,1,0));material.Attributes.Set("GltfEmissiveSampler",repeat);var emitRepeat=await Take("emissive_repeat_red");Match(red,emitRepeat,"emissive repeat");
   material.Attributes.Set("GltfEmissiveSampler",clamp);var emitClamp=await Take("emissive_clamp_green");Match(green,emitClamp,"emissive clamp");
   Require(instance.DrawCalls>0 && instance.CallbackErrors.Length==0,"Candidate draw failed.");instance.Dispose();instance=null;Require((await Take("empty_after_delete")).Coverage==0,"Deletion failed.");
   Measurements.Add(new{compiled,passed=true,input_sha256=Hash(input),frames,scope="Synthetic interleaved source UVs and manually bound 2x1 GPU texture/samplers. Both role-specific UV selection, transform and repeat/clamp compared with independent literal colors using same candidate shader; not source material texture loading or public route."});
  }
  finally{await MainThread.Wait();instance?.Dispose();asset?.Dispose();material?.Destroy();texture?.Dispose();shader?.Destroy();Measurements.Add(new{compiled,frames,output});}
  async Task<GltfGpuTriangleGateTests.NativeCapture.Frame> Take(string name){var f=await capture.Take(name);frames.Add(new{name,f.Path,f.Sha256,f.Coverage});return f;}
 }
 static void Match(GltfGpuTriangleGateTests.NativeCapture.Frame expected,GltfGpuTriangleGateTests.NativeCapture.Frame actual,string label){Require(expected.Coverage>0 && MaxDiff(expected.Pixels,actual.Pixels)<=1f/255f,label+" differs from literal color by more than one output byte.");}
 static float MaxDiff(Color[] a,Color[] b){Require(a.Length==b.Length,"Frame sizes differ.");float max=0;for(int i=0;i<a.Length;i++)max=Math.Max(max,Math.Max(Math.Abs(a[i].r-b[i].r),Math.Max(Math.Abs(a[i].g-b[i].g),Math.Abs(a[i].b-b[i].b))));return max;}
}
