using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;

namespace Sandbox.Tests.Gltf;

// Khronos KTX-Software fixtures at 1f63a3ee42bd46f05168850f1a8a8d33e8a92528.
// Upstream testimages/genref ties the mip images to independently authored source
// colors and the alpha fixture to alpha_simple.png. Lossy tolerance is intentional.
// Full provenance and license: Fixtures/Ktx/README.md and Apache-2.0.txt.
[TestClass]
public sealed class GltfKtxTests
{
	private static byte[] Fixture( string suffix )
	{
		var assembly = typeof( GltfKtxTests ).Assembly;
		var name = assembly.GetManifestResourceNames().Single( x => x.EndsWith( suffix, StringComparison.Ordinal ) );
		using var stream = assembly.GetManifestResourceStream( name )!;
		using var memory = new MemoryStream();
		stream.CopyTo( memory );
		return memory.ToArray();
	}

	private static void NearPixels( GltfKtxLevel level, byte[] expected, int tolerance )
	{
		Assert.AreEqual( level.Width * level.Height * 4, level.Pixels.Length );
		for ( var offset = 0; offset < level.Pixels.Length; offset += 4 )
			for ( var channel = 0; channel < 4; channel++ )
				Assert.IsTrue( Math.Abs( level.Pixels[offset + channel] - expected[channel] ) <= tolerance,
					$"pixel {offset / 4}, channel {channel}, got {level.Pixels[offset + channel]}, expected near {expected[channel]}" );
	}

	[DataTestMethod]
	[DataRow( "gltf-ktx-cyan_rgb_reference_basis.ktx2" )]
	[DataRow( "gltf-ktx-cyan_rgb_reference_uastc.ktx2" )]
	public void TranscodesKhronosEtc1sAndUastcColor( string file )
	{
		var input = Fixture( file );
		var image = GltfKtx.DecodeRgba32( input );
		Array.Clear( input ); // Output must survive input mutation and native decoder disposal.
		Assert.AreEqual( 64, image.Width );
		Assert.AreEqual( 64, image.Height );
		Assert.IsTrue( image.IsSrgb );
		Assert.AreEqual( 1, image.Levels.Count );
		NearPixels( image.Levels[0], [0, 255, 255, 255], 3 );
	}

	[TestMethod]
	public void TranscodesIndependentReferenceMipColorsInOrder()
	{
		var image = GltfKtx.DecodeRgba32( Fixture( "gltf-ktx-rgba-mipmap-reference-basis.ktx2" ) );
		byte[][] colors = [[255,0,0,255], [255,102,0,255], [255,255,0,255], [0,255,0,255], [0,0,255,255], [0,255,255,255], [255,0,255,255]];
		Assert.AreEqual( colors.Length, image.Levels.Count );
		for ( var i = 0; i < colors.Length; i++ )
		{
			Assert.AreEqual( Math.Max( 1, 64 >> i ), image.Levels[i].Width );
			Assert.AreEqual( Math.Max( 1, 64 >> i ), image.Levels[i].Height );
			NearPixels( image.Levels[i], colors[i], 16 );
		}
	}

	[TestMethod]
	public void PreservesTranslucentAlpha()
	{
		var image = GltfKtx.DecodeRgba32( Fixture( "gltf-ktx-alpha-simple.ktx2" ) );
		Assert.AreEqual( 8, image.Width );
		Assert.AreEqual( 8, image.Height );
		NearPixels( image.Levels[0], [170, 187, 204, 128], 3 );
	}

	[TestMethod]
	public void PreservesLinearTransferMetadata()
	{
		var input = Fixture( "gltf-ktx-cyan_rgb_reference_uastc.ktx2" );
		var dfd = (int)BinaryPrimitives.ReadUInt32LittleEndian( input.AsSpan( 48 ) );
		input[dfd + 13] = 0; // unspecified primaries
		input[dfd + 14] = 1; // linear transfer
		Assert.IsFalse( GltfKtx.DecodeRgba32( input ).IsSrgb );
	}

	[TestMethod]
	public void SelectedGltfBasisTextureTranscodesItsReferencedImage()
	{
		var source = Fixture( "gltf-ktx-alpha-simple.ktx2" );
		var json = """
			{"asset":{"version":"2.0"},"extensionsUsed":["KHR_texture_basisu"],
			 "extensionsRequired":["KHR_texture_basisu"],"images":[{"uri":"alpha.ktx2","mimeType":"image/ktx2"}],
			 "textures":[{"extensions":{"KHR_texture_basisu":{"source":0}}}]}
			""";
		using var document = GltfReader.Read( Encoding.UTF8.GetBytes( json ), uri => uri == "alpha.ktx2" ? source : throw new InvalidOperationException(),
			new HashSet<string> { "KHR_texture_basisu" } );
		var texture = document.ReadTexture( 0 );
		Assert.AreEqual( 0, texture.Image );
		var image = document.ReadImage( texture.Image.Value );
		Assert.AreEqual( "image/ktx2", image.MimeType );
		NearPixels( GltfKtx.DecodeRgba32( image.Bytes ).Levels[0], [170, 187, 204, 128], 3 );
	}

