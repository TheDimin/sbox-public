using System;
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
internal sealed class PointLineCandidateTests
{
 private static string checkout;public static List<object> Measurements{get;}=new();
 public static void Configure(string root){checkout=root;Measurements.Clear();}
 [TestMethod] [DataRow(false)] [DataRow(true)]
 public async Task CandidateLightAndBaseEmissionControls(bool compiled)
 {
  await MainThread.Wait();var output=Path.Combine(checkout,"Temp/gltf-validation/direct-gpu/MeshPrimitiveModes/candidate",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);
  var path=Path.Combine(checkout,"engine/Tools/Gltf/direct-gpu/models/MeshPrimitiveModes/point_line_candidate.shader");var bytes=File.ReadAllBytes(path);
  Require(Hash(bytes)=="9b3342209cf8a51f520c998bbf62e65852cfc59a87637fc4e2d679c83212a76c","Candidate shader changed.");
  Shader shader=null;Material material=null;Texture white=null,black=null;GltfGpuAsset asset=null;GltfGpuInstance instance=null;var frames=new List<object>();
  using var capture=new GltfGpuTriangleGateTests.NativeCapture(output,new Vector3(500,0,0),Vector3.Zero,360);
  capture.Camera.AutoExposure.Enabled=false;capture.Camera.AutoExposure.MinimumExposure=1;capture.Camera.AutoExposure.MaximumExposure=1;capture.Camera.AutoExposure.Compensation=0;
  var light=(ScenePointLight)typeof(GltfGpuTriangleGateTests.NativeCapture).GetField("light",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(capture);
  try
  {
   using(var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(90)))shader=await Shader.CompileAsync(Encoding.UTF8.GetString(bytes),timeout.Token);await MainThread.Wait();
   Require(shader is {IsValid:true,IsAnonymous:true},"Candidate compile failed.");material=Material.Create("point_line_candidate_"+Guid.NewGuid().ToString("N"),shader);Require(material is {IsValid:true,IsError:false},"Candidate material invalid.");
   white=Texture.Create(1,1,ImageFormat.RGBA8888).WithMips(1).WithData(new byte[]{255,255,255,255}).WithStaticUsage().Finish();black=Texture.Create(1,1,ImageFormat.RGBA8888).WithMips(1).WithData(new byte[]{0,0,0,255}).WithStaticUsage().Finish();
   var form=new Inputs(checkout).Get(compiled?"glTF/compiled_glb":"glTF/source");using var doc=form.Read();
   asset=GltfGpuAsset.Create(doc,GltfManagedModelResource.ComputeShaderPath,_=>material);instance=asset.CreateInstance(capture.Scene.SceneWorld);
   material.Set("g_vGltfBaseColor",new Vector4(1,1,1,1));material.Set("g_vGltfEmissiveFactor",new Vector4(0,0,0,0));
   var lit=await Take("base_white_light_on");var lightColor=light.LightColor;light.LightColor=Color.Black;var dark=await Take("base_white_light_off");light.LightColor=lightColor;var restored=await Take("base_white_light_restored");
   Require(instance.DrawCalls>0 && instance.CallbackErrors.Length==0,"Candidate GPU draw failed.");
   Require(lit.Coverage>0 && lit.Pixels.SequenceEqual(dark.Pixels) && lit.Pixels.SequenceEqual(restored.Pixels),"Candidate white changed with direct light.");
   material.Set("g_vGltfBaseColor",new Vector4(0,0,0,1));var zero=await Take("zero_base_and_emissive");Require(zero.Coverage==0,"Zero base/emission control not black.");
   material.Set("g_vGltfEmissiveFactor",new Vector4(1,1,1,0));var emission=await Take("white_emissive_only");Require(emission.Pixels.SequenceEqual(lit.Pixels),"White emissive-only should match white base-only in this opaque fixture.");
   material.Set("g_flGltfHasEmissiveTexture",1f);material.Set("g_tGltfEmissiveTexture",black);var blockedEmission=await Take("black_emissive_texture");Require(blockedEmission.Coverage==0,"Black emissive texture did not suppress emission.");
   material.Set("g_tGltfEmissiveTexture",white);var texturedEmission=await Take("white_emissive_texture");Require(texturedEmission.Pixels.SequenceEqual(emission.Pixels),"White emissive texture changed emission.");
   material.Set("g_flGltfHasEmissiveTexture",0f);material.Set("g_vGltfEmissiveFactor",new Vector4(0,0,0,0));material.Set("g_vGltfBaseColor",new Vector4(1,1,1,1));material.Set("g_flGltfHasBaseColorTexture",1f);material.Set("g_tGltfBaseColorTexture",black);var blockedBase=await Take("black_base_texture");Require(blockedBase.Coverage==0,"Black base texture did not suppress base color.");
   material.Set("g_tGltfBaseColorTexture",white);var texturedBase=await Take("white_base_texture");Require(texturedBase.Pixels.SequenceEqual(lit.Pixels),"White base texture changed base color.");
   instance.Dispose();instance=null;Require((await Take("empty_after_delete")).Coverage==0,"Candidate deletion failed.");
   Measurements.Add(new{compiled,passed=true,frames,scope="Anonymous research candidate on actual source geometry, explicit material resolver. All seven diagnostic primitives use candidate; this does not implement primitive routing or public Model.Load. Black/white texture constants test shader inputs only, not glTF sampler or decoding."});
  }
  finally{await MainThread.Wait();instance?.Dispose();asset?.Dispose();material?.Destroy();white?.Dispose();black?.Dispose();shader?.Destroy();Measurements.Add(new{compiled,frames,output});}
  async Task<GltfGpuTriangleGateTests.NativeCapture.Frame> Take(string name){var f=await capture.Take(name);frames.Add(new{name,f.Path,f.Sha256,f.Coverage,f.CameraStateJson});return f;}
 }
}
