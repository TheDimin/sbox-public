using System;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
namespace Sandbox.Gltf.DirectGpu.AlphaBlendModeValidation;
[TestClass]
public sealed class AlphaMaskAdapterTests
{
 [DataTestMethod]
 [DataRow(false)]
 [DataRow(true)]
 public void SourceMaskRetainsAlphaCutoffTextureAndRejectsBlend(bool compiled)
 {
  var fixture=AlphaControlFixtures.All().Single(x=>x.Name=="linear_texture_factor_color4");var json=JsonNode.Parse(fixture.Bytes)!.AsObject();json["materials"]![0]!["alphaMode"]="MASK";json["materials"]![0]!["alphaCutoff"]=.125f;
  byte[] Form(JsonNode root){var bytes=Encoding.UTF8.GetBytes(root.ToJsonString());return compiled?GltfCompiler.Compile(bytes,null):bytes;}
  using var doc=GltfReader.Read(Form(json));var prepared=global::Sandbox.GltfStockMaterialAdapter.PrepareSource(doc,doc.ReadMaterial(0));
  Assert.IsTrue(prepared.AlphaMask);Assert.AreEqual(.125f,prepared.AlphaCutoff);Assert.AreEqual(.375f,prepared.LinearBaseColorFactor.W);Assert.IsTrue(prepared.Resources.ContainsKey(global::Sandbox.GltfMaterialTextureRole.BaseColor));
  var blend=json.DeepClone();blend["materials"]![0]!["alphaMode"]="BLEND";using var blendDoc=GltfReader.Read(Form(blend));Assert.ThrowsException<GltfException>(()=>global::Sandbox.GltfStockMaterialAdapter.PrepareSource(blendDoc,blendDoc.ReadMaterial(0)),"Unproven lit BLEND must remain rejected.");
 }
}
