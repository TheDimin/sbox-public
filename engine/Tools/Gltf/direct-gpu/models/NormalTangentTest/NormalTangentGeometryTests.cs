using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Sandbox.Gltf;
using NVector3 = System.Numerics.Vector3;

namespace Sandbox.Gltf.DirectGpu.NormalTangentTestValidation;

[TestClass]
public sealed class NormalTangentGeometryTests
{
 public static string CheckoutRoot { get; set; }
 [DataTestMethod]
 [DataRow("glTF", false)]
 [DataRow("glTF", true)]
 [DataRow("glTF-Binary", false)]
 [DataRow("glTF-Binary", true)]
 public void GeneratedCornerFramesMatchPinnedGeometry( string variant, bool compiled )
 {
  var root = Path.Combine(CheckoutRoot,"Temp/gltf-reference/glTF-Sample-Assets/Models/NormalTangentTest");
  var dir = Path.Combine(root,variant); var file = variant == "glTF" ? "NormalTangentTest.gltf" : "NormalTangentTest.glb";
  var bytes = File.ReadAllBytes(Path.Combine(dir,file));
  Assert.AreEqual(variant == "glTF" ? "ef2b152063304ebb0314646d3d1610cdc680e131d2cbfef10972dc6120d4dd18" : "5ac0932355ae1ea05a7485eeddcc3f1fbe56c678e00b519075feced80e9e9d6a", Convert.ToHexStringLower(SHA256.HashData(bytes)));
  var raw = File.ReadAllBytes(Path.Combine(root,"glTF/NormalTangentTest0.bin"));
  Assert.AreEqual("2d0fc6d43cb11b61a746bc80218b5e9e6c60dcc40f86d4a3f4fdbf11909cd990", Convert.ToHexStringLower(SHA256.HashData(raw)));
  byte[] Resolve(string uri) => File.ReadAllBytes(Path.Combine(dir,uri));
  using var doc = GltfReader.Read(compiled ? GltfCompiler.Compile(bytes,Resolve) : bytes, Resolve);
  var plan = GltfGpuPrimitivePlan.Create(doc,0,0).ProjectMaterial(0,5,null,[0,1,4,5,6]);
  var route = GltfGpuGeneratedTangentRoute.Prepare(plan,false);
  Assert.IsNotNull(route); Assert.AreEqual(23322,route.SourceVertices.Length); Assert.AreEqual(23322,route.Indices.Length);
  var output = route.OutputDescriptors.Single(x => x.Role == 2); Assert.AreEqual(1u,output.Padding0);
  var descriptor = route.InputDescriptors[output.Source]; Assert.AreEqual(16u,descriptor.ByteStride);
  var negativeDetected = false;
  for (var i=0; i<route.SourceVertices.Length; i++)
  {
   var expectedIndex = BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(127456+i*2));
   Assert.AreEqual((uint)expectedIndex,route.SourceVertices[i]); Assert.AreEqual((uint)i,route.Indices[i]);
   var offset=checked((int)descriptor.ByteOffset+i*16); var tangent = new NVector3(F(route.InputBytes,offset),F(route.InputBytes,offset+4),F(route.InputBytes,offset+8)); var sign=F(route.InputBytes,offset+12);
   var normal = new NVector3(F(raw,expectedIndex*12),F(raw,expectedIndex*12+4),F(raw,expectedIndex*12+8));
   Assert.IsTrue(float.IsFinite(tangent.X) && float.IsFinite(tangent.Y) && float.IsFinite(tangent.Z) && Math.Abs(tangent.Length()-1)<0.0001f && Math.Abs(NVector3.Dot(tangent,normal))<0.0001f && Math.Abs(Math.Abs(sign)-1)<0.0001f,$"Invalid generated frame at corner {i}");
   negativeDetected |= Math.Abs(NVector3.Dot(normal,normal)) > .5f;
  }
  Assert.IsTrue(negativeDetected,"Normal-as-tangent negative control was not detected");
 }
 private static float F(byte[] b,int o) => BinaryPrimitives.ReadSingleLittleEndian(b.AsSpan(o));
}
