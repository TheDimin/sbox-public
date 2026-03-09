// ============================================================================
// DbpfPackage — The primary API for opening and reading Sims 4 .package files.
//
// Design principles:
//   • Memory-mapped I/O — the OS pages data on demand, no manual buffering.
//   • Zero index-parsing allocations — entries array is allocated once.
//   • Lazy decompression — compressed resources are inflated on demand.
//   • ArrayPool-backed decompression buffers — returned via IDisposable handle.
//   • Span-based resource readers — callers never see raw byte arrays.
// ============================================================================

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.IO.MemoryMappedFiles;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Sims4.Dbpf.Enums;
using Sims4.Dbpf.Readers;
using Sims4.Dbpf.Structures;
using ResourceType = Sims4.Dbpf.Enums.ResourceType;

namespace Sims4.Dbpf;

/// <summary>
/// Represents an open DBPF 2.1 .package file. Dispose to release the memory map.
/// </summary>
public sealed unsafe class DbpfPackage : IDisposable
{
	private readonly MemoryMappedFile _mmf;
	private readonly MemoryMappedViewAccessor _accessor;
	private readonly byte* _basePtr;
	private readonly long _fileLength;

	/// <summary>The 96-byte file header.</summary>
	public readonly DbpfHeader Header;

	/// <summary>All resolved index entries.</summary>
	public readonly DbpfEntry[] Entries;

	// Lookup by TGI key
	private readonly Dictionary<ResourceKey, int> _keyIndex;

	private Dictionary<ulong, string> _stblCache = null;

	private DbpfPackage(
		MemoryMappedFile mmf,
		MemoryMappedViewAccessor accessor,
		byte* basePtr,
		long fileLength,
		DbpfHeader header,
		DbpfEntry[] entries,
		Dictionary<ResourceKey, int> keyIndex )
	{
		_mmf = mmf;
		_accessor = accessor;
		_basePtr = basePtr;
		_fileLength = fileLength;
		Header = header;
		Entries = entries;
		_keyIndex = keyIndex;
	}

	/// <summary>Opens a .package file with memory-mapped access.</summary>
	public static DbpfPackage Open( string path )
	{
		var fileInfo = new FileInfo( path );
		if ( !fileInfo.Exists ) throw new FileNotFoundException( "Package file not found.", path );
		long fileLength = fileInfo.Length;

		var mmf = MemoryMappedFile.CreateFromFile( path, FileMode.Open, null, 0, MemoryMappedFileAccess.Read );
		var accessor = mmf.CreateViewAccessor( 0, fileLength, MemoryMappedFileAccess.Read );

		unsafe
		{
			byte* ptr = null;
			accessor.SafeMemoryMappedViewHandle.AcquirePointer( ref ptr );

			try
			{
				var fileSpan = new ReadOnlySpan<byte>( ptr, (int)Math.Min( fileLength, int.MaxValue ) );

				// Read and validate header
				var header = MemoryMarshal.Read<DbpfHeader>( fileSpan );
				if ( !header.IsValid )
				{
					accessor.SafeMemoryMappedViewHandle.ReleasePointer();
					accessor.Dispose();
					mmf.Dispose();
					throw new InvalidDataException( "Not a valid DBPF file (bad magic)." );
				}

				// Parse index
				int entryCount = (int)header.IndexEntryCount;
				var entries = new DbpfEntry[entryCount];
				var indexSpan = fileSpan.Slice( (int)header.IndexOffset, (int)header.IndexSize );
				DbpfIndexReader.Read( indexSpan, entryCount, entries );

				// Build lookup dictionary
				var keyIndex = new Dictionary<ResourceKey, int>( entryCount );
				for ( int i = 0; i < entryCount; i++ )
					keyIndex.TryAdd( entries[i].Key, i );

				return new DbpfPackage( mmf, accessor, ptr, fileLength, header, entries, keyIndex );
			}
			catch
			{
				accessor.SafeMemoryMappedViewHandle.ReleasePointer();
				accessor.Dispose();
				mmf.Dispose();
				throw;
			}
		}
	}

	// ---- Raw Data Access ----------------------------------------------------

	/// <summary>
	/// Returns a read-only span over the raw (possibly compressed) on-disk data for an entry.
	/// Zero-copy — points directly into the memory-mapped file.
	/// </summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public unsafe ReadOnlySpan<byte> GetRawData( in DbpfEntry entry )
	{
		int offset = (int)entry.ChunkOffset;
		int size = (int)entry.CompressedSize;
		return new ReadOnlySpan<byte>( _basePtr + offset, size );
	}

