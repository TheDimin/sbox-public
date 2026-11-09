using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Sandbox.Gltf;

namespace Sandbox.Gltf.DirectGpu.NormalTangentTestValidation;

[TestClass]
public sealed class NormalTangentCandidateGpuTests
{
 public static string CheckoutRoot { get; set; }
 public static List<object> Measurements { get; } = new();
 [TestMethod]
 public async Task CompileAndDrawSourceAndCompiledCandidate()
 {
  await MainThread.Wait(); Measurements.Clear();
  var sourcePath = Path.Combine(CheckoutRoot,"engine/Tools/Gltf/direct-gpu/models/NormalTangentTest/gltf_surface_five_uv.shader");
  var source = File.ReadAllBytes(sourcePath); var sourceHash=Convert.ToHexStringLower(SHA256.HashData(source));
  var output=Path.Combine(CheckoutRoot,"Temp/gltf-validation/direct-gpu/normal-tangent-candidate",Guid.NewGuid().ToString("N")); Directory.CreateDirectory(output);
  Shader shader=null; Material template=null;
  using var capture = new GltfGpuTriangleGateTests.NativeCapture(output, new global::Vector3(200,0,0),global::Vector3.Zero,110);
  try
  {
   var empty=await capture.Take("empty_before"); Assert.AreEqual(0,empty.Coverage);
   using(var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(90))) shader=await Shader.CompileAsync(Encoding.UTF8.GetString(source),timeout.Token);
   await MainThread.Wait(); Assert.IsTrue(shader is {IsValid:true,IsAnonymous:true},"Five-UV candidate shader failed anonymous compilation.");
   Assert.AreEqual(sourceHash,Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(sourcePath))));
   template=Material.Create("normal_tangent_candidate_"+Guid.NewGuid().ToString("N"),shader); Assert.IsTrue(template is {IsValid:true,IsError:false});
   Measurements.Add(new {feature=template.GetFeature("F_RENDER_BACKFACES"),hasDoubleSided=template.HasParameter("g_flGltfDoubleSided")});
   Measurements.Add(new {sourceHash, output, scope="Anonymous candidate only; no public promotion"});
   foreach(var variant in new[]{"glTF"}) foreach(var compiled in new[]{false,true})
   {
    var dir=Path.Combine(CheckoutRoot,"Temp/gltf-reference/glTF-Sample-Assets/Models/NormalTangentTest",variant);
    var bytes=File.ReadAllBytes(Path.Combine(dir,variant=="glTF"?"NormalTangentTest.gltf":"NormalTangentTest.glb"));
    byte[] Resolve(string uri)=>File.ReadAllBytes(Path.Combine(dir,uri));
    // Diagnostic derivative only: retain all textures and geometry, but clear the
    // currently gated double-sided flag. This cannot establish source fidelity.
    var json=Encoding.UTF8.GetString(bytes); Assert.IsTrue(json.Contains("\"doubleSided\": true"));
    bytes=Encoding.UTF8.GetBytes(json.Replace("\"doubleSided\": true","\"doubleSided\": false"));
    using var document=GltfReader.Read(compiled?GltfCompiler.Compile(bytes,Resolve):bytes,Resolve);
    using var asset=GltfGpuAsset.PrepareProjected(document,GltfManagedModelResource.ComputeShaderPath,32*1024*1024,(_,_)=>template,GltfUnlitMaterialPlan.Features.None);
    using(var instance=asset.CreateInstance(capture.Scene.SceneWorld))
    {
     var frame=await capture.Take(variant+"_"+(compiled?"compiled":"source"));
     Assert.IsTrue(frame.Coverage>100,"Candidate did not produce visible geometry.");
     Assert.IsTrue(instance.SubmittedRevision==instance.PoseRevision && instance.DrawCalls>0 && instance.CallbackErrors.Length==0,string.Join("\n",instance.CallbackErrors));
     var row=instance.Readback().Single(); Assert.AreEqual(23322,row.VertexCount); Assert.AreEqual(23322,row.Indices.Length);
     Assert.IsTrue(row.Fields.Any(x=>x.Semantic=="TANGENT" && x.SemanticIndex==0));
     Measurements.Add(new {variant,compiled,frame.Path,frame.Sha256,frame.Coverage,row.VertexCount,row.IndexDataOrigin,instance.DrawCalls,scope="single-sided diagnostic derivative; actual GPU but not public Model.Load or source-material fidelity"});
    }
    var after=await capture.Take(variant+"_"+(compiled?"compiled":"source")+"_deleted"); Assert.AreEqual(0,after.Coverage,"Deleted candidate remained visible.");
   }
   Assert.AreEqual(3,await GeneratedTangentSkinProbe.Run(capture.Scene.SceneWorld,GltfManagedModelResource.ComputeShaderPath,template,capture.WaitFrames));
  }
  finally { await MainThread.Wait(); template?.Destroy(); shader?.Destroy(); }
 }
}
