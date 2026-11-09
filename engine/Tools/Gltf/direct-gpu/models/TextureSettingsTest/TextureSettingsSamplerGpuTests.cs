using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NativeEngine;
using Sandbox.Rendering;

namespace Sandbox.Gltf.DirectGpu.TextureSettingsValidation;

using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;
using Png = Sandbox.Gltf.DirectGpu.BoxTexturedValidation.PngTexelReference;
using V4 = System.Numerics.Vector4;
using Ref = TextureSettingsReference;

/// <summary>
/// Actual authored PNG/UV sampler acceptance. This diagnostic proves annotated
/// sampler realization, not final model lighting, Model.Load or whole-scene parity.
/// Coordinator alone may invoke anonymous compilation and graphics operations.
/// </summary>
[TestClass]
public sealed class TextureSettingsSamplerGpuTests
{
	public static string CheckoutRoot { get; set; }
	public static string ExpectedShaderSha256 { get; set; }
	public static List<object> Measurements { get; } = new();

	[TestMethod]
	public async Task ActualSourceImagesHonorIndependentFiltersWrapsAndMaterialRoles()
	{
		Assert.IsTrue( Application.IsEditor && !Game.IsPlaying && !Graphics.IsActive && Graphics.IsAvailable );
		ThreadSafe.AssertIsMainThread(); Measurements.Clear();
		Measurements.Add( new { Phase = "sampling_plan", ExpectedPixelReadbacks = 102, MaximumError = .04, OriginalFailureReceipt = "454761de8fe6263661da49a84989385a14f6d991a98c279071a998b4732b5716" } );
		var path = Path.Combine( CheckoutRoot, "engine/Tools/Gltf/direct-gpu/models/TextureSettingsTest/texture_settings_sampler_candidate.shader" );
		var bytes = File.ReadAllBytes( path );
		Assert.AreEqual( ExpectedShaderSha256, Convert.ToHexStringLower( SHA256.HashData( bytes ) ), "Coordinator must approve exact candidate source." );
		using var reference = new Ref( CheckoutRoot ); using var document = reference.Open( "glTF", false );
		Shader shader = null; Material material = null;
		var textures = new List<global::Sandbox.GltfMaterialLease<Texture>>();
		try
		{
			using var cancellation = new CancellationTokenSource( TimeSpan.FromSeconds( 90 ) );
			shader = await Shader.CompileAsync( System.Text.Encoding.UTF8.GetString( bytes ), cancellation.Token );
			await MainThread.Wait();
			Assert.IsTrue( shader is { IsValid: true, IsAnonymous: true } );
			material = Material.Create( "texture_settings_sampler_" + Guid.NewGuid().ToString( "N" ), shader );
			Assert.IsTrue( material is { IsValid: true, IsError: false } );
			for ( var i = 0; i < 2; i++ ) textures.Add( global::Sandbox.GltfNativeTextureCache.Acquire( global::Sandbox.GltfTextureCompiler.CompileWithMipmaps( document.ReadImage( i ), true ) ) );
			var mip = Reduce( reference.Images[1] );
			var filterUvs = FilterWitnesses( reference.Images[1], mip );
			// glTF's six min filters x two mag filters. Non-mip modes must get an
			// actual single-level resource; setting MaxLod=0 is not equivalent.
			using var baseTexture = global::Sandbox.GltfNativeTextureCache.Acquire( global::Sandbox.GltfTextureCompiler.Compile( document.ReadImage( 1 ), true ) );
			await TextureSettingsTextureReadiness.Wait( CheckoutRoot, Measurements,
				new( "CheckAndX_V_full", textures[0].Value, reference.Images[0].Width, reference.Images[0].Height, 10 ),
				new( "CheckAndX_full", textures[1].Value, reference.Images[1].Width, reference.Images[1].Height, 10 ),
				new( "CheckAndX_base", baseTexture.Value, reference.Images[1].Width, reference.Images[1].Height, 1 ) );
			foreach ( var min in new[] { 9728, 9729, 9984, 9985, 9986, 9987 } ) foreach ( var mag in new[] { 9728, 9729 } )
			{
				var mapped = global::Sandbox.GltfSamplerMapping.Map( new GltfSampler { MinFilter = min, MagFilter = mag, WrapS = 10497, WrapT = 10497 }, 9987, 9729 );
				Assert.IsNull( mapped.State.MaxLod );
				var texture = mapped.RequiresBaseMipOnly ? baseTexture.Value : textures[1].Value;
				foreach ( var lod in new[] { -1f, .25f } ) foreach ( var uv in filterUvs )
				{
					var expected = Expected( reference.Images[1], mip, uv.x, uv.y, lod, min, mag, 10497, 10497 );
					Check( $"min={min},mag={mag},lod={lod},uv={uv}", expected, Sample( material, texture, mapped.State, mapped.State, uv, lod, 0 ) );
				}
			}
			// Full cross product of S and T, including negative and >1 tiles.
			foreach ( var s in new[] { 10497, 33071, 33648 } ) foreach ( var t in new[] { 10497, 33071, 33648 } )
			{
				var state = global::Sandbox.GltfSamplerMapping.Map( new GltfSampler { MinFilter = 9986, MagFilter = 9729, WrapS = s, WrapT = t }, 9987, 9729 ).State;
				foreach ( var uv in new[] { new Vector2( -.337f, 1.673f ), new Vector2( 1.673f, -.337f ) } )
					Check( $"independent S={s},T={t},uv={uv}", Ref.Sample( reference.Images[1], uv.x, uv.y, s, t, true ), Sample( material, textures[1].Value, state, state, uv, -1, 0 ) );
			}
			// All authored test triangles and every wrong wrap negative control.
			foreach ( var mesh in new[] { 0, 1, 2, 3, 5, 6 } )
			{
				var ti = Ref.MaterialTextures[mesh]; var si = Ref.TextureSamplers[ti]; var imageIndex = Ref.TextureImages[ti]; var image = reference.Images[imageIndex];
				var state = global::Sandbox.GltfSamplerMapping.Map( document.ReadSampler( si ), 9987, 9729 ).State;
				var axisS = mesh is 1 or 5 or 6;
				foreach ( var wrong in new[] { 10497, 33071, 33648 }.Where( x => x != (axisS ? Ref.WrapS[si] : Ref.WrapT[si]) ) )
				{
					var us = axisS ? wrong : Ref.WrapS[si]; var vt = axisS ? Ref.WrapT[si] : wrong;
					var uv = reference.TestColumnUvs( mesh ).OrderByDescending( x => V4.Distance( Ref.Sample( image, x.X, x.Y, Ref.WrapS[si], Ref.WrapT[si], true ), Ref.Sample( image, x.X, x.Y, us, vt, true ) ) ).First();
					var expected = Ref.Sample( image, uv.X, uv.Y, Ref.WrapS[si], Ref.WrapT[si], true ); var negative = Ref.Sample( image, uv.X, uv.Y, us, vt, true );
					Assert.IsTrue( V4.Distance( expected, negative ) > .1f, "Actual source geometry must distinguish the wrong mode." );
					var bad = global::Sandbox.GltfSamplerMapping.Map( new GltfSampler { MinFilter = 9986, MagFilter = 9729, WrapS = us, WrapT = vt }, 9987, 9729 ).State;
					var coord = new Vector2( uv.X, uv.Y );
					Check( $"mesh={mesh},correct role0,negative={wrong}", expected, Sample( material, textures[imageIndex].Value, state, bad, coord, -1, 0 ) );
					Check( $"mesh={mesh},independent role1,negative={wrong}", negative, Sample( material, textures[imageIndex].Value, state, bad, coord, -1, 1 ) );
					Check( $"mesh={mesh},role0 unchanged after role1", expected, Sample( material, textures[imageIndex].Value, state, bad, coord, -1, 0 ) );
				}
			}
		}
		finally
		{
			await MainThread.Wait(); material?.Destroy();
			foreach ( var texture in textures ) texture.Dispose(); shader?.Destroy();
		}
	}

