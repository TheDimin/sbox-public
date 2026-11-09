using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace Sandbox.Gltf.DirectGpu.BoxTexturedValidation;

using NVector4 = System.Numerics.Vector4;

/// <summary>
/// Bounded independent validation oracle, not a production decoder. Supports
/// noninterlaced 8-bit PNG RGB, RGBA and indexed color with optional tRNS. It
/// deliberately rejects other precision/layouts instead of approximating them.
/// No Skia, Texture, GltfImage or production importer implementation is used.
/// </summary>
public static class PngTexelReference
{
	public sealed record Image( int Width, int Height, int ColorType, byte[] Rgba );
	public static Image Decode( byte[] png, int maxDimension = 4096 )
	{
		Require( png is { Length: >= 33 and <= 16 * 1024 * 1024 } && png.AsSpan( 0, 8 ).SequenceEqual( new byte[] { 137,80,78,71,13,10,26,10 } ), "Invalid bounded PNG signature/length." );
		Require( maxDimension is > 0 and <= 4096, "Invalid PNG oracle dimension budget." );
		var width = 0; var height = 0; var type = -1; var components = 0; var ended = false; var header = false;
		byte[] palette = null, alpha = null; using var compressed = new MemoryStream();
		for ( var offset = 8; offset < png.Length; )
		{
			Require( png.Length - offset >= 12, "Truncated PNG chunk." );
			var size = checked((int)BinaryPrimitives.ReadUInt32BigEndian( png.AsSpan( offset ) )); Require( size >= 0 && size <= png.Length - offset - 12, "Bad PNG chunk extent." );
			var chunk = Encoding.ASCII.GetString( png, offset + 4, 4 ); var data = png.AsSpan( offset + 8, size );
			Require( Crc( png.AsSpan( offset + 4, size + 4 ) ) == BinaryPrimitives.ReadUInt32BigEndian( png.AsSpan( offset + size + 8 ) ), "PNG CRC mismatch." );
			if ( chunk == "IHDR" )
			{
				Require( offset == 8 && !header && size == 13, "Invalid PNG IHDR." ); header = true;
				width = checked((int)BinaryPrimitives.ReadUInt32BigEndian( data )); height = checked((int)BinaryPrimitives.ReadUInt32BigEndian( data[4..] )); type = data[9];
				Require( width > 0 && height > 0 && width <= maxDimension && height <= maxDimension && (long)width * height * 4 <= 32 * 1024 * 1024 && data[8] == 8 && type is 2 or 3 or 6 && data[10] == 0 && data[11] == 0 && data[12] == 0, "Unsupported precision/layout or oversized PNG oracle input." );
				components = type == 2 ? 3 : type == 6 ? 4 : 1;
			}
			else
			{
				Require( header, "PNG data precedes IHDR." );
				if ( chunk == "PLTE" ) palette = data.ToArray();
				else if ( chunk == "tRNS" ) alpha = data.ToArray();
				else if ( chunk == "IDAT" ) compressed.Write( data );
				else if ( chunk == "IEND" ) Require( size == 0, "Invalid PNG IEND." );
				else Require( (png[offset + 4] & 32) != 0, "Unknown critical PNG chunk." );
			}
			offset += size + 12;
			if ( chunk == "IEND" ) { Require( offset == png.Length, "Trailing PNG bytes." ); ended = true; break; }
		}
		Require( header && ended && compressed.Length > 0, "Missing PNG image data/end." );
		if ( type == 3 ) Require( palette is { Length: > 0 and <= 768 } && palette.Length % 3 == 0 && (alpha is null || alpha.Length <= palette.Length / 3), "Invalid palette or palette alpha." );
		if ( type == 2 && alpha is not null ) Require( alpha.Length == 6, "Invalid RGB transparency key." );
		if ( type == 6 ) Require( alpha is null, "RGBA PNG must not contain tRNS." );
		var rowBytes = checked(width * components); var filtered = new byte[checked((rowBytes + 1) * height)];
		compressed.Position = 0; using ( var zlib = new ZLibStream( compressed, CompressionMode.Decompress ) ) { zlib.ReadExactly( filtered ); Require( zlib.ReadByte() == -1, "Extra decompressed PNG rows." ); }
		var decoded = new byte[checked(rowBytes * height)];
		for ( var y = 0; y < height; y++ )
		{
			var filter = filtered[y * (rowBytes + 1)]; Require( filter <= 4, "Unknown PNG row filter." );
			for ( var x = 0; x < rowBytes; x++ )
			{
				var a = x < components ? 0 : decoded[y * rowBytes + x - components]; var b = y == 0 ? 0 : decoded[(y - 1) * rowBytes + x]; var c = x < components || y == 0 ? 0 : decoded[(y - 1) * rowBytes + x - components];
				var prediction = filter switch { 0 => 0, 1 => a, 2 => b, 3 => (a + b) / 2, _ => Paeth( a, b, c ) };
				decoded[y * rowBytes + x] = unchecked((byte)(filtered[y * (rowBytes + 1) + 1 + x] + prediction));
			}
		}
		var rgba = new byte[checked(width * height * 4)];
		for ( var pixel = 0; pixel < width * height; pixel++ )
		{
			if ( type == 3 )
			{
				var index = decoded[pixel]; Require( index * 3 + 2 < palette.Length, "PNG palette index out of range." );
				palette.AsSpan( index * 3, 3 ).CopyTo( rgba.AsSpan( pixel * 4 ) ); rgba[pixel * 4 + 3] = alpha is not null && index < alpha.Length ? alpha[index] : (byte)255;
			}
			else
			{
				decoded.AsSpan( pixel * components, components ).CopyTo( rgba.AsSpan( pixel * 4 ) );
				if ( type == 2 ) rgba[pixel * 4 + 3] = alpha is not null && decoded[pixel * 3] == BinaryPrimitives.ReadUInt16BigEndian( alpha ) && decoded[pixel * 3 + 1] == BinaryPrimitives.ReadUInt16BigEndian( alpha.AsSpan( 2 ) ) && decoded[pixel * 3 + 2] == BinaryPrimitives.ReadUInt16BigEndian( alpha.AsSpan( 4 ) ) ? (byte)0 : (byte)255;
			}
		}
		return new( width, height, type, rgba );
	}

