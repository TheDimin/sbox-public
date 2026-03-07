using System;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using Sandbox.Diagnostics;

namespace Sandbox.Mounting.Sims4;

/// <summary>
/// A single resource record within a DBPF package.
/// Resources are uniquely identified by their TGI (Type, Group, Instance).
/// </summary>
public sealed class DbpfRecord
{
	/// <summary>Resource type ID (e.g. 0x00B2D882 = DDS texture)</summary>
	public uint TypeId;

	/// <summary>Group ID (categorization / namespace)</summary>
	public uint GroupId;

	/// <summary>64-bit instance ID (high 32 bits | low 32 bits)</summary>
	public ulong InstanceId;

	/// <summary>Absolute offset within the package file</summary>
	public long FileOffset;

	/// <summary>Compressed data size in bytes</summary>
	public int CompressedSize;

	/// <summary>Original size after decompression (equals CompressedSize if uncompressed)</summary>
	public int DecompressedSize;

	/// <summary>
	/// Compression method:
	///   0x0000 = uncompressed
	///   0x5A42 = zlib (deflate)
	///   0xFFFF = RefPack
	///   0xFFFE = RefPack (streamable)
	/// </summary>
	public ushort CompressionType;

	public bool IsCompressed => CompressionType != 0x0000;
}

public enum Sims4AssetKind
{
	Unknown,
	ObjectDefinition,
	Texture,
	Model,
}

public readonly record struct Sims4AssetInfo( Sims4AssetKind Kind, DbpfRecord Record );
public readonly record struct Sims4ObjectAssetGraph(
	DbpfRecord Objd,
	IReadOnlyList<DbpfRecord> Models,
	IReadOnlyList<DbpfRecord> Geoms,
	IReadOnlyList<DbpfRecord> Materials,
	IReadOnlyList<DbpfRecord> Textures );

public readonly record struct StblString( uint Key, string Text, byte Locale );

/// <summary>
/// Reads a DBPF v2 package file — the archive format used by The Sims 4.
///
/// The file starts with an 88-byte header containing version info and the
/// location of the index. The index maps TGI keys to data offsets. Data
/// blocks may be uncompressed, zlib-compressed, or RefPack-compressed.
/// </summary>
public sealed class DbpfPackage : IDisposable
{
	private static readonly Logger Log = new Logger( "Sims4-DbpfPackage" );
	private const int DbpfHeaderSize = 96;
	private const int DbpfHeaderPrefixSize = 72;

	[StructLayout( LayoutKind.Sequential, Pack = 1 )]
	private readonly struct DbpfHeaderPrefix
	{
		public readonly uint Magic;
		public readonly uint MajorVersion;
		public readonly uint MinorVersion;
		public readonly uint Unknown1;
		public readonly uint Unknown2;
		public readonly uint Unknown3;
		public readonly uint DateCreated;
		public readonly uint DateModified;
		public readonly uint IndexMajorVersion;
		public readonly uint IndexEntryCount;
		public readonly uint IndexFirstEntryOffset;
		public readonly uint IndexSize;
		public readonly uint HoleEntryCount;
		public readonly uint HoleOffset;
		public readonly uint HoleSize;
		public readonly uint IndexMinorVersion;
		public readonly uint IndexOffset;
		public readonly uint Unknown4;
	}

	public enum TypeID : uint
	{
		Texture = 0x00B2D882,
		Model = 0x01661233,
		ObjectDefinition = 0xC0DB5AE7,
		Objd = 0x319E4F1D,
		MlodTypeId = 0x01D10F34,
		GeomTypeId = 0x015A1849,
		MtlsTypeId = 0x01D0E75D,
		MatdTypeId = 0x02019972,
		Rle2TypeId = 0x3453CF95,
		NameMapTypeId = 0x0166038C,
		StblTypeId = 0x220557DA,

	}

	private readonly FileStream _stream;
	private readonly object _lock = new();
	private readonly List<DbpfRecord> _records = new();
	private readonly Dictionary<uint, List<DbpfRecord>> _recordsByType = new();
	private readonly Dictionary<(uint TypeId, uint GroupId, ulong InstanceId), DbpfRecord> _recordsByTgi = new();
	private Dictionary<ulong, string> _resolvedNames = [];
	private bool _resolvedNamesLoaded;

	public IReadOnlyList<DbpfRecord> Records => _records;
	public string FilePath { get; }

	private DbpfPackage( string filePath, FileStream stream )
	{
		FilePath = filePath;
		_stream = stream;
	}

