using System.Text;
using System.Text.Json;
using Sandbox.Gltf.DirectGpu.MeshPrimitiveModesValidation;

static class Program
{
 static async Task Main()
 {
  var checks = 0;
  void Check(bool value,string name) { if(!value) throw new Exception(name); checks++; }
  var bytes=Encoding.UTF8.GetBytes("shader A");
  using(var owner=new CaptureShaderBatch(CancellationToken.None,_=>{}))
  {
   var first=await CaptureShaderBatch.AcquireAsync(bytes);var second=await CaptureShaderBatch.AcquireAsync((byte[])bytes.Clone());
   Check(ReferenceEquals(first,second)&&owner.CompileCount==1&&owner.ReuseCount==1,"same source reuse");
   try { await CaptureShaderBatch.AcquireAsync(Encoding.UTF8.GetBytes("shader B")); throw new Exception("different shader accepted"); } catch(InvalidOperationException) { checks++; }
   Check(first.DestroyCount==0,"borrowed shader destroyed early"); owner.Dispose(); owner.Dispose(); Check(first.DestroyCount==1&&owner.Released,"exactly one destruction");
  }
  using(var cancel=new CancellationTokenSource())
  using(var owner=new CaptureShaderBatch(cancel.Token,_=>{}))
  {
   Sandbox.Shader.Block = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
   var pending=CaptureShaderBatch.AcquireAsync(bytes); cancel.Cancel();
   Check(!pending.IsCompleted,"must not detach work on cancellation");
   Sandbox.Shader.Block.SetResult();
   try { await pending; throw new Exception("cancel ignored"); } catch(OperationCanceledException) { checks++; }
   owner.Dispose();Check(owner.Released,"cleanup after completed cancellation");Sandbox.Shader.Block=null;
  }
  var journal=Path.Combine(Path.GetTempPath(),"capture-batch-test-"+Guid.NewGuid().ToString("N")+".json");
  try
  {
   RemainingControlsBatch.JournalPath=journal;
   var rows=await RemainingControlsBatch.Run("unused",CancellationToken.None);using var json=JsonDocument.Parse(File.ReadAllText(journal));var r=json.RootElement;
   Check(rows.Count==2&&rows.All(x=>x.Passed),"two separate case results");
   var rowJson=JsonSerializer.SerializeToElement(rows[0]); Check(rowJson.GetProperty("Batch").GetProperty("compile_count").GetInt32()==1 && rowJson.GetProperty("Batch").GetProperty("shader_released").GetBoolean(),"result records lifetime proof");
   Check(r.GetProperty("terminal").GetString()=="complete"&&r.GetProperty("shader_released").GetBoolean(),"terminal cleanup recorded");
   Check(r.GetProperty("compile_count").GetInt32()==1&&r.GetProperty("reuse_count").GetInt32()==1,"one actual compile and one reuse");
   Check(r.GetProperty("events").EnumerateArray().Count(x=>x.GetProperty("state").GetString()=="case_completed")==2,"case checkpoints");
   FakeCases.FailFirst=true;
   var failedRows=await RemainingControlsBatch.Run("unused",CancellationToken.None); Check(failedRows.Count==2 && !failedRows[0].Passed && failedRows[1].Passed,"failed case remains machine-readable");
   using var failure=JsonDocument.Parse(File.ReadAllText(journal));var failed=failure.RootElement;
   Check(failed.GetProperty("terminal").GetString()=="failed" && failed.GetProperty("shader_released").GetBoolean(),"failure journal and cleanup");
   Check(failed.GetProperty("completed_cases").GetArrayLength()==2 && !failed.GetProperty("completed_cases")[0].GetProperty("Passed").GetBoolean(),"failed row retained");
   FakeCases.FailFirst=false;
  }
  finally { File.Delete(journal); }
  Console.WriteLine($"PASS {checks} lifecycle assertions; fake compiler, not native rendering");
 }
}
namespace Sandbox
{
 public static class ThreadSafe { public static void AssertIsMainThread() {} }
 public static class MainThread { public static Task Wait()=>Task.CompletedTask; }
 public sealed class Shader
 {
  public bool IsValid=>true; public bool IsAnonymous=>true;public int DestroyCount;
  public static TaskCompletionSource Block;
  public static async Task<Shader> CompileAsync(string source,CancellationToken token) { if(Block!=null) await Block.Task; token.ThrowIfCancellationRequested(); return new Shader(); }
  public void Destroy()=>DestroyCount++;
 }
}
namespace Sandbox.Gltf.DirectGpu.MeshPrimitiveModesValidation
{
 internal static class FakeCases { public static bool FailFirst; }
 internal static class FixtureShader { public static byte[] Bytes => File.ReadAllBytes("engine/Tools/Gltf/direct-gpu/models/MeshPrimitiveModes/combined_alpha_pointline_candidate.shader"); }
 internal sealed class MergedDrawAttributeTests { public static List<object> Measurements {get;} = new(); public static void Configure(string _){} public async Task SharedMaterialDistinctDrawAttributeAndMutation() { _=await CaptureShaderBatch.AcquireAsync(FixtureShader.Bytes); if(FakeCases.FailFirst) throw new InvalidOperationException("deliberate assertion failure"); } }
 internal sealed class MergedLineBlendTests { public static List<object> Measurements {get;} = new(); public static void Configure(string _){} public async Task FlaggedLineBlendOpacityAndRestore() { _=await CaptureShaderBatch.AcquireAsync(FixtureShader.Bytes); } }
}
