using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Sandbox.Gltf.DirectGpu.TextureEncodingTestValidation;

using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;
using static TextureEncodingReference;

/// <summary>
/// Actual GPU texture sample readback versus literal numbers. The anonymous
/// shader is a transfer diagnostic; it does not prove source material shading,
/// scene geometry or the public Model.Load backend. Only the coordinator runs it.
/// </summary>
[TestClass]
public sealed class TextureEncodingGpuSamplingTests
{
	public static string CheckoutRoot { get; set; }
	public sealed record Measurement( string Form, int Image, bool IsSrgb, int Pixel, double[] Expected, float[] Actual, double RgbTolerance, double AlphaTolerance );
	public static List<object> Measurements { get; } = new();
	public static Dictionary<string, string> ShaderSources { get; } = new();
	public static void Configure( string checkoutRoot ) { CheckoutRoot = checkoutRoot; Measurements.Clear(); ShaderSources.Clear(); }

	[TestMethod]
	public async Task ActualGpuSamplesMatchIndependentTransferAlphaAndOriginControls()
	{
		Assert.IsTrue( Graphics.IsAvailable && Application.IsEditor && !Game.IsPlaying && ThreadSafe.IsMainThread && !Graphics.IsActive, "Requires the coordinator's authorized idle editor GPU session." );
		Measurements.Clear(); ShaderSources.Clear();
		var reference = Open( CheckoutRoot ); Shader srgbShader = null, linearShader = null; Material srgbMaterial = null, linearMaterial = null;
		try
		{
			using var cancellation = new CancellationTokenSource( TimeSpan.FromSeconds( 90 ) );
			var srgbSource = Source( true ); var linearSource = Source( false );
			ShaderSources.Add( "srgb", Convert.ToHexStringLower( SHA256.HashData( Encoding.UTF8.GetBytes( srgbSource ) ) ) ); ShaderSources.Add( "linear", Convert.ToHexStringLower( SHA256.HashData( Encoding.UTF8.GetBytes( linearSource ) ) ) );
			Measurements.Add( new { phase = "anonymous_texture_shaders_requested", shaders = new Dictionary<string, string>( ShaderSources ), scope = "Source hashes only; no completed GPU samples." } );
			srgbShader = await Shader.CompileAsync( srgbSource, cancellation.Token ); await MainThread.Wait();
			Assert.IsTrue( srgbShader is { IsValid: true, IsAnonymous: true }, "sRGB diagnostic must compile anonymously." );
			linearShader = await Shader.CompileAsync( linearSource, cancellation.Token ); await MainThread.Wait();
			Assert.IsTrue( linearShader is { IsValid: true, IsAnonymous: true }, "Linear diagnostic must compile anonymously." );
			Measurements.Add( new { phase = "anonymous_texture_shaders_compiled", shaders = new Dictionary<string, string>( ShaderSources ), srgb_anonymous = srgbShader.IsAnonymous, linear_anonymous = linearShader.IsAnonymous } );
			srgbMaterial = Material.Create( "texture_encoding_srgb_" + Guid.NewGuid().ToString( "N" ), srgbShader );
			linearMaterial = Material.Create( "texture_encoding_linear_" + Guid.NewGuid().ToString( "N" ), linearShader );
			Assert.IsTrue( srgbMaterial is { IsValid: true, IsError: false } && linearMaterial is { IsValid: true, IsError: false } );
			AssertClearTarget();
			foreach ( var form in reference.Forms() )
			{
				using var doc = GltfReader.Read( form.Bytes, form.Resolve );
				for ( var image = 0; image < 6; image++ )
				{
					var actualGreen = new float[2]; var role = 0;
					foreach ( var srgb in new[] { false, true } )
					{
						using var lease = global::Sandbox.GltfNativeTextureCache.Acquire( global::Sandbox.GltfTextureCompiler.Compile( doc.ReadImage( image ), srgb ) );
						var actual = Sample( srgb ? srgbMaterial : linearMaterial, lease.Value, .5f, .5f );
						Check( form.Name, image, srgb, 0, LiteralTexel( image ), actual ); actualGreen[role++] = actual[1];
					}
					Assert.IsTrue( actualGreen[0] - actualGreen[1] > .28f, "Actual color/data resource paths must be observably distinct; wrong-role transfer is a negative control." );
				}
			}
			foreach ( var colorType in new[] { 2, 6 } )
			{
				var png = StraightAlphaFixture( colorType, out var literal );
				foreach ( var srgb in new[] { false, true } )
				{
					using var lease = global::Sandbox.GltfNativeTextureCache.Acquire( global::Sandbox.GltfTextureCompiler.Compile( new GltfImage { Bytes = png, MimeType = "image/png" }, srgb ) );
					for ( var pixel = 0; pixel < 4; pixel++ ) Check( "literal_2x2_" + colorType, -1, srgb, pixel, literal.AsSpan( pixel * 4, 4 ).ToArray(), Sample( srgb ? srgbMaterial : linearMaterial, lease.Value, (pixel % 2 + .5f) / 2, (pixel / 2 + .5f) / 2 ) );
				}
			}
			Assert.AreEqual( 64, Measurements.OfType<Measurement>().Count(), "48 sample-model +16 asymmetric fixture samples must complete." );
			AssertClearTarget();
		}
		finally
		{
			await MainThread.Wait();
			linearMaterial?.Destroy(); srgbMaterial?.Destroy(); linearShader?.Destroy(); srgbShader?.Destroy();
		}
	}

