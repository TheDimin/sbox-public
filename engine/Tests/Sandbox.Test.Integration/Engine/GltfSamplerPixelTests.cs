using NativeEngine;
using Sandbox.Rendering;
using System;
using System.IO;

namespace EngineTests;

/// <summary>
/// Opt-in GPU verification of managed sampler descriptors. The reference values are
/// derived from the authored checker and uniform mip colors, not from native enum names.
/// </summary>
[TestClass]
public class GltfSamplerPixelTests
{
	[TestInitialize]
	public void RequireGraphics()
	{
		if ( Environment.GetEnvironmentVariable( "SBOX_TEST_GRAPHICS" ) != "1" || !Graphics.IsAvailable )
			Assert.Inconclusive( "Set SBOX_TEST_GRAPHICS=1 and initialize a real renderer to run sampler pixel tests." );
	}

	[TestMethod]
	[DataRow( FilterMode.Point )]
	[DataRow( FilterMode.MinMagPointMipLinear )]
	[DataRow( FilterMode.MinPointMagLinearMipPoint )]
	[DataRow( FilterMode.MinPointMagMipLinear )]
	[DataRow( FilterMode.MinLinearMagMipPoint )]
	[DataRow( FilterMode.MinLinearMagPointMipLinear )]
	[DataRow( FilterMode.Bilinear )]
	[DataRow( FilterMode.Trilinear )]
	public void IndependentMinMagAndMipFiltersMatchReferencePixels( FilterMode filter )
	{
		using var texture = MakeTexture( true );
		var material = MakeMaterial();
		try
		{
			var sampler = Sampler( filter );
			var bits = (int)filter;
			var magBase = (bits & 0x04) == 0 ? 1f : 0.75f;
			var minBase = (bits & 0x10) == 0 ? 1f : 0.75f;
			var mipMix = (bits & 0x01) == 0 ? 0f : 0.25f;
			var expectedMin = new Vector3( minBase * (1 - mipMix), minBase * (1 - mipMix) + mipMix, minBase * (1 - mipMix) );
			foreach ( var bindless in new[] { false, true } )
			{
				AssertPixel( new Vector3( magBase, magBase, magBase ), Sample( material, texture, sampler, new Vector2( 0.4375f, 0.375f ), -1, bindless ), $"{filter} magnification, bindless={bindless}" );
				AssertPixel( expectedMin, Sample( material, texture, sampler, new Vector2( 0.4375f, 0.375f ), 0.25f, bindless ), $"{filter} minification, bindless={bindless}" );
			}
		}
		finally { material.Destroy(); }
	}

	[TestMethod]
	[DataRow( FilterMode.Point )]
	[DataRow( FilterMode.MinMagPointMipLinear )]
	[DataRow( FilterMode.MinPointMagLinearMipPoint )]
	[DataRow( FilterMode.MinPointMagMipLinear )]
	[DataRow( FilterMode.MinLinearMagMipPoint )]
	[DataRow( FilterMode.MinLinearMagPointMipLinear )]
	[DataRow( FilterMode.Bilinear )]
	[DataRow( FilterMode.Trilinear )]
	// Vulkan applies the sampler's LOD clamp before selecting minification versus
	// magnification, so MaxLod=0 chooses the magnification filter at every scale.
	// See VkSamplerCreateInfo's "Mapping of OpenGL to Vulkan Filter Modes" note.
	public void VulkanMaxLodZeroUsesMagnificationFilterEvenWhenMinified( FilterMode filter )
	{
		if ( g_pRenderDevice.GetRenderDeviceAPI() != RenderDeviceAPI_t.RENDER_DEVICE_API_VULKAN )
			Assert.Inconclusive( "This assertion follows Vulkan's specified LOD-clamp and filter-selection ordering." );
		using var texture = MakeTexture( true );
		var material = MakeMaterial();
		try
		{
			var sampler = Sampler( filter );
			var clamped = sampler with { MaxLod = 0 };
			Assert.AreNotEqual( sampler.Index, clamped.Index, "Sampler cache must distinguish the LOD clamp." );
			Assert.AreEqual( clamped.Index, (sampler with { MaxLod = 0 }).Index, "Identical sampler states should reuse their index." );
			foreach ( var bindless in new[] { false, true } )
			{
				var uv = new Vector2( 0.4375f, 0.375f );
				var mag = ((int)filter & 0x04) == 0 ? 1f : 0.75f;
				var expected = new Vector3( mag, mag, mag );
				var actual = Sample( material, texture, clamped, uv, 1, bindless );
				AssertPixel( expected, actual, $"{filter} Vulkan max LOD 0 uses mag filter, bindless={bindless}" );
				var unclamped = Sample( material, texture, sampler, uv, 1, bindless );
				Assert.IsTrue( MathF.Abs( unclamped.x - actual.x ) > 0.1f, "The mip colors must make the clamp observable." );
			}
		}
		finally { material.Destroy(); }
	}

