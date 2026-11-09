using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
namespace Sandbox.Gltf.DirectGpu;
using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;
[TestClass]
public sealed class GltfGpuNativeLightAuditTests
{
 public static object[] Measurements {get;private set;} = Array.Empty<object>();
 [TestMethod]
 public void NativePackingObservationsUseOnlyOwnedLightsAndKnownLoadedProducers()
 {
  Assert.IsTrue(Application.IsEditor && !Game.IsPlaying && !Graphics.IsActive);
  var scene=Scene.CreateEditorScene();
  try
  {
   var before=scene.SceneWorld.SceneObjects.Count();
   var rows=LightsPunctualLampValidation.LightsPunctualLampNativeLightAudit.Run(scene.SceneWorld);
   Measurements=rows.Cast<object>().ToArray();
   Assert.AreEqual(6,rows.Length);
   Assert.AreEqual(before,scene.SceneWorld.SceneObjects.Count(),"Temporary native lights leaked from audit.");
   Assert.IsTrue(rows.All(row=>row.EngineMvid==typeof(Model).Assembly.ManifestModule.ModuleVersionId.ToString()));
  }
  finally {scene.Destroy();}
 }
}
