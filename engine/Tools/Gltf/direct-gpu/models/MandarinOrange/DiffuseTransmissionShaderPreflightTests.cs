using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Assert=Microsoft.VisualStudio.TestTools.UnitTesting.Assert;
using Sandbox;

namespace Sandbox.Gltf.DirectGpu.MandarinOrangeValidation;

[TestClass]
internal sealed class DiffuseTransmissionShaderPreflightTests
{
 public static string CheckoutRoot {get;set;}
 public static List<object> Measurements {get;}=new();
 [TestMethod]
 public async Task AnonymousNativeShaderCompilesAndReflectsThinDiffuseInputs()
 {
  await MainThread.Wait();string path=Path.Combine(CheckoutRoot,"engine/Tools/Gltf/direct-gpu/models/MandarinOrange/gltf_diffuse_transmission_candidate.shader");byte[] source=File.ReadAllBytes(path);Shader shader=null;Material template=null;
  try
  {
   using(var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(90)))shader=await Shader.CompileAsync(System.Text.Encoding.UTF8.GetString(source),timeout.Token);
   await MainThread.Wait();Assert.IsTrue(shader is {IsValid:true,IsAnonymous:true});template=Material.Create("diffuse_shader_preflight_"+Guid.NewGuid().ToString("N"),shader);Assert.IsTrue(template is {IsValid:true,IsError:false});
   foreach(string role in new[]{"DiffuseTransmission","DiffuseTransmissionColor"})foreach(string parameter in new[]{"g_tGltf"+role+"Texture","g_flGltfHas"+role+"Texture","g_flGltf"+role+"UvSlot","g_vGltf"+role+"UvRowU","g_vGltf"+role+"UvRowV"})Assert.IsTrue(template.HasParameter(parameter),"Actual native shader did not reflect "+parameter);
   Assert.IsTrue(template.HasParameter("g_flGltfDiffuseTransmissionFactor"));Assert.IsTrue(template.HasParameter("g_vGltfDiffuseTransmissionColorFactor"));
   Measurements.Add(new{scope="Actual anonymous native HLSL compilation and reflected material inputs only. No producer parsing, GPU output, resource/presentation or public capability acceptance.",source_path=path,source_sha256=Convert.ToHexStringLower(SHA256.HashData(source)),template.ShaderName,shader.IsAnonymous,engine_mvid=typeof(Model).Assembly.ManifestModule.ModuleVersionId});
  }
  finally{await MainThread.Wait();template?.Destroy();shader?.Destroy();}
 }
}
