using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Sandbox;

namespace Sandbox.Gltf.DirectGpu.AlphaBlendModeValidation;

// Presentation only. The accepted source/canonical functional cases remain separate.
[TestClass]
internal sealed class AlphaSourcePresentationTests
{
 public static string CheckoutRoot { get; set; }
 public static string OutputDirectory { get; set; }
 public static List<object> Measurements { get; } = new();
 static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

 [TestMethod]
 public async Task SourceFrontViewSettledAtReferenceDimensions()
 {
  await MainThread.Wait();
  const int width = 332, height = 154;
  var sourceDirectory = Path.Combine(CheckoutRoot, "Temp/gltf-reference/glTF-Sample-Assets/Models/AlphaBlendModeTest/glTF");
  var sourcePath = Path.Combine(sourceDirectory, "AlphaBlendModeTest.gltf");
  var source = File.ReadAllBytes(sourcePath);
  Assert.AreEqual("49e06672900df95593040d35bbc7a2ee5921ae8d46edc44b64ccf2df65e64849", Hash(source));
  Directory.CreateDirectory(OutputDirectory);
  var directory = Path.Combine(Path.GetTempPath(), "alpha_front_" + Guid.NewGuid().ToString("N"));
  Directory.CreateDirectory(directory);
  var name = "alpha_front_" + Guid.NewGuid().ToString("N") + ".gltf";
  LocalFileSystem mount = null;
  Model model = null;
  ModelRenderer renderer = null;
  SceneDirectionalLight light = null;
  var position = new global::Vector3(12 / .0254f, 0, 2.5f / .0254f);
  var targetPosition = new global::Vector3(0, 0, 1.1f / .0254f);
  using var capture = new GltfGpuTriangleGateTests.NativeCapture(OutputDirectory, position, targetPosition, 4.15f / .0254f);
  using var target = Texture.CreateRenderTarget().WithSize(width, height).WithFormat(ImageFormat.RGBA8888).Create("alpha_front_" + Guid.NewGuid().ToString("N"));
  try
  {
   var helperLight = (ScenePointLight)typeof(GltfGpuTriangleGateTests.NativeCapture).GetField("light", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(capture);
   helperLight.LightColor = Color.Black;
   var camera = capture.Camera;
   camera.CustomSize = new global::Vector2(width, height);
   camera.BackgroundColor = new Color(.2f, .2f, .3f, 1);
   Tonemapping tonemapping;
   using (capture.Scene.Push()) tonemapping = camera.GameObject.Components.Create<Tonemapping>();
   tonemapping.Mode = Tonemapping.TonemappingMode.ACES;
   tonemapping.AutoExposureEnabled = true;
   tonemapping.MinimumExposure = 1;
   tonemapping.MaximumExposure = 1;
   tonemapping.ExposureCompensation = -.5f;
   tonemapping.Rate = 20;
   using (capture.Scene.Push()) capture.Scene.EditorTick(0, 0);
   light = new SceneDirectionalLight(capture.Scene.SceneWorld, Rotation.LookAt(new global::Vector3(-1, 0, -.3f).Normal, global::Vector3.Up), Color.White * .75f);
   var empty = await SettledEmpty("empty_before");
   foreach (var path in Directory.GetFiles(sourceDirectory)) File.Copy(path, Path.Combine(directory, Path.GetFileName(path)));
   File.WriteAllBytes(Path.Combine(directory, name), source);
   mount = new LocalFileSystem(directory);
   FileSystem.Mounted.Mount(mount);
   model = Model.Load(name);
   Assert.IsTrue(model is { IsValid: true, IsError: false } && model.native.IsNull);
   using (capture.Scene.Push())
   {
    renderer = capture.Scene.CreateObject().Components.Create<ModelRenderer>(false);
    renderer.Model = model;
    renderer.Enabled = true;
    capture.Scene.EditorTick(0, 0);
   }
   var rows = new List<object>();
   Color32[] previous = null, final = null;
   int stablePairs = 0;
   string firstPath = null, firstHash = null, finalPath = null, finalHash = null;
   for (int frame = 0; frame < 24; frame++)
   {
    var current = await Render();
    int changed = 0, maxDelta = 0;
    long sumDelta = 0;
    if (previous is not null)
    {
     for (int i = 0; i < current.Length; i++)
     {
      var a = current[i]; var b = previous[i];
      int dr = Math.Abs(a.r - b.r), dg = Math.Abs(a.g - b.g), db = Math.Abs(a.b - b.b);
      var delta = Math.Max(dr, Math.Max(dg, db));
      maxDelta = Math.Max(maxDelta, delta);
      sumDelta += dr + dg + db;
      if (delta != 0) changed++;
     }
     stablePairs = maxDelta == 0 ? stablePairs + 1 : 0;
    }
    rows.Add(new { render = frame + 1, engineFrame = Application.FrameCount, changedPixels = previous is null ? (int?)null : changed, maximumChannelDeltaCodes = previous is null ? (int?)null : maxDelta, meanAbsoluteDeltaCodes = previous is null ? (double?)null : sumDelta / (3.0 * current.Length), stablePairs,
     observedExposure = new { enabled = camera.AutoExposure.Enabled, minimum = camera.AutoExposure.MinimumExposure, maximum = camera.AutoExposure.MaximumExposure, compensationEv = camera.AutoExposure.Compensation, nativeTonemapEnabled = camera.SceneCamera.Tonemap.Enabled } });
    if (frame == 0) (firstPath, firstHash) = Save("first_actual_source");
    if (stablePairs >= 4)
    {
     final = current;
     (finalPath, finalHash) = Save("settled_source_front");
     break;
    }
    previous = current;
   }
   Assert.IsNotNull(final, "Source presentation did not settle across four exact successive native RTT pairs.");
   Measurements.Add(new { phase = "source_rtt_convergence", settled = true, width, height, firstPath, firstHash, finalPath, finalHash, rows, criteria = "Four successive full-image native RTT/readback pairs with maximum RGB channel delta zero." });
   int minX = width, maxX = -1, minY = height, maxY = -1, coverage = 0, nearWhite = 0;
   for (int i = 0; i < final.Length; i++)
   {
    var a = final[i]; var b = empty[i];
    if (Math.Max(Math.Abs(a.r - b.r), Math.Max(Math.Abs(a.g - b.g), Math.Abs(a.b - b.b))) <= 3) continue;
    coverage++;
    var x = i % width; var y = i / width;
    minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
    if (a.r >= 245 && a.g >= 245 && a.b >= 245) nearWhite++;
   }
   Assert.IsTrue(coverage > 3000 && maxX - minX > 285 && minX > 0 && maxX < width - 1 && minY > 0 && maxY < height - 1, "Front view failed the bounded nonempty/full-model framing check.");
   Assert.IsTrue(renderer.GltfInstance.DrawCalls > 0 && renderer.GltfInstance.CallbackErrors.Length == 0);
   var drawCalls = renderer.GltfInstance.DrawCalls;
   var materialNames = model.Materials.Select(material => material.ShaderName).ToArray();
   var observedCamera = new { positionInches = new[] { camera.WorldPosition.x, camera.WorldPosition.y, camera.WorldPosition.z }, targetInches = new[] { targetPosition.x, targetPosition.y, targetPosition.z }, camera.Orthographic, camera.OrthographicHeight, camera.ZNear, camera.ZFar, camera.EnablePostProcessing, debugMode = camera.DebugMode.ToString() };
   using (capture.Scene.Push())
   {
    renderer.GameObject.Destroy();
    capture.Scene.EditorTick(0, 0);
    capture.Scene.ProcessDeletes();
   }
   renderer = null;
   var deleted = await SettledEmpty("empty_after_delete");
   Assert.IsTrue(empty.SequenceEqual(deleted), "Source deletion retained foreground.");
   Measurements.Add(new { phase = "source_front_presentation", width, height, sourceSha256 = Hash(source), firstPath, firstHash, finalPath, finalHash, rows, settled = true, coverage, foregroundBounds = new[] { minX, minY, maxX, maxY }, nearWhiteRgb245Pixels = nearWhite, drawCalls, materialNames, observedCamera, nativeCaptureSubmitted = true, callbackErrors = Array.Empty<string>(),
    recipe = new { space = "gltf-metres", position = new[] { 0f, 2.5f, 12f }, target = new[] { 0f, 1.1f, 0f }, projection = "orthographic", orthographicHeight = 4.15f, background = new[] { .2f, .2f, .3f, 1f }, lightDirection = new[] { 0f, -.3f, -1f }, lightIntensity = .75f, tonemapping = "ACES", minimumExposure = 1, maximumExposure = 1, exposureCompensationEv = -.5f },
    scope = "Actual source-only public Model.Load/ModelRenderer, inferred front orthographic camera, lower directional light, fixed native exposure, exact reference dimensions, four zero-delta RTT pairs and deletion. Source materials unchanged. Canonical functional proof remains separate. Unknown upstream lighting/exposure; user appearance approval pending." });
  }
  finally
  {
   await MainThread.Wait();
   using (capture.Scene.Push()) { renderer?.GameObject.Destroy(); capture.Scene.EditorTick(0, 0); capture.Scene.ProcessDeletes(); }
   light?.Delete();
   model?.Destroy();
   if (mount is not null) { FileSystem.Mounted.UnMount(mount); mount.Dispose(); }
   Directory.Delete(directory, true);
   Assert.AreEqual("49e06672900df95593040d35bbc7a2ee5921ae8d46edc44b64ccf2df65e64849", Hash(File.ReadAllBytes(sourcePath)));
  }

  async Task<Color32[]> SettledEmpty(string label)
  {
   Color32[] previous = null;
   var rows = new List<object>();
   int stablePairs = 0;
   for (int frame = 0; frame < 24; frame++)
   {
    var current = await Render();
    bool unchanged = previous is not null && current.SequenceEqual(previous);
    stablePairs = unchanged ? stablePairs + 1 : 0;
    rows.Add(new { render = frame + 1, engineFrame = Application.FrameCount, unchangedFromPrevious = unchanged, stablePairs });
    if (stablePairs >= 4)
    {
     var saved = Save(label);
     Measurements.Add(new { phase = "empty_rtt_convergence", label, settled = true, saved.Path, saved.Sha256, rows });
     return current;
    }
    previous = current;
   }
   Measurements.Add(new { phase = "empty_rtt_convergence", label, settled = false, rows });
   throw new InvalidOperationException("Empty native RTT presentation did not settle.");
  }

  async Task<Color32[]> Render()
  {
   await capture.WaitFrames();
   using (capture.Scene.Push())
   {
    capture.Scene.PreCameraRender();
    capture.Camera.InitializeRendering(false);
    Assert.AreEqual(new global::Vector2(width, height), capture.Camera.CustomSize.Value);
    Assert.IsTrue(capture.Camera.RenderToTexture(target));
   }
   await capture.WaitFrames();
   Measurements.Add(new { phase = "rtt_exposure_observation", engineFrame = Application.FrameCount, enabled = capture.Camera.AutoExposure.Enabled, minimum = capture.Camera.AutoExposure.MinimumExposure, maximum = capture.Camera.AutoExposure.MaximumExposure, compensationEv = capture.Camera.AutoExposure.Compensation });
   Assert.IsTrue(capture.Camera.AutoExposure.Enabled && capture.Camera.AutoExposure.MinimumExposure == 1 && capture.Camera.AutoExposure.MaximumExposure == 1 && capture.Camera.AutoExposure.Compensation == -.5f, "Explicit native exposure settings changed.");
   var pixels = new Color32[width * height];
   target.GetPixels<Color32>((0, 0, width, height), 0, 0, pixels, ImageFormat.RGBA8888);
   Assert.IsTrue(pixels.All(pixel => !(pixel.r == 255 && pixel.g == 0 && pixel.b == 255)));
   return pixels;
  }

  (string Path, string Sha256) Save(string label)
  {
   using var bitmap = target.GetBitmap();
   byte[] png = bitmap.ToPng();
   var path = Path.Combine(OutputDirectory, label + ".png");
   File.WriteAllBytes(path, png);
   return (path, Hash(png));
  }
 }
}
