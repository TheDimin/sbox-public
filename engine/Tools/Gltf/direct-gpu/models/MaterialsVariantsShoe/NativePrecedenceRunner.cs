using System;
using System.Text.Json;
using System.Threading.Tasks;
namespace Sandbox.Gltf.DirectGpu.MaterialsVariantsShoeValidation;
public static class NativePrecedenceRunner
{
 public sealed record Result(string Name, bool Passed, string Error);
 public static async Task<Result[]> Run(string root)
 {
  var receipt = await NativeMaterialOverridePrecedenceProbe.Run(root);
  var data = JsonSerializer.SerializeToElement(receipt, receipt.GetType());
  return new[]{new Result("native_override_matrix", data.GetProperty("passed").GetBoolean(), data.GetProperty("failure").ToString())};
 }
}
