using System;
using System.Numerics;
using System.Text;
using System.Buffers.Binary;
using Sandbox.Gltf;

namespace Sandbox.Tests;

[TestClass]
public class GltfStockMaterialTests
{
	[TestMethod]
	[DataRow( 0f, 0f )]
	[DataRow( 0.0031308f, 0.040449936f )]
	[DataRow( 0.21404114f, 0.5f )]
	[DataRow( 1f, 1f )]
	public void LinearGltfTintIsEncodedForTheStockShadersSrgbTint( float linear, float encoded )
	{
		Assert.AreEqual( encoded, GltfStockMaterialAdapter.EncodeTint( linear ), .000001f );
	}

	[TestMethod]
	[DataRow( 0f, 0f )]
	[DataRow( 0.3f, 0.7f )]
	[DataRow( 1f, 1f )]
	public void NonDefaultMetallicAndRoughnessFactorsArePreserved( float metallic, float roughness )
	{
		using var document = GltfReader.Read( Encoding.UTF8.GetBytes( """{"asset":{"version":"2.0"}}""" ) );
		var prepared = GltfStockMaterialAdapter.Prepare( document, Material( metallic, roughness ) );
		Assert.AreEqual( metallic, prepared.MetallicFactor );
		Assert.AreEqual( roughness, prepared.RoughnessFactor );
		Assert.IsTrue( prepared.NormalRoughnessResource.Length > 16 );
	}

	[TestMethod]
	public void OpaqueMaterialIgnoresAuthoredBaseColorAlpha()
	{
		using var document = GltfReader.Read( Encoding.UTF8.GetBytes( """{"asset":{"version":"2.0"}}""" ) );
		var prepared = GltfStockMaterialAdapter.Prepare( document, new GltfMaterial
		{
			BaseColorFactor = new System.Numerics.Vector4( .21404114f, 0, 1, .2f ),
			MetallicFactor = .5f, RoughnessFactor = .25f, AlphaMode = "OPAQUE"
		} );
		Assert.AreEqual( .5f, prepared.ColorTint.x, .000001f );
		Assert.AreEqual( 0f, prepared.ColorTint.y );
		Assert.AreEqual( 1f, prepared.ColorTint.z );
		Assert.AreEqual( 1f, prepared.ColorTint.w );
	}

	[TestMethod]
	[DataRow( "MASK" )]
	[DataRow( "BLEND" )]
	public void UnsupportedAlphaMappingsFailBeforeNativeAllocation( string alphaMode )
	{
		using var document = GltfReader.Read( Encoding.UTF8.GetBytes( """{"asset":{"version":"2.0"}}""" ) );
		Assert.ThrowsException<GltfException>( () => GltfStockMaterialAdapter.Prepare( document, new GltfMaterial
		{
			BaseColorFactor = System.Numerics.Vector4.One, MetallicFactor = 1, RoughnessFactor = 1, AlphaMode = alphaMode
		} ) );
	}

	private static GltfMaterial Material( float metallic, float roughness ) => new()
	{
		BaseColorFactor = System.Numerics.Vector4.One, MetallicFactor = metallic, RoughnessFactor = roughness, AlphaMode = "OPAQUE"
	};

	[TestMethod]
	public void ColorMipsAverageInLinearLightAndKeepAlphaLinear()
	{
		byte[] source = [0, 0, 0, 0, 255, 255, 255, 255];
		CollectionAssert.AreEqual( new byte[] { 188, 188, 188, 128 }, GltfTextureCompiler.Downsample( source, 2, 1, true ) );
		CollectionAssert.AreEqual( new byte[] { 128, 128, 128, 128 }, GltfTextureCompiler.Downsample( source, 2, 1, false ) );
	}

	[TestMethod]
	public void OddSizedMipsIncludeTheLastRowAndColumn()
	{
		byte[] source = [255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255];
		CollectionAssert.AreEqual( new byte[] { 85, 85, 85, 255 }, GltfTextureCompiler.Downsample( source, 3, 1, false ) );
	}

