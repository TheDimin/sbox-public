using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Sandbox.Gltf.DirectGpu.TextureTransformTestValidation;

using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;
using Png = Sandbox.Gltf.DirectGpu.BoxTexturedValidation.PngTexelReference;
using NativeTexture = Sandbox.Gltf.DirectGpu.BoxTexturedValidation.BoxTexturedReference;
using NVector4 = System.Numerics.Vector4;

/// <summary>
/// Actual engine texture serialization versus the Box owner's independently
/// decoded PNGs. These checks do not claim that a shader obeys its sampler.
/// </summary>
[TestClass]
public sealed class TextureTransformTextureResourceTests
{
	public static string CheckoutRoot { get; set; }

	[TestMethod]
	[DataRow( 0 )]
	[DataRow( 1 )]
	[DataRow( 2 )]
	[DataRow( 3 )]
	[DataRow( 4 )]
	public void EverySourcePngMatchesStraightRgbaNativeSerialization( int imageIndex )
	{
		using var input = new TextureTransformReference.Inputs( CheckoutRoot, false );
		var image = input.Document.ReadImage( imageIndex ); var expected = Png.Decode( image.Bytes );
		Assert.AreEqual( imageIndex == 0 ? 256 : 128, expected.Width ); Assert.AreEqual( expected.Width, expected.Height );
		Assert.AreEqual( imageIndex < 2 ? 6 : 2, expected.ColorType );
		var actual = NativeTexture.ParseNativeTextureResource( global::Sandbox.GltfTextureCompiler.Compile( image, true ) );
		Assert.AreEqual( expected.Width, actual.Width ); Assert.AreEqual( expected.Height, actual.Height );
		Assert.AreEqual( 1, actual.MipCount ); Assert.AreEqual( 4, actual.Format );
		Assert.IsTrue( (actual.Flags & 0x0008) != 0 && (actual.Flags & 0x0200) == 0, "sRGB source must retain base-only sRGB storage." );
		CollectionAssert.AreEqual( expected.Rgba, actual.Payload, "RGBA values or top-left row order differ from independent PNG bytes." );
	}

	[TestMethod]
	[DataRow( 0, 0 )]
	[DataRow( 3, 1 )]
	public void DefaultStockMaterialMainTexturesKeepClampLinearAndNoMip( int materialIndex, int imageIndex )
	{
		using var input = new TextureTransformReference.Inputs( CheckoutRoot, false );
		var expected = Png.Decode( input.Document.ReadImage( imageIndex ).Bytes );
		var prepared = global::Sandbox.GltfStockMaterialAdapter.Prepare( input.Document, input.Document.ReadMaterial( materialIndex ) );
		var native = NativeTexture.ParseNativeTextureResource( prepared.BaseColorResource );
		Assert.AreEqual( 1, native.MipCount ); Assert.AreEqual( expected.Width, native.Width ); Assert.AreEqual( expected.Height, native.Height );
		CollectionAssert.AreEqual( expected.Rgba, native.Payload );
		Assert.AreEqual( global::Sandbox.Rendering.TextureAddressMode.Clamp, prepared.ColorSampler.AddressModeU );
		Assert.AreEqual( global::Sandbox.Rendering.TextureAddressMode.Clamp, prepared.ColorSampler.AddressModeV );
		Assert.AreEqual( 0x14, Convert.ToInt32( prepared.ColorSampler.Filter ), "Independent min/mag linear bits; no mip filter bit." );
		Assert.AreEqual( 0f, prepared.MetallicFactor ); Assert.AreEqual( 1f, prepared.RoughnessFactor );
	}

	[TestMethod]
	[DataRow( 0 )]
	[DataRow( 1 )]
	public void SourceBordersProvideIndependentClampVersusWrapSensitivity( int imageIndex )
	{
		using var input = new TextureTransformReference.Inputs( CheckoutRoot, false );
		var image = Png.Decode( input.Document.ReadImage( imageIndex ).Bytes );
		var distinct = 0;
		foreach ( var v in new[] { .125, .375, .625, .875 } ) foreach ( var u in new[] { -.375, -.125, 1.125, 1.375 } )
		{
			var clamp = Png.BilinearLinearRgba( image.Rgba, image.Width, image.Height, u, v, true );
			var wrap = Png.BilinearLinearRgba( image.Rgba, image.Width, image.Height, u, v, false );
			Assert.IsTrue( float.IsFinite( clamp.X ) && float.IsFinite( clamp.Y ) && float.IsFinite( clamp.Z ) && float.IsFinite( clamp.W ) );
			if ( NVector4.Distance( clamp, wrap ) > .01f ) distinct++;
		}
		Assert.IsTrue( distinct > 0, "This exact source image must expose the native wrap-versus-clamp regression; decoder metadata alone is insufficient." );
	}
}
