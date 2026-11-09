using Sandbox.Gltf;
using Sandbox.Resources;
using SkiaSharp;
using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Sandbox;

/// <summary>Compiles image bytes to a native texture resource without creating a GPU texture.</summary>
internal static class GltfTextureCompiler
{
	internal readonly record struct MetallicRoughnessResources( byte[] NormalRoughness, byte[] Metalness );

	/// <summary>
	/// Default peak decoded bytes for one texture: the base bitmap plus three mip-chain-sized
	/// serialization buffers (see <see cref="ValidateColorTextureBudget"/>). A 4096x4096 color
	/// texture with mipmaps needs about 320 MiB, so 512 MiB admits 4K and rejects 8K.
	/// </summary>
	internal const long DefaultMaxDecodedBytes = 512L * 1024 * 1024;

	internal static unsafe byte[] Compile( GltfImage image, bool isSrgb, long maxDecodedBytes = DefaultMaxDecodedBytes )
		=> CompileInternal( image, isSrgb, maxDecodedBytes, false );

	/// <summary>Generate the complete native mip chain in linear light for color textures.</summary>
	internal static byte[] CompileWithMipmaps( GltfImage image, bool isSrgb, long maxDecodedBytes = DefaultMaxDecodedBytes )
		=> CompileInternal( image, isSrgb, maxDecodedBytes, true );

	private static unsafe byte[] CompileInternal( GltfImage image, bool isSrgb, long maxDecodedBytes, bool mipmaps )
	{
		if ( maxDecodedBytes <= 0 ) throw new GltfException( "Texture decoded-byte budget must be positive." );
		if ( IsBasisImage( image ) ) return GltfBasisTexturePlan.Create( image, isSrgb, mipmaps, maxDecodedBytes ).Compile();
		if ( !isSrgb && IsPng16( image ) ) return CompilePng16Data( image, mipmaps, maxDecodedBytes );
		var codec = OpenImage( image, out var input );
		using var ownedInput = input;
		using var ownedCodec = codec;
		var width = codec.Info.Width;
		var height = codec.Info.Height;
		if ( width <= 0 || height <= 0 || width > 16384 || height > 16384 || (long)width * height * 4 > maxDecodedBytes )
			throw new GltfException( "Image dimensions or decoded-byte budget are invalid." );
		long chainBytes = 0;
		var mipCount = 0;
		for ( var w = width; ; w = Math.Max( 1, w / 2 ) )
		{
			var h = Math.Max( 1, height >> mipCount );
			chainBytes += (long)w * h * 4;
			mipCount++;
			if ( !mipmaps || w == 1 && h == 1 ) break;
		}
		ValidateColorTextureBudget( width, height, mipmaps, maxDecodedBytes );
		if ( chainBytes > int.MaxValue ) throw new GltfException( "Image mip chain exceeds the supported array size." );

		// Decode straight-alpha pixels directly into a bounded CPU bitmap. A partial
		// decoder result is not a successful import, even if it produced some pixels.
		using var bitmap = new Bitmap( width, height );
		var info = new SKImageInfo( width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul );
		fixed ( byte* decodedPixels = bitmap.GetBuffer() )
		{
			var result = codec.GetPixels( info, (IntPtr)decodedPixels );
			if ( result != SKCodecResult.Success ) throw new GltfException( $"Image decode failed: {result}." );
		}

		var writer = new VTexWriter();
		writer.Header.Width = (ushort)width;
		writer.Header.Height = (ushort)height;
		writer.Header.Depth = 1;
		writer.Header.MipCount = (byte)mipCount;
		writer.Header.Format = VTexWriter.VTEX_Format_t.VTEX_FORMAT_RGBA8888;
		writer.Header.Flags = (mipmaps ? VTexWriter.VTEX_Flags_t.NONE : VTexWriter.VTEX_Flags_t.VTEX_FLAG_NO_LOD) |
			(isSrgb ? VTexWriter.VTEX_Flags_t.NONE : VTexWriter.VTEX_Flags_t.VTEX_FLAG_CREATE_LINEAR_API_TEXTURE);
		var resource = new ResourceWriter { ResourceVersion = 1 };
		resource.SetDataBlock( writer.GetData() );
		var pixels = bitmap.GetBuffer().ToArray();
		if ( !mipmaps ) resource.SetStreamingData( pixels );
		else
		{
			var chain = new byte[(int)chainBytes];
			var offset = chain.Length;
			var w = width;
			var h = height;
			while ( true )
			{
				offset -= pixels.Length;
				pixels.CopyTo( chain, offset ); // VTEX stores smallest mip first.
				if ( w == 1 && h == 1 ) break;
				pixels = Downsample( pixels, w, h, isSrgb );
				w = Math.Max( 1, w / 2 ); h = Math.Max( 1, h / 2 );
			}
			resource.SetStreamingData( chain );
		}
		return resource.ToArray();
	}