	[TestMethod]
	public void ColorTextureBudgetIncludesBothSerializationCopiesAtTheBoundary()
	{
		// 2x1 RGBA8 base=8bytes. Its full chain is8+4=12bytes; bitmap plus
		// chain, native-resource ByteStream and final resource copy total44bytes.
		GltfTextureCompiler.ValidateColorTextureBudget( 2, 1, true, 44 );
		Assert.ThrowsException<GltfException>( () => GltfTextureCompiler.ValidateColorTextureBudget( 2, 1, true, 43 ) );
		GltfTextureCompiler.ValidateColorTextureBudget( 2, 1, false, 32 );
		Assert.ThrowsException<GltfException>( () => GltfTextureCompiler.ValidateColorTextureBudget( 2, 1, false, 31 ) );
	}

	[TestMethod]
	public void MetallicRoughnessUsesGAndBInLinearSpaceAndIgnoresRAndAlpha()
	{
		byte[] source = [255, 127, 64, 0, 0, 127, 64, 255];
		var roughness = GltfTextureCompiler.PackMetallicRoughnessPixels( source, .7f, true );
		var metalness = GltfTextureCompiler.PackMetallicRoughnessPixels( source, .25f, false );
		for ( var i = 0; i < 2; i++ )
		{
			Assert.AreEqual( 1.00392163f * .5f, roughness[i * 4] );
			Assert.AreEqual( roughness[i * 4], roughness[i * 4 + 1] );
			Assert.AreEqual( (float)(127.0 / 255 * .7f), roughness[i * 4 + 2], .0000001f );
			Assert.AreEqual( (float)(64.0 / 255 * .25f), metalness[i * 4 + 1], .0000001f );
			Assert.AreEqual( 0f, metalness[i * 4] );
			Assert.AreEqual( 0f, metalness[i * 4 + 2] );
			Assert.AreEqual( 1f, roughness[i * 4 + 3] );
			Assert.AreEqual( 1f, metalness[i * 4 + 3] );
		}
	}

	[TestMethod]
	[DataRow( 0f )]
	[DataRow( 1f )]
	public void MetallicRoughnessFactorEndpointsAreExact( float factor )
	{
		byte[] source = [255, 255, 255, 255];
		Assert.AreEqual( factor, GltfTextureCompiler.PackMetallicRoughnessPixels( source, factor, true )[2] );
		Assert.AreEqual( factor, GltfTextureCompiler.PackMetallicRoughnessPixels( source, factor, false )[1] );
	}

	[TestMethod]
	public void MetallicRoughnessPackingRetainsSubByteFactorPrecision()
	{
		byte[] source = [0, 1, 1, 255];
		var roughness = GltfTextureCompiler.PackMetallicRoughnessPixels( source, .001f, true )[2];
		var metalness = GltfTextureCompiler.PackMetallicRoughnessPixels( source, .002f, false )[1];
		Assert.AreEqual( .001f / 255, roughness, .00000000001f );
		Assert.AreEqual( .002f / 255, metalness, .00000000001f );
		Assert.IsTrue( roughness > 0 && roughness < 1f / 255 );
		Assert.IsTrue( metalness > roughness );
	}

