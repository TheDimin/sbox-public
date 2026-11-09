using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Engine;
namespace Sandbox.Gltf.DirectGpu.MaterialsVariantsShoeValidation;
[TestClass]
public sealed class NativeAccessorRollbackTests
{
 sealed class FailingTarget : MaterialAccessor.ITarget
 {
  readonly MaterialAccessor.ITarget inner; public bool Fail;
  public FailingTarget(MaterialAccessor.ITarget inner){this.inner=inner;}
  public bool IsValid=>inner.IsValid;
  public int GetMaterialCount()=>inner.GetMaterialCount();
  public Material Get(int i)=>inner.Get(i);
  public void SetOverride(int i,Material material){if(Fail)throw new InvalidOperationException("injected target failure");inner.SetOverride(i,material);}
  public void ClearOverrides()=>inner.ClearOverrides();
 }
 [TestMethod]
 public void NativeTargetExceptionPreservesAccessorState()
 {
  var scene=Scene.CreateEditorScene();Material owned=null;
  try
  {
   using var scope=scene.Push();var renderer=scene.CreateObject().Components.Create<ModelRenderer>(false);renderer.Model=Model.Cube;renderer.Enabled=true;scene.EditorTick(0,0);
   Assert.IsTrue(renderer.SceneObject.IsValid() && renderer.SceneObject is not SceneCustomObject);var target=new FailingTarget((MaterialAccessor.ITarget)renderer);var accessor=new MaterialAccessor(target);Assert.AreEqual(1,accessor.Count);
   owned=Material.Load("materials/default/white.vmat").CreateCopy("native_accessor_"+Guid.NewGuid().ToString("N"));accessor.SetOverride(0,owned);Assert.AreSame(owned,accessor.GetOverride(0));
   target.Fail=true;Assert.ThrowsException<InvalidOperationException>(()=>accessor.SetOverride(0,null));Assert.IsTrue(accessor.HasOverride(0));Assert.AreSame(owned,accessor.GetOverride(0));
   Assert.ThrowsException<InvalidOperationException>(()=>accessor.SetOverride(1,owned));Assert.IsFalse(accessor.HasOverride(1));
   target.Fail=false;accessor.SetOverride(0,null);Assert.IsFalse(accessor.HasOverride(0));
  }
  finally{scene.Destroy();owned?.Destroy();}
 }
}