	private static Vector2[] FilterWitnesses( Png.Image image, Png.Image mip )
	{
		var candidates = new List<Vector2>();
		for ( var y = 1; y < image.Height - 1; y += 3 ) for ( var x = 1; x < image.Width - 1; x += 3 ) candidates.Add( new( (x + .25f) / image.Width, (y + .5f) / image.Height ) );
		var mag = candidates.OrderByDescending( uv => V4.Distance( Ref.Sample( image, uv.x, uv.y, 10497, 10497, false ), Ref.Sample( image, uv.x, uv.y, 10497, 10497, true ) ) ).First();
		var mipUv = candidates.OrderByDescending( uv => V4.Distance( Ref.Sample( image, uv.x, uv.y, 10497, 10497, false ), Ref.Sample( mip, uv.x, uv.y, 10497, 10497, false ) ) ).First();
		Assert.IsTrue( V4.Distance( Ref.Sample( image, mag.x, mag.y, 10497, 10497, false ), Ref.Sample( image, mag.x, mag.y, 10497, 10497, true ) ) > .08f );
		Assert.IsTrue( V4.Distance( Ref.Sample( image, mipUv.x, mipUv.y, 10497, 10497, false ), Ref.Sample( mip, mipUv.x, mipUv.y, 10497, 10497, false ) ) > .16f );
		return new[] { mag, mipUv };
	}
	private static V4 Expected( Png.Image image, Png.Image mip, double u, double v, float lod, int min, int mag, int s, int t )
	{
		if ( lod <= 0 ) return Ref.Sample( image, u, v, s, t, mag == 9729 );
		var linear = min is 9729 or 9985 or 9987;
		var first = Ref.Sample( image, u, v, s, t, linear );
		return min is 9986 or 9987 ? V4.Lerp( first, Ref.Sample( mip, u, v, s, t, linear ), lod ) : first;
	}
	private static Png.Image Reduce( Png.Image source )
	{
		var w = source.Width / 2; var h = source.Height / 2; var pixels = new byte[w * h * 4];
		for ( var y = 0; y < h; y++ ) for ( var x = 0; x < w; x++ ) for ( var c = 0; c < 4; c++ )
		{
			double sum = 0;
			for ( var dy = 0; dy < 2; dy++ ) for ( var dx = 0; dx < 2; dx++ )
			{
				var value = source.Rgba[((y * 2 + dy) * source.Width + x * 2 + dx) * 4 + c] / 255d;
				sum += c == 3 ? value : value <= .04045 ? value / 12.92 : Math.Pow( (value + .055) / 1.055, 2.4 );
			}
			var mean = sum / 4; if ( c < 3 ) mean = mean <= .0031308 ? mean * 12.92 : 1.055 * Math.Pow( mean, 1 / 2.4 ) - .055;
			pixels[(y * w + x) * 4 + c] = checked((byte)Math.Round( mean * 255, MidpointRounding.ToEven ));
		}
		return new( w, h, 6, pixels );
	}
	private static V4 Sample( Material material, Texture texture, SamplerState color, SamplerState surface, Vector2 uv, float lod, int role )
	{
		material.Attributes.Set( "ProbeTexture", texture ); material.Attributes.Set( "GltfBaseColorSampler", color ); material.Attributes.Set( "GltfMetallicRoughnessSampler", surface );
		using var target = Texture.CreateRenderTarget().WithSize( 1, 1 ).WithFormat( ImageFormat.RGBA32323232F ).WithInitialColor( Color.Magenta ).Create();
		using ( var scope = Graphics.Scope.Create() )
		{
			Graphics.RenderTarget = RenderTarget.From( target ); scope.Context.Clear( Color.Magenta, true, false, false );
			material.Attributes.MergeTo( Graphics.Attributes );
			Graphics.Attributes.Set( "ProbeUv", uv ); Graphics.Attributes.Set( "ProbeRole", role );
			Graphics.Attributes.Set( "ProbeDdx", new Vector2( MathF.Pow( 2, lod ) / texture.Width, 0 ) );
			Graphics.Attributes.Set( "ProbeDdy", new Vector2( 0, MathF.Pow( 2, lod ) / texture.Height ) );
			Graphics.Blit( material );
		}
		g_pRenderDevice.ForceFlushGPU( default ); var pixels = new float[4];
		target.GetPixels( (0, 0, 1, 1), 0, 0, pixels.AsSpan(), ImageFormat.RGBA32323232F );
		return new( pixels[0], pixels[1], pixels[2], pixels[3] );
	}
	private static void Check( string name, V4 expected, V4 actual )
	{
		var error = V4.Abs( expected - actual ); var maximum = MathF.Max( MathF.Max( error.X, error.Y ), MathF.Max( error.Z, error.W ) );
		Measurements.Add( new { Name = name, Expected = expected.ToString(), Actual = actual.ToString(), MaximumError = maximum } );
		Assert.IsTrue( float.IsFinite( maximum ) && maximum <= .04f, $"{name}: expected {expected}, actual {actual}; max error {maximum}." );
	}
}
