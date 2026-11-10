namespace Sandbox.Gltf;

/// <summary>Decoded image data owned by managed code.</summary>
public sealed class GltfKtxImage
{
	public int Width { get; init; }
	public int Height { get; init; }
	public bool IsSrgb { get; init; }
	public IReadOnlyList<GltfKtxLevel> Levels { get; init; }
}

/// <summary>One tightly packed RGBA8 mip level.</summary>
public sealed class GltfKtxLevel
{
	public int Width { get; init; }
	public int Height { get; init; }
	public byte[] Pixels { get; init; }
}

/// <summary>The GPU block-compressed format Basis Universal data is transcoded to.</summary>
public enum GltfKtxBlockFormat
{
	/// <summary>BC7, 16 bytes per 4x4 block, four channels. For colour and for data textures that use three or four channels.</summary>
	Bc7,
	/// <summary>
	/// BC5, 16 bytes per 4x4 block, two channels. Basis stores the second channel of two-channel data in its alpha slice,
	/// so the blocks hold the source's red and alpha, not red and green. A shader has to rebuild blue itself.
	/// </summary>
	Bc5
}

/// <summary>Transcoded block-compressed image data owned by managed code.</summary>
public sealed class GltfKtxBlockImage
{
	public int Width { get; init; }
	public int Height { get; init; }
	public bool IsSrgb { get; init; }
	public GltfKtxBlockFormat Format { get; init; }
	public IReadOnlyList<GltfKtxBlockLevel> Levels { get; init; }
}

/// <summary>One mip level of block data: <c>ceil(w/4) * ceil(h/4)</c> blocks of 16 bytes, row by row.</summary>
public sealed class GltfKtxBlockLevel
{
	public int Width { get; init; }
	public int Height { get; init; }
	public byte[] Data { get; init; }
}

public static class GltfKtx
{
	/// <summary>Bytes of one mip level of block-compressed data. Both BC5 and BC7 use 16-byte blocks.</summary>
	public static long BlockLevelBytes( int width, int height ) => (long)((width + 3) / 4) * ((height + 3) / 4) * 16;

	/// <summary>
	/// Transcode a Basis Universal KTX2 image (ETC1S or UASTC) straight to BC7 or BC5, one block at a time.
	/// The pixels are never expanded to RGBA, so this is several times smaller than <see cref="DecodeRgba32"/>
	/// and the result can be uploaded as it is.
	/// </summary>
	public static GltfKtxBlockImage TranscodeToBlocks( byte[] encoded, GltfKtxBlockFormat format, long maxTranscodedBytes = 256L * 1024 * 1024 )
	{
		var target = format switch
		{
			GltfKtxBlockFormat.Bc7 => Evergine.Bindings.KTX.ktx_transcode_fmt_e.KTX_TTF_BC7_RGBA,
			GltfKtxBlockFormat.Bc5 => Evergine.Bindings.KTX.ktx_transcode_fmt_e.KTX_TTF_BC5_RG,
			_ => throw new GltfException( "Unsupported KTX block format." )
		};
		var core = Transcode( encoded, target, BlockLevelBytes, maxTranscodedBytes );
		return new GltfKtxBlockImage
		{
			Width = core.Width, Height = core.Height, IsSrgb = core.IsSrgb, Format = format,
			Levels = Array.AsReadOnly( core.Levels.Select( x => new GltfKtxBlockLevel { Width = x.Width, Height = x.Height, Data = x.Data } ).ToArray() )
		};
	}

	public static GltfKtxImage DecodeRgba32( byte[] encoded, long maxDecodedBytes = 256L * 1024 * 1024 )
	{
		var core = Transcode( encoded, Evergine.Bindings.KTX.ktx_transcode_fmt_e.KTX_TTF_RGBA32, ( w, h ) => (long)w * h * 4, maxDecodedBytes );
		return new GltfKtxImage
		{
			Width = core.Width, Height = core.Height, IsSrgb = core.IsSrgb,
			Levels = core.Levels.Select( x => new GltfKtxLevel { Width = x.Width, Height = x.Height, Pixels = x.Data } ).ToArray()
		};
	}

	private sealed record CoreLevel( int Width, int Height, byte[] Data );
	private sealed record CoreImage( int Width, int Height, bool IsSrgb, CoreLevel[] Levels );

