using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using NumericsVector3 = System.Numerics.Vector3;

namespace GtaV.ManagedReader;

internal sealed partial class ArchiveIndex
{
	const uint CacheMagic = 0x49415447; // GTAI
	const int CacheVersion = 5;
	const int CacheFileRowSize = 9 * sizeof(int);
	const int CacheArchetypeRowSize = 16 * sizeof(int) + 2;
	const int CacheBatchSize = 1024;

	static string CacheKey( string directory, FileInfo executable, FileInfo[] paths )
	{
		executable.Refresh();
		using var input = new MemoryStream();
		using var writer = new BinaryWriter( input );
		writer.Write( CacheVersion );
		writer.Write( directory );
		writer.Write( executable.Length );
		writer.Write( executable.LastWriteTimeUtc.Ticks );
		foreach ( var path in paths )
		{
			writer.Write( Path.GetRelativePath( directory, path.FullName ) );
			writer.Write( path.Length );
			writer.Write( path.LastWriteTimeUtc.Ticks );
		}
		return Convert.ToHexString( SHA256.HashData( input.GetBuffer().AsSpan( 0, (int)input.Length ) ) );
	}

	static ArchiveIndex ReadCache( string path )
	{
		try
		{
			using var file = new FileStream( path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 65536, FileOptions.SequentialScan );
			using var reader = new BinaryReader( file );
			if ( reader.ReadUInt32() != CacheMagic || reader.ReadInt32() != CacheVersion ) return null;
			var archives = new Archive[ReadCount( reader, 13 )];
			for ( int i = 0; i < archives.Length; i++ )
			{
				string name = reader.ReadString();
				int parent = reader.ReadInt32();
				int entry = reader.ReadInt32();
				int count = reader.ReadInt32();
				if ( parent < -1 || parent >= i || entry < 0 || count < 0 ) throw new InvalidDataException( "Invalid cached archive row." );
				archives[i] = new Archive( name, parent, entry, count );
			}
			var ranks = new DlcLoadOrder.Rank[ReadCount( reader, 14 )];
			for ( int i = 0; i < ranks.Length; i++ )
				ranks[i] = new DlcLoadOrder.Rank( reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadBoolean(), reader.ReadString() );
			var strings = ReadStrings( reader );
			var files = new FileEntry[ReadCount( reader, CacheFileRowSize )];
			ReadFiles( file, strings, archives.Length, ranks, files );
			var archetypes = new ArchetypeEntry[ReadCount( reader, CacheArchetypeRowSize )];
			ReadArchetypes( file, files, archetypes );
			var parents = new TextureRelationships.Relationship[ReadCount( reader, 2 * sizeof(uint) )];
			for ( int i = 0; i < parents.Length; i++ )
			{
				parents[i] = new( reader.ReadUInt32(), reader.ReadUInt32() );
				if ( i > 0 && parents[i - 1].Child >= parents[i].Child ) throw new InvalidDataException( "Invalid cached texture parent order." );
			}
			var names = new (uint Hash, string Name)[ReadCount( reader, sizeof(uint) + 1 )];
			for ( int i = 0; i < names.Length; i++ )
			{
				names[i] = (reader.ReadUInt32(), reader.ReadString());
				if ( i > 0 && names[i - 1].Hash >= names[i].Hash ) throw new InvalidDataException( "Invalid cached texture name order." );
			}
			if ( file.Position != file.Length ) throw new InvalidDataException( "Unexpected trailing data in the archive index cache." );
			return new ArchiveIndex( archives, files, ranks, archetypes, parents, names, [] );
		}
		catch ( Exception e ) when ( IsReadError( e ) || e is FormatException ) { return null; }
	}

	static int ReadCount( BinaryReader reader, int minBytes )
	{
		int count = reader.ReadInt32();
		if ( count < 0 || (long)count * minBytes > reader.BaseStream.Length - reader.BaseStream.Position )
			throw new InvalidDataException( "Invalid archive cache table size." );
		return count;
	}

