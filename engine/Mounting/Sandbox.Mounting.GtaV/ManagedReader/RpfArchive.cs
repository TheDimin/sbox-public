using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace GtaV.ManagedReader;

// RPF7 layout derived from ragekit. See ThirdPartyNotices.txt.
internal sealed class RpfArchive
{
	const uint Magic = 0x52504637;
	const int HeaderSize = 16;
	const int EntrySize = 16;
	const int BlockSize = 512;
	const uint ExtendedSize = 0xFFFFFF;

	internal struct Entry
	{
		internal string Name;
		internal int Parent;
		internal uint Offset, Stored, Uncompressed, SystemFlags, GraphicsFlags;
		internal bool IsDirectory, IsResource, Encrypted;

		internal readonly bool IsArchive => !IsDirectory && !IsResource && Name.EndsWith( ".rpf", StringComparison.OrdinalIgnoreCase );
		internal readonly uint StoredLength => Stored == 0 && !IsResource ? Uncompressed : Stored;
		internal readonly uint Size => IsResource ? (uint)Math.Min( RscResource.PageSize( SystemFlags ) + RscResource.PageSize( GraphicsFlags ), uint.MaxValue ) : Uncompressed == 0 ? Stored : Uncompressed;
	}

	readonly SafeFileHandle file;
	readonly long start, length;
	readonly RpfKeys keys;
	readonly uint encryption;
	readonly Entry[] entries;
	readonly string[] directoryPaths;

	internal string Name { get; }
	internal ReadOnlySpan<Entry> Entries => entries;

	// The caller owns the handle. Nested archives share it and carry their own checked byte range.
	internal RpfArchive( SafeFileHandle file, long start, long length, string name, RpfKeys keys )
	{
		this.file = file;
		this.start = start;
		this.length = length;
		this.keys = keys;
		Name = name;

		Span<byte> header = stackalloc byte[HeaderSize];
		ReadAt( 0, header );
		if ( Word( header ) != Magic ) throw Error( "Invalid RPF7 header." );
		uint count = Word( header[4..] );
		uint namesLength = Word( header[8..] );
		encryption = Word( header[12..] );
		long tableSize = (long)count * EntrySize + namesLength;
		if ( count == 0 || tableSize > Array.MaxLength || HeaderSize + tableSize > length ) throw Error( "Invalid RPF directory size." );

		var storage = ArrayPool<byte>.Shared.Rent( (int)tableSize );
		try
		{
			var buffers = storage.AsSpan( 0, (int)tableSize );
			ReadAt( HeaderSize, buffers );
			var table = buffers[..((int)count * EntrySize)];
			var names = buffers[table.Length..];
			keys.Decrypt( encryption, table, Name, unchecked((uint)length) );
			keys.Decrypt( encryption, names, Name, unchecked((uint)length) );

			entries = new Entry[count];
			directoryPaths = new string[count];
			ReadEntries( table, names );
			BuildDirectoryTree( table );
		}
		finally { ArrayPool<byte>.Shared.Return( storage ); }
	}

	void ReadEntries( ReadOnlySpan<byte> table, ReadOnlySpan<byte> names )
	{
		// Several entries can refer to the same name. Decode each offset once.
		var decodedNames = new System.Collections.Generic.Dictionary<uint, string>();
		for ( int i = 0; i < entries.Length; i++ )
		{
			var row = table.Slice( i * EntrySize, EntrySize );
			uint kind = Word( row[4..] );
			ref var entry = ref entries[i];
			entry.Parent = -1;
			entry.IsDirectory = kind == 0x7FFFFF00;
			var nameOffset = entry.IsDirectory ? Word( row ) : BinaryPrimitives.ReadUInt16LittleEndian( row );
			if ( !decodedNames.TryGetValue( nameOffset, out entry.Name ) )
			{
				if ( nameOffset >= names.Length ) throw Error( $"Invalid name offset in entry {i}." );
				var text = names[(int)nameOffset..];
				int end = text.IndexOf( (byte)0 );
				int size = Math.Min( end < 0 ? text.Length : end, 256 );
				decodedNames.Add( nameOffset, entry.Name = Encoding.UTF8.GetString( text[..size] ) );
			}
			if ( entry.IsDirectory ) continue;

			entry.Stored = Word( row[2..] ) & ExtendedSize;
			entry.Offset = (kind >> 8) & 0x7FFFFF;
			entry.IsResource = (kind & 0x80000000) != 0;
			if ( entry.IsResource )
			{
				entry.SystemFlags = Word( row[8..] );
				entry.GraphicsFlags = Word( row[12..] );
				entry.Encrypted = entry.Name.EndsWith( ".ysc", StringComparison.OrdinalIgnoreCase );
			}
			else
			{
				var encrypted = Word( row[12..] );
				if ( encrypted > 1 ) throw Error( $"Invalid encryption flag in entry {i}." );
				entry.Uncompressed = Word( row[8..] );
				entry.Encrypted = encrypted == 1;
			}
		}
	}

	void BuildDirectoryTree( ReadOnlySpan<byte> table )
	{
		if ( !entries[0].IsDirectory ) throw Error( "Missing RPF root directory." );
		directoryPaths[0] = string.Empty;
		var pending = new System.Collections.Generic.Stack<int>();
		pending.Push( 0 );
		while ( pending.TryPop( out int directory ) )
		{
			var row = table.Slice( directory * EntrySize, EntrySize );
			uint first = Word( row[8..] );
			uint count = Word( row[12..] );
			if ( (ulong)first + count > (uint)entries.Length ) throw Error( $"Invalid child range in directory {directory}." );
			for ( uint i = first; i < first + count; i++ )
			{
				if ( i == 0 || entries[i].Parent >= 0 ) continue;
				ref var entry = ref entries[i];
				entry.Parent = directory;
				if ( !entry.IsDirectory ) continue;
				directoryPaths[i] = JoinPath( directoryPaths[directory], entry.Name );
				pending.Push( (int)i );
			}
		}
		for ( int i = 1; i < entries.Length; i++ )
			if ( entries[i].Parent < 0 ) throw Error( $"Entry {i} is outside the directory tree." );
	}

