using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
namespace Sandbox.Gltf.DirectGpu;
using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;
[TestClass]
public sealed class GltfGpuPublicCaptureTests
{
 private static string checkout;
 public static List<object> Measurements { get; } = new();
 public static void Configure(string root) { checkout=root; Measurements.Clear(); }
 [TestMethod]
 public async Task PublicModelLoadAndOrdinaryRendererMatchIndependentOrientationGeometry()
 {
  await MainThread.Wait();
  var output=Path.Combine(checkout,"Temp/gltf-validation/direct-gpu/public-captures/OrientationTest",Guid.NewGuid().ToString("N"));
  Directory.CreateDirectory(output); GltfGpuTriangleGateTests.Measurements.Clear();
  using var capture=new GltfGpuTriangleGateTests.NativeCapture(output);
  try
  {
   var result=await Validation.OrientationPublicCapture.Run(checkout,Path.Combine(checkout,"Temp/gltf-validation/direct-gpu/OrientationTest/source-oracle.json"),capture.Scene,capture.Camera,capture.WaitFrames,
    async label => (await capture.Take(label)).Pixels.ToArray());
   Measurements.Add(new {model="OrientationTest", loader="public Model.Load and ordinary ModelRenderer", result, output,
    directional_light="owned model probe: camera.WorldRotation, Color.White * 2", scope="Actual native camera pixels and GPU readback; same borrowed source materials isolate geometry/binding, not independent glTF PBR fidelity."});
   Assert.IsTrue(result.Passed,result.Error);
  }
  finally
  {
   Measurements.Add(new {model="OrientationTest",native_captures=GltfGpuTriangleGateTests.Measurements.ToArray()});
  }
 }
}
