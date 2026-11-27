using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GtaV.ManagedReader;

/// <summary>A read-only index of the Enhanced install's RPF files and their DLC precedence.</summary>
internal sealed partial class ArchiveIndex
{
	internal readonly record struct Archive( string Path, int Parent, int ParentEntry, int FileCount );
	internal readonly record struct FileEntry( uint Hash, string Stem, string Extension, int Archive, int Entry, string Directory, uint Size, int Rank, int ScanOrder );
	internal readonly record struct ArchetypeEntry( int File, YtypCatalog.Archetype Data );

	internal ReadOnlySpan<Archive> Archives => archives;
	internal ReadOnlySpan<FileEntry> Files => files;
	internal ReadOnlySpan<DlcLoadOrder.Rank> Ranks => ranks;
	internal ReadOnlySpan<ArchetypeEntry> Archetypes => archetypes;
	internal ReadOnlySpan<TextureRelationships.Relationship> TextureParents => textureParents;
	internal IReadOnlyList<string> Warnings { get; }
	internal string WorldCatalogCache => PacketCachePath( "world-v1" );
	string indexCachePath;
	readonly Archive[] archives;
	readonly FileEntry[] files;
	readonly DlcLoadOrder.Rank[] ranks;
	readonly ArchetypeEntry[] archetypes;
	readonly TextureRelationships.Relationship[] textureParents;
	readonly (uint Hash, string Name)[] textureNames;

	readonly record struct FileSortKey( uint Hash, int Extension, int Rank, int Source ) : IComparable<FileSortKey>
	{
		public int CompareTo( FileSortKey other )
		{
			int order = Hash.CompareTo( other.Hash );
			if ( order == 0 ) order = Extension.CompareTo( other.Extension );
			if ( order == 0 ) order = Rank.CompareTo( other.Rank );
			return order == 0 ? Source.CompareTo( other.Source ) : order;
		}
	}

	readonly record struct ArchetypeSortKey( uint Name, int Rank, int Source ) : IComparable<ArchetypeSortKey>
	{
		public int CompareTo( ArchetypeSortKey other )
		{
			int order = Name.CompareTo( other.Name );
			if ( order == 0 ) order = Rank.CompareTo( other.Rank );
			return order == 0 ? Source.CompareTo( other.Source ) : order;
		}
	}

	ArchiveIndex( Archive[] archives, FileEntry[] files, DlcLoadOrder.Rank[] ranks, ArchetypeEntry[] archetypes,
		TextureRelationships.Relationship[] textureParents, (uint Hash, string Name)[] textureNames, string[] warnings )
	{
		this.archives = archives;
		this.files = files;
		this.ranks = ranks;
		this.archetypes = archetypes;
		this.textureParents = textureParents;
		this.textureNames = textureNames;
		Warnings = warnings;
	}

	internal static ArchiveIndex Open( string directory, string keyDirectory = null, string cacheDirectory = null, bool rebuild = false )
	{
		directory = Path.GetFullPath( directory );
		var executable = new FileInfo( Path.Combine( directory, "GTA5_Enhanced.exe" ) );
		if ( !executable.Exists || !File.Exists( Path.Combine( directory, "common.rpf" ) ) )
			throw new DirectoryNotFoundException( $"No GTA V Enhanced install at '{directory}'." );

		var paths = TopArchives( directory );
		string key = CacheKey( directory, executable, paths );
		cacheDirectory ??= Path.Combine( Environment.GetFolderPath( Environment.SpecialFolder.LocalApplicationData ), "sbox", "gtav", "managed-index" );
		string cachePath = Path.Combine( cacheDirectory, $"{key}.bin" );
		if ( !rebuild && ReadCache( cachePath ) is { } cached )
		{
			cached.indexCachePath = cachePath;
			return cached;
		}

		var keys = new RpfKeys( keyDirectory ?? RpfKeys.DefaultDirectory );
		var scans = new Scan[paths.Length];
		// Schedule larger archives first. Merge in the original file order so ties stay deterministic.
		var queue = Enumerable.Range( 0, paths.Length ).OrderByDescending( i => paths[i].Length ).ToArray();
		Parallel.ForEach( queue, new ParallelOptions { MaxDegreeOfParallelism = Math.Min( 8, Environment.ProcessorCount ) }, i =>
		{
			var info = paths[i];
			string path = Path.GetRelativePath( directory, info.FullName ).Replace( '\\', '/' );
			var scan = scans[i] = new Scan();
			try
			{
				using var file = File.OpenHandle( info.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.RandomAccess );
				var archive = new RpfArchive( file, 0, RandomAccess.GetLength( file ), info.Name, keys );
				scan.Add( archive, path, -1, 0, path.Equals( "update/update.rpf", StringComparison.OrdinalIgnoreCase ) );
			}
			catch ( Exception e ) when ( IsReadError( e ) ) { scan.Warnings.Add( $"{path}: {e.Message}" ); }
		} );
		if ( scans[0].Archives.Count == 0 ) throw new InvalidDataException( scans[0].Warnings[0] );
		var index = Merge( scans );
		// Do not keep an incomplete snapshot. A file or key repair must take effect on the next open.
		if ( index.Warnings.Count == 0 && key == CacheKey( directory, executable, TopArchives( directory ) ) )
		{
			WriteCache( cachePath, index );
			index.indexCachePath = cachePath;
		}
		return index;
	}

