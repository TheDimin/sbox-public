using System.Buffers;
using System.Buffers.Binary;
using System.IO.MemoryMappedFiles;

namespace Sims4Reader;

/// <summary>
/// High-performance, read-only DBPF package reader.
/// Opens Sims 4 .package files and provides O(1) resource lookup by TGI key.
/// Uses memory-mapped I/O for lock-free, zero-syscall reads after initial open.
/// </summary>
///
public sealed class DbpfPackage : IDisposable
{
	private const int HeaderSize = 96;
	private const string Magic = "DBPF";

	private Stream _stream;
	private readonly bool _ownsStream;
	private readonly object _streamLock = new();
	private MemoryMappedFile? _mmf;
	private MemoryMappedViewAccessor? _accessor;
	private ResourceEntry[] _entries;
	private Dictionary<ResourceKey, int>? _index;
	private Dictionary<ResourceType, int[]>? _typeIndex;
	private StringTable? _stringTable;
	private bool _stringTableLoaded;

	private DbpfPackage( Stream stream, bool ownsStream )
	{
		_stream = stream;
		_ownsStream = ownsStream;
		_entries = Array.Empty<ResourceEntry>();
		ReadPackage();
	}

	/// <summary>
	/// Open a .package file from disk (read-only).
	/// Memory mapping is set up lazily on first data read to avoid overhead for packages
	/// that only need index-based lookups.
	/// </summary>
	public static DbpfPackage Open( string path )
	{
		var fs = new FileStream( path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan );
		return new DbpfPackage( fs, ownsStream: true );
	}

	private void EnsureMemoryMapped()
	{
		if ( _accessor != null || _mmf != null ) return;
		if ( _stream is not FileStream fs ) return;

		try
		{
			_mmf = MemoryMappedFile.CreateFromFile( fs, null, 0, MemoryMappedFileAccess.Read, HandleInheritability.None, leaveOpen: true );
			_accessor = _mmf.CreateViewAccessor( 0, 0, MemoryMappedFileAccess.Read );
		}
		catch
		{
			// Fall back to stream-based I/O if memory mapping fails
		}
	}

	/// <summary>
	/// Open a .package from an existing stream. The caller owns the stream lifetime.
	/// </summary>
	public static DbpfPackage Open( Stream stream )
	{
		return new DbpfPackage( stream, ownsStream: false );
	}

	/// <summary>
	/// All resource entries in the package.
	/// </summary>
	public IReadOnlyList<ResourceEntry> Entries => _entries;

	/// <summary>
	/// O(1) lookup by full TGI key. Returns null if not found.
	/// The key index is built lazily on first call (not during Open).
	/// </summary>
	public ResourceEntry? Find( ResourceKey key )
	{
		if ( _index == null )
		{
			var index = new Dictionary<ResourceKey, int>( _entries.Length );
			for ( int i = 0; i < _entries.Length; i++ )
				index[_entries[i].Key] = i;
			_index = index;
		}
		return _index.TryGetValue( key, out int idx ) ? _entries[idx] : null;
	}

	/// <summary>
	/// O(1) lookup by Type, Group, Instance.
	/// </summary>
	public ResourceEntry? Find( ResourceType type, uint group, ulong instance )
	{
		return Find( new ResourceKey( type, group, instance ) );
	}

	/// <summary>
	/// Find all entries of a given resource type.
	/// The type index is built lazily on first call (not during Open).
	/// </summary>
	public IEnumerable<ResourceEntry> FindAll( ResourceType type )
	{
		if ( _typeIndex == null )
			BuildTypeIndex();

		if ( _typeIndex!.TryGetValue( type, out var indices ) )
		{
			for ( int i = 0; i < indices.Length; i++ )
				yield return _entries[indices[i]];
		}
	}

	private void BuildTypeIndex()
	{
		var temp = new Dictionary<ResourceType, List<int>>();
		for ( int i = 0; i < _entries.Length; i++ )
		{
			var rt = _entries[i].Key.Type;
			if ( !temp.TryGetValue( rt, out var list ) )
			{
				list = new List<int>();
				temp[rt] = list;
			}
			list.Add( i );
		}

		// Convert to arrays for faster iteration in FindAll
		var result = new Dictionary<ResourceType, int[]>( temp.Count );
		foreach ( var kv in temp )
			result[kv.Key] = kv.Value.ToArray();

		_typeIndex = result;
	}

