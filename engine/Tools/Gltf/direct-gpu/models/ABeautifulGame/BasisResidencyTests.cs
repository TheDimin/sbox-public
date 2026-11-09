using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox;
using Sandbox.Gltf;
using Assert=Microsoft.VisualStudio.TestTools.UnitTesting.Assert;
namespace Sandbox.Gltf.DirectGpu.ABeautifulGameValidation;
[TestClass]
internal sealed class BasisResidencyTests
{
 static string checkout;
 public static List<object> Measurements {get;}=new();
 public static void Configure(string root){checkout=root;Measurements.Clear();}
 [TestMethod]
 [DataRow(1,false)]
 public async Task RealGpuTextureMipBytes(int imageIndex,bool srgb)
 {
  await MainThread.Wait(); string source=Path.Combine(checkout,"Temp/gltf-reference/glTF-Sample-Assets/Models/ABeautifulGame/glTF-Binary-KTX-ETC1S-Draco/ABeautifulGame.glb");
  using var document=GltfReader.Read(File.ReadAllBytes(source),null,new HashSet<string>{"KHR_draco_mesh_compression","KHR_texture_basisu"});
  var plan=GltfBasisTexturePlan.Create(document.ReadImage(imageIndex),srgb,true);var decoded=plan.DecodeRgba32();var compiled=plan.Compile();
  string output=Path.Combine(checkout,"Temp/gltf-validation/direct-gpu/ABeautifulGame/basis-gpu",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);
  using var capture=new GltfGpuTriangleGateTests.NativeCapture(output);using var lease=GltfNativeTextureCache.Acquire(compiled);var texture=lease.Value;
  Assert.IsTrue(texture is {IsValid:true,IsError:false}); texture.MarkUsed(2048);await capture.WaitFrames();await MainThread.Wait();
  Assert.AreEqual(12,texture.Mips);var timer=Stopwatch.StartNew();int attempts=0;int initial=0;int resident=0;
  do { texture.MarkUsed(2048);await capture.WaitFrames();await MainThread.Wait();resident=texture.GetPixels(0).Length;if(attempts++==0)initial=resident; } while(resident!=2048*2048 && timer.Elapsed<TimeSpan.FromSeconds(45));
  Measurements.Add(new{image=imageIndex,initial_pixels=initial,resident_pixels=resident,attempts,seconds=timer.Elapsed.TotalSeconds,scope="Bounded full mip residency wait; initial short residency retained separately."});
  Assert.AreEqual(2048*2048,resident,"Texture did not settle to full residency within 45 seconds");var mipRows=new List<object>();
  for(int mip=0;mip<12;mip++)
  {
   texture.MarkUsed(2048);await capture.WaitFrames();await MainThread.Wait();var actual=texture.GetPixels(mip);var expected=decoded.Levels[mip].Pixels;
   Assert.AreEqual(expected.Length/4,actual.Length,"streaming mip residency or size mismatch");long errors=0;int max=0;
   for(int i=0;i<actual.Length;i++) { var p=actual[i];for(int c=0;c<4;c++){int v=c==0?p.r:c==1?p.g:c==2?p.b:p.a;int e=Math.Abs(v-expected[4*i+c]);if(e!=0)errors++;max=Math.Max(max,e);} }
   mipRows.Add(new{mip,pixels=actual.Length,different_channels=errors,maximum_byte_error=max});Assert.AreEqual(0L,errors,"GPU texture does not preserve decoded authored mip bytes");
  }
  using var reference=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(checkout,"research/gltf-validation/direct-gpu/models/ABeautifulGame/jpeg-reference.json")));
  var data=texture.GetPixels(0);var points=reference.RootElement.GetProperty(imageIndex.ToString()).GetProperty("points");double sum=0;int maximum=0,n=0;
  foreach(var point in points.EnumerateArray()){int x=point.GetProperty("x").GetInt32(),y=point.GetProperty("y").GetInt32();var p=data[y*2048+x];var rgb=point.GetProperty("rgb");for(int c=0;c<3;c++){int actual=c==0?p.r:c==1?p.g:p.b;int delta=Math.Abs(actual-rgb[c].GetInt32());sum+=delta;maximum=Math.Max(maximum,delta);n++;}}
  Measurements.Add(new{image=imageIndex,srgb,mips=mipRows,jpeg_landmarks_mean_absolute_byte_error=sum/n,jpeg_landmarks_maximum_byte_error=maximum,jpeg_reference="independently decoded upstream JPEG; ETC1S and JPEG are lossy distinct encodings, diagnostic only",scope="Actual GPU readback against native codec output for all authored mips; does not independently prove codec, final sampling or sRGB lighting"});
 }
}
