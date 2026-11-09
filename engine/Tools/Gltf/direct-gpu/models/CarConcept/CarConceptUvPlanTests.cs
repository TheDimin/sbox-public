using System;
using System.Collections.Generic;
using System.IO;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;
using Sandbox;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;
using Sandbox.Gltf.DirectGpu;

namespace Sandbox.Gltf.DirectGpu.CarConceptValidation;
[TestClass]
public sealed class CarConceptUvPlanTests
{
 static string root;
 public static void Configure(string checkout) => root=checkout;
 [TestMethod,DataRow(false),DataRow(true)]
 public void AuthoredPaintNormalUvUsesSpecMatrix(bool compiled)
 {
  var dir=Path.Combine(root,"Temp/gltf-reference/glTF-Sample-Assets/Models/CarConcept/glTF");
  byte[] input=File.ReadAllBytes(Path.Combine(dir,"CarConcept.gltf"));
  byte[] Resolve(string uri)=>File.ReadAllBytes(Path.Combine(dir,uri));
  var selected=new HashSet<string>{"KHR_texture_transform"};
  if(compiled) input=GltfCompiler.Compile(input,Resolve,selected);
  using var doc=GltfReader.Read(input,Resolve,selected);
  var texture=doc.ReadMaterial(10).NormalTexture;
  var upload=doc.MaterializePrimitiveUpload(32,0);
  var plan=GltfMaterialUvPlan.CreateForVariant(doc,32,0,upload,null);
  Assert.AreEqual(10,plan.MaterialIndex);
  var binding=plan.Bindings[GltfMaterialTextureRole.Normal];
  Assert.AreEqual(8,binding.TextureIndex);
  var actual=binding.Transform.Apply(new NVector2(.25f,.5f));
  Assert.AreEqual(-2.0367755f,actual.X,.0001f);
  Assert.AreEqual(16.6463673f,actual.Y,.0001f);
  // Distinct literal normal texels in a 4x4 nearest/repeat map: correct (3,2),
  // wrong rotation (2,1). This is an independent address oracle, not a GPU sample.
  NVector3[] texels=new NVector3[16]; texels[11]=new(.75f,.25f,1);texels[6]=new(.125f,.875f,1);
  float Repeat(float x)=>x-MathF.Floor(x);
  int index=(int)MathF.Floor(Repeat(actual.Y)*4)*4+(int)MathF.Floor(Repeat(actual.X)*4);
  Assert.AreEqual(new NVector3(.75f,.25f,1),texels[index]);
 }
}
