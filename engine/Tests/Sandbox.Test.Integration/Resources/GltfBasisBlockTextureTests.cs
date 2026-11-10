using Sandbox.Gltf;
using Sandbox.Internal;
using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;

namespace ResourceTests;

/// <summary>Basis textures are written as BC7 (or BC5) instead of being expanded to RGBA on the CPU.</summary>
[TestClass]
public sealed class GltfBasisBlockTextureTests
{
	const byte Rgba8888 = 4, Bc7 = 20, Bc5 = 21;
	const ushort NoLod = 0x0008, LinearApi = 0x0200;

	static GltfImage Image( string name ) => new()
	{
		MimeType = "image/ktx2",
		Bytes = File.ReadAllBytes( Path.Combine( AppContext.BaseDirectory, "Fixtures", "Ktx", name ) )
	};

	// Same layout the existing compiled-texture tests read: data block header, then the streaming data.
	record Header( byte Format, byte Mips, ushort Flags, int Width, int Height, byte[] Streaming );

	static Header Read( byte[] bytes )
	{
		Assert.AreEqual( (ushort)12, BinaryPrimitives.ReadUInt16LittleEndian( bytes.AsSpan( 4 ) ) );
		var table = checked(8 + BinaryPrimitives.ReadInt32LittleEndian( bytes.AsSpan( 8 ) ));
		Assert.IsTrue( bytes.AsSpan( table, 4 ).SequenceEqual( "DATA"u8 ) );
		var block = checked(table + 4 + BinaryPrimitives.ReadInt32LittleEndian( bytes.AsSpan( table + 4 ) ));
		var streamingOffset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian( bytes ));
		return new( bytes[block + 26], bytes[block + 27], BinaryPrimitives.ReadUInt16LittleEndian( bytes.AsSpan( block + 2 ) ),
			BinaryPrimitives.ReadUInt16LittleEndian( bytes.AsSpan( block + 20 ) ), BinaryPrimitives.ReadUInt16LittleEndian( bytes.AsSpan( block + 22 ) ),
			bytes.AsSpan( streamingOffset ).ToArray() );
	}

	/// <summary>The same image tagged as linear data instead of sRGB colour. Only the descriptor changes; the fixtures are all sRGB.</summary>
	static GltfImage AsLinear( string name )
	{
		var image = Image( name );
		var dfd = (int)BinaryPrimitives.ReadUInt32LittleEndian( image.Bytes.AsSpan( 48 ) );
		image.Bytes[dfd + 14] = 1;
		return image;
	}

	[TestMethod]
	[DataRow( true )]
	[DataRow( false )]
	public void FullMipChainIsWrittenAsBc7SmallestMipFirst( bool srgb )
	{
		var image = srgb ? Image( "gltf-ktx-rgba-mipmap-reference-basis.ktx2" ) : AsLinear( "gltf-ktx-rgba-mipmap-reference-basis.ktx2" );
		var header = Read( GltfTextureCompiler.CompileWithMipmaps( image, srgb ) );
		Assert.AreEqual( Bc7, header.Format );
		Assert.AreEqual( 7, header.Mips );
		Assert.AreEqual( 64, header.Width );
		Assert.AreEqual( 0, header.Flags & NoLod, "A power-of-two chain streams." );
		Assert.AreEqual( !srgb, (header.Flags & LinearApi) != 0 );
		// 64, 32, 16, 8, 4, 2, 1: the last three mips are still one 16 byte block each.
		var expected = new[] { 64, 32, 16, 8, 4, 2, 1 }.Sum( size => GltfKtx.BlockLevelBytes( size, size ) );
		Assert.AreEqual( expected, header.Streaming.Length );
		// 4096 bytes of the base mip come last.
		Assert.AreEqual( 4096, GltfKtx.BlockLevelBytes( 64, 64 ) );
	}

	[TestMethod]
	public void BaseMipOnlyIsOneBc7LevelWithoutStreaming()
	{
		var header = Read( GltfTextureCompiler.Compile( Image( "gltf-ktx-cyan_rgb_reference_basis.ktx2" ), true ) );
		Assert.AreEqual( Bc7, header.Format );
		Assert.AreEqual( 1, header.Mips );
		Assert.AreNotEqual( 0, header.Flags & NoLod );
		Assert.AreEqual( 4096, header.Streaming.Length );
	}

	[TestMethod]
	public void ImagesThatBlocksCannotHoldKeepTheExactRgbaPath()
	{
		// 8x8: smaller than 16 pixels, so it would waste most of a block.
		var small = Read( GltfTextureCompiler.CompileWithMipmaps( Image( "gltf-ktx-alpha-simple.ktx2" ), true ) );
		Assert.AreEqual( Rgba8888, small.Format );
		// Mips are requested but the file has only one: the tail is generated from pixels.
		var generated = Read( GltfTextureCompiler.CompileWithMipmaps( Image( "gltf-ktx-cyan_rgb_reference_basis.ktx2" ), true ) );
		Assert.AreEqual( Rgba8888, generated.Format );
		Assert.AreEqual( 7, generated.Mips );
	}

	// The engine's native converter cannot decode BC7, and writing a full BC7 decoder here would test the decoder more than
	// the code. The block layout (order, mip offsets, orientation) is the same for BC5 and BC7, and BC5 is simple enough to
	// decode in a few lines, so the texel comparison below uses BC5. The BC7 payload itself comes from libktx's transcoder.
	[TestMethod]
	public void Bc7BlocksAreWellFormedAndUniformImagesGiveUniformBlocks()
	{
		var flat = Read( GltfTextureCompiler.Compile( Image( "gltf-ktx-cyan_rgb_reference_uastc.ktx2" ), true ) );
		Assert.AreEqual( Bc7, flat.Format );
		var blocks = flat.Streaming.Chunk( 16 ).ToArray();
		Assert.AreEqual( 16 * 16, blocks.Length );
		// Every block names one of the eight BC7 modes: the lowest set bit of the first byte. Zero is not a mode.
		Assert.IsTrue( blocks.All( block => block[0] != 0 ), "A BC7 block with no mode bit decodes as transparent black." );
		// A flat colour image transcodes to identical blocks.
		Assert.AreEqual( 1, blocks.Select( block => Convert.ToHexString( block ) ).Distinct().Count() );

		var detailed = Read( GltfTextureCompiler.CompileWithMipmaps( Image( "gltf-ktx-rgba-mipmap-reference-basis.ktx2" ), true ) );
		var all = detailed.Streaming.Chunk( 16 ).ToArray();
		Assert.IsTrue( all.All( block => block[0] != 0 ) );
		Assert.IsTrue( all.Select( block => Convert.ToHexString( block ) ).Distinct().Count() > 4, "The mip fixture is not flat." );
	}

	[TestMethod]
	public void Bc5HoldsTheRedAndAlphaChannelsOfTheSource()
	{
		var plan = GltfBasisTexturePlan.Create( AsLinear( "gltf-ktx-rgba-mipmap-reference-basis.ktx2" ), false, true );
		var header = Read( plan.Compile( GltfBasisTargetCodec.Bc5 ) );
		Assert.AreEqual( Bc5, header.Format );
		Assert.AreEqual( 7, header.Mips );
		var reference = GltfKtx.DecodeRgba32( AsLinear( "gltf-ktx-rgba-mipmap-reference-basis.ktx2" ).Bytes );
		// Streaming data holds the smallest mip first; walk up from the end to the base, then each smaller one.
		var end = header.Streaming.Length;
		for ( var level = 0; level < 7; level++ )
		{
			var size = 64 >> level;
			var bytes = (int)GltfKtx.BlockLevelBytes( size, size );
			var decoded = DecodeBc5( header.Streaming.AsSpan( end - bytes, bytes ), size, size );
			end -= bytes;
			var pixels = reference.Levels[level].Pixels;
			double sum = 0;
			// Basis stores a second data channel as the alpha slice, so BC5 gets (red, alpha) of the source, not (red, green).
			for ( var i = 0; i < size * size; i++ ) sum += Math.Abs( decoded[i * 2] - pixels[i * 4] ) + Math.Abs( decoded[i * 2 + 1] - pixels[i * 4 + 3] );
			var error = sum / (size * size * 2);
			// BC5 is lossy; at 1x1 and 2x2 a single block must hold the average.
			Assert.IsTrue( error < 6.0, $"mip {level} ({size}x{size}): BC5 red and alpha differ from the RGBA decode by {error:0.00} levels on average." );
		}
		Assert.AreEqual( 0, end );
	}

	[TestMethod]
	public void Bc5RefusesColourAndFallsBackWhenBlocksDoNotFit()
	{
		var colour = GltfBasisTexturePlan.Create( Image( "gltf-ktx-rgba-mipmap-reference-basis.ktx2" ), true, true );
		Assert.ThrowsException<GltfException>( () => colour.Compile( GltfBasisTargetCodec.Bc5 ) );
		var tiny = GltfBasisTexturePlan.Create( AsLinear( "gltf-ktx-alpha-simple.ktx2" ), false, true );
		Assert.AreEqual( GltfBasisTargetCodec.Rgba32, tiny.TargetCodec );
		Assert.AreEqual( Rgba8888, Read( tiny.Compile( GltfBasisTargetCodec.Bc7 ) ).Format );
	}

	[TestMethod]
	public void BlockOutputIsDeterministicAndFarSmallerThanRgba()
	{
		var image = Image( "gltf-ktx-rgba-mipmap-reference-basis.ktx2" );
		var first = GltfTextureCompiler.CompileWithMipmaps( image, true );
		CollectionAssert.AreEqual( first, GltfTextureCompiler.CompileWithMipmaps( image, true ) );
		var plan = GltfBasisTexturePlan.Create( image, true, true );
		Assert.IsTrue( Read( first ).Streaming.Length * 3 < plan.OutputRgbaBytes * 2 );
	}

	[TestMethod]
	public void Bc7ResourceLoadsThroughTheNativePathWithItsMipChain()
	{
		if ( Environment.GetEnvironmentVariable( "SBOX_TEST_GRAPHICS" ) != "1" || !Sandbox.Graphics.IsAvailable )
			Assert.Inconclusive( "Requires an authorized graphics-enabled test session." );
		var root = Path.Combine( Path.GetTempPath(), "gltf-bc7-" + Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( root );
		var path = Path.Combine( root, "image.vtex_c" );
		var virtualBase = "gltf-bc7-test/" + Guid.NewGuid().ToString( "N" );
		var link = "/" + virtualBase + ".vtex_c";
		var redirect = RedirectFileSystem.Create( root );
		Texture texture = null;
		try
		{
			File.WriteAllBytes( path, GltfTextureCompiler.CompileWithMipmaps( Image( "gltf-ktx-rgba-mipmap-reference-basis.ktx2" ), true ) );
			redirect.AddAbsFile( link, path );
			NativeEngine.g_pResourceSystem.InvalidateDatabase();
			texture = Texture.Load( virtualBase + ".vtex", false );
			Assert.IsTrue( texture is { IsValid: true, IsError: false }, "The native loader must accept the BC7 resource." );
			Assert.AreEqual( 64, texture.Width );
			Assert.AreEqual( 64, texture.Height );
			Assert.AreEqual( 7, texture.Mips );
			Assert.AreEqual( ImageFormat.BC7, texture.ImageFormat );
		}
		finally
		{
			texture?.Dispose();
			redirect.RemoveAbsFile( link );
			redirect.Dispose();
			File.Delete( path );
			Directory.Delete( root );
		}
	}

	/// <summary>BC5 is two BC4 blocks (red, green): two 8-bit end points and sixteen 3-bit indices each. Returns tightly packed red, green pairs.</summary>
	static byte[] DecodeBc5( ReadOnlySpan<byte> blocks, int width, int height )
	{
		var blocksX = (width + 3) / 4;
		var result = new byte[width * height * 2];
		for ( var by = 0; by < (height + 3) / 4; by++ )
			for ( var bx = 0; bx < blocksX; bx++ )
				for ( var channel = 0; channel < 2; channel++ )
				{
					var block = blocks.Slice( (by * blocksX + bx) * 16 + channel * 8, 8 );
					int e0 = block[0], e1 = block[1];
					var palette = new int[8]; palette[0] = e0; palette[1] = e1;
					if ( e0 > e1 ) for ( var i = 1; i < 7; i++ ) palette[i + 1] = ((7 - i) * e0 + i * e1) / 7;
					else { for ( var i = 1; i < 5; i++ ) palette[i + 1] = ((5 - i) * e0 + i * e1) / 5; palette[6] = 0; palette[7] = 255; }
					ulong bits = 0;
					for ( var i = 0; i < 6; i++ ) bits |= (ulong)block[2 + i] << (8 * i);
					for ( var texel = 0; texel < 16; texel++ )
					{
						var x = bx * 4 + texel % 4; var y = by * 4 + texel / 4;
						if ( x >= width || y >= height ) continue;
						result[(y * width + x) * 2 + channel] = (byte)palette[(int)((bits >> (3 * texel)) & 7)];
					}
				}
		return result;
	}
}
