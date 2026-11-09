using System.IO;
using System.Buffers.Binary;
using System.Numerics;

namespace Editor.Gltf;

/// <summary>
/// Lossless APNG assembly from native PNG frames: compressed IDAT payloads are copied byte for byte.
/// Verification re-reads the written file and proves every frame payload equals its source IDAT bytes,
/// which is an equal check to decoding both and comparing pixels.
/// </summary>
internal static class GltfApng
{
	static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

	static void Require( bool value, string message ) { if ( !value ) throw new InvalidOperationException( message ); }

	static readonly uint[] CrcTable = Enumerable.Range( 0, 256 ).Select( n =>
	{
		var c = (uint)n;
		for ( var k = 0; k < 8; k++ ) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
		return c;
	} ).ToArray();

	static uint Crc( string kind, ReadOnlySpan<byte> data )
	{
		var c = 0xFFFFFFFFu;
		foreach ( var b in kind ) c = CrcTable[(c ^ (byte)b) & 0xFF] ^ (c >> 8);
		foreach ( var b in data ) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
		return c ^ 0xFFFFFFFFu;
	}

	public static List<(string Kind, byte[] Data)> Chunks( byte[] file )
	{
		Require( file.AsSpan( 0, 8 ).SequenceEqual( Signature ), "Invalid PNG signature" );
		var chunks = new List<(string, byte[])>(); var offset = 8;
		while ( true )
		{
			Require( offset + 8 <= file.Length, "Truncated PNG" );
			var length = (int)BinaryPrimitives.ReadUInt32BigEndian( file.AsSpan( offset ) );
			var kind = System.Text.Encoding.ASCII.GetString( file, offset + 4, 4 );
			Require( length >= 0 && length <= 32 * 1024 * 1024 && offset + 12 + length <= file.Length, "Truncated PNG chunk" );
			var data = file.AsSpan( offset + 8, length ).ToArray();
			Require( Crc( kind, data ) == BinaryPrimitives.ReadUInt32BigEndian( file.AsSpan( offset + 8 + length ) ), "PNG CRC differs" );
			chunks.Add( (kind, data) ); offset += 12 + length;
			if ( kind == "IEND" ) return chunks;
		}
	}

	/// <summary>Exact port of Python Fraction(str(seconds)).limit_denominator(65535).</summary>
	public static (ushort Num, ushort Den) Delay( double seconds )
	{
		var text = seconds.ToString( "R", System.Globalization.CultureInfo.InvariantCulture ).ToLowerInvariant();
		var exponent = 0; var e = text.IndexOf( 'e' );
		if ( e >= 0 ) { exponent = int.Parse( text[(e + 1)..], System.Globalization.CultureInfo.InvariantCulture ); text = text[..e]; }
		var dot = text.IndexOf( '.' );
		if ( dot >= 0 ) { exponent -= text.Length - dot - 1; text = text.Remove( dot, 1 ); }
		BigInteger num = BigInteger.Parse( text, System.Globalization.CultureInfo.InvariantCulture ), den = 1;
		if ( exponent >= 0 ) num *= BigInteger.Pow( 10, exponent ); else den = BigInteger.Pow( 10, -exponent );
		var g = BigInteger.GreatestCommonDivisor( num, den ); num /= g; den /= g;
		const int max = 65535;
		BigInteger rn = num, rd = den;
		if ( den > max )
		{
			BigInteger p0 = 0, q0 = 1, p1 = 1, q1 = 0, n = num, d = den;
			while ( true )
			{
				var a = BigInteger.Divide( n, d );
				var q2 = q0 + a * q1;
				if ( q2 > max ) break;
				(p0, q0, p1, q1) = (p1, q1, p0 + a * p1, q2);
				(n, d) = (d, n - a * d);
			}
			var k = (max - q0) / q1;
			if ( 2 * d * (q0 + k * q1) <= den ) { rn = p1; rd = q1; }
			else { rn = p0 + k * p1; rd = q0 + k * q1; }
		}
		Require( rn > 0 && rn <= max && rd <= max, "Unrepresentable APNG frame delay" );
		return ((ushort)rn, (ushort)rd);
	}