	/// <summary>
	/// Get the raw decompressed bytes for a resource entry.
	/// Uses memory-mapped I/O when available (lock-free), falls back to stream I/O.
	/// </summary>
	public byte[] GetBytes( ResourceEntry entry )
	{
		if ( entry.ChunkOffset == 0xFFFFFFFF ) return Array.Empty<byte>();
		if ( entry.FileSize == 1 && entry.MemSize == 0xFFFFFFFF ) return Array.Empty<byte>();

		EnsureMemoryMapped();

		byte[] fileData = new byte[entry.FileSize];

		if ( _accessor != null )
		{
			// Memory-mapped: no lock needed, just a memory copy
			_accessor.ReadArray( entry.ChunkOffset, fileData, 0, (int)entry.FileSize );
		}
		else
		{
			lock ( _streamLock )
			{
				_stream.Position = entry.ChunkOffset;
				_stream.ReadExactly( fileData );
			}
		}

		if ( !entry.IsCompressed )
			return fileData;

		return Compression.Decompress( fileData, 0, fileData.Length, (int)entry.MemSize );
	}

	/// <summary>
	/// Get a partially decompressed resource entry, up to maxBytes of output.
	/// Much faster than full decompression when only the first portion of data is needed.
	/// Uses memory-mapped I/O for zero-copy access to compressed data.
	/// </summary>
	public unsafe byte[] GetBytesPartial( ResourceEntry entry, int maxBytes )
	{
		if ( entry.ChunkOffset == 0xFFFFFFFF ) return Array.Empty<byte>();
		if ( entry.FileSize == 1 && entry.MemSize == 0xFFFFFFFF ) return Array.Empty<byte>();

		EnsureMemoryMapped();

		int outputSize = Math.Min( maxBytes, (int)entry.MemSize );

		if ( !entry.IsCompressed )
		{
			// Uncompressed: just copy the requested portion
			int copySize = Math.Min( outputSize, (int)entry.FileSize );
			byte[] result = new byte[copySize];
			if ( _accessor != null )
				_accessor.ReadArray( entry.ChunkOffset, result, 0, copySize );
			else
			{
				lock ( _streamLock )
				{
					_stream.Position = entry.ChunkOffset;
					_stream.ReadExactly( result, 0, copySize );
				}
			}
			return result;
		}

		if ( _accessor != null )
		{
			byte* basePtr = null;
			_accessor.SafeMemoryMappedViewHandle.AcquirePointer( ref basePtr );
			basePtr += _accessor.PointerOffset;
			try
			{
				byte* entryData = basePtr + entry.ChunkOffset;
				return Compression.DecompressPartial( entryData, (int)entry.FileSize, outputSize );
			}
			finally
			{
				_accessor.SafeMemoryMappedViewHandle.ReleasePointer();
			}
		}
		else
		{
			// Stream fallback
			byte[] fileData;
			lock ( _streamLock )
			{
				_stream.Position = entry.ChunkOffset;
				fileData = new byte[entry.FileSize];
				_stream.ReadExactly( fileData );
			}
			return Compression.Decompress( fileData, 0, fileData.Length, outputSize );
		}
	}

