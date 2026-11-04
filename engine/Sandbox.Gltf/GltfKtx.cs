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

public static class GltfKtx
{
	public static unsafe GltfKtxImage DecodeRgba32( byte[] encoded, long maxDecodedBytes = 256L * 1024 * 1024 )
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
			var bytes = (ulong)w * h * 4;
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
				result = Evergine.Bindings.KTX.KTX.ktxTexture2_TranscodeBasis( texture, Evergine.Bindings.KTX.ktx_transcode_fmt_e.KTX_TTF_RGBA32, 0 );
				if ( result != Evergine.Bindings.KTX.ktx_error_code_e.KTX_SUCCESS ) throw new GltfException( $"KTX2 Basis transcode failed: {result}." );
				var data = Evergine.Bindings.KTX.KTX.ktxTexture_GetData( (Evergine.Bindings.KTX.ktxTexture*)texture );
				var dataSize = (ulong)Evergine.Bindings.KTX.KTX.ktxTexture_GetDataSize( (Evergine.Bindings.KTX.ktxTexture*)texture );
				if ( data == null || dataSize > (ulong)maxDecodedBytes ) throw new GltfException( "Invalid KTX2 transcoded data size." );
				var levels = new GltfKtxLevel[(int)levelCount];
				for ( var level = 0; level < levels.Length; level++ )
				{
					var w = Math.Max( 1u, width >> level ); var h = Math.Max( 1u, height >> level );
					var byteCount = (int)((ulong)w * h * 4);
					nuint offset = 0;
					result = Evergine.Bindings.KTX.KTX.ktxTexture2_GetImageOffset( texture, (uint)level, 0, 0, &offset );
					if ( result != Evergine.Bindings.KTX.ktx_error_code_e.KTX_SUCCESS || (ulong)offset > dataSize || (ulong)byteCount > dataSize - (ulong)offset ) throw new GltfException( "Invalid KTX2 transcoded mip bounds." );
					var pixels = new byte[byteCount];
					System.Runtime.InteropServices.Marshal.Copy( (IntPtr)(data + (nint)offset), pixels, 0, byteCount );
					levels[level] = new GltfKtxLevel { Width = (int)w, Height = (int)h, Pixels = pixels };
				}
				return new GltfKtxImage { Width = (int)width, Height = (int)height, IsSrgb = transfer == 2, Levels = levels };
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
