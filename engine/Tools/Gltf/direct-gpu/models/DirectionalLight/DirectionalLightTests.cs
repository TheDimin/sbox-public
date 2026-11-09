using System;
using System.IO;
using System.Collections.Generic;
using Sandbox.Engine;
using Sandbox.Gltf;
using Sandbox.Gltf.DirectGpu.Lighting;
using NMatrix = System.Numerics.Matrix4x4;

namespace Sandbox.Gltf.DirectGpu.DirectionalLightValidation;
[TestClass]
public sealed class DirectionalLightTests
{
 public static string Root { get; set; }
 public static List<object> Measurements { get; } = new();
 static readonly IReadOnlySet<string> Selected = new HashSet<string>{"KHR_lights_punctual"};
 [DataTestMethod]
 [DataRow("glTF",false)] [DataRow("glTF",true)] [DataRow("glTF-Binary",false)] [DataRow("glTF-Binary",true)]
 public void SourcePlanAndPublicLoad(string variant, bool compiled)
 {
  var dir=Path.Combine(Root,"Temp/gltf-reference/glTF-Sample-Assets/Models/DirectionalLight",variant);
  var source=File.ReadAllBytes(Path.Combine(dir,variant=="glTF"?"DirectionalLight.gltf":"DirectionalLight.glb"));
  byte[] Resolve(string uri)=>File.ReadAllBytes(Path.Combine(dir,uri));
  var bytes=compiled?GltfCompiler.Compile(source,Resolve,Selected):source;
  using(var doc=GltfReader.Read(bytes,Resolve,Selected))
  {
   var plan=GltfPunctualLightPlan.Create(doc); Assert.AreEqual(1,plan.Bindings.Count);
   var b=plan.Bindings[0]; Assert.AreEqual(3,b.Node); Assert.AreEqual(0,b.Light);
   Assert.AreEqual(GltfPunctualLightPlan.LightKind.Directional,b.Kind); Assert.AreEqual(GltfPunctualLightPlan.IntensityUnit.Lux,b.Unit);
   Assert.AreEqual(1f,b.Intensity); Assert.AreEqual(.9f,b.LinearColor.X,1e-6f); Assert.AreEqual(.8f,b.LinearColor.Y,1e-6f); Assert.AreEqual(.1f,b.LinearColor.Z,1e-6f);
   var state=plan.Evaluate(null,NMatrix.Identity)[0]; Assert.IsTrue(state.HasEmissionDirection); Assert.IsFalse(state.HasPosition);
   Assert.AreEqual(-1f,state.EmissionDirection.X,1e-6f); Assert.AreEqual(0f,state.EmissionDirection.Y,1e-6f); Assert.AreEqual(0f,state.EmissionDirection.Z,1e-6f);
   var placed=plan.Evaluate(null,NMatrix.CreateRotationZ(MathF.PI/2))[0]; Assert.AreEqual(0f,placed.EmissionDirection.X,1e-6f); Assert.AreEqual(-1f,placed.EmissionDirection.Y,1e-6f);
  }
  var tmp=Path.Combine(Path.GetTempPath(),"directional_public_"+Guid.NewGuid().ToString("N")); var prefix="directional_"+Guid.NewGuid().ToString("N");
  var folder=Path.Combine(tmp,prefix); var relative=prefix+"/model"+(compiled?".glb_c":variant=="glTF"?".gltf":".glb");
  BaseFileSystem aggregate=null; LocalFileSystem mount=null; Model model=null; string error=null;
  try { Directory.CreateDirectory(folder); File.WriteAllBytes(Path.Combine(tmp,relative),bytes); if(!compiled && variant=="glTF") File.Copy(Path.Combine(dir,"DirectionalLight.bin"),Path.Combine(folder,"DirectionalLight.bin"));
   aggregate=Sandbox.FileSystem.Mounted; mount=new LocalFileSystem(tmp); aggregate.Mount(mount);
   try {model=Model.Load(relative);} catch(Exception e) { error=e.ToString(); }
   Measurements.Add(new {variant,compiled,valid=model is {IsValid:true,IsError:false},error});
   Assert.IsTrue(model is {IsValid:true,IsError:false} && model.native.IsNull && model.GltfResource is not null,"Public required-light model must load and retain managed resource. Actual: "+error);
  } finally { if(model is not null && model.ResourcePath==Resource.FixPath(relative)) model.Destroy(); if(mount is not null){aggregate.UnMount(mount);mount.Dispose();} if(Directory.Exists(tmp)) Directory.Delete(tmp,true); }
 }
}
