using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox;
using Sandbox.Gltf;
using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;
using NMatrix = System.Numerics.Matrix4x4;

namespace Sandbox.Gltf.DirectGpu.ABeautifulGameValidation;

[TestClass]
internal sealed class OpticalCopyDiagnosticTests
{
	static string root;
	public static List<object> Measurements { get; } = new();
	public static void Configure( string checkout ) { root = checkout; Measurements.Clear(); }

	[TestMethod]
	public async Task TraceOpticalInputsAndCopyPixels()
	{
		await MainThread.Wait();
		var owned = Path.Combine( root, "research/gltf-validation/direct-gpu/models/ABeautifulGame" );
		var source = File.ReadAllBytes( Path.Combine( owned, "gltf_optical_copy_diagnostic.shader" ) );
		Assert.AreEqual( "23f7f8c193e68f02667bf491a35ea38174ba4cfc5d509b49f78e3fccb0e3fd9c", Hash( source ) );
		var fixture = File.ReadAllBytes( Path.Combine( owned, "optical-control-plane.gltf" ) );
		Assert.AreEqual( "d83fbbde05a3071d54ceabf4aac34901a2a79bb52165ec5cd22404dbc5e0543f", Hash( fixture ) );
		var output = Path.Combine( root, "Temp/gltf-validation/direct-gpu/ABeautifulGame/optical-copy-diagnostic", Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( output );
		using var capture = new GltfGpuTriangleGateTests.NativeCapture( output, new global::Vector3( 200, 0, 0 ), global::Vector3.Zero, 100 );
		capture.Scene.SceneWorld.AmbientLightColor = Color.Black;
		var light = (ScenePointLight)typeof(GltfGpuTriangleGateTests.NativeCapture).GetField( "light", BindingFlags.Instance | BindingFlags.NonPublic ).GetValue( capture );
		light.LightColor = Color.Black;
		Shader shader = null;
		Material optics = null, red = null, green = null;
		try
		{
			using ( var timeout = new CancellationTokenSource( TimeSpan.FromSeconds( 90 ) ) )
				shader = await Shader.CompileAsync( Encoding.UTF8.GetString( source ), timeout.Token );
			await MainThread.Wait();
			Assert.IsTrue( shader is { IsValid: true, IsAnonymous: true } );
			optics = Material.Create( "optics_copy_" + Guid.NewGuid().ToString( "N" ), shader );
			optics.SetFeature( "F_TRANSLUCENT", 1 );
			optics.Set( "g_vGltfBaseColor", new global::Vector4( 1, 1, 1, 1 ) );
			optics.Set( "g_flGltfRoughnessFactor", 0f );
			optics.Set( "g_flGltfMetallicFactor", 0f );
			optics.Set( "g_flGltfOpticalTransmissionFactor", 1f );
			var backdrop = Material.FromShader( "shaders/gltf_unlit_five_uv.shader" );
			red = backdrop.CreateCopy( "optics_red_" + Guid.NewGuid().ToString( "N" ) );
			green = backdrop.CreateCopy( "optics_green_" + Guid.NewGuid().ToString( "N" ) );
			red.Set( "g_vGltfBaseColor", new global::Vector4( 1, 0, 0, 1 ) );
			green.Set( "g_vGltfBaseColor", new global::Vector4( 0, 1, 0, 1 ) );
			using var document = GltfReader.Read( fixture );
			using var redAsset = GltfGpuAsset.Prepare( document, GltfManagedModelResource.ComputeShaderPath, _ => red );
			using var greenAsset = GltfGpuAsset.Prepare( document, GltfManagedModelResource.ComputeShaderPath, _ => green );
			using var surfaceAsset = GltfGpuAsset.Prepare( document, GltfManagedModelResource.ComputeShaderPath, _ => optics );
			using var left = redAsset.CreateInstance( capture.Scene.SceneWorld );
			using var right = greenAsset.CreateInstance( capture.Scene.SceneWorld );
			using var surface = surfaceAsset.CreateInstance( capture.Scene.SceneWorld );
			left.Placement = NMatrix.CreateScale( .5f ) * NMatrix.CreateTranslation( -30, -16, 0 );
			right.Placement = NMatrix.CreateScale( .5f ) * NMatrix.CreateTranslation( -30, 16, 0 );
			Assert.IsTrue( surface.SceneObject.Flags.WantsFrameBufferCopy );
			var draw = ((System.Collections.IEnumerable)typeof(GltfGpuInstance).GetField( "draws", BindingFlags.Instance | BindingFlags.NonPublic ).GetValue( surface )).Cast<object>().Single();
			var sort = (SceneCustomObject)draw.GetType().GetField( "SortObject", BindingFlags.Instance | BindingFlags.NonPublic ).GetValue( draw );
			Assert.IsTrue( sort.IsValid() && sort.Flags.WantsFrameBufferCopy );
			var render = sort.RenderOverride;
			var copies = new System.Collections.Concurrent.ConcurrentDictionary<string, int>();
			sort.RenderOverride = obj =>
			{
				if ( Graphics.LayerType == SceneLayerType.Translucent && Graphics.Attributes.GetTexture( "FrameBufferCopyTexture" ) is { IsValid: true, IsError: false } copy )
					copies.AddOrUpdate( $"{copy.Width}x{copy.Height}:{copy.Mips}:{copy.ImageFormat}", 1, (_, count) => count + 1 );
				render( obj );
			};
			var names = new[] { "candidate", "optical_inputs", "screen_uv", "copy_scale", "stock_copy_uv", "dimensions_copy_uv", "texel_load", "unscaled_uv", "shader_copy_dimensions" };
			foreach ( bool postProcessing in new[] { false, true } )
			{
				capture.Camera.EnablePostProcessing = postProcessing;
				surface.SceneObject.RenderingEnabled = false;
				var opaque = await capture.Take( $"pp_{postProcessing}_opaque" );
				surface.SceneObject.RenderingEnabled = true;
				var rows = new List<object>();
				for ( var mode = 0; mode < names.Length; mode++ )
				{
					optics.Set( "g_flOpticalDebug", (float)mode );
					Assert.AreEqual( (float)mode, optics.GetVector4( "g_flOpticalDebug" ).x );
					var frame = await capture.Take( $"pp_{postProcessing}_{names[mode]}" );
					rows.Add( new { mode = names[mode], frame.Path, frame.Sha256, left = Pixel( frame, 70, 96 ), middle = Pixel( frame, 96, 96 ), right = Pixel( frame, 122, 96 ) } );
				}
				Measurements.Add( new { postProcessing, opaque.Path, opaque.Sha256, opaqueLeft = Pixel( opaque, 70, 96 ), opaqueRight = Pixel( opaque, 122, 96 ), rows } );
			}
			Measurements.Add( new { copies, actualTransmission = optics.GetVector4( "g_flGltfOpticalTransmissionFactor" ).x, sourceHash = Hash( source ) } );
			Assert.AreEqual( 0, surface.CallbackErrors.Length, string.Join( "\n", surface.CallbackErrors ) );
			Assert.IsTrue( copies.Count > 0 && surface.DrawCalls > 0 );
		}
		finally
		{
			await MainThread.Wait();
			optics?.Destroy();
			red?.Destroy();
			green?.Destroy();
			shader?.Destroy();
		}
	}

	static float[] Pixel( GltfGpuTriangleGateTests.NativeCapture.Frame frame, int x, int y )
	{
		var pixel = frame.Pixels[y * frame.Width + x];
		return [pixel.r, pixel.g, pixel.b];
	}

	static string Hash( byte[] bytes ) => Convert.ToHexStringLower( SHA256.HashData( bytes ) );
}
