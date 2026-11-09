using System;
using System.Collections.Generic;
using System.IO;
using Sandbox;
using Sandbox.Gltf;

namespace Sandbox.Gltf.DirectGpu.ABeautifulGameValidation;

public static class BasisTests
{
 public sealed record Result(string Name, string Form, bool Passed, string Error);
 public static Result[] RunAll(string checkout)
 {
  var rows=new List<Result>(); var path=Path.Combine(checkout,"Temp/gltf-reference/glTF-Sample-Assets/Models/ABeautifulGame/glTF-Binary-KTX-ETC1S-Draco/ABeautifulGame.glb");
  using var doc=GltfReader.Read(File.ReadAllBytes(path),null,new HashSet<string>{"KHR_draco_mesh_compression","KHR_texture_basisu"});
  var color=new HashSet<int>{2,5,8,11,14,17,18,21,22,25,26,29,32};
  for(int i=0;i<33;i++) { int index=i; Run("authored_etc1s_mips_"+i,()=> { var image=doc.ReadImage(index); var plan=GltfBasisTexturePlan.Create(image,color.Contains(index),true); Require(plan.Width==2048 && plan.Height==2048 && plan.Levels.Count==12 && plan.OutputLevelCount==12,"dimensions or authored mip chain"); Require(plan.SourceCodec==GltfBasisSourceCodec.Etc1S && plan.TargetCodec==GltfBasisTargetCodec.Rgba32 && plan.SupercompressionScheme==1 && plan.IsSrgb==color.Contains(index),"codec or transfer"); Require(plan.Orientation=="rd" && plan.Swizzle=="rgba","orientation or swizzle"); }); }
  foreach(int i in new[]{0,1,2}) { int index=i; Run("native_rgba32_resource_"+i,()=> { var plan=GltfBasisTexturePlan.Create(doc.ReadImage(index),color.Contains(index),true); var bytes=plan.Compile(); Require(bytes!=null && bytes.Length>2048*2048*4,"native resource too short"); }); }
  Run("wrong_transfer_role_rejected",()=> { try { _=GltfBasisTexturePlan.Create(doc.ReadImage(2),false,true); } catch(GltfException) { return; } throw new InvalidOperationException("sRGB base-color image accepted as linear data"); });
  return rows.ToArray();
  void Run(string name,Action body) { try { body(); rows.Add(new(name,"glTF-Binary-KTX-ETC1S-Draco",true,null)); } catch(Exception e) { rows.Add(new(name,"glTF-Binary-KTX-ETC1S-Draco",false,e.ToString())); } }
 }
 static void Require(bool value,string message) { if(!value) throw new InvalidOperationException(message); }
}
