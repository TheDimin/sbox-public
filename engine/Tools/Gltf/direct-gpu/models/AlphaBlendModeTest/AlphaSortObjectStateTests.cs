using System;
using System.Collections;
using System.Linq;
using System.Reflection;
namespace Sandbox.Gltf.DirectGpu.AlphaBlendModeValidation;
[TestClass]
public sealed class AlphaSortObjectStateTests
{
 [TestMethod]
 public void TransparentUnitFollowsPrimaryStateOverridesVisibilityAndLifetime()
 {
  var scene=Scene.CreateEditorScene();var opaque=Material.Load("materials/default/white.vmat").CreateCopy();var blend=opaque.CreateCopy();GltfGpuAsset asset=null;GltfGpuInstance instance=null;
  try
  {
   blend.SetFeature("F_TRANSLUCENT",1);Assert.IsTrue(blend.Flags.IsTranslucent&&!opaque.Flags.IsTranslucent);
   using var doc=GltfReader.Read(AlphaControlFixtures.All().Single(x=>x.Name=="mask_depth_equal").Bytes, supportedExtensions: new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal) { "KHR_node_visibility" });
   asset=GltfGpuAsset.Create(doc,"shaders/gltf_deform_cs.shader",index=>index==0?blend:opaque);instance=asset.CreateInstance(scene.SceneWorld);
   const BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Instance;var rows=((IEnumerable)typeof(GltfGpuInstance).GetField("draws",flags).GetValue(instance)).Cast<object>().ToArray();Assert.AreEqual(2,rows.Length);
   var field=rows[0].GetType().GetField("SortObject",flags);var child=(SceneCustomObject)field.GetValue(rows[0]);Assert.IsTrue(child.IsValid()&&child.RenderingEnabled&&child.Flags.IsTranslucent);Assert.IsFalse(((SceneCustomObject)field.GetValue(rows[1])).IsValid(),"Opaque primitive must stay batched in primary object.");
   var primary=instance.SceneObject;primary.ColorTint=new Color(.2f,.4f,.6f,.8f);Assert.AreEqual(primary.ColorTint,child.ColorTint);primary.Tags.Add("alpha_sort_probe");Assert.IsTrue(child.Tags.Has("alpha_sort_probe"));primary.Flags.ExcludeGameLayer=true;Assert.IsTrue(child.Flags.ExcludeGameLayer);
   primary.RenderingEnabled=false;Assert.IsFalse(child.RenderingEnabled);primary.RenderingEnabled=true;Assert.IsTrue(child.RenderingEnabled);
   instance.SetNodeVisibilityOverrides(new System.Collections.Generic.Dictionary<int,bool>{{0,false}});Assert.IsFalse(child.RenderingEnabled);instance.SetNodeVisibilityOverrides(new System.Collections.Generic.Dictionary<int,bool>());Assert.IsTrue(child.RenderingEnabled);
   instance.SetMaterialOverride(0,opaque);Assert.IsFalse(child.RenderingEnabled);instance.ClearMaterialOverrides();Assert.IsTrue(child.RenderingEnabled);
   var invalid=opaque.CreateCopy();invalid.Destroy();Assert.ThrowsException<GltfException>(()=>instance.SetMaterialOverride(0,invalid));Assert.IsTrue(child.RenderingEnabled&&child.Flags.IsTranslucent,"Invalid override changed active sort unit.");
   var injected=false;void FailOnce(Sandbox.Rendering.SceneObjectChange change){if(!injected&&(change&Sandbox.Rendering.SceneObjectChange.Flags)!=0){injected=true;throw new InvalidOperationException("injected primary flag publication failure");}}
   primary.DependentStateChanged+=FailOnce;
   try{Assert.ThrowsException<InvalidOperationException>(()=>instance.SetMaterialOverride(0,opaque));}finally{primary.DependentStateChanged-=FailOnce;}
   Assert.IsTrue(injected&&primary.Flags.IsOpaque&&primary.Flags.IsTranslucent&&child.IsValid()&&child.RenderingEnabled&&child.Flags.IsTranslucent,"Rollback did not restore effective classification and native child state.");
   using(scene.Push()){var component=scene.CreateObject().Components.Create<ModelRenderer>(false);primary.Component=component;primary.CallbackTarget=component;Assert.AreSame(component,child.Component);Assert.AreSame(component,child.CallbackTarget);}
   instance.Dispose();instance=null;Assert.IsFalse(child.IsValid(),"Disposed instance retained its native sort unit.");asset.Dispose();asset=null;
  }
  finally{instance?.Dispose();asset?.Dispose();blend.Destroy();opaque.Destroy();scene.Destroy();}
 }
 [TestMethod]
 public void DeletingPublicPrimaryRetiresOwnedTransparentUnits()
 {
  var scene=Scene.CreateEditorScene();var opaque=Material.Load("materials/default/white.vmat").CreateCopy();var blend=opaque.CreateCopy();GltfGpuAsset asset=null;GltfGpuInstance instance=null;
  try
  {
   blend.SetFeature("F_TRANSLUCENT",1);using var doc=GltfReader.Read(AlphaControlFixtures.All().Single(x=>x.Name=="mask_depth_equal").Bytes);
   asset=GltfGpuAsset.Create(doc,"shaders/gltf_deform_cs.shader",index=>index==0?blend:opaque);instance=asset.CreateInstance(scene.SceneWorld);
   const BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Instance;var row=((IEnumerable)typeof(GltfGpuInstance).GetField("draws",flags).GetValue(instance)).Cast<object>().First();var child=(SceneCustomObject)row.GetType().GetField("SortObject",flags).GetValue(row);Assert.IsTrue(child.IsValid());
   instance.SceneObject.Delete();Assert.IsFalse(child.IsValid(),"Deleting public primary left a transparent native unit alive.");Assert.ThrowsException<ObjectDisposedException>(()=>instance.SetMaterialOverride(0,opaque));instance.Dispose();instance=null;asset.Dispose();asset=null;
  }
  finally{instance?.Dispose();asset?.Dispose();blend.Destroy();opaque.Destroy();scene.Destroy();}
 }

}
