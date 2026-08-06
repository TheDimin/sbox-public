using System.IO;
using System.Text;

namespace Sandbox;

/// <summary>
/// Persistent source and network hashes for physical project files.
/// </summary>
internal sealed class FileHashCache
{
	internal const uint CacheMagic = 0x31484346;
	internal const int CacheVersion = 1;

	private const int MaxEntries = 1_000_000;
	private const int MaxPathBytes = 64 * 1024;
	private const byte SourceHashFlag = 1 << 0;
	private const byte CrcFlag = 1 << 1;
	private const byte KnownFlags = SourceHashFlag | CrcFlag;
	private static readonly UTF8Encoding PathEncoding = new( false, true );
	private static readonly object CurrentLock = new();
	private static readonly FileHashCache NonPersistent = new( null );
	private static FileHashCache _current;
	private static string _currentRoot;

	private readonly object _lock = new();
	private readonly Dictionary<string, Entry> _entries;
	private bool _dirty;

	internal string CachePath { get; }

	internal static FileHashCache Current
	{
		get
		{
			var root = Project.Current?.GetRootPath();
			if ( string.IsNullOrWhiteSpace( root ) )
				return NonPersistent;

			root = Path.GetFullPath( root );
			lock ( CurrentLock )
			{
				if ( _current is not null && PathsEqual( root, _currentRoot ) )
					return _current;

				_current?.Flush();
				_currentRoot = root;
				_current = new FileHashCache( root );
				return _current;
			}
		}
	}

	internal FileHashCache( string projectRoot )
	{
		_entries = new Dictionary<string, Entry>( PhysicalPathComparer );
		CachePath = string.IsNullOrWhiteSpace( projectRoot )
			? null
			: Path.Combine( Path.GetFullPath( projectRoot ), ".sbox", "cache", "file-hashes-v1.bin" );

		Load();
	}

	internal int GetOrComputeSourceHash( BaseFileSystem fs, string path, out bool cacheHit )
	{
		ArgumentNullException.ThrowIfNull( fs );
		ArgumentException.ThrowIfNullOrEmpty( path );

		if ( !TryGetPhysicalFile( fs, path, out var physicalPath, out var before ) )
		{
			cacheHit = false;
			return fs.ReadAllText( path ).FastHash();
		}

		if ( TryGetSourceHash( physicalPath, before, out var cached ) )
		{
			cacheHit = true;
			return cached;
		}

		cacheHit = false;
		var calculated = 0;
		for ( var attempt = 0; attempt < 2; attempt++ )
		{
			calculated = fs.ReadAllText( path ).FastHash();
			if ( TryReadMetadata( physicalPath, out var after ) && before == after )
			{
				StoreSourceHash( physicalPath, after, calculated );
				return calculated;
			}

			if ( attempt == 0 && TryReadMetadata( physicalPath, out before ) )
				continue;

			break;
		}

		return calculated;
	}

	internal ulong GetOrComputeCrc( BaseFileSystem fs, string path, bool forceRefresh, out long size, out bool cacheHit )
	{
		ArgumentNullException.ThrowIfNull( fs );
		ArgumentException.ThrowIfNullOrEmpty( path );

		if ( !TryGetPhysicalFile( fs, path, out var physicalPath, out var before ) )
		{
			cacheHit = false;
			var uncached = fs.GetCrc( path );
			size = fs.FileSize( path );
			return uncached;
		}

		if ( !forceRefresh && TryGetCrc( physicalPath, before, out var cached ) )
		{
			cacheHit = true;
			size = before.Size;
			return cached;
		}

		cacheHit = false;
		var calculated = 0UL;
		size = before.Size;
		for ( var attempt = 0; attempt < 2; attempt++ )
		{
			calculated = fs.GetCrc( path );
			if ( TryReadMetadata( physicalPath, out var after ) )
			{
				size = after.Size;
				if ( before == after )
				{
					StoreCrc( physicalPath, after, calculated );
					return calculated;
				}

				if ( attempt == 0 )
				{
					before = after;
					continue;
				}
			}

			break;
		}

		return calculated;
	}

