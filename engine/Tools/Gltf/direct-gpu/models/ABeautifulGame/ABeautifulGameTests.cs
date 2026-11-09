using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Sandbox;
using Sandbox.Gltf;

namespace Sandbox.Gltf.DirectGpu.ABeautifulGameValidation;

public static class ABeautifulGameTests
{
 public sealed record Result(string Name, string Form, bool Passed, string Error);
 static readonly string[] Variants = { "glTF", "glTF-Binary", "glTF-Binary-KTX-ETC1S-Draco" };
 static readonly string[] Hashes = { "2e1b63ae34173c6e3251f749b1b8b97a32e92517aab2cfd4bfffd9158f4dd278", "bd7133b4b322aae97c589b8839dae8155ad2546acb35ae32a127e722a959d007", "d950162d41e0064be9dfac0c09d80c7a23719a37af37f08cd79385b8af0af0c1" };
 static readonly string[] Names = { "King_Black", "King_White", "Queen_Black", "Queen_White", "Chessboard", "Pawn_Top_White", "Pawn_Body_White", "Pawn_Top_Black", "Pawn_Body_Black", "Castle_Black", "Castle_White", "Knight_Black", "Knight_White", "Bishop_Black", "Bishop_White" };
 static readonly IReadOnlySet<string> Selected = new HashSet<string> { "KHR_draco_mesh_compression", "KHR_texture_basisu" };
 static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
 public static Result[] RunAll(string checkout)
 {
  var results = new List<Result>();
  for (int index=0; index<Variants.Length; index++)
  {
   int i=index; var variant=Variants[i]; var directory=Path.Combine(checkout,"Temp/gltf-reference/glTF-Sample-Assets/Models/ABeautifulGame",variant); var path=Path.Combine(directory, "ABeautifulGame"+(i==0 ? ".gltf" : ".glb"));
   GltfDocument Read() { var bytes=File.ReadAllBytes(path); Require(Convert.ToHexStringLower(SHA256.HashData(bytes))==Hashes[i],"Pinned input hash changed"); return GltfReader.Read(bytes, uri => File.ReadAllBytes(Path.Combine(directory,uri)), Selected); }
   Run("pinned_inventory",variant,() => { using var d=Read(); var root=d.Root; Require(root.GetProperty("nodes").GetArrayLength()==49 && root.GetProperty("meshes").GetArrayLength()==15 && root.GetProperty("materials").GetArrayLength()==15 && root.GetProperty("images").GetArrayLength()==33,"Inventory changed"); });
   Run("source_material_roles_and_images",variant,() => { using var d=Read(); for(int m=0;m<15;m++) { var mat=d.ReadMaterial(m); Require(d.Root.GetProperty("materials")[m].GetProperty("name").GetString()==Names[m],"Material name/order changed"); Require(mat.NormalTexture != null && !mat.NormalTexture.IsSrgb && mat.NormalTexture.TexCoord==0 && mat.NormalTexture.Scale==1,"normal role"); Require(mat.MetallicRoughnessTexture != null && !mat.MetallicRoughnessTexture.IsSrgb && mat.MetallicFactor==1 && mat.RoughnessFactor==1,"metal/rough role"); bool top=m==5 || m==7; Require((mat.OcclusionTexture==null)==top,"AO presence"); if(!top) Require(mat.OcclusionTexture.Index==mat.MetallicRoughnessTexture.Index && !mat.OcclusionTexture.IsSrgb && mat.OcclusionTexture.Strength==1 && mat.OcclusionTexture.TexCoord==0,"AO role"); Require((mat.BaseColorTexture==null)==top && (top || mat.BaseColorTexture.IsSrgb) && mat.EmissiveTexture==null && mat.EmissiveFactor==System.Numerics.Vector3.Zero,"color/emissive role"); foreach(var role in new[]{mat.BaseColorTexture,mat.NormalTexture,mat.MetallicRoughnessTexture,mat.OcclusionTexture}.Where(x=>x!=null)) { var texture=d.ReadTexture(role.Index); Require(texture.Image.HasValue,"missing selected image"); var image=d.ReadImage(texture.Image.Value); d.ValidateTextureImage(role.Index,image); Require(image.MimeType==(i==2 ? "image/ktx2" : "image/jpeg"),"wrong source codec"); } } });
   Run("all_primitive_uploads",variant,() => { using var d=Read(); for(int m=0;m<15;m++) { var p=d.ReadPrimitive(m,0); var u=d.MaterializePrimitiveUpload(m,0); Require(p.Material==m && u.Layout.VertexCount>0 && u.Indices.Count>0 && u.Attributes.ContainsKey("POSITION") && u.Attributes.ContainsKey("NORMAL") && u.Attributes.ContainsKey("TEXCOORD_0"),"Primitive data/material binding changed at "+m); } });
  }
  Run("public_required_basis_draco_load",Variants[2],() => { var directory=Path.Combine(Path.GetTempPath(),"gltf_beautiful_"+Guid.NewGuid().ToString("N")); LocalFileSystem mount=null; Model model=null; var aggregate=FileSystem.Mounted; try { Directory.CreateDirectory(directory); string name="beautiful_"+Guid.NewGuid().ToString("N")+".glb"; File.Copy(Path.Combine(checkout,"Temp/gltf-reference/glTF-Sample-Assets/Models/ABeautifulGame",Variants[2],"ABeautifulGame.glb"),Path.Combine(directory,name)); mount=new LocalFileSystem(directory); aggregate.Mount(mount); model=Model.Load(name); Require(model!=null && model.IsValid && !model.IsError,"public Model.Load returned null, invalid, or error model"); } finally { if(model!=null && model.ResourcePath!=null && model.ResourcePath.StartsWith("beautiful_")) model.Destroy(); if(mount!=null) { aggregate.UnMount(mount); mount.Dispose(); } if(Directory.Exists(directory)) Directory.Delete(directory,true); } });
  return results.ToArray();
  void Run(string name,string form,Action action) { try { action(); results.Add(new(name,form,true,null)); } catch(Exception e) { results.Add(new(name,form,false,e.ToString())); } }
 }
}