	internal string PacketCachePath( string name ) => indexCachePath is null ? null
		: Path.Combine( Path.GetDirectoryName( indexCachePath ), $"{name}-{Path.GetFileName( indexCachePath )}" );

	internal FileEntry? Find( uint hash, string extension ) => FindId( hash, extension ) is int id ? files[id] : null;

	internal int? FindId( uint hash, string extension )
	{
		extension = extension.ToLowerInvariant();
		if ( extension.Length > 0 && extension[0] != '.' ) extension = $".{extension}";
		// Upper bound of the hash and extension. The last match is the highest-ranked copy.
		int start = 0;
		int end = files.Length;
		while ( start < end )
		{
			int mid = start + (end - start) / 2;
			ref readonly var row = ref files[mid];
			if ( CompareName( row, hash, extension ) <= 0 ) start = mid + 1;
			else end = mid;
		}
		if ( start == 0 ) return null;
		ref readonly var candidate = ref files[start - 1];
		return CompareName( candidate, hash, extension ) == 0 && ranks[candidate.Rank].Active ? start - 1 : null;
	}

	static int CompareName( in FileEntry file, uint hash, string extension ) => file.Hash == hash
		? string.CompareOrdinal( file.Extension, extension ) : file.Hash.CompareTo( hash );

	static ArchiveIndex Merge( Scan[] scans )
	{
		var loadOrder = new DlcLoadOrder( scans.SelectMany( s => s.Setups ), scans.Select( s => s.ListedPacks ).LastOrDefault( list => list is not null ) ?? [] );
		var warnings = new List<string>();
		if ( !scans.Any( scan => scan.ListedPacks is not null ) ) warnings.Add( "Missing common/data/dlclist.xml. DLC packs are inactive." );
		var archives = new List<Archive>( scans.Sum( s => s.Archives.Count ) );
		var files = new FileEntry[scans.Sum( s => s.Files.Count )];
		var rankIds = new Dictionary<DlcLoadOrder.Rank, int>();
		var ranks = new List<DlcLoadOrder.Rank>();
		var archetypes = new List<ArchetypeEntry>( scans.Sum( s => s.Archetypes.Count ) );
		var relationships = new List<(int File, TextureRelationships.NamedRelationship[] Pairs)>();
		int at = 0;
		foreach ( var scan in scans )
		{
			warnings.AddRange( scan.Warnings );
			int archiveBase = archives.Count;
			int fileBase = at;
			foreach ( var row in scan.Archives ) archives.Add( row.Parent < 0 ? row : row with { Parent = row.Parent + archiveBase } );
			var directoryRanks = new int[scan.Directories.Count];
			for ( int i = 0; i < directoryRanks.Length; i++ )
			{
				var (archive, path) = scan.Directories[i];
				var rank = loadOrder.For( RpfArchive.JoinPath( scan.Archives[archive].Path, path ).ToLowerInvariant() );
				if ( !rankIds.TryGetValue( rank, out int id ) )
				{
					rankIds.Add( rank, id = ranks.Count );
					ranks.Add( rank );
				}
				directoryRanks[i] = id;
			}
			foreach ( var item in scan.Files )
			{
				var (archive, path) = scan.Directories[item.Directory];
				files[at] = new FileEntry( item.Hash, item.Stem, item.Extension, archiveBase + archive, item.Entry, path, item.Size, directoryRanks[item.Directory], at );
				at++;
			}
			foreach ( var row in scan.Archetypes ) archetypes.Add( row with { File = fileBase + row.File } );
			foreach ( var (file, pairs) in scan.Relationships ) relationships.Add( (fileBase + file, pairs) );
		}
		var extensions = scans.SelectMany( scan => scan.Extensions ).Distinct( StringComparer.Ordinal ).Order( StringComparer.Ordinal )
			.Select( (extension, index) => (extension, index) ).ToDictionary( item => item.extension, item => item.index, StringComparer.Ordinal );
		var rankOrder = new int[ranks.Count];
		int rankIndex = 0;
		foreach ( int id in Enumerable.Range( 0, ranks.Count ).OrderBy( id => ranks[id] ) ) rankOrder[id] = rankIndex++;
		var fileOrder = new FileSortKey[files.Length];
		for ( int i = 0; i < files.Length; i++ )
		{
			ref readonly var row = ref files[i];
			fileOrder[i] = new FileSortKey( row.Hash, extensions[row.Extension], rankOrder[row.Rank], i );
		}
		Array.Sort( fileOrder );
		var sortedFiles = new FileEntry[files.Length];
		var sortedPosition = new int[files.Length];
		for ( int i = 0; i < fileOrder.Length; i++ )
		{
			int source = fileOrder[i].Source;
			sortedFiles[i] = files[source];
			sortedPosition[source] = i;
		}
		var archetypeOrder = new ArchetypeSortKey[archetypes.Count];
		for ( int i = 0; i < archetypeOrder.Length; i++ )
		{
			var row = archetypes[i];
			archetypeOrder[i] = new ArchetypeSortKey( row.Data.Name, rankOrder[files[row.File].Rank], i );
		}
		Array.Sort( archetypeOrder );
		var sortedArchetypes = new ArchetypeEntry[archetypeOrder.Length];
		for ( int i = 0; i < archetypeOrder.Length; i++ )
		{
			var row = archetypes[archetypeOrder[i].Source];
			sortedArchetypes[i] = row with { File = sortedPosition[row.File] };
		}
		var parents = new Dictionary<uint, uint>();
		var names = new Dictionary<uint, string>();
		// The highest-ranked definition wins. For equal ranks, the first definition wins for texture parents.
		foreach ( var (_, pairs) in relationships.OrderByDescending( input => rankOrder[files[input.File].Rank] ) )
			foreach ( var pair in pairs ) parents.TryAdd( Remember( pair.Child ), Remember( pair.Parent ) );
		var sortedParents = parents.OrderBy( row => row.Key ).Select( row => new TextureRelationships.Relationship( row.Key, row.Value ) ).ToArray();
		var sortedNames = names.OrderBy( row => row.Key ).Select( row => (row.Key, row.Value) ).ToArray();
		return new ArchiveIndex( archives.ToArray(), sortedFiles, ranks.ToArray(), sortedArchetypes, sortedParents, sortedNames, warnings.ToArray() );

		uint Remember( string name )
		{
			uint hash = HashName( name );
			if ( !names.TryGetValue( hash, out string previous ) || string.CompareOrdinal( name, previous ) < 0 ) names[hash] = name;
			return hash;
		}
	}