	private static void Check( string form, int image, bool srgb, int pixel, byte[] literal, float[] actual )
	{
		var expected = new double[4];
		for ( var channel = 0; channel < 4; channel++ ) { var encoded = literal[channel] / 255.0; expected[channel] = srgb && channel < 3 ? encoded <= .04045 ? encoded / 12.92 : Math.Pow( (encoded + .055) / 1.055, 2.4 ) : encoded; }
		// GPU sRGB conversion may approximate the analytic equation. This bound is
		// below one normalized byte and over 280x smaller than the model's wrong
		// transfer separation. Alpha has no transfer and keeps a stricter bound.
		Measurements.Add( new Measurement( form, image, srgb, pixel, expected, actual, .001, .000001 ) );
		for ( var channel = 0; channel < 4; channel++ ) Assert.IsTrue( float.IsFinite( actual[channel] ) && Math.Abs( expected[channel] - actual[channel] ) <= (channel == 3 ? .000001 : .001), $"GPU sample mismatch {form}, image{image}, sRGB{srgb}, pixel{pixel}, channel{channel}: expected{expected[channel]}, actual{actual[channel]}." );
	}

	private static float[] Sample( Material material, Texture source, float u, float v )
	{
		using var target = Target();
		using ( var scope = Graphics.Scope.Create() )
		{
			Graphics.RenderTarget = RenderTarget.From( target ); scope.Context.Clear( Color.Magenta, true, false, false );
			Graphics.Attributes.Set( "EncodingTexture", source ); Graphics.Attributes.Set( "EncodingUv", new global::Vector2( u, v ) ); Graphics.Blit( material );
		}
		Graphics.FlushGPU(); var actual = new float[4]; target.GetPixels( (0, 0, 1, 1), 0, 0, actual.AsSpan(), ImageFormat.RGBA32323232F ); return actual;
	}

	private static void AssertClearTarget()
	{
		using var target = Target();
		using ( var scope = Graphics.Scope.Create() ) { Graphics.RenderTarget = RenderTarget.From( target ); scope.Context.Clear( Color.Magenta, true, false, false ); }
		Graphics.FlushGPU(); var actual = new float[4]; target.GetPixels( (0, 0, 1, 1), 0, 0, actual.AsSpan(), ImageFormat.RGBA32323232F );
		Assert.AreEqual( 1f, actual[0] ); Assert.AreEqual( 0f, actual[1] ); Assert.AreEqual( 1f, actual[2] ); Assert.AreEqual( 1f, actual[3] );
	}
	private static Texture Target() => Texture.CreateRenderTarget().WithSize( 1, 1 ).WithFormat( ImageFormat.RGBA32323232F ).WithInitialColor( Color.Magenta ).Create( "texture_encoding_numeric_" + Guid.NewGuid().ToString( "N" ) );

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
