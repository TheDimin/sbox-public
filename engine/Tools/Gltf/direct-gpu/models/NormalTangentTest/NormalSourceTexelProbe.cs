using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Sandbox.Engine;
namespace Sandbox.Gltf.DirectGpu.NormalTangentTestValidation;
[TestClass]
public sealed class NormalSourceTexelProbe
{
 public static string CheckoutRoot {get;set;}
 public static List<object> Measurements {get;}=new();
 [TestMethod]
 public async Task ActualSourceMaterialNormalTextureRetainsUpperLowerPixels()
 {
  await MainThread.Wait();Measurements.Clear();var root=Path.Combine(CheckoutRoot,"Temp/gltf-reference/glTF-Sample-Assets/Models/NormalTangentTest/glTF");
  var temp=Path.Combine(Path.GetTempPath(),"normal_source_texture_"+Guid.NewGuid().ToString("N"));var prefix="normal_texel_"+Guid.NewGuid().ToString("N");var folder=Path.Combine(temp,prefix);Directory.CreateDirectory(folder);var path=prefix+"/model.gltf";
  Assert.AreEqual("9cebea6c513ddfb1f5f5c761c08bca1468d2283a3cd9772bba3a57e484862ead",Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(root,"NormalTangentTest_Normal.png")))));
  File.Copy(Path.Combine(root,"NormalTangentTest.gltf"),Path.Combine(folder,"model.gltf"));
  foreach(var file in new[]{"NormalTangentTest0.bin","NormalTangentTest_BaseColor.png","NormalTangentTest_Normal.png","NormalTangentTest_OcclusionRoughnessMetallic.png"})File.Copy(Path.Combine(root,file),Path.Combine(folder,file));
  var aggregate=Sandbox.FileSystem.Mounted;var mount=new LocalFileSystem(temp);aggregate.Mount(mount);Model model=null;
  var output=Path.Combine(CheckoutRoot,"Temp/gltf-validation/direct-gpu/normal-source-texel",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);using var capture=new GltfGpuTriangleGateTests.NativeCapture(output);
  try
  {
   model=Model.Load(path);Assert.IsTrue(model is {IsValid:true,IsError:false} && model.GltfResource is not null && model.native.IsNull);var material=model.Materials.Single();var texture=material.GetTexture("g_tGltfNormalTexture");Assert.IsTrue(texture is {IsValid:true,IsError:false});
   global::Color32[] pixels=null;for(var i=0;i<12;i++){texture.MarkUsed(2048);await capture.WaitFrames();pixels=texture.GetPixels();if(pixels?.Length==2048*2048)break;}
   Assert.IsNotNull(pixels);Assert.AreEqual(2048*2048,pixels.Length,"Normal image failed to reach expected resident dimensions.");
   var top=pixels[168*2048+413];var bottom=pixels[241*2048+413];var oppositeTop=pixels[1879*2048+413];var oppositeBottom=pixels[1806*2048+413];
   Measurements.Add(new{texturePath=texture.ResourcePath,texture.Width,texture.Height,texture.Mips,texture.ImageFormat,top=new[]{top.r,top.g,top.b,top.a},bottom=new[]{bottom.r,bottom.g,bottom.b,bottom.a},oppositeTop=new[]{oppositeTop.r,oppositeTop.g,oppositeTop.b,oppositeTop.a},oppositeBottom=new[]{oppositeBottom.r,oppositeBottom.g,oppositeBottom.b,oppositeBottom.a},scope="Native bound material texture GetPixels after real frames; verifies retained resource texels, not direct shader Sample instruction."});
   Assert.IsTrue(top.r==126 && top.g==185 && top.b==241 && bottom.r==126 && bottom.g==63 && bottom.b==238,"Actual normal resource does not preserve independently decoded source texels at tested upper/lower positions.");
  }
  finally {await MainThread.Wait();if(model is not null && model.ResourcePath==Resource.FixPath(path))model.Destroy();aggregate.UnMount(mount);mount.Dispose();if(Directory.Exists(temp))Directory.Delete(temp,true);}
 }
}
