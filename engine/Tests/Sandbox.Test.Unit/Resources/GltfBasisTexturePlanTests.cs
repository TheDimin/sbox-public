using System;
using System.Buffers.Binary;
using System.Linq;
using System.Text;
using Sandbox.Gltf;

namespace Sandbox.Tests;

/// <summary>Independent synthetic container/texel controls; no native decoder is called.</summary>
[TestClass]
public sealed class GltfBasisTexturePlanTests
{
	[TestMethod]
	[DataRow( 163, 1, "Etc1S" )]
	[DataRow( 166, 0, "Uastc" )]
	[DataRow( 166, 2, "Uastc" )]
	public void SelectsSourceCodecFromDfdAndPortableRgbaTarget( int model, int compression, string codec )
	{
		var source = Container( model, compression );
		var plan = GltfBasisTexturePlan.Create( Image( source ), true, true );
		Assert.AreEqual( codec, plan.SourceCodec.ToString() );
		Assert.AreEqual( "Rgba32", plan.TargetCodec.ToString() );
		Assert.AreEqual( 3, plan.OutputLevelCount );
		Assert.AreEqual( 1, plan.Levels.Count );
		Assert.AreEqual( "rd", plan.Orientation );
		Assert.AreEqual( "rgba", plan.Swizzle );
		Assert.AreEqual( 84L, plan.OutputRgbaBytes );
		Assert.AreEqual( 1, plan.ColorPrimaries );
		Assert.AreEqual( 2, plan.TransferFunction );
		Array.Clear( source );
		Assert.AreEqual( 0xab, plan.EncodedSource.Span[0] );
		Assert.AreEqual( 166 == model ? 166 : 163, plan.DataFormatDescriptor.Span[12] );
	}

	[TestMethod]
	public void AuthoredMipLevelsStayInTheirDeclaredOrderAndMissingTailIsGenerated()
	{
		var plan = GltfBasisTexturePlan.Create( Image( Container( levels: 2 ) ), true, true );
		var decoded = new GltfKtxImage
		{
			Width = 4,
			Height = 4,
			IsSrgb = true,
			Levels = [Level( 4, [255, 0, 0, 255] ), Level( 2, [0, 255, 0, 255] )]
		};
		var levels = plan.CompleteLevels( decoded );
		Assert.AreEqual( 3, levels.Count );
		CollectionAssert.AreEqual( new byte[] { 255, 0, 0, 255 }, levels[0].Pixels[..4] );
		CollectionAssert.AreEqual( new byte[] { 0, 255, 0, 255 }, levels[1].Pixels[..4] );
		CollectionAssert.AreEqual( new byte[] { 0, 255, 0, 255 }, levels[2].Pixels );
	}

	[TestMethod]
	public void MissingMipsUseLinearLightForColorAndLinearAlpha()
	{
		var plan = GltfBasisTexturePlan.Create( Image( Container() ), true, true );
		var pixels = Enumerable.Range( 0, 16 ).SelectMany( x => x % 2 == 0 ? new byte[] { 0, 0, 0, 0 } : new byte[] { 255, 255, 255, 255 } ).ToArray();
		var levels = plan.CompleteLevels( new GltfKtxImage { Width = 4, Height = 4, IsSrgb = true, Levels = [new GltfKtxLevel { Width = 4, Height = 4, Pixels = pixels }] } );
		CollectionAssert.AreEqual( new byte[] { 188, 188, 188, 128 }, levels[1].Pixels[..4] );
	}

	[TestMethod]
	public void BaseMipOnlyRetainsSourceMipDescriptorWithoutGeneratingMips()
	{
		var plan = GltfBasisTexturePlan.Create( Image( Container( levels: 2 ) ), true, false );
		Assert.AreEqual( 2, plan.Levels.Count );
		Assert.AreEqual( 1, plan.OutputLevelCount );
		Assert.AreEqual( 64L, plan.OutputRgbaBytes );
	}

	[TestMethod]
	public void LinearDataKeepsHistoricalPrimariesMetadataWithoutAColorConversion()
	{
		var source = Container(); source[118] = 1;
		var plan = GltfBasisTexturePlan.Create( Image( source ), false, false );
		Assert.AreEqual( 1, plan.ColorPrimaries );
		Assert.IsTrue( plan.HasNonColorPrimaries );
		Assert.IsFalse( plan.IsSrgb );
		Assert.AreEqual( 1, plan.DataFormatDescriptor.Span[14] );
	}