	/// <summary>
	/// Returns a read-only span over the raw data, but capped to the file boundary.
	/// </summary>
	public unsafe ReadOnlySpan<byte> GetRawDataSafe( in DbpfEntry entry )
	{
		long offset = entry.ChunkOffset;
		long size = entry.CompressedSize;
		if ( offset + size > _fileLength )
			size = _fileLength - offset;
		if ( size <= 0 ) return ReadOnlySpan<byte>.Empty;
		return new ReadOnlySpan<byte>( _basePtr + offset, (int)size );
	}

	// ---- Decompression ------------------------------------------------------

	/// <summary>
	/// Gets decompressed resource data. If the entry is uncompressed, returns a zero-copy
	/// span from the memory map wrapped in a no-op handle. If compressed, decompresses
	/// into a pooled buffer and returns an <see cref="ResourceData"/> that must be disposed.
	/// </summary>
	internal ResourceData GetResourceData( in DbpfEntry entry )
	{
		if ( !entry.IsCompressed )
		{
			// Uncompressed — zero-copy from the memory map
			return new ResourceData( GetRawData( in entry ), null );
		}

		// Zlib-compressed — decompress into a pooled buffer
		var compressed = GetRawData( in entry );
		int decompressedSize = (int)entry.MemSize;

		byte[] buffer = ArrayPool<byte>.Shared.Rent( decompressedSize );
		try
		{
			int written = DecompressZlib( compressed, buffer.AsSpan( 0, decompressedSize ) );
			return new ResourceData( buffer.AsSpan( 0, written ), buffer );
		}
		catch
		{
			ArrayPool<byte>.Shared.Return( buffer );
			throw;
		}
	}

	/// <summary>Decompress a zlib stream (with header) into the destination.</summary>
	private static int DecompressZlib( ReadOnlySpan<byte> source, Span<byte> dest )
	{
		// ZLibStream handles the zlib header (2 bytes) + deflate data + adler32 checksum
		using var sourceStream = new UnmanagedMemoryStreamWrapper( source );
		using var zlib = new ZLibStream( sourceStream, CompressionMode.Decompress, leaveOpen: true );

		int totalRead = 0;
		int read;
		while ( totalRead < dest.Length &&
			   (read = zlib.Read( dest.Slice( totalRead ) )) > 0 )
		{
			totalRead += read;
		}
		return totalRead;
	}

	// ---- Lookup -------------------------------------------------------------

	/// <summary>Finds an entry by its Type-Group-Instance key.</summary>
	public bool TryGetEntry( ResourceKey key, out DbpfEntry entry )
	{
		if ( _keyIndex.TryGetValue( key, out int idx ) )
		{
			entry = Entries[idx];
			return true;
		}
		entry = default;
		return false;
	}

	/// <summary>Returns all entries matching the given resource type.</summary>
	public EntryTypeEnumerator GetEntriesOfType( ResourceType type ) => new( Entries, type );

	/// <summary>Gets the entry at the given index.</summary>
	public ref readonly DbpfEntry this[int index]
	{
		[MethodImpl( MethodImplOptions.AggressiveInlining )]
		get => ref Entries[index];
	}

	/// <summary>Number of resources in this package.</summary>
	public int Count => Entries.Length;

	// ---- Disposal -----------------------------------------------------------

	public unsafe void Dispose()
	{
		_accessor.SafeMemoryMappedViewHandle.ReleasePointer();
		_accessor.Dispose();
		_mmf.Dispose();
	}

	/// <summary>
	/// Gets the resource name by looking it up in STBL (String Table) resources.
	/// If no STBL entry is found, returns the resource ID as a hex string.
	/// </summary>
	public string GetResourceName( ulong resourceId )
	{
		// Build STBL cache on first call
		if ( _stblCache == null )
		{
			_stblCache = new Dictionary<ulong, string>();

			foreach ( var entry in GetEntriesOfType( ResourceType.STBL ) )
			{
				var stbl = this.ReadStbl( entry );

				// STBL maps hash -> string
				//TODO these maps are localization tables... idk how we know which id belongs to which language yet...
				foreach ( var kvp in stbl.Entries )
				{
					if ( !_stblCache.ContainsKey( kvp.Key ) )
					{
						_stblCache[kvp.Key] = kvp.Value;
					}
				}

			}
		}

		// Try to find the name in cache
		if ( _stblCache.TryGetValue( resourceId, out var name ) )
		{
			return name;
		}

		// Fallback to hex representation
		return $"0x{resourceId:X16}";
	}

