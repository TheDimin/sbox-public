using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;
using V3 = System.Numerics.Vector3;

namespace Sandbox.Gltf.DirectGpu.BrainStemValidation;

[TestClass]
public sealed class BrainStemReferenceTests
{
 public static string CheckoutRoot { get; set; }
 private static string Corpus => Path.Combine(CheckoutRoot, "Temp/gltf-reference/glTF-Sample-Assets");
 private static JsonDocument Oracle() => JsonDocument.Parse(File.ReadAllBytes(Path.Combine(CheckoutRoot,"Temp/gltf-validation/direct-gpu/BrainStem/reference.json")));
 private static string Hash(byte[] x) => Convert.ToHexStringLower(SHA256.HashData(x));
 private static byte[] Resolve(string path,string uri) => File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(path),Uri.UnescapeDataString(uri)));
 private static byte[] Tight(GltfUploadStream s) { int size=s.ComponentType switch {5120 or 5121=>1,5122 or 5123=>2,5125 or 5126=>4,_=>throw new Exception("component")}; int n=size*s.Components; var b=new byte[checked(s.Count*n)]; for(int i=0;i<s.Count;i++) s.Storage.Bytes.Span.Slice(s.ByteOffset+i*s.ByteStride,n).CopyTo(b.AsSpan(i*n)); return b; }
 [DataTestMethod]
 [DataRow("glTF",false)] [DataRow("glTF",true)] [DataRow("glTF-Embedded",false)] [DataRow("glTF-Embedded",true)] [DataRow("glTF-Binary",false)] [DataRow("glTF-Binary",true)]
 public void CoreSourceAndCompiledMatchIndependentDataAndPose(string variant,bool compiled)
 {
  using var oracle=Oracle(); var asset=oracle.RootElement.GetProperty("assets").GetProperty(variant); var reference=oracle.RootElement.GetProperty("variants").GetProperty(variant); var path=Path.Combine(Corpus,asset.GetProperty("relative").GetString()); var bytes=File.ReadAllBytes(path);
  Assert.AreEqual(asset.GetProperty("sha256").GetString(),Hash(bytes)); foreach(var dep in asset.GetProperty("dependencies").EnumerateObject()) Assert.AreEqual(dep.Value.GetString(),Hash(File.ReadAllBytes(Path.Combine(Corpus,dep.Name))));
  if(compiled) bytes=GltfCompiler.Compile(bytes,uri=>Resolve(path,uri));
  using var doc=GltfReader.Read(bytes,uri=>Resolve(path,uri)); Assert.AreEqual(59,doc.Root.GetProperty("meshes")[0].GetProperty("primitives").GetArrayLength());
  var primitives=reference.GetProperty("primitives");
  for(int p=0;p<59;p++) { var upload=doc.MaterializePrimitiveUpload(0,p); var expected=primitives[p]; foreach(var a in upload.Attributes) { var e=expected.GetProperty("attributes").GetProperty(a.Key); Assert.AreEqual(e.GetProperty("raw_sha256").GetString(),Hash(Tight(a.Value)), $"primitive {p} {a.Key}"); Assert.AreEqual(e.GetProperty("component_type").GetInt32(),a.Value.ComponentType); Assert.AreEqual(e.GetProperty("normalized").GetBoolean(),a.Value.Normalized); } Assert.AreEqual(expected.GetProperty("indices").GetProperty("raw_sha256").GetString(),Hash(Tight(upload.Indices)), $"indices {p}"); }
  foreach(var sample in reference.GetProperty("samples").EnumerateArray()) { var t=sample.GetProperty("time"); var pose=t.ValueKind==JsonValueKind.Null?doc.EvaluatePose():doc.EvaluatePose(0,t.GetSingle()); for(int p=0;p<59;p++) foreach(var row in sample.GetProperty("primitives")[p].EnumerateArray()) { var expected=row.GetProperty("position"); var actual=doc.EvaluateVertexPosition(1,p,row.GetProperty("vertex").GetInt32(),pose); var v=new V3(expected[0].GetSingle(),expected[1].GetSingle(),expected[2].GetSingle()); Assert.IsTrue(V3.Distance(v,actual)<=.00002f,$"{variant} {sample.GetProperty("label").GetString()} primitive {p} vertex {row.GetProperty("vertex").GetInt32()} error {V3.Distance(v,actual)}"); } }
 }
}
