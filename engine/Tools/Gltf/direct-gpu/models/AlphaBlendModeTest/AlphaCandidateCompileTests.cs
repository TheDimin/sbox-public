using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Sandbox.Gltf.DirectGpu.AlphaBlendModeValidation;

[TestClass]
public sealed class AlphaCandidateCompileTests
{
 public static string ManifestPath { get; set; }
 public static string ManifestSha256 { get; set; }
 public static List<object> Measurements { get; } = new();
 [TestMethod]
 public async Task CompileSixAnonymousCandidatesSerially()
 {
  var bytes=File.ReadAllBytes(ManifestPath);
  Assert.AreEqual(ManifestSha256, Convert.ToHexStringLower(SHA256.HashData(bytes)), "Generation receipt changed.");
  using var json=JsonDocument.Parse(bytes); var variants=json.RootElement.GetProperty("variants").EnumerateArray().ToArray();
  Assert.AreEqual(6,variants.Length); var failures=new List<string>();
  foreach(var item in variants)
  {
   var mode=item.GetProperty("mode").GetString(); var sided=item.GetProperty("doubleSided").GetBoolean(); var path=item.GetProperty("sourcePath").GetString(); var expected=item.GetProperty("sourceSha256").GetString(); var source=File.ReadAllBytes(path);
   Assert.AreEqual(expected,Convert.ToHexStringLower(SHA256.HashData(source)),"Candidate source changed.");
   Shader shader=null; Material material=null; string error=null;
   try
   {
    using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(90)); shader=await Shader.CompileAsync(Encoding.UTF8.GetString(source),timeout.Token); await MainThread.Wait();
    if(shader is not { IsValid:true, IsAnonymous:true }) throw new InvalidOperationException("Compiler did not return a valid anonymous shader.");
    material=Material.Create("alpha_candidate_"+Guid.NewGuid().ToString("N"),shader);
    if(material is not { IsValid:true, IsError:false }) throw new InvalidOperationException("Anonymous material invalid.");
    Measurements.Add(new {mode,sided,path,sha256=expected,shader_valid=shader.IsValid,shader_anonymous=shader.IsAnonymous,material_translucent=material.Flags.IsTranslucent,material_alpha_test=material.Flags.IsAlphaTest});
   }
   catch(Exception ex){error=ex.ToString();failures.Add(mode+"/"+sided+": "+error); Measurements.Add(new {mode,sided,path,sha256=expected,error});}
   finally{await MainThread.Wait();material?.Destroy();shader?.Destroy();}
  }
  Assert.AreEqual(0,failures.Count,string.Join("\n",failures));
 }
}
