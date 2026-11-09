using System;
namespace Sandbox.Gltf.DirectGpu.NormalTangentTestValidation;
[TestClass]
public sealed class GeneratedConventionTests
{
 [TestMethod]
 public void MirroredAndNonMirroredCornersUseGltfSigns() => Assert.AreEqual(7,GeneratedTangentProducerTests.Run());
 [TestMethod]
 public void ConvertedSignsMatchPinnedNativeMikkForCurvedAndDegenerateInputs() => Assert.AreEqual(3,NativeMikkDifferential.Run("/tmp/normal-tangent-mikk-reference"));
}
