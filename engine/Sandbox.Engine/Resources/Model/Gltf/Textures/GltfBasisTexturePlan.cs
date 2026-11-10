using Sandbox.Gltf;
using Sandbox.Resources;
using System.Buffers.Binary;
using System.Text;

namespace Sandbox;

internal enum GltfBasisSourceCodec { Etc1S, Uastc }
/// <summary>
/// The texture format a Basis image is written as. Block formats stay compressed on the GPU, so they are the normal
/// choice; RGBA32 is the exact fallback for images a block format cannot hold.
/// </summary>
internal enum GltfBasisTargetCodec { Rgba32, Bc7, Bc5 }

/// <summary>
/// CPU-owned portable Basis source and deterministic texture preparation. The source is transcoded to BC7 (or BC5 on
/// request) block by block and written as it is, so no RGBA image is built and the GPU samples compressed data.
/// An image that cannot be written that way (smaller than 16 pixels, or missing mips that must be generated from
/// pixels) is decoded to RGBA32 instead.
/// Encoded source, DFD, key/value bytes and authored mip order remain intact.
/// </summary>
internal sealed class GltfBasisTexturePlan
{
	internal readonly record struct Level( int Width, int Height, ulong ByteOffset, ulong ByteLength, ulong UncompressedByteLength );

	private byte[] encoded;
	private long budget;
	internal ReadOnlyMemory<byte> EncodedSource => encoded;
	internal ReadOnlyMemory<byte> DataFormatDescriptor { get; private init; }
	internal ReadOnlyMemory<byte> KeyValueData { get; private init; }
	internal IReadOnlyList<Level> Levels { get; private init; }
	internal int Width { get; private init; }
	internal int Height { get; private init; }
	internal byte ColorPrimaries { get; private init; }
	internal byte TransferFunction { get; private init; }
	internal bool IsSrgb => TransferFunction == 2;
	// Historical samples tag linear data with BT709. Preserve/report that tag;
	// non-color channels have no color-primary conversion applied to their bytes.
	internal bool HasNonColorPrimaries => !IsSrgb && ColorPrimaries != 0;
	internal string Orientation { get; private init; }
	internal string Swizzle { get; private init; }
	internal GltfBasisSourceCodec SourceCodec { get; private init; }
	/// <summary>
	/// True when the texture can be written as block data: at least 16 pixels each way (a 1x1 image would become a 4x4
	/// block, which is what the engine's own texture writer avoids), and every output mip comes from the file. A mip that
	/// has to be generated is made from pixels, which block data does not give.
	/// </summary>
	internal bool CanUseBlocks => Width >= 16 && Height >= 16 && Levels.Count >= OutputLevelCount;
	/// <summary>The format <see cref="Compile"/> writes without a request: BC7 where possible, otherwise RGBA32.</summary>
	internal GltfBasisTargetCodec TargetCodec => CanUseBlocks ? GltfBasisTargetCodec.Bc7 : GltfBasisTargetCodec.Rgba32;
	internal uint SupercompressionScheme { get; private init; }
	internal int OutputLevelCount { get; private init; }
	internal long OutputRgbaBytes { get; private init; }