	/// <summary>
	/// Open a .package file and parse its index. Does not load resource data.
	/// </summary>
	public static DbpfPackage Open( string filePath )
	{
		Log.Info( $"Opening DBPF package: {filePath}" );
		var stream = new FileStream( filePath, FileMode.Open, FileAccess.Read, FileShare.Read );
		var pkg = new DbpfPackage( filePath, stream );

		try
		{
			pkg.ReadHeader();
			Log.Info( $"Successfully loaded {pkg.Records.Count} records from {filePath}" );
		}
		catch ( Exception ex )
		{
			Log.Error( $"Failed to read DBPF package {filePath}: {ex.Message}" );
			stream.Dispose();
			throw;
		}

		return pkg;
	}

	private void ReadHeader()
	{
		if ( !BitConverter.IsLittleEndian )
			throw new PlatformNotSupportedException( "DBPF parser requires little-endian architecture." );

		// Try to find and validate DBPF header
		long headerStart = FindDbpfHeader();
		_stream.Seek( headerStart, SeekOrigin.Begin );

		var headerBytes = new byte[DbpfHeaderSize];
		_stream.ReadExactly( headerBytes );
		var header = MemoryMarshal.Read<DbpfHeaderPrefix>( headerBytes.AsSpan( 0, DbpfHeaderPrefixSize ) );

		var majorVersion = header.MajorVersion;
		var minorVersion = header.MinorVersion;
		Log.Trace( $"Version: {majorVersion}.{minorVersion}" );

		if ( majorVersion != 2 && majorVersion != 1 )
			throw new InvalidDataException( $"Unsupported DBPF version {majorVersion}.{minorVersion}" );

		// DBPF v2 header layout (96 bytes total, offsets from magic start):
		//   0x00  magic "DBPF"
		//   0x04  major version
		//   0x08  minor version
		//   0x0C  user version major
		//   0x10  user version minor
		//   0x14  unused
		//   0x18  creation time
		//   0x1C  modification time
		//   0x20  unused
		//   0x24  index entry count
		//   0x28  index position (legacy, 0 in v2)
		//   0x2C  index size
		//   0x30  unused (×3)
		//   0x3C  unused (index version)
		//   0x40  index position (current)
		//   0x44–0x5F  reserved
		var entryCount = header.IndexEntryCount;           // 0x24 index entry count
		var indexPositionLow = header.IndexFirstEntryOffset;     // 0x28 legacy index position (0 in v2)
		var indexSize = header.IndexSize;            // 0x2C index size

		var indexPosition = header.IndexOffset;        // 0x40 index position (current)
		if ( indexPosition == 0 )
			indexPosition = indexPositionLow;

		Log.Info( $"DBPF Header: Version={majorVersion}.{minorVersion}" );
		Log.Info( $"  EntryCount={entryCount}, IndexPosition=0x{indexPosition:X8}, IndexSize={indexSize}" );

		if ( entryCount == 0 || indexSize == 0 )
		{
			Log.Warning( $"DBPF package has no entries (EntryCount={entryCount}, IndexSize={indexSize}). File may be empty or corrupted." );
			return;
		}

		// Validate index position
		if ( indexPosition > (uint)_stream.Length )
		{
			Log.Error( $"Index position 0x{indexPosition:X8} exceeds file length {_stream.Length}. File may be corrupted." );
			throw new InvalidDataException( $"Invalid index position: 0x{indexPosition:X8}" );
		}

		Log.Trace( $"Seeking to index position 0x{indexPosition:X}" );
		_stream.Seek( indexPosition, SeekOrigin.Begin );
		ReadIndex( (int)entryCount, (int)indexSize );
	}

	private static uint ReadUInt32( ReadOnlySpan<byte> buffer, int offset )
	{
		if ( (uint)(offset + sizeof( uint )) > (uint)buffer.Length )
			throw new InvalidDataException( "Unexpected end of DBPF index/header data while reading UInt32." );

		var value = MemoryMarshal.Read<uint>( buffer[offset..] );
		return BitConverter.IsLittleEndian ? value : BinaryPrimitives.ReverseEndianness( value );
	}

	private static ushort ReadUInt16( ReadOnlySpan<byte> buffer, int offset )
	{
		if ( (uint)(offset + sizeof( ushort )) > (uint)buffer.Length )
			throw new InvalidDataException( "Unexpected end of DBPF index/header data while reading UInt16." );

		var value = MemoryMarshal.Read<ushort>( buffer[offset..] );
		return BitConverter.IsLittleEndian ? value : BinaryPrimitives.ReverseEndianness( value );
	}

