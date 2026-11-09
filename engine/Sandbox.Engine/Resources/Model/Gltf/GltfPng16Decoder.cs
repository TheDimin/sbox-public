using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using Sandbox.Gltf;

namespace Sandbox;

/// <summary>Bounded raw-sample PNG16 decoder. Embedded color metadata is ignored.</summary>
internal static class GltfPng16Decoder
{
	internal sealed record Image( int Width, int Height, ushort[] Rgba );
	private static readonly int[] StartX = [0, 4, 0, 2, 0, 1, 0], StartY = [0, 0, 4, 0, 2, 0, 1], StepX = [8, 8, 4, 4, 2, 2, 1], StepY = [8, 8, 8, 4, 4, 2, 2];
	internal static Image Decode( byte[] bytes, long budget )
	{
		try { return DecodeCore( bytes, budget ); }
		catch ( GltfException ) { throw; }
		catch ( Exception e ) when ( e is InvalidDataException or EndOfStreamException or IOException or OverflowException or ArgumentException ) { throw new GltfException( "Invalid PNG16: " + e.Message ); }
	}
	private static Image DecodeCore( byte[] bytes, long budget )
	{
		if ( bytes is null || budget <= 0 || bytes.Length < 8 || !bytes.AsSpan( 0, 8 ).SequenceEqual( new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 } ) ) throw new GltfException( "Invalid PNG signature or budget." );
		int offset = 8, width = 0, height = 0, type = -1, interlace = -1, channels = 0;
		bool header = false, idat = false, idatEnded = false, ended = false, transparency = false, palette = false;
		ushort tr = 0, tg = 0, tb = 0;
		using var compressed = new MemoryStream();
		while ( offset < bytes.Length )
		{
			if ( bytes.Length - offset < 12 ) throw new GltfException( "Truncated PNG chunk." );
			uint n = BinaryPrimitives.ReadUInt32BigEndian( bytes.AsSpan( offset, 4 ) );
			if ( n > int.MaxValue || n > bytes.Length - offset - 12 ) throw new GltfException( "Invalid PNG chunk length." );
			int len = (int)n; var name = bytes.AsSpan( offset + 4, 4 ); var payload = bytes.AsSpan( offset + 8, len );
			if ( Crc( bytes.AsSpan( offset + 4, len + 4 ) ) != BinaryPrimitives.ReadUInt32BigEndian( bytes.AsSpan( offset + 8 + len, 4 ) ) ) throw new GltfException( "PNG chunk CRC mismatch." );
			for ( int i = 0; i < 4; i++ ) if ( !(name[i] is >= 65 and <= 90 or >= 97 and <= 122) ) throw new GltfException( "Invalid PNG chunk name." );
			if ( (name[2] & 32) != 0 ) throw new GltfException( "Invalid PNG reserved chunk bit." );
			if ( !header && !name.SequenceEqual( "IHDR"u8 ) ) throw new GltfException( "PNG IHDR must be first." );
			if ( idat && !name.SequenceEqual( "IDAT"u8 ) ) idatEnded = true;
			if ( name.SequenceEqual( "IHDR"u8 ) )
			{
				if ( header || len != 13 ) throw new GltfException( "Invalid IHDR." );
				uint w = BinaryPrimitives.ReadUInt32BigEndian( payload ), h = BinaryPrimitives.ReadUInt32BigEndian( payload[4..] );
				if ( w == 0 || h == 0 || w > 16384 || h > 16384 || payload[8] != 16 || payload[10] != 0 || payload[11] != 0 || payload[12] > 1 ) throw new GltfException( "Invalid PNG16 dimensions or coding." );
				width = (int)w; height = (int)h; type = payload[9]; interlace = payload[12];
				channels = type switch { 0 => 1, 2 => 3, 4 => 2, 6 => 4, _ => throw new GltfException( "Invalid PNG16 color type." ) };
				// The encoded input is retained by the caller; account for both it and
				// a conservative two copies of compressed data plus pixels and rows.
				long peak = checked(3L * bytes.Length + (long)width * height * 8 + (long)width * channels * 4 + 65536);
				if ( peak > budget || (long)width * height * 4 > int.MaxValue ) throw new GltfException( "PNG16 decoded-byte budget exceeded." );
				header = true;
			}
			else if ( name.SequenceEqual( "PLTE"u8 ) )
			{
				if ( palette || idat || type is 0 or 4 || len == 0 || len > 768 || len % 3 != 0 ) throw new GltfException( "Invalid PLTE." );
				palette = true;
			}
			else if ( name.SequenceEqual( "tRNS"u8 ) )
			{
				if ( transparency || idat || type is not (0 or 2) || len != (type == 0 ? 2 : 6) ) throw new GltfException( "Invalid PNG16 transparency." );
				tr = BinaryPrimitives.ReadUInt16BigEndian( payload );
				if ( type == 2 ) { tg = BinaryPrimitives.ReadUInt16BigEndian( payload[2..] ); tb = BinaryPrimitives.ReadUInt16BigEndian( payload[4..] ); }
				transparency = true;
			}
			else if ( name.SequenceEqual( "IDAT"u8 ) )
			{
				if ( idatEnded ) throw new GltfException( "PNG IDAT chunks are not consecutive." );
				compressed.Write( payload ); idat = true;
			}
			else if ( name.SequenceEqual( "IEND"u8 ) )
			{
				if ( len != 0 || !idat || offset + 12 != bytes.Length ) throw new GltfException( "Invalid PNG IEND." );
				ended = true; break;
			}
			else if ( (name[0] & 32) == 0 ) throw new GltfException( "Unknown critical PNG chunk." );
			offset = checked(offset + 12 + len);
		}
		if ( !ended ) throw new GltfException( "PNG IEND missing." );
		var rgba = new ushort[checked(width * height * 4)]; compressed.Position = 0;
		using var z = new ZLibStream( compressed, CompressionMode.Decompress );
		int passes = interlace == 0 ? 1 : 7;
		for ( int pass = 0; pass < passes; pass++ )
		{
			int sx = interlace == 0 ? 0 : StartX[pass], sy = interlace == 0 ? 0 : StartY[pass], dx = interlace == 0 ? 1 : StepX[pass], dy = interlace == 0 ? 1 : StepY[pass];
			int pw = width <= sx ? 0 : (width - sx + dx - 1) / dx, ph = height <= sy ? 0 : (height - sy + dy - 1) / dy;
			if ( pw == 0 || ph == 0 ) continue;
			int bpp = channels * 2; byte[] previous = new byte[checked(pw * bpp)], row = new byte[previous.Length];
			for ( int y = 0; y < ph; y++ )
			{
				int filter = z.ReadByte(); if ( filter < 0 || filter > 4 ) throw new GltfException( "Invalid PNG row filter." );
				z.ReadExactly( row );
				for ( int i = 0; i < row.Length; i++ )
				{
					int left = i >= bpp ? row[i - bpp] : 0, up = previous[i], upperLeft = i >= bpp ? previous[i - bpp] : 0;
					int predictor = filter switch { 0 => 0, 1 => left, 2 => up, 3 => (left + up) / 2, 4 => Paeth( left, up, upperLeft ), _ => 0 };
					row[i] = unchecked((byte)(row[i] + predictor));
				}
				for ( int x = 0; x < pw; x++ )
				{
					int at = x * bpp, dest = ((sy + y * dy) * width + sx + x * dx) * 4;
					ushort a = BinaryPrimitives.ReadUInt16BigEndian( row.AsSpan( at, 2 ) );
					ushort r = a, g = a, b = a, alpha = 65535;
					if ( type is 2 or 6 ) { g = BinaryPrimitives.ReadUInt16BigEndian( row.AsSpan( at + 2, 2 ) ); b = BinaryPrimitives.ReadUInt16BigEndian( row.AsSpan( at + 4, 2 ) ); }
					if ( type is 4 or 6 ) alpha = BinaryPrimitives.ReadUInt16BigEndian( row.AsSpan( at + (channels - 1) * 2, 2 ) );
					else if ( transparency && (type == 0 ? r == tr : r == tr && g == tg && b == tb) ) alpha = 0;
					rgba[dest] = r; rgba[dest + 1] = g; rgba[dest + 2] = b; rgba[dest + 3] = alpha;
				}
				(row, previous) = (previous, row);
			}
		}
		if ( z.ReadByte() != -1 ) throw new GltfException( "PNG decompressed data exceeds dimensions." );
		return new( width, height, rgba );
	}
	private static int Paeth( int a, int b, int c ) { int p = a + b - c, pa = Math.Abs( p - a ), pb = Math.Abs( p - b ), pc = Math.Abs( p - c ); return pa <= pb && pa <= pc ? a : pb <= pc ? b : c; }
	private static uint Crc( ReadOnlySpan<byte> bytes ) { uint c = uint.MaxValue; foreach ( byte b in bytes ) { c ^= b; for ( int i = 0; i < 8; i++ ) c = (c >> 1) ^ ((c & 1) != 0 ? 0xedb88320u : 0); } return ~c; }
}
