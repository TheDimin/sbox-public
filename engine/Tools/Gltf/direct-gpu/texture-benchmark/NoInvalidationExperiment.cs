using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox;
using Sandbox.Internal;
using Sandbox.Gltf;

namespace Sandbox.Gltf.DirectGpu.TextureBenchmark;

[TestClass]
internal sealed class NoInvalidationExperiment
{
 static string checkout;
 public static List<object> Measurements {get;}=new();
 public static void Configure(string root){checkout=root;Measurements.Clear();}
 [TestMethod]
 public async Task FreshAliasesAndPreviouslyMissingAlias()
 {
  await MainThread.Wait();
  var source=Path.Combine(checkout,"Temp/gltf-reference/glTF-Sample-Assets/Models/ABeautifulGame/glTF-Binary/ABeautifulGame.glb");
  var input=File.ReadAllBytes(source); Assert.AreEqual("bd7133b4b322aae97c589b8839dae8155ad2546acb35ae32a127e722a959d007",Convert.ToHexStringLower(SHA256.HashData(input)));
  using var doc=GltfReader.Read(input,null,new HashSet<string>{"KHR_draco_mesh_compression","KHR_texture_basisu"});
  var prepared=GltfStockMaterialAdapter.PrepareSource(doc,doc.ReadMaterial(0));
  var selected=prepared.Resources.Where(x=>x.Key!=GltfMaterialTextureRole.Occlusion).OrderBy(x=>x.Key).ToArray();
  Assert.AreEqual(3,selected.Length);
  string dir=Path.Combine(Path.GetTempPath(),"gltf_no_invalidate_"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
  var redirects=new List<RedirectFileSystem>(); var textures=new List<Texture>();
  try
  {
   var missingAlias="gltf-noinvalidate/"+Guid.NewGuid().ToString("N")+".vtex";
   var missing=Texture.Load(missingAlias,false); bool missingWasError=missing is null || !missing.IsValid || missing.IsError;
   Measurements.Add(new{phase="before_registration",missing_is_null=missing is null,missing_is_error=missing?.IsError,missing_is_valid=missing?.IsValid});
   Assert.IsTrue(missingWasError,"Negative alias unexpectedly resolved before registration");
   var timer=Stopwatch.StartNew();
   foreach(var pair in selected)
   {
    string id=Guid.NewGuid().ToString("N"),alias="gltf-noinvalidate/"+id+".vtex",path=Path.Combine(dir,id+".vtex_c");
    File.WriteAllBytes(path,pair.Value.Resource); var redirect=RedirectFileSystem.Create(dir);redirects.Add(redirect);redirect.AddAbsFile("/"+alias+"_c",path);
    var texture=Texture.Load(alias,false); bool valid=texture is {IsValid:true,IsError:false};
    Measurements.Add(new{phase="fresh_alias_no_invalidation",role=pair.Key.ToString(),valid,elapsed_ms=timer.Elapsed.TotalMilliseconds});
    if(valid)textures.Add(texture); Assert.IsTrue(valid,"Fresh registered alias failed without database invalidation");
   }
   for(int i=0;i<selected.Length;i++) await Verify(textures[i],selected[i].Value.Resource,selected[i].Key.ToString());
   var old=textures[0]; foreach(var texture in textures)texture.Dispose();textures.Clear();foreach(var redirect in redirects)redirect.Dispose();redirects.Clear();
   // A new generation deliberately uses different content and a different alias.
   string nextId=Guid.NewGuid().ToString("N"),nextAlias="gltf-noinvalidate/"+nextId+".vtex",nextPath=Path.Combine(dir,nextId+".vtex_c");
   File.WriteAllBytes(nextPath,selected[1].Value.Resource);var nextRedirect=RedirectFileSystem.Create(dir);redirects.Add(nextRedirect);nextRedirect.AddAbsFile("/"+nextAlias+"_c",nextPath);
   var next=Texture.Load(nextAlias,false); bool nextValid=next is {IsValid:true,IsError:false};
   Measurements.Add(new{phase="fresh_different_content_generation",valid=nextValid,same_wrapper=ReferenceEquals(old,next)});
   Assert.IsTrue(nextValid); textures.Add(next); Assert.AreNotSame(old,next); await Verify(next,selected[1].Value.Resource,"different-generation");
   // Try the exact previously missing alias only after the fresh-alias test.
   string negativePath=Path.Combine(dir,"negative.vtex_c"); File.WriteAllBytes(negativePath,selected[0].Value.Resource);
   var negativeRedirect=RedirectFileSystem.Create(dir); redirects.Add(negativeRedirect);negativeRedirect.AddAbsFile("/"+missingAlias+"_c",negativePath);
   var after=Texture.Load(missingAlias,false); bool afterValid=after is {IsValid:true,IsError:false};
   Measurements.Add(new{phase="previously_missing_alias_after_registration_without_invalidation",valid=afterValid,is_null=after is null,is_error=after?.IsError});
   if(afterValid){textures.Add(after);await Verify(after,selected[0].Value.Resource,"previously-missing");}
  }
  finally { await MainThread.Wait(); foreach(var texture in textures)texture?.Dispose(); foreach(var redirect in redirects)redirect.Dispose(); Directory.Delete(dir,true); }
 }
 static async Task Verify(Texture texture,byte[] resource,string role)
 {
  Assert.AreEqual(12,texture.Mips); var timer=Stopwatch.StartNew(); Color32[] basePixels; int attempts=0;
  do { texture.MarkUsed(2048);await Task.Delay(100);await MainThread.Wait();basePixels=texture.GetPixels(0);attempts++; }
  while(basePixels.Length!=2048*2048 && timer.Elapsed<TimeSpan.FromSeconds(45));
  Measurements.Add(new{phase="residency",role,attempts,pixels=basePixels.Length,seconds=timer.Elapsed.TotalSeconds});
  Assert.AreEqual(2048*2048,basePixels.Length); int offset=resource.Length;
  for(int mip=0;mip<12;mip++)
  {
   int size=Math.Max(1,2048>>mip);offset-=size*size*4;var pixels=texture.GetPixels(mip);Assert.AreEqual(size*size,pixels.Length);long changed=0;
   for(int i=0;i<pixels.Length;i++){var p=pixels[i];int at=offset+i*4;if(p.r!=resource[at]||p.g!=resource[at+1]||p.b!=resource[at+2]||p.a!=resource[at+3])changed++;}
   Measurements.Add(new{phase="mip_readback",role,mip,pixels=pixels.Length,different_pixels=changed});Assert.AreEqual(0L,changed);
  }
 }
}
