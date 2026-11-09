using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Sandbox.Gltf.DirectGpu.TextureSettingsValidation;

using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;
using V4 = System.Numerics.Vector4;

/// <summary>
/// Sampling-test precondition only: observe actual complete GPU views while
/// preserving default native streaming. On-disk dimensions and preload flags
/// are not residency evidence. This does not change production load policy.
/// </summary>
internal static class TextureSettingsTextureReadiness
{
	private const string ShaderSha256 = "415836bf47e7e43600a493deb5d7e73c3c2599ffda48ad9c72760d2a0f3a2003";
	internal readonly record struct Request( string Name, Texture Texture, int Width, int Height, int Mips );

	internal static async Task Wait( string checkout, List<object> observations, params Request[] requests )
	{
		ArgumentNullException.ThrowIfNull( observations ); ThreadSafe.AssertIsMainThread();
		var source = File.ReadAllBytes( Path.Combine( checkout, "engine/Tools/Gltf/direct-gpu/models/TextureSettingsTest/texture_settings_mip_diagnostic.shader" ) );
		Assert.AreEqual( ShaderSha256, Convert.ToHexStringLower( SHA256.HashData( source ) ) );
		var sample = typeof( TextureSettingsMipDiagnosticTests ).GetMethod( "Sample", BindingFlags.NonPublic | BindingFlags.Static )!;
		Shader shader = null; Material material = null;
		try
		{
			using var cancellation = new CancellationTokenSource( TimeSpan.FromSeconds( 90 ) );
			shader = await Shader.CompileAsync( System.Text.Encoding.UTF8.GetString( source ), cancellation.Token ); await MainThread.Wait();
			Assert.IsTrue( shader is { IsValid: true, IsAnonymous: true } );
			material = Material.Create( "texture_settings_readiness_" + Guid.NewGuid().ToString( "N" ), shader ); Assert.IsTrue( material is { IsValid: true, IsError: false } );
			var timer = Stopwatch.StartNew(); var first = Application.FrameCount; ulong? previousReadyFrame = null;
			while ( true )
			{
				ThreadSafe.AssertIsMainThread(); Assert.IsTrue( Application.IsEditor && !Game.IsPlaying && !Graphics.IsActive );
				var allReady = true;
				foreach ( var request in requests )
				{
					request.Texture.MarkUsed( Math.Max( request.Width, request.Height ) );
					var actual = (V4)sample.Invoke( null, new object[] { material, request.Texture, 0 } );
					var expected = new V4( request.Width, request.Height, request.Mips, 1 ); var ready = actual == expected; allReady &= ready;
					observations.Add( new { Phase = "texture_gpu_view_readiness", request.Name, Expected = expected.ToString(), Actual = actual.ToString(), Ready = ready, Frame = Application.FrameCount, ElapsedFrames = Application.FrameCount - first, Milliseconds = timer.Elapsed.TotalMilliseconds, DeadlineSeconds = 10 } );
				}
				// Two actual distinct frames protect against treating a single cached
				// or transient observation as the stable starting view for all102.
				if ( allReady && previousReadyFrame is ulong prior && Application.FrameCount > prior ) return;
				previousReadyFrame = allReady ? Application.FrameCount : null;
				Assert.IsTrue( timer.Elapsed < TimeSpan.FromSeconds( 10 ), "Native textures did not expose the complete independently expected GPU view within the bounded readiness wait." );
				var observedFrame = Application.FrameCount;
				do { await Task.Delay( 20 ); await MainThread.Wait(); Assert.IsTrue( timer.Elapsed < TimeSpan.FromSeconds( 10 ), "Real frames did not advance within the bounded texture readiness wait." ); }
				while ( Application.FrameCount == observedFrame );
			}
		}
		finally { await MainThread.Wait(); material?.Destroy(); shader?.Destroy(); }
	}
}
