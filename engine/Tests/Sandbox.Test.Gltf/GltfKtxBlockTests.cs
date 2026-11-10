using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;

namespace Sandbox.Tests.Gltf;

[TestClass]
public class GltfKtxBlockTests
{
	static byte[] Fixture( string suffix )
	{
		var assembly = typeof( GltfKtxBlockTests ).Assembly;
		using var input = assembly.GetManifestResourceStream( assembly.GetManifestResourceNames().Single( x => x.EndsWith( suffix, StringComparison.Ordinal ) ) )!;
		using var output = new MemoryStream(); input.CopyTo( output ); return output.ToArray();
	}

	[TestMethod]
	[DataRow( "gltf-ktx-cyan_rgb_reference_basis.ktx2", GltfKtxBlockFormat.Bc7, 1 )]
	[DataRow( "gltf-ktx-cyan_rgb_reference_uastc.ktx2", GltfKtxBlockFormat.Bc7, 1 )]
	[DataRow( "gltf-ktx-rgba-mipmap-reference-basis.ktx2", GltfKtxBlockFormat.Bc7, 7 )]
	[DataRow( "gltf-ktx-alpha-simple.ktx2", GltfKtxBlockFormat.Bc7, 1 )]
	[DataRow( "gltf-ktx-cyan_rgb_reference_basis.ktx2", GltfKtxBlockFormat.Bc5, 1 )]
	[DataRow( "gltf-ktx-rgba-mipmap-reference-basis.ktx2", GltfKtxBlockFormat.Bc5, 7 )]
	public void BlockLevelsKeepTheMipChainAndExactBlockSizes( string name, GltfKtxBlockFormat format, int levels )
	{
		var image = GltfKtx.TranscodeToBlocks( Fixture( name ), format );
		var rgba = GltfKtx.DecodeRgba32( Fixture( name ) );
		Assert.AreEqual( levels, image.Levels.Count );
		Assert.AreEqual( rgba.Width, image.Width );
		Assert.AreEqual( rgba.Height, image.Height );
		Assert.AreEqual( rgba.IsSrgb, image.IsSrgb );
		for ( var i = 0; i < levels; i++ )
		{
			Assert.AreEqual( rgba.Levels[i].Width, image.Levels[i].Width );
			Assert.AreEqual( rgba.Levels[i].Height, image.Levels[i].Height );
			// A 1x1 mip is still one whole 16 byte block.
			Assert.AreEqual( GltfKtx.BlockLevelBytes( image.Levels[i].Width, image.Levels[i].Height ), image.Levels[i].Data.Length );
		}
		if ( levels == 7 ) Assert.AreEqual( 16, image.Levels[^1].Data.Length ); // the 1x1 mip
	}

	[TestMethod]
	public void BlockDataIsFarSmallerThanExpandedRgba()
	{
		var bytes = Fixture( "gltf-ktx-rgba-mipmap-reference-basis.ktx2" );
		var blocks = GltfKtx.TranscodeToBlocks( bytes, GltfKtxBlockFormat.Bc7 ).Levels.Sum( x => (long)x.Data.Length );
		var rgba = GltfKtx.DecodeRgba32( bytes ).Levels.Sum( x => (long)x.Pixels.Length );
		Assert.IsTrue( blocks * 3 < rgba * 2, $"BC7 {blocks} bytes against RGBA32 {rgba} bytes." );
	}

	[TestMethod]
	public void BlockTranscodeHonoursItsByteBudget()
	{
		Assert.ThrowsException<GltfException>( () => GltfKtx.TranscodeToBlocks( Fixture( "gltf-ktx-cyan_rgb_reference_basis.ktx2" ), GltfKtxBlockFormat.Bc7, 8 ) );
		Assert.ThrowsException<GltfException>( () => GltfKtx.TranscodeToBlocks( new byte[40], GltfKtxBlockFormat.Bc7 ) );
		Assert.ThrowsException<ArgumentNullException>( () => GltfKtx.TranscodeToBlocks( null, GltfKtxBlockFormat.Bc7 ) );
	}

	[TestMethod]
	public void RgbaDecodeIsUnchangedByTheSharedCore()
	{
		var image = GltfKtx.DecodeRgba32( Fixture( "gltf-ktx-cyan_rgb_reference_basis.ktx2" ) );
		Assert.AreEqual( 64 * 64 * 4, image.Levels[0].Pixels.Length );
		// The reference image is flat cyan. ETC1S is lossy, so allow a small error.
		Assert.IsTrue( image.Levels[0].Pixels[0] <= 8 );
		Assert.IsTrue( image.Levels[0].Pixels[1] >= 247 );
		Assert.IsTrue( image.Levels[0].Pixels[2] >= 247 );
	}
}
