using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NativeEngine;
using Sandbox.Rendering;

namespace Sandbox.Gltf.DirectGpu.TextureEncodingTestValidation;

using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

/// <summary>
/// Separates literal GPU arithmetic/readback, float render-target capture and
/// hardware sRGB sampling. It retains the original .001 transfer threshold.
/// This diagnostic neither changes a texture producer nor proves material shading.
/// </summary>
[TestClass]
public sealed class TextureEncodingPrecisionTests
{
	public static List<object> Measurements { get; } = new();
	public static Dictionary<string, string> ShaderSources { get; } = new();
	private static readonly float[] Literal = { .5840784178911641f, .768151147247507f, .35153259950043936f, .5f };
	public sealed record NumericRow( string Path, double[] Expected, float[] Actual, string Scope );

	[TestMethod]
	public async Task LiteralBufferAndFloatTargetSeparateCaptureFromSrgbConversion()
	{
		Assert.IsTrue( Graphics.IsAvailable && Application.IsEditor && !Game.IsPlaying && ThreadSafe.IsMainThread && !Graphics.IsActive, "Requires the coordinator's authorized idle editor GPU session." );
		Measurements.Clear(); ShaderSources.Clear();
		Shader compute = null, raster = null; Material computeMaterial = null, rasterMaterial = null;
		try
		{
			using var cancellation = new CancellationTokenSource( TimeSpan.FromSeconds( 90 ) );
			foreach ( var pair in new[] { (Name: "compute", Text: ComputeSource), (Name: "raster", Text: RasterSource) } )
				ShaderSources.Add( pair.Name, Convert.ToHexStringLower( SHA256.HashData( Encoding.UTF8.GetBytes( pair.Text ) ) ) );
			Measurements.Add( new { phase = "precision_shaders_requested", shaders = new Dictionary<string, string>( ShaderSources ) } );
			compute = await Shader.CompileAsync( ComputeSource, cancellation.Token ); await MainThread.Wait();
			Assert.IsTrue( compute is { IsValid: true, IsAnonymous: true }, "Precision compute shader must compile anonymously." );
			raster = await Shader.CompileAsync( RasterSource, cancellation.Token ); await MainThread.Wait();
			Assert.IsTrue( raster is { IsValid: true, IsAnonymous: true }, "Precision raster shader must compile anonymously." );
			computeMaterial = Material.Create( "gltf_precision_compute_" + Guid.NewGuid().ToString( "N" ), compute );
			rasterMaterial = Material.Create( "gltf_precision_raster_" + Guid.NewGuid().ToString( "N" ), raster );
			Assert.IsTrue( computeMaterial is { IsValid: true, IsError: false } && rasterMaterial is { IsValid: true, IsError: false } );
			var png = TextureEncodingReference.StraightAlphaFixture( 2, out var encoded );
			using var srgb = global::Sandbox.GltfNativeTextureCache.Acquire( global::Sandbox.GltfTextureCompiler.Compile( new GltfImage { Bytes = png, MimeType = "image/png" }, true ) );
			using var linear = global::Sandbox.GltfNativeTextureCache.Acquire( global::Sandbox.GltfTextureCompiler.Compile( new GltfImage { Bytes = png, MimeType = "image/png" }, false ) );
			Describe( "source_srgb", srgb.Value ); Describe( "source_linear", linear.Value );
			using var buffer = new GpuBuffer( 5, 16, GpuBuffer.UsageFlags.Structured, "gltf_transfer_precision" );
			buffer.SetData<float>( Enumerable.Repeat( -123f, 20 ).ToArray() );
			using ( var scope = Graphics.Scope.Create() )
			{
				SetInputs( srgb.Value, linear.Value ); Graphics.Attributes.Set( "PrecisionOutput", buffer );
				Graphics.ResourceBarrierTransition( buffer, ResourceState.GenericRead, ResourceState.UnorderedAccess );
				RenderTools.Compute( Graphics.Context, Graphics.Attributes.Get(), computeMaterial.native.GetMode(), 1, 1, 1 );
				Graphics.ResourceBarrierTransition( buffer, ResourceState.UnorderedAccess, ResourceState.GenericRead );
			}
			Graphics.FlushGPU(); var values = new float[20]; buffer.GetData<float>( values );
			var texel = encoded.AsSpan( 12, 4 ).ToArray(); var raw = texel.Select( x => x / 255.0 ).ToArray();
			var decoded = raw.Select( (x, c) => c == 3 ? x : Decode( x ) ).ToArray();
			var rows = new[]
			{
				Row( "compute_literal", Literal.Select( x => (double)x ).ToArray(), values.AsSpan( 0, 4 ).ToArray(), "float4 uniform -> RWStructuredBuffer<float4> -> synchronous float readback" ),
				Row( "compute_srgb_sample", decoded, values.AsSpan( 4, 4 ).ToArray(), "hardware sRGB SampleLevel -> float buffer; no color render target" ),
				Row( "compute_linear_sample", raw, values.AsSpan( 8, 4 ).ToArray(), "linear SampleLevel -> float buffer" ),
				Row( "compute_manual_eotf", decoded, values.AsSpan( 12, 4 ).ToArray(), "linear SampleLevel plus explicit float32 piecewise EOTF -> float buffer" ),
				Row( "compute_srgb_load", decoded, values.AsSpan( 16, 4 ).ToArray(), "hardware sRGB Load -> float buffer; no sampler or color target" ),
				Row( "float_target_clear", Literal.Select( x => (double)x ).ToArray(), Capture( null, srgb.Value, linear.Value, 0 ), "native clear -> requested RGBA32F target -> RGBA32F GetPixels" ),
				Row( "float_target_literal_shader", Literal.Select( x => (double)x ).ToArray(), Capture( rasterMaterial, srgb.Value, linear.Value, 0 ), "literal pixel output -> same requested RGBA32F capture" ),
				Row( "float_target_srgb_shader", decoded, Capture( rasterMaterial, srgb.Value, linear.Value, 1 ), "hardware sRGB SampleLevel -> same requested RGBA32F capture" )
			};
			// Every result is retained before any numeric assertion. A red transfer
			// gate cannot hide the literal/format controls which classify the fault.
			foreach ( var row in rows ) Assert.IsTrue( row.Actual.All( float.IsFinite ), row.Path + " must contain finite completed output." );
			foreach ( var row in rows.Where( x => x.Path.Contains( "literal", StringComparison.Ordinal ) || x.Path == "float_target_clear" || x.Path == "compute_linear_sample" || x.Path == "compute_manual_eotf" ) )
				for ( var c = 0; c < 4; c++ ) Assert.IsTrue( Math.Abs( row.Expected[c] - row.Actual[c] ) <= .000001, $"{row.Path} channel{c}: expected{row.Expected[c]}, actual{row.Actual[c]}." );
			foreach ( var row in rows.Where( x => x.Path == "compute_srgb_sample" || x.Path == "compute_srgb_load" || x.Path == "float_target_srgb_shader" ) )
				for ( var c = 0; c < 4; c++ ) Assert.IsTrue( Math.Abs( row.Expected[c] - row.Actual[c] ) <= (c == 3 ? .000001 : .001), $"Unchanged transfer gate {row.Path} channel{c}: expected{row.Expected[c]}, actual{row.Actual[c]}." );
		}
		finally
		{
			await MainThread.Wait(); rasterMaterial?.Destroy(); computeMaterial?.Destroy(); raster?.Destroy(); compute?.Destroy();
		}
	}