	private void ReadIndex( int entryCount, int indexSize )
	{
		Log.Trace( $"Current stream position before ReadIndex: 0x{_stream.Position:X}" );
		Log.Trace( $"Reading index with {entryCount} entries" );
		if ( indexSize < 4 )
			throw new InvalidDataException( $"Invalid DBPF index size: {indexSize}" );

		var indexData = new byte[indexSize];
		_stream.ReadExactly( indexData );
		var indexSpan = indexData.AsSpan();
		var cursor = 0;

		// The first dword encodes which TGI fields are shared across all entries.
		// If a bit is set, the value is stored once after this dword instead of per-entry.
		var indexType = ReadUInt32( indexSpan, cursor );
		cursor += sizeof( uint );
		Log.Trace( $"Index type flags: 0x{indexType:X8}" );

		bool typeConstant = (indexType & 0x01) != 0;
		bool groupConstant = (indexType & 0x02) != 0;
		bool instanceHighConstant = (indexType & 0x04) != 0;

		uint constantType = typeConstant ? ReadUInt32( indexSpan, cursor ) : 0;
		if ( typeConstant )
			cursor += sizeof( uint );

		uint constantGroup = groupConstant ? ReadUInt32( indexSpan, cursor ) : 0;
		if ( groupConstant )
			cursor += sizeof( uint );

		uint constantInstanceHigh = instanceHighConstant ? ReadUInt32( indexSpan, cursor ) : 0;
		if ( instanceHighConstant )
			cursor += sizeof( uint );

		Log.Trace( $"Index compression flags: Type={typeConstant}, Group={groupConstant}, InstanceHigh={instanceHighConstant}" );
		if ( typeConstant ) Log.Trace( $"  Constant Type: 0x{constantType:X8}" );
		if ( groupConstant ) Log.Trace( $"  Constant Group: 0x{constantGroup:X8}" );
		if ( instanceHighConstant ) Log.Trace( $"  Constant InstanceHigh: 0x{constantInstanceHigh:X8}" );

		for ( int i = 0; i < entryCount; i++ )
		{
			uint typeId = typeConstant ? constantType : ReadUInt32( indexSpan, cursor );
			if ( !typeConstant )
				cursor += sizeof( uint );

			uint groupId = groupConstant ? constantGroup : ReadUInt32( indexSpan, cursor );
			if ( !groupConstant )
				cursor += sizeof( uint );

			uint instanceHigh = instanceHighConstant ? constantInstanceHigh : ReadUInt32( indexSpan, cursor );
			if ( !instanceHighConstant )
				cursor += sizeof( uint );

			uint instanceLow = ReadUInt32( indexSpan, cursor );
			cursor += sizeof( uint );

			ulong instanceId = ((ulong)instanceHigh << 32) | instanceLow;

			uint fileOffset = ReadUInt32( indexSpan, cursor );
			cursor += sizeof( uint );

			uint fileSizeRaw = ReadUInt32( indexSpan, cursor );
			cursor += sizeof( uint );
			bool isCompressed = (fileSizeRaw & 0x80000000u) != 0;
			int compressedSize = (int)(fileSizeRaw & 0x7FFFFFFF);

			// Sims 4 DBPF index entries always include decompressed size and committed flag.
			// Compression type is only present when compressed.
			int decompressedSize = (int)ReadUInt32( indexSpan, cursor );
			cursor += sizeof( uint );

			ushort compressionType = isCompressed ? ReadUInt16( indexSpan, cursor ) : (ushort)0;
			if ( isCompressed )
				cursor += sizeof( ushort );

			_ = ReadUInt16( indexSpan, cursor ); // committed indicator (unused)
			cursor += sizeof( ushort );

			var record = new DbpfRecord
			{
				TypeId = typeId,
				GroupId = groupId,
				InstanceId = instanceId,
				FileOffset = fileOffset,
				CompressedSize = compressedSize,
				DecompressedSize = decompressedSize,
				CompressionType = compressionType,
			};

			_records.Add( record );
			if ( !_recordsByType.TryGetValue( record.TypeId, out var typedRecords ) )
			{
				typedRecords = [];
				_recordsByType[record.TypeId] = typedRecords;
			}

			typedRecords.Add( record );
			_recordsByTgi.TryAdd( (record.TypeId, record.GroupId, record.InstanceId), record );
		}

		Log.Info( $"Loaded {_records.Count} records from index" );
	}

	public static Sims4AssetKind GetAssetKind( uint typeId ) => (TypeID)typeId switch
	{
		TypeID.ObjectDefinition => Sims4AssetKind.ObjectDefinition,
		TypeID.Objd => Sims4AssetKind.ObjectDefinition,
		TypeID.Texture => Sims4AssetKind.Texture,
		TypeID.Model => Sims4AssetKind.Model,
		_ => Sims4AssetKind.Unknown,
	};

	public IEnumerable<DbpfRecord> GetRecordsByType( TypeID typeId ) =>
		_recordsByType.TryGetValue( (uint)typeId, out var records ) ? records : [];