	public static double Write( IReadOnlyList<string> framePaths, IReadOnlyList<string> frameHashes, IReadOnlyList<double> durations, bool loop, string destination )
	{
		Require( framePaths.Count == durations.Count && framePaths.Count > 1 && framePaths.Count <= 2048, "Invalid bounded APNG plan" );
		var delays = durations.Select( Delay ).ToArray();
		var temporary = Path.Combine( Path.GetDirectoryName( destination ), "." + Path.GetFileName( destination ) + ".tmp" );
		var sources = new List<List<byte[]>>();
		byte[] firstHeader = null;
		using ( var output = File.Create( temporary ) )
		{
			output.Write( Signature );
			void Chunk( string kind, ReadOnlySpan<byte> data )
			{
				Span<byte> head = stackalloc byte[8];
				BinaryPrimitives.WriteUInt32BigEndian( head, (uint)data.Length ); System.Text.Encoding.ASCII.GetBytes( kind, head[4..] );
				output.Write( head ); output.Write( data );
				Span<byte> crc = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian( crc, Crc( kind, data ) ); output.Write( crc );
			}
			uint sequence = 0;
			for ( var index = 0; index < framePaths.Count; index++ )
			{
				var file = File.ReadAllBytes( framePaths[index] );
				Require( GltfCorpusProof.Hash( file ) == frameHashes[index], "Captured frame changed before encoding" );
				var chunks = Chunks( file );
				Require( chunks[0].Kind == "IHDR" && chunks[0].Data.Length == 13, "Missing native PNG header" );
				var header = chunks[0].Data;
				if ( index == 0 )
				{
					firstHeader = header; Chunk( "IHDR", header );
					var actl = new byte[8]; BinaryPrimitives.WriteUInt32BigEndian( actl, (uint)framePaths.Count ); BinaryPrimitives.WriteUInt32BigEndian( actl.AsSpan( 4 ), loop ? 0u : 1u );
					Chunk( "acTL", actl );
				}
				Require( header.AsSpan().SequenceEqual( firstHeader ), "Animation capture formats/extents differ" );
				var fctl = new byte[26];
				BinaryPrimitives.WriteUInt32BigEndian( fctl, sequence++ );
				header.AsSpan( 0, 8 ).CopyTo( fctl.AsSpan( 4 ) );
				BinaryPrimitives.WriteUInt16BigEndian( fctl.AsSpan( 20 ), delays[index].Num ); BinaryPrimitives.WriteUInt16BigEndian( fctl.AsSpan( 22 ), delays[index].Den );
				Chunk( "fcTL", fctl );
				var idat = new List<byte[]>();
				foreach ( var (kind, data) in chunks.Skip( 1 ) )
				{
					if ( kind == "IDAT" )
					{
						idat.Add( data );
						if ( index == 0 ) Chunk( kind, data );
						else
						{
							var fdat = new byte[data.Length + 4]; BinaryPrimitives.WriteUInt32BigEndian( fdat, sequence++ ); data.CopyTo( fdat, 4 );
							Chunk( "fdAT", fdat );
						}
					}
					else if ( index == 0 && kind is not ("IEND" or "acTL" or "fcTL" or "fdAT") ) Chunk( kind, data );
				}
				Require( idat.Count > 0, "Native PNG frame has no image data" );
				sources.Add( idat );
			}
			Chunk( "IEND", ReadOnlySpan<byte>.Empty );
		}
		Verify( temporary, sources, delays, loop );
		File.Move( temporary, destination, true );
		return delays.Sum( d => (double)d.Num / d.Den );
	}

	static void Verify( string path, List<List<byte[]>> sources, (ushort Num, ushort Den)[] delays, bool loop )
	{
		var chunks = Chunks( File.ReadAllBytes( path ) );
		var actl = chunks.Single( c => c.Kind == "acTL" ).Data;
		Require( BinaryPrimitives.ReadUInt32BigEndian( actl ) == sources.Count && BinaryPrimitives.ReadUInt32BigEndian( actl.AsSpan( 4 ) ) == (loop ? 0u : 1u ), "Encoded frame inventory differs" );
		var frames = new List<List<byte[]>>(); uint expected = 0; var frame = -1;
		foreach ( var (kind, data) in chunks )
		{
			if ( kind == "fcTL" )
			{
				Require( BinaryPrimitives.ReadUInt32BigEndian( data ) == expected++, "APNG sequence differs" );
				frame++; frames.Add( new List<byte[]>() );
				Require( BinaryPrimitives.ReadUInt16BigEndian( data.AsSpan( 20 ) ) == delays[frame].Num && BinaryPrimitives.ReadUInt16BigEndian( data.AsSpan( 22 ) ) == delays[frame].Den, "Encoded APNG delays differ" );
			}
			else if ( kind == "IDAT" ) { Require( frame == 0, "IDAT outside the first frame" ); frames[0].Add( data ); }
			else if ( kind == "fdAT" )
			{
				Require( frame > 0 && BinaryPrimitives.ReadUInt32BigEndian( data ) == expected++, "APNG fdAT sequence differs" );
				frames[frame].Add( data[4..] );
			}
		}
		Require( frames.Count == sources.Count, "Encoded frame inventory differs" );
		for ( var index = 0; index < frames.Count; index++ )
			Require( frames[index].Count == sources[index].Count && frames[index].Zip( sources[index], ( a, b ) => a.AsSpan().SequenceEqual( b ) ).All( x => x ), "APNG pixels differ from native capture" );
	}
}
