using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace Sandbox.Gltf.DirectGpu.AlphaBlendModeValidation;

internal static class AlphaStandaloneCompilation
{
 public static byte[] Compile(byte[] source, Func<string, byte[]> resolve)
 {
  var compiled = GltfCompiler.CompileWithEmbeddedImages(source, resolve);
  using var original = JsonDocument.Parse(source);
  using var packaged = GltfReader.Read(compiled);
  var originals = original.RootElement.GetProperty("images").EnumerateArray().ToArray();
  var images = packaged.Root.GetProperty("images").EnumerateArray().ToArray();
  Assert.AreEqual(originals.Length, images.Length);
  for (var i = 0; i < originals.Length; i++)
  {
   var uri = originals[i].GetProperty("uri").GetString();
   Assert.IsFalse(string.IsNullOrWhiteSpace(uri) || uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase), "This fixture expects original external encoded images.");
   var expected = SHA256.HashData(resolve(uri));
   Assert.IsFalse(images[i].TryGetProperty("uri", out _), "Standalone canonical fixture retained an external image URI.");
   CollectionAssert.AreEqual(expected, SHA256.HashData(packaged.ReadImage(i).Bytes), "Explicit image embedding changed original encoded image bytes.");
  }
  return compiled;
 }
}
