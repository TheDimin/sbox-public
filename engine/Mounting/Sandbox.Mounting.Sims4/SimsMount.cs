using System;
using System.Collections.Concurrent;
using Microsoft.Win32;
using Mounting.Sims4;
using Sims4Reader;
using Sims4Reader.Resources;


public class SimsMount : BaseGameMount
{
	new internal static Sandbox.Diagnostics.Logger Log = new Sandbox.Diagnostics.Logger( "Sims4-Mount" );
	public override string Ident => "sims4";
	public override string Title => "The Sims 4";

	private List<DbpfPackage> packages = new List<DbpfPackage>();

	/// <summary>
	/// The active SimsMount instance, if any. Set during Mount, cleared on Shutdown.
	/// </summary>
	internal static SimsMount? Instance { get; private set; }

	/// <summary>
	/// Returns all currently mounted packages. Used by debug commands.
	/// </summary>
	public IReadOnlyList<DbpfPackage> GetPackages() => packages;

	// TS4 on Steam
	const long AppId = 1222670;
	const string ClientIconHash = "ca6bc8b2411bce4a2cd325ab75f0204bc3a4ad98";

	string? _gameDir;
	string? _steamIconPath;
	protected override void Initialize( InitializeContext context )
	{
		// 1. Steam
		if ( context.IsAppInstalled( AppId ) )
		{
			Log.Info( "Sims 4 Steam install detected" );
			var dir = context.GetAppDirectory( AppId );
			if ( System.IO.Directory.Exists( dir ) )
			{
				Log.Info( $"Sims 4 Steam directory: {dir}" );
				_gameDir = dir;
				IsInstalled = true;

				// Resolve Steam client icon from the library cache.
				// The Steam install path comes from the registry because the game
				// may be installed in a different Steam library folder.
				if ( OperatingSystem.IsWindows() )
				{
					var steamPath = Registry.GetValue( @"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null ) as string;
					if ( !string.IsNullOrEmpty( steamPath ) )
					{
						var iconPath = System.IO.Path.Combine( steamPath, "appcache", "librarycache", AppId.ToString(), $"{ClientIconHash}.jpg" );
						if ( System.IO.File.Exists( iconPath ) )
							_steamIconPath = iconPath;
					}
				}

				return;
			}
		}

		//Detecting it trough EA app requires us to know if the user owns the game...
		return;
	}

	/// <summary>
	/// Lightweight entry collected during parallel Phase 1.
	/// No string paths or loader objects are allocated here — only TGI + package reference.
	/// </summary>
	private readonly struct RawEntry
	{
		public readonly DbpfPackage Package;
		public readonly ResourceEntry Entry;
		public readonly EntryKind Kind;

		public RawEntry( DbpfPackage package, ResourceEntry entry, EntryKind kind )
		{
			Package = package;
			Entry = entry;
			Kind = kind;
		}
	}

	private enum EntryKind : byte { Model, CatalogObject, CatalogFloor, CatalogWall }

	protected override Task Mount( MountContext context )
	{
		if ( string.IsNullOrWhiteSpace( _gameDir ) || !System.IO.Directory.Exists( _gameDir ) )
			return Task.CompletedTask;

		var dataDir = System.IO.Path.Combine( _gameDir, "Data" );
		if ( !System.IO.Directory.Exists( dataDir ) )
			return Task.CompletedTask;

		// Mount the Steam client icon if available
		if ( _steamIconPath != null )
			context.Add( Sandbox.Mounting.ResourceType.Texture, "icon", new SteamIconLoader( _steamIconPath ) );

		var files = System.IO.Directory.GetFiles( dataDir, "*.package", SearchOption.AllDirectories );

		// Phase 1 (parallel): Open packages, collect TGI entries, extract COBJ categories,
		// and build OBJD→MODL name+category mappings — all in one pass for I/O overlap.
		var dedup = new ConcurrentDictionary<ResourceKey, RawEntry>(
			concurrencyLevel: Environment.ProcessorCount, capacity: 32000 );
		var modelMeta = new ConcurrentDictionary<ResourceKey, (string Name, string? Category)>(
			concurrencyLevel: Environment.ProcessorCount, capacity: 26000 );
		var cobjCategories = new ConcurrentDictionary<ulong, string>(
			concurrencyLevel: Environment.ProcessorCount, capacity: 8000 );
		var openedPackages = new DbpfPackage[files.Length];

		Parallel.For( 0, files.Length, i =>
		{
			try
			{
				var package = DbpfPackage.Open( files[i] );
				openedPackages[i] = package;
				CollectEntries( package, dedup );

				// Extract COBJ categories from tags (COBJ and OBJD share instance IDs)
				foreach ( var entry in package.FindAll( Sims4Reader.ResourceType.CatalogObject ) )
				{
					if ( entry.MemSize == 0 || entry.FileSize == 0 ) continue;
					try
					{
						var cobj = package.GetResource<CatalogObjectResource>( entry );
						var category = BuyCategoryTag.GetCategory( cobj.Tags );
						if ( category != null )
							cobjCategories.TryAdd( entry.Key.Instance, category );
					}
					catch { }
				}

				// Extract OBJD→MODL name+category mappings
				try { CollectObjdModelNames( package, modelMeta, cobjCategories ); }
				catch { }
			}
			catch ( Exception ex )
			{
				Log.Error( $"Failed to open package {files[i]}: {ex}" );
			}
		} );

		// Collect opened packages
		for ( int i = 0; i < openedPackages.Length; i++ )
		{
			if ( openedPackages[i] != null )
				packages.Add( openedPackages[i] );
		}

		// Phase 2 (sequential): Build paths + loaders + register only unique entries.
		foreach ( var raw in dedup.Values )
		{
			var key = raw.Entry.Key;
			switch ( raw.Kind )
			{
				case EntryKind.Model:
				{
					string name;
					if ( modelMeta.TryGetValue( key, out var meta ) )
					{
						var category = meta.Category ?? "objects";
						name = string.Concat( "models/", category, "/", StripVariantSuffix( meta.Name ) );
					}
					else
					{
						name = string.Concat( "models/undefined/", key.Instance.ToString( "x" ) );
					}

					context.Add( Sandbox.Mounting.ResourceType.Model, name,
						new ModlLoader( raw.Package, raw.Entry, packages ) );
					break;
				}
				case EntryKind.CatalogObject:
				{
					var path = string.Concat( "catalogs/", key.Instance.ToString( "x" ), ".s4cor" );
					context.Add( Sandbox.Mounting.ResourceType.Text, path,
						new CatalogObjectLoader( raw.Package, raw.Entry, packages ) );
					break;
				}
				case EntryKind.CatalogFloor:
				{
					var path = string.Concat( "catalogs/floors/", key.Instance.ToString( "x" ), ".s4sur" );
					context.Add( Sandbox.Mounting.ResourceType.Text, path,
						new CatalogSurfaceLoader( raw.Package, raw.Entry, packages, "floor" ) );
					break;
				}
				case EntryKind.CatalogWall:
				{
					var path = string.Concat( "catalogs/walls/", key.Instance.ToString( "x" ), ".s4sur" );
					context.Add( Sandbox.Mounting.ResourceType.Text, path,
						new CatalogSurfaceLoader( raw.Package, raw.Entry, packages, "wall" ) );
					break;
				}
			}
		}

		Instance = this;
		IsMounted = true;
		return Task.CompletedTask;
	}

	/// <summary>
	/// Collect lightweight entries from a package directly into the shared dedup map.
	/// Thread-safe: ConcurrentDictionary handles contention; last writer wins.
	/// </summary>
	private static void CollectEntries(
		DbpfPackage package,
		ConcurrentDictionary<ResourceKey, RawEntry> dedup )
	{
		foreach ( var entry in package.FindAll( Sims4Reader.ResourceType.Model ) )
		{
			if ( entry.MemSize == 0 || entry.FileSize == 0 ) continue;
			dedup[entry.Key] = new RawEntry( package, entry, EntryKind.Model );
		}

		foreach ( var entry in package.FindAll( Sims4Reader.ResourceType.CatalogFloor ) )
		{
			if ( entry.MemSize == 0 || entry.FileSize == 0 ) continue;
			dedup[entry.Key] = new RawEntry( package, entry, EntryKind.CatalogFloor );
		}

		foreach ( var entry in package.FindAll( Sims4Reader.ResourceType.CatalogFlooring ) )
		{
			if ( entry.MemSize == 0 || entry.FileSize == 0 ) continue;
			dedup[entry.Key] = new RawEntry( package, entry, EntryKind.CatalogFloor );
		}

		foreach ( var entry in package.FindAll( Sims4Reader.ResourceType.CatalogWall ) )
		{
			if ( entry.MemSize == 0 || entry.FileSize == 0 ) continue;
			dedup[entry.Key] = new RawEntry( package, entry, EntryKind.CatalogWall );
		}

		foreach ( var entry in package.FindAll( Sims4Reader.ResourceType.CatalogObject ) )
		{
			if ( entry.MemSize == 0 || entry.FileSize == 0 ) continue;
			dedup[entry.Key] = new RawEntry( package, entry, EntryKind.CatalogObject );
		}
	}

	/// <summary>
	/// Parse OBJD entries from a package to map MODL keys → object name + category.
	/// Names come from the OBJD internal Name property. Categories come from COBJ tags
	/// matched by shared instance ID.
	/// </summary>
	private static void CollectObjdModelNames(
		DbpfPackage package,
		ConcurrentDictionary<ResourceKey, (string Name, string? Category)> modelMeta,
		ConcurrentDictionary<ulong, string> cobjCategories )
	{
		var objdEntries = new List<ResourceEntry>();
		foreach ( var entry in package.FindAll( Sims4Reader.ResourceType.ObjectDefinition ) )
		{
			if ( entry.MemSize == 0 || entry.FileSize == 0 ) continue;
			objdEntries.Add( entry );
		}

		if ( objdEntries.Count == 0 ) return;

		var batchData = package.GetBytesBatch( objdEntries );
		for ( int j = 0; j < batchData.Length; j++ )
		{
			try
			{
				if ( batchData[j] == null || batchData[j].Length == 0 ) continue;

				var name = CleanObjectName( ExtractObjdName( batchData[j] ) );
				if ( name == null ) continue;

				cobjCategories.TryGetValue( objdEntries[j].Key.Instance, out var category );
				ExtractObjdModelKeys( batchData[j], modelMeta, name, category );
			}
			catch { }
		}
	}

	/// <summary>
	/// Extract the Name property (0xE7F07786) from raw OBJD binary data.
	/// </summary>
	private static string? ExtractObjdName( byte[] data )
	{
		if ( data.Length < 6 ) return null;

		uint tablePos = BitConverter.ToUInt32( data, 2 );
		if ( tablePos + 2 > data.Length ) return null;

		ushort entryCount = BitConverter.ToUInt16( data, (int)tablePos );
		int tableStart = (int)tablePos + 2;

		for ( int i = 0; i < entryCount; i++ )
		{
			int pos = tableStart + i * 8;
			if ( pos + 8 > data.Length ) break;

			uint propId = BitConverter.ToUInt32( data, pos );
			if ( propId == 0xE7F07786 ) // Name
			{
				uint offset = BitConverter.ToUInt32( data, pos + 4 );
				if ( offset + 4 > data.Length ) return null;

				int length = BitConverter.ToInt32( data, (int)offset );
				if ( length <= 0 || length > 10000 ) return null;

				int strStart = (int)offset + 4;
				if ( strStart + length > data.Length ) return null;

				return System.Text.Encoding.ASCII.GetString( data, strStart, length );
			}
		}

		return null;
	}

	/// <summary>
	/// Fast binary extraction of Model TGI references from an OBJD resource.
	/// Only reads the Model property (0x8D20ACC6) — name comes from OBJD Name,
	/// category from COBJ tags.
	/// </summary>
	private static void ExtractObjdModelKeys( byte[] data, ConcurrentDictionary<ResourceKey, (string Name, string? Category)> modelMeta, string name, string? category )
	{
		if ( data.Length < 6 ) return;

		// Header: version(2) + tablePosition(4)
		uint tablePos = BitConverter.ToUInt32( data, 2 );
		if ( tablePos + 2 > data.Length ) return;

		ushort entryCount = BitConverter.ToUInt16( data, (int)tablePos );
		int tableStart = (int)tablePos + 2;

		uint modelOffset = 0;

		// Scan property table for Model entry only
		for ( int i = 0; i < entryCount; i++ )
		{
			int pos = tableStart + i * 8;
			if ( pos + 8 > data.Length ) break;

			uint propId = BitConverter.ToUInt32( data, pos );
			if ( propId == 0x8D20ACC6 ) // Model
			{
				modelOffset = BitConverter.ToUInt32( data, pos + 4 );
				break;
			}
		}

		if ( modelOffset == 0 ) return;

		// Read TGI block list at modelOffset
		if ( modelOffset + 4 > data.Length ) return;
		int byteCount = BitConverter.ToInt32( data, (int)modelOffset );
		int count = byteCount / 4;
		if ( count <= 0 || count > 1000 ) return;

		int tgiStart = (int)modelOffset + 4;
		for ( int i = 0; i < count; i++ )
		{
			int off = tgiStart + i * 16;
			if ( off + 16 > data.Length ) break;

			ulong instance = BitConverter.ToUInt64( data, off );
			instance = (instance << 32) | (instance >> 32); // swap halves (s4pi convention)
			uint type = BitConverter.ToUInt32( data, off + 8 );
			uint group = BitConverter.ToUInt32( data, off + 12 );

			if ( type == (uint)Sims4Reader.ResourceType.Model && instance != 0 )
				modelMeta.TryAdd( new ResourceKey( (Sims4Reader.ResourceType)type, group, instance ), (name, category) );
		}
	}

	/// <summary>
	/// Strip common prefixes and normalize an OBJD name for use in mount paths.
	/// </summary>
	private static string? CleanObjectName( string? name )
	{
		if ( string.IsNullOrWhiteSpace( name ) )
			return null;

		if ( name.StartsWith( "object_", StringComparison.OrdinalIgnoreCase ) )
			name = name.Substring( 7 );

		name = name.Replace( ' ', '_' ).Replace( '\\', '_' ).Replace( '/', '_' );
		return string.IsNullOrWhiteSpace( name ) ? null : name;
	}

	/// <summary>
	/// Strip variant/swatch suffixes like "_01_set1", "_02", "_set3" from an object name.
	/// "chessTableGENOutdoor_01_set1" → "chessTableGENOutdoor"
	/// </summary>
	private static string StripVariantSuffix( string name )
	{
		// Walk backwards, stripping trailing segments that are numeric (_01, _02)
		// or set identifiers (_set1, _set2) or color swatches (_red, _blue)
		var span = name.AsSpan();
		while ( span.Length > 0 )
		{
			int lastUnderscore = span.LastIndexOf( '_' );
			if ( lastUnderscore <= 0 ) break;

			var suffix = span.Slice( lastUnderscore + 1 );

			// Pure numeric: _01, _02, _1, etc.
			bool isNumeric = true;
			for ( int i = 0; i < suffix.Length; i++ )
			{
				if ( !char.IsDigit( suffix[i] ) ) { isNumeric = false; break; }
			}
			if ( suffix.Length > 0 && isNumeric )
			{
				span = span.Slice( 0, lastUnderscore );
				continue;
			}

			// Set identifier: _set1, _set2, etc.
			if ( suffix.Length >= 4 && suffix[0] == 's' && suffix[1] == 'e' && suffix[2] == 't' )
			{
				bool setNumeric = true;
				for ( int i = 3; i < suffix.Length; i++ )
				{
					if ( !char.IsDigit( suffix[i] ) ) { setNumeric = false; break; }
				}
				if ( setNumeric )
				{
					span = span.Slice( 0, lastUnderscore );
					continue;
				}
			}

			break;
		}

		return span.Length > 0 ? span.ToString() : name;
	}

	protected override void Shutdown()
	{
		Instance = null;
		foreach ( var item in packages )
		{
			item.Dispose();
		}
	}
}