	internal string DirectoryOf( in Entry entry ) => directoryPaths[entry.Parent];

	internal RpfArchive OpenChild( int index )
	{
		ref readonly var entry = ref entries[index];
		if ( !entry.IsArchive ) throw Error( $"{entry.Name} is not an archive." );
		long offset = (long)entry.Offset * BlockSize;
		CheckRange( offset, entry.StoredLength );
		return new RpfArchive( file, start + offset, entry.StoredLength, entry.Name, keys );
	}

	internal byte[] Extract( int index )
	{
		ref readonly var entry = ref entries[index];
		if ( entry.IsDirectory ) throw Error( $"{entry.Name} is a directory." );
		long offset = (long)entry.Offset * BlockSize;
		uint size = ResolveStoredSize( entry, offset );
		if ( size == 0 ) return [];
		CheckRange( offset, size );
		if ( size > Array.MaxLength || (entry.IsResource && size < HeaderSize) ) throw Error( $"{entry.Name} has an invalid stored size." );

		var bytes = GC.AllocateUninitializedArray<byte>( (int)size );
		ReadAt( offset, bytes );
		var body = entry.IsResource ? bytes.AsSpan( HeaderSize ) : bytes.AsSpan();
		if ( entry.Encrypted ) keys.Decrypt( encryption == RpfKeys.AesMode ? encryption : RpfKeys.NgMode, body, entry.Name, entry.IsResource ? size : entry.Uncompressed );
		if ( entry.IsResource )
		{
			BinaryPrimitives.WriteUInt32LittleEndian( bytes, RscResource.Magic );
			BinaryPrimitives.WriteUInt32LittleEndian( bytes.AsSpan( 4 ), (entry.SystemFlags >> 28) << 4 | entry.GraphicsFlags >> 28 );
			BinaryPrimitives.WriteUInt32LittleEndian( bytes.AsSpan( 8 ), entry.SystemFlags );
			BinaryPrimitives.WriteUInt32LittleEndian( bytes.AsSpan( 12 ), entry.GraphicsFlags );
			return bytes;
		}

		if ( entry.Stored == 0 ) return bytes;
		if ( entry.Uncompressed > Array.MaxLength || entry.Uncompressed > (long)size * 1032 + 65536 )
			throw Error( $"{entry.Name} has an invalid expanded size." );
		using var source = new MemoryStream( bytes, writable: false );
		using var decoder = new DeflateStream( source, CompressionMode.Decompress );
		var result = GC.AllocateUninitializedArray<byte>( (int)entry.Uncompressed );
		decoder.ReadExactly( result );
		if ( decoder.ReadByte() != -1 ) throw Error( $"{entry.Name} expands past its declared size." );
		return result;
	}

	internal Stream OpenResourceStream( int index, out RscHeader header )
	{
		ref readonly var entry = ref entries[index];
		if ( !entry.IsResource || entry.Encrypted )
		{
			var bytes = Extract( index );
			header = RscHeader.Read( bytes );
			return new MemoryStream( bytes, RscResource.HeaderSize, bytes.Length - RscResource.HeaderSize, writable: false );
		}

		long offset = (long)entry.Offset * BlockSize;
		uint size = ResolveStoredSize( entry, offset );
		CheckRange( offset, size );
		if ( size < HeaderSize ) throw Error( $"{entry.Name} has an invalid stored size." );
		header = new RscHeader( (int)((entry.SystemFlags >> 28) << 4 | entry.GraphicsFlags >> 28),
			RscResource.PageSize( entry.SystemFlags ), RscResource.PageSize( entry.GraphicsFlags ) );
		return new BufferedStream( new RpfRegionStream( file, start + offset + HeaderSize, size - HeaderSize ), 64 * 1024 );
	}

	uint ResolveStoredSize( in Entry entry, long offset )
	{
		uint size = entry.StoredLength;
		if ( !entry.IsResource || size != ExtendedSize ) return size;
		Span<byte> header = stackalloc byte[HeaderSize];
		ReadAt( offset, header );
		return (uint)(header[7] | header[14] << 8 | header[5] << 16 | header[2] << 24);
	}

	void ReadAt( long offset, Span<byte> bytes )
	{
		CheckRange( offset, bytes.Length );
		while ( !bytes.IsEmpty )
		{
			int count = RandomAccess.Read( file, bytes, start + offset );
			if ( count == 0 ) throw new EndOfStreamException( $"{Name}: archive was truncated." );
			bytes = bytes[count..];
			offset += count;
		}
	}

	void CheckRange( long offset, long count )
	{
		if ( offset < 0 || offset > length || count > length - offset ) throw Error( "Read extends past the archive." );
	}

	InvalidDataException Error( string message ) => new( $"{Name}: {message}" );
	static uint Word( ReadOnlySpan<byte> bytes ) => BinaryPrimitives.ReadUInt32LittleEndian( bytes );
	internal static string JoinPath( string left, string right ) => string.IsNullOrEmpty( left ) ? right : $"{left}/{right}";
}
