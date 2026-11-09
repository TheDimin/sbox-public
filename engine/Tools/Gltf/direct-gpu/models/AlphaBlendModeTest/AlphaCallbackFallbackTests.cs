using System;
using System.Collections;
using System.Linq;
using System.Reflection;
namespace Sandbox.Gltf.DirectGpu.AlphaBlendModeValidation;
[TestClass]
public sealed class AlphaCallbackFallbackTests
{
 [TestMethod]
 public void CallbackFlagsSwitchBetweenContiguousAndSplitTransparency()
 {
  var scene=Scene.CreateEditorScene();var blend=Material.Load("materials/default/white.vmat").CreateCopy();GltfGpuAsset asset=null;GltfGpuInstance instance=null;
  try
  {
   blend.SetFeature("F_TRANSLUCENT",1);Assert.IsTrue(blend.Flags.IsTranslucent);using var doc=GltfReader.Read(AlphaControlFixtures.All().Single(x=>x.Name=="two_separated_blend_layers_near_first").Bytes);
   asset=GltfGpuAsset.Create(doc,"shaders/gltf_deform_cs.shader",_=>blend);instance=asset.CreateInstance(scene.SceneWorld);
   const BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Instance;var rows=((IEnumerable)typeof(GltfGpuInstance).GetField("draws",flags).GetValue(instance)).Cast<object>().ToArray();var field=rows[0].GetType().GetField("SortObject",flags);var children=rows.Select(row=>(SceneCustomObject)field.GetValue(row)).ToArray();var primary=instance.SceneObject;
   Assert.IsTrue(children.Length==2&&children.All(c=>c.IsValid()&&c.RenderingEnabled));
   primary.Flags.WantsExecuteBefore=true;Assert.IsTrue(primary.Flags.IsTranslucent&&children.All(c=>!c.RenderingEnabled),"Command callbacks must retain contiguous primary submission");
   primary.Flags.WantsExecuteBefore=false;Assert.IsTrue(children.All(c=>c.RenderingEnabled),"Clearing callbacks did not restore native sort units");
   primary.Flags.WantsExecuteAfter=true;Assert.IsTrue(children.All(c=>!c.RenderingEnabled));primary.Flags.WantsExecuteAfter=false;Assert.IsTrue(children.All(c=>c.RenderingEnabled));
   instance.Dispose();instance=null;Assert.IsTrue(children.All(c=>!c.IsValid()),"Dispose must retire all native units");
  }
  finally{instance?.Dispose();asset?.Dispose();blend.Destroy();scene.Destroy();}
 }

}
