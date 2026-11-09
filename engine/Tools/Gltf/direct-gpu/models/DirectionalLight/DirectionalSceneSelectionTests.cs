using System.Collections.Generic;
using System.Text;
using Sandbox.Gltf.DirectGpu.Lighting;

namespace Sandbox.Gltf.DirectGpu.DirectionalLightValidation;
[TestClass]
public sealed class DirectionalSceneSelectionTests
{
 [TestMethod]
 public void ExplicitSceneAndUnselectedResourceHaveIndependentBindings()
 {
  const string json="""{"asset":{"version":"2.0"},"extensionsUsed":["KHR_lights_punctual"],"scenes":[{"nodes":[0]},{"nodes":[1]}],"nodes":[{"extensions":{"KHR_lights_punctual":{"light":0}}},{"extensions":{"KHR_lights_punctual":{"light":1}}}],"extensions":{"KHR_lights_punctual":{"lights":[{"type":"directional","intensity":2},{"type":"directional","intensity":7}]}}}""";
  using var doc=GltfReader.Read(Encoding.UTF8.GetBytes(json),supportedExtensions:new HashSet<string>{"KHR_lights_punctual"});
  Assert.AreEqual(0,GltfPunctualLightPlan.CreateForScene(doc,null,true).Bindings.Count);
  Assert.ThrowsException<GltfException>(()=>GltfPunctualLightPlan.CreateForScene(doc,null,false));
  var a=GltfPunctualLightPlan.CreateForScene(doc,0,false).Bindings.Single();
  var b=GltfPunctualLightPlan.CreateForScene(doc,1,false).Bindings.Single();
  Assert.AreEqual(0,a.Node); Assert.AreEqual(2f,a.Intensity);
  Assert.AreEqual(1,b.Node); Assert.AreEqual(7f,b.Intensity);
  Assert.ThrowsException<GltfException>(()=>GltfPunctualLightPlan.CreateForScene(doc,2,false));
 }
}
