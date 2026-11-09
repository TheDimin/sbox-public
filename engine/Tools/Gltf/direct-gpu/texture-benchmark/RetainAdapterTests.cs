using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox;
using Sandbox.Gltf;

namespace Sandbox.Gltf.DirectGpu.TextureBenchmark;

[TestClass]
internal sealed class RetainAdapterTests
{
 static string checkout;
 public static void Configure(string root){checkout=root;}
 [TestMethod]
 public void RoleRetainSuccessAndPartialFailureRelease()
 {
  var input=File.ReadAllBytes(Path.Combine(checkout,"Temp/gltf-reference/glTF-Sample-Assets/Models/ABeautifulGame/glTF-Binary/ABeautifulGame.glb"));
  Assert.AreEqual("bd7133b4b322aae97c589b8839dae8155ad2546acb35ae32a127e722a959d007",Convert.ToHexStringLower(SHA256.HashData(input)));
  using var doc=GltfReader.Read(input,null,new HashSet<string>{"KHR_draco_mesh_compression","KHR_texture_basisu"});
  var source=doc.ReadMaterial(0); var prepared=GltfStockMaterialAdapter.PrepareSource(doc,source);
  var plan=GltfGpuPrimitivePlan.Create(doc,0,0).ProjectMaterial(0,5,null,[0,1,4,5,6]);
  var templateSource=Material.FromShader("shaders/gltf_surface_five_uv.shader"); Assert.IsTrue(templateSource is {IsValid:true,IsError:false});
  var template=templateSource.CreateCopy("retain_adapter_"+Guid.NewGuid().ToString("N"));
  var directory=Path.Combine(Path.GetTempPath(),"sbox-gltf-textures",Environment.ProcessId.ToString());
  string[] Files()=>Directory.Exists(directory)?Directory.GetFiles(directory).Order().ToArray():Array.Empty<string>();
  var before=Files(); GltfMaterialLease<Texture>[] bases=null;
  try
  {
   template.SetFeature("F_ALPHA_TEST",0);template.SetFeature("F_TRANSLUCENT",0);template.SetFeature("F_RENDER_BACKFACES",0);
   var roles=prepared.Resources.Keys.ToArray(); bases=GltfNativeTextureCache.AcquireBatch(prepared.Resources.Values.Select(x=>x.Resource).ToArray());
   var byRole=roles.Select((role,i)=>(role,lease:bases[i])).ToDictionary(x=>x.role,x=>x.lease);
   int calls=0;
   Assert.ThrowsException<InvalidOperationException>(()=>GltfStockMaterialAdapter.AcquireSource(prepared,template,plan.MaterialUv,plan.UvProjection,true,role=>
   {
    if(++calls==2)throw new InvalidOperationException("injected partial retain failure");
    return GltfNativeTextureCache.Retain(byRole[role]);
   }));
   Assert.AreEqual(2,calls); Assert.IsTrue(bases.All(x=>x.Value.IsValid),"Failed adapter construction must preserve every base lease");
   using(var material=GltfStockMaterialAdapter.AcquireSource(prepared,template,plan.MaterialUv,plan.UvProjection,true,role=>GltfNativeTextureCache.Retain(byRole[role])))
   {
    foreach(var role in roles) Assert.AreSame(byRole[role].Value,material.Value.GetTexture("g_tGltf"+role+"Texture"),"Bound texture changed for "+role);
    Assert.AreSame(byRole[GltfMaterialTextureRole.MetallicRoughness].Value,byRole[GltfMaterialTextureRole.Occlusion].Value);
   }
  }
  finally { if(bases!=null)foreach(var lease in bases)lease.Dispose(); template?.Destroy(); }
  CollectionAssert.AreEqual(before,Files(),"Transferred and base leases must leave no retained native resource files");
 }
}
