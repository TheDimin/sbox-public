using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Rendering;

namespace Sandbox.Gltf.DirectGpu.TextureSettingsValidation;

using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;
using Png = Sandbox.Gltf.DirectGpu.BoxTexturedValidation.PngTexelReference;
using V4 = System.Numerics.Vector4;

/// <summary>Scoped full-resident positive control only; does not alter production streaming policy.</summary>
[TestClass]
public sealed class TextureSettingsResidentControlTests
{
	public static string CheckoutRoot { get; set; }
	public static string ExpectedShaderSha256 { get; set; }
	public static List<object> Measurements { get; } = new();

	[TestMethod]
	public async Task FreshScopedPreloadSeparatesReadinessFromMipSampling()
	{
		ThreadSafe.AssertIsMainThread(); Assert.IsTrue( Application.IsEditor && !Game.IsPlaying && !Graphics.IsActive && Graphics.IsAvailable ); Measurements.Clear();
		var source = File.ReadAllBytes( Path.Combine( CheckoutRoot, "engine/Tools/Gltf/direct-gpu/models/TextureSettingsTest/texture_settings_mip_diagnostic.shader" ) );
		Assert.AreEqual( ExpectedShaderSha256, Convert.ToHexStringLower( SHA256.HashData( source ) ) );
		using var reference = new TextureSettingsReference( CheckoutRoot ); using var document = reference.Open( "glTF", false );
		var resource = global::Sandbox.GltfTextureCompiler.CompileWithMipmaps( document.ReadImage( 1 ), true );
		var sample = typeof( TextureSettingsMipDiagnosticTests ).GetMethod( "Sample", BindingFlags.Static | BindingFlags.NonPublic )!;
		var reduce = typeof( TextureSettingsSamplerGpuTests ).GetMethod( "Reduce", BindingFlags.Static | BindingFlags.NonPublic )!;
		var mip1 = (Png.Image)reduce.Invoke( null, new object[] { reference.Images[1] } );
		V4 Expected( Png.Image image ) => TextureSettingsReference.Sample( image, .34228515625, .7646484375, 10497, 10497, false );
		Shader shader = null; Material material = null; global::Sandbox.GltfMaterialLease<Texture> normal = null, resident = null;
		try
		{
			using var cancel = new CancellationTokenSource( TimeSpan.FromSeconds( 90 ) );
			shader = await Shader.CompileAsync( System.Text.Encoding.UTF8.GetString( source ), cancel.Token ); await MainThread.Wait(); ThreadSafe.AssertIsMainThread();
			Assert.IsTrue( shader is { IsValid: true, IsAnonymous: true } );
			material = Material.Create( "texture_settings_resident_control_" + Guid.NewGuid().ToString( "N" ), shader ); Assert.IsTrue( material is { IsValid: true, IsError: false } );
			normal = global::Sandbox.GltfNativeTextureCache.Acquire( resource ); var priorTexture = normal.Value;
			foreach ( var mode in new[] { 0, 1, 3, 4 } )
			{
				var value = (V4)sample.Invoke( null, new object[] { material, priorTexture, mode } );
				Measurements.Add( new { Phase = "default_streamed_before", Mode = mode, Actual = value.ToString(), Frame = Application.FrameCount } );
			}
			// No cache hit may masquerade as an independently force-preloaded load.
			normal.Dispose(); normal = null;
			TextureStreaming.ExecuteWithDisabled( () => resident = global::Sandbox.GltfNativeTextureCache.Acquire( resource ) );
			Assert.AreNotSame( priorTexture, resident.Value, "Another live cache lease prevented a fresh scoped-preload control." );
			var failed = new List<string>();
			for ( var mode = 0; mode <= 4; mode++ )
			{
				var actual = (V4)sample.Invoke( null, new object[] { material, resident.Value, mode } );
				var expected = mode == 0 ? new V4( 512, 512, 10, 1 ) : mode == 2 ? Expected( mip1 ) : Expected( reference.Images[1] );
				var error = V4.Abs( expected - actual ); var maximum = MathF.Max( MathF.Max( error.X, error.Y ), MathF.Max( error.Z, error.W ) );
				Measurements.Add( new { Phase = "fresh_scoped_preload", Mode = mode, Expected = expected.ToString(), Actual = actual.ToString(), MaximumError = maximum, Frame = Application.FrameCount } );
				if ( !float.IsFinite( maximum ) || maximum > (mode == 0 ? 0 : .04f) ) failed.Add( $"Mode {mode}: expected {expected}, actual {actual}, maximum {maximum}" );
			}
			Assert.AreEqual( 0, failed.Count, string.Join( "\n", failed ) );
		}
		finally { await MainThread.Wait(); material?.Destroy(); normal?.Dispose(); resident?.Dispose(); shader?.Destroy(); }
	}
}
