using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using Sandbox.Gltf;

namespace Sandbox.Gltf.DirectGpu.CarConceptValidation;

public static class CarConceptTireTests
{
 public sealed record Result(string Name,string Form,bool Passed,string Error);
 public static Result[] Run(string root)
 {
  using var audit=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root,"research/gltf-validation/direct-gpu/models/CarConcept/reader-source-audit.json")));
  foreach(var file in audit.RootElement.GetProperty("files").EnumerateArray()) {
   var path=Path.Combine(root,file.GetProperty("path").GetString());
   if(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)))!=file.GetProperty("sha256").GetString()) throw new InvalidOperationException("Changed pinned input: "+path);
  }
  var rows=new List<Result>();var selected=new HashSet<string>{"KHR_texture_transform"};
  foreach(var (form,name) in new[]{("glTF","CarConcept.gltf"),("glTF-Binary","CarConcept.glb"),("glTF-JPG","CarConcept.gltf")}) {
   string dir=Path.Combine(root,"Temp/gltf-reference/glTF-Sample-Assets/Models/CarConcept",form);
   byte[] source=File.ReadAllBytes(Path.Combine(dir,name)); byte[] Resolve(string uri)=>File.ReadAllBytes(Path.Combine(dir,uri));
   foreach(bool compiled in new[]{false,true}) {
    string label=form+(compiled?"/compiled":"/source");
    try {
     byte[] input=compiled?GltfCompiler.Compile(source,Resolve,selected):source;
     using var doc=GltfReader.Read(input,Resolve,selected);
     foreach(int mesh in new[]{89,91,93,95}) {
      Require(doc.ReadPrimitive(mesh,0).Material==18 && doc.ReadPrimitive(mesh,1).Material==19,"wheel side/tread material routing");
      foreach(int primitive in new[]{0,1}) {
       var upload=doc.MaterializePrimitiveUpload(mesh,primitive);
       Require(new[]{"POSITION","NORMAL","TANGENT","TEXCOORD_0","TEXCOORD_1"}.All(k=>upload.Attributes.ContainsKey(k)),"wheel vertex roles");
      }
     }
     var side=doc.ReadMaterial(18);var tread=doc.ReadMaterial(19);
     Require(side.MetallicFactor==0 && side.RoughnessFactor==.4f && tread.MetallicFactor==0 && tread.RoughnessFactor==.6f,"rubber PBR factors");
     Require(Math.Abs(tread.BaseColorFactor.X-.0090214927f)<1e-8f && tread.BaseColorFactor.X==tread.BaseColorFactor.Y && tread.BaseColorFactor.Y==tread.BaseColorFactor.Z,"tread linear base");
     Require(side.BaseColorTexture.Index==11 && side.BaseColorTexture.IsSrgb && side.NormalTexture.Index==12 && !side.NormalTexture.IsSrgb && tread.NormalTexture.Index==13 && !tread.NormalTexture.IsSrgb,"tire role bindings or color spaces");
     foreach(var mat in new[]{side,tread}) Require(mat.OcclusionTexture.Index==2 && mat.OcclusionTexture.TexCoord==1 && Math.Abs(mat.OcclusionTexture.Strength-.85f)<1e-6f && !mat.OcclusionTexture.IsSrgb,"wheel AO binding");
     var uv=tread.NormalTexture.TransformUv(new Vector2(.25f,.5f));Require(Vector2.Distance(uv,new Vector2(.25f,3.5f))<1e-6f,"tread transform");
     foreach(int i in new[]{2,11,12,13}) { var tex=doc.ReadTexture(i); Require(tex.Sampler==0,"tire sampler binding"); }
     var sampler=doc.ReadSampler(0);Require(sampler.MagFilter==9729 && sampler.MinFilter==9987 && sampler.WrapS==10497 && sampler.WrapT==10497,"linear trilinear repeat sampler");
     rows.Add(new("tire_source_materials",label,true,null));
    }catch(Exception e){rows.Add(new("tire_source_materials",label,false,e.ToString()));}
   }
  }
  return rows.ToArray();
 }
 static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