	public IEnumerable<DbpfRecord> GetTextureRecords() => GetRecordsByType( TypeID.Texture );

	public IEnumerable<DbpfRecord> GetModelRecords() => GetRecordsByType( TypeID.Model );

	public IEnumerable<DbpfRecord> GetObjectDefinitionRecords() => GetRecordsByType( TypeID.ObjectDefinition );

	public IEnumerable<DbpfRecord> GetObjdRecords() => GetRecordsByType(TypeID.Objd);

	public IEnumerable<DbpfRecord> GetAllObjectDefinitionRecords() =>
		GetObjectDefinitionRecords().Concat( GetObjdRecords() );

	public IEnumerable<DbpfRecord> GetMlodRecords() => GetRecordsByType( TypeID.MlodTypeId );

	public IEnumerable<DbpfRecord> GetGeomRecords() => GetRecordsByType( TypeID.GeomTypeId );

	public IEnumerable<DbpfRecord> GetMaterialRecords() =>
		GetRecordsByType( TypeID.MtlsTypeId ).Concat( GetRecordsByType( TypeID.MatdTypeId ) );

	public IEnumerable<DbpfRecord> GetRle2Records() => GetRecordsByType( TypeID.Rle2TypeId );

	public IEnumerable<DbpfRecord> GetNameMapRecords() => GetRecordsByType(TypeID. NameMapTypeId );

	public IEnumerable<DbpfRecord> GetStblRecords() => GetRecordsByType( TypeID.StblTypeId );

	public bool TryGetRecord( uint typeId, uint groupId, ulong instanceId, out DbpfRecord record )
	{
		if ( _recordsByTgi.TryGetValue( (typeId, groupId, instanceId), out record ) )
			return true;

		if ( groupId != 0 && _recordsByTgi.TryGetValue( (typeId, 0u, instanceId), out record ) )
			return true;

		record = null;
		return false;
	}

	public IEnumerable<Sims4AssetInfo> GetValidRecords()
	{
		foreach ( var record in _records )
		{
			var kind = GetAssetKind( record.TypeId );
			if ( kind == Sims4AssetKind.Unknown )
				continue;

			yield return new Sims4AssetInfo( kind, record );
		}
	}

	/// <summary>
	/// Build object-centric asset graphs following the expected Sims 4 dependency chain:
	/// OBJD -&gt; MODL/MLOD -&gt; GEOM -&gt; MTLS/MATD -&gt; RLE2/DDS.
	///
	/// Relationships are resolved by instance identity first (exact or low 32-bit hash match),
	/// then by group ID as a fallback when direct IDs are unavailable.
	/// </summary>
	public IEnumerable<Sims4ObjectAssetGraph> GetObjectAssetGraphs()
	{
		var modlAndMlod = GetModelRecords().Concat( GetMlodRecords() ).ToList();
		var geoms = GetGeomRecords().ToList();
		var materials = GetMaterialRecords().ToList();
		var textures = GetRle2Records().Concat( GetTextureRecords() ).ToList();

		foreach ( var objd in GetAllObjectDefinitionRecords() )
		{
			var linkedModels = FindRelatedRecords( [objd], modlAndMlod );
			var linkedGeoms = FindRelatedRecords( linkedModels.Count > 0 ? linkedModels : [objd], geoms );
			var linkedMaterials = FindRelatedRecords( linkedGeoms.Count > 0 ? linkedGeoms : linkedModels, materials );
			var textureSeeds = linkedMaterials.Count > 0 ? linkedMaterials : (linkedGeoms.Count > 0 ? linkedGeoms : linkedModels);
			if ( textureSeeds.Count == 0 )
				textureSeeds = [objd];

			var linkedTextures = FindRelatedRecords( textureSeeds, textures );

			yield return new Sims4ObjectAssetGraph( objd, linkedModels, linkedGeoms, linkedMaterials, linkedTextures );
		}
	}