	static int ReadIndex( ReadOnlySpan<byte> bytes, int count )
	{
		int index = BinaryPrimitives.ReadInt32LittleEndian( bytes );
		if ( (uint)index >= (uint)count ) throw new InvalidDataException( "Invalid archive cache table index." );
		return index;
	}

	static string[] ReadStrings( BinaryReader reader )
	{
		var strings = new string[ReadCount( reader, sizeof(int) )];
		var offsets = new int[strings.Length];
		for ( int i = 0; i < offsets.Length; i++ ) offsets[i] = reader.ReadInt32();
		int length = ReadCount( reader, 1 );
		var bytes = ArrayPool<byte>.Shared.Rent( length );
		try
		{
			reader.BaseStream.ReadExactly( bytes.AsSpan( 0, length ) );
			int start = 0;
			for ( int i = 0; i < offsets.Length; i++ )
			{
				int end = offsets[i];
				if ( end < start || end > length ) throw new InvalidDataException( "Invalid archive cache string offset." );
				strings[i] = Encoding.UTF8.GetString( bytes, start, end - start );
				start = end;
			}
			if ( start != length ) throw new InvalidDataException( "Invalid archive cache string pool length." );
			return strings;
		}
		finally { ArrayPool<byte>.Shared.Return( bytes ); }
	}

	static void WriteStrings( BinaryWriter writer, List<string> strings )
	{
		writer.Write( strings.Count );
		int length = 0;
		foreach ( string value in strings )
		{
			length = checked(length + Encoding.UTF8.GetByteCount( value ));
			writer.Write( length );
		}
		writer.Write( length );
		var bytes = ArrayPool<byte>.Shared.Rent( length );
		try
		{
			int offset = 0;
			foreach ( string value in strings ) offset += Encoding.UTF8.GetBytes( value, bytes.AsSpan( offset ) );
			writer.Write( bytes.AsSpan( 0, length ) );
		}
		finally { ArrayPool<byte>.Shared.Return( bytes ); }
	}

	static void ReadFiles( Stream file, string[] strings, int archiveCount, DlcLoadOrder.Rank[] ranks, FileEntry[] files )
	{
		var buffer = ArrayPool<byte>.Shared.Rent( CacheBatchSize * CacheFileRowSize );
		try
		{
			for ( int start = 0; start < files.Length; start += CacheBatchSize )
			{
				int count = Math.Min( CacheBatchSize, files.Length - start );
				var rows = buffer.AsSpan( 0, count * CacheFileRowSize );
				file.ReadExactly( rows );
				for ( int row = 0; row < count; row++ )
				{
					var bytes = rows.Slice( row * CacheFileRowSize, CacheFileRowSize );
					int i = start + row;
					uint hash = Word( bytes, 0 );
					string stem = strings[ReadIndex( bytes[4..], strings.Length )];
					string extension = strings[ReadIndex( bytes[8..], strings.Length )];
					int archive = ReadIndex( bytes[12..], archiveCount );
					int entry = BinaryPrimitives.ReadInt32LittleEndian( bytes[16..] );
					string directory = strings[ReadIndex( bytes[20..], strings.Length )];
					uint size = Word( bytes, 24 );
					int rank = ReadIndex( bytes[28..], ranks.Length );
					int order = ReadIndex( bytes[32..], files.Length );
					if ( entry <= 0 ) throw new InvalidDataException( "Invalid cached file entry." );
					files[i] = new FileEntry( hash, stem, extension, archive, entry, directory, size, rank, order );
					if ( i == 0 ) continue;
					ref readonly var previous = ref files[i - 1];
					int name = CompareName( previous, hash, extension );
					if ( name > 0 ) throw new InvalidDataException( "Invalid cached file ordering." );
					if ( name == 0 )
					{
						int precedence = ranks[previous.Rank].CompareTo( ranks[rank] );
						if ( precedence > 0 || (precedence == 0 && previous.ScanOrder >= order) ) throw new InvalidDataException( "Invalid cached file precedence." );
					}
				}
			}
		}
		finally { ArrayPool<byte>.Shared.Return( buffer ); }
	}