	internal static void ValidateColorTextureBudget( int width, int height, bool mipmaps, long budget )
	{
		if ( width <= 0 || height <= 0 || width > 16384 || height > 16384 || budget <= 0 ) throw new GltfException( "Image dimensions or decoded-byte budget are invalid." );
		long chainBytes = 0;
		for ( int w = width, h = height; ; w = Math.Max( 1, w / 2 ), h = Math.Max( 1, h / 2 ) )
		{
			chainBytes += (long)w * h * 4;
			if ( !mipmaps || w == 1 && h == 1 ) break;
		}
		// At serialization the base bitmap, mip chain, ResourceWriter ByteStream
		// and its final ToArray copy coexist. Include all three chain-sized buffers.
		if ( (long)width * height * 4 + chainBytes * 3 > budget ) throw new GltfException( "Image mip chain and serialization copies exceed the decoded-byte budget." );
	}

	private static bool IsPng16( GltfImage image ) => string.Equals( image?.MimeType, "image/png", StringComparison.OrdinalIgnoreCase ) && image.Bytes is { Length: >= 29 } bytes && bytes[24] == 16;

	private static byte[] CompilePng16Data( GltfImage image, bool mipmaps, long budget )
	{
		ValidatePng16WorkingSet( image.Bytes, mipmaps, budget );
		var decoded = GltfPng16Decoder.Decode( image.Bytes, budget );
		var pixels = new float[decoded.Rgba.Length];
		for ( var i = 0; i < pixels.Length; i++ ) pixels[i] = decoded.Rgba[i] / 65535f;
		return CompileLinearFloatTexture( pixels, decoded.Width, decoded.Height, mipmaps );
	}

	private static float[] PackPng16MetallicRoughness( ushort[] source, float factor, bool roughness )
	{
		var pixels = new float[source.Length];
		for ( var i = 0; i < pixels.Length; i += 4 )
		{
			if ( roughness ) { pixels[i] = pixels[i + 1] = 1.00392163f * .5f; pixels[i + 2] = source[i + 1] / 65535f * factor; }
			else pixels[i + 1] = source[i + 2] / 65535f * factor;
			pixels[i + 3] = 1;
		}
		return pixels;
	}

