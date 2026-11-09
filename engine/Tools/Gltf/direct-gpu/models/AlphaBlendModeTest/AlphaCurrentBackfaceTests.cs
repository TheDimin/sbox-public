using System;
using System.Collections.Generic;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Sandbox.Gltf;

namespace Sandbox.Gltf.DirectGpu.AlphaBlendModeValidation;

[TestClass]
public sealed class AlphaCurrentBackfaceTests
{
 public static string CheckoutRoot { get; set; }
 public static List<object> Measurements { get; } = new();
 [TestMethod]
 [DataRow(false, "BLEND")]
 [DataRow(true, "BLEND")]
 public async Task DoubleSidedCandidateMatchesIndependentBakedNormalsBackface(bool compiled,string mode)
 {
  await MainThread.Wait(); var output=Path.Combine(CheckoutRoot,"Temp/gltf-validation/direct-gpu/normal-tangent-backface",Guid.NewGuid().ToString("N")); Directory.CreateDirectory(output);
  var shaderPath=Path.Combine(CheckoutRoot,"engine/Tools/Gltf/direct-gpu/models/AlphaBlendModeTest/gltf_lit_alpha_current_candidate.shader"); var source=File.ReadAllBytes(shaderPath); var hash=Convert.ToHexStringLower(SHA256.HashData(source));
  var folder=Path.Combine(CheckoutRoot,"research/gltf-validation/direct-gpu/models/NormalTangentMirrorTest");
  var normalBytes=File.ReadAllBytes(Path.Combine(folder,"asymmetric-normal-uv-fixture.gltf")); var bakedBytes=File.ReadAllBytes(Path.Combine(folder,"asymmetric-baked-normal-reference.gltf"));
  Assert.AreEqual("9fcabb44c8471390ca4b9bb6c29e173d7fe525e827f5bdfdc8085f8dc62d023e",Convert.ToHexStringLower(SHA256.HashData(normalBytes)));
  Assert.AreEqual("80e0dc7470d11472433e96fd7d21224842dc3d3d8a2e3444956a8eeed32eaeda",Convert.ToHexStringLower(SHA256.HashData(bakedBytes)));
  var normal=JsonNode.Parse(normalBytes); var baked=JsonNode.Parse(bakedBytes);
  var wrong=normal.DeepClone(); var transform=wrong["materials"][0]["normalTexture"]["extensions"]["KHR_texture_transform"]; transform["offset"]=new JsonArray(0,0); transform["scale"]=new JsonArray(1,1);
  var wrongW=normal.DeepClone(); var uri=wrongW["buffers"][0]["uri"].GetValue<string>(); const string dataPrefix="data:application/octet-stream;base64,";
  Assert.IsTrue(uri.StartsWith(dataPrefix,StringComparison.Ordinal)); var buffer=Convert.FromBase64String(uri.Substring(dataPrefix.Length));
  for(var v=0;v<32;v++){var at=768+v*16+12;var w=BinaryPrimitives.ReadSingleLittleEndian(buffer.AsSpan(at));BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(at),-w);}
  wrongW["buffers"][0]["uri"]=dataPrefix+Convert.ToBase64String(buffer);
  var selected=new HashSet<string>(StringComparer.Ordinal){"KHR_texture_transform"};
  byte[] Form(JsonNode value) {var b=Encoding.UTF8.GetBytes(value.ToJsonString()); return compiled ? GltfCompiler.Compile(b,null,selected) : b;}
  Shader shader=null; Material template=null;
  using var capture=new GltfGpuTriangleGateTests.NativeCapture(output,new global::Vector3(-500,0,0),global::Vector3.Zero,185);
  try
  {
   capture.Camera.DebugMode=SceneCameraDebugMode.NormalMap;
   using(var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(90))) shader=await Shader.CompileAsync(Encoding.UTF8.GetString(source),timeout.Token);
   await MainThread.Wait(); Assert.IsTrue(shader is {IsValid:true,IsAnonymous:true}); Assert.AreEqual("1a65f5c77ab9424972754e2d4c1a88a94ba16b56d76a9f7e138b639d2c5ce8f1",hash); Assert.AreEqual(hash,Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(shaderPath))));
   template=Material.Create("normal_asymmetric_"+Guid.NewGuid().ToString("N"),shader); Assert.IsTrue(template is {IsValid:true,IsError:false});
   using var actualDoc=GltfReader.Read(Form(normal),null,selected); using var expectedDoc=GltfReader.Read(Form(baked),null,selected); using var wrongDoc=GltfReader.Read(Form(wrong),null,selected); using var wrongWDoc=GltfReader.Read(Form(wrongW),null,selected);
   GltfGpuAsset Prepare(GltfDocument d)
   {
    var asset=GltfGpuAsset.Prepare(d,GltfManagedModelResource.ComputeShaderPath,_=>null);
    try
    {
     asset.ConfigureProjectedVariants(1024*1024,(index,plan,images)=>
     {
      var material=d.ReadMaterial(index.Value); var original=GltfStockMaterialAdapter.PrepareSource(d,material,images.ReadImage);
      Assert.IsTrue(original.DoubleSided);
      var oneSided=new GltfStockMaterialAdapter.SourcePrepared { Textures=original.Textures,Resources=original.Resources,LinearBaseColorFactor=original.LinearBaseColorFactor,EmissiveFactor=original.EmissiveFactor,MetallicFactor=original.MetallicFactor,RoughnessFactor=original.RoughnessFactor,OcclusionStrength=original.OcclusionStrength,NormalScale=original.NormalScale,DoubleSided=false };
      var lease=GltfStockMaterialAdapter.AcquireSource(oneSided,template,plan.MaterialUv,plan.UvProjection); Material clone=null;
      try
      {
       clone=lease.Value.CreateCopy("backface_candidate_"+Guid.NewGuid().ToString("N")); clone.SetFeature("F_RENDER_BACKFACES",1); clone.SetFeature("F_ALPHA_TEST",mode=="MASK"?1:0); clone.SetFeature("F_TRANSLUCENT",mode=="BLEND"?1:0); clone.Set("g_flGltfAlphaCutoff",.5f); clone.Set("g_flGltfDoubleSided",1f); Assert.AreEqual(mode=="MASK",clone.Flags.IsAlphaTest); Assert.AreEqual(mode=="BLEND",clone.Flags.IsTranslucent);
       Assert.AreEqual(1,clone.GetFeature("F_RENDER_BACKFACES"));
       Measurements.Add(new{feature=clone.GetFeature("F_RENDER_BACKFACES"),doubleSidedParameter=clone.HasParameter("g_flGltfDoubleSided"),scope="test-owned copied material, not shared cache or public adapter"});
       var retained=clone; return new GltfMaterialLease<Material>(retained,()=>{retained.Destroy();lease.Dispose();},()=>ThreadSafe.AssertIsMainThread());
      }
      catch {clone?.Destroy();lease.Dispose();throw;}
     });
     return asset;
    }
    catch {asset.Dispose();throw;}
   }
   using var actual=Prepare(actualDoc); using var expected=Prepare(expectedDoc); using var negative=Prepare(wrongDoc); using var negativeW=Prepare(wrongWDoc);
   Assert.AreEqual(0,(await capture.Take("empty_before")).Coverage);
   var a=await Draw(actual,"actual_normal_texture"); var e=await Draw(expected,"independent_baked_normal"); var w=await Draw(negative,"negative_wrong_uv_transform"); var tw=await Draw(negativeW,"negative_flipped_tangent_w");
   double sum=0,badSum=0,wSum=0,max=0; int inter=0,union=0,changed=0,wChanged=0;
   for(int i=0;i<a.Pixels.Length;i++)
   {
    var p=a.Pixels[i]; var q=e.Pixels[i]; var z=w.Pixels[i]; var t=tw.Pixels[i]; var pa=GltfGpuTriangleGateTests.NativeCapture.Foreground(p); var pe=GltfGpuTriangleGateTests.NativeCapture.Foreground(q); if(pa||pe) union++; if(pa&&pe) inter++;
    var err=new[]{Math.Abs((double)p.r-q.r),Math.Abs((double)p.g-q.g),Math.Abs((double)p.b-q.b)}; var bad=new[]{Math.Abs((double)z.r-q.r),Math.Abs((double)z.g-q.g),Math.Abs((double)z.b-q.b)};
    var wErr=new[]{Math.Abs((double)t.r-q.r),Math.Abs((double)t.g-q.g),Math.Abs((double)t.b-q.b)}; sum+=err.Sum();badSum+=bad.Sum();wSum+=wErr.Sum();max=Math.Max(max,err.Max()); if(bad.Max()>.02)changed++; if(wErr.Max()>.02)wChanged++;
   }
   var meanRed=a.Pixels.Where(GltfGpuTriangleGateTests.NativeCapture.Foreground).Average(p=>(double)p.r); Measurements.Add(new{phase="independent_backface_signed_world_normal",compiled,mode,meanRed}); Assert.IsTrue(meanRed<.15,$"Independent negative world normal sign failed: mean red {meanRed}");
   var mean=sum/(a.Pixels.Length*3);var badMean=badSum/(a.Pixels.Length*3);var wMean=wSum/(a.Pixels.Length*3);var iou=union==0?0:(double)inter/union;
   Measurements.Add(new{compiled,mode,shaderSha256=hash,actual=a.Path,actualSha256=a.Sha256,expected=e.Path,expectedSha256=e.Sha256,negative=w.Path,negativeSha256=w.Sha256,wrongTangent=tw.Path,wrongTangentSha256=tw.Sha256,intersection=inter,iou,mean,max,changed,badMean,wChanged,wMean,scope="Independent baked normal RGB fixture; explicit test-owned double-sided material copy; not production adapter; normal-debug pixels with signed world-normal control; not lit PBR or original model."});
   Assert.IsTrue(inter>256 && iou>=.995 && mean<=.002 && max<=.03,$"Baked normal comparison failed: intersection {inter} iou {iou} mean {mean} max {max}");
   Assert.IsTrue(changed>=4 && badMean>mean+.00005,$"Wrong UV negative failed: changed {changed} mean {badMean} vs {mean}");
   Assert.IsTrue(wChanged>=4 && wMean>mean+.00005,$"Flipped tangent W negative failed: changed {wChanged} mean {wMean} vs {mean}");
   async Task<GltfGpuTriangleGateTests.NativeCapture.Frame> Draw(GltfGpuAsset asset,string label)
   {
    GltfGpuTriangleGateTests.NativeCapture.Frame frame;
    using(var instance=asset.CreateInstance(capture.Scene.SceneWorld))
    {
     frame=await capture.Take(label); Assert.IsTrue(instance.DrawCalls>0 && instance.SubmittedRevision==instance.PoseRevision && instance.CallbackErrors.Length==0,string.Join("\n",instance.CallbackErrors));
     var row=instance.Readback().Single(); Assert.AreEqual(32,row.VertexCount);Assert.AreEqual(48,row.Indices.Length);Assert.AreEqual("gpu-readback",row.IndexDataOrigin);
    }
    Assert.AreEqual(0,(await capture.Take(label+"_deleted")).Coverage); return frame;
   }
  }
  finally {await MainThread.Wait();template?.Destroy();shader?.Destroy();}
 }
}
