using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sandbox.Gltf;
namespace Sandbox.Gltf.DirectGpu.MaterialsVariantsShoeValidation;

public static class ShoePublicSelectionTests
{
 public sealed record Result(string Name, bool Passed, string Error);
 public static Result[] Run(string root)
 {
  var results = new List<Result>();
  foreach (var binary in new[]{false,true}) foreach (var compiled in new[]{false,true})
  {
   try { Check(root,binary,compiled); results.Add(new((binary ? "glTF-Binary/" : "glTF/") + (compiled ? "compiled" : "source"),true,null)); }
   catch(Exception e) { results.Add(new((binary ? "glTF-Binary/" : "glTF/") + (compiled ? "compiled" : "source"),false,e.ToString())); }
  }
  return results.ToArray();
 }
 public static Result[] RunCompiledGltfOnly(string root)
 {
  try { Check(root,false,true); return new[]{new Result("glTF/compiled_with_external_dependencies",true,null)}; }
  catch(Exception e) { return new[]{new Result("glTF/compiled_with_external_dependencies",false,e.ToString())}; }
 }
 static void Check(string root,bool binary,bool compiled)
 {
  var sourceDir=Path.Combine(root,"Temp/gltf-reference/glTF-Sample-Assets/Models/MaterialsVariantsShoe",binary ? "glTF-Binary" : "glTF");
  var directory=Path.Combine(Path.GetTempPath(),"shoe_public_"+Guid.NewGuid().ToString("N"));
  var name="shoe_"+Guid.NewGuid().ToString("N")+(compiled?".glb_c":binary?".glb":".gltf");
  var aggregate=Sandbox.FileSystem.Mounted; LocalFileSystem mount=null; Model model=null; Scene scene=null;
  try
  {
   Directory.CreateDirectory(directory); var input=File.ReadAllBytes(Path.Combine(sourceDir,binary?"MaterialsVariantsShoe.glb":"MaterialsVariantsShoe.gltf"));
   byte[] Resolve(string uri)=>File.ReadAllBytes(Path.Combine(sourceDir,uri));
   if(compiled) input=GltfCompiler.Compile(input,Resolve);
   if (!binary) foreach(var f in Directory.GetFiles(sourceDir).Where(p=>!p.EndsWith(".gltf",StringComparison.OrdinalIgnoreCase))) File.Copy(f,Path.Combine(directory,Path.GetFileName(f)));
   File.WriteAllBytes(Path.Combine(directory,name),input); mount=new LocalFileSystem(directory); aggregate.Mount(mount);
   model=Model.Load(name); Require(model is {IsValid:true,IsError:false},"public load failed");
   Require(model.MaterialVariantNames.SequenceEqual(new[]{"midnight","beach","street"}),"authored names differ");
   scene=Scene.CreateEditorScene(); using var scope=scene.Push();
   ModelRenderer Create(){var r=scene.CreateObject().Components.Create<ModelRenderer>(false);r.Model=model;r.Enabled=true;scene.EditorTick(0,0);Require(r.SceneObject.IsValid(),"scene object absent");return r;}
   var a=Create();var b=Create(); Require(a.MaterialVariant is null && b.MaterialVariant is null,"initial defaults");
   a.MaterialVariant=1; b.MaterialVariant=2; Require(a.MaterialVariant==1 && b.MaterialVariant==2,"instance isolation");
   a.MaterialVariant=2; Require(a.MaterialVariant==2 && b.MaterialVariant==2,"switch B");
   try {a.MaterialVariant=3;throw new Exception("invalid index accepted");} catch(ArgumentOutOfRangeException){} Require(a.MaterialVariant==2,"invalid selection changed state");
   a.MaterialVariant=null; Require(a.MaterialVariant is null && b.MaterialVariant==2,"reset/independence");
   b.Enabled=false; scene.EditorTick(0,0); b.Enabled=true; scene.EditorTick(0,0); Require(b.MaterialVariant==2 && b.SceneObject.IsValid(),"reenable lost selection");
   a.GameObject.Destroy();scene.ProcessDeletes(); Require(b.MaterialVariant==2 && b.SceneObject.IsValid(),"destroy disturbed second instance");
  }
  finally {scene?.Destroy();model?.Destroy();if(mount is not null){aggregate.UnMount(mount);mount.Dispose();}if(Directory.Exists(directory))Directory.Delete(directory,true);}
 }
 static void Require(bool condition,string error){if(!condition)throw new InvalidOperationException(error);}
}
