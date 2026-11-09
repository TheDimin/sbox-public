using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Sandbox.Gltf.DirectGpu.Lighting;
using NMatrix=System.Numerics.Matrix4x4;

namespace Sandbox.Gltf.DirectGpu.DirectionalLightValidation;
/// <summary>Direct selected source asset and experimental native factory, not public-loader or absolute lux proof.</summary>
[TestClass]
public sealed class DirectionalRenderedTests
{
 static string root;
 public static List<object> Measurements {get;}=new();
 public static void Configure(string checkout){root=checkout;Measurements.Clear();}
 [TestMethod]
 public async Task SourceMaterialRespondsToDirectionalIntensityRotationZeroAndRemoval()
 {
  await MainThread.Wait();
  string dir=Path.Combine(root,"Temp/gltf-reference/glTF-Sample-Assets/Models/DirectionalLight/glTF");
  byte[] Resolve(string uri)=>File.ReadAllBytes(Path.Combine(dir,uri));
  var doc=GltfReader.Read(File.ReadAllBytes(Path.Combine(dir,"DirectionalLight.gltf")),Resolve,new HashSet<string>{"KHR_lights_punctual"});
  var output=Path.Combine(root,"Temp/gltf-validation/direct-gpu/DirectionalLight/rendered",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);
  var cap=new GltfGpuTriangleGateTests.NativeCapture(output,new global::Vector3(100,0,0),global::Vector3.Zero,75);
  GltfManagedModelResource resource=null; GltfGpuInstance instance=null; GltfPunctualLightInstances lights=null;
  try
  {
   var point=(ScenePointLight)typeof(GltfGpuTriangleGateTests.NativeCapture).GetField("light",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(cap);point.LightColor=global::Color.Black;
   resource=new GltfManagedModelResource(doc); doc=null;
   instance=resource.CreateInstance(cap.Scene.SceneWorld);
   var plan=GltfPunctualLightPlan.Create(resource.Asset.Document);
   lights=GltfPunctualLightInstances.Create(plan,new GltfDirectionalLightFactory(cap.Scene.SceneWorld,1),null,NMatrix.Identity,[true,true,true,true,true]);
   var light=cap.Scene.SceneWorld.SceneObjects.OfType<SceneDirectionalLight>().Single();
   var full=await cap.Take("candidate_intensity_1");
   Assert.IsTrue(instance.DrawCalls>0 && instance.CallbackErrors.Length==0); Assert.AreEqual(3,instance.Readback().Count);
   light.LightColor=new global::Color(.225f,.2f,.025f); var quarter=await cap.Take("candidate_intensity_quarter");
   light.LightColor=global::Color.Black; var zero=await cap.Take("zero");
   double Delta(GltfGpuTriangleGateTests.NativeCapture.Frame f)=>f.Pixels.Zip(zero.Pixels,(a,b)=>Math.Max(0,a.r-b.r)+Math.Max(0,a.g-b.g)+Math.Max(0,a.b-b.b)).Sum();
   var df=Delta(full);var dq=Delta(quarter); Assert.IsTrue(df>1 && dq>0 && df>dq,"Directional light did not produce monotonic positive rendered response.");
   lights.Update(null,NMatrix.CreateRotationZ(MathF.PI),[true,true,true,true,true]); var reversed=await cap.Take("reversed_direction");
   var dr=Delta(reversed); Assert.IsTrue(df>dr,"Back-facing directional light did not reduce front-side response.");
   lights.Dispose();lights=null; var removed=await cap.Take("removed");
   var max=removed.Pixels.Zip(zero.Pixels,(a,b)=>Math.Max(Math.Abs(a.r-b.r),Math.Max(Math.Abs(a.g-b.g),Math.Abs(a.b-b.b)))).Max(); Assert.IsTrue(max<=.01f,"Removed directional light differs from zero control.");
   Measurements.Add(new {output,full=full.Path,full.Sha256,quarter=quarter.Path,quarter_sha256=quarter.Sha256,zero=zero.Path,zero_sha256=zero.Sha256,reversed=reversed.Path,reversed_sha256=reversed.Sha256,removed=removed.Path,removed_sha256=removed.Sha256,full_delta=df,quarter_delta=dq,reversed_delta=dr,removed_max_difference=max,scope="Actual source material and GPU geometry; explicit candidate native color scale. Postprocessed relative diagnostic only, not public loader, calibrated lux, linear radiometry, reference parity or visual approval."});
  }
  finally {await MainThread.Wait();lights?.Dispose();instance?.Dispose();resource?.ReleaseOwner();doc?.Dispose();cap.Dispose();}
 }
}
