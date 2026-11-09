using System;
using System.Linq;
namespace Sandbox.Gltf.DirectGpu.AlphaBlendModeValidation;
[TestClass]
public sealed class AlphaFramebufferOwnershipTests
{
 [TestMethod]
 public void ExplicitRequestSurvivesAutomaticOverrideTransitions()
 {
  var scene=Scene.CreateEditorScene();var ordinary=new SceneCustomObject(scene.SceneWorld);var opaque=Material.Load("materials/default/white.vmat").CreateCopy();var shader=Shader.Load("shaders/glass.shader");Material glass=null;GltfGpuAsset asset=null;GltfGpuInstance instance=null;
  try
  {
   ordinary.Flags.WantsFrameBufferCopy=true;Assert.IsTrue(ordinary.Flags.WantsFrameBufferCopy,"Ordinary scene object true setter changed");ordinary.Flags.WantsFrameBufferCopy=false;Assert.IsFalse(ordinary.Flags.WantsFrameBufferCopy,"Ordinary scene object false setter changed");
   Assert.IsTrue(shader is {IsValid:true,IsError:false});glass=Material.Create("alpha_framebuffer_"+Guid.NewGuid().ToString("N"),shader);glass.SetFeature("F_GLASS_QUALITY",0);glass.SetFeature("F_TRANSLUCENT",1);Assert.IsTrue(glass.Flags.IsTranslucent&&glass.Flags.GetInt("bWantsFBCopyTexture")!=0);
   using var doc=GltfReader.Read(AlphaControlFixtures.All().Single(x=>x.Name=="two_separated_blend_layers_near_first").Bytes);asset=GltfGpuAsset.Create(doc,"shaders/gltf_deform_cs.shader",_=>opaque);instance=asset.CreateInstance(scene.SceneWorld);var flags=instance.SceneObject.Flags;
   Assert.IsFalse(flags.WantsFrameBufferCopy);instance.SetMaterialOverride(glass);Assert.IsTrue(flags.WantsFrameBufferCopy);instance.ClearMaterialOverrides();Assert.IsFalse(flags.WantsFrameBufferCopy,"Automatic request must clear when requirement disappears");
   flags.WantsFrameBufferCopy=true;instance.SetMaterialOverride(glass);instance.ClearMaterialOverrides();Assert.IsTrue(flags.WantsFrameBufferCopy,"Explicit true must survive automatic requirement disappearing");flags.WantsFrameBufferCopy=false;Assert.IsFalse(flags.WantsFrameBufferCopy);
   instance.SetMaterialOverride(glass);flags.WantsFrameBufferCopy=true;instance.ClearMaterialOverrides();Assert.IsTrue(flags.WantsFrameBufferCopy,"Explicit true set while already automatically true must survive");flags.WantsFrameBufferCopy=false;Assert.IsFalse(flags.WantsFrameBufferCopy);
   instance.SetMaterialOverride(glass);flags.WantsFrameBufferCopy=false;Assert.IsTrue(flags.WantsFrameBufferCopy,"Automatic requirement must survive explicit false");instance.ClearMaterialOverrides();Assert.IsFalse(flags.WantsFrameBufferCopy);
  }
  finally{instance?.Dispose();asset?.Dispose();glass?.Destroy();opaque.Destroy();if(ordinary.IsValid())ordinary.Delete();scene.Destroy();}
 }
}
