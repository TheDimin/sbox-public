using System;
using System.IO;
using Sandbox.Engine;
using Sandbox.Gltf;

namespace Sandbox.Gltf.DirectGpu.UnlitTestValidation;

[TestClass]
public sealed class UnlitTestPublicLoadTests
{
 public static string CheckoutRoot { get; set; }
 [DataTestMethod]
 [DataRow("glTF", false)]
 [DataRow("glTF", true)]
 [DataRow("glTF-Binary", false)]
 [DataRow("glTF-Binary", true)]
 public void PublicModelLoadsAuthoredMaterials(string variant, bool compiled)
 {
  var sourceDir = Path.Combine(CheckoutRoot, "Temp/gltf-reference/glTF-Sample-Assets/Models/UnlitTest", variant);
  var name = variant == "glTF" ? "UnlitTest.gltf" : "UnlitTest.glb";
  var source = File.ReadAllBytes(Path.Combine(sourceDir, name));
  byte[] Resolve(string uri) => File.ReadAllBytes(Path.Combine(sourceDir, uri));
  var bytes = compiled ? GltfCompiler.Compile(source, Resolve, UnlitTestReference.Selected) : source;
  var directory = Path.Combine(Path.GetTempPath(), "unlit_public_" + Guid.NewGuid().ToString("N"));
  var prefix = "unlit_public_" + Guid.NewGuid().ToString("N");
  var relative = prefix + "/model" + (compiled ? ".glb_c" : variant == "glTF" ? ".gltf" : ".glb");
  BaseFileSystem aggregate = null; LocalFileSystem mount = null; Model model = null;
  try
  {
   Directory.CreateDirectory(Path.Combine(directory, prefix)); File.WriteAllBytes(Path.Combine(directory, relative), bytes);
   if (!compiled && variant == "glTF") foreach (var dep in new[]{"UnlitTest.bin"}) File.Copy(Path.Combine(sourceDir, dep), Path.Combine(directory, prefix, dep));
   aggregate = Sandbox.FileSystem.Mounted; mount = new LocalFileSystem(directory); aggregate.Mount(mount);
   model = Model.Load(relative);
   Assert.IsTrue(model is { IsValid: true, IsError: false } && model.native.IsNull && model.GltfResource is not null, "Public Model.Load must create the native-free resource with authored materials.");
   Assert.AreEqual(2, model.Materials.Length, "All authored material slots must realize.");
   foreach (var material in model.Materials) Assert.IsTrue(material is { IsValid: true, IsError: false }, "Authored material must realize.");
  }
  finally { if (model is not null && model.ResourcePath == Resource.FixPath(relative)) model.Destroy(); if (mount is not null) { aggregate.UnMount(mount); mount.Dispose(); } if (Directory.Exists(directory)) Directory.Delete(directory, true); }
 }
}
