using Sandbox.Gltf;
using Sandbox.Resources;
using SkiaSharp;
using System.Buffers.Binary;

namespace Sandbox;

/// <summary>Compiles image bytes to a native texture resource without creating a GPU texture.</summary>
internal static class GltfTextureCompiler
{
	internal static unsafe byte[] Compile( GltfImage image, bool isSrgb, long maxDecodedBytes = 256L * 1024 * 1024 )
	{
		if ( image?.Bytes is not { Length: > 0 } bytes ) throw new GltfException( "Image data is missing." );
		if ( maxDecodedBytes <= 0 ) throw new GltfException( "Texture decoded-byte budget must be positive." );
		var png = string.Equals( image.MimeType, "image/png", StringComparison.OrdinalIgnoreCase );
		var jpeg = string.Equals( image.MimeType, "image/jpeg", StringComparison.OrdinalIgnoreCase );
		if ( !png && !jpeg ) throw new GltfException( "Only PNG and JPEG images are supported by this texture compiler." );
		if ( png ) ValidatePngPrecision( bytes );
		else ValidateJpegPrecision( bytes );

		using var input = new SKMemoryStream( bytes );
		using var codec = SKCodec.Create( input );
		if ( codec is null || png && codec.EncodedFormat != SKEncodedImageFormat.Png || jpeg && codec.EncodedFormat != SKEncodedImageFormat.Jpeg )
			throw new GltfException( "Image data is invalid or does not match its MIME type." );
		// Skia reports zero frames for a static image; the engine image loader also
		// treats counts above one as animated.
		if ( codec.FrameCount > 1 ) throw new GltfException( "Animated images are not supported by this texture compiler." );
		var width = codec.Info.Width;
		var height = codec.Info.Height;
		if ( width <= 0 || height <= 0 || width > 16384 || height > 16384 || (long)width * height * 4 > maxDecodedBytes )
			throw new GltfException( "Image dimensions or decoded-byte budget are invalid." );

		// Decode straight-alpha pixels directly into a bounded CPU bitmap. A partial
		// decoder result is not a successful import, even if it produced some pixels.
		using var bitmap = new Bitmap( width, height );
		var info = new SKImageInfo( width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul );
		fixed ( byte* pixels = bitmap.GetBuffer() )
		{
			var result = codec.GetPixels( info, (IntPtr)pixels );
			if ( result != SKCodecResult.Success ) throw new GltfException( $"Image decode failed: {result}." );
		}

		var writer = new VTexWriter();
		writer.Header.Width = (ushort)width;
		writer.Header.Height = (ushort)height;
		writer.Header.Depth = 1;
		writer.Header.MipCount = 1;
		writer.Header.Format = VTexWriter.VTEX_Format_t.VTEX_FORMAT_RGBA8888;
		writer.Header.Flags = VTexWriter.VTEX_Flags_t.VTEX_FLAG_NO_LOD |
			(isSrgb ? VTexWriter.VTEX_Flags_t.NONE : VTexWriter.VTEX_Flags_t.VTEX_FLAG_CREATE_LINEAR_API_TEXTURE);
		var resource = new ResourceWriter { ResourceVersion = 1 };
		resource.SetDataBlock( writer.GetData() );
		resource.SetStreamingData( bitmap.GetBuffer().ToArray() );
		return resource.ToArray();
	}

	private static void ValidatePngPrecision( ReadOnlySpan<byte> bytes )
	{
		ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
		if ( bytes.Length < 29 || !bytes.StartsWith( signature ) || BinaryPrimitives.ReadUInt32BigEndian( bytes[8..] ) != 13 || !bytes.Slice( 12, 4 ).SequenceEqual( "IHDR"u8 ) )
			throw new GltfException( "Invalid or truncated PNG header." );
		if ( bytes[24] == 16 ) throw new GltfException( "16-bit PNG requires a higher-precision texture compiler." );
	}

	private static void ValidateJpegPrecision( ReadOnlySpan<byte> bytes )
	{
		if ( bytes.Length < 4 || bytes[0] != 0xff || bytes[1] != 0xd8 ) throw new GltfException( "Invalid or truncated JPEG header." );
		var offset = 2;
		while ( offset < bytes.Length )
		{
			if ( bytes[offset++] != 0xff ) throw new GltfException( "Invalid JPEG marker." );
			while ( offset < bytes.Length && bytes[offset] == 0xff ) offset++;
			if ( offset >= bytes.Length ) break;
			var marker = bytes[offset++];
			if ( marker is 0xd9 or 0xda ) break;
			if ( marker is 0x01 or >= 0xd0 and <= 0xd7 ) continue;
			if ( offset + 2 > bytes.Length ) break;
			var length = BinaryPrimitives.ReadUInt16BigEndian( bytes[offset..] );
			if ( length < 2 || length > bytes.Length - offset ) throw new GltfException( "Invalid JPEG segment length." );
			if ( marker is >= 0xc0 and <= 0xcf && marker is not (0xc4 or 0xc8 or 0xcc) )
			{
				if ( length < 8 || bytes[offset + 2] != 8 ) throw new GltfException( "Only 8-bit JPEG is supported by this texture compiler." );
				return;
			}
			offset += length;
		}
		throw new GltfException( "JPEG frame header is missing or truncated." );
	}
}