	static FileInfo[] TopArchives( string directory )
	{
		var root = new DirectoryInfo( directory );
		var files = root.EnumerateFiles( "*.rpf" ).Where( file => file.Name.Equals( "common.rpf", StringComparison.OrdinalIgnoreCase ) || file.Name.StartsWith( "x64", StringComparison.OrdinalIgnoreCase ) )
			.OrderBy( file => file.Name.ToLowerInvariant(), StringComparer.Ordinal ).ToList();
		files.AddRange( EnumerateRpf( Path.Combine( directory, "x64" ) ) );
		foreach ( string name in new[] { "update.rpf", "update2.rpf" } )
		{
			var file = new FileInfo( Path.Combine( directory, "update", name ) );
			if ( file.Exists ) files.Add( file );
		}
		files.AddRange( EnumerateRpf( Path.Combine( directory, "update", "x64", "dlcpacks" ) )
			.Where( file => file.Name.Length <= 12 && file.Name.StartsWith( "dlc", StringComparison.OrdinalIgnoreCase ) ) );
		return files.ToArray();
	}

	static IEnumerable<FileInfo> EnumerateRpf( string folder ) => Directory.Exists( folder )
		? new DirectoryInfo( folder ).EnumerateFiles( "*.rpf", SearchOption.AllDirectories )
			.OrderBy( file => file.FullName.Replace( '\\', '/' ).ToLowerInvariant(), StringComparer.Ordinal )
		: [];

	static bool IsReadError( Exception error ) => error is IOException or InvalidDataException or UnauthorizedAccessException or System.Xml.XmlException;

	internal static uint HashName( string name, bool lower = true )
	{
		uint hash = 0;
		if ( name.AsSpan().IndexOfAnyInRange( '\u0080', char.MaxValue ) < 0 )
		{
			foreach ( char character in name ) hash = AddByte( hash, (byte)character, lower );
		}
		else
		{
			foreach ( byte value in Encoding.UTF8.GetBytes( name ) ) hash = AddByte( hash, value, lower );
		}
		hash = unchecked(hash + (hash << 3));
		hash ^= hash >> 11;
		return unchecked(hash + (hash << 15));
	}

