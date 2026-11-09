using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Assert=Microsoft.VisualStudio.TestTools.UnitTesting.Assert;
using Sandbox;
using Sandbox.Gltf;

namespace Sandbox.Gltf.DirectGpu.MandarinOrangeValidation;

// These tests target the proposed actual reader/adapter fields after the shared
// freeze is released and the integration owner installs that producer.
[TestClass]
internal sealed class DiffuseTransmissionReaderTests
{
 const string Extension="KHR_materials_diffuse_transmission";
 static readonly HashSet<string> Supported=new(StringComparer.Ordinal){Extension,"KHR_texture_transform"};
 public static string CheckoutRoot {get;set;}
 public static List<object> Measurements {get;}=new();
 static byte[] Bytes(JsonNode value)=>Encoding.UTF8.GetBytes(value.ToJsonString());
 static JsonObject Root(JsonNode extension)=>new(){["asset"]=new JsonObject{["version"]="2.0"},["extensionsUsed"]=new JsonArray(Extension),["materials"]=new JsonArray(new JsonObject{["extensions"]=new JsonObject{[Extension]=extension}})};

 [TestMethod]
 public void OptionalUnsupportedIgnoresExtensionWithoutClaimingSupport()
 {
  var root=Root(JsonValue.Create("malformed unsupported extension"));using var d=GltfReader.Read(Bytes(root));
  Assert.IsNull(d.ReadMaterial(0).DiffuseTransmission);Assert.IsTrue(d.UnsupportedOptionalExtensions.Contains(Extension));
  root["extensionsRequired"]=new JsonArray(Extension);
  var error=Assert.ThrowsException<GltfException>(()=>GltfReader.Read(Bytes(root)));StringAssert.Contains(error.Message,Extension);
 }

 [TestMethod]
 public void SupportedDefaultsAndExplicitZeroAreRetained()
 {
  var root=Root(new JsonObject());using var d=GltfReader.Read(Bytes(root),supportedExtensions:Supported);
  var material=d.ReadMaterial(0).DiffuseTransmission;Assert.IsNotNull(material);Assert.AreEqual(0f,material.Factor);Assert.AreEqual(System.Numerics.Vector3.One,material.ColorFactor);Assert.IsNull(material.Texture);Assert.IsNull(material.ColorTexture);
  root["materials"][0]["extensions"][Extension]=new JsonObject{["diffuseTransmissionFactor"]=0,["diffuseTransmissionColorFactor"]=new JsonArray(0,0,0)};
  using var zero=GltfReader.Read(Bytes(root),supportedExtensions:Supported);Assert.AreEqual(System.Numerics.Vector3.Zero,zero.ReadMaterial(0).DiffuseTransmission.ColorFactor);
  Assert.IsFalse(d.UnsupportedOptionalExtensions.Contains(Extension));
 }

 [TestMethod]
 [DataRow("diffuseTransmissionFactor","-0.01")]
 [DataRow("diffuseTransmissionFactor","1.01")]
 [DataRow("diffuseTransmissionFactor","null")]
 [DataRow("diffuseTransmissionFactor","\"0.5\"")]
 [DataRow("diffuseTransmissionColorFactor","[0.1,0.2]")]
 [DataRow("diffuseTransmissionColorFactor","[0.1,0.2,0.3,0.4]")]
 [DataRow("diffuseTransmissionColorFactor","[0.1,-0.1,0.3]")]
 [DataRow("diffuseTransmissionColorFactor","[0.1,0.2,1.1]")]
 [DataRow("diffuseTransmissionColorFactor","[0.1,0.2,null]")]
 [DataRow("diffuseTransmissionTexture","{\"index\":-1}")]
 [DataRow("diffuseTransmissionColorTexture","{\"index\":0}")]
 public void SchemaInvalidValuesAreRejectedWhenSupported(string property,string json)
 {
  var root=Root(new JsonObject{[property]=JsonNode.Parse(json)});using var d=GltfReader.Read(Bytes(root),supportedExtensions:Supported);Assert.ThrowsException<GltfException>(()=>d.ReadMaterial(0));
 }

 [TestMethod]
 [DataRow("KHR_materials_unlit")]
 [DataRow("KHR_materials_pbrSpecularGlossiness")]
 public void ExcludedMaterialModelsAreRejected(string other)
 {
  var root=Root(new JsonObject());root["extensionsUsed"]=new JsonArray(Extension,other);root["materials"][0]["extensions"][other]=new JsonObject();using var d=GltfReader.Read(Bytes(root),supportedExtensions:Supported);var error=Assert.ThrowsException<GltfException>(()=>d.ReadMaterial(0));StringAssert.Contains(error.Message,other);
 }

 [TestMethod]
 public void RequiredSupportedReadsButUndeclaredUsedFails()
 {
  var root=Root(new JsonObject{["diffuseTransmissionFactor"]=.5});root["extensionsRequired"]=new JsonArray(Extension);
  using(var d=GltfReader.Read(Bytes(root),supportedExtensions:Supported))Assert.AreEqual(.5f,d.ReadMaterial(0).DiffuseTransmission.Factor);
  root.Remove("extensionsRequired");root["extensionsUsed"]=new JsonArray();using var invalid=GltfReader.Read(Bytes(root),supportedExtensions:Supported);var error=Assert.ThrowsException<GltfException>(()=>invalid.ReadMaterial(0));StringAssert.Contains(error.Message,"extensionsUsed");
 }