	/// <summary>
	/// Clears the cached STBL strings. Call this if the package changes.
	/// </summary>
	public void ClearNameCache()
	{
		_stblCache?.Clear();
		_stblCache = null;
	}
}

// ============================================================================
// ResourceData — A possibly-pooled decompressed resource buffer.
// Dispose to return the buffer to ArrayPool.
// If the data was zero-copy (uncompressed), Dispose is a no-op.
// ============================================================================

/// <summary>
/// Holds a span of resource data that may be backed by a pooled array.
/// Always dispose after use.
/// </summary>
public readonly ref struct ResourceData
{
	/// <summary>The decompressed (or raw) resource bytes.</summary>
	public readonly ReadOnlySpan<byte> Span;

	private readonly byte[] _pooledBuffer;

	internal ResourceData( ReadOnlySpan<byte> span, byte[] pooledBuffer )
	{
		Span = span;
		_pooledBuffer = pooledBuffer;
	}

	public void Dispose()
	{
		if ( _pooledBuffer is not null )
			ArrayPool<byte>.Shared.Return( _pooledBuffer );
	}
}

// ============================================================================
// EntryTypeEnumerator — Allocation-free enumeration of entries by type.
// ============================================================================

public struct EntryTypeEnumerator
{
	private readonly DbpfEntry[] _entries;
	private readonly ResourceType _type;
	private int _index;

	internal EntryTypeEnumerator( DbpfEntry[] entries, ResourceType type )
	{
		_entries = entries;
		_type = type;
		_index = -1;
	}

	public readonly EntryTypeEnumerator GetEnumerator() => this;
	public ref readonly DbpfEntry Current => ref _entries[_index];

	public bool MoveNext()
	{
		while ( ++_index < _entries.Length )
		{
			if ( _entries[_index].Type == _type )
				return true;
		}
		return false;
	}
}

// ============================================================================
// Internal helper — wraps a ReadOnlySpan<byte> as a read-only Stream
// so ZLibStream can consume it without allocating a MemoryStream copy.
// ============================================================================

internal sealed unsafe class UnmanagedMemoryStreamWrapper : Stream
{
	private readonly byte* _ptr;
	private readonly int _length;
	private int _position;

	public UnmanagedMemoryStreamWrapper( ReadOnlySpan<byte> data )
	{
		// Pin: the span comes from a memory-mapped view — already pinned.
		// We just grab the pointer. This is safe as long as the MMF outlives this stream.
		fixed ( byte* p = data )
		{
			_ptr = p;
		}
		_length = data.Length;
		_position = 0;
	}

	public override bool CanRead => true;
	public override bool CanSeek => true;
	public override bool CanWrite => false;
	public override long Length => _length;

	public override long Position
	{
		get => _position;
		set => _position = (int)value;
	}

	public override int Read( byte[] buffer, int offset, int count )
	{
		int available = _length - _position;
		if ( available <= 0 ) return 0;
		int toRead = Math.Min( count, available );
		new ReadOnlySpan<byte>( _ptr + _position, toRead ).CopyTo( buffer.AsSpan( offset, toRead ) );
		_position += toRead;
		return toRead;
	}

	public override int Read( Span<byte> buffer )
	{
		int available = _length - _position;
		if ( available <= 0 ) return 0;
		int toRead = Math.Min( buffer.Length, available );
		new ReadOnlySpan<byte>( _ptr + _position, toRead ).CopyTo( buffer.Slice( 0, toRead ) );
		_position += toRead;
		return toRead;
	}

	public override long Seek( long offset, SeekOrigin origin )
	{
		_position = origin switch
		{
			SeekOrigin.Begin => (int)offset,
			SeekOrigin.Current => _position + (int)offset,
			SeekOrigin.End => _length + (int)offset,
			_ => throw new ArgumentOutOfRangeException( nameof( origin ) )
		};
		return _position;
	}

	public override void Flush() { }
	public override void SetLength( long value ) => throw new NotSupportedException();
	public override void Write( byte[] buffer, int offset, int count ) => throw new NotSupportedException();
}