	internal static GltfBasisTexturePlan Create( GltfImage image, bool isSrgb, bool mipmaps, long maxDecodedBytes = GltfTextureCompiler.DefaultMaxDecodedBytes )
	{
		if ( image?.Bytes is not { Length: >= 104 } bytes || !string.Equals( image.MimeType, "image/ktx2", StringComparison.OrdinalIgnoreCase ) ||
			!bytes.AsSpan( 0, 12 ).SequenceEqual( new byte[] { 0xab, 0x4b, 0x54, 0x58, 0x20, 0x32, 0x30, 0xbb, 13, 10, 26, 10 } ) )
			throw new GltfException( "Expected an image/ktx2 Basis texture with a complete header and level index." );
		if ( maxDecodedBytes <= 0 ) throw new GltfException( "Basis texture decoded-byte budget must be positive." );
		uint U32( int offset ) => BinaryPrimitives.ReadUInt32LittleEndian( bytes.AsSpan( offset, 4 ) );
		ulong U64( int offset ) => BinaryPrimitives.ReadUInt64LittleEndian( bytes.AsSpan( offset, 8 ) );
		bool InBounds( ulong offset, ulong count ) => offset <= (ulong)bytes.Length && count <= (ulong)bytes.Length - offset;
		var width = U32( 20 ); var height = U32( 24 ); var count = U32( 40 ); var compression = U32( 44 );
		if ( U32( 12 ) != 0 || U32( 16 ) != 1 || width == 0 || height == 0 || width > 16384 || height > 16384 || width % 4 != 0 || height % 4 != 0 ||
			U32( 28 ) != 0 || U32( 32 ) != 0 || U32( 36 ) != 1 )
			throw new GltfException( "Basis material texture must be a 2D non-array, single-face universal image with dimensions divisible by four." );
		var fullLevels = 1;
		for ( var size = Math.Max( width, height ); size > 1; size >>= 1 ) fullLevels++;
		if ( count == 0 || count > fullLevels || 80L + 24L * count > bytes.Length ) throw new GltfException( "Invalid Basis texture mip count or level index." );
		var dfdOffset = U32( 48 ); var dfdLength = U32( 52 ); var kvdOffset = U32( 56 ); var kvdLength = U32( 60 );
		if ( dfdLength < 44 || !InBounds( dfdOffset, dfdLength ) || !InBounds( kvdOffset, kvdLength ) || !InBounds( U64( 64 ), U64( 72 ) ) )
			throw new GltfException( "Invalid Basis texture metadata bounds." );
		var dfd = bytes.AsSpan( (int)dfdOffset, (int)dfdLength );
		var blockSize = BinaryPrimitives.ReadUInt16LittleEndian( dfd[10..] );
		if ( BinaryPrimitives.ReadUInt32LittleEndian( dfd ) != dfdLength || BinaryPrimitives.ReadUInt32LittleEndian( dfd[4..] ) != 0 ||
			BinaryPrimitives.ReadUInt16LittleEndian( dfd[8..] ) != 2 || blockSize < 40 || (blockSize - 24) % 16 != 0 || blockSize + 4 > dfd.Length )
			throw new GltfException( "Invalid Basis texture basic data format descriptor." );
		var sourceCodec = dfd[12] switch
		{
			163 when compression == 1 => GltfBasisSourceCodec.Etc1S,
			166 when compression is 0 or 2 => GltfBasisSourceCodec.Uastc,
			_ => throw new GltfException( "Expected ETC1S/BasisLZ or UASTC with optional Zstandard supercompression." )
		};
		if ( dfd[14] != (isSrgb ? 2 : 1) || isSrgb && dfd[13] != 1 || dfd[15] != 0 )
			throw new GltfException( "Basis transfer function, color primaries or straight-alpha metadata disagree with the material texture role." );
		var kvd = bytes.AsSpan( (int)kvdOffset, (int)kvdLength );
		var metadata = ReadMetadata( kvd );
		var orientation = metadata.GetValueOrDefault( "KTXorientation", "rd" ); var swizzle = metadata.GetValueOrDefault( "KTXswizzle", "rgba" );
		if ( orientation != "rd" || swizzle != "rgba" ) throw new GltfException( "KHR_texture_basisu requires rd orientation and rgba swizzle, or omitted metadata." );
		var levels = new Level[(int)count];
		long sourceRgbaBytes = 0; ulong expandedBytes = 0;
		for ( var level = 0; level < levels.Length; level++ )
		{
			var at = 80 + level * 24; var offset = U64( at ); var length = U64( at + 8 ); var expanded = U64( at + 16 );
			var w = Math.Max( 1, (int)width >> level ); var h = Math.Max( 1, (int)height >> level );
			if ( length == 0 || !InBounds( offset, length ) || expanded > (ulong)maxDecodedBytes || expandedBytes > (ulong)maxDecodedBytes - expanded )
				throw new GltfException( "Invalid Basis mip bounds or expansion budget." );
			expandedBytes += expanded; sourceRgbaBytes += (long)w * h * 4;
			levels[level] = new( w, h, offset, length, expanded );
		}
		var outputCount = mipmaps ? fullLevels : 1; long outputBytes = 0;
		for ( var level = 0; level < outputCount; level++ ) outputBytes += (long)Math.Max( 1, (int)width >> level ) * Math.Max( 1, (int)height >> level ) * 4;
		if ( (long)bytes.Length + dfdLength + kvdLength > maxDecodedBytes || sourceRgbaBytes > maxDecodedBytes || outputBytes > maxDecodedBytes || outputBytes > int.MaxValue ) throw new GltfException( "Basis decoded mip chain exceeds the configured byte budget." );
		return new()
		{
			encoded = (byte[])bytes.Clone(),
			budget = maxDecodedBytes,
			DataFormatDescriptor = dfd.ToArray(),
			KeyValueData = kvd.ToArray(),
			Levels = Array.AsReadOnly( levels ),
			Width = (int)width,
			Height = (int)height,
			ColorPrimaries = dfd[13],
			TransferFunction = dfd[14],
			Orientation = orientation,
			Swizzle = swizzle,
			SourceCodec = sourceCodec,
			SupercompressionScheme = compression,
			OutputLevelCount = outputCount,
			OutputRgbaBytes = outputBytes
		};
	}

