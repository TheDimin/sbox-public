using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NativeEngine;
using Sandbox.Rendering;

namespace Sandbox.Gltf.DirectGpu.TextureSettingsValidation;

using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;
using Png = Sandbox.Gltf.DirectGpu.BoxTexturedValidation.PngTexelReference;
using ResourceReference = Sandbox.Gltf.DirectGpu.BoxTexturedValidation.BoxTexturedReference;
using V4 = System.Numerics.Vector4;

/// <summary>Exact differential for retained failure454761; never relaxes the original sampling expectation.</summary>
[TestClass]
public sealed class TextureSettingsMipDiagnosticTests
{
	public static string CheckoutRoot { get; set; }
	public static string ExpectedShaderSha256 { get; set; }
	public static List<object> Measurements { get; } = new();
	private static readonly Vector2 Uv = new( .34228515625f, .7646484375f );

	[TestMethod]
	public async Task FirstFailedSourceTexelSeparatesNativeMipResourceResidencyAndSampler()
	{
		Idle(); Measurements.Clear();
		var sourceBytes = File.ReadAllBytes( Path.Combine( CheckoutRoot, "engine/Tools/Gltf/direct-gpu/models/TextureSettingsTest/texture_settings_mip_diagnostic.shader" ) );
		Assert.AreEqual( ExpectedShaderSha256, Convert.ToHexStringLower( SHA256.HashData( sourceBytes ) ) );
		using var reference = new TextureSettingsReference( CheckoutRoot ); using var document = reference.Open( "glTF", false );
		var levels = new List<Png.Image> { reference.Images[1] };
		var reduce = typeof( TextureSettingsSamplerGpuTests ).GetMethod( "Reduce", BindingFlags.Static | BindingFlags.NonPublic )!;
		while ( levels[^1].Width > 1 ) levels.Add( (Png.Image)reduce.Invoke( null, new object[] { levels[^1] } ) );
		var expectedChain = levels.AsEnumerable().Reverse().SelectMany( x => x.Rgba ).ToArray();
		var fullResource = global::Sandbox.GltfTextureCompiler.CompileWithMipmaps( document.ReadImage( 1 ), true );
		var parsed = ResourceReference.ParseNativeTextureResource( fullResource );
		Assert.AreEqual( 512, parsed.Width ); Assert.AreEqual( 512, parsed.Height ); Assert.AreEqual( 10, parsed.MipCount ); Assert.AreEqual( 4, parsed.Format );
		CollectionAssert.AreEqual( expectedChain, parsed.Payload, "Full native resource must match independently decoded PNG + linear-light chain in smallest-first order." );
		Measurements.Add( new { Phase = "cpu_resource", ResourceSha256 = Hash( fullResource ), PayloadSha256 = Hash( parsed.Payload ), parsed.Width, parsed.Height, parsed.MipCount, parsed.Format, parsed.Flags, Levels = levels.Select( (image, mip) => new { Mip = mip, image.Width, image.Height, EncodedPixel = Encoded( image ), LinearPixel = Expected( image ).ToString() } ).ToArray() } );
		Shader shader = null; Material material = null;
		global::Sandbox.GltfMaterialLease<Texture> full = null, single = null; Texture uploaded = null;
		try
		{
			using var cancel = new CancellationTokenSource( TimeSpan.FromSeconds( 90 ) );
			shader = await Shader.CompileAsync( System.Text.Encoding.UTF8.GetString( sourceBytes ), cancel.Token ); await MainThread.Wait(); Idle();
			Assert.IsTrue( shader is { IsValid: true, IsAnonymous: true } );
			material = Material.Create( "texture_settings_mip_diagnostic_" + Guid.NewGuid().ToString( "N" ), shader ); Assert.IsTrue( material is { IsValid: true, IsError: false } );
			full = global::Sandbox.GltfNativeTextureCache.Acquire( fullResource );
			single = global::Sandbox.GltfNativeTextureCache.Acquire( global::Sandbox.GltfTextureCompiler.Compile( document.ReadImage( 1 ), true ) );
			// Same independent byte chain through explicit non-streamed GPU upload.
			uploaded = Texture.Create( 512, 512, ImageFormat.RGBA8888 ).WithMips( 10 ).WithData( expectedChain ).WithStaticUsage().Finish();
			var textures = new[] { (Name: "native_full_chain", Texture: full.Value), (Name: "native_base_only", Texture: single.Value), (Name: "explicit_full_chain_upload", Texture: uploaded) };
			var firstFrame = Application.FrameCount; var timer = Stopwatch.StartNew();
			V4 finalDimensions = default, finalLoad = default, finalSample = default;
			foreach ( var frames in new[] { 0UL, 1UL, 4UL, 16UL, 64UL } )
			{
				while ( Application.FrameCount - firstFrame < frames )
				{
					Assert.IsTrue( timer.Elapsed < TimeSpan.FromSeconds( 10 ), "Real application frames did not advance within the bounded residency observation." );
					foreach ( var texture in textures ) texture.Texture.MarkUsed( 512 );
					await Task.Delay( 20 ); await MainThread.Wait(); Idle();
				}
				foreach ( var entry in textures )
				{
					var results = new List<object>();
					if ( entry.Name == "native_full_chain" ) finalDimensions = finalLoad = finalSample = new V4( float.NaN );
					for ( var mode = 0; mode <= 4; mode++ )
					{
						if ( mode == 2 && entry.Texture.Mips < 2 ) continue;
						try
						{
							var actual = Sample( material, entry.Texture, mode ); results.Add( new { Mode = mode, Actual = actual.ToString() } );
							if ( entry.Name == "native_full_chain" ) { if ( mode == 0 ) finalDimensions = actual; if ( mode == 1 ) finalLoad = actual; if ( mode == 4 ) finalSample = actual; }
						}
						catch ( Exception error ) { results.Add( new { Mode = mode, Error = error.ToString() } ); }
					}
					var nativePixels = new List<object>();
					// Defer CPU-side texture readback until the final phase so it cannot
					// itself force residency during the MarkUsed/real-frame differential.
					for ( var mip = 0; frames == 64 && mip < Math.Min( 2, entry.Texture.Mips ); mip++ )
					{
						var w = 512 >> mip; var data = new byte[4];
						try { entry.Texture.GetPixels( ((int)(Uv.x * w), (int)(Uv.y * w), 1, 1), 0, mip, data.AsSpan(), ImageFormat.RGBA8888 ); nativePixels.Add( new { Mip = mip, Encoded = data } ); }
						catch ( Exception error ) { nativePixels.Add( new { Mip = mip, Error = error.ToString() } ); }
					}
					Measurements.Add( new { Phase = "gpu_observation", entry.Name, RequestedFrames = frames, ActualFrames = Application.FrameCount - firstFrame, Milliseconds = timer.Elapsed.TotalMilliseconds, entry.Texture.Width, entry.Texture.Height, entry.Texture.Mips, ShaderResults = results, NativeGetPixels = nativePixels } );
				}
			}
			// These remain exact-level requirements, not acceptance of coarse fallback.
			Assert.AreEqual( new V4( 512, 512, 10, 1 ), finalDimensions, "After the bounded full-resolution request, the bound view must expose the complete source levels." );
			Assert.IsTrue( MaximumError( Expected( levels[0] ), finalLoad ) <= .04f, "Explicit Load(mip0) must read the authored white texel." );
			Assert.IsTrue( MaximumError( Expected( levels[0] ), finalSample ) <= .04f, "Original nearest magnification request must read the same authored mip0 texel." );
		}
		finally
		{
			await MainThread.Wait(); material?.Destroy(); uploaded?.Dispose(); single?.Dispose(); full?.Dispose(); shader?.Destroy();
		}
	}

