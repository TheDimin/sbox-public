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
internal sealed class PointLineColorTests
{
 static string checkout;public static List<object> Measurements{get;}=new();
 public static void Configure(string root){checkout=root;Measurements.Clear();}
 static byte[] Fixture()
 {
  var root=Path.Combine(checkout,"Temp/gltf-reference/glTF-Sample-Assets/Models/MeshPrimitiveModes/glTF");
  var json=JsonNode.Parse(File.ReadAllBytes(Path.Combine(root,"MeshPrimitiveModes.gltf")));var original=File.ReadAllBytes(Path.Combine(root,"buffer.bin"));
  // COLOR_0 is an independently supplied float4 at offset 8 within a padded
  // 28-byte stride, in a bufferView with a nonzero origin. Positions and all
  // source topology remain upstream data, not fixed-layout GPU input.
  var data=new byte[216+8+7*28];original.CopyTo(data,0);Array.Fill(data,(byte)0xcd,216,data.Length-216);
  for(int vertex=0;vertex<7;vertex++)for(int component=0;component<4;component++)BitConverter.GetBytes(new[]{.5f,1f,.25f,1f}[component]).CopyTo(data,216+8+vertex*28+component*4);
  int view=json["bufferViews"].AsArray().Count;json["bufferViews"].AsArray().Add(new JsonObject{["buffer"]=0,["byteOffset"]=216,["byteLength"]=data.Length-216,["byteStride"]=28,["target"]=34962});
  int accessor=json["accessors"].AsArray().Count;json["accessors"].AsArray().Add(new JsonObject{["bufferView"]=view,["byteOffset"]=8,["componentType"]=5126,["count"]=7,["type"]="VEC4"});
  foreach(var mesh in json["meshes"].AsArray())mesh["primitives"][0]["attributes"]["COLOR_0"]=accessor;
  json["buffers"][0]["uri"]="data:application/octet-stream;base64,"+Convert.ToBase64String(data);json["buffers"][0]["byteLength"]=data.Length;
  return Encoding.UTF8.GetBytes(json.ToJsonString());
 }
 [TestMethod] [DataRow(false)] [DataRow(true)]
 public async Task SourceColorAndEmissionAgainstLiteralProduct(bool compiled)
 {
  await MainThread.Wait();var output=Path.Combine(checkout,"Temp/gltf-validation/direct-gpu/MeshPrimitiveModes/color-candidate",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);
  var shaderBytes=File.ReadAllBytes(Path.Combine(checkout,"engine/Tools/Gltf/direct-gpu/models/MeshPrimitiveModes/point_line_candidate.shader"));Require(Hash(shaderBytes)=="9b3342209cf8a51f520c998bbf62e65852cfc59a87637fc4e2d679c83212a76c","Candidate changed.");
  Shader shader=null;Material actualMaterial=null,referenceMaterial=null;GltfGpuAsset asset=null;GltfGpuInstance instance=null;var frames=new List<object>();
  using var capture=new GltfGpuTriangleGateTests.NativeCapture(output,new Vector3(500,0,0),Vector3.Zero,360);
  capture.Camera.AutoExposure.Enabled=false;capture.Camera.AutoExposure.MinimumExposure=1;capture.Camera.AutoExposure.MaximumExposure=1;capture.Camera.AutoExposure.Compensation=0;
  try
  {
   using(var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(90)))shader=await Shader.CompileAsync(Encoding.UTF8.GetString(shaderBytes),timeout.Token);await MainThread.Wait();Require(shader is {IsValid:true,IsAnonymous:true},"Candidate compile failed.");
   actualMaterial=Material.Create("modes_color_actual_"+Guid.NewGuid().ToString("N"),shader);referenceMaterial=Material.Create("modes_color_literal_"+Guid.NewGuid().ToString("N"),shader);
   // Independent arithmetic: (.5,.5,.5) * (.5,1,.25) + (0,.125,0).
   referenceMaterial.Set("g_vGltfBaseColor",new Vector4(.25f,.625f,.125f,1));referenceMaterial.Set("g_vGltfEmissiveFactor",new Vector4(0,0,0,0));
   actualMaterial.Set("g_vGltfBaseColor",new Vector4(.5f,.5f,.5f,1));actualMaterial.Set("g_vGltfEmissiveFactor",new Vector4(0,.125f,0,0));
   var source=new Inputs(checkout).Get(compiled?"glTF/compiled_glb":"glTF/source");using(var doc=source.Read())
   {
    asset=GltfGpuAsset.Create(doc,GltfManagedModelResource.ComputeShaderPath,_=>referenceMaterial);instance=asset.CreateInstance(capture.Scene.SceneWorld);
    var expected=await Take("literal_product_white_vertex");Require(instance.DrawCalls>0 && expected.Coverage>0,"Literal product did not render.");
    instance.Dispose();instance=null;asset.Dispose();asset=null;Require((await Take("empty_after_reference")).Coverage==0,"Reference deletion failed.");
    var fixture=Fixture();if(compiled)fixture=GltfCompiler.Compile(fixture,null);using var colored=GltfReader.Read(fixture,null);
    var plan=colored.DescribePrimitiveUpload(0,0);Require(plan.Attributes.ContainsKey("COLOR_0"),"Fixture lost COLOR_0.");
    asset=GltfGpuAsset.Create(colored,GltfManagedModelResource.ComputeShaderPath,_=>actualMaterial);instance=asset.CreateInstance(capture.Scene.SceneWorld);
    var actual=await Take("actual_strided_color_plus_emission");Require(instance.DrawCalls>0 && instance.CallbackErrors.Length==0,"Actual candidate did not draw.");
    var error=MaxDiff(expected.Pixels,actual.Pixels);Measurements.Add(new{compiled,expected=expected.Path,actual=actual.Path,max_rgb_error=error,fixture_sha256=Hash(fixture),scope="Metamorphic literal product using same candidate shader and original white COLOR_0; independent CPU arithmetic, not independent shader."});Require(error<=1f/255f,"Strided COLOR_0 and emission differ from literal product by more than one output byte.");
    actualMaterial.Set("g_vGltfEmissiveFactor",new Vector4(0,0,0,0));var noEmission=await Take("wrong_missing_emission");Require(MaxDiff(expected.Pixels,noEmission.Pixels)>.02f,"Missing-emission negative did not change output.");
    actualMaterial.Set("g_vGltfEmissiveFactor",new Vector4(0,.125f,0,0));actualMaterial.Set("g_vGltfBaseColor",new Vector4(0,0,0,1));var noBase=await Take("wrong_missing_base");Require(MaxDiff(expected.Pixels,noBase.Pixels)>.02f,"Missing-base negative did not change output.");
    instance.Dispose();instance=null;Require((await Take("empty_after_candidate")).Coverage==0,"Candidate deletion failed.");
   }
   Measurements.Add(new{compiled,passed=true,frames});
  }
  finally{await MainThread.Wait();instance?.Dispose();asset?.Dispose();actualMaterial?.Destroy();referenceMaterial?.Destroy();shader?.Destroy();Measurements.Add(new{compiled,frames,output});}
  async Task<GltfGpuTriangleGateTests.NativeCapture.Frame> Take(string name){var f=await capture.Take(name);frames.Add(new{name,f.Path,f.Sha256,f.Coverage});return f;}
 }
 static float MaxDiff(Color[] a,Color[] b){Require(a.Length==b.Length,"Frame sizes differ.");float max=0;for(int i=0;i<a.Length;i++)max=Math.Max(max,Math.Max(Math.Abs(a[i].r-b[i].r),Math.Max(Math.Abs(a[i].g-b[i].g),Math.Abs(a[i].b-b[i].b))));return max;}
}
