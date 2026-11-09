using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
namespace Sandbox.Gltf.DirectGpu.NormalTangentTestValidation;
[TestClass]
public sealed class NormalFlatSkinPublicComputeTests
{
 public static string CheckoutRoot {get;set;}
 [TestMethod]
 public async Task UnequalSkinWeightsUseDeformedFlatFaceNormals()
 {
  await MainThread.Wait();var output=Path.Combine(CheckoutRoot,"Temp/gltf-validation/direct-gpu/normal-flat-skin-public-compute",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);
  var source=File.ReadAllText(Path.Combine(CheckoutRoot,"engine/Tools/Gltf/direct-gpu/models/NormalTangentTest/gltf_surface_five_uv.shader"));Shader shader=null;Material template=null;
  using var capture=new GltfGpuTriangleGateTests.NativeCapture(output);
  try
  {
   using(var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(90))) shader=await Shader.CompileAsync(source,timeout.Token);
   await MainThread.Wait();Assert.IsTrue(shader is {IsValid:true,IsAnonymous:true});template=Material.Create("normal_flat_skin_"+Guid.NewGuid().ToString("N"),shader);
   Assert.AreEqual(6,await GeneratedTangentSkinProbe.RunUnequalWeightFlatNormals(capture.Scene.SceneWorld,GltfManagedModelResource.ComputeShaderPath,template,capture.WaitFrames));
  }
  finally {await MainThread.Wait();template?.Destroy();shader?.Destroy();}
 }
}
