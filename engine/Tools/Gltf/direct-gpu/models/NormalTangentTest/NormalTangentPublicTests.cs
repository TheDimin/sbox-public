using System;
using System.IO;
using Sandbox.Engine;
using Sandbox.Gltf;

namespace Sandbox.Gltf.DirectGpu.NormalTangentTestValidation;

[TestClass]
public sealed class NormalTangentPublicTests
{
 public static string CheckoutRoot { get; set; }
 [TestMethod]
 public void IndependentGeneratedCornerOracle() => Assert.AreEqual( 7, GeneratedTangentProducerTests.Run() );

 [DataTestMethod]
 [DataRow("glTF", false)]
 [DataRow("glTF", true)]
 [DataRow("glTF-Binary", false)]
 [DataRow("glTF-Binary", true)]
 public void PublicModelLoadsSourceMaterial( string variant, bool compiled )
 {
  var sourceDir = Path.Combine( CheckoutRoot, "Temp/gltf-reference/glTF-Sample-Assets/Models/NormalTangentTest", variant );
  var name = variant == "glTF" ? "NormalTangentTest.gltf" : "NormalTangentTest.glb";
  var source = File.ReadAllBytes( Path.Combine( sourceDir, name ) );
  byte[] Resolve( string uri ) => File.ReadAllBytes( Path.Combine( sourceDir, uri ) );
  var bytes = compiled ? GltfCompiler.Compile( source, Resolve ) : source;
  var directory = Path.Combine( Path.GetTempPath(), "normal_tangent_public_" + Guid.NewGuid().ToString("N") );
  var prefix = "normal_tangent_" + Guid.NewGuid().ToString("N"); var folder = Path.Combine( directory, prefix );
  var relative = prefix + "/model" + (compiled ? ".glb_c" : variant == "glTF" ? ".gltf" : ".glb");
  BaseFileSystem aggregate = null; LocalFileSystem mount = null; Model model = null;
  try
  {
   Directory.CreateDirectory( folder ); File.WriteAllBytes( Path.Combine(directory, relative), bytes );
   if ( !compiled && variant == "glTF" ) foreach ( var dep in new[]{"NormalTangentTest0.bin", "NormalTangentTest_BaseColor.png", "NormalTangentTest_Normal.png", "NormalTangentTest_OcclusionRoughnessMetallic.png"} ) File.Copy( Path.Combine(sourceDir,dep), Path.Combine(folder,dep) );
   aggregate = Sandbox.FileSystem.Mounted; mount = new LocalFileSystem(directory); aggregate.Mount(mount);
   model = Model.Load(relative);
   Assert.IsTrue( model is { IsValid: true, IsError: false } && model.native.IsNull && model.GltfResource is not null, "Public Model.Load must produce a valid native-free managed resource with its normal and occlusion material." );
   Assert.IsTrue( model.Materials.Length > 0 && model.Materials[0] is { IsValid: true, IsError: false }, "Source material must realize." );
  }
  finally { if ( model is not null && model.ResourcePath == Resource.FixPath(relative) ) model.Destroy(); if ( mount is not null ) { aggregate.UnMount(mount); mount.Dispose(); } if (Directory.Exists(directory)) Directory.Delete(directory,true); }
 }
}
