using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;
using Assert=Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace Sandbox.Gltf.DirectGpu.ABeautifulGameValidation;

// Targets the isolated reader proposal only after the candidate producer is installed.
// Reader capability does not imply public shader/backend or required-extension support.
[TestClass]
internal sealed class OpticalReaderTests
{
 const string T="KHR_materials_transmission", V="KHR_materials_volume";
 static readonly HashSet<string> Supported=new(StringComparer.Ordinal){T,V,"KHR_texture_transform"};
 static byte[] Bytes(JsonNode x)=>Encoding.UTF8.GetBytes(x.ToJsonString());
 static JsonObject Root(JsonNode t,JsonNode v)=>new(){["asset"]=new JsonObject{["version"]="2.0"},["extensionsUsed"]=new JsonArray(T,V),["materials"]=new JsonArray(new JsonObject{["extensions"]=new JsonObject{[T]=t,[V]=v}})};
 [TestMethod] public void UnsupportedOptionalFallsBackAndRequiredFails()
 {
  var root=Root(JsonValue.Create("corrupt optional"),new JsonObject());using(var d=GltfReader.Read(Bytes(root))){var m=d.ReadMaterial(0);Assert.IsNull(m.OpticalTransmission);Assert.IsNull(m.Volume);Assert.IsTrue(d.UnsupportedOptionalExtensions.Contains(T));}
  root["extensionsRequired"]=new JsonArray(T);Assert.ThrowsException<GltfException>(()=>GltfReader.Read(Bytes(root)));
 }
 [TestMethod] public void SupportedDefaultsAndAuthoredValues()
 {
  var root=Root(new JsonObject(),new JsonObject());using(var d=GltfReader.Read(Bytes(root),supportedExtensions:Supported)){var m=d.ReadMaterial(0);Assert.AreEqual(0f,m.OpticalTransmission.Factor);Assert.IsNull(m.OpticalTransmission.Texture);Assert.AreEqual(0f,m.Volume.ThicknessFactor);Assert.IsNull(m.Volume.ThicknessTexture);Assert.AreEqual(float.PositiveInfinity,m.Volume.AttenuationDistance);Assert.AreEqual(System.Numerics.Vector3.One,m.Volume.AttenuationColor);}
  root["materials"][0]["extensions"][T]=new JsonObject{["transmissionFactor"]=1};root["materials"][0]["extensions"][V]=new JsonObject{["thicknessFactor"]=.22,["attenuationColor"]=new JsonArray(.8,.8,.8)};using var sample=GltfReader.Read(Bytes(root),supportedExtensions:Supported);var a=sample.ReadMaterial(0);Assert.AreEqual(1f,a.OpticalTransmission.Factor);Assert.AreEqual(.22f,a.Volume.ThicknessFactor);Assert.AreEqual(float.PositiveInfinity,a.Volume.AttenuationDistance);
 }
 [TestMethod]
 [DataRow(T,"transmissionFactor","-0.01")][DataRow(T,"transmissionFactor","1.01")][DataRow(T,"transmissionFactor","null")][DataRow(T,"transmissionFactor","\"0.5\"")][DataRow(T,"transmissionTexture","{\"index\":-1}")]
 [DataRow(V,"thicknessFactor","-0.01")][DataRow(V,"thicknessFactor","null")][DataRow(V,"thicknessTexture","{\"index\":0}")][DataRow(V,"attenuationDistance","0")][DataRow(V,"attenuationDistance","-1")][DataRow(V,"attenuationDistance","null")][DataRow(V,"attenuationColor","[0.5,0.5]")][DataRow(V,"attenuationColor","[1.1,0,0]")][DataRow(V,"attenuationColor","[0,null,0]")]
 public void InvalidSupportedDataIsRejected(string extension,string property,string json)
 {
  var root=Root(new JsonObject(),new JsonObject());root["materials"][0]["extensions"][extension]=new JsonObject{[property]=JsonNode.Parse(json)};using var d=GltfReader.Read(Bytes(root),supportedExtensions:Supported);Assert.ThrowsException<GltfException>(()=>d.ReadMaterial(0));
 }
 [TestMethod] [DataRow("KHR_materials_unlit")][DataRow("KHR_materials_pbrSpecularGlossiness")]
 public void ExcludedModelsRejected(string exclusion)
 {
  var root=Root(new JsonObject(),new JsonObject());root["materials"][0]["extensions"][exclusion]=new JsonObject();using var d=GltfReader.Read(Bytes(root),supportedExtensions:Supported);Assert.ThrowsException<GltfException>(()=>d.ReadMaterial(0));
 }
 [TestMethod] public void RequiredSupportedReaderAndUndeclaredExtension()
 {
  var root=Root(new JsonObject{["transmissionFactor"]=.5},new JsonObject());root["extensionsRequired"]=new JsonArray(T,V);using(var d=GltfReader.Read(Bytes(root),supportedExtensions:Supported))Assert.AreEqual(.5f,d.ReadMaterial(0).OpticalTransmission.Factor);
  root.Remove("extensionsRequired");root["extensionsUsed"]=new JsonArray();using var bad=GltfReader.Read(Bytes(root),supportedExtensions:Supported);Assert.ThrowsException<GltfException>(()=>bad.ReadMaterial(0));
 }
 [TestMethod] public void OpticalTextureRolesAreLinearAndIndependent()
 {
  var root=Root(new JsonObject{["transmissionTexture"]=new JsonObject{["index"]=0,["texCoord"]=2}},new JsonObject{["thicknessTexture"]=new JsonObject{["index"]=1,["texCoord"]=5}});root["textures"]=new JsonArray(new JsonObject(),new JsonObject());using var d=GltfReader.Read(Bytes(root),supportedExtensions:Supported);var m=d.ReadMaterial(0);Assert.AreEqual(2,m.OpticalTransmission.Texture.TexCoord);Assert.AreEqual(5,m.Volume.ThicknessTexture.TexCoord);Assert.IsFalse(m.OpticalTransmission.Texture.IsSrgb);Assert.IsFalse(m.Volume.ThicknessTexture.IsSrgb);
 }
}
