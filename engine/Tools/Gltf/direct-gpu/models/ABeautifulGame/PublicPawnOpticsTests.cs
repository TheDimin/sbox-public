using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox;
using Sandbox.Gltf;
using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace Sandbox.Gltf.DirectGpu.ABeautifulGameValidation;

[TestClass]
internal sealed class PublicPawnOpticsTests
{
 const string Transmission = "KHR_materials_transmission";
 const string Volume = "KHR_materials_volume";
 static string checkout;
 public static List<object> Measurements { get; } = new();

 public static void Configure(string root)
 {
  checkout = root;
  Measurements.Clear();
 }

 [TestMethod]
 public async Task SourceAndCanonicalRenderAuthoredPawnsWithIndependentFallbackControl()
 {
  await MainThread.Wait();
  string modelDir = Path.Combine(checkout, "Temp/gltf-reference/glTF-Sample-Assets/Models/ABeautifulGame/glTF");
  byte[] source = File.ReadAllBytes(Path.Combine(modelDir, "ABeautifulGame.gltf"));
  Assert.AreEqual("2e1b63ae34173c6e3251f749b1b8b97a32e92517aab2cfd4bfffd9158f4dd278", Hash(source));
  byte[] Resolve(string uri) => File.ReadAllBytes(Path.Combine(modelDir, uri));
  byte[] canonical = GltfCompiler.Compile(source, Resolve);
  Assert.AreEqual("aa0e45833d5d39e5c9a81658d19588ce91d88356509bfb9755dce88599eeca33", Hash(canonical));

  // A separate negative fixture removes only the optical extension objects.
  // Both positive inputs and every dependency are staged from original bytes.
  var fallback = JsonNode.Parse(source);
  foreach (int index in new[] { 5, 7 })
  {
   var extensions = fallback["materials"][index]["extensions"].AsObject();
   Assert.IsTrue(extensions.Remove(Transmission) && extensions.Remove(Volume));
  }
  fallback.AsObject().Remove("extensionsUsed");
  byte[] fallbackBytes = JsonSerializer.SerializeToUtf8Bytes(fallback);

  string output = Path.Combine(checkout, "Temp/gltf-validation/direct-gpu/ABeautifulGame/pawn-optics", Guid.NewGuid().ToString("N"));
  string temporary = Path.Combine(Path.GetTempPath(), "gltf_pawn_optics_" + Guid.NewGuid().ToString("N"));
  string bundle = Path.GetFileName(temporary);
  Directory.CreateDirectory(output);
  Directory.CreateDirectory(Path.Combine(temporary, bundle));
  using var environment = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(checkout,
   "research/gltf-validation/direct-gpu/models/ABeautifulGame/reference-env-config.json")));
  foreach (var prefix in new[] { "source", "compiled" })
   Assert.AreEqual(environment.RootElement.GetProperty(prefix + "Sha256").GetString(),
    Hash(File.ReadAllBytes(environment.RootElement.GetProperty(prefix + "Path").GetString())));
  LocalFileSystem mount = null;
  try
  {
   foreach (var file in Directory.GetFiles(modelDir))
   {
    if (Path.GetExtension(file) is not (".jpg" or ".bin")) continue;
    File.Copy(file, Path.Combine(temporary, bundle, Path.GetFileName(file)));
   }
   File.WriteAllBytes(Path.Combine(temporary, bundle, "source.gltf"), source);
   File.WriteAllBytes(Path.Combine(temporary, bundle, "canonical.glb_c"), canonical);
   File.WriteAllBytes(Path.Combine(temporary, bundle, "fallback.gltf"), fallbackBytes);
   mount = new LocalFileSystem(temporary);
   FileSystem.Mounted.Mount(mount);
   var sourceFrame = await Render(output, bundle + "/source.gltf", "source", true);
   var compiledFrame = await Render(output, bundle + "/canonical.glb_c", "canonical", true);
   var fallbackFrame = await Render(output, bundle + "/fallback.gltf", "fallback", false);
   // Interior white/black marble locations from the reviewed 800x620 framing.
   var roiCenters = new[] { (X: 552, Y: 280), (X: 723, Y: 185) };
   foreach (var center in roiCenters)
   {
    var roi = Enumerable.Range(center.Y - 6, 13)
     .SelectMany(y => Enumerable.Range(center.X - 6, 13).Select(x => y * 800 + x)).ToArray();
    var sourceDelta = Delta(sourceFrame, fallbackFrame, roi);
    var compiledDelta = Delta(compiledFrame, fallbackFrame, roi);
    var pairDelta = Delta(sourceFrame, compiledFrame, roi);
    Measurements.Add(new { phase = "pawn_roi", center, sourceDelta, compiledDelta, pairDelta });
    Assert.IsTrue(sourceDelta > .015 && compiledDelta > .015, "Enabling authored optics did not change the pawn interior.");
    Assert.IsTrue(pairDelta < Math.Min(sourceDelta, compiledDelta) * .5, "Source and canonical optical appearances disagree within the pawn interior.");
   }
   Measurements.Add(new { phase = "inputs", source = Hash(source), canonical = Hash(canonical), fallback = Hash(fallbackBytes) });
  }
  finally
  {
   await MainThread.Wait();
   try { if (mount is not null) FileSystem.Mounted.UnMount(mount); }
   finally { mount?.Dispose(); Directory.Delete(temporary, true); }
  }
 }

 static async Task<Capture.Frame> Render(string output, string path, string label, bool optical)
 {
  using var capture = new Capture(output);
  Model model = null;
  SceneCubemap probe = null;
  ModelRenderer renderer = null;
  try
  {
   model = Model.Load(path);
   Assert.IsTrue(model is { IsValid: true, IsError: false } && model.native.IsNull);
   Assert.AreEqual(49, model.MeshCount);
   Assert.AreEqual(15, model.Materials.Length);
   var slots = model.GltfResource.Asset.MaterialSlotSourceIndices;
   foreach (var index in new[] { 5, 7 })
   {
    int slot = slots.ToList().IndexOf(index);
    Assert.IsTrue(slot >= 0);
    var material = model.Materials[slot];
    Assert.IsTrue(material is { IsValid: true, IsError: false });
    if (optical)
    {
     Assert.AreEqual("shaders/gltf_optical.shader", material.ShaderName);
     Assert.AreEqual(1f, material.GetVector4("g_flGltfOpticalTransmissionFactor").x);
     Assert.AreEqual(.22f, material.GetVector4("g_flGltfVolumeThicknessMeshMeters").x);
     Assert.AreEqual(0f, material.GetVector4("g_flGltfAttenuationDistanceInches").x);
     Assert.IsTrue(material.Flags.IsTranslucent && material.Flags.GetInt("bWantsFBCopyTexture") != 0);
     Assert.AreEqual(.8f, material.GetVector4("g_vGltfAttenuationColor").x);
     foreach (var role in new[] { "MetallicRoughness", "Normal" })
     {
      Assert.AreEqual(1f, material.GetVector4("g_flGltfHas" + role + "Texture").x);
      Assert.AreEqual(0f, material.GetVector4("g_flGltf" + role + "UvSlot").x);
      Assert.IsTrue(material.GetTexture("g_tGltf" + role + "Texture") is { IsValid: true, IsError: false, Width: 2048, Height: 2048 });
     }
     Assert.AreEqual(0f, material.GetVector4("g_flGltfHasOpticalTransmissionTexture").x);
     Assert.AreEqual(0f, material.GetVector4("g_flGltfHasVolumeThicknessTexture").x);
    }
    else Assert.IsFalse(material.Flags.IsTranslucent);
   }
   var texture = Texture.Load("materials/gltf_reference_environment/aerodynamics_workshop.vtex");
   Assert.IsTrue(texture is { IsValid: true, IsError: false } && texture.Depth == 6 && texture.Mips > 1);
   capture.View(new Vector3(-15.74548f,-12.19278f,4.90470f),new Vector3(1.22496f,-2.72452f,.17646f),38.54507f);
   probe = new SceneCubemap(capture.Scene.SceneWorld, texture, BBox.FromPositionAndSize(Vector3.Zero, 1000));
   probe.Projection = SceneCubemap.ProjectionMode.Sphere;
   probe.Transform = Transform.Zero.WithRotation(Rotation.FromYaw(270));
   probe.TintColor = Color.White;
   probe.Feathering = .01f;
   probe.RenderDirty();
   Assert.IsTrue(probe.IsValid());
   using (capture.Scene.Push())
   {
    renderer = capture.Scene.CreateObject().Components.Create<ModelRenderer>(false);
    renderer.Model = model;
    renderer.Enabled = true;
    capture.Scene.EditorTick(0, 0);
   }
   var instance = renderer.GltfInstance;
   Assert.IsNotNull(instance);
   Assert.AreEqual(optical, instance.SceneObject.Flags.WantsFrameBufferCopy);
   Capture.Frame prior = null;
   Capture.Frame frame = null;
   for (int i = 1; i <= 17; i++)
   {
    await capture.Settle();
    prior = frame;
    frame = await capture.Take(label + (i < 17 ? "_warmup_" + i : ""));
   }
   Assert.IsTrue(instance.DrawCalls > 0);
   Assert.AreEqual(0, instance.CallbackErrors.Length, string.Join("\n", instance.CallbackErrors));
   var roi = Enumerable.Range(0, frame.Pixels.Length).ToArray();
   var drift = Delta(frame, prior, roi);
   Measurements.Add(new { phase = "presentation", label, frame.Path, sha256 = frame.Sha, frame_drift = drift, exposure = capture.VerifyExposure(), optical });
   Assert.IsTrue(drift < .01, "The capture was still changing after the recorded warmup.");
   return frame;
  }
  finally
  {
   await MainThread.Wait();
   if (renderer is not null)
   {
    using var scope = capture.Scene.Push();
    renderer.GameObject.Destroy();
    capture.Scene.EditorTick(0, 0);
    capture.Scene.ProcessDeletes();
   }
   if (probe.IsValid()) probe.Delete();
   model?.Destroy();
  }
 }

 static double Delta(Capture.Frame a, Capture.Frame b, int[] roi) =>
  roi.Average(i => (Math.Abs(a.Pixels[i].r - b.Pixels[i].r) + Math.Abs(a.Pixels[i].g - b.Pixels[i].g) + Math.Abs(a.Pixels[i].b - b.Pixels[i].b)) / 3.0);

 static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
 sealed class Capture:IDisposable
 {
  public record Frame(Color[] Pixels,string Path,string Sha);public readonly Scene Scene;readonly CameraComponent camera;readonly Tonemapping tone;readonly Editor.SceneRenderingWidget widget;readonly string output;readonly Action onFrame;int frames;
  public Capture(string path){output=path;Scene=Scene.CreateEditorScene();using(Scene.Push()){camera=Scene.CreateObject().Components.Create<CameraComponent>();camera.Orthographic=false;camera.FovAxis=CameraComponent.Axis.Vertical;camera.ZNear=.1f;camera.ZFar=1000;camera.CustomSize=new Vector2(800,620);camera.BackgroundColor=new Color(.65f,.62f,.58f,1);camera.ClearFlags=ClearFlags.All;camera.EnablePostProcessing=true;camera.DebugMode=SceneCameraDebugMode.Normal;tone=camera.GameObject.Components.Create<Tonemapping>();tone.Mode=Tonemapping.TonemappingMode.ACES;tone.AutoExposureEnabled=true;tone.MinimumExposure=1;tone.MaximumExposure=1;tone.ExposureCompensation=2;Scene.EditorTick(0,0);}widget=new Editor.SceneRenderingWidget{Scene=Scene,Camera=camera,WindowTitle="A Beautiful Game reference reconstruction",Size=new Vector2(800,620)};onFrame=()=>frames++;widget.OnPreFrame+=onFrame;widget.Show();}
  public object VerifyExposure(){var e=camera.AutoExposure;Assert.IsTrue(e.Enabled && Math.Abs(e.MinimumExposure-1)<.0001f && Math.Abs(e.MaximumExposure-1)<.0001f && Math.Abs(e.Compensation-2)<.0001f,"ACES exposure settings were overwritten at render time");return new{e.Enabled,e.MinimumExposure,e.MaximumExposure,e.Compensation,e.Rate,scope="Camera settings observed after render; not photometric proof of Babylon tone-curve equivalence"};}
  public void View(Vector3 pos,Vector3 target,float fov){using(Scene.Push()){camera.WorldPosition=pos;camera.WorldRotation=Rotation.LookAt(target-pos,Vector3.Up);camera.FieldOfView=fov;Scene.EditorTick(0,0);}}
  async Task Wait(){await MainThread.Wait();var start=frames;var timer=Stopwatch.StartNew();while(frames<start+3||Graphics.IsActive){Assert.IsTrue(timer.Elapsed<TimeSpan.FromSeconds(15),"Preview frames stalled");await Task.Delay(20);await MainThread.Wait();}}
  public async Task Settle(){for(int i=0;i<20;i++)await Wait();}
  public async Task<Frame> Take(string name){await Wait();using(Scene.Push()){Scene.PreCameraRender();camera.InitializeRendering(false);}using var target=Texture.CreateRenderTarget().WithSize(800,620).WithFormat(ImageFormat.RGBA8888).WithInitialColor(Color.Magenta).Create("beautiful_reference_"+Guid.NewGuid().ToString("N"));bool ok;using(Scene.Push()){target.Clear(Color.Magenta);ok=camera.RenderToTexture(target);}Assert.IsTrue(ok);await Wait();using var bitmap=target.GetBitmap();var pixels=bitmap.GetPixels();Assert.AreEqual(800*620,pixels.Length);byte[] png=bitmap.ToPng();string path=Path.Combine(output,name+".png");File.WriteAllBytes(path,png);return new(pixels,path,Hash(png));}
  public void Dispose(){if(widget.IsValid){widget.OnPreFrame-=onFrame;widget.Close();widget.Destroy();}Scene.Destroy();}
 }
}