	private static List<DbpfRecord> FindRelatedRecords( IReadOnlyList<DbpfRecord> sources, IReadOnlyList<DbpfRecord> candidates )
	{
		if ( sources.Count == 0 || candidates.Count == 0 )
			return [];

		var result = new List<DbpfRecord>();
		var seen = new HashSet<(uint TypeId, uint GroupId, ulong InstanceId)>();
		var sourceInstanceIds = new HashSet<ulong>( sources.Count );
		var sourceInstanceLow32 = new HashSet<uint>( sources.Count );
		var sourceGroups = new HashSet<uint>( sources.Count );

		for ( int i = 0; i < sources.Count; i++ )
		{
			var source = sources[i];
			sourceInstanceIds.Add( source.InstanceId );
			sourceInstanceLow32.Add( (uint)source.InstanceId );
			if ( source.GroupId != 0 )
				sourceGroups.Add( source.GroupId );
		}

		foreach ( var candidate in candidates )
		{
			if ( !sourceInstanceIds.Contains( candidate.InstanceId ) &&
				 !sourceInstanceLow32.Contains( (uint)candidate.InstanceId ) )
				continue;

			var key = (candidate.TypeId, candidate.GroupId, candidate.InstanceId);
			if ( seen.Add( key ) )
				result.Add( candidate );
		}

		if ( result.Count > 0 )
			return result;

		foreach ( var candidate in candidates )
		{
			if ( !sourceGroups.Contains( candidate.GroupId ) )
				continue;

			var key = (candidate.TypeId, candidate.GroupId, candidate.InstanceId);
			if ( seen.Add( key ) )
				result.Add( candidate );
		}

		return result;
	}

	private Dictionary<ulong, string> ReadNameMap()
	{
		var names = new Dictionary<ulong, string>();

		foreach ( var record in GetNameMapRecords() )
		{
			if ( !TryReadData( record, out var data ) )
				continue;

			TryReadNameMapData( data, names );
		}

		return names;
	}

	/// <summary>
	/// Unified name lookup map for package resources.
	/// Merges _KEY (instanceId -&gt; name) and STBL (hash -&gt; localized text)
	/// into a single dictionary keyed by resource-compatible IDs.
	/// </summary>
	public Dictionary<ulong, string> ReadResolvedNames()
	{
		var resolved = ReadNameMap();

		foreach ( var entry in ReadStblStrings() )
		{
			var key = (ulong)entry.Key;
			if ( !resolved.ContainsKey( key ) && !string.IsNullOrWhiteSpace( entry.Text ) )
				resolved[key] = entry.Text;
		}

		return resolved;
	}

	/// <summary>
	/// Resolve a human-readable name for an instance ID without requiring callers
	/// to know whether the source is _KEY or STBL.
	/// </summary>
	public bool TryResolveName( ulong instanceId, out string name )
	{
		if ( !_resolvedNamesLoaded )
		{
			_resolvedNames = ReadResolvedNames();
			_resolvedNamesLoaded = true;
		}

		Log.Trace( $"Attempting to resolve name for instance ID 0x{instanceId:X16}" );

		if ( _resolvedNames.TryGetValue( instanceId, out name ) )
			return true;

		Log.Trace( $"Attempting fallback resolution using low 32 bits of instance ID: 0x{instanceId & 0xFFFFFFFF:X8}" );

		var low32 = (ulong)(uint)instanceId;
		if ( _resolvedNames.TryGetValue( low32, out name ) )
			return true;

		name = string.Empty;
		return false;
	}

	public List<StblString> ReadStblStrings( byte preferredLocale = 0x00 )
	{
		var allStrings = new List<StblString>();

		foreach ( var record in GetStblRecords() )
		{
			if ( !TryReadData( record, out var data ) )
				continue;

			if ( !TryParseStblData( data, out var strings ) )
				continue;

			var locale = GetLocaleFromInstanceId( record.InstanceId );
			for ( int i = 0; i < strings.Count; i++ )
			{
				var entry = strings[i];
				allStrings.Add( new StblString( entry.Key, entry.Value, locale ) );
			}
		}

		if ( allStrings.Count == 0 )
			return allStrings;

		var preferred = allStrings.Where( x => x.Locale == preferredLocale ).ToList();
		if ( preferred.Count > 0 )
			return preferred;

		return allStrings;
	}

	public Dictionary<uint, string> ReadStblDictionary( byte preferredLocale = 0x00 )
	{
		var result = new Dictionary<uint, string>();
		foreach ( var entry in ReadStblStrings( preferredLocale ) )
			result[entry.Key] = entry.Text;

		return result;
	}

	/// <summary>
	/// Extract localized human-readable object strings from STBL resources.
	/// NameMap (_KEY) is optional and often absent in Sims 4 packages.
	/// </summary>
	public List<StblString> ReadObjectNames( byte preferredLocale = 0x00 ) => ReadStblStrings( preferredLocale );

	/// <summary>
	/// Extract localized object strings keyed by STBL hash.
	/// NameMap (_KEY) is optional and often absent in Sims 4 packages.
	/// </summary>
	public Dictionary<uint, string> ReadObjectNameDictionary( byte preferredLocale = 0x00 ) => ReadStblDictionary( preferredLocale );

