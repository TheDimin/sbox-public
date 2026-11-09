using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
namespace Sandbox.Gltf.DirectGpu.MaterialsVariantsShoeValidation;
[TestClass]
public sealed class ShoeTightCompiledAcceptance
{
 public static List<object> Measurements => ShoeReviewCaptureTests.Measurements;
 public static void Configure(string root)=>ShoeReviewCaptureTests.Configure(root);
 [TestMethod] public async Task CompiledGltfResetOriginalThreshold()=>await new ShoeReviewCaptureTests().PublicVariantDrawAndReset(true);
}