	private static unsafe CoreImage Transcode( byte[] encoded, Evergine.Bindings.KTX.ktx_transcode_fmt_e target, Func<int, int, long> levelBytes, long maxDecodedBytes )
	{
		ArgumentNullException.ThrowIfNull( encoded );
		if ( maxDecodedBytes <= 0 ) throw new GltfException( "KTX decoded-byte budget must be positive." );
		if ( encoded.Length < 80 || !encoded.AsSpan( 0, 12 ).SequenceEqual( new byte[] { 0xab, 0x4b, 0x54, 0x58, 0x20, 0x32, 0x30, 0xbb, 13, 10, 26, 10 } ) )
			throw new GltfException( "Invalid or truncated KTX2 header." );
		uint U32( int at ) => System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian( encoded.AsSpan( at, 4 ) );
		ulong U64( int at ) => System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian( encoded.AsSpan( at, 8 ) );
		bool InBounds( ulong offset, ulong length ) => offset <= (ulong)encoded.Length && length <= (ulong)encoded.Length - offset;
		if ( !InBounds( U32( 48 ), U32( 52 ) ) || !InBounds( U32( 56 ), U32( 60 ) ) || !InBounds( U64( 64 ), U64( 72 ) ) )
			throw new GltfException( "Invalid KTX2 metadata bounds." );
		var width = U32( 20 ); var height = U32( 24 );
		var declaredLevels = U32( 40 );
		var levelCount = declaredLevels == 0 ? 1u : declaredLevels;
		if ( width == 0 || height == 0 || width > int.MaxValue || height > int.MaxValue || U32( 28 ) != 0 || U32( 32 ) != 0 || U32( 36 ) != 1 )
			throw new GltfException( "Expected a 2D non-array, single-face KTX2 image." );
		var maxLevels = 1;
		for ( var size = Math.Max( width, height ); size > 1; size >>= 1 ) maxLevels++;
		if ( levelCount > maxLevels || 80L + 24L * levelCount > encoded.Length )
			throw new GltfException( "Invalid or truncated KTX2 level index." );
		ulong total = 0, totalExpanded = 0;
		for ( var level = 0; level < levelCount; level++ )
		{
			var w = Math.Max( 1u, width >> (int)level );
			var h = Math.Max( 1u, height >> (int)level );
			var bytes = (ulong)levelBytes( (int)w, (int)h );
			if ( bytes > (ulong)maxDecodedBytes || total > (ulong)maxDecodedBytes - bytes || bytes > int.MaxValue )
				throw new GltfException( "KTX2 decoded image exceeds the configured byte budget." );
			total += bytes;
			var at = 80 + (int)level * 24;
			var offset = U64( at ); var length = U64( at + 8 ); var expanded = U64( at + 16 );
			if ( !InBounds( offset, length ) || expanded > (ulong)maxDecodedBytes || totalExpanded > (ulong)maxDecodedBytes - expanded )
				throw new GltfException( "Invalid KTX2 level bounds or expansion size." );
			totalExpanded += expanded;
		}
		Evergine.Bindings.KTX.ktxTexture2* texture = null;
		try
		{
			fixed ( byte* source = encoded )
			{
				var result = Evergine.Bindings.KTX.KTX.ktxTexture2_CreateFromMemory( source, (nuint)encoded.Length, 9, &texture );
				if ( result != Evergine.Bindings.KTX.ktx_error_code_e.KTX_SUCCESS || texture == null ) throw new GltfException( $"KTX2 creation failed: {result}." );
				if ( texture->baseWidth != width || texture->baseHeight != height || texture->numLevels != levelCount ) throw new GltfException( "KTX2 native metadata disagrees with header." );
				var transfer = (int)Evergine.Bindings.KTX.KTX.ktxTexture2_GetTransferFunction_e( texture );
				if ( transfer is not (1 or 2) ) throw new GltfException( "Unsupported KTX2 transfer function." );
				if ( Evergine.Bindings.KTX.KTX.ktxTexture2_NeedsTranscoding( texture ) == 0 ) throw new GltfException( "Expected a Basis Universal KTX2 image." );
				result = Evergine.Bindings.KTX.KTX.ktxTexture2_TranscodeBasis( texture, target, 0 );
				if ( result != Evergine.Bindings.KTX.ktx_error_code_e.KTX_SUCCESS ) throw new GltfException( $"KTX2 Basis transcode failed: {result}." );
				var data = Evergine.Bindings.KTX.KTX.ktxTexture_GetData( (Evergine.Bindings.KTX.ktxTexture*)texture );
				var dataSize = (ulong)Evergine.Bindings.KTX.KTX.ktxTexture_GetDataSize( (Evergine.Bindings.KTX.ktxTexture*)texture );
				if ( data == null || dataSize > (ulong)maxDecodedBytes ) throw new GltfException( "Invalid KTX2 transcoded data size." );
				var levels = new CoreLevel[(int)levelCount];
				for ( var level = 0; level < levels.Length; level++ )
				{
					var w = Math.Max( 1u, width >> level ); var h = Math.Max( 1u, height >> level );
					var expected = levelBytes( (int)w, (int)h );
					// The library's own size for this level must agree with the layout we copy, or we would read the wrong bytes.
					var nativeSize = (long)Evergine.Bindings.KTX.KTX.ktxTexture_GetImageSize( (Evergine.Bindings.KTX.ktxTexture*)texture, (uint)level );
					if ( nativeSize != expected ) throw new GltfException( $"KTX2 transcoded mip {level} is {nativeSize} bytes; {expected} expected." );
					var byteCount = (int)expected;
					nuint offset = 0;
					result = Evergine.Bindings.KTX.KTX.ktxTexture2_GetImageOffset( texture, (uint)level, 0, 0, &offset );
					if ( result != Evergine.Bindings.KTX.ktx_error_code_e.KTX_SUCCESS || (ulong)offset > dataSize || (ulong)byteCount > dataSize - (ulong)offset ) throw new GltfException( "Invalid KTX2 transcoded mip bounds." );
					var pixels = new byte[byteCount];
					System.Runtime.InteropServices.Marshal.Copy( (IntPtr)(data + (nint)offset), pixels, 0, byteCount );
					levels[level] = new CoreLevel( (int)w, (int)h, pixels );
				}
				return new CoreImage( (int)width, (int)height, transfer == 2, levels );
			}
		}
		catch ( Exception ex ) when ( ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException )
		{
			throw new GltfException( "KTX decoder is unavailable for this platform.", ex );
		}
		finally
		{
			if ( texture != null ) Evergine.Bindings.KTX.KTX.ktxTexture2_Destroy( texture );
		}
	}
}