	public static bool TryParseStblData( byte[] data, out List<KeyValuePair<uint, string>> strings )
	{
		strings = [];

		if ( data.Length < 10 )
			return false;

		try
		{
			using var stream = new MemoryStream( data, writable: false );
			using var br = new BinaryReader( stream, Encoding.UTF8, leaveOpen: false );

			Span<byte> magic = stackalloc byte[4];
			if ( br.Read( magic ) != 4 )
				return false;

			if ( magic[0] != 'S' || magic[1] != 'T' || magic[2] != 'B' || magic[3] != 'L' )
				return false;

			br.ReadUInt16(); // version
			var entryCount = br.ReadUInt32();
			if ( entryCount == 0 )
				return true;

			strings = new List<KeyValuePair<uint, string>>( (int)Math.Min( entryCount, int.MaxValue ) );
			for ( uint i = 0; i < entryCount && stream.Position < stream.Length; i++ )
			{
				if ( stream.Length - stream.Position < 6 )
					break;

				var key = br.ReadUInt32();
				var length = br.ReadUInt16();
				if ( stream.Position + length > stream.Length )
					break;

				var textBytes = br.ReadBytes( length );
				var text = Encoding.UTF8.GetString( textBytes ).Replace( "\0", string.Empty ).Trim();
				if ( string.IsNullOrWhiteSpace( text ) )
					continue;

				strings.Add( new KeyValuePair<uint, string>( key, text ) );
			}

			if ( strings.Count == 0 && IsStblV5Layout( data ) && TryParseStblDataV5( data, strings ) )
				return true;

			return true;
		}
		catch
		{
			strings = [];
			return false;
		}
	}

	private static bool IsStblV5Layout( byte[] data ) =>
		data.Length >= 8 &&
		data[0] == (byte)'S' && data[1] == (byte)'T' && data[2] == (byte)'B' && data[3] == (byte)'L' &&
		data[4] == 0x05 && data[5] == 0x00 && data[6] == 0x00 && data[7] == 0x02;

	private static bool TryParseStblDataV5( byte[] data, List<KeyValuePair<uint, string>> strings )
	{
		// STBL v5 payloads observed in Sims 4 packages use a 20-byte header,
		// after which records begin.
		const int v5HeaderSize = 20;
		if ( data.Length < v5HeaderSize + 8 )
			return false;

		var offset = v5HeaderSize;
		var parsedAny = false;

		while ( offset + 7 < data.Length )
		{
			var key = BitConverter.ToUInt32( data, offset );

			var textStart = -1;
			var textLength = 0;

			if ( offset + 8 <= data.Length )
			{
				// Layout A: key:u32 + flags:u16 + len:u16 + utf8 bytes
				var length16 = BitConverter.ToUInt16( data, offset + 6 );
				if ( length16 > 0 && offset + 8 + length16 <= data.Length && IsLikelyUtf8Text( data, offset + 8, length16 ) )
				{
					textStart = offset + 8;
					textLength = length16;
				}
			}

			if ( textStart < 0 && offset + 7 <= data.Length )
			{
				// Layout B: key:u32 + flags:u8 + len:u8 + 0x00 + utf8 bytes
				var length8 = data[offset + 5];
				if ( length8 > 0 && data[offset + 6] == 0 && offset + 7 + length8 <= data.Length && IsLikelyUtf8Text( data, offset + 7, length8 ) )
				{
					textStart = offset + 7;
					textLength = length8;
				}
			}

			if ( textStart < 0 )
				break;

			var text = Encoding.UTF8.GetString( data, textStart, textLength ).Replace( "\0", string.Empty ).Trim();
			if ( !string.IsNullOrWhiteSpace( text ) )
			{
				strings.Add( new KeyValuePair<uint, string>( key, text ) );
				parsedAny = true;
			}

			offset = textStart + textLength;
		}

		return parsedAny;
	}

	private static bool IsLikelyUtf8Text( byte[] data, int start, int length )
	{
		var printable = 0;
		for ( var i = 0; i < length; i++ )
		{
			var b = data[start + i];
			if ( b == 0 )
				continue;

			if ( b >= 0x20 && b <= 0x7E )
				printable++;
		}

		return printable >= Math.Max( 3, length / 2 );
	}

	private static byte GetLocaleFromInstanceId( ulong instanceId ) => (byte)(instanceId >> 56);

	private static void TryReadNameMapData( byte[] data, Dictionary<ulong, string> names )
	{
		if ( data.Length < 8 )
			return;

		try
		{
			using var stream = new MemoryStream( data, writable: false );
			using var br = new BinaryReader( stream, Encoding.UTF8, leaveOpen: false );

			var version = br.ReadUInt32();
			if ( version != 1 )
				return;

			var count = br.ReadInt32();
			if ( count <= 0 )
				return;

			for ( int i = 0; i < count && stream.Position < stream.Length; i++ )
			{
				var key = br.ReadUInt64();
				var length = br.ReadInt32();

				if ( length < 0 )
					break;

				var chars = br.ReadChars( length );
				var value = new string( chars ).Replace( "\0", string.Empty ).Trim();
				if ( string.IsNullOrWhiteSpace( value ) )
					continue;

				names[key] = value;
			}
		}
		catch
		{
			// Ignore malformed _KEY resources and continue with hash-based names.
		}
	}