	[TestMethod]
	[DataRow( FilterMode.Point )]
	[DataRow( FilterMode.MinMagPointMipLinear )]
	[DataRow( FilterMode.MinPointMagLinearMipPoint )]
	[DataRow( FilterMode.MinPointMagMipLinear )]
	[DataRow( FilterMode.MinLinearMagMipPoint )]
	[DataRow( FilterMode.MinLinearMagPointMipLinear )]
	[DataRow( FilterMode.Bilinear )]
	[DataRow( FilterMode.Trilinear )]
	public void SingleMipTextureRetainsIndependentMinificationAndMagnification( FilterMode filter )
	{
		using var texture = MakeTexture( false );
		var material = MakeMaterial();
		try
		{
			var sampler = Sampler( filter );
			var uv = new Vector2( 0.4375f, 0.375f );
			var mag = ((int)filter & 0x04) == 0 ? 1f : 0.75f;
			var min = ((int)filter & 0x10) == 0 ? 1f : 0.75f;
			foreach ( var bindless in new[] { false, true } )
			{
				AssertPixel( new Vector3( mag, mag, mag ), Sample( material, texture, sampler, uv, -1, bindless ), $"{filter} single mip magnification, bindless={bindless}" );
				AssertPixel( new Vector3( min, min, min ), Sample( material, texture, sampler, uv, 1, bindless ), $"{filter} single mip minification, bindless={bindless}" );
			}
		}
		finally { material.Destroy(); }
	}

	[TestMethod]
	[DataRow( TextureAddressMode.Wrap, 1f, 0f )]
	[DataRow( TextureAddressMode.Mirror, 0f, 1f )]
	[DataRow( TextureAddressMode.Clamp, 0f, 0f )]
	public void AddressModesSelectExpectedTexelsOutsideTexture( TextureAddressMode address, float first, float second )
	{
		using var texture = MakeTexture( true );
		var material = MakeMaterial();
		try
		{
			var sampler = Sampler( FilterMode.Point ) with { AddressModeU = address, MaxLod = 0 };
			foreach ( var bindless in new[] { false, true } )
			{
				AssertPixel( new Vector3( first, first, first ), Sample( material, texture, sampler, new Vector2( -0.0625f, 0.375f ), -1, bindless ), $"{address} first, bindless={bindless}" );
				AssertPixel( new Vector3( second, second, second ), Sample( material, texture, sampler, new Vector2( -0.3125f, 0.375f ), -1, bindless ), $"{address} second, bindless={bindless}" );
			}
		}
		finally { material.Destroy(); }
	}

	private static SamplerState Sampler( FilterMode filter ) => new()
	{
		Filter = filter,
		AddressModeU = TextureAddressMode.Clamp,
		AddressModeV = TextureAddressMode.Clamp,
		MaxAnisotropy = 1
	};

	private static Material MakeMaterial()
	{
		var material = Material.Create( "gltf-sampler-probe", "shaders/gltf_sampler_probe.shader" );
		Assert.IsNotNull( material );
		Assert.IsTrue( material.IsValid );
		Assert.AreEqual( "shaders/gltf_sampler_probe.shader", material.Shader.native.GetFilename(), "Probe shader was not compiled or mounted." );
		return material;
	}

	private static Texture MakeTexture( bool mips )
	{
		using var data = new MemoryStream();
		if ( mips )
		{
			WritePixel( data, 0, 0, 255 ); // 1x1 mip 2: blue
			for ( var i = 0; i < 4; i++ ) WritePixel( data, 0, 255, 0 ); // 2x2 mip 1: green
		}
		for ( var y = 0; y < 4; y++ )
			for ( var x = 0; x < 4; x++ )
			{
				byte value = (byte)(x % 2 == 0 ? 0 : 255);
				WritePixel( data, value, value, value );
			}
		return Texture.Create( 4, 4, ImageFormat.RGBA8888 ).WithMips( mips ? 3 : 1 ).WithData( data.ToArray() ).WithStaticUsage().Finish();
	}

	private static void WritePixel( Stream data, byte r, byte g, byte b )
	{
		data.WriteByte( r ); data.WriteByte( g ); data.WriteByte( b ); data.WriteByte( 255 );
	}

	private static Vector3 Sample( Material material, Texture texture, SamplerState sampler, Vector2 uv, float lod, bool bindless )
	{
		// A floating-point target avoids sRGB render-target conversion and 8-bit output quantization.
		using var target = Texture.CreateRenderTarget().WithSize( 1, 1 ).WithFormat( ImageFormat.RGBA32323232F ).Create();
		var samplerIndex = bindless ? sampler.Index : 0;
		using ( var scope = Graphics.Scope.Create() )
		{
			Graphics.RenderTarget = RenderTarget.From( target );
			scope.Context.Clear( Color.Transparent, true, false, false );
			var derivative = MathF.Pow( 2f, lod ) / 4f;
			Graphics.Attributes.Set( "ProbeTexture", texture );
			Graphics.Attributes.Set( "ProbeSampler", sampler );
			Graphics.Attributes.Set( "ProbeSamplerIndex", samplerIndex );
			Graphics.Attributes.Set( "ProbeUseBindless", bindless ? 1 : 0 );
			Graphics.Attributes.Set( "ProbeUv", uv );
			Graphics.Attributes.Set( "ProbeDdx", new Vector2( derivative, 0 ) );
			Graphics.Attributes.Set( "ProbeDdy", new Vector2( 0, derivative ) );
			Graphics.Blit( material );
		}
		g_pRenderDevice.ForceFlushGPU( default );
		var pixels = new float[4];
		target.GetPixels( (0, 0, 1, 1), 0, 0, pixels.AsSpan(), ImageFormat.RGBA32323232F );
		return new Vector3( pixels[0], pixels[1], pixels[2] );
	}

	private static void AssertPixel( Vector3 expected, Vector3 actual, string message )
	{
		const float tolerance = 0.04f;
		Assert.IsTrue( float.IsFinite( actual.x ) && float.IsFinite( actual.y ) && float.IsFinite( actual.z ), message + " produced a non-finite pixel" );
		Assert.AreEqual( expected.x, actual.x, tolerance, message + " red" );
		Assert.AreEqual( expected.y, actual.y, tolerance, message + " green" );
		Assert.AreEqual( expected.z, actual.z, tolerance, message + " blue" );
	}
}
