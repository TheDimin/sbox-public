using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Sandbox.Mounting.Sims4;

/// <summary>
/// High-performance DBPF reader tailored for Sims 4 packages. Parses header/index once
/// and exposes zero-allocation enumerators for resource access.
/// </summary>
public sealed class DbpfInstance : IDisposable
{
	private readonly FileStream _stream;
	private readonly DbpfHeader _header;
	private readonly DbpfRecord[] _records;

	private DbpfInstance( FileStream stream, DbpfHeader header, DbpfRecord[] records )
	{
		_stream = stream;
		_header = header;
		_records = records;
	}

	public static DbpfInstance Open( string path )
	{
		var stream = new FileStream( path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 64 * 1024, FileOptions.RandomAccess );

		Span<byte> headerBuffer = stackalloc byte[DbpfHeader.Sizeof()];
		stream.ReadExactly( headerBuffer );
		var header = StructUtil.ReadStruct<DbpfHeader>( headerBuffer );

		if ( header.Magic != DbpfHeader.ExpectedMagic )
		{
			stream.Dispose();
			throw new InvalidDataException( $"'{path}' Not a DBPF file (missing DBPF magic) {header.Magic}" );
		}

		var indexData = GC.AllocateUninitializedArray<byte>( checked((int)header.IndexSize) );
		stream.Seek( header.IndexOffset, SeekOrigin.Begin );
		stream.ReadExactly( indexData );

		var records = ParseIndex( header, indexData );
		return new DbpfInstance( stream, header, records );
	}

	public ReadOnlySpan<DbpfRecord> Records => _records;

	public IEnumerable<DbpfRecord> GetRecords( ResourceType resourceType )
	{
		foreach ( var record in _records )
		{
			if ( record.ResourceType == resourceType )
				yield return record;
		}
	}

	/// <summary>
	/// Convenience enumerator matching requested API: foreach(GEOMResource r in GetEnumerator(ResourceType.GEOM))
	/// </summary>
	public IEnumerable<GEOMResource> GetEnumerator( ResourceType resourceType )
	{
		if ( resourceType != ResourceType.GEOM )
			throw new NotSupportedException( $"Typed enumeration is only implemented for GEOM. Requested {resourceType}" );

		foreach ( var record in _records )
		{
			if ( record.ResourceType != ResourceType.GEOM )
				continue;

			var payload = ReadResourcePayload( record );
			yield return new GEOMResource( payload );
		}
	}

	public byte[] ReadRaw( DbpfRecord record ) => ReadResourcePayload( record );

	private static DbpfRecord[] ParseIndex( DbpfHeader header, ReadOnlySpan<byte> indexData )
	{
		var reader = new SpanReader( indexData );
		var indexType = reader.ReadUInt32();

		uint constType = (indexType & 0x01) != 0 ? reader.ReadUInt32() : 0;
		uint constGroup = (indexType & 0x02) != 0 ? reader.ReadUInt32() : 0;
		uint constInstanceHi = (indexType & 0x04) != 0 ? reader.ReadUInt32() : 0;
		uint constInstanceLo = (indexType & 0x08) != 0 ? reader.ReadUInt32() : 0;
		uint constFileSize = (indexType & 0x20) != 0 ? reader.ReadUInt32() : 0;
		uint constMemSize = (indexType & 0x40) != 0 ? reader.ReadUInt32() : 0;
		uint constCompressed = (indexType & 0x80) != 0 ? reader.ReadUInt32() : 0;

		var records = new DbpfRecord[header.IndexEntryCount];
		for ( int i = 0; i < records.Length; i++ )
		{
			var type = (ResourceType)((indexType & 0x01) != 0 ? constType : reader.ReadUInt32());
			uint group = (indexType & 0x02) != 0 ? constGroup : reader.ReadUInt32();
			uint instanceHi = (indexType & 0x04) != 0 ? constInstanceHi : reader.ReadUInt32();
			uint instanceLo = (indexType & 0x08) != 0 ? constInstanceLo : reader.ReadUInt32();
			ulong instanceId = ((ulong)instanceHi << 32) | instanceLo;

			uint chunkOffset = reader.ReadUInt32();

			uint fileSizeRaw = (indexType & 0x20) != 0 ? constFileSize : reader.ReadUInt32();
			uint memSize = (indexType & 0x40) != 0 ? constMemSize : reader.ReadUInt32();

			ushort compressionWord;
			ushort unknownWord;
			if ( (indexType & 0x80) != 0 )
			{
				compressionWord = (ushort)(constCompressed & 0xFFFF);
				unknownWord = (ushort)(constCompressed >> 16);
			}
			else
			{
				compressionWord = reader.ReadUInt16();
				unknownWord = reader.ReadUInt16();
			}

			uint compressedSize = fileSizeRaw & 0x7FFFFFFF;
			records[i] = new DbpfRecord( type, group, instanceId, chunkOffset, compressedSize, memSize, (CompressionType)compressionWord, unknownWord );
		}

		return records;
	}

	private byte[] ReadResourcePayload( DbpfRecord record )
	{
		var compressedSize = checked((int)record.CompressedSize);
		var compressed = GC.AllocateUninitializedArray<byte>( compressedSize );

		_stream.Seek( record.FileOffset, SeekOrigin.Begin );
		_stream.ReadExactly( compressed );

		if ( record.CompressionType == CompressionType.Uncompressed || record.CompressedSize == record.DecompressedSize )
			return compressed;

		if ( record.CompressionType != CompressionType.Zlib )
			throw new NotSupportedException( $"Unsupported compression type: 0x{((ushort)record.CompressionType):X4}" );

		var output = GC.AllocateUninitializedArray<byte>( checked((int)record.DecompressedSize) );
		using var source = new MemoryStream( compressed, writable: false );
		using var zlib = new ZLibStream( source, CompressionMode.Decompress, leaveOpen: true );
		int readTotal = 0;
		while ( readTotal < output.Length )
		{
			int read = zlib.Read( output, readTotal, output.Length - readTotal );
			if ( read == 0 )
				break;
			readTotal += read;
		}

		if ( readTotal != output.Length )
			throw new InvalidDataException( $"Expected {output.Length} bytes after decompression, got {readTotal}" );

		return output;
	}

	public void Dispose()
	{
		_stream.Dispose();
		GC.SuppressFinalize( this );
	}
}