	private static void ValidatePng16WorkingSet( byte[] bytes, bool mipmaps, long budget )
	{
		if ( bytes is not { Length: >= 29 } || budget <= 0 || !bytes.AsSpan( 0, 8 ).SequenceEqual( new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 } ) || BinaryPrimitives.ReadUInt32BigEndian( bytes.AsSpan( 8 ) ) != 13 || !bytes.AsSpan( 12, 4 ).SequenceEqual( "IHDR"u8 ) ) throw new GltfException( "Invalid PNG16 header or budget." );
		var width = BinaryPrimitives.ReadUInt32BigEndian( bytes.AsSpan( 16 ) ); var height = BinaryPrimitives.ReadUInt32BigEndian( bytes.AsSpan( 20 ) );
		if ( width == 0 || height == 0 || width > 16384 || height > 16384 ) throw new GltfException( "Invalid PNG16 dimensions." );
		long chain = 0;
		for ( long w = width, h = height; ; w = Math.Max( 1, w / 2 ), h = Math.Max( 1, h / 2 ) )
		{
			chain = checked(chain + w * h * 16);
			if ( !mipmaps || w == 1 && h == 1 ) break;
		}
		// Bound encoded data, decode buffers, source 16-bit pixels, float working
		// images, both material resources and serialization copies conservatively.
		var pixels = (long)width * height;
		var peak = checked(3L * bytes.Length + pixels * 40 + chain * 4 + 1024 * 1024);
		if ( chain > int.MaxValue || pixels * 4 > int.MaxValue || peak > budget ) throw new GltfException( "PNG16 texture working set exceeds the decoded-byte budget." );
	}

	private static bool IsBasisImage( GltfImage image ) => string.Equals( image?.MimeType, "image/ktx2", StringComparison.OrdinalIgnoreCase );

	private static SKCodec OpenImage( GltfImage image, out SKMemoryStream input )
	{
		if ( image?.Bytes is not { Length: > 0 } bytes ) throw new GltfException( "Image data is missing." );
		var png = string.Equals( image.MimeType, "image/png", StringComparison.OrdinalIgnoreCase );
		var jpeg = string.Equals( image.MimeType, "image/jpeg", StringComparison.OrdinalIgnoreCase );
		var webp = string.Equals( image.MimeType, "image/webp", StringComparison.OrdinalIgnoreCase );
		if ( !png && !jpeg && !webp ) throw new GltfException( "Only PNG, JPEG and WebP images are supported by this texture compiler." );
		if ( png ) ValidatePngPrecision( bytes ); else if ( jpeg ) ValidateJpegPrecision( bytes );
		input = new SKMemoryStream( bytes );
		SKCodec codec = null;
		try
		{
			codec = SKCodec.Create( input );
			if ( codec is null || png && codec.EncodedFormat != SKEncodedImageFormat.Png || jpeg && codec.EncodedFormat != SKEncodedImageFormat.Jpeg || webp && codec.EncodedFormat != SKEncodedImageFormat.Webp )
				throw new GltfException( "Image data is invalid or does not match its MIME type." );
			// Skia reports zero frames for a static image.
			if ( codec.FrameCount > 1 ) throw new GltfException( "Animated images are not supported by this texture compiler." );
			return codec;
		}
		catch { codec?.Dispose(); input.Dispose(); throw; }
	}

	/// <summary>
	/// Repack linear glTF G roughness/B metallic into the shipped complex shader's
	/// flat hemi-octahedral RG+roughness B texture and metalness G texture.
	/// Factors are baked exactly once into float32 resources, before mip generation.
	/// </summary>
	internal static unsafe MetallicRoughnessResources CompileMetallicRoughness( GltfImage image, float metallicFactor, float roughnessFactor, bool mipmaps, long maxDecodedBytes = 256L * 1024 * 1024 )
	{
		ValidateFactor( metallicFactor ); ValidateFactor( roughnessFactor );
		if ( IsBasisImage( image ) )
		{
			var plan = GltfBasisTexturePlan.Create( image, false, mipmaps, maxDecodedBytes );
			var retainedSourceBytes = 2L * plan.EncodedSource.Length + plan.DataFormatDescriptor.Length + plan.KeyValueData.Length;
			// Reject the configured float-adapter working set before native decode.
			// The authored compressed image remains owned by the source plan.
			ValidateMetallicRoughnessBudget( plan.Width, plan.Height, mipmaps, maxDecodedBytes - retainedSourceBytes );
			return CompileMetallicRoughnessLevels( plan.DecodeRgba32().Levels, metallicFactor, roughnessFactor, mipmaps, maxDecodedBytes - retainedSourceBytes );
		}
		if ( IsPng16( image ) )
		{
			ValidatePng16WorkingSet( image.Bytes, mipmaps, maxDecodedBytes );
			var decoded = GltfPng16Decoder.Decode( image.Bytes, maxDecodedBytes );
			var roughness = CompileLinearFloatTexture( PackPng16MetallicRoughness( decoded.Rgba, roughnessFactor, true ), decoded.Width, decoded.Height, mipmaps );
			var metalness = CompileLinearFloatTexture( PackPng16MetallicRoughness( decoded.Rgba, metallicFactor, false ), decoded.Width, decoded.Height, mipmaps );
			return new( roughness, metalness );
		}
		var codec = OpenImage( image, out var input );
		using var ownedInput = input;
		using var ownedCodec = codec;
		var width = codec.Info.Width; var height = codec.Info.Height;
		ValidateMetallicRoughnessBudget( width, height, mipmaps, maxDecodedBytes );
		var pixels = new byte[checked(width * height * 4)];
		var info = new SKImageInfo( width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul );
		fixed ( byte* output = pixels )
		{
			var result = codec.GetPixels( info, (IntPtr)output );
			if ( result != SKCodecResult.Success ) throw new GltfException( $"Image decode failed: {result}." );
		}
		return CompileMetallicRoughnessPixels( pixels, width, height, metallicFactor, roughnessFactor, mipmaps, maxDecodedBytes );
	}

	internal static MetallicRoughnessResources CompileMetallicRoughnessPixels( ReadOnlySpan<byte> pixels, int width, int height, float metallicFactor, float roughnessFactor, bool mipmaps, long maxDecodedBytes = 256L * 1024 * 1024 )
	{
		ValidateFactor( metallicFactor ); ValidateFactor( roughnessFactor );
		ValidateMetallicRoughnessBudget( width, height, mipmaps, maxDecodedBytes );
		if ( (long)width * height * 4 != pixels.Length ) throw new GltfException( "Metallic-roughness RGBA image dimensions do not match its pixels." );
		// Compile sequentially; two large float working images need not coexist.
		var roughness = CompileLinearFloatTexture( PackMetallicRoughnessPixels( pixels, roughnessFactor, true ), width, height, mipmaps );
		var metalness = CompileLinearFloatTexture( PackMetallicRoughnessPixels( pixels, metallicFactor, false ), width, height, mipmaps );
		return new( roughness, metalness );
	}

	/// <summary>
	/// Repack authored linear RGBA mips independently, then generate only a missing
	/// tail in float32. Rebuilding from the base or reducing to RGBA8 first would
	/// discard authored mip content or quantize sub-byte material factors.
	/// </summary>
	internal static MetallicRoughnessResources CompileMetallicRoughnessLevels( IReadOnlyList<GltfKtxLevel> levels, float metallicFactor, float roughnessFactor, bool mipmaps, long maxDecodedBytes = 256L * 1024 * 1024 )
	{
		ValidateFactor( metallicFactor ); ValidateFactor( roughnessFactor );
		if ( levels is not { Count: > 0 } || levels[0] is not { } first ) throw new GltfException( "Metallic-roughness authored mip levels are missing." );
		ValidateMetallicRoughnessBudget( first.Width, first.Height, mipmaps, maxDecodedBytes );
		var fullCount = 1;
		for ( var size = Math.Max( first.Width, first.Height ); size > 1; size >>= 1 ) fullCount++;
		if ( levels.Count > fullCount ) throw new GltfException( "Metallic-roughness authored mip count exceeds the source dimensions." );
		for ( var level = 0; level < levels.Count; level++ )
		{
			var source = levels[level];
			if ( source is null || source.Width != Math.Max( 1, first.Width >> level ) || source.Height != Math.Max( 1, first.Height >> level ) || source.Pixels?.LongLength != (long)source.Width * source.Height * 4 )
				throw new GltfException( "Metallic-roughness authored mip shape or pixels are invalid." );
		}
		var roughness = CompileLinearFloatTexture( levels, roughnessFactor, true, mipmaps );
		var metalness = CompileLinearFloatTexture( levels, metallicFactor, false, mipmaps );
		return new( roughness, metalness );
	}

	private static void ValidateFactor( float factor )
	{
		if ( !float.IsFinite( factor ) || factor < 0 || factor > 1 ) throw new GltfException( "Metallic-roughness factor must be finite and in [0,1]." );
	}

	private static void ValidateMetallicRoughnessBudget( int width, int height, bool mipmaps, long budget )
	{
		if ( width <= 0 || height <= 0 || width > 16384 || height > 16384 || budget <= 0 ) throw new GltfException( "Metallic-roughness image dimensions or decoded-byte budget are invalid." );
		long chain = 0;
		for ( int w = width, h = height; ; w = Math.Max( 1, w / 2 ), h = Math.Max( 1, h / 2 ) )
		{
			chain += (long)w * h * 4 * sizeof(float);
			if ( !mipmaps || w == 1 && h == 1 ) break;
		}
		// Retained first resource + current chain + ResourceWriter's temporary
		// stream and final copy, one float working image, decoder/base image, headers.
		var peak = chain * 4 + (long)width * height * (4 * sizeof(float) + 8) + 1024 * 1024;
		if ( chain > int.MaxValue || peak > budget ) throw new GltfException( "Metallic-roughness native float texture working set exceeds the decoded-byte budget." );
	}

	internal static float[] PackMetallicRoughnessPixels( ReadOnlySpan<byte> pixels, float factor, bool roughness )
	{
		ValidateFactor( factor );
		if ( pixels.Length == 0 || pixels.Length % 4 != 0 || (long)pixels.Length * sizeof(float) > int.MaxValue ) throw new GltfException( "Invalid or oversized metallic-roughness RGBA pixel stream." );
		var result = new float[pixels.Length];
		for ( var i = 0; i < pixels.Length; i += 4 )
		{
			if ( roughness )
			{
				result[i] = result[i + 1] = 1.00392163f * .5f;
				result[i + 2] = pixels[i + 1] / 255f * factor;
			}
			else result[i + 1] = pixels[i + 2] / 255f * factor;
			result[i + 3] = 1;
		}
		return result;
	}

	private static byte[] CompileLinearFloatTexture( float[] pixels, int width, int height, bool mipmaps )
	{
		var chainBytes = 0; var count = 0;
		for ( int w = width, h = height; ; w = Math.Max( 1, w / 2 ), h = Math.Max( 1, h / 2 ) )
		{
			chainBytes = checked(chainBytes + w * h * 4 * sizeof(float)); count++;
			if ( !mipmaps || w == 1 && h == 1 ) break;
		}
		var chain = new byte[chainBytes]; var offset = chainBytes;
		for ( int w = width, h = height; ; w = Math.Max( 1, w / 2 ), h = Math.Max( 1, h / 2 ) )
		{
			var bytes = MemoryMarshal.AsBytes( pixels.AsSpan() ); offset -= bytes.Length; bytes.CopyTo( chain.AsSpan( offset ) );
			if ( !mipmaps || w == 1 && h == 1 ) break;
			pixels = DownsampleLinearRgba( pixels, w, h );
		}
		return SerializeLinearFloatTexture( chain, width, height, count, mipmaps );
	}

	private static byte[] CompileLinearFloatTexture( IReadOnlyList<GltfKtxLevel> source, float factor, bool roughness, bool mipmaps )
	{
		var width = source[0].Width; var height = source[0].Height;
		var chainBytes = 0; var count = 0;
		for ( int w = width, h = height; ; w = Math.Max( 1, w / 2 ), h = Math.Max( 1, h / 2 ) )
		{
			chainBytes = checked(chainBytes + w * h * 4 * sizeof(float)); count++;
			if ( !mipmaps || w == 1 && h == 1 ) break;
		}
		var chain = new byte[chainBytes]; var offset = chainBytes; float[] pixels = null;
		for ( var level = 0; level < count; level++ )
		{
			// Complete authored mips remain independent inputs. A generated mip
			// starts only after the last authored level, without byte quantization.
			pixels = level < source.Count ? PackMetallicRoughnessPixels( source[level].Pixels, factor, roughness )
				: DownsampleLinearRgba( pixels, Math.Max( 1, width >> (level - 1) ), Math.Max( 1, height >> (level - 1) ) );
			var bytes = MemoryMarshal.AsBytes( pixels.AsSpan() ); offset -= bytes.Length; bytes.CopyTo( chain.AsSpan( offset ) );
		}
		return SerializeLinearFloatTexture( chain, width, height, count, mipmaps );
	}

	private static byte[] SerializeLinearFloatTexture( byte[] chain, int width, int height, int count, bool mipmaps )
	{
		var writer = new VTexWriter(); writer.Header.Width = (ushort)width; writer.Header.Height = (ushort)height; writer.Header.Depth = 1;
		writer.Header.MipCount = (byte)count; writer.Header.Format = VTexWriter.VTEX_Format_t.VTEX_FORMAT_RGBA32323232F;
		writer.Header.Flags = VTexWriter.VTEX_Flags_t.VTEX_FLAG_CREATE_LINEAR_API_TEXTURE | (mipmaps ? VTexWriter.VTEX_Flags_t.NONE : VTexWriter.VTEX_Flags_t.VTEX_FLAG_NO_LOD);
		var resource = new ResourceWriter { ResourceVersion = 1 }; resource.SetDataBlock( writer.GetData() ); resource.SetStreamingData( chain ); return resource.ToArray();
	}

	internal static float[] DownsampleLinearRgba( ReadOnlySpan<float> pixels, int width, int height )
	{
		if ( width <= 0 || height <= 0 || (long)width * height * 4 != pixels.Length || pixels.Length > int.MaxValue / sizeof(float) ) throw new GltfException( "Invalid linear RGBA image dimensions." );
		var targetWidth = Math.Max( 1, width / 2 ); var targetHeight = Math.Max( 1, height / 2 );
		var result = new float[checked(targetWidth * targetHeight * 4)];
		for ( var y = 0; y < targetHeight; y++ ) for ( var x = 0; x < targetWidth; x++ ) for ( var channel = 0; channel < 4; channel++ )
		{
			double total = 0; var samples = 0;
			for ( var sy = y * height / targetHeight; sy < (y + 1) * height / targetHeight; sy++ ) for ( var sx = x * width / targetWidth; sx < (x + 1) * width / targetWidth; sx++ )
			{
				var value = pixels[(sy * width + sx) * 4 + channel]; if ( !float.IsFinite( value ) ) throw new GltfException( "Nonfinite linear mip input." ); total += value; samples++;
			}
			result[(y * targetWidth + x) * 4 + channel] = (float)(total / samples);
		}
		return result;
	}

	internal static byte[] Downsample( ReadOnlySpan<byte> pixels, int width, int height, bool isSrgb )
	{
		if ( width <= 0 || height <= 0 || (long)width * height * 4 != pixels.Length ) throw new ArgumentException( "Invalid RGBA image dimensions." );
		var targetWidth = Math.Max( 1, width / 2 );
		var targetHeight = Math.Max( 1, height / 2 );
		var result = new byte[checked(targetWidth * targetHeight * 4)];
		for ( var y = 0; y < targetHeight; y++ )
			for ( var x = 0; x < targetWidth; x++ )
				for ( var channel = 0; channel < 4; channel++ )
				{
					double total = 0;
					var samples = 0;
					for ( var sy = y * height / targetHeight; sy < (y + 1) * height / targetHeight; sy++ )
						for ( var sx = x * width / targetWidth; sx < (x + 1) * width / targetWidth; sx++ )
						{
							var value = pixels[(sy * width + sx) * 4 + channel] / 255.0;
							if ( isSrgb && channel < 3 ) value = value <= .04045 ? value / 12.92 : Math.Pow( (value + .055) / 1.055, 2.4 );
							total += value;
							samples++;
						}
					var average = total / samples;
					if ( isSrgb && channel < 3 ) average = average <= .0031308 ? average * 12.92 : 1.055 * Math.Pow( average, 1 / 2.4 ) - .055;
					result[(y * targetWidth + x) * 4 + channel] = (byte)Math.Clamp( (int)Math.Round( average * 255 ), 0, 255 );
				}
		return result;
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