	[DataTestMethod]
	[DataRow( "gltf-ktx-cyan_rgb_reference_basis.ktx2", false )]
	[DataRow( "gltf-ktx-cyan_rgb_reference_basis.ktx2", true )]
	[DataRow( "gltf-ktx-cyan_rgb_reference_uastc.ktx2", false )]
	[DataRow( "gltf-ktx-cyan_rgb_reference_uastc.ktx2", true )]
	public void RequiredBasisTextureResolvesDataUriOrBufferViewAndDecodes( string file, bool bufferView )
	{
		var input = Fixture( file );
		var root = BasisFixture( input, bufferView, required: true );
		using var document = GltfReader.Read( root, supportedExtensions: new HashSet<string> { "KHR_texture_basisu" } );
		var texture = document.ReadTexture( 0 );
		Assert.AreEqual( 1, texture.Image );
		var image = document.ReadImage( texture.Image.Value );
		Assert.AreEqual( "image/ktx2", image.MimeType );
		var decoded = GltfKtx.DecodeRgba32( image.Bytes );
		Assert.IsTrue( decoded.IsSrgb );
		Assert.AreEqual( 64, decoded.Width );
		Assert.AreEqual( 64, decoded.Height );
		NearPixels( decoded.Levels[0], [0, 255, 255, 255], 3 );
	}

	[TestMethod]
	public void RequiredBasisIsRejectedWithoutSupportAndOptionalBasisUsesPngFallback()
	{
		var input = Fixture( "gltf-ktx-cyan_rgb_reference_basis.ktx2" );
		Assert.ThrowsException<GltfException>( () => GltfReader.Read( BasisFixture( input, false, required: true ) ) );
		using var document = GltfReader.Read( BasisFixture( input, false, required: false ) );
		Assert.AreEqual( 0, document.ReadTexture( 0 ).Image );
		Assert.AreEqual( "image/png", document.ReadImage( 0 ).MimeType );
		CollectionAssert.Contains( document.UnsupportedOptionalExtensions.ToArray(), "KHR_texture_basisu" );
	}

	private static byte[] BasisFixture( byte[] input, bool bufferView, bool required )
	{
		const string white = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4////fwAJ+wP9KobjigAAAABJRU5ErkJggg==";
		object ktxImage = bufferView ? new { bufferView = 0, mimeType = "image/ktx2" }
			: new { uri = "data:image/ktx2;base64," + Convert.ToBase64String( input ), mimeType = "image/ktx2" };
		var root = new Dictionary<string, object>
		{
			["asset"] = new { version = "2.0" },
			["extensionsUsed"] = new[] { "KHR_texture_basisu" },
			["images"] = new object[] { new { uri = "data:image/png;base64," + white }, ktxImage },
			["textures"] = new[] { new { source = 0, extensions = new Dictionary<string, object> { ["KHR_texture_basisu"] = new { source = 1 } } } }
		};
		if ( required ) root["extensionsRequired"] = new[] { "KHR_texture_basisu" };
		if ( bufferView )
		{
			root["buffers"] = new[] { new { uri = "data:application/octet-stream;base64," + Convert.ToBase64String( input ), byteLength = input.Length } };
			root["bufferViews"] = new[] { new { buffer = 0, byteLength = input.Length } };
		}
		return JsonSerializer.SerializeToUtf8Bytes( root );
	}

	[TestMethod]
	public void RejectsInvalidTransferAndColorModelMetadata()
	{
		var source = Fixture( "gltf-ktx-cyan_rgb_reference_uastc.ktx2" );
		var dfd = (int)BinaryPrimitives.ReadUInt32LittleEndian( source.AsSpan( 48 ) );
		foreach ( var offset in new[] { dfd + 12, dfd + 14 } )
		{
			var invalid = (byte[])source.Clone();
			invalid[offset] = 255;
			Assert.ThrowsException<GltfException>( () => GltfKtx.DecodeRgba32( invalid ) );
		}
	}

	[TestMethod]
	public void RejectsTruncationInvalidHeadersAndDeclaredExpansionBeforeNativeDecode()
	{
		var original = Fixture( "gltf-ktx-alpha-simple.ktx2" );
		foreach ( var length in new[] { 0, 11, 79, 103, original.Length - 1 } )
			Assert.ThrowsException<GltfException>( () => GltfKtx.DecodeRgba32( original[..length] ) );
		var invalidIdentifier = (byte[])original.Clone();
		invalidIdentifier[0] ^= 1;
		Assert.ThrowsException<GltfException>( () => GltfKtx.DecodeRgba32( invalidIdentifier ) );
		var hugeWidth = (byte[])original.Clone();
		BinaryPrimitives.WriteUInt32LittleEndian( hugeWidth.AsSpan( 20 ), uint.MaxValue );
		Assert.ThrowsException<GltfException>( () => GltfKtx.DecodeRgba32( hugeWidth, 1024 ) );
		var invalidLevel = (byte[])original.Clone();
		BinaryPrimitives.WriteUInt64LittleEndian( invalidLevel.AsSpan( 80 ), ulong.MaxValue );
		Assert.ThrowsException<GltfException>( () => GltfKtx.DecodeRgba32( invalidLevel ) );
		var invalidDfd = (byte[])original.Clone();
		BinaryPrimitives.WriteUInt32LittleEndian( invalidDfd.AsSpan( 48 ), uint.MaxValue );
		Assert.ThrowsException<GltfException>( () => GltfKtx.DecodeRgba32( invalidDfd ) );
		var array = (byte[])original.Clone();
		BinaryPrimitives.WriteUInt32LittleEndian( array.AsSpan( 32 ), 2 );
		Assert.ThrowsException<GltfException>( () => GltfKtx.DecodeRgba32( array ) );
		Assert.ThrowsException<GltfException>( () => GltfKtx.DecodeRgba32( original, 255 ) );
		Assert.ThrowsException<GltfException>( () => GltfKtx.DecodeRgba32( original, 0 ) );
	}
}
