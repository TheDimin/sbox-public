using Sandbox.Gltf;
using Sandbox.Internal;
using System;
using System.Buffers.Binary;
using System.IO;

namespace EngineTests;

[TestClass]
public class GltfCompiledTextureTests
{
	// Self-generated 2x2 RGBA PNG; the partially transparent pixels have straight RGB.
	private const string Png = "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAGklEQVR4nGP4UKHQIHAi4D+Dw4IPDgwMDAwARTgGQCHDUy8AAAAASUVORK5CYII=";
	// Self-generated 2x2 JPEG from uniform (40,120,200), quality 95, no chroma subsampling.
	private const string Jpeg = "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAIBAQEBAQIBAQECAgICAgQDAgICAgUEBAMEBgUGBgYFBgYGBwkIBgcJBwYGCAsICQoKCgoKBggLDAsKDAkKCgr/2wBDAQICAgICAgUDAwUKBwYHCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgr/wAARCAACAAIDAREAAhEBAxEB/8QAFAABAAAAAAAAAAAAAAAAAAAAB//EABQQAQAAAAAAAAAAAAAAAAAAAAD/xAAUAQEAAAAAAAAAAAAAAAAAAAAI/8QAFBEBAAAAAAAAAAAAAAAAAAAAAP/aAAwDAQACEQMRAD8AIzUEN//Z";
	private static readonly byte[] Pixels = [240,120,32,128, 16,200,80,255, 64,160,240,64, 0,0,0,0];
	private static GltfImage Image() => new() { MimeType = "image/png", Bytes = Convert.FromBase64String( Png ) };

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void CompilesLosslessRgbaDataAndColorRole( bool isSrgb )
	{
		var first = GltfTextureCompiler.Compile( Image(), isSrgb );
		var second = GltfTextureCompiler.Compile( Image(), isSrgb );
		CollectionAssert.AreEqual( first, second, "Identical inputs should yield deterministic compiled resources." );
		var block = DataOffset( first );
		Assert.AreEqual( (ushort)1, BinaryPrimitives.ReadUInt16LittleEndian( first.AsSpan( block ) ) );
		var flags = BinaryPrimitives.ReadUInt16LittleEndian( first.AsSpan( block + 2 ) );
		Assert.AreEqual( !isSrgb, (flags & 0x0200) != 0, "Linear API creation flag must reflect the image's role." );
		Assert.AreEqual( (ushort)2, BinaryPrimitives.ReadUInt16LittleEndian( first.AsSpan( block + 20 ) ) );
		Assert.AreEqual( (ushort)2, BinaryPrimitives.ReadUInt16LittleEndian( first.AsSpan( block + 22 ) ) );
		Assert.AreEqual( (ushort)1, BinaryPrimitives.ReadUInt16LittleEndian( first.AsSpan( block + 24 ) ) );
		Assert.AreEqual( (byte)4, first[block + 26], "Expected native uncompressed RGBA8888." );
		Assert.AreEqual( (byte)1, first[block + 27], "This slice writes the supplied base level only." );
		var streamingOffset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian( first ));
		CollectionAssert.AreEqual( Pixels, first.AsSpan( streamingOffset ).ToArray(), "RGBA and alpha must remain straight and lossless." );
	}

	[TestMethod]
	public void RejectsInvalidImagesAndExpansionBudget()
	{
		Assert.ThrowsException<GltfException>( () => GltfTextureCompiler.Compile( new GltfImage { MimeType = "image/png", Bytes = [1, 2, 3] }, true ) );
		Assert.ThrowsException<GltfException>( () => GltfTextureCompiler.Compile( new GltfImage { MimeType = "image/ktx2", Bytes = Image().Bytes }, true ) );
		Assert.ThrowsException<GltfException>( () => GltfTextureCompiler.Compile( new GltfImage { MimeType = "image/jpeg", Bytes = Image().Bytes }, true ) );
		Assert.ThrowsException<GltfException>( () => GltfTextureCompiler.Compile( new GltfImage { MimeType = "image/jpeg", Bytes = Convert.FromBase64String( Jpeg )[..100] }, true ) );
		// Valid self-generated 16-bit-per-channel PNG; this RGBA8 slice must not silently quantize it.
		var highPrecisionPng = Convert.FromBase64String( "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABEAYAAABPhRjKAAAAEUlEQVR4nGMQMgmrmLXn3gcADakEOVMAyboAAAAASUVORK5CYII=" );
		Assert.ThrowsException<GltfException>( () => GltfTextureCompiler.Compile( new GltfImage { MimeType = "image/png", Bytes = highPrecisionPng }, true ) );
		Assert.ThrowsException<GltfException>( () => GltfTextureCompiler.Compile( Image(), true, maxDecodedBytes: 15 ) );
		Assert.ThrowsException<GltfException>( () => GltfTextureCompiler.Compile( Image(), true, maxDecodedBytes: 0 ) );
	}

	[TestMethod]
	public void CompilesJpegWithoutFurtherPixelLoss()
	{
		var input = Convert.FromBase64String( Jpeg );
		using var reference = Bitmap.CreateFromBytes( input );
		Assert.IsNotNull( reference );
		var resource = GltfTextureCompiler.Compile( new GltfImage { MimeType = "image/jpeg", Bytes = input }, true );
		var streaming = checked((int)BinaryPrimitives.ReadUInt32LittleEndian( resource ));
		var pixels = resource.AsSpan( streaming ).ToArray();
		CollectionAssert.AreEqual( reference.GetBuffer().ToArray(), pixels, "Compilation must not change decoded JPEG pixels." );
		Assert.AreEqual( 16, pixels.Length );
		for ( var offset = 0; offset < pixels.Length; offset += 4 )
		{
			// The JPEG itself is lossy; the independently known source color allows small encoder error.
			Assert.IsTrue( Math.Abs( pixels[offset] - 40 ) <= 3 );
			Assert.IsTrue( Math.Abs( pixels[offset + 1] - 120 ) <= 3 );
			Assert.IsTrue( Math.Abs( pixels[offset + 2] - 200 ) <= 3 );
			Assert.AreEqual( (byte)255, pixels[offset + 3] );
		}
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void CompiledTextureLoadsThroughNativeResourcePathAndReadsBack( bool isSrgb )
	{
		if ( Environment.GetEnvironmentVariable( "SBOX_TEST_GRAPHICS" ) != "1" || !Graphics.IsAvailable )
			Assert.Inconclusive( "Requires an authorized graphics-enabled test session." );
		Assert.IsTrue( ThreadSafe.IsMainThread );
		var root = Path.Combine( Path.GetTempPath(), "gltf-vtex-" + Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( root );
		var path = Path.Combine( root, "image.vtex_c" );
		var virtualBase = "gltf-vtex-test/" + Guid.NewGuid().ToString( "N" );
		var link = "/" + virtualBase + ".vtex_c";
		var redirect = RedirectFileSystem.Create( root );
		Texture texture = null;
		try
		{
			File.WriteAllBytes( path, GltfTextureCompiler.Compile( Image(), isSrgb ) );
			redirect.AddAbsFile( link, path );
			NativeEngine.g_pResourceSystem.InvalidateDatabase();
			texture = Texture.Load( virtualBase + ".vtex", false );
			Assert.IsNotNull( texture );
			Assert.IsTrue( texture.IsValid && !texture.IsError, "The native loader must accept the generated resource." );
			Assert.AreEqual( 2, texture.Width );
			Assert.AreEqual( 2, texture.Height );
			var actual = new byte[Pixels.Length];
			texture.GetPixels( (0, 0, 2, 2), 0, 0, actual.AsSpan(), ImageFormat.RGBA8888 );
			CollectionAssert.AreEqual( Pixels, actual, "GPU readback must preserve straight-alpha RGBA bytes." );
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

	private static int DataOffset( byte[] bytes )
	{
		Assert.IsTrue( bytes.Length >= 28 );
		Assert.AreEqual( (ushort)12, BinaryPrimitives.ReadUInt16LittleEndian( bytes.AsSpan( 4 ) ) );
		Assert.AreEqual( (ushort)1, BinaryPrimitives.ReadUInt16LittleEndian( bytes.AsSpan( 6 ) ) );
		var table = checked(8 + BinaryPrimitives.ReadInt32LittleEndian( bytes.AsSpan( 8 ) ));
		var count = BinaryPrimitives.ReadUInt32LittleEndian( bytes.AsSpan( 12 ) );
		Assert.AreEqual( 1u, count );
		Assert.IsTrue( bytes.AsSpan( table, 4 ).SequenceEqual( "DATA"u8 ) );
		var start = checked(table + 4 + BinaryPrimitives.ReadInt32LittleEndian( bytes.AsSpan( table + 4 ) ));
		var size = BinaryPrimitives.ReadUInt32LittleEndian( bytes.AsSpan( table + 8 ) );
		var end = BinaryPrimitives.ReadUInt32LittleEndian( bytes );
		Assert.IsTrue( size >= 32 && start >= 0 && (long)start + size <= end && end <= bytes.Length );
		return start;
	}
}