	/// <summary>
	/// Batch-read and decompress multiple entries. With memory-mapped I/O, decompresses
	/// directly from mapped memory — no intermediate byte[] allocations for compressed data.
	/// Falls back to stream-based I/O when memory mapping is not available.
	/// </summary>
	public unsafe byte[][] GetBytesBatch( IReadOnlyList<ResourceEntry> entries )
	{
		if ( entries.Count == 0 ) return Array.Empty<byte[]>();

		EnsureMemoryMapped();

		var results = new byte[entries.Count][];

		if ( _accessor != null )
		{
			// Zero-copy path: decompress directly from memory-mapped pages.
			// Eliminates N byte[] allocations and N memory copies for compressed data.
			byte* basePtr = null;
			_accessor.SafeMemoryMappedViewHandle.AcquirePointer( ref basePtr );
			basePtr += _accessor.PointerOffset;
			try
			{
				for ( int i = 0; i < entries.Count; i++ )
				{
					var entry = entries[i];
					if ( entry.ChunkOffset == 0xFFFFFFFF || (entry.FileSize == 1 && entry.MemSize == 0xFFFFFFFF) )
					{
						results[i] = Array.Empty<byte>();
						continue;
					}

					byte* entryData = basePtr + entry.ChunkOffset;

					if ( !entry.IsCompressed )
					{
						results[i] = new byte[entry.FileSize];
						new Span<byte>( entryData, (int)entry.FileSize ).CopyTo( results[i] );
						continue;
					}

					// Decompress directly from mapped memory — no intermediate buffer
					results[i] = Compression.DecompressMapped( entryData, (int)entry.FileSize, (int)entry.MemSize );
				}
			}
			finally
			{
				_accessor.SafeMemoryMappedViewHandle.ReleasePointer();
			}
		}
		else
		{
			// Stream fallback: sort by offset for sequential I/O
			var compressedData = new byte[entries.Count][];
			var sortOrder = new int[entries.Count];
			for ( int i = 0; i < entries.Count; i++ ) sortOrder[i] = i;
			Array.Sort( sortOrder, ( a, b ) => entries[a].ChunkOffset.CompareTo( entries[b].ChunkOffset ) );

			lock ( _streamLock )
			{
				for ( int si = 0; si < sortOrder.Length; si++ )
				{
					int i = sortOrder[si];
					var entry = entries[i];
					if ( entry.ChunkOffset == 0xFFFFFFFF || (entry.FileSize == 1 && entry.MemSize == 0xFFFFFFFF) )
					{
						results[i] = Array.Empty<byte>();
						continue;
					}
					_stream.Position = entry.ChunkOffset;
					compressedData[i] = new byte[entry.FileSize];
					_stream.ReadExactly( compressedData[i] );

					if ( !entry.IsCompressed )
					{
						results[i] = compressedData[i];
						compressedData[i] = null!;
					}
				}
			}

			for ( int i = 0; i < entries.Count; i++ )
			{
				if ( results[i] != null ) continue;
				if ( compressedData[i] == null ) continue;
				results[i] = Compression.Decompress( compressedData[i], 0, compressedData[i].Length, (int)entries[i].MemSize );
			}
		}

		return results;
	}

	/// <summary>
	/// Get a typed resource parsed from the entry's data.
	/// </summary>
	public T GetResource<T>( ResourceEntry entry ) where T : IResource, new()
	{
		byte[] data = GetBytes( entry );
		var resource = new T();
		resource.Parse( data );
		return resource;
	}

	/// <summary>
	/// Get the first string table in the package (cached).
	/// </summary>
	public StringTable? GetStringTable()
	{
		if ( _stringTableLoaded ) return _stringTable;
		_stringTableLoaded = true;

		foreach ( var entry in FindAll( ResourceType.StringTable ) )
		{
			_stringTable = GetResource<StringTable>( entry );
			break;
		}
		return _stringTable;
	}

	/// <summary>
	/// Try to resolve a display name for a resource by cross-referencing string tables.
	/// Returns null if no name is found.
	/// </summary>
	public string? GetDisplayName( ResourceEntry entry )
	{
		var stbl = GetStringTable();
		if ( stbl == null ) return null;

		// Try the instance hash as a string table key
		uint hash = (uint)(entry.Key.Instance & 0xFFFFFFFF);
		return stbl.GetString( hash );
	}

	private void ReadPackage()
	{
		Span<byte> headerBuf = stackalloc byte[HeaderSize];
		_stream.Position = 0;
		_stream.ReadExactly( headerBuf );

		// Validate magic
		if ( headerBuf[0] != 'D' || headerBuf[1] != 'B' || headerBuf[2] != 'P' || headerBuf[3] != 'F' )
			throw new InvalidDataException( "Not a DBPF package (invalid magic bytes)" );

		int major = BinaryPrimitives.ReadInt32LittleEndian( headerBuf[4..] );
		if ( major != 2 )
			throw new InvalidDataException( $"Unsupported DBPF major version: {major} (expected 2)" );

		int indexCount = BinaryPrimitives.ReadInt32LittleEndian( headerBuf[36..] );
		int indexSize = BinaryPrimitives.ReadInt32LittleEndian( headerBuf[44..] );
		int indexPosition = BinaryPrimitives.ReadInt32LittleEndian( headerBuf[64..] );
		if ( indexPosition == 0 )
			indexPosition = BinaryPrimitives.ReadInt32LittleEndian( headerBuf[40..] );

		if ( indexCount == 0 || indexPosition == 0 )
		{
			_entries = Array.Empty<ResourceEntry>();
			return;
		}

		ReadIndex( indexPosition, indexSize, indexCount );
	}