	static void ReadArchetypes( Stream file, FileEntry[] files, ArchetypeEntry[] archetypes )
	{
		var buffer = ArrayPool<byte>.Shared.Rent( CacheBatchSize * CacheArchetypeRowSize );
		try
		{
			for ( int start = 0; start < archetypes.Length; start += CacheBatchSize )
			{
				int count = Math.Min( CacheBatchSize, archetypes.Length - start );
				var rows = buffer.AsSpan( 0, count * CacheArchetypeRowSize );
				file.ReadExactly( rows );
				for ( int i = 0; i < count; i++ )
				{
					var row = rows.Slice( i * CacheArchetypeRowSize, CacheArchetypeRowSize );
					int source = ReadIndex( row, files.Length );
					var kind = (YtypCatalog.Kind)row[64];
					if ( kind > YtypCatalog.Kind.Interior || files[source].Extension != ".ytyp" ) throw new InvalidDataException( "Invalid cached archetype row." );
					var archetype = new YtypCatalog.Archetype( Word( row, 4 ), Word( row, 8 ), Word( row, 12 ), Word( row, 16 ),
						Word( row, 20 ), Word( row, 24 ), Word( row, 28 ), Word( row, 32 ), BinaryPrimitives.ReadSingleLittleEndian( row[36..] ),
						ReadVector( row[40..] ), ReadVector( row[52..] ), kind, unchecked((sbyte)row[65]) );
					int at = start + i;
					archetypes[at] = new ArchetypeEntry( source, archetype );
					if ( at > 0 && archetypes[at - 1].Data.Name > archetype.Name ) throw new InvalidDataException( "Invalid cached archetype order." );
				}
			}
		}
		finally { ArrayPool<byte>.Shared.Return( buffer ); }
	}

	static uint Word( ReadOnlySpan<byte> row, int offset ) => BinaryPrimitives.ReadUInt32LittleEndian( row[offset..] );
	static NumericsVector3 ReadVector( ReadOnlySpan<byte> row ) =>
		new( BinaryPrimitives.ReadSingleLittleEndian( row ), BinaryPrimitives.ReadSingleLittleEndian( row[4..] ), BinaryPrimitives.ReadSingleLittleEndian( row[8..] ) );
	static void WriteVector( Span<byte> row, NumericsVector3 vector )
	{
		BinaryPrimitives.WriteSingleLittleEndian( row, vector.X );
		BinaryPrimitives.WriteSingleLittleEndian( row[4..], vector.Y );
		BinaryPrimitives.WriteSingleLittleEndian( row[8..], vector.Z );
	}

