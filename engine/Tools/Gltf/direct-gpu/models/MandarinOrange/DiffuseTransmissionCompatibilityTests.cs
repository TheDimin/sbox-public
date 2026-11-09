using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Sandbox;
using Sandbox.Gltf;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Assert=Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace Sandbox.Gltf.DirectGpu.MandarinOrangeValidation;

[TestClass]
internal sealed class DiffuseTransmissionCompatibilityTests
{
 public static string CheckoutRoot {get;set;}
 public static List<object> Measurements {get;}=new();
 static string Hash(byte[] bytes)=>Convert.ToHexStringLower(SHA256.HashData(bytes));
 [TestMethod]
 public async Task ZeroFactorVersusOrdinaryCoreAcrossRoughness()
 {
  await MainThread.Wait();
  string own=Path.Combine(CheckoutRoot,"research/gltf-validation/direct-gpu/models/MandarinOrange");
  string output=Path.Combine(CheckoutRoot,"Temp/gltf-validation/direct-gpu/mandarin-diffuse-compatibility",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);
  using var capture=new GltfGpuTriangleGateTests.NativeCapture(output,new global::Vector3(200,0,0),global::Vector3.Zero,100);
  capture.Camera.EnablePostProcessing=false;capture.Scene.SceneWorld.AmbientLightColor=Color.Black;
  var light=(ScenePointLight)typeof(GltfGpuTriangleGateTests.NativeCapture).GetField("light",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(capture);
  light.Position=new global::Vector3(100,173.20508075688772f,0);
  var templates=new List<Material>();
  try
  {
   foreach(float roughness in new[]{0f,.5f,1f})
   {
    var core=await Draw(false,roughness);var zero=await Draw(true,roughness);
    int changed=0,samples=0;double maximum=0,sum=0;
    for(int y=64;y<128;y++)for(int x=64;x<128;x++)for(int c=0;c<3;c++)
    {
     int at=(y*192+x)*4+c;double difference=Math.Abs((double)zero[at]-core[at]);maximum=Math.Max(maximum,difference);sum+=difference;samples++;if(difference>0)changed++;
    }
    Measurements.Add(new{phase="zero_factor_core_compatibility",roughness,light_incidence_degrees=60,core_center_rgb=Center(core),transmission_zero_center_rgb=Center(zero),maximum_absolute_linear_difference=maximum,mean_absolute_linear_difference=sum/samples,changed_values=changed,checked_values=samples,scope="Measured native core BRDF versus extension Lambert-reflection difference, including their shared native specular service. Diagnostic, not a pixel-identity or glTF core conformance assertion. No percentage tolerance used."});
   }
   async Task<float[]> Draw(bool transmission,float roughness)
   {
    string suffix=roughness==.5f?"0_5":roughness==0?"0":"1";
    string label=(transmission?"transmission_zero":"ordinary_core")+"_"+suffix;
    byte[] bytes=File.ReadAllBytes(Path.Combine(own,"diffuse-compatibility-fixtures",label+".gltf"));
    using var document=GltfReader.Read(bytes,supportedExtensions:GltfModelLoader.SupportedExtensions);
    var material=document.ReadMaterial(0);Assert.AreEqual(roughness,material.RoughnessFactor);
    if(transmission){Assert.IsNotNull(material.DiffuseTransmission);Assert.AreEqual(0f,material.DiffuseTransmission.Factor);}else Assert.IsNull(material.DiffuseTransmission);
    string shaderPath=transmission?"shaders/gltf_diffuse_transmission.shader":"shaders/gltf_surface_five_uv.shader";
    var original=Material.FromShader(shaderPath);Assert.IsTrue(original is {IsValid:true,IsError:false});
    var template=original.CreateCopy("diffuse_compatibility_"+Guid.NewGuid().ToString("N"));templates.Add(template);template.SetFeature("F_ALPHA_TEST",0);template.SetFeature("F_RENDER_BACKFACES",0);
    using var asset=GltfGpuAsset.Prepare(document,GltfManagedModelResource.ComputeShaderPath,_=>null);
    asset.ConfigureProjectedVariants(1024*1024,(_,plan,images)=>GltfStockMaterialAdapter.AcquireSource(GltfStockMaterialAdapter.PrepareSource(document,material,images.ReadImage),template,plan.MaterialUv,plan.UvProjection,true));
    using var instance=asset.CreateInstance(capture.Scene.SceneWorld);await capture.WaitFrames();
    using(capture.Scene.Push()){capture.Scene.PreCameraRender();capture.Camera.InitializeRendering(false);capture.Camera.SceneCamera.AntiAliasing=false;}
    using var target=Texture.CreateRenderTarget().WithSize(192,192).WithFormat(ImageFormat.RGBA32323232F).Create(label+Guid.NewGuid().ToString("N"));
    target.Clear(Color.Magenta);await capture.WaitFrames();var clear=Enumerable.Repeat(float.NaN,192*192*4).ToArray();target.GetPixels<float>((0,0,192,192),0,0,clear,ImageFormat.RGBA32323232F);
    Assert.IsTrue(clear.Select((v,i)=>float.IsFinite(v) && v==(i%4==1?0f:1f)).All(v=>v),"Exact endpoint float readback control failed");
    using(capture.Scene.Push())Assert.IsTrue(capture.Camera.RenderToTexture(target));await capture.WaitFrames();
    var pixels=Enumerable.Repeat(float.NaN,clear.Length).ToArray();target.GetPixels<float>((0,0,192,192),0,0,pixels,ImageFormat.RGBA32323232F);Assert.IsTrue(pixels.All(float.IsFinite));
    Assert.IsTrue(Center(pixels).All(v=>v>0),"Native comparison shader produced no positive interior signal");
    Assert.AreEqual(0,instance.CallbackErrors.Length);Assert.IsTrue(instance.DrawCalls>0);Assert.AreEqual(6,instance.Readback().Single().Indices.Length);
    var raw=System.Runtime.InteropServices.MemoryMarshal.AsBytes(pixels.AsSpan()).ToArray();File.WriteAllBytes(Path.Combine(output,label+".rgba32f"),raw);var preview=await capture.Take(label);
    Measurements.Add(new{label,roughness,shader=shaderPath,input_sha256=Hash(bytes),hdr_sha256=Hash(raw),preview.Path,preview.Sha256,output});return pixels;
   }
  }
  finally{await MainThread.Wait();foreach(var template in templates)template.Destroy();}
 }
 static double[] Center(float[] pixels)=>Enumerable.Range(0,3).Select(c=>new[]{95,96}.Sum(y=>new[]{95,96}.Sum(x=>(double)pixels[(y*192+x)*4+c]))/4).ToArray();
}