	/// <summary>
	/// Top-left image coordinates; convert sRGB RGB to linear before filtering.
	/// This is an explicit base-level magnification oracle. It makes no assertion
	/// about an unmeasured texture derivative, minification LOD or anisotropy.
	/// </summary>
	public static NVector4 BilinearLinearRgba( byte[] rgba, int width, int height, double u, double v, bool clampToEdge )
	{
		Require( width > 0 && height > 0 && (long)width * height * 4 == rgba.Length && double.IsFinite( u ) && double.IsFinite( v ), "Invalid reference image/UV." );
		if ( clampToEdge ) { u = Math.Clamp( u, 0, 1 ); v = Math.Clamp( v, 0, 1 ); } else { u -= Math.Floor( u ); v -= Math.Floor( v ); }
		var x = u * width - .5; var y = v * height - .5; var ix = (int)Math.Floor( x ); var iy = (int)Math.Floor( y ); var tx = x - ix; var ty = y - iy;
		var value = new double[4];
		for ( var dy = 0; dy <= 1; dy++ ) for ( var dx = 0; dx <= 1; dx++ )
		{
			var sx = clampToEdge ? Math.Clamp( ix + dx, 0, width - 1 ) : ((ix + dx) % width + width) % width;
			var sy = clampToEdge ? Math.Clamp( iy + dy, 0, height - 1 ) : ((iy + dy) % height + height) % height;
			var weight = (dx == 0 ? 1 - tx : tx) * (dy == 0 ? 1 - ty : ty);
			for ( var channel = 0; channel < 4; channel++ ) { var component = rgba[(sy * width + sx) * 4 + channel] / 255.0; value[channel] += (channel == 3 ? component : Linear( component )) * weight; }
		}
		return new( (float)value[0], (float)value[1], (float)value[2], (float)value[3] );
	}