	static void WriteCache( string path, ArchiveIndex index )
	{
		string temp = $"{path}.{Guid.NewGuid():N}.tmp";
		try
		{
			Directory.CreateDirectory( Path.GetDirectoryName( path ) );
			using ( var file = new FileStream( temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536 ) )
			using ( var writer = new BinaryWriter( file ) )
			{
				writer.Write( CacheMagic );
				writer.Write( CacheVersion );
				writer.Write( index.archives.Length );
				foreach ( ref readonly var archive in index.Archives )
				{
					writer.Write( archive.Path );
					writer.Write( archive.Parent );
					writer.Write( archive.ParentEntry );
					writer.Write( archive.FileCount );
				}
				writer.Write( index.ranks.Length );
				foreach ( ref readonly var rank in index.Ranks )
				{
					writer.Write( rank.Tier );
					writer.Write( rank.Order );
					writer.Write( rank.Mount );
					writer.Write( rank.Patch );
					writer.Write( rank.Pack );
				}
				var strings = new List<string>();
				var ids = new Dictionary<string, int>( StringComparer.Ordinal );
				foreach ( ref readonly var row in index.Files )
				{
					AddString( row.Stem );
					AddString( row.Extension );
					AddString( row.Directory );
				}
				WriteStrings( writer, strings );
				writer.Write( index.files.Length );
				Span<byte> bytes = stackalloc byte[CacheFileRowSize];
				foreach ( ref readonly var row in index.Files )
				{
					BinaryPrimitives.WriteUInt32LittleEndian( bytes, row.Hash );
					BinaryPrimitives.WriteInt32LittleEndian( bytes[4..], ids[row.Stem] );
					BinaryPrimitives.WriteInt32LittleEndian( bytes[8..], ids[row.Extension] );
					BinaryPrimitives.WriteInt32LittleEndian( bytes[12..], row.Archive );
					BinaryPrimitives.WriteInt32LittleEndian( bytes[16..], row.Entry );
					BinaryPrimitives.WriteInt32LittleEndian( bytes[20..], ids[row.Directory] );
					BinaryPrimitives.WriteUInt32LittleEndian( bytes[24..], row.Size );
					BinaryPrimitives.WriteInt32LittleEndian( bytes[28..], row.Rank );
					BinaryPrimitives.WriteInt32LittleEndian( bytes[32..], row.ScanOrder );
					file.Write( bytes );
				}
				writer.Write( index.archetypes.Length );
				Span<byte> archetypeRow = stackalloc byte[CacheArchetypeRowSize];
				foreach ( ref readonly var row in index.Archetypes )
				{
					var archetype = row.Data;
					BinaryPrimitives.WriteInt32LittleEndian( archetypeRow, row.File );
					BinaryPrimitives.WriteUInt32LittleEndian( archetypeRow[4..], archetype.Name );
					BinaryPrimitives.WriteUInt32LittleEndian( archetypeRow[8..], archetype.Asset );
					BinaryPrimitives.WriteUInt32LittleEndian( archetypeRow[12..], archetype.Texture );
					BinaryPrimitives.WriteUInt32LittleEndian( archetypeRow[16..], archetype.Drawable );
					BinaryPrimitives.WriteUInt32LittleEndian( archetypeRow[20..], archetype.Physics );
					BinaryPrimitives.WriteUInt32LittleEndian( archetypeRow[24..], archetype.Clip );
					BinaryPrimitives.WriteUInt32LittleEndian( archetypeRow[28..], archetype.Flags );
					BinaryPrimitives.WriteUInt32LittleEndian( archetypeRow[32..], archetype.Ytyp );
					BinaryPrimitives.WriteSingleLittleEndian( archetypeRow[36..], archetype.LodDistance );
					WriteVector( archetypeRow[40..], archetype.Min );
					WriteVector( archetypeRow[52..], archetype.Max );
					archetypeRow[64] = (byte)archetype.Type;
					archetypeRow[65] = unchecked((byte)archetype.AssetType);
					file.Write( archetypeRow );
				}
				writer.Write( index.textureParents.Length );
				foreach ( ref readonly var pair in index.TextureParents )
				{
					writer.Write( pair.Child );
					writer.Write( pair.Parent );
				}
				writer.Write( index.textureNames.Length );
				foreach ( var (hash, name) in index.textureNames )
				{
					writer.Write( hash );
					writer.Write( name );
				}

				void AddString( string value )
				{
					if ( ids.TryAdd( value, strings.Count ) ) strings.Add( value );
				}
			}
			File.Move( temp, path, overwrite: true );
		}
		catch ( Exception e ) when ( IsReadError( e ) )
		{
			// The index is usable in memory even if the local cache can't be written.
			try { File.Delete( temp ); }
			catch ( Exception cleanup ) when ( IsReadError( cleanup ) ) { }
		}
	}
}
