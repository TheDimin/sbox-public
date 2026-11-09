using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sandbox;
using Sandbox.Gltf;

namespace Sandbox.Gltf.DirectGpu.ABeautifulGameValidation;
public static class PublicTests
{
 public sealed record Result(string Name,string Form,bool Passed,string Error);
 public static Result[] RunAll(string checkout)
 {
  var rows=new List<Result>();
  foreach(var variant in new[]{"glTF","glTF-Binary"}) foreach(bool compiled in new[]{false,true})
  {
   var name=variant+(compiled?"/compiled_glb_c":"/source");
   try { Test(checkout,variant,compiled); rows.Add(new("public_load_and_material_realization",name,true,null)); } catch(Exception e) { rows.Add(new("public_load_and_material_realization",name,false,e.ToString())); }
  }
  return rows.ToArray();
 }
 static void Test(string checkout,string variant,bool compiled)
 {
  var source=Path.Combine(checkout,"Temp/gltf-reference/glTF-Sample-Assets/Models/ABeautifulGame",variant); var sourceFile=Path.Combine(source,"ABeautifulGame"+(variant=="glTF"?".gltf":".glb"));
  var directory=Path.Combine(Path.GetTempPath(),"beautiful_"+Guid.NewGuid().ToString("N")); LocalFileSystem mount=null; Model model=null; var aggregate=FileSystem.Mounted;
  try
  {
   Directory.CreateDirectory(directory); var bytes=File.ReadAllBytes(sourceFile); byte[] Resolve(string uri)=>File.ReadAllBytes(Path.Combine(source,uri));
   if(compiled) bytes=GltfCompiler.Compile(bytes,Resolve,new HashSet<string>{"KHR_draco_mesh_compression","KHR_texture_basisu"});
   if(variant=="glTF") foreach(var file in Directory.EnumerateFiles(source)) if(Path.GetExtension(file) is ".bin" or ".jpg") File.Copy(file,Path.Combine(directory,Path.GetFileName(file)));
   var name="beautiful_"+Guid.NewGuid().ToString("N")+(compiled?".glb_c":variant=="glTF"?".gltf":".glb"); File.WriteAllBytes(Path.Combine(directory,name),bytes);
   mount=new LocalFileSystem(directory); aggregate.Mount(mount); model=Model.Load(name);
   Require(model is { IsValid:true, IsError:false } && model.native.IsNull && model.GltfResource is not null,"public managed Model.Load failed");
   Require(model.GltfResource.Asset.MeshCount==49,"expected 49 authored node primitive instances");
   var materials=model.Materials; Require(materials.Length==15 && materials.All(x=>x is { IsValid:true,IsError:false }),"15 actual material resources not realized");
  }
  finally { if(model!=null && model.ResourcePath!=null && model.ResourcePath.StartsWith("beautiful_")) model.Destroy(); if(mount!=null) { aggregate.UnMount(mount); mount.Dispose(); } if(Directory.Exists(directory)) Directory.Delete(directory,true); }
 }
 static void Require(bool value,string message) { if(!value) throw new InvalidOperationException(message); }
}
