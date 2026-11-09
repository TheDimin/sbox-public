using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;

namespace Sandbox.Gltf.DirectGpu.BrainStemValidation;

[TestClass]
public sealed class BrainStemMeshoptTests
{
 public static string CheckoutRoot { get; set; }
 private static readonly IReadOnlySet<string> Extensions = new HashSet<string>(StringComparer.Ordinal) { "KHR_mesh_quantization", "KHR_meshopt_compression", "EXT_meshopt_compression" };
 [DataTestMethod]
 [DataRow("glTF-Meshopt",false)] [DataRow("glTF-Meshopt",true)] [DataRow("glTF-Meshopt-EXT",false)] [DataRow("glTF-Meshopt-EXT",true)]
 public void PinnedCompressedStreamsRetainFileDefinedFormats(string variant,bool compiled)
 {
  string corpus=Path.Combine(CheckoutRoot,"Temp/gltf-reference/glTF-Sample-Assets"); using var oracle=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(CheckoutRoot,"Temp/gltf-validation/direct-gpu/BrainStem/reference.json"))); var asset=oracle.RootElement.GetProperty("assets").GetProperty(variant); string path=Path.Combine(corpus,asset.GetProperty("relative").GetString()); byte[] bytes=File.ReadAllBytes(path);
  string Hash(byte[] b)=>Convert.ToHexStringLower(SHA256.HashData(b)); Assert.AreEqual(asset.GetProperty("sha256").GetString(),Hash(bytes)); foreach(var dep in asset.GetProperty("dependencies").EnumerateObject()) Assert.AreEqual(dep.Value.GetString(),Hash(File.ReadAllBytes(Path.Combine(corpus,dep.Name))));
  byte[] Resolve(string uri)=>File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(path),Uri.UnescapeDataString(uri)));
  if(compiled) bytes=GltfCompiler.Compile(bytes,Resolve,Extensions); using var doc=GltfReader.Read(bytes,Resolve,Extensions);
  Assert.AreEqual(49,doc.Root.GetProperty("meshes")[0].GetProperty("primitives").GetArrayLength()); Assert.AreEqual(13,doc.Root.GetProperty("animations")[0].GetProperty("channels").GetArrayLength());
  for(int i=0;i<49;i++) { var u=doc.MaterializePrimitiveUpload(0,i); var pos=u.Attributes["POSITION"]; var norm=u.Attributes["NORMAL"]; var joints=u.Attributes["JOINTS_0"]; var weights=u.Attributes["WEIGHTS_0"]; Assert.AreEqual(5126,pos.ComponentType); Assert.IsFalse(pos.Normalized); Assert.AreEqual(5120,norm.ComponentType); Assert.IsTrue(norm.Normalized); Assert.AreEqual(5121,joints.ComponentType); Assert.IsFalse(joints.Normalized); Assert.AreEqual(5121,weights.ComponentType); Assert.IsTrue(weights.Normalized); Assert.AreEqual(pos.Count,norm.Count); Assert.AreEqual(pos.Count,joints.Count); Assert.AreEqual(pos.Count,weights.Count); Assert.IsTrue(u.Indices.Count>0 && u.Indices.Count%3==0); }
 }
}