	public bool TryReadData( DbpfRecord record, out byte[] data )
	{
		try
		{
			data = ReadData( record );
			return true;
		}
		catch
		{
			data = [];
			return false;
		}
	}

	/// <summary>
	/// Read and decompress the data for a record.
	/// Thread-safe: multiple loaders may call this concurrently.
	/// </summary>
	public byte[] ReadData( DbpfRecord record )
	{
		Log.Trace( $"Reading data for record: Type=0x{record.TypeId:X8} Group=0x{record.GroupId:X8} Instance=0x{record.InstanceId:X16}, Compressed={record.CompressedSize}, Decompressed={record.DecompressedSize}, Compression=0x{record.CompressionType:X4}" );

		byte[] compressed;

		lock ( _lock )
		{
			_stream.Seek( record.FileOffset, SeekOrigin.Begin );
			compressed = new byte[record.CompressedSize];
			_stream.ReadExactly( compressed );
		}

		var decompressed = Decompress( compressed, record.CompressionType, record.DecompressedSize );
		Log.Trace( $"Successfully decompressed data: {decompressed.Length} bytes" );
		return decompressed;
	}

	private static byte[] Decompress( byte[] data, ushort compressionType, int decompressedSize )
	{
		try
		{
			return compressionType switch
			{
				0x0000 => data,
				0x5A42 => DecompressZlib( data, decompressedSize ),
				0xFFFF or 0xFFFE => DecompressRefPack( data, decompressedSize ),
				_ => throw new InvalidDataException( $"Unsupported DBPF compression type: 0x{compressionType:X4}" ),
			};
		}
		catch ( Exception ex )
		{
			Log.Error( $"Decompression failed for compression type 0x{compressionType:X4}: {ex.Message}" );
			throw;
		}
	}

	private static byte[] DecompressZlib( byte[] data, int expectedSize )
	{
		using var compressed = new MemoryStream( data );
		using var zlib = new ZLibStream( compressed, CompressionMode.Decompress );
		var result = new byte[expectedSize];
		zlib.ReadExactly( result );
		return result;
	}