	/// <summary>Uses the existing pinned native codec, never a second decoder.</summary>
	internal GltfKtxImage DecodeRgba32() => GltfKtx.DecodeRgba32( encoded, budget );

	/// <summary>Retains all supplied authored levels before generating a missing tail.</summary>
	internal IReadOnlyList<GltfKtxLevel> CompleteLevels( GltfKtxImage decoded )
	{
		if ( decoded is null || decoded.Width != Width || decoded.Height != Height || decoded.IsSrgb != IsSrgb || decoded.Levels?.Count != Levels.Count )
			throw new GltfException( "Basis decoder metadata disagrees with the texture plan." );
		for ( var i = 0; i < Levels.Count; i++ )
		{
			var level = decoded.Levels[i];
			if ( level is null || level.Width != Levels[i].Width || level.Height != Levels[i].Height || level.Pixels?.LongLength != (long)level.Width * level.Height * 4 )
				throw new GltfException( "Basis decoded mip shape disagrees with the source descriptor." );
		}
		var result = new GltfKtxLevel[OutputLevelCount];
		for ( var i = 0; i < result.Length; i++ )
		{
			if ( i < decoded.Levels.Count ) result[i] = decoded.Levels[i];
			else
			{
				var previous = result[i - 1];
				result[i] = new GltfKtxLevel
				{
					Width = Math.Max( 1, previous.Width / 2 ),
					Height = Math.Max( 1, previous.Height / 2 ),
					Pixels = GltfTextureCompiler.Downsample( previous.Pixels, previous.Width, previous.Height, IsSrgb )
				};
			}
		}
		return Array.AsReadOnly( result );
	}

	/// <summary>CPU serialization through the established native texture resource format.</summary>
	/// <param name="requested">
	/// The format to write, or null for <see cref="TargetCodec"/>. A block format that this texture cannot use falls
	/// back to RGBA32. BC5 keeps red and green only, so request it only for two-channel data.
	/// </param>
	internal byte[] Compile( GltfBasisTargetCodec? requested = null )
	{
		var target = requested ?? TargetCodec;
		if ( target != GltfBasisTargetCodec.Rgba32 && !CanUseBlocks ) target = GltfBasisTargetCodec.Rgba32;
		if ( target == GltfBasisTargetCodec.Bc5 && IsSrgb ) throw new GltfException( "BC5 holds two channels of data; it cannot store an sRGB colour texture." );
		return target == GltfBasisTargetCodec.Rgba32 ? CompileRgba32() : CompileBlocks( target );
	}

	private byte[] CompileBlocks( GltfBasisTargetCodec target )
	{
		long blockBytes = 0; ulong expandedBytes = 0;
		for ( var i = 0; i < Levels.Count; i++ ) { blockBytes += GltfKtx.BlockLevelBytes( Levels[i].Width, Levels[i].Height ); expandedBytes += Levels[i].UncompressedByteLength; }
		// Bound the native encoded and expanded copies, the transcoded levels, and the output chain with its serialization copies.
		var peak = 2L * encoded.Length + (long)expandedBytes + 3 * blockBytes + 1024 * 1024;
		if ( peak > budget ) throw new GltfException( "Basis texture transcoding and serialization copies exceed the decoded-byte budget." );
		var image = GltfKtx.TranscodeToBlocks( encoded, target == GltfBasisTargetCodec.Bc5 ? GltfKtxBlockFormat.Bc5 : GltfKtxBlockFormat.Bc7, budget );
		if ( image.Width != Width || image.Height != Height || image.IsSrgb != IsSrgb || image.Levels.Count != Levels.Count )
			throw new GltfException( "Basis transcoder metadata disagrees with the texture plan." );
		long chainBytes = 0;
		for ( var i = 0; i < OutputLevelCount; i++ )
		{
			var level = image.Levels[i];
			if ( level.Width != Levels[i].Width || level.Height != Levels[i].Height || level.Data?.LongLength != GltfKtx.BlockLevelBytes( level.Width, level.Height ) )
				throw new GltfException( "Basis transcoded mip shape disagrees with the source descriptor." );
			chainBytes += level.Data.Length;
		}
		// Smallest mip first, like every other texture resource.
		var chain = new byte[checked((int)chainBytes)]; var offset = chain.Length;
		for ( var i = 0; i < OutputLevelCount; i++ ) { var data = image.Levels[i].Data; offset -= data.Length; data.CopyTo( chain, offset ); }
		var writer = new VTexWriter();
		writer.Header.Width = (ushort)Width; writer.Header.Height = (ushort)Height; writer.Header.Depth = 1;
		writer.Header.MipCount = (byte)OutputLevelCount;
		writer.Header.Format = target == GltfBasisTargetCodec.Bc5 ? VTexWriter.VTEX_Format_t.VTEX_FORMAT_BC5 : VTexWriter.VTEX_Format_t.VTEX_FORMAT_BC7;
		// The engine does not stream compressed textures whose mips are not all multiples of four.
		var streams = OutputLevelCount > 1 && IsPowerOfTwo( Width ) && IsPowerOfTwo( Height );
		writer.Header.Flags = (streams ? VTexWriter.VTEX_Flags_t.NONE : VTexWriter.VTEX_Flags_t.VTEX_FLAG_NO_LOD) |
			(IsSrgb ? VTexWriter.VTEX_Flags_t.NONE : VTexWriter.VTEX_Flags_t.VTEX_FLAG_CREATE_LINEAR_API_TEXTURE);
		var resource = new ResourceWriter { ResourceVersion = 1 }; resource.SetDataBlock( writer.GetData() ); resource.SetStreamingData( chain );
		return resource.ToArray();
	}

