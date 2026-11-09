using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Sandbox;
using Sandbox.Gltf;

namespace Sandbox.Gltf.DirectGpu.MandarinOrangeValidation;

internal static class MandarinOrangeTests
{
 public sealed record Result(string Name, string Form, bool Passed, string Error, string InputSha256);
 static void Require(bool value,string message) { if(!value) throw new InvalidOperationException(message); }
 public static Result[] RunAll(string checkout)
 {
  var results=new List<Result>();string inputHash=null;
  var directory=Path.Combine(checkout,"Temp/gltf-reference/glTF-Sample-Assets/Models/MandarinOrange/glTF");
  var source=File.ReadAllBytes(Path.Combine(directory,"MandarinOrange.gltf"));
  Require(Convert.ToHexStringLower(SHA256.HashData(source))=="148a260776eb8258f1d90c1623bad1bdca17b1bec42da597536ef09e816c2091","Pinned source changed");
  foreach(var dependency in new[]{
   ("MandarinOrange.bin","15bc58559ab4b60e6e1a94449940911e1c5ebc4c021004b7f4fac54323063cfd"),
   ("MandarinOrange_Basecolor.jpg","a652724500c853d1470cf2c37d97ea3741f2f1bd206322fe8e9a3b99f9cdaedd"),
   ("MandarinOrange_DiffuseTransmission.png","6ccaa5a695cf3916d66f7a13565fd0b4a672b2d99f11327b9e36451f5ca49842"),
   ("MandarinOrange_Normal.png","45fe254d98974f3dadc161100b9175c7f8f3377ab3881a5b9835503e1f0a1dd5"),
   ("MandarinOrange_OcclusionRough.jpg","08a87a3a66198992988ea98adf69c0d6d787b0b863b5a0f56921178f03152c34"),
  }) Require(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(directory,dependency.Item1))))==dependency.Item2,"Pinned dependency changed: "+dependency.Item1);
  byte[] Resolve(string uri)=>File.ReadAllBytes(Path.Combine(directory,uri));
  foreach(bool compiled in new[]{false,true})
  {
   string form=compiled ? "glTF/derived-glb_c" : "glTF/source";
   byte[] bytes=compiled ? GltfCompiler.Compile(source,Resolve) : source;
   inputHash=Convert.ToHexStringLower(SHA256.HashData(bytes));
   if(compiled){var artifacts=Path.Combine(checkout,"Temp/gltf-validation/direct-gpu/MandarinOrange");Directory.CreateDirectory(artifacts);File.WriteAllBytes(Path.Combine(artifacts,"MandarinOrange.glb_c"),bytes);}
   GltfDocument Read()=>GltfModelLoader.ReadDocument(bytes,Resolve);
   Run("valid_no_default_preserves_real_scene",form,()=>{using var d=Read(); Require(d.GetDefaultScene()==null,"Invented a default scene"); Require(d.Root.GetProperty("scenes").GetArrayLength()==1 && d.Root.GetProperty("nodes").GetArrayLength()==2 && d.Root.GetProperty("meshes").GetArrayLength()==1,"Authored graph changed"); Require(d.Root.GetProperty("scenes")[0].GetProperty("nodes")[0].GetInt32()==1,"Wrong scene root");});
   Run("independent_interleaved_position_oracle",form,()=>{using var d=Read(); var p=d.ReadPrimitive(0,0);Require(p.VertexCount==24138 && p.Indices.Length==122508 && p.Mode==4 && p.Material==0,"Wrong primitive inventory"); var positions=d.ReadFloatAccessor(p.Attributes["POSITION"]); var samples=new[]{(0,new[]{.002522495109587908f,-.005430183839052916f,-.004190611653029919f}),(42,new[]{.0011712511768564582f,-.0051133232191205025f,-.0042830295860767365f}),(12000,new[]{.00506800040602684f,.0011182439047843218f,.002062230370938778f}),(24137,new[]{-.003804005915299058f,.0023642308078706264f,.0001588021987117827f})}; foreach(var (index,expected) in samples) for(int c=0;c<3;c++) Require(positions[index*3+c]==expected[c],"Position mismatch at "+index+" component "+c); Require(positions[0]!=positions[3],"Negative wrong-vertex control lacks sensitivity"); var upload=d.MaterializePrimitiveUpload(0,0);Require(upload.Layout.VertexCount==24138 && upload.Attributes.ContainsKey("NORMAL") && upload.Attributes.ContainsKey("TEXCOORD_0"),"Upload lost attributes");});
   Run("optional_extension_standard_core_fallback",form,()=>{using var d=Read();Require(d.UnsupportedOptionalExtensions.SequenceEqual(new[]{"KHR_materials_diffuse_transmission"}),"Optional extension diagnostic missing");var m=d.ReadMaterial(0);Require(m.BaseColorTexture.Index==0 && m.NormalTexture.Index==2 && m.NormalTexture.Scale==.5f && m.MetallicRoughnessTexture.Index==3 && m.OcclusionTexture.Index==3 && m.MetallicFactor==1 && m.RoughnessFactor==1,"Core fallback material changed");});
   Run("public_unselected_resource_is_valid_without_default",form,()=>WithModel(bytes,directory,compiled,path=>{var model=Model.Load(path);try{Require(model.IsValid && !model.IsError && model.native.IsNull,"Valid managed resource required without native placeholder");Require(model.GltfSceneCount==1 && model.GltfDefaultScene==null && model.GltfSelectedScene==null && model.MeshCount==0 && !model.HasRenderMeshes(),"Selected-presentation metadata must be empty, authored scene inventory preserved");Require(model.GltfResource.Asset.Document.Root.GetProperty("meshes").GetArrayLength()==1,"Unselected asset lost real geometry");Require(ReferenceEquals(model,Model.Load(model.ResourcePath)),"ResourcePath roundtrip lost unselected resource");try{using var instance=model.GltfResource.CreateInstance(null);throw new InvalidOperationException("Default renderer selected scene silently");}catch(GltfException e){Require(e.Message.Contains("no default scene"),"Wrong render-intent error");}}finally{model.Destroy();}}));
   Run("explicit_actual_scene_cpu_geometry",form,()=>{using var d=Read();using var asset=GltfGpuAsset.Prepare(d,GltfGpuAsset.DefaultComputeShaderPath,_=>null,sceneIndex:0);Require(asset.SelectedSceneIndex==0 && asset.MeshCount==1 && asset.Nodes.Single().Index==0,"Wrong real scene import");Require(d.GetDefaultScene()==null,"Scene import rewrote source default");var metadata=new GltfManagedModelMetadata(d,0);Require(metadata.Bones.Count==0 && metadata.Morphs.Count==0,"Unexpected authored animation metadata");});
   Run("public_explicit_actual_scene_source_material",form,()=>WithModel(bytes,directory,compiled,path=>{var model=Model.Load(path,0);try{Require(model.IsValid && !model.IsError && model.native.IsNull && model.GltfSelectedScene==0 && model.MeshCount==1,"Explicit scene must be real managed geometry");}finally{model.Destroy();}}));
  }
  inputHash=null;
  Run("required_diffuse_transmission_rejection_negative_control","Mandarin-required-extension-control",()=>{var required=System.Text.Json.Nodes.JsonNode.Parse(source).AsObject();required["extensionsRequired"]=new System.Text.Json.Nodes.JsonArray("KHR_materials_diffuse_transmission");try{using var d=GltfModelLoader.ReadDocument(Encoding.UTF8.GetBytes(required.ToJsonString()),Resolve);throw new InvalidOperationException("Unsupported required transmission extension silently fell back");}catch(GltfException e){Require(e.Message.Contains("KHR_materials_diffuse_transmission"),"Missing actual required extension diagnostic");}});
  foreach(var fixture in new[]{("entity-library","{\"asset\":{\"version\":\"2.0\"}}"),("empty-default-scene","{\"asset\":{\"version\":\"2.0\"},\"scenes\":[{}],\"scene\":0}")})
   Run("valid_"+fixture.Item1,"core-fixture",()=>{using var d=GltfModelLoader.ReadDocument(Encoding.UTF8.GetBytes(fixture.Item2));Require(d!=null,"Valid document rejected");});
  return results.ToArray();
  void Run(string name,string form,Action check){try{check();results.Add(new(name,form,true,null,inputHash));}catch(Exception e){results.Add(new(name,form,false,e.ToString(),inputHash));}}
 }
 static void WithModel(byte[] bytes,string sourceDirectory,bool compiled,Action<string> check)
 {
  string directory=Path.Combine(Path.GetTempPath(),"gltf_mandarin_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);LocalFileSystem mount=null;var aggregate=FileSystem.Mounted;
  try {foreach(var file in Directory.GetFiles(sourceDirectory))File.Copy(file,Path.Combine(directory,Path.GetFileName(file)));string name="mandarin_"+Guid.NewGuid().ToString("N")+(compiled ? ".glb_c" : ".gltf");File.WriteAllBytes(Path.Combine(directory,name),bytes);mount=new LocalFileSystem(directory);aggregate.Mount(mount);check(name);}
  finally{try{if(mount!=null)aggregate.UnMount(mount);}finally{mount?.Dispose();Directory.Delete(directory,true);}}
 }
}
