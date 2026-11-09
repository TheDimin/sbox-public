import pathlib,unittest
root=pathlib.Path(__file__).resolve().parents[5];own=pathlib.Path(__file__).resolve().parent
class SourceContract(unittest.TestCase):
 def test_remaining_controls_change_only_ownership_and_cooperative_cancellation(self):
  base=root/'engine/Tools/Gltf/direct-gpu/models/MeshPrimitiveModes'
  for name in ('MergedDrawAttributeTests.cs','MergedLineBlendTests.cs'):
   original=(base/name).read_text(); candidate=(own/name).read_text()
   candidate=candidate.replace('shader=await CaptureShaderBatch.AcquireAsync(bytes);','using(var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(90)))shader=await Shader.CompileAsync(Encoding.UTF8.GetString(bytes),timeout.Token);')
   candidate=candidate.replace('material?.Destroy();','material?.Destroy();shader?.Destroy();')
   candidate=candidate.replace('capture.Cancellation=CaptureShaderBatch.Cancellation; capture.Camera.AutoExposure.Enabled=false;','capture.Camera.AutoExposure.Enabled=false;')
   self.assertEqual(original,candidate,name)
 def test_capture_helper_does_not_change_required_frame_counts_or_render_assertions(self):
  original=(root/'engine/Tools/Gltf/direct-gpu/GltfGpuTriangleGateTests.cs').read_text();candidate=(own/'GltfGpuTriangleGateTests.cs').read_text()
  candidate=candidate.replace('private int frames;\n\t\tpublic System.Threading.CancellationToken Cancellation { get; set; }','private int frames;')
  candidate=candidate.replace('private async Task Safe()','private static async Task Safe()')
  candidate=candidate.replace('Cancellation.ThrowIfCancellationRequested(); await MainThread.Wait(); Cancellation.ThrowIfCancellationRequested(); Assert.IsTrue( Application.IsEditor && !Game.IsPlaying ); RequireNativeRenderer();','await MainThread.Wait(); Assert.IsTrue( Application.IsEditor && !Game.IsPlaying ); RequireNativeRenderer();')
  candidate=candidate.replace('await Task.Delay( 20, Cancellation ); await MainThread.Wait();','await Task.Delay( 20 ); await MainThread.Wait();').replace('await Task.Delay( 20, Cancellation ); await Safe();','await Task.Delay( 20 ); await Safe();')
  self.assertEqual(original,candidate)
if __name__=='__main__':unittest.main()
