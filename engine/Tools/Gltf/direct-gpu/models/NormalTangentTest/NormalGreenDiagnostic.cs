using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Sandbox.Gltf;
namespace Sandbox.Gltf.DirectGpu.NormalTangentTestValidation;
[TestClass]
public sealed class NormalGreenDiagnostic
{
 public static string CheckoutRoot {get;set;}
 public static System.Collections.Generic.List<object> Measurements {get;}=new();
 [TestMethod]
 public async Task SourceNormalMapBaselineAndGreenInversionDiagnostic()
 {
  await MainThread.Wait();Measurements.Clear();var output=Path.Combine(CheckoutRoot,"Temp/gltf-validation/direct-gpu/normal-green-diagnostic",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);
  var path=Path.Combine(CheckoutRoot,"game/core/shaders/gltf_surface_five_uv.shader");var original=File.ReadAllText(path);const string needle="normalTs.y = -normalTs.y;";Assert.AreEqual(1,original.Split(needle).Length-1);var inverted=original.Replace(needle,"// Diagnostic: leave stock TransformNormal Y flip unopposed.");
  var input=Path.Combine(CheckoutRoot,"Temp/gltf-reference/glTF-Sample-Assets/Models/NormalTangentTest/glTF");var source=File.ReadAllBytes(Path.Combine(input,"NormalTangentTest.gltf"));Assert.AreEqual("ef2b152063304ebb0314646d3d1610cdc680e131d2cbfef10972dc6120d4dd18",Hash(source));byte[] Resolve(string uri)=>File.ReadAllBytes(Path.Combine(input,uri));
  using var capture=new GltfGpuTriangleGateTests.NativeCapture(output,new global::Vector3(184.28319f,0,-4.921259f),new global::Vector3(1.3877954f,0,-4.921259f),89.76378f);capture.Camera.DebugMode=SceneCameraDebugMode.NormalMap;
  var baseline=await Draw(original,"baseline",false);var flip=await Draw(inverted,"green_inversion",false);var flipSign=await Draw(original,"generated_w_inversion",true);
  // Matched central patches within each 192px pair. Raw pixel differences are
  // diagnostics only, not a specification or tolerance relaxation.
  double PairError(global::Color[] pixels)
  {
   double total=0;int count=0;for(int row=0;row<5;row++){int y=(int)Math.Round(18.3+33.8*row);for(int dy=-4;dy<=4;dy++)for(int dx=-4;dx<=4;dx++){
    var a=pixels[(y+dy)*192+(20+dx)];var b=pixels[(y+dy)*192+(45+dx)];total+=Math.Abs(a.r-b.r)+Math.Abs(a.g-b.g)+Math.Abs(a.b-b.b);count+=3;}}
   return total/count;
  }
  var before=PairError(baseline.Pixels);var after=PairError(flip.Pixels);var signError=PairError(flipSign.Pixels);
  var sum=0d;for(var i=0;i<flip.Pixels.Length;i++){var a=flip.Pixels[i];var b=flipSign.Pixels[i];sum+=Math.Abs(a.r-b.r)+Math.Abs(a.g-b.g)+Math.Abs(a.b-b.b);}var equivalentMean=sum/(flip.Pixels.Length*3);
  Measurements.Add(new{baselinePath=baseline.Path,baselineHash=baseline.Sha256,flipPath=flip.Path,flipHash=flip.Sha256,baselinePairError=before,flippedPairError=after,signPath=flipSign.Path,signHash=flipSign.Sha256,signPairError=signError,greenVsGeneratedSignMean=equivalentMean,scope="Anonymous shader green-channel negative control on original generated-tangent material; diagnostic only, no public or conformance acceptance."});
  Assert.IsTrue(baseline.Coverage>100 && flip.Coverage>100 && baseline.Sha256!=flip.Sha256,"Green inversion diagnostic must change actual normal pixels.");
  async Task<GltfGpuTriangleGateTests.NativeCapture.Frame> Draw(string text,string name,bool flipGeneratedW)
  {
   Shader shader=null,computeShader=null;Material template=null,compute=null;try
   {
    using(var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(90)))shader=await Shader.CompileAsync(text,timeout.Token);await MainThread.Wait();Assert.IsTrue(shader is {IsValid:true,IsAnonymous:true});template=Material.Create("normal_green_"+Guid.NewGuid().ToString("N"),shader);template.SetFeature("F_RENDER_BACKFACES",1);
    using var doc=GltfReader.Read(source,Resolve); GltfGpuAsset asset;
    if(flipGeneratedW)
    {
     var computeText=File.ReadAllText(Path.Combine(CheckoutRoot,"game/core/shaders/gltf_deform_cs.shader"));const string frame="value=float4(Unit(tangent,Perpendicular(worldNormal)),value.w*handedness);";Assert.AreEqual(1,computeText.Split(frame).Length-1);computeText=computeText.Replace(frame,"value=float4(Unit(tangent,Perpendicular(worldNormal)),value.w*handedness*(field.Padding0==1u ? -1 : 1));");
     using(var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(90)))computeShader=await Shader.CompileAsync(computeText,timeout.Token);await MainThread.Wait();Assert.IsTrue(computeShader is {IsValid:true,IsAnonymous:true});compute=Material.Create("normal_w_diagnostic_"+Guid.NewGuid().ToString("N"),computeShader);
     asset=GltfGpuAsset.Create(doc,compute,_=>null);try {asset.ConfigureProjectedSourceMaterials(64*1024*1024,(_,_)=>template,GltfUnlitMaterialPlan.Features.None,sourceLitFeatureSidedness:true);}catch{asset.Dispose();throw;}
    }
    else asset=GltfGpuAsset.PrepareProjected(doc,GltfManagedModelResource.ComputeShaderPath,64*1024*1024,(_,_)=>template,GltfUnlitMaterialPlan.Features.None,sourceLitFeatureSidedness:true);
    using(asset)
    using(var instance=asset.CreateInstance(capture.Scene.SceneWorld))
    {
     var material=asset.GetMaterials().Single();var texture=material.GetTexture("g_tGltfNormalTexture");Assert.IsTrue(texture is {IsValid:true,IsError:false});texture.MarkUsed(2048);await capture.WaitFrames();var frame=await capture.Take(name);Assert.IsTrue(instance.DrawCalls>0 && instance.CallbackErrors.Length==0 && instance.SubmittedRevision==instance.PoseRevision);return frame;
    }
   }
   finally {await MainThread.Wait();compute?.Destroy();computeShader?.Destroy();template?.Destroy();shader?.Destroy();}
  }
 }
 private static string Hash(byte[] b)=>Convert.ToHexStringLower(SHA256.HashData(b));
}