	internal void Flush()
	{
		if ( CachePath is null )
			return;

		lock ( _lock )
		{
			if ( !_dirty )
				return;

			var directory = Path.GetDirectoryName( CachePath );
			var temporaryPath = Path.Combine( directory, $".{Path.GetFileName( CachePath )}.{Guid.NewGuid():N}.tmp" );
			try
			{
				Directory.CreateDirectory( directory );
				using ( var stream = new FileStream( temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None ) )
				using ( var writer = new BinaryWriter( stream, PathEncoding, true ) )
				{
					writer.Write( CacheMagic );
					writer.Write( CacheVersion );
					writer.Write( _entries.Count );
					foreach ( var (path, entry) in _entries.OrderBy( x => x.Key, PhysicalPathComparer ) )
					{
						WritePath( writer, path );
						writer.Write( entry.Size );
						writer.Write( entry.LastWriteUtcTicks );
						var flags = (byte)((entry.HasSourceHash ? SourceHashFlag : 0) | (entry.HasCrc ? CrcFlag : 0));
						writer.Write( flags );
						if ( entry.HasSourceHash ) writer.Write( entry.SourceHash );
						if ( entry.HasCrc ) writer.Write( entry.Crc );
					}

					writer.Flush();
					stream.Flush( true );
				}

				File.Move( temporaryPath, CachePath, true );
				_dirty = false;
			}
			catch ( Exception ex )
			{
				Log.Warning( ex, $"Failed to write file hash cache '{CachePath}'" );
				TryDelete( temporaryPath );
			}
		}
	}

	internal void Clear()
	{
		lock ( _lock )
		{
			_entries.Clear();
			_dirty = false;
			if ( CachePath is not null )
				TryDelete( CachePath );
		}
	}

	[ConCmd( "file_hash_cache_clear", ConVarFlags.Protected )]
	public static void ClearCurrentProjectCache()
	{
		Current.Clear();
		Log.Info( "File hash cache cleared" );
	}

	private void Load()
	{
		if ( CachePath is null || !File.Exists( CachePath ) )
			return;

		try
		{
			using var stream = new FileStream( CachePath, FileMode.Open, FileAccess.Read, FileShare.Read );
			using var reader = new BinaryReader( stream, PathEncoding, false );
			if ( reader.ReadUInt32() != CacheMagic || reader.ReadInt32() != CacheVersion )
				return;

			var count = reader.ReadInt32();
			if ( count < 0 || count > MaxEntries )
				throw new InvalidDataException( "Invalid file hash cache entry count" );

			var loaded = new Dictionary<string, Entry>( PhysicalPathComparer );
			for ( var i = 0; i < count; i++ )
			{
				var path = ReadPath( reader );
				if ( !Path.IsPathFullyQualified( path ) )
					throw new InvalidDataException( "File hash cache path is not absolute" );

				var entry = new Entry
				{
					Size = reader.ReadInt64(),
					LastWriteUtcTicks = reader.ReadInt64()
				};
				var flags = reader.ReadByte();
				if ( (flags & ~KnownFlags) != 0 )
					throw new InvalidDataException( "File hash cache flags are invalid" );

				entry.HasSourceHash = (flags & SourceHashFlag) != 0;
				entry.HasCrc = (flags & CrcFlag) != 0;
				if ( entry.HasSourceHash ) entry.SourceHash = reader.ReadInt32();
				if ( entry.HasCrc ) entry.Crc = reader.ReadUInt64();
				loaded[path] = entry;
			}

			if ( stream.Position != stream.Length )
				throw new InvalidDataException( "File hash cache contains trailing data" );

			foreach ( var pair in loaded )
				_entries[pair.Key] = pair.Value;
		}
		catch ( Exception ex ) when ( ex is IOException or UnauthorizedAccessException or InvalidDataException or DecoderFallbackException or ArgumentException )
		{
			_entries.Clear();
		}
	}

