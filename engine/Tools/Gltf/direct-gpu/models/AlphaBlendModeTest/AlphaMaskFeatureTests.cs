using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Sandbox.Gltf.DirectGpu.AlphaBlendModeValidation;

[TestClass]
public sealed class AlphaMaskFeatureTests
{
 public static string CheckoutRoot {get;set;}
 public static string OutputDirectory {get;set;}
 public static string ShaderSha256 {get;set;}
 public static List<object> Measurements {get;}=new();
 [TestMethod]
 public async Task MaskNativeFeatures()
 {
  var source=File.ReadAllBytes(Path.Combine(CheckoutRoot,"engine/Tools/Gltf/direct-gpu/models/AlphaBlendModeTest/gltf_lit_mask_candidate.shader"));
  Assert.AreEqual(ShaderSha256,Convert.ToHexStringLower(SHA256.HashData(source)));
  Shader shader=null;Material root=null;var copies=new List<Material>();
  try
  {
   using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(90));shader=await Shader.CompileAsync(Encoding.UTF8.GetString(source),timeout.Token);await MainThread.Wait();
   Assert.IsTrue(shader is {IsValid:true,IsAnonymous:true},"Candidate compilation failed.");root=Material.Create("alpha_lit_"+Guid.NewGuid().ToString("N"),shader);
   Assert.IsTrue(root is {IsValid:true,IsError:false});
   foreach(var mode in new[]{"OPAQUE","MASK"}) foreach(var sided in new[]{false,true})
   {
    var material=root.CreateCopy();copies.Add(material);material.SetFeature("F_ALPHA_TEST",mode=="MASK"?1:0);material.SetFeature("F_TRANSLUCENT",mode=="BLEND"?1:0);material.SetFeature("F_RENDER_BACKFACES",sided?1:0);
    var alpha=material.Flags.IsAlphaTest;var blend=material.Flags.IsTranslucent;
    Measurements.Add(new{phase="alpha_candidate_native_flags",mode,sided,alpha,blend,backfaces=material.GetFeature("F_RENDER_BACKFACES"),shader_sha256=ShaderSha256});
    Assert.AreEqual(mode=="MASK",alpha,mode+" alpha test");Assert.AreEqual(mode=="BLEND",blend,mode+" blend");Assert.AreEqual(sided?1:0,material.GetFeature("F_RENDER_BACKFACES"));
   }
  }
  finally{await MainThread.Wait();foreach(var m in copies)m.Destroy();root?.Destroy();shader?.Destroy();}
 }
}
