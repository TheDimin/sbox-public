using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace Sandbox.Gltf.DirectGpu.AlphaBlendModeValidation;

[TestClass]
internal sealed class AlphaPublicFixtureRetryTests
{
 public static string CheckoutRoot {get;set;}
 public static string OutputDirectory {get;set;}
 public static List<object> Measurements {get;} = new();

 [TestMethod]
 public void VisibilityWithExplicitReaderCapability()
 {
  Directory.CreateDirectory(OutputDirectory);
  var source = AlphaControlFixtures.All().Single(x => x.Name == "mask_depth_equal").Bytes;
  var path = Path.Combine(OutputDirectory, "mask-depth-equal.gltf");
  File.WriteAllBytes(path, source);
  Measurements.Add(new { fixture = path, sha256 = Convert.ToHexStringLower(SHA256.HashData(source)), scope = "Core fixture bytes unchanged; reader explicitly opts into KHR_node_visibility for runtime overrides." });
  new AlphaSortObjectStateTests().TransparentUnitFollowsPrimaryStateOverridesVisibilityAndLifetime();

 }

 [TestMethod]
 public void StandaloneCanonicalPublicLoad()
 {
  AlphaPublicLoadTests.CheckoutRoot = CheckoutRoot;
  new AlphaPublicLoadTests().PublicModelLoadsAuthoredMaterials("glTF", true);
 }

 [TestMethod]
 public async Task StandaloneCanonicalPublicRendering()
 {
  AlphaCanonicalPublicRenderingTests.CheckoutRoot = CheckoutRoot;
  await new AlphaCanonicalPublicRenderingTests().SourceMaterialModelRenderer("glTF", true);
  Measurements.Add(new { embedding = "CompileWithEmbeddedImages", externalImageUris = 0, encodedImageHashesPreserved = true, scope = "Explicit standalone test packaging; automatic asset ownership and optimized publication remain separate." });
  Measurements.AddRange(AlphaCanonicalPublicRenderingTests.Measurements);
 }
}
