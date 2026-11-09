using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
namespace Sandbox.Gltf.DirectGpu.MaterialsVariantsShoeValidation;
[TestClass]
public sealed class ShoeCompiledCaptureRetry
{
 public static List<object> Measurements => ShoePublicCaptureTests.Measurements;
 public static void Configure(string root)=>ShoePublicCaptureTests.Configure(root);
 [TestMethod] public async Task CompiledGltfWithExternalDependencies()=>await new ShoePublicCaptureTests().PublicVariantDrawAndReset(true);
}
