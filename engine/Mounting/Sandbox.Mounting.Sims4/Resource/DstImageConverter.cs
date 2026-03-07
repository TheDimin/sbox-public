using System;
using System.Buffers.Binary;
using Sandbox.Diagnostics;

namespace Sandbox.Mounting.Sims4;

static class DstImageConverter
{
	private static readonly Logger Log = new Logger( "Sims4-DstImage" );

	private const uint DdsMagic = 0x20534444;
	private const uint FourCcDst1 = 0x31545344;
	private const uint FourCcDst3 = 0x33545344;
	private const uint FourCcDst5 = 0x35545344;
	private const uint FourCcDxt1 = 0x31545844;
	private const uint FourCcDxt3 = 0x33545844;
	private const uint FourCcDxt5 = 0x35545844;

	public static byte[] ConvertToDxtIfDst( byte[] ddsBytes )
	{
		if ( ddsBytes.Length < 128 )
			return ddsBytes;

		var span = new ReadOnlySpan<byte>( ddsBytes );
		if ( BinaryPrimitives.ReadUInt32LittleEndian( span ) != DdsMagic )
			return ddsBytes;

		var fourCc = BinaryPrimitives.ReadUInt32LittleEndian( span.Slice( 84, 4 ) );
		return fourCc switch
		{
			FourCcDst1 => UnshuffleDst1( ddsBytes ),
			FourCcDst5 => UnshuffleDst5( ddsBytes ),
			FourCcDst3 => UnshuffleDst3( ddsBytes ),
			_ => ddsBytes,
		};
	}

	private static byte[] UnshuffleDst1( byte[] dst )
	{
		var output = (byte[])dst.Clone();
		BinaryPrimitives.WriteUInt32LittleEndian( new Span<byte>( output, 84, 4 ), FourCcDxt1 );

		int dataSize = dst.Length - 128;
		if ( dataSize <= 0 )
			return output;

		var source = new ReadOnlySpan<byte>( dst, 128, dataSize );
		var target = new Span<byte>( output, 128, dataSize );

		int offsetA = 0;
		int offsetB = dataSize >> 1;
		int dstOffset = 0;
		int blockCount = dataSize / 8;

		for ( int i = 0; i < blockCount; i++ )
		{
			source.Slice( offsetA, 4 ).CopyTo( target.Slice( dstOffset, 4 ) );
			source.Slice( offsetB, 4 ).CopyTo( target.Slice( dstOffset + 4, 4 ) );
			offsetA += 4;
			offsetB += 4;
			dstOffset += 8;
		}

		return output;
	}

	private static byte[] UnshuffleDst3( byte[] dst )
	{
		// DST3 samples are rare. Keep loading with a corrected FourCC to avoid hard failure.
		var output = (byte[])dst.Clone();
		BinaryPrimitives.WriteUInt32LittleEndian( new Span<byte>( output, 84, 4 ), FourCcDxt3 );
		Log.Warning( "DST3 texture encountered; using passthrough conversion (possible color mismatch)." );
		return output;
	}

	private static byte[] UnshuffleDst5( byte[] dst )
	{
		var output = (byte[])dst.Clone();
		BinaryPrimitives.WriteUInt32LittleEndian( new Span<byte>( output, 84, 4 ), FourCcDxt5 );

		int dataSize = dst.Length - 128;
		if ( dataSize <= 0 )
			return output;

		var source = new ReadOnlySpan<byte>( dst, 128, dataSize );
		var target = new Span<byte>( output, 128, dataSize );

		int offset0 = 0;
		int offset2 = dataSize >> 3;
		int offset1 = offset2 + (dataSize >> 2);
		int offset3 = offset1 + ((6 * dataSize) >> 4);
		int dstOffset = 0;
		int blockCount = dataSize / 16;

		for ( int i = 0; i < blockCount; i++ )
		{
			source.Slice( offset0, 2 ).CopyTo( target.Slice( dstOffset, 2 ) );
			source.Slice( offset1, 6 ).CopyTo( target.Slice( dstOffset + 2, 6 ) );
			source.Slice( offset2, 4 ).CopyTo( target.Slice( dstOffset + 8, 4 ) );
			source.Slice( offset3, 4 ).CopyTo( target.Slice( dstOffset + 12, 4 ) );

			offset0 += 2;
			offset1 += 6;
			offset2 += 4;
			offset3 += 4;
			dstOffset += 16;
		}

		return output;
	}
}