 [TestMethod]
 public void SevenArbitrarySourceSetsKeepRoleTransferSamplerAndTransforms()
 {
  string own=Path.Combine(CheckoutRoot,"research/gltf-validation/direct-gpu/models/MandarinOrange");
  var root=JsonNode.Parse(File.ReadAllBytes(Path.Combine(own,"diffuse-transmission-plane.gltf")));
  root["extensionsUsed"]=new JsonArray(Extension,"KHR_texture_transform");
  root["images"]=new JsonArray(new JsonObject{["uri"]="role.png"});
  root["samplers"]=new JsonArray(new JsonObject{["magFilter"]=9728,["minFilter"]=9729,["wrapS"]=33071,["wrapT"]=33648},new JsonObject{["magFilter"]=9729,["minFilter"]=9728,["wrapS"]=10497,["wrapT"]=33071});
  root["textures"]=new JsonArray(Enumerable.Range(0,7).Select(i=>(JsonNode)new JsonObject{["source"]=0,["sampler"]=i%2}).ToArray());
  int[] sets={2,7,11,19,23,41,49};var attributes=root["meshes"][0]["primitives"][0]["attributes"].AsObject();attributes.Remove("TEXCOORD_0");foreach(int set in sets)attributes["TEXCOORD_"+set]=2;for(int set=0;set<=49;set++)if(!attributes.ContainsKey("TEXCOORD_"+set))attributes["TEXCOORD_"+set]=2;
  JsonObject Info(int index)=>new(){["index"]=index,["texCoord"]=0,["extensions"]=new JsonObject{["KHR_texture_transform"]=new JsonObject{["texCoord"]=sets[index],["offset"]=new JsonArray(index/10f,.125f),["scale"]=new JsonArray(.5f,2f)}}};
  var material=root["materials"][0];material["pbrMetallicRoughness"]["baseColorTexture"]=Info(0);material["pbrMetallicRoughness"]["metallicRoughnessTexture"]=Info(1);material["normalTexture"]=Info(2);material["occlusionTexture"]=Info(3);material["emissiveTexture"]=Info(4);
  material["extensions"]=new JsonObject{[Extension]=new JsonObject{["diffuseTransmissionFactor"]=.5,["diffuseTransmissionTexture"]=Info(5),["diffuseTransmissionColorTexture"]=Info(6)}};
  byte[] image=File.ReadAllBytes(Path.Combine(own,"diffuse-transmission-role-control.png"));int resolves=0;
  using var d=GltfReader.Read(Bytes(root),_=>{resolves++;return (byte[])image.Clone();},Supported);
  var m=d.ReadMaterial(0);var upload=d.MaterializePrimitiveUpload(0,0);var uv=GltfMaterialUvPlan.Create(m,upload,0);Assert.AreEqual(7,uv.Bindings.Count);
  var projection=GltfMaterialUvProjection.Create(uv,7);CollectionAssert.AreEqual(sets,projection.Sources.Select(x=>x.TexCoord).ToArray());Assert.ThrowsException<GltfException>(()=>GltfMaterialUvProjection.Create(uv,5));
  var a=uv.Bindings[GltfMaterialTextureRole.DiffuseTransmission];var c=uv.Bindings[GltfMaterialTextureRole.DiffuseTransmissionColor];Assert.AreEqual(41,a.TexCoord);Assert.AreEqual(49,c.TexCoord);Assert.AreEqual(5,projection.Slots[a.Role]);Assert.AreEqual(6,projection.Slots[c.Role]);
  Assert.IsFalse(a.IsSrgb);Assert.IsTrue(c.IsSrgb);Assert.AreSame(upload.Attributes["TEXCOORD_41"],a.Source);Assert.AreSame(upload.Attributes["TEXCOORD_49"],c.Source);
  Assert.AreEqual(new System.Numerics.Vector2(.6f,.525f),a.Transform.Apply(new System.Numerics.Vector2(.2f,.2f)));Assert.AreEqual(new System.Numerics.Vector2(.70000005f,.525f),c.Transform.Apply(new System.Numerics.Vector2(.2f,.2f)));
  var encoding=GltfMaterialTextureEncodingPlan.Create(d,m);var ea=encoding.Bindings[a.Role];var ec=encoding.Bindings[c.Role];Assert.AreEqual(GltfMaterialTextureChannels.Alpha,ea.Channels);Assert.AreEqual(GltfMaterialTextureChannels.Rgb,ec.Channels);Assert.IsTrue(ea.HasLinearAlpha);Assert.IsFalse(ea.UsesAlphaCoverage);Assert.IsFalse(ea.IsSrgb);Assert.IsTrue(ec.IsSrgb);Assert.AreEqual(1,ea.SamplerIndex);Assert.AreEqual(0,ec.SamplerIndex);Assert.AreEqual(10497,ea.Sampler.WrapS);Assert.AreEqual(33648,ec.Sampler.WrapT);
  var snapshot=d.SnapshotMaterialImages(new int?[]{0},1024*1024);Assert.AreEqual(1,resolves);var retained=snapshot.ReadImage(0);CollectionAssert.AreEqual(image,retained.Bytes);retained.Bytes[0]=0;CollectionAssert.AreEqual(image,snapshot.ReadImage(0).Bytes);
  var plan=GltfGpuPrimitivePlan.Create(d,0,0).ProjectMaterial(0,7,null,new[]{0,1,4,5,6,7,8});Assert.AreEqual(7,plan.UvProjection.Sources.Count);CollectionAssert.AreEqual(new[]{0,1,4,5,6,7,8},plan.Fields.Where(f=>f.Semantic=="TEXCOORD").Select(f=>f.SemanticIndex).ToArray());
  Measurements.Add(new{scope="Seven arbitrary authored UV sets and canonical descriptor projection; separate alpha/color transfer and sampler contracts; immutable retained encoded generation",sets,encoded_bytes=snapshot.EncodedBytes,resolver_calls=resolves});
 }
}