	private static V4 Sample( Material material, Texture texture, int mode )
	{
		var state = global::Sandbox.GltfSamplerMapping.Map( new GltfSampler { MinFilter = 9984, MagFilter = 9728, WrapS = 10497, WrapT = 10497 }, 9987, 9729 ).State;
		material.Attributes.Set( "ProbeTexture", texture ); material.Attributes.Set( "GltfBaseColorSampler", state );
		using var target = Texture.CreateRenderTarget().WithSize( 1, 1 ).WithFormat( ImageFormat.RGBA32323232F ).Create();
		using ( var scope = Graphics.Scope.Create() )
		{
			Graphics.RenderTarget = RenderTarget.From( target ); scope.Context.Clear( Color.Magenta, true, false, false );
			material.Attributes.MergeTo( Graphics.Attributes ); Graphics.Attributes.Set( "ProbeMode", mode ); Graphics.Attributes.Set( "ProbeUv", Uv );
			Graphics.Attributes.Set( "ProbeDdx", new Vector2( .5f / 512, 0 ) ); Graphics.Attributes.Set( "ProbeDdy", new Vector2( 0, .5f / 512 ) );
			Graphics.Blit( material );
		}
		g_pRenderDevice.ForceFlushGPU( default ); var output = new float[4]; target.GetPixels( (0, 0, 1, 1), 0, 0, output.AsSpan(), ImageFormat.RGBA32323232F );
		return new( output[0], output[1], output[2], output[3] );
	}
	private static byte[] Encoded( Png.Image image ) => image.Rgba.AsSpan( ((int)(Uv.y * image.Height) * image.Width + (int)(Uv.x * image.Width)) * 4, 4 ).ToArray();
	private static V4 Expected( Png.Image image ) => TextureSettingsReference.Sample( image, Uv.x, Uv.y, 10497, 10497, false );
	private static float MaximumError( V4 expected, V4 actual ) { var d = V4.Abs( expected - actual ); return MathF.Max( MathF.Max( d.X, d.Y ), MathF.Max( d.Z, d.W ) ); }
	private static string Hash( byte[] value ) => Convert.ToHexStringLower( SHA256.HashData( value ) );
	private static void Idle() { ThreadSafe.AssertIsMainThread(); Assert.IsTrue( Application.IsEditor && !Game.IsPlaying && !Graphics.IsActive && Graphics.IsAvailable ); }
}
