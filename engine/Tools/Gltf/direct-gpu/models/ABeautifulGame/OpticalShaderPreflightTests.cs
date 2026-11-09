using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox;
using Assert=Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace Sandbox.Gltf.DirectGpu.ABeautifulGameValidation;
[TestClass]
internal sealed class OpticalShaderPreflightTests
{
 static string root;public static List<object> Measurements {get;}=new();public static void Configure(string checkout){root=checkout;Measurements.Clear();}
 [TestMethod] public async Task AnonymousShaderNativeCompilesAndExposesOpticalInputs()
 {
  await MainThread.Wait();string path=Path.Combine(root,"engine/Tools/Gltf/direct-gpu/models/ABeautifulGame/gltf_optical_candidate.shader");byte[] source=File.ReadAllBytes(path);Assert.AreEqual("f06941210fe895dcdbffb6c95bb34a4b14bb0d3d956c55bfec59ec326c4f9325",Convert.ToHexStringLower(SHA256.HashData(source)));Shader shader=null;Material material=null;
  try
  {
   using(var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(90)))shader=await Shader.CompileAsync(Encoding.UTF8.GetString(source),timeout.Token);
   await MainThread.Wait();Assert.IsTrue(shader is {IsValid:true,IsAnonymous:true});material=Material.Create("gltf_optical_preflight_"+Guid.NewGuid().ToString("N"),shader);Assert.IsTrue(material is {IsValid:true,IsError:false});material.SetFeature("F_TRANSLUCENT",1);
   foreach(string role in new[]{"OpticalTransmission","VolumeThickness"})foreach(string parameter in new[]{"g_tGltf"+role+"Texture","g_flGltfHas"+role+"Texture","g_flGltf"+role+"UvSlot","g_vGltf"+role+"UvRowU","g_vGltf"+role+"UvRowV"})Assert.IsTrue(material.HasParameter(parameter),"Missing reflected parameter "+parameter);
   foreach(string parameter in new[]{"g_flGltfOpticalTransmissionFactor","g_flGltfVolumeThicknessMeshMeters","g_flGltfAttenuationDistanceInches","g_vGltfAttenuationColor"})Assert.IsTrue(material.HasParameter(parameter),"Missing reflected parameter "+parameter);
   Assert.AreNotEqual(0,material.Flags.GetInt("bWantsFBCopyTexture"));Assert.IsTrue(material.Flags.IsTranslucent);
   Measurements.Add(new{source_path=path,source_sha256=Convert.ToHexStringLower(SHA256.HashData(source)),material.ShaderName,shader.IsAnonymous,framebuffer_copy=material.Flags.GetInt("bWantsFBCopyTexture"),translucent=material.Flags.IsTranslucent,scope="Native anonymous compilation and reflected material/flags only; no GPU pixel behavior, optical semantic or public capability claim."});
  }
  finally{await MainThread.Wait();material?.Destroy();shader?.Destroy();}
 }
}
