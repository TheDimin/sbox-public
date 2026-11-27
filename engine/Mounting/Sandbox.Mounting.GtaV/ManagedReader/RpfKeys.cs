using System;
using System.Buffers.Binary;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

namespace GtaV.ManagedReader;

// RPF encryption formats derived from ragekit. See ThirdPartyNotices.txt.
internal sealed class RpfKeys
{
	internal const uint Open = 0x4E45504F;
	internal const uint AesMode = 0x0FFFFFF9;
	internal const uint NgMode = 0x0FEFFFFF;
	const int NgKeyCount = 101;
	const int NgRounds = 17;
	const int NgKeySize = NgRounds * 16;
	readonly byte[] aes;
	readonly uint[] ngKeys, ngTables;
	readonly byte[] nameTable;

	internal RpfKeys( string directory )
	{
		aes = Read( directory, "gtav_aes_key.dat", 32 );
		ngKeys = Words( Read( directory, "gtav_ng_key.dat", NgKeyCount * NgKeySize ) );
		ngTables = Words( Read( directory, "gtav_ng_decrypt_tables.dat", NgRounds * 16 * 256 * sizeof(uint) ) );
		nameTable = Read( directory, "gtav_hash_lut.dat", 256 );
	}

	internal static string DefaultDirectory => Path.Combine( Environment.GetFolderPath( Environment.SpecialFolder.UserProfile ), ".ragekit", "keys" );

	internal void Decrypt( uint mode, Span<byte> data, string name, uint length )
	{
		var blocks = data[..(data.Length & ~15)];
		if ( blocks.IsEmpty || mode is 0 or Open ) return;
		if ( mode == AesMode )
		{
			if ( aes.Length == 0 ) throw new InvalidDataException( $"{name}: configured RPF AES key is unavailable." );
			using var cipher = Aes.Create();
			cipher.Key = aes;
			cipher.DecryptEcb( blocks, blocks, PaddingMode.None );
			return;
		}

		if ( ngKeys.Length == 0 || ngTables.Length == 0 || nameTable.Length == 0 )
			throw new InvalidDataException( $"{name}: configured RPF NG keys or tables are unavailable." );

		// The original name's bytes and the entry length select a key. Leave any trailing partial block in place.
		uint hash = 0;
		foreach ( var value in Encoding.UTF8.GetBytes( name ) )
		{
			uint mixed = unchecked(1025u * (nameTable[value] + hash));
			hash = (mixed >> 6) ^ mixed;
		}
		hash = unchecked(hash * 9);
		hash = unchecked(32769u * ((hash >> 11) ^ hash));
		int key = (int)(unchecked(hash + length + 61) % NgKeyCount) * (NgKeySize / sizeof(uint));
		for ( int offset = 0; offset < blocks.Length; offset += 16 ) DecodeBlock( blocks.Slice( offset, 16 ), ngKeys.AsSpan( key ), ngTables );
	}

	static byte[] Read( string directory, string name, int size )
	{
		try
		{
			using var file = File.OpenRead( Path.Combine( directory, name ) );
			if ( file.Length != size ) throw new InvalidDataException( $"{name}: invalid RPF key file size." );
			var bytes = new byte[size];
			file.ReadExactly( bytes );
			return bytes;
		}
		catch ( FileNotFoundException ) { return []; }
		catch ( DirectoryNotFoundException ) { return []; }
	}

	static uint[] Words( byte[] bytes )
	{
		var words = new uint[bytes.Length / sizeof(uint)];
		for ( int i = 0; i < words.Length; i++ ) words[i] = BinaryPrimitives.ReadUInt32LittleEndian( bytes.AsSpan( i * sizeof(uint) ) );
		return words;
	}

	[MethodImpl( MethodImplOptions.AggressiveOptimization )]
	static void DecodeBlock( Span<byte> block, ReadOnlySpan<uint> key, ReadOnlySpan<uint> tables )
	{
		uint a = BinaryPrimitives.ReadUInt32LittleEndian( block );
		uint b = BinaryPrimitives.ReadUInt32LittleEndian( block[4..] );
		uint c = BinaryPrimitives.ReadUInt32LittleEndian( block[8..] );
		uint d = BinaryPrimitives.ReadUInt32LittleEndian( block[12..] );
		for ( int round = 0; round < NgRounds; round++ )
		{
			int row = round * 4096;
			int word = round * 4;
			(a, b, c, d) = round is 0 or 1 or 16
				? (Linear( tables, row, a ) ^ key[word], Linear( tables, row + 1024, b ) ^ key[word + 1],
					Linear( tables, row + 2048, c ) ^ key[word + 2], Linear( tables, row + 3072, d ) ^ key[word + 3])
				: (tables[row + (byte)a] ^ tables[row + 1792 + (byte)(b >> 24)] ^ tables[row + 2560 + (byte)(c >> 16)] ^ tables[row + 3328 + (byte)(d >> 8)] ^ key[word],
					tables[row + 256 + (byte)(a >> 8)] ^ tables[row + 1024 + (byte)b] ^ tables[row + 2816 + (byte)(c >> 24)] ^ tables[row + 3584 + (byte)(d >> 16)] ^ key[word + 1],
					tables[row + 512 + (byte)(a >> 16)] ^ tables[row + 1280 + (byte)(b >> 8)] ^ tables[row + 2048 + (byte)c] ^ tables[row + 3840 + (byte)(d >> 24)] ^ key[word + 2],
					tables[row + 768 + (byte)(a >> 24)] ^ tables[row + 1536 + (byte)(b >> 16)] ^ tables[row + 2304 + (byte)(c >> 8)] ^ tables[row + 3072 + (byte)d] ^ key[word + 3]);
		}
		BinaryPrimitives.WriteUInt32LittleEndian( block, a );
		BinaryPrimitives.WriteUInt32LittleEndian( block[4..], b );
		BinaryPrimitives.WriteUInt32LittleEndian( block[8..], c );
		BinaryPrimitives.WriteUInt32LittleEndian( block[12..], d );
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	static uint Linear( ReadOnlySpan<uint> tables, int row, uint word ) =>
		tables[row + (byte)word] ^ tables[row + 256 + (byte)(word >> 8)] ^ tables[row + 512 + (byte)(word >> 16)] ^ tables[row + 768 + (byte)(word >> 24)];
}