	public static int TestRgbRowFilters() => TestFilters( 3 );
	public static int TestRgbaRowFilters() => TestFilters( 4 );
	public static int TestCrcSensitivity()
	{
		var png = Fixture( 4, out _ ); png[29] ^= 1;
		try { _ = Decode( png ); } catch ( InvalidOperationException error ) when ( error.Message.Contains( "CRC", StringComparison.Ordinal ) ) { return 1; }
		throw new InvalidOperationException( "PNG oracle failed to detect changed chunk CRC." );
	}
	public static int TestBilinearAndClamp()
	{
		byte[] pixels = [0,0,0,0, 255,255,255,255];
		var midpoint = BilinearLinearRgba( pixels, 2, 1, .5, .5, true ); Require( midpoint == new NVector4( .5f ), "sRGB conversion must precede bilinear filtering; alpha remains linear." );
		Require( BilinearLinearRgba( pixels, 2, 1, -.25, .5, true ) == NVector4.Zero && BilinearLinearRgba( pixels, 2, 1, 1.25, .5, true ) == NVector4.One, "Clamp must extend edge texels." );
		Require( BilinearLinearRgba( pixels, 2, 1, -.25, .5, false ) == NVector4.One, "Negative repeat UV must wrap to the opposite texel." ); return 3;
	}
	private static int TestFilters( int components )
	{
		var png = Fixture( components, out var expected ); var image = Decode( png );
		Require( image.Width == 3 && image.Height == 5 && image.Rgba.AsSpan().SequenceEqual( expected ), "Independent row filters must use RGB/RGBA byte widths rather than one-byte palette prediction." ); return 5;
	}
	private static byte[] Fixture( int components, out byte[] expected )
	{
		const int width = 3, height = 5; var rowBytes = width * components; var raw = new byte[rowBytes * height]; expected = new byte[width * height * 4];
		for ( var pixel = 0; pixel < width * height; pixel++ ) for ( var channel = 0; channel < components; channel++ ) raw[pixel * components + channel] = (byte)((pixel * 37 + channel * 53) % 256);
		for ( var pixel = 0; pixel < width * height; pixel++ ) { raw.AsSpan( pixel * components, components ).CopyTo( expected.AsSpan( pixel * 4 ) ); if ( components == 3 ) expected[pixel * 4 + 3] = 255; }
		var filtered = new byte[(rowBytes + 1) * height];
		for ( var y = 0; y < height; y++ )
		{
			filtered[y * (rowBytes + 1)] = (byte)y;
			for ( var x = 0; x < rowBytes; x++ )
			{
				var left = x < components ? 0 : raw[y * rowBytes + x - components]; var above = y == 0 ? 0 : raw[(y - 1) * rowBytes + x]; var diagonal = y == 0 || x < components ? 0 : raw[(y - 1) * rowBytes + x - components];
				var prediction = y switch { 0 => 0, 1 => left, 2 => above, 3 => (left + above) / 2, _ => Paeth( left, above, diagonal ) };
				filtered[y * (rowBytes + 1) + 1 + x] = unchecked((byte)(raw[y * rowBytes + x] - prediction));
			}
		}
		using var output = new MemoryStream(); output.Write( new byte[] { 137,80,78,71,13,10,26,10 } );
		var header = new byte[13]; BinaryPrimitives.WriteInt32BigEndian( header, width ); BinaryPrimitives.WriteInt32BigEndian( header.AsSpan( 4 ), height ); header[8] = 8; header[9] = components == 3 ? (byte)2 : (byte)6; WriteChunk( output, "IHDR", header );
		using var compressed = new MemoryStream(); using ( var zlib = new ZLibStream( compressed, CompressionLevel.Optimal, leaveOpen: true ) ) zlib.Write( filtered );
		WriteChunk( output, "IDAT", compressed.ToArray() ); WriteChunk( output, "IEND", Array.Empty<byte>() ); return output.ToArray();
	}
	private static void WriteChunk( Stream stream, string type, byte[] bytes )
	{
		Span<byte> integer = stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian( integer, bytes.Length ); stream.Write( integer ); var data = Encoding.ASCII.GetBytes( type ).Concat( bytes ).ToArray(); stream.Write( data ); BinaryPrimitives.WriteUInt32BigEndian( integer, Crc( data ) ); stream.Write( integer );
	}
	private static double Linear( double value ) => value <= .04045 ? value / 12.92 : Math.Pow( (value + .055) / 1.055, 2.4 );
	private static int Paeth( int a, int b, int c ) { var p = a + b - c; var da = Math.Abs( p - a ); var db = Math.Abs( p - b ); var dc = Math.Abs( p - c ); return da <= db && da <= dc ? a : db <= dc ? b : c; }
	private static uint Crc( ReadOnlySpan<byte> bytes ) { var crc = uint.MaxValue; foreach ( var value in bytes ) { crc ^= value; for ( var bit = 0; bit < 8; bit++ ) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xedb88320u); } return ~crc; }
	private static void Require( bool condition, string message ) { if ( !condition ) throw new InvalidOperationException( message ); }
}
