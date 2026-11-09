using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox;
using Sandbox.Gltf;
using Assert=Microsoft.VisualStudio.TestTools.UnitTesting.Assert;
namespace Sandbox.Gltf.DirectGpu.ABeautifulGameValidation;
[TestClass]
internal sealed class BasisSamplingTests
{
 static string checkout; public static List<object> Measurements {get;}=new();
 public static void Configure(string root){checkout=root;Measurements.Clear();}
 [TestMethod]
 public async Task ActualBasisTexelsApplyRoleTransfer()
 {
  await MainThread.Wait();var path=Path.Combine(checkout,"Temp/gltf-reference/glTF-Sample-Assets/Models/ABeautifulGame/glTF-Binary-KTX-ETC1S-Draco/ABeautifulGame.glb");using var document=GltfReader.Read(File.ReadAllBytes(path),null,new HashSet<string>{"KHR_draco_mesh_compression","KHR_texture_basisu"});
  Shader srgbShader=null,linearShader=null;Material srgbMaterial=null,linearMaterial=null;
  string output=Path.Combine(checkout,"Temp/gltf-validation/direct-gpu/ABeautifulGame/basis-sampling",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);using var capture=new GltfGpuTriangleGateTests.NativeCapture(output);
  try
  {
   using var cancel=new CancellationTokenSource(TimeSpan.FromSeconds(90));srgbShader=await Shader.CompileAsync(Source(true),cancel.Token);await MainThread.Wait();linearShader=await Shader.CompileAsync(Source(false),cancel.Token);await MainThread.Wait();Assert.IsTrue(srgbShader is {IsValid:true,IsAnonymous:true} && linearShader is {IsValid:true,IsAnonymous:true});
   srgbMaterial=Material.Create("basis_srgb_"+Guid.NewGuid().ToString("N"),srgbShader);linearMaterial=Material.Create("basis_linear_"+Guid.NewGuid().ToString("N"),linearShader);
   foreach(int index in new[]{0,1,2})
   {
    bool srgb=index==2;var plan=GltfBasisTexturePlan.Create(document.ReadImage(index),srgb,true);var decoded=plan.DecodeRgba32();using var lease=GltfNativeTextureCache.Acquire(plan.Compile());var texture=lease.Value;
    var timer=System.Diagnostics.Stopwatch.StartNew();int size;do{texture.MarkUsed(2048);await capture.WaitFrames();await MainThread.Wait();size=texture.GetPixels().Length;}while(size!=2048*2048 && timer.Elapsed<TimeSpan.FromSeconds(45));Assert.AreEqual(2048*2048,size,"Texture failed to stream full base mip");
    var errors=new List<double>();double strongestTransferSeparation=0;
    for(int y=256;y<2048;y+=512)for(int x=256;x<2048;x+=512)
    {
     int offset=4*(y*2048+x);var actual=Sample(srgb?srgbMaterial:linearMaterial,texture,(x+.5f)/2048,(y+.5f)/2048);for(int channel=0;channel<4;channel++){double encoded=decoded.Levels[0].Pixels[offset+channel]/255.0;double expected=srgb && channel<3?(encoded<=.04045?encoded/12.92:Math.Pow((encoded+.055)/1.055,2.4)):encoded;double err=Math.Abs(expected-actual[channel]);Assert.IsTrue(double.IsFinite(actual[channel]) && err<=(channel==3?.000001:.001),$"sample image {index} x{x} y{y} channel{channel} expected {expected:R} actual {actual[channel]:R}");errors.Add(err);if(srgb && channel<3)strongestTransferSeparation=Math.Max(strongestTransferSeparation,Math.Abs(encoded-expected));}
    }
    if(srgb)Assert.IsTrue(strongestTransferSeparation>.2,"Samples cannot detect missing sRGB conversion");Measurements.Add(new{image=index,srgb,samples=16,maximum_error=errors.Max(),strongest_transfer_negative=strongestTransferSeparation,residency_wait_seconds=timer.Elapsed.TotalSeconds,scope="Actual GPU point sampling and analytical role transfer against this source native decoded bytes; codec itself is not independently verified, and shader is diagnostic, not public source-lit material."});
   }
  }
  finally{await MainThread.Wait();linearMaterial?.Destroy();srgbMaterial?.Destroy();linearShader?.Destroy();srgbShader?.Destroy();}
 }
 static float[] Sample(Material material,Texture texture,float u,float v)
 {
  using var target=Texture.CreateRenderTarget().WithSize(1,1).WithFormat(ImageFormat.RGBA32323232F).WithInitialColor(Color.Magenta).Create("basis_sample_"+Guid.NewGuid().ToString("N"));
  using(var scope=Graphics.Scope.Create()){Graphics.RenderTarget=RenderTarget.From(target);scope.Context.Clear(Color.Magenta,true,false,false);Graphics.Attributes.Set("EncodingTexture",texture);Graphics.Attributes.Set("EncodingUv",new global::Vector2(u,v));Graphics.Blit(material);}Graphics.FlushGPU();var result=new float[4];target.GetPixels((0,0,1,1),0,0,result.AsSpan(),ImageFormat.RGBA32323232F);return result;
 }
	private static string Source( bool srgb ) => """
		HEADER
		{
			DevShader = true;
			Description = "glTF numeric texture encoding diagnostic";
		}
		MODES
		{
			Default();
			Forward();
		}
		COMMON
		{
			#include "postprocess/shared.hlsl"
			struct VertexInput
			{
				float3 vPositionOs : POSITION < Semantic( PosXyz ); >;
			};
			struct PixelInput
			{
				#if ( PROGRAM == VFX_PROGRAM_VS )
				float4 vPositionPs : SV_Position;
				#endif
				#if ( PROGRAM == VFX_PROGRAM_PS )
				float4 vPositionSs : SV_Position;
				#endif
			};
		}
		VS
		{
			PixelInput MainVs( VertexInput i )
			{
				PixelInput o;
				o.vPositionPs = float4( i.vPositionOs.xy, 0, 1 );
				return o;
			}
		}
		PS
		{
			RenderState( CullMode, NONE ); RenderState( DepthEnable, false ); RenderState( DepthWriteEnable, false ); RenderState( BlendEnable, false );
			Texture2D g_tEncoding < Attribute("EncodingTexture"); SrgbRead(__SRGB__); >;
			SamplerState g_sEncoding < Filter(POINT); AddressU(CLAMP); AddressV(CLAMP); >;
			float2 g_vEncodingUv < Attribute("EncodingUv"); >;
			float4 MainPs( PixelInput i ) : SV_Target0
			{
				return g_tEncoding.SampleLevel( g_sEncoding, g_vEncodingUv, 0 );
			}
		}
		""".Replace( "__SRGB__", srgb ? "true" : "false", StringComparison.Ordinal );
}
