using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;

namespace Sandbox.Tests.Gltf;

[TestClass]
public sealed class GltfCompilerImageTests
{
 private static byte[] Fixture(string suffix)
 {
  var assembly=typeof(GltfCompilerImageTests).Assembly;
  using var input=assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(x=>x.EndsWith(suffix,StringComparison.Ordinal)))!;
  using var output=new MemoryStream();input.CopyTo(output);return output.ToArray();
 }
 [TestMethod]
 public void EmbeddedImagesRetainExactBytesRolesAndUnknownMetadataWithoutResolver()
 {
  var png=Fixture("compiler.png");var jpeg=Fixture("compiler.jpg");
  var source=Encoding.UTF8.GetBytes("""
  {"asset":{"version":"2.0"},"extras":{"keep":"opaque"},"extensionsUsed":["EXT_unknown"],"extensions":{"EXT_unknown":{"image":1}},
   "images":[{"uri":"textures/a%20b.png","name":"A"},{"uri":"data:image/jpeg;base64,JPEG_PLACEHOLDER","name":"B"}],
   "samplers":[{"wrapS":33071}],"textures":[{"source":0,"sampler":0},{"source":1}],
   "materials":[{"pbrMetallicRoughness":{"baseColorTexture":{"index":1}}}]}
  """.Replace("JPEG_PLACEHOLDER",Convert.ToBase64String(jpeg)));
  var output=GltfCompiler.CompileWithEmbeddedImages(source,uri=>uri=="textures/a%20b.png"?png:throw new InvalidOperationException(uri));
  using var document=GltfReader.Read(output);
  CollectionAssert.AreEqual(png,document.ReadImage(0).Bytes);CollectionAssert.AreEqual(jpeg,document.ReadImage(1).Bytes);
  Assert.AreEqual("image/png",document.ReadImage(0).MimeType);Assert.AreEqual("image/jpeg",document.ReadImage(1).MimeType);
  var root=document.Root;Assert.AreEqual("opaque",root.GetProperty("extras").GetProperty("keep").GetString());
  Assert.AreEqual(1,root.GetProperty("extensions").GetProperty("EXT_unknown").GetProperty("image").GetInt32());
  Assert.AreEqual(0,document.ReadTexture(0).Image);Assert.AreEqual(0,document.ReadTexture(0).Sampler);
  Assert.AreEqual(1,document.ReadMaterial(0).BaseColorTexture!.Index);
  for(int i=0;i<2;i++)
  {
   var image=root.GetProperty("images")[i];Assert.IsFalse(image.TryGetProperty("uri",out _));
   var view=root.GetProperty("bufferViews")[image.GetProperty("bufferView").GetInt32()];Assert.AreEqual(0,view.GetProperty("byteOffset").GetInt32()%4);
  }
  CollectionAssert.AreEqual(output,GltfCompiler.CompileWithEmbeddedImages(output));
 }
 [TestMethod]
 public void RequiredBasisImageRemainsSelectedAndByteIdentical()
 {
  var ktx=Fixture("gltf-ktx-cyan_rgb_reference_basis.ktx2");
  var source=Encoding.UTF8.GetBytes("""
  {"asset":{"version":"2.0"},"extensionsUsed":["KHR_texture_basisu"],"extensionsRequired":["KHR_texture_basisu"],
   "images":[{"uri":"a.ktx2"}],"textures":[{"extensions":{"KHR_texture_basisu":{"source":0}}}]}
  """);
  var supported=new System.Collections.Generic.HashSet<string>{"KHR_texture_basisu"};
  using var document=GltfReader.Read(GltfCompiler.CompileWithEmbeddedImages(source,_=>ktx,supported),supportedExtensions:supported);
  Assert.AreEqual(0,document.ReadTexture(0).Image);Assert.AreEqual("image/ktx2",document.ReadImage(0).MimeType);
  CollectionAssert.AreEqual(ktx,document.ReadImage(0).Bytes);
 }
 [TestMethod]
 public void MissingUnknownSignatureAndMimeMismatchFail()
 {
  var source=Encoding.UTF8.GetBytes("""{"asset":{"version":"2.0"},"images":[{"uri":"missing.png"}]}""");
  Assert.ThrowsException<GltfException>(()=>GltfCompiler.CompileWithEmbeddedImages(source));
  Assert.ThrowsException<GltfException>(()=>GltfCompiler.CompileWithEmbeddedImages(source,_=>[1,2,3]));
  var mismatch=Encoding.UTF8.GetBytes("""{"asset":{"version":"2.0"},"images":[{"uri":"a.png","mimeType":"image/jpeg"}]}""");
  Assert.ThrowsException<GltfException>(()=>GltfCompiler.CompileWithEmbeddedImages(mismatch,_=>Fixture("compiler.png")));
 }
 [TestMethod]
 public void ImageLevelUnknownExtensionsAreNotSilentlyRewritten()
 {
  var source=Encoding.UTF8.GetBytes("""{"asset":{"version":"2.0"},"images":[{"uri":"a.png","extensions":{"EXT_unknown":{"uri":"a.png"}}}]}""");
  Assert.ThrowsException<GltfException>(()=>GltfCompiler.CompileWithEmbeddedImages(source,_=>Fixture("compiler.png")));
 }
 [TestMethod]
 public void GeometryAndImageShareOnePackagingBudget()
 {
  var source=Encoding.UTF8.GetBytes("""{"asset":{"version":"2.0"},"buffers":[{"uri":"x.bin","byteLength":20}],"images":[{"uri":"a.png"}]}""");
  var png=Fixture("compiler.png");byte[] Resolve(string uri)=>uri=="x.bin"?new byte[20]:png;
  long imageCharge=12L*((png.Length+3)&~3);
  Assert.ThrowsException<GltfException>(()=>GltfCompiler.CompileWithEmbeddedImages(source,Resolve,maxBufferBytes:imageCharge+79));
  using var document=GltfReader.Read(GltfCompiler.CompileWithEmbeddedImages(source,Resolve,maxBufferBytes:imageCharge+80));
  CollectionAssert.AreEqual(png,document.ReadImage(0).Bytes);
 }
 [TestMethod]
 public void ConflictingOrMalformedImageAndViewTypesFailAsGltfExceptions()
 {
  var both=Encoding.UTF8.GetBytes("""{"asset":{"version":"2.0"},"images":[{"uri":"a.png","bufferView":0}]}""");
  Assert.ThrowsException<GltfException>(()=>GltfCompiler.CompileWithEmbeddedImages(both,_=>Fixture("compiler.png")));
  var view=Encoding.UTF8.GetBytes("""{"asset":{"version":"2.0"},"bufferViews":{},"images":[{"bufferView":0,"mimeType":"image/png"}]}""");
  Assert.ThrowsException<GltfException>(()=>GltfCompiler.CompileWithEmbeddedImages(view));
  var image=Encoding.UTF8.GetBytes("""{"asset":{"version":"2.0"},"images":[12]}""");
  Assert.ThrowsException<GltfException>(()=>GltfCompiler.CompileWithEmbeddedImages(image));
 }
 [TestMethod]
 public void OrphanBinaryAndImageRemainDistinct()
 {
  var json=Encoding.UTF8.GetBytes("""{"asset":{"version":"2.0"},"images":[{"uri":"a.png"}]}""");
  int jsonLength=(json.Length+3)&~3;var source=new byte[12+8+jsonLength+8+4];
  BinaryPrimitives.WriteUInt32LittleEndian(source,0x46546c67);BinaryPrimitives.WriteUInt32LittleEndian(source.AsSpan(4),2);
  BinaryPrimitives.WriteUInt32LittleEndian(source.AsSpan(8),(uint)source.Length);BinaryPrimitives.WriteUInt32LittleEndian(source.AsSpan(12),(uint)jsonLength);
  BinaryPrimitives.WriteUInt32LittleEndian(source.AsSpan(16),0x4e4f534a);source.AsSpan(20,jsonLength).Fill(32);json.CopyTo(source,20);
  int bin=20+jsonLength;BinaryPrimitives.WriteUInt32LittleEndian(source.AsSpan(bin),4);BinaryPrimitives.WriteUInt32LittleEndian(source.AsSpan(bin+4),0x004e4942);
  source[bin+8]=1;source[bin+9]=2;source[bin+10]=3;source[bin+11]=4;
  var png=Fixture("compiler.png");var output=GltfCompiler.CompileWithEmbeddedImages(source,_=>png);
  int outputBin=20+(int)BinaryPrimitives.ReadUInt32LittleEndian(output.AsSpan(12));
  CollectionAssert.AreEqual(new byte[]{1,2,3,4},output.AsSpan(outputBin+8,4).ToArray());
  using var document=GltfReader.Read(output);CollectionAssert.AreEqual(png,document.ReadImage(0).Bytes);
  CollectionAssert.AreEqual(output,GltfCompiler.CompileWithEmbeddedImages(output));
 }
}