	private static NumericRow Row( string path, double[] expected, float[] actual, string scope )
	{
		var row = new NumericRow( path, expected, actual, scope ); Measurements.Add( row ); return row;
	}
	private static double Decode( double x ) => x <= .04045 ? x / 12.92 : Math.Pow( (x + .055) / 1.055, 2.4 );
	private static void SetInputs( Texture srgb, Texture linear )
	{
		Graphics.Attributes.Set( "PrecisionSrgb", srgb ); Graphics.Attributes.Set( "PrecisionLinear", linear );
		Graphics.Attributes.Set( "PrecisionLiteral", new global::Vector4( Literal[0], Literal[1], Literal[2], Literal[3] ) );
	}
	private static float[] Capture( Material material, Texture srgb, Texture linear, float mode )
	{
		using var target = Texture.CreateRenderTarget().WithSize( 1, 1 ).WithFormat( ImageFormat.RGBA32323232F ).WithInitialColor( Color.Magenta ).Create( "gltf_precision_capture_" + Guid.NewGuid().ToString( "N" ) );
		Describe( material is null ? "clear_target" : "shader_target_" + mode, target );
		using ( var scope = Graphics.Scope.Create() )
		{
			Graphics.RenderTarget = RenderTarget.From( target );
			Measurements.Add( new { phase = "bound_target", mode = material is null ? "clear" : mode.ToString(), ideal_color_format = Graphics.IdealColorFormat.ToString(), actual_target = Graphics.RenderTarget.ColorTarget.ImageFormat.ToString() } );
			scope.Context.Clear( material is null ? new Color( Literal[0], Literal[1], Literal[2], Literal[3] ) : Color.Magenta, true, false, false );
			if ( material is not null ) { SetInputs( srgb, linear ); Graphics.Attributes.Set( "PrecisionMode", mode ); Graphics.Blit( material ); }
		}
		Graphics.FlushGPU(); var result = new float[4]; target.GetPixels( (0, 0, 1, 1), 0, 0, result.AsSpan(), ImageFormat.RGBA32323232F ); return result;
	}
	private static void Describe( string label, Texture texture )
	{
		var disk = texture.Desc; var runtime = g_pRenderDevice.GetTextureDesc( texture.native );
		Measurements.Add( new { phase = "texture_descriptor", label, public_format = texture.ImageFormat.ToString(), disk_format = disk.m_nImageFormat.ToString(), disk_flags = disk.m_nFlags.ToString(), disk_decode = disk.m_nDecodeFlags.ToString(), runtime_format = runtime.m_nImageFormat.ToString(), runtime_flags = runtime.m_nFlags.ToString(), runtime_decode = runtime.m_nDecodeFlags.ToString(), width = runtime.m_nWidth, height = runtime.m_nHeight, mips = runtime.m_nNumMipLevels } );
	}

