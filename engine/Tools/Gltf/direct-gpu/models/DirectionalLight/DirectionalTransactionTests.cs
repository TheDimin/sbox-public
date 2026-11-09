using System;
using System.Collections.Generic;
using System.IO;
using Sandbox.Gltf.DirectGpu.Lighting;
using NMatrix=System.Numerics.Matrix4x4;

namespace Sandbox.Gltf.DirectGpu.DirectionalLightValidation;
/// <summary>Injects a partial native-style apply failure; not a native driver failure claim.</summary>
[TestClass]
public sealed class DirectionalTransactionTests
{
 public static string Root {get;set;}
 [TestMethod]
 public void FailedPoseAndVisibilityApplyRestoreLightBeforeGeometryCommit()
 {
  string dir=Path.Combine(Root,"Temp/gltf-reference/glTF-Sample-Assets/Models/DirectionalLight/glTF");
  var doc=GltfReader.Read(File.ReadAllBytes(Path.Combine(dir,"DirectionalLight.gltf")),uri=>File.ReadAllBytes(Path.Combine(dir,uri)),new HashSet<string>{"KHR_lights_punctual"});
  var scene=Scene.CreateEditorScene();GltfManagedModelResource resource=null;GltfGpuInstance instance=null;
  try
  {
   resource=new GltfManagedModelResource(doc,0,true);doc=null;instance=resource.CreateInstance(scene.SceneWorld);
   var factory=new FailingFactory();instance.SetDirectionalLightScale(1,true,factory);
   var light=factory.Light;var original=light.State.EmissionDirection;var originalRevision=instance.PoseRevision;var originalPlacement=instance.Placement;
   light.FailCount=1;Assert.ThrowsException<InvalidOperationException>(()=>instance.Placement=NMatrix.CreateRotationZ(MathF.PI/2));
   Assert.AreEqual(originalRevision,instance.PoseRevision);Assert.AreEqual(originalPlacement,instance.Placement);Assert.AreEqual(original,light.State.EmissionDirection);Assert.IsTrue(light.Visible);
   light.FailCount=1;Assert.ThrowsException<InvalidOperationException>(()=>instance.SetNodeVisibilityOverrides(new Dictionary<int,bool>{{3,false}}));
   Assert.IsTrue(light.Visible,"Partially applied visibility was not restored.");Assert.AreEqual(originalRevision,instance.PoseRevision);
   instance.Placement=NMatrix.CreateRotationZ(MathF.PI/2);Assert.AreNotEqual(original,light.State.EmissionDirection);
   instance.SetNodeVisibilityOverrides(new Dictionary<int,bool>{{3,false}});Assert.IsFalse(light.Visible);
  }
  finally {instance?.Dispose();resource?.ReleaseOwner();doc?.Dispose();scene.Destroy();}
 }
 [TestMethod]
 public void FailedRestoreReportsBothOriginalAndRollbackErrors()
 {
  const string json="""{"asset":{"version":"2.0"},"extensionsUsed":["KHR_lights_punctual"],"scene":0,"scenes":[{"nodes":[0]}],"nodes":[{"extensions":{"KHR_lights_punctual":{"light":0}}}],"extensions":{"KHR_lights_punctual":{"lights":[{"type":"directional"}]}}}""";
  using var doc=GltfReader.Read(System.Text.Encoding.UTF8.GetBytes(json),supportedExtensions:new HashSet<string>{"KHR_lights_punctual"});
  var factory=new FailingFactory();using var owner=GltfPunctualLightInstances.Create(GltfPunctualLightPlan.Create(doc),factory,null,NMatrix.Identity,[true]);
  factory.Light.FailCount=2;
  var error=Assert.ThrowsException<AggregateException>(()=>owner.Update(null,NMatrix.CreateRotationZ(1),[true]));
  Assert.AreEqual(2,error.InnerExceptions.Count,"Both the original apply and failed restore must remain visible.");
 }
 sealed class FailingFactory:GltfPunctualLightInstances.IFactory
 {
  public FailingLight Light {get;}=new();
  public GltfPunctualLightInstances.ILight Create(GltfPunctualLightPlan.Binding binding)=>Light;
 }
 sealed class FailingLight:GltfPunctualLightInstances.ILight
 {
  public GltfPunctualLightPlan.State State;public bool Visible;public int FailCount;
  public void Apply(GltfPunctualLightPlan.State state,bool visible){State=state;Visible=visible;if(FailCount>0){FailCount--;throw new InvalidOperationException("Injected partial light apply");}}
  public void Dispose(){}
 }
}