	private bool TryGetSourceHash( string path, Metadata metadata, out int hash )
	{
		lock ( _lock )
		{
			if ( _entries.TryGetValue( path, out var entry ) && entry.Matches( metadata ) && entry.HasSourceHash )
			{
				hash = entry.SourceHash;
				return true;
			}
		}

		hash = default;
		return false;
	}

	private bool TryGetCrc( string path, Metadata metadata, out ulong crc )
	{
		lock ( _lock )
		{
			if ( _entries.TryGetValue( path, out var entry ) && entry.Matches( metadata ) && entry.HasCrc )
			{
				crc = entry.Crc;
				return true;
			}
		}

		crc = default;
		return false;
	}

	private void StoreSourceHash( string path, Metadata metadata, int hash )
	{
		lock ( _lock )
		{
			var entry = GetCompatibleEntry( path, metadata );
			entry.HasSourceHash = true;
			entry.SourceHash = hash;
			_entries[path] = entry;
			_dirty = true;
		}
	}

	private void StoreCrc( string path, Metadata metadata, ulong crc )
	{
		lock ( _lock )
		{
			var entry = GetCompatibleEntry( path, metadata );
			entry.HasCrc = true;
			entry.Crc = crc;
			_entries[path] = entry;
			_dirty = true;
		}
	}

	private Entry GetCompatibleEntry( string path, Metadata metadata )
	{
		if ( _entries.TryGetValue( path, out var entry ) && entry.Matches( metadata ) )
			return entry;

		return new Entry
		{
			Size = metadata.Size,
			LastWriteUtcTicks = metadata.LastWriteUtcTicks
		};
	}

	private static bool TryGetPhysicalFile( BaseFileSystem fs, string path, out string physicalPath, out Metadata metadata )
	{
		physicalPath = fs.GetFullPath( path );
		if ( string.IsNullOrWhiteSpace( physicalPath ) || !Path.IsPathFullyQualified( physicalPath ) )
		{
			metadata = default;
			return false;
		}

		physicalPath = Path.GetFullPath( physicalPath );
		return TryReadMetadata( physicalPath, out metadata );
	}

	private static bool TryReadMetadata( string physicalPath, out Metadata metadata )
	{
		try
		{
			var info = new FileInfo( physicalPath );
			if ( !info.Exists )
			{
				metadata = default;
				return false;
			}

			metadata = new Metadata( info.Length, info.LastWriteTimeUtc.Ticks );
			return true;
		}
		catch ( Exception ex ) when ( ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException )
		{
			metadata = default;
			return false;
		}
	}

	private static void WritePath( BinaryWriter writer, string path )
	{
		var bytes = PathEncoding.GetBytes( path );
		writer.Write( bytes.Length );
		writer.Write( bytes );
	}

	private static string ReadPath( BinaryReader reader )
	{
		var length = reader.ReadInt32();
		if ( length <= 0 || length > MaxPathBytes )
			throw new InvalidDataException( "Invalid file hash cache path length" );

		var bytes = reader.ReadBytes( length );
		if ( bytes.Length != length )
			throw new EndOfStreamException();

		return PathEncoding.GetString( bytes );
	}

	private static void TryDelete( string path )
	{
		try
		{
			File.Delete( path );
		}
		catch ( Exception ex ) when ( ex is IOException or UnauthorizedAccessException )
		{
			Log.Warning( ex, $"Failed to delete file hash cache '{path}'" );
		}
	}

	private static StringComparer PhysicalPathComparer => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
		? StringComparer.OrdinalIgnoreCase
		: StringComparer.Ordinal;

	private static bool PathsEqual( string left, string right ) => PhysicalPathComparer.Equals( left, right );

	private readonly record struct Metadata( long Size, long LastWriteUtcTicks );

	private sealed class Entry
	{
		internal long Size;
		internal long LastWriteUtcTicks;
		internal bool HasSourceHash;
		internal int SourceHash;
		internal bool HasCrc;
		internal ulong Crc;

		internal bool Matches( Metadata metadata ) => Size == metadata.Size && LastWriteUtcTicks == metadata.LastWriteUtcTicks;
	}
}