	[TestMethod]
	public void PackedMetallicRoughnessMipsAverageWithoutSrgbOrByteQuantization()
	{
		byte[] source = [0, 0, 255, 255, 255, 255, 0, 0];
		var resources = GltfTextureCompiler.CompileMetallicRoughnessPixels( source, 2, 1, .3f, .7f, true );
		// A native resource header stores the streaming payload offset at byte0.
		// Its smallest mip comes first, followed by the largest: 1+2 RGBA32F pixels.
		var roughnessOffset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian( resources.NormalRoughness.AsSpan() ));
		var metalnessOffset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian( resources.Metalness.AsSpan() ));
		Assert.AreEqual( 3 * 4 * sizeof(float), resources.NormalRoughness.Length - roughnessOffset );
		Assert.AreEqual( 3 * 4 * sizeof(float), resources.Metalness.Length - metalnessOffset );
		Assert.AreEqual( .35f, BinaryPrimitives.ReadSingleLittleEndian( resources.NormalRoughness.AsSpan( roughnessOffset + 2 * sizeof(float) ) ), .0000001f );
		Assert.AreEqual( .15f, BinaryPrimitives.ReadSingleLittleEndian( resources.Metalness.AsSpan( metalnessOffset + sizeof(float) ) ), .0000001f );
		Assert.AreEqual( .7f, BinaryPrimitives.ReadSingleLittleEndian( resources.NormalRoughness.AsSpan( roughnessOffset + 8 * sizeof(float) + 2 * sizeof(float) ) ), .0000001f );
		Assert.AreEqual( .3f, BinaryPrimitives.ReadSingleLittleEndian( resources.Metalness.AsSpan( metalnessOffset + 4 * sizeof(float) + sizeof(float) ) ), .0000001f );
	}

	[TestMethod]
	public void BaseMipOnlyPackingOmitsGeneratedMips()
	{
		var resources = GltfTextureCompiler.CompileMetallicRoughnessPixels( new byte[12], 3, 1, 1, 1, false );
		var offset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian( resources.NormalRoughness.AsSpan() ));
		Assert.AreEqual( 3 * 4 * sizeof(float), resources.NormalRoughness.Length - offset );
	}

	[TestMethod]
	public void FloatingPointOddSizedMipsIncludeTheFinalTexel()
	{
		float[] source = [0, 0, 0, 1, .3f, .6f, .9f, 1, .6f, .9f, .3f, 1];
		var result = GltfTextureCompiler.DownsampleLinearRgba( source, 3, 1 );
		Assert.AreEqual( .3f, result[0], .0000001f );
		Assert.AreEqual( .5f, result[1], .0000001f );
		Assert.AreEqual( .4f, result[2], .0000001f );
		Assert.AreEqual( 1f, result[3] );
	}

	[TestMethod]
	[DataRow( -.1f )]
	[DataRow( 1.1f )]
	public void InvalidMetallicRoughnessFactorsAreRejected( float factor )
	{
		Assert.ThrowsException<GltfException>( () => GltfTextureCompiler.PackMetallicRoughnessPixels( new byte[4], factor, true ) );
		Assert.ThrowsException<GltfException>( () => GltfTextureCompiler.PackMetallicRoughnessPixels( new byte[4], factor, false ) );
	}

	[TestMethod]
	public void MetallicRoughnessPackingIsBoundedBeforeFloatAllocations()
	{
		Assert.ThrowsException<GltfException>( () => GltfTextureCompiler.CompileMetallicRoughnessPixels( new byte[8], 2, 1, 1, 1, true, 16 ) );
		Assert.ThrowsException<GltfException>( () => GltfTextureCompiler.CompileMetallicRoughnessPixels( new byte[8], 3, 1, 1, 1, true ) );
		Assert.ThrowsException<GltfException>( () => GltfTextureCompiler.CompileMetallicRoughnessPixels( new byte[8], 2, 1, float.NaN, 1, true ) );
	}

	[TestMethod]
	public void ConflictingBaseColorAndRoughnessSamplersFailBeforeImageDecode()
	{
		using var document = GltfReader.Read( Encoding.UTF8.GetBytes( """
			{"asset":{"version":"2.0"},"images":[{"uri":"data:image/png;base64,AA=="}],"samplers":[{"wrapS":10497},{"wrapS":33071}],"textures":[{"source":0,"sampler":0},{"source":0,"sampler":1}]}
			""" ) );
		var error = Assert.ThrowsException<GltfException>( () => GltfStockMaterialAdapter.Prepare( document, new GltfMaterial
		{
			BaseColorFactor = System.Numerics.Vector4.One, MetallicFactor = 1, RoughnessFactor = 1, AlphaMode = "OPAQUE",
			BaseColorTexture = new GltfTextureInfo { Index = 0 }, MetallicRoughnessTexture = new GltfTextureInfo { Index = 1 }
		} ) );
		StringAssert.Contains( error.Message, "sampler" );
	}
}
