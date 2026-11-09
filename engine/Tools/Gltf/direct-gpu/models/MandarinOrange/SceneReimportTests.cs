using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Editor;
using Sandbox;
using Sandbox.Gltf;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Assert=Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace Sandbox.Gltf.DirectGpu.MandarinOrangeValidation;

[TestClass]
internal sealed class SceneReimportTests
{
 [TestMethod]
 [DataRow(false)]
 [DataRow(true)]
 public void RealAssetChangeRefreshesEverySelectedViewWithExactSourceIdentity(bool binary)
 {
  using var fixture=new ImportFixture(binary);
  var unselected=fixture.Load();var first=fixture.Load(0);var second=fixture.LoadCompiled(1);
  Assert.AreSame(second,Model.Load(fixture.Asset.RelativePath,1),"Compiled-alias view must share the canonical source-scene identity");
  var old=new[]{unselected.GltfResource,first.GltfResource,second.GltfResource};
  // Same basename in the other editable source format and a path prefix sibling
  // are independent assets. A filename-prefix fan-out would incorrectly reload them.
  var sibling=fixture.Sibling(opposite:true);var prefix=fixture.Sibling(opposite:false);
  var siblingGeneration=sibling.GltfResource;var prefixGeneration=prefix.GltfResource;
  var arbitraryGeneration=new GltfManagedModelResource(GltfModelLoader.ReadDocument(Encoding.UTF8.GetBytes(SceneResourceTests.Fixture(false))));
  var arbitrary=Model.FromGltfResource(arbitraryGeneration,"arbitrary_managed_"+Guid.NewGuid().ToString("N"));fixture.Track(arbitrary);
  var native=Model.Load("models/dev/box.vmdl");Assert.IsTrue(native.IsValid && native.GltfResource==null && !native.native.IsNull);
  var nativeCount=native.MeshCount;var before=second.Bounds;
  fixture.Change(SceneResourceTests.Fixture(false).Replace("[5,0,0]","[7,0,0]",StringComparison.Ordinal));
  GltfAssetImport.Reimport(fixture.Asset);
  Assert.IsTrue(unselected.IsValid && unselected.GltfSelectedScene==null && unselected.GltfSceneCount==2);
  Assert.AreEqual(0,first.GltfSelectedScene);Assert.AreEqual(1,second.GltfSelectedScene);Assert.AreNotEqual(before,second.Bounds);
  Assert.IsTrue(old.All(resource=>!resource.IsValid),"Previous owners were not released after the group committed");
  Assert.AreSame(first,Model.Load(first.ResourcePath));Assert.AreSame(second,Model.Load(second.ResourcePath));Assert.AreSame(second,Model.Load(GltfModelLoader.CompiledPath(fixture.Asset.RelativePath)+"#scene=1"),"Reload lost the compiled-alias scene view");
  Assert.AreSame(siblingGeneration,sibling.GltfResource);Assert.AreSame(prefixGeneration,prefix.GltfResource);Assert.AreSame(arbitraryGeneration,arbitrary.GltfResource);Assert.IsTrue(arbitrary.IsValid);
  Assert.AreSame(native,Model.Load("models/dev/box.vmdl"));Assert.AreEqual(nativeCount,native.MeshCount);Assert.IsNull(native.GltfResource);
  using var compiled=GltfModelLoader.ReadDocument(File.ReadAllBytes(fixture.Output));Assert.IsNull(compiled.GetDefaultScene());
 }
 [TestMethod]
 [DataRow(false)]
 [DataRow(true)]
 public void RemovedExplicitSceneKeepsEveryLastValidViewAndPriorCompiledFile(bool binary)
 {
  using var fixture=new ImportFixture(binary);var unselected=fixture.Load();var first=fixture.Load(0);var second=fixture.Load(1);
  var old=new[]{unselected.GltfResource,first.GltfResource,second.GltfResource};var bytes=File.ReadAllBytes(fixture.Output);
  fixture.Change(SceneResourceTests.Fixture(false).Replace(",\"scenes\":[{\"nodes\":[0]},{\"nodes\":[1]}]",",\"scenes\":[{\"nodes\":[0]}]",StringComparison.Ordinal));
  Assert.ThrowsException<ArgumentOutOfRangeException>(()=>GltfAssetImport.Reimport(fixture.Asset));
  CollectionAssert.AreEqual(bytes,File.ReadAllBytes(fixture.Output));Assert.AreSame(old[0],unselected.GltfResource);Assert.AreSame(old[1],first.GltfResource);Assert.AreSame(old[2],second.GltfResource);
  Assert.IsTrue(old.All(resource=>resource.IsValid),"Failed preparation released a last-valid owner");
 }
 [TestMethod]
 public void LateRendererApplicationFailureRollsBackAllViewsAndDisk()
 {
  using var fixture=new ImportFixture(false);var first=fixture.Load(0);var second=fixture.Load(1);
  using var presentation=new Presentation(first,second);
  var firstOld=first.GltfResource;var secondOld=second.GltfResource;var firstInstance=presentation.First.GltfInstance;var secondInstance=presentation.Second.GltfInstance;
  var bytes=File.ReadAllBytes(fixture.Output);presentation.Second.FailNextCreation=true;
  fixture.Change(SceneResourceTests.Fixture(false).Replace("[5,0,0]","[7,0,0]",StringComparison.Ordinal));
  Assert.ThrowsException<InvalidOperationException>(()=>GltfAssetImport.Reimport(fixture.Asset));
  Assert.AreSame(firstOld,first.GltfResource);Assert.AreSame(secondOld,second.GltfResource);Assert.IsTrue(firstOld.IsValid && secondOld.IsValid);
  Assert.AreSame(firstInstance,presentation.First.GltfInstance);Assert.AreSame(secondInstance,presentation.Second.GltfInstance);
  CollectionAssert.AreEqual(bytes,File.ReadAllBytes(fixture.Output));
 }
 [TestMethod]
 public void PostCommitNotificationFailureKeepsAllViewsAndDiskNewAndIndependentLeaseAlive()
 {
  using var fixture=new ImportFixture(false);var first=fixture.Load(0);var second=fixture.Load(1);using var presentation=new Presentation(first,second);
  var firstOld=first.GltfResource;var secondOld=second.GltfResource;var previousFirst=presentation.First.GltfInstance;var previousSecond=presentation.Second.GltfInstance;
  // An external instance owns a separate old-generation lease across reimport.
  using var retained=firstOld.CreateInstance(presentation.Scene.SceneWorld);
  bool groupFinishedBeforeNotification=false;
  int notifications=0;void Fail(){notifications++;var plan=typeof(ModelRenderer).GetField("gltfReloadPlan",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);groupFinishedBeforeNotification=plan.GetValue(presentation.First)==null && plan.GetValue(presentation.Second)==null && !firstOld.IsValid && !secondOld.IsValid;throw new InvalidOperationException("Injected post-commit external failure");}void Observe(){notifications++;}
  presentation.First.ModelChanged+=Fail;presentation.Second.ModelChanged+=Observe;
  try
  {
   var bytes=File.ReadAllBytes(fixture.Output);fixture.Change(SceneResourceTests.Fixture(false).Replace("[5,0,0]","[7,0,0]",StringComparison.Ordinal));GltfAssetImport.Reimport(fixture.Asset);
   Assert.IsFalse(bytes.SequenceEqual(File.ReadAllBytes(fixture.Output)));Assert.AreEqual(2,notifications,"Notification failure stopped another committed view");Assert.IsTrue(groupFinishedBeforeNotification,"An external observer saw a scene view before the entire group finalized its reload plan and released prior owners");
   Assert.IsFalse(firstOld.IsValid);Assert.IsFalse(secondOld.IsValid);Assert.IsTrue(first.IsValid && second.IsValid);
   Assert.AreNotSame(previousFirst,presentation.First.GltfInstance);Assert.AreNotSame(previousSecond,presentation.Second.GltfInstance);
   Assert.IsTrue(retained.SceneObject.IsValid());retained.SetPose(firstOld.Asset.Document.EvaluatePose());
   Assert.AreSame(first,Model.Load(first.ResourcePath));Assert.AreSame(second,Model.Load(second.ResourcePath));
  }
  finally{presentation.First.ModelChanged-=Fail;presentation.Second.ModelChanged-=Observe;}
 }
 [Expose]
 public sealed class FailingRenderer:ModelRenderer
 {
  internal bool FailNextCreation;
  internal override void OnSceneObjectCreated(SceneObject value){base.OnSceneObjectCreated(value);if(FailNextCreation){FailNextCreation=false;throw new InvalidOperationException("Injected late selected-view renderer apply failure");}}
 }
 sealed class Presentation:IDisposable
 {
  public readonly Scene Scene=Scene.CreateEditorScene();public readonly ModelRenderer First;public readonly FailingRenderer Second;
  public Presentation(Model first,Model second){try{using var scope=Scene.Push();First=Scene.CreateObject().Components.Create<ModelRenderer>(false);First.Model=first;First.Enabled=true;Second=Scene.CreateObject().Components.Create<FailingRenderer>(false);Second.Model=second;Second.Enabled=true;Scene.EditorTick(0,0);Assert.IsNotNull(First.GltfInstance);Assert.IsNotNull(Second.GltfInstance);}catch{Scene.Destroy();throw;}}
  public void Dispose()=>Scene.Destroy();
 }
 sealed class ImportFixture:IDisposable
 {
  public readonly Asset Asset;public readonly string Output;readonly string directory,source;readonly bool binary;readonly List<Model> models=new();
  public ImportFixture(bool binary)
  {
   this.binary=binary;directory=Path.Combine(Project.Current.GetAssetsPath(),"codex_gltf_scene_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);source=Path.Combine(directory,"selection"+(binary ? ".glb" : ".gltf"));try{Change(SceneResourceTests.Fixture(false));Asset=AssetSystem.RegisterFile(source)??throw new InvalidOperationException("Could not register owned editable asset");Assert.IsTrue(GltfAssetImport.CanImport(Asset));Output=GltfModelLoader.CompiledPath(source);GltfAssetImport.Reimport(Asset);}catch{Directory.Delete(directory,true);throw;}
  }
  public void Change(string json){var bytes=Encoding.UTF8.GetBytes(json);File.WriteAllBytes(source,binary ? GltfCompiler.Compile(bytes) : bytes);}
  public void Track(Model model)=>models.Add(model);
  public Model LoadCompiled(int scene){var model=Model.Load(GltfModelLoader.CompiledPath(Asset.RelativePath),scene);models.Add(model);return model;}
  public Model Load(int? scene=null){var model=scene.HasValue ? Model.Load(Asset.RelativePath,scene.Value) : Model.Load(Asset.RelativePath);models.Add(model);return model;}
  public Model Sibling(bool opposite)
  {
   var name=opposite ? "selection"+(binary ? ".gltf" : ".glb") : "selection_extra"+(binary ? ".glb" : ".gltf");var bytes=Encoding.UTF8.GetBytes(SceneResourceTests.Fixture(false));File.WriteAllBytes(Path.Combine(directory,name),(opposite ? !binary : binary) ? GltfCompiler.Compile(bytes) : bytes);var asset=AssetSystem.RegisterFile(Path.Combine(directory,name));Assert.IsNotNull(asset);var model=Model.Load(asset.RelativePath,0);models.Add(model);return model;
  }
  public void Dispose(){try{foreach(var model in models.Distinct())model.Destroy();}finally{if(Directory.Exists(directory))Directory.Delete(directory,true);}}
 }
}

[TestClass]
internal sealed class SceneGroupObserverTests
{
 [TestMethod]
 public void EveryViewFinalizesBeforeTheFirstExternalCallback()=>new SceneReimportTests().PostCommitNotificationFailureKeepsAllViewsAndDiskNewAndIndependentLeaseAlive();
}
