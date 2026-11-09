using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NativeEngine;
using Sandbox.Rendering;

namespace Sandbox.Gltf.DirectGpu.TextureEncodingTestValidation;

[TestClass]
public sealed class Png16GpuProducerTests
{
	[TestMethod]
	public async Task LinearProducerNativeSampleRetainsAdjacentSixteenBitValues()
	{
		Assert.IsTrue( Graphics.IsAvailable && Application.IsEditor && !Game.IsPlaying && ThreadSafe.IsMainThread && !Graphics.IsActive );
		var png = Convert.FromBase64String( "iVBORw0KGgoAAAANSUhEUgAAAAIAAAABEAYAAACksqPJAAAAGUlEQVR4nGNgZGRkamA0sGRkYmRsYLpiCAAORAJ8hmWiZwAAAABJRU5ErkJggg==" );
		Shader shader = null; Material material = null;
		try
		{
			using var cancel = new CancellationTokenSource( TimeSpan.FromSeconds( 90 ) );
			shader = await Shader.CompileAsync( Source, cancel.Token ); await MainThread.Wait();
			Assert.IsTrue( shader is { IsValid: true, IsAnonymous: true } );
			material = Material.Create( "gltf_png16_probe_" + Guid.NewGuid().ToString( "N" ), shader );
			Assert.IsTrue( material is { IsValid: true, IsError: false } );
			using var texture = global::Sandbox.GltfNativeTextureCache.Acquire( global::Sandbox.GltfTextureCompiler.Compile( new GltfImage { MimeType = "image/png", Bytes = png }, false ) );
			using var buffer = new GpuBuffer( 3, 16, GpuBuffer.UsageFlags.Structured, "gltf_png16_probe" );
			buffer.SetData<float>( new float[12] );
			using ( var scope = Graphics.Scope.Create() )
			{
				Graphics.Attributes.Set( "Source16", texture.Value ); Graphics.Attributes.Set( "Output16", buffer );
				Graphics.ResourceBarrierTransition( buffer, ResourceState.GenericRead, ResourceState.UnorderedAccess );
				RenderTools.Compute( Graphics.Context, Graphics.Attributes.Get(), material.native.GetMode(), 1, 1, 1 );
				Graphics.ResourceBarrierTransition( buffer, ResourceState.UnorderedAccess, ResourceState.GenericRead );
			}
			Graphics.FlushGPU(); var actual = new float[12]; buffer.GetData<float>( actual );
			var first = new[] { 257,258,32769,12345 }; var second = new[] { 258,257,32770,54321 };
			for ( var c = 0; c < 4; c++ )
			{
				Assert.IsTrue( Math.Abs( actual[c] - first[c] / 65535f ) < .000001, "first texel channel " + c );
				Assert.IsTrue( Math.Abs( actual[4+c] - second[c] / 65535f ) < .000001, "second texel channel " + c );
				Assert.IsTrue( Math.Abs( actual[8+c] - (first[c] + second[c]) / 131070f ) < .000001, "linear filtered channel " + c );
			}
		}
		finally { await MainThread.Wait(); material?.Destroy(); shader?.Destroy(); }
	}
	private const string Source = """
		HEADER { Description = "glTF PNG16 linear candidate"; }
		MODES { Default(); }
		COMMON
		{
			#include "system.fxc"
			#include "common.fxc"
		}
		CS
		{
			Texture2D g_tSource16 < Attribute("Source16"); SrgbRead(false); >;
			SamplerState g_sSource16 < Filter(LINEAR); AddressU(CLAMP); AddressV(CLAMP); >;
			RWStructuredBuffer<float4> g_Output16 < Attribute("Output16"); >;
			[numthreads(1,1,1)] void MainCs(uint3 id : SV_DispatchThreadID)
			{
				g_Output16[0] = g_tSource16.Load(int3(0,0,0));
				g_Output16[1] = g_tSource16.Load(int3(1,0,0));
				g_Output16[2] = g_tSource16.SampleLevel(g_sSource16, float2(.5,.5), 0);
			}
		}
		""";
}
