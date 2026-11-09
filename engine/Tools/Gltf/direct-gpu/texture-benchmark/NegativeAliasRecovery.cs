using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox;
using Sandbox.Internal;
using Sandbox.Gltf;

namespace Sandbox.Gltf.DirectGpu.TextureBenchmark;

[TestClass]
internal sealed class NegativeAliasRecovery
{
 static string checkout;
 public static List<object> Measurements {get;}=new();
 public static void Configure(string root){checkout=root;Measurements.Clear();}
 [TestMethod]
 public void ExplicitRefreshForPreviouslyMissingAlias()
 {
  var source=Path.Combine(checkout,"Temp/gltf-reference/glTF-Sample-Assets/Models/ABeautifulGame/glTF-Binary/ABeautifulGame.glb");
  var input=File.ReadAllBytes(source);Assert.AreEqual("bd7133b4b322aae97c589b8839dae8155ad2546acb35ae32a127e722a959d007",Convert.ToHexStringLower(SHA256.HashData(input)));
  using var doc=GltfReader.Read(input,null,new HashSet<string>{"KHR_draco_mesh_compression","KHR_texture_basisu"});
  var bytes=GltfStockMaterialAdapter.PrepareSource(doc,doc.ReadMaterial(0)).Resources[GltfMaterialTextureRole.BaseColor].Resource;
  string id=Guid.NewGuid().ToString("N"),alias="gltf-negative-recovery/"+id+".vtex",dir=Path.Combine(Path.GetTempPath(),"gltf_negative_"+id),path=Path.Combine(dir,id+".vtex_c");
  RedirectFileSystem redirect=null; Texture recovered=null;
  try
  {
   var missing=Texture.Load(alias,false); Assert.IsTrue(missing is null || !missing.IsValid || missing.IsError);
   Directory.CreateDirectory(dir);File.WriteAllBytes(path,bytes);redirect=RedirectFileSystem.Create(dir);redirect.AddAbsFile("/"+alias+"_c",path);
   var stale=Texture.Load(alias,false);bool staleValid=stale is {IsValid:true,IsError:false};
   Measurements.Add(new{phase="after_register_without_refresh",valid=staleValid,is_null=stale is null});
   Assert.IsFalse(staleValid,"Negative lookup control must retain a stale miss before refresh");
   NativeEngine.g_pResourceSystem.InvalidateDatabase();recovered=Texture.Load(alias,false);
   Measurements.Add(new{phase="after_explicit_refresh",valid=recovered is {IsValid:true,IsError:false},is_null=recovered is null});
   Assert.IsTrue(recovered is {IsValid:true,IsError:false},"Explicit refresh did not recover previously missing alias");
  }
  finally { if(recovered is {IsValid:true,IsError:false})recovered.Dispose();redirect?.Dispose();if(Directory.Exists(dir))Directory.Delete(dir,true); }
 }
}
