using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Sandbox;
using Sandbox.Gltf;

namespace Sandbox.Gltf.DirectGpu.MandarinOrangeValidation;

internal static class SceneResourceTests
{
 public sealed record Result(string Name,string Form,bool Passed,string Error);
 static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
 public static Result[] RunAll()
 {
  var rows=new List<Result>();
  foreach(bool compiled in new[]{false,true})
  {
   string form=compiled ? "derived-glb_c" : "source-gltf";
   Run("unselected_entity_library",()=>WithModel("{\"asset\":{\"version\":\"2.0\"}}",compiled,(path,models)=>{var m=Load(path,models);Require(m.IsValid && !m.IsError && m.native.IsNull && m.GltfSceneCount==0 && m.GltfSelectedScene==null && m.MeshCount==0,"Entity-library validity/selection");}));
   Run("empty_authored_default_is_selected_valid",()=>WithModel("{\"asset\":{\"version\":\"2.0\"},\"scenes\":[{}],\"scene\":0}",compiled,(path,models)=>{var m=Load(path,models);Require(m.IsValid && !m.IsError && m.native.IsNull && m.GltfSceneCount==1 && m.GltfSelectedScene==0 && m.MeshCount==0 && !m.HasRenderMeshes(),"Empty selected scene confused with missing selection");}));
   Run("geometry_library_without_scenes",()=>WithModel(Fixture(false,false,true),compiled,(path,models)=>{var m=Load(path,models);Require(m.IsValid && m.GltfSceneCount==0 && m.GltfResource.Asset.Document.Root.GetProperty("meshes").GetArrayLength()==1 && m.MeshCount==0,"Geometry library was dropped or implicitly drawn");}));
   Run("positionless_primitive_is_valid_geometry_free_scene",()=>WithModel(Fixture(true,true),compiled,(path,models)=>{var m=Load(path,models);Require(m.IsValid && !m.IsError && m.GltfSelectedScene==1 && m.MeshCount==0 && m.GltfResource.Asset.Document.ReadPrimitive(0,0).Attributes.ContainsKey("NORMAL"),"Legal POSITION-less primitive lost or fabricated");}));
   Run("explicit_selection_identity_roundtrip_and_lifetime",()=>WithModel(Fixture(false),compiled,(path,models)=>{var asset=Load(path,models);var first=Load(path,models,0);var second=Load(path,models,1);Require(asset.GltfSelectedScene==null && first.GltfSelectedScene==0 && second.GltfSelectedScene==1,"Scene scopes conflated");Require(!ReferenceEquals(asset,first) && !ReferenceEquals(first,second) && first.ResourcePath!=second.ResourcePath,"Scene cache identities aliased");Require(first.MeshCount==1 && second.MeshCount==1 && first.GltfResource.Asset.Nodes.Single().Index==0 && second.GltfResource.Asset.Nodes.Single().Index==1,"Scene selection merged or picked wrong node");Require(ReferenceEquals(first,Model.Load(first.ResourcePath)) && ReferenceEquals(second,Model.Load(path+"#scene=01")),"Selected ResourcePath/normalized fragment roundtrip failed");Require(ReferenceEquals(first,Model.LoadAsync(first.ResourcePath).GetAwaiter().GetResult()),"Async selected ResourcePath roundtrip failed");Require(ReferenceEquals(first,Json.Deserialize<Model>(Json.Serialize(first))) && ReferenceEquals(first,Model.Load(ResourceId.Get(first))),"Model JSON/ResourceId selection roundtrip failed");Require(first.GltfDefaultScene==null && first.GltfResource.Asset.Document.GetDefaultScene()==null,"Selection mutated source default");var serialized=Json.Serialize(first);first.Destroy();var cold=Json.Deserialize<Model>(serialized);models.Add(cold);Require(cold.IsValid && !ReferenceEquals(first,cold) && cold.GltfSelectedScene==0 && cold.GltfDefaultScene==null,"Cold serialized scene selection did not rebuild a valid managed resource");Require(second.IsValid && asset.IsValid && second.MeshCount==1,"Releasing one scene invalidated other resources");}));
   Run("default_versus_explicit_same_scene_distinct_identity",()=>WithModel(Fixture(true),compiled,(path,models)=>{var d=Load(path,models);var e=Load(path,models,1);Require(d.GltfDefaultScene==1 && e.GltfDefaultScene==1 && d.GltfSelectedScene==1 && e.GltfSelectedScene==1,"Explicit same-index lost authored default");Require(d.ResourcePath!=e.ResourcePath && !ReferenceEquals(d,e),"Default import selection identity collapsed");Require(d.Bounds==e.Bounds && d.MeshCount==e.MeshCount,"Same scene selected different geometry");}));
   Run("invalid_indices_fragments_do_not_publish_or_damage_resource",()=>WithModel(Fixture(false),compiled,(path,models)=>{var valid=Load(path,models);foreach(var index in new[]{-1,2}){try{var unexpected=Model.Load(path,index);models.Add(unexpected);throw new InvalidOperationException("Invalid scene accepted");}catch(ArgumentOutOfRangeException){}}foreach(var suffix in new[]{"#scene=-1","#scene=2147483648","#scene=0#scene=1","#scene=0/../x","#unknown=0"}){try{var unexpected=Model.Load(path+suffix);models.Add(unexpected);throw new InvalidOperationException("Malformed selection accepted");}catch(GltfException){}}try{var unexpected=Model.Load(path+"#scene=1",0);models.Add(unexpected);throw new InvalidOperationException("Conflicting selection accepted");}catch(ArgumentException){}Require(valid.IsValid && ReferenceEquals(valid,Model.Load(path)),"Invalid request corrupted asset cache");}));
   Run("scene_qualified_reload_preserves_selection",()=>WithModel(Fixture(false),compiled,(path,models)=>{var selected=Load(path,models,1);var source=Encoding.UTF8.GetBytes(Fixture(false));var candidate=GltfModelLoader.PrepareReload(selected.ResourcePath,GltfCompiler.Compile(source));Require(ReferenceEquals(selected,candidate.Model) && candidate.Resource.SelectedSceneIndex==1,"Reload lost selected scene identity");Require(selected.ReplaceGltfResource(candidate.Resource)==null && selected.IsValid && selected.GltfSelectedScene==1 && selected.GltfDefaultScene==null,"Reload commit lost valid unselected asset/default distinction");}));
   if(compiled)Run("gltf_compiled_only_logical_alias_and_dependency_base",()=>WithModel(Fixture(false),true,(path,models)=>{var logical=path[..^6];var selected=Load(logical,models,0);Require(ReferenceEquals(selected,Model.Load(path,0)) && ReferenceEquals(selected,Model.Load(selected.ResourcePath)),"Compiled-only .gltf alias selected identity did not roundtrip");Require(GltfModelLoader.ResolveMountedUri(GltfModelLoader.ParseScenePath(selected.ResourcePath).Path,"data.bin")=="data.bin","Selection fragment contaminated URI resolution");}));
   void Run(string name,Action check){try{check();rows.Add(new(name,form,true,null));}catch(Exception e){rows.Add(new(name,form,false,e.ToString()));}}
  }
  return rows.ToArray();
 }
 static Model Load(string path,List<Model> models,int? scene=null){var m=scene.HasValue ? Model.Load(path,scene.Value) : Model.Load(path);models.Add(m);return m;}
 static void WithModel(string json,bool compiled,Action<string,List<Model>> check)
 {
  var directory=Path.Combine(Path.GetTempPath(),"gltf_scene_resource_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);LocalFileSystem mount=null;var models=new List<Model>();var aggregate=FileSystem.Mounted;
  try{var name="scene_"+Guid.NewGuid().ToString("N")+".gltf"+(compiled ? ".glb_c" : "");var bytes=Encoding.UTF8.GetBytes(json);File.WriteAllBytes(Path.Combine(directory,name),compiled ? GltfCompiler.Compile(bytes) : bytes);mount=new LocalFileSystem(directory);aggregate.Mount(mount);check(name,models);}
  finally{try{foreach(var model in models.Distinct())model?.Destroy();}finally{try{if(mount!=null)aggregate.UnMount(mount);}finally{mount?.Dispose();Directory.Delete(directory,true);}}}
 }
 internal static string Fixture(bool defaultScene,bool normalOnly=false,bool omitScenes=false)
 {
  var values=normalOnly ? new float[]{0,0,1,0,0,1,0,0,1} : new float[]{0,0,0,1,0,0,0,1,0};var binary=new byte[36];Buffer.BlockCopy(values,0,binary,0,36);
  return "{\"asset\":{\"version\":\"2.0\"},\"buffers\":[{\"uri\":\"data:application/octet-stream;base64,"+Convert.ToBase64String(binary)+"\",\"byteLength\":36}],\"bufferViews\":[{\"buffer\":0,\"byteLength\":36}],\"accessors\":[{\"bufferView\":0,\"componentType\":5126,\"count\":3,\"type\":\"VEC3\""+(normalOnly ? "" : ",\"min\":[0,0,0],\"max\":[1,1,0]")+"}],\"meshes\":[{\"primitives\":[{\"attributes\":{\""+(normalOnly ? "NORMAL" : "POSITION")+"\":0}}]}],\"nodes\":[{\"mesh\":0},{\"mesh\":0,\"translation\":[5,0,0]}]"+(omitScenes ? "" : ",\"scenes\":[{\"nodes\":[0]},{\"nodes\":[1]}]"+(defaultScene ? ",\"scene\":1" : ""))+"}";
 }
}