	private void ReadIndex( int indexPosition, int indexSize, int indexCount )
	{
		_stream.Position = indexPosition;

		byte[] indexData = ArrayPool<byte>.Shared.Rent( indexSize );
		try
		{
			_stream.ReadExactly( indexData, 0, indexSize );
			var span = indexData.AsSpan( 0, indexSize );

			int pos = 0;
			uint indexType = BinaryPrimitives.ReadUInt32LittleEndian( span[pos..] );
			pos += 4;

			// Read shared header values
			uint sharedType = 0, sharedGroup = 0, sharedInstanceHigh = 0;
			if ( (indexType & 0x01) != 0 ) { sharedType = BinaryPrimitives.ReadUInt32LittleEndian( span[pos..] ); pos += 4; }
			if ( (indexType & 0x02) != 0 ) { sharedGroup = BinaryPrimitives.ReadUInt32LittleEndian( span[pos..] ); pos += 4; }
			if ( (indexType & 0x04) != 0 ) { sharedInstanceHigh = BinaryPrimitives.ReadUInt32LittleEndian( span[pos..] ); pos += 4; }

			_entries = new ResourceEntry[indexCount];

			// Pre-compute per-entry stride based on which fields are shared.
			bool hasType = (indexType & 0x01) == 0;
			bool hasGroup = (indexType & 0x02) == 0;
			bool hasInstanceHigh = (indexType & 0x04) == 0;

			// Parse entries — hot loop with no dictionary writes.
			for ( int i = 0; i < indexCount; i++ )
			{
				uint type = hasType ? BinaryPrimitives.ReadUInt32LittleEndian( span.Slice( pos, 4 ) ) : sharedType;
				if ( hasType ) pos += 4;

				uint group = hasGroup ? BinaryPrimitives.ReadUInt32LittleEndian( span.Slice( pos, 4 ) ) : sharedGroup;
				if ( hasGroup ) pos += 4;

				uint instanceHigh = hasInstanceHigh ? BinaryPrimitives.ReadUInt32LittleEndian( span.Slice( pos, 4 ) ) : sharedInstanceHigh;
				if ( hasInstanceHigh ) pos += 4;

				uint instanceLow = BinaryPrimitives.ReadUInt32LittleEndian( span.Slice( pos, 4 ) ); pos += 4;
				uint chunkOffset = BinaryPrimitives.ReadUInt32LittleEndian( span.Slice( pos, 4 ) ); pos += 4;
				uint fileSize = BinaryPrimitives.ReadUInt32LittleEndian( span.Slice( pos, 4 ) ) & 0x7FFFFFFF; pos += 4;
				uint memSize = BinaryPrimitives.ReadUInt32LittleEndian( span.Slice( pos, 4 ) ); pos += 4;
				ushort compressed = BinaryPrimitives.ReadUInt16LittleEndian( span.Slice( pos, 2 ) ); pos += 2;
				pos += 2; // skip Unknown2

				ulong instance = ((ulong)instanceHigh << 32) | instanceLow;
				var key = new ResourceKey( (ResourceType)type, group, instance );
				_entries[i] = new ResourceEntry( key, chunkOffset, fileSize, memSize, compressed, i );
			}

			// _typeIndex and _index are built lazily on first FindAll/Find call.
		}
		finally
		{
			ArrayPool<byte>.Shared.Return( indexData );
		}
	}

	public void Dispose()
	{
		_accessor?.Dispose();
		_mmf?.Dispose();
		if ( _ownsStream )
			_stream?.Dispose();
	}
}