	private const string ComputeSource = """
		HEADER
		{
			Description = "glTF transfer precision float-buffer diagnostic";
		}
		MODES
		{
			Default();
		}
		COMMON
		{
			#include "system.fxc"
			#include "common.fxc"
		}
		CS
		{
			Texture2D g_tPrecisionSrgb < Attribute("PrecisionSrgb"); SrgbRead(true); >;
			Texture2D g_tPrecisionLinear < Attribute("PrecisionLinear"); SrgbRead(false); >;
			SamplerState g_sPrecision < Filter(POINT); AddressU(CLAMP); AddressV(CLAMP); >;
			float4 g_vPrecisionLiteral < Attribute("PrecisionLiteral"); >;
			RWStructuredBuffer<float4> g_PrecisionOutput < Attribute("PrecisionOutput"); >;
			float DecodePrecision(float x)
			{
				return x <= .04045 ? x / 12.92 : pow((x + .055) / 1.055, 2.4);
			}
			[numthreads(1,1,1)]
			void MainCs(uint3 id : SV_DispatchThreadID)
			{
				float4 raw = g_tPrecisionLinear.SampleLevel(g_sPrecision, float2(.75,.75), 0);
				g_PrecisionOutput[0] = g_vPrecisionLiteral;
				g_PrecisionOutput[1] = g_tPrecisionSrgb.SampleLevel(g_sPrecision, float2(.75,.75), 0);
				g_PrecisionOutput[2] = raw;
				g_PrecisionOutput[3] = float4(DecodePrecision(raw.r), DecodePrecision(raw.g), DecodePrecision(raw.b), raw.a);
				g_PrecisionOutput[4] = g_tPrecisionSrgb.Load(int3(1,1,0));
			}
		}
		""";
	private const string RasterSource = """
		HEADER
		{
			DevShader = true;
			Description = "glTF transfer precision float-target diagnostic";
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
			PixelInput MainVs(VertexInput i)
			{
				PixelInput o;
				o.vPositionPs = float4(i.vPositionOs.xy, 0, 1);
				return o;
			}
		}
		PS
		{
			RenderState(CullMode, NONE); RenderState(DepthEnable, false); RenderState(DepthWriteEnable, false); RenderState(BlendEnable, false);
			Texture2D g_tPrecisionSrgb < Attribute("PrecisionSrgb"); SrgbRead(true); >;
			SamplerState g_sPrecision < Filter(POINT); AddressU(CLAMP); AddressV(CLAMP); >;
			float4 g_vPrecisionLiteral < Attribute("PrecisionLiteral"); >;
			float g_flPrecisionMode < Attribute("PrecisionMode"); >;
			float4 MainPs(PixelInput i) : SV_Target0
			{
				if (g_flPrecisionMode < .5) return g_vPrecisionLiteral;
				return g_tPrecisionSrgb.SampleLevel(g_sPrecision, float2(.75,.75), 0);
			}
		}
		""";
}