	private static bool IsPowerOfTwo( int value ) => value > 0 && (value & (value - 1)) == 0;

	private byte[] CompileRgba32()
	{
		long authoredRgbaBytes = 0; ulong expandedBytes = 0;
		foreach ( var level in Levels ) { authoredRgbaBytes += (long)level.Width * level.Height * 4; expandedBytes += level.UncompressedByteLength; }
		// Bound native encoded/expanded/transcoded storage, managed authored/tail
		// levels, serialization stream and final copy; no GPU texture is allocated.
		var peak = 2L * encoded.Length + (long)expandedBytes + 2 * authoredRgbaBytes + 4 * OutputRgbaBytes + 1024 * 1024;
		if ( peak > budget ) throw new GltfException( "Basis texture decoding and serialization copies exceed the decoded-byte budget." );
		var levels = CompleteLevels( DecodeRgba32() );
		var chain = new byte[(int)OutputRgbaBytes]; var offset = chain.Length;
		foreach ( var level in levels ) { offset -= level.Pixels.Length; level.Pixels.CopyTo( chain, offset ); }
		var writer = new VTexWriter();
		writer.Header.Width = (ushort)Width; writer.Header.Height = (ushort)Height; writer.Header.Depth = 1;
		writer.Header.MipCount = (byte)OutputLevelCount; writer.Header.Format = VTexWriter.VTEX_Format_t.VTEX_FORMAT_RGBA8888;
		writer.Header.Flags = (OutputLevelCount > 1 ? VTexWriter.VTEX_Flags_t.NONE : VTexWriter.VTEX_Flags_t.VTEX_FLAG_NO_LOD) |
			(IsSrgb ? VTexWriter.VTEX_Flags_t.NONE : VTexWriter.VTEX_Flags_t.VTEX_FLAG_CREATE_LINEAR_API_TEXTURE);
		var resource = new ResourceWriter { ResourceVersion = 1 }; resource.SetDataBlock( writer.GetData() ); resource.SetStreamingData( chain );
		return resource.ToArray();
	}

	private static Dictionary<string, string> ReadMetadata( ReadOnlySpan<byte> bytes )
	{
		var values = new Dictionary<string, string>( StringComparer.Ordinal );
		for ( var offset = 0; offset < bytes.Length; )
		{
			if ( bytes.Length - offset < 4 ) throw new GltfException( "Truncated KTX key/value length." );
			var size = BinaryPrimitives.ReadUInt32LittleEndian( bytes[offset..] ); offset += 4;
			if ( size < 2 || size > bytes.Length - offset ) throw new GltfException( "Invalid KTX key/value bounds." );
			var pair = bytes.Slice( offset, (int)size ); var separator = pair.IndexOf( (byte)0 );
			if ( separator <= 0 ) throw new GltfException( "KTX metadata key has no terminator." );
			var key = Encoding.UTF8.GetString( pair[..separator] );
			var value = pair[(separator + 1)..]; if ( value.Length > 0 && value[^1] == 0 ) value = value[..^1];
			if ( !values.TryAdd( key, Encoding.UTF8.GetString( value ) ) ) throw new GltfException( "Duplicate KTX metadata key." );
			var padded = ((long)size + 3) & ~3L;
			if ( padded > bytes.Length - offset ) throw new GltfException( "Truncated KTX key/value padding." );
			offset += (int)padded;
		}
		return values;
	}
}