	[TestMethod]
	[DataRow( 163, 0 )]
	[DataRow( 163, 2 )]
	[DataRow( 166, 1 )]
	[DataRow( 255, 0 )]
	public void RejectsInvalidSourceCodecAndSupercompressionPairs( int model, int compression )
		=> Assert.ThrowsException<GltfException>( () => GltfBasisTexturePlan.Create( Image( Container( model, compression ) ), true, true ) );

	[TestMethod]
	[DataRow( "KTXorientation", "ru" )]
	[DataRow( "KTXswizzle", "bgra" )]
	public void RejectsMetadataThatWouldRequireAnUnstatedFlipOrSwizzle( string key, string value )
		=> Assert.ThrowsException<GltfException>( () => GltfBasisTexturePlan.Create( Image( Container( key: key, value: value ) ), true, false ) );

	[TestMethod]
	public void RejectsTransferMismatchPremultiplicationAndInvalidBoundsBeforeDecode()
	{
		var original = Container();
		foreach ( var offset in new[] { 118, 119, 20, 80, 48 } )
		{
			var source = (byte[])original.Clone(); source[offset] = 255;
			Assert.ThrowsException<GltfException>( () => GltfBasisTexturePlan.Create( Image( source ), true, true ), "offset " + offset );
		}
		Assert.ThrowsException<GltfException>( () => GltfBasisTexturePlan.Create( Image( original ), false, false ) );
		Assert.ThrowsException<GltfException>( () => GltfBasisTexturePlan.Create( Image( original ), true, true, 83 ) );
	}

	[TestMethod]
	public void RejectsDecodedMetadataOrMipShapeDisagreement()
	{
		var plan = GltfBasisTexturePlan.Create( Image( Container() ), true, true );
		foreach ( var image in new[]
		{
			new GltfKtxImage { Width = 4, Height = 4, IsSrgb = false, Levels = [Level( 4, [0, 0, 0, 255] )] },
			new GltfKtxImage { Width = 4, Height = 4, IsSrgb = true, Levels = [Level( 2, [0, 0, 0, 255] )] }
		} ) Assert.ThrowsException<GltfException>( () => plan.CompleteLevels( image ) );
	}

	private static GltfImage Image( byte[] source ) => new() { MimeType = "image/ktx2", Bytes = source };
	private static GltfKtxLevel Level( int size, byte[] color ) => new() { Width = size, Height = size, Pixels = Enumerable.Range( 0, size * size ).SelectMany( _ => color ).ToArray() };

	// Synthetic container bytes are a planning fixture, not purported encoded image data.
	private static byte[] Container( int model = 166, int compression = 0, int levels = 1, string key = null, string value = null )
	{
		var indexEnd = 80 + levels * 24; var dfd = indexEnd; var kvd = dfd + 44;
		var pair = key is null ? [] : Encoding.UTF8.GetBytes( key + "\0" + value + "\0" );
		var kvdLength = pair.Length == 0 ? 0 : 4 + ((pair.Length + 3) & ~3);
		var payload = kvd + kvdLength; var source = new byte[payload + levels * 16];
		new byte[] { 0xab, 0x4b, 0x54, 0x58, 0x20, 0x32, 0x30, 0xbb, 13, 10, 26, 10 }.CopyTo( source, 0 );
		void U32( int offset, uint x ) => BinaryPrimitives.WriteUInt32LittleEndian( source.AsSpan( offset ), x );
		void U64( int offset, ulong x ) => BinaryPrimitives.WriteUInt64LittleEndian( source.AsSpan( offset ), x );
		U32( 16, 1 ); U32( 20, 4 ); U32( 24, 4 ); U32( 36, 1 ); U32( 40, (uint)levels ); U32( 44, (uint)compression );
		U32( 48, (uint)dfd ); U32( 52, 44 ); U32( 56, kvdLength == 0 ? 0 : (uint)kvd ); U32( 60, (uint)kvdLength );
		U32( dfd, 44 ); source[dfd + 8] = 2; source[dfd + 10] = 40; source[dfd + 12] = (byte)model; source[dfd + 13] = 1; source[dfd + 14] = 2;
		source[dfd + 16] = source[dfd + 17] = 3; source[dfd + 30] = 127;
		for ( var level = 0; level < levels; level++ ) { U64( 80 + level * 24, (ulong)(payload + level * 16) ); U64( 88 + level * 24, 16 ); U64( 96 + level * 24, compression == 1 ? 0UL : 16UL ); }
		if ( pair.Length > 0 ) { U32( kvd, (uint)pair.Length ); pair.CopyTo( source, kvd + 4 ); }
		return source;
	}
}