	static uint AddByte( uint hash, byte value, bool lower )
	{
		hash = unchecked(hash + (lower && value is >= (byte)'A' and <= (byte)'Z' ? (uint)value + 32 : value));
		hash = unchecked(hash + (hash << 10));
		return hash ^ (hash >> 6);
	}

	sealed class Scan
	{
		internal readonly List<Archive> Archives = new();
		internal readonly List<(uint Hash, string Stem, string Extension, int Entry, int Directory, uint Size)> Files = new();
		internal readonly List<(int Archive, string Path)> Directories = new();
		internal readonly List<(string Path, int Order)> Setups = new();
		internal readonly List<ArchetypeEntry> Archetypes = new();
		internal readonly List<(int File, TextureRelationships.NamedRelationship[] Pairs)> Relationships = new();
		internal readonly List<string> Warnings = new();
		internal string[] ListedPacks;
		readonly Dictionary<string, (uint Hash, string Stem, string Extension)> names = new( StringComparer.Ordinal );
		readonly Dictionary<string, string> extensions = new( StringComparer.Ordinal );
		internal IEnumerable<string> Extensions => extensions.Keys;

		internal void Add( RpfArchive archive, string path, int parent, int parentEntry, bool update, int depth = 0 )
		{
			if ( depth >= 64 ) throw new InvalidDataException( "Too many nested RPF archives." );
			int slot = Archives.Count;
			Archives.Add( new Archive( path, parent, parentEntry, 0 ) );
			bool pack = archive.Name.Equals( "dlc.rpf", StringComparison.OrdinalIgnoreCase );
			int setupOrder = 0;
			var entries = archive.Entries;
			var directories = new int[entries.Length];
			for ( int i = 0; i < entries.Length; i++ )
			{
				if ( !entries[i].IsDirectory ) continue;
				directories[i] = Directories.Count;
				Directories.Add( (slot, i == 0 ? string.Empty : RpfArchive.JoinPath( archive.DirectoryOf( entries[i] ), entries[i].Name )) );
			}
			int fileCount = 0;
			for ( int i = 1; i < entries.Length; i++ )
			{
				ref readonly var entry = ref entries[i];
				if ( entry.IsDirectory ) continue;
				fileCount++;
				var name = IndexName( entry.Name );
				string directory = archive.DirectoryOf( entry );
				int file = Files.Count;
				Files.Add( (name.Hash, name.Stem, name.Extension, i, directories[entry.Parent], entry.Size) );
				try
				{
					if ( entry.IsArchive )
						Add( archive.OpenChild( i ), RpfArchive.JoinPath( path, RpfArchive.JoinPath( directory, entry.Name ) ), slot, i, false, depth + 1 );
					else if ( name.Extension == ".ytyp" )
					{
						foreach ( var row in YtypCatalog.Read( archive.Extract( i ) ) ) Archetypes.Add( new ArchetypeEntry( file, row ) );
					}
					else if ( TextureRelationships.ContainsRelationships( name.Stem, name.Extension ) )
						Relationships.Add( (file, TextureRelationships.Read( archive.Extract( i ) )) );
					else if ( update && directory.Equals( "common/data", StringComparison.OrdinalIgnoreCase ) && entry.Name.Equals( "dlclist.xml", StringComparison.OrdinalIgnoreCase ) )
						ListedPacks = DlcLoadOrder.ReadListedPacks( archive.Extract( i ) );
					else if ( pack && entry.Parent == 0 && entry.Name.Equals( "setup2.xml", StringComparison.OrdinalIgnoreCase ) )
						setupOrder = DlcLoadOrder.ReadSetupOrder( archive.Extract( i ) );
				}
				catch ( Exception e ) when ( IsReadError( e ) ) { Warnings.Add( $"{path}/{RpfArchive.JoinPath( directory, entry.Name )}: {e.Message}" ); }
			}
			Archives[slot] = Archives[slot] with { FileCount = fileCount };
			if ( pack ) Setups.Add( (path, setupOrder) );
		}

		(uint Hash, string Stem, string Extension) IndexName( string name )
		{
			if ( names.TryGetValue( name, out var indexed ) ) return indexed;
			string lower = name.ToLowerInvariant();
			int dot = lower.LastIndexOf( '.' );
			string stem = dot > 0 ? lower[..dot] : lower;
			string extension = dot > 0 ? lower[dot..] : string.Empty;
			if ( extensions.TryGetValue( extension, out string known ) ) extension = known;
			else extensions.Add( extension, extension );
			indexed = (HashName( stem ), stem, extension);
			names.Add( name, indexed );
			return indexed;
		}
	}
}