	/// <summary>
	/// Decompress RefPack / QFS compressed data (LZ77 variant used by EA/Maxis games).
	///
	/// Control byte encoding:
	///   0x00-0x7F: 2-byte block — plain 0-3, copy 3-10, offset 1-1024
	///   0x80-0xBF: 3-byte block — plain 0-3, copy 4-67, offset 1-16384
	///   0xC0-0xDF: 4-byte block — plain 0-3, copy 5-1028, offset 1-131072
	///   0xE0-0xFB: 1-byte block — plain 4-112 (multiples of 4), no copy
	///   0xFC-0xFF: end-of-stream — plain 0-3, no copy
	/// </summary>
	private static byte[] DecompressRefPack( byte[] data, int expectedSize )
	{
		using var input = new MemoryStream( data );

		// RefPack header: flags byte + 0xFB magic + decompressed size (big-endian, 3 or 4 bytes)
		int flags = input.ReadByte();
		if ( input.ReadByte() != 0xFB )
			throw new InvalidDataException( "Invalid RefPack header." );

		bool large = (flags & 0x80) != 0;
		// Skip the embedded size — we use the DBPF-provided expectedSize
		int skipBytes = large ? 4 : 3;
		for ( int i = 0; i < skipBytes; i++ ) input.ReadByte();

		var output = new byte[expectedSize];
		int outPos = 0;

		while ( input.Position < input.Length && outPos < expectedSize )
		{
			int b0 = input.ReadByte();
			if ( b0 < 0 ) break;

			int numPlain, numCopy, offset;

			if ( b0 < 0x80 )
			{
				// 2-byte block: 0_pp_ccc_oo + b1[7:0]
				//   pp  = plain count (bits 6-5)
				//   ccc = copy count - 3 (bits 4-2)
				//   oo  = offset high 2 bits (bits 1-0)
				int b1 = input.ReadByte();
				numPlain = (b0 >> 5) & 0x03;
				numCopy = ((b0 >> 2) & 0x07) + 3;
				offset = ((b0 & 0x03) << 8 | b1) + 1;
			}
			else if ( b0 < 0xC0 )
			{
				// 3-byte block: 10_cccccc + b1[7:6=pp, 5:0=off_hi] + b2[7:0=off_lo]
				//   cccccc = copy count - 4 (bits 5-0 of b0)
				//   pp     = plain count (bits 7-6 of b1)
				//   offset = (b1[5:0] << 8 | b2) + 1  (14-bit, 1-16384)
				int b1 = input.ReadByte();
				int b2 = input.ReadByte();
				numPlain = (b1 >> 6) & 0x03;
				numCopy = (b0 & 0x3F) + 4;
				offset = ((b1 & 0x3F) << 8 | b2) + 1;
			}
			else if ( b0 < 0xE0 )
			{
				// 4-byte block: 110_x_cc_pp + b1[7:0=off_hi] + b2[7:0=off_lo] + b3[7:0=copy_lo]
				//   x   = offset bit 16 (bit 4 of b0)
				//   cc  = copy count high bits (bits 3-2 of b0)
				//   pp  = plain count (bits 1-0 of b0)
				//   offset = (b0[4] << 16 | b1 << 8 | b2) + 1  (17-bit, 1-131072)
				//   copy   = (b0[3:2] << 6 | b3) + 5  (range 5-1028)
				int b1 = input.ReadByte();
				int b2 = input.ReadByte();
				int b3 = input.ReadByte();
				numPlain = b0 & 0x03;
				numCopy = ((b0 & 0x0C) << 6) + b3 + 5;
				offset = ((b0 & 0x10) << 12 | b1 << 8 | b2) + 1;
			}
			else if ( b0 < 0xFC )
			{
				// 1-byte literal run: 111_nnnnn → plain = (n+1)*4
				numPlain = ((b0 & 0x1F) + 1) << 2;
				numCopy = 0;
				offset = 0;
			}
			else
			{
				// End of stream: plain = b0 & 3
				numPlain = b0 & 0x03;
				numCopy = 0;
				offset = 0;

				for ( int i = 0; i < numPlain && outPos < expectedSize; i++ )
					output[outPos++] = (byte)input.ReadByte();

				break;
			}

			// Emit literal bytes
			for ( int i = 0; i < numPlain && outPos < expectedSize; i++ )
				output[outPos++] = (byte)input.ReadByte();

			// Copy from back-reference (may overlap: src advances with dst)
			int srcPos = outPos - offset;
			for ( int i = 0; i < numCopy && outPos < expectedSize; i++ )
				output[outPos++] = output[srcPos++];
		}

		return output;
	}

	private long FindDbpfHeader()
	{
		_stream.Seek( 0, SeekOrigin.Begin );

		// Read first few bytes to check for compression magic/wrappers
		byte[] magic = new byte[4];
		_stream.ReadExactly( magic );

		// Check for zlib/gzip magic
		if ( (magic[0] == 0x78 && (magic[1] == 0x01 || magic[1] == 0x5E || magic[1] == 0x9C || magic[1] == 0xDA)) ||
			 (magic[0] == 0x1F && magic[1] == 0x8B) )
		{
			Log.Warning( "File appears to be compressed (zlib/gzip). This package format is not currently supported." );
			throw new InvalidDataException( "Compressed DBPF packages are not supported." );
		}

		// Check for DBPF at position 0
		if ( magic[0] == 'D' && magic[1] == 'B' && magic[2] == 'P' && magic[3] == 'F' )
		{
			Log.Trace( "DBPF magic found at position 0x0" );
			_stream.Seek( 0, SeekOrigin.Begin );
			return 0;
		}

		// Search for DBPF magic in the file
		Log.Warning( $"DBPF magic not found at position 0. Got: 0x{magic[0]:X2}{magic[1]:X2}{magic[2]:X2}{magic[3]:X2}. Searching file for DBPF header..." );
		_stream.Seek( 0, SeekOrigin.Begin );

		byte[] buffer = new byte[65536];
		long filePos = 0;
		int bytesRead;

		while ( (bytesRead = _stream.Read( buffer, 0, buffer.Length )) > 0 )
		{
			for ( int i = 0; i <= bytesRead - 4; i++ )
			{
				if ( buffer[i] == 'D' && buffer[i + 1] == 'B' &&
					 buffer[i + 2] == 'P' && buffer[i + 3] == 'F' )
				{
					long foundPos = filePos + i;
					Log.Info( $"Found DBPF magic at position 0x{foundPos:X}" );
					_stream.Seek( foundPos, SeekOrigin.Begin );
					return foundPos;
				}
			}
			filePos += bytesRead;
		}

		throw new InvalidDataException( "DBPF magic signature not found in file. This does not appear to be a valid DBPF package file." );
	}

	public void Dispose() => _stream.Dispose();
}
