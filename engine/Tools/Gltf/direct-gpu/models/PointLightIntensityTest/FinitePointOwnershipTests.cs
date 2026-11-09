using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;
using Sandbox.Gltf.DirectGpu.Lighting;
using Sandbox.SceneRenderer.Gpu;
using NMatrix=System.Numerics.Matrix4x4;

namespace Sandbox.Gltf.DirectGpu.PointLightIntensityValidation;

// Ownership/selection candidate against the real source, independently of
// public Model.Load and of source-material rendering.
[TestClass]
internal sealed class FinitePointOwnershipTests
{
 public static string CheckoutRoot {get;set;}
 public static List<object> Measurements {get;}=new();
 [DataTestMethod] [DataRow(false)] [DataRow(true)]
 public void SourceAndCompiledPointBindingsOwnIndependentLights(bool compiled)
 {
  string dir=Path.Combine(CheckoutRoot,"Temp/gltf-reference/glTF-Sample-Assets/Models/PointLightIntensityTest/glTF");
  byte[] source=File.ReadAllBytes(Path.Combine(dir,"PointLightIntensityTest.gltf"));
  Assert.AreEqual("7913fe3e6829beba77f65ea749e40b3e95906bc2894afc443c22e3d91e263733",Convert.ToHexStringLower(SHA256.HashData(source)));
  byte[] Resolve(string uri)=>File.ReadAllBytes(Path.Combine(dir,uri));
  var supported=new HashSet<string>(StringComparer.Ordinal){"KHR_lights_punctual","KHR_materials_unlit"};
  byte[] bytes=compiled?GltfCompiler.Compile(source,Resolve,supported):source;
  using var doc=GltfReader.Read(bytes,compiled?null:Resolve,supported);
  var plan=GltfPunctualLightPlan.Create(doc);Assert.AreEqual(8,plan.Bindings.Count);
  var scene=Scene.CreateEditorScene();Texture marker=null;GltfPunctualLightInstances first=null,second=null;
  try {
   int before=scene.SceneWorld.SceneObjects.Count();marker=Texture.Create(1,1,ImageFormat.RGBA8888).WithMips(1).WithData(new byte[]{255,255,255,255}).WithStaticUsage().Finish();
   var factory=new FinitePointFactoryCandidate(scene.SceneWorld,marker);var visible=Enumerable.Repeat(true,15).ToArray();
   first=GltfPunctualLightInstances.Create(plan,factory,null,NMatrix.Identity,visible);
   second=GltfPunctualLightInstances.Create(plan,factory,null,NMatrix.CreateTranslation(500,0,0),visible);
   var lights=scene.SceneWorld.SceneObjects.OfType<ScenePointLight>().ToArray();Assert.AreEqual(16,lights.Length);
   Assert.IsTrue(lights.All(x=>Math.Abs(x.Radius-(1.125/.0254))<.0001 && x.LightCookie==marker));
   Assert.AreEqual(2,lights.Count(x=>Math.Abs(x.LightColor.r-.5f)<.00001 && Math.Abs(x.LightColor.g-.5f)<.00001 && Math.Abs(x.LightColor.b-.5f)<.00001));
   Assert.IsTrue(lights.All(x=>RenderContext.PackSceneLight(x,out var p,out _,out _) && p.LightCookieTextureIndex==(uint)marker.Index));
   // Literal authored light positions after the (z,x,y) basis permutation.
   // Match the actual native light objects, independent of plan.Evaluate.
   (double y,double z,Color color)[] expected=[
    (0,-2.5,Color.White),(-2.25,0,new Color(1,0,0)),(2.25,0,new Color(0,0,1)),(0,0,new Color(0,1,0)),
    (2.25,-2.5,new Color(.5f,.5f,.5f)),(-2.25,-2.5,new Color(0,0,1)),(-2.25,-2.5,new Color(0,1,0)),(-2.25,-2.5,new Color(1,0,0))
   ];
   foreach(var offset in new[]{0,500})
   foreach(var (y,z,color) in expected)
    Assert.AreEqual(1,lights.Count(x=>Math.Abs(x.Position.x-(.2/.0254+offset))<.001 &&
      Math.Abs(x.Position.y-y/.0254)<.001 && Math.Abs(x.Position.z-z/.0254)<.001 &&
      Math.Abs(x.LightColor.r-color.r)<.00001 && Math.Abs(x.LightColor.g-color.g)<.00001 &&
      Math.Abs(x.LightColor.b-color.b)<.00001));
   Assert.ThrowsException<GltfException>(()=>factory.Create(plan.Bindings[0] with {RangeMeters=null}));
   Assert.AreEqual(16,scene.SceneWorld.SceneObjects.OfType<ScenePointLight>().Count(),"Unbounded rejection must leave native lights intact");
   visible[0]=false;first.Update(null,NMatrix.Identity,visible);Assert.AreEqual(1,lights.Count(x=>x.LightColor.r==0 && x.LightColor.g==0 && x.LightColor.b==0));
   first.Dispose();first=null;
   var retained=scene.SceneWorld.SceneObjects.OfType<ScenePointLight>().ToArray();
   Assert.AreEqual(8,retained.Length,"Deleting first must retain second lights");
   Assert.IsTrue(retained.All(x=>Math.Abs(x.Position.x-(.2/.0254+500))<.001));
   Assert.IsTrue(marker.IsValid);second.Dispose();second=null;Assert.AreEqual(before,scene.SceneWorld.SceneObjects.Count());
   Measurements.Add(new{compiled,bindings=plan.Bindings.Count,instances=2,initial_lights=16,retained_lights=8,unbounded_rejected=true,scope="Source/derived-compiled selected light plan and candidate ownership with independent positions and colors. Public Model.Load integration, source rendering, calibrated photometry and shadows remain separate gates."});
  } finally {first?.Dispose();second?.Dispose();marker?.Dispose();scene.Destroy();}
 }
}
