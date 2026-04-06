using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
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
	readonly HashSet<string> _mountedCatalogPaths = new( StringComparer.OrdinalIgnoreCase );

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
	/// Pre-processed package data produced in parallel, consumed sequentially by MountResources.
	/// </summary>
	private readonly struct PreparedPackage
	{
		public readonly DbpfPackage Package;
		public readonly Dictionary<ResourceKey, ModlMetadata> ModlMetadata;
		public readonly Dictionary<ulong, ObjdMetadata> ObjdMetadata;
		public readonly Dictionary<ulong, string> CobjCategories;

		public PreparedPackage( DbpfPackage package, Dictionary<ResourceKey, ModlMetadata> modlMetadata, Dictionary<ulong, ObjdMetadata> objdMetadata, Dictionary<ulong, string> cobjCategories )
		{
			Package = package;
			ModlMetadata = modlMetadata;
			ObjdMetadata = objdMetadata;
			CobjCategories = cobjCategories;
		}
	}

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
		var sw = Stopwatch.StartNew();

		Log.Trace( $"Mount starting: {files.Length} packages in {dataDir}" );

		// Phase 1 (parallel): Open packages + parse COBJ/OBJD metadata.
		// Each package is independent — no shared state between packages.
		// DbpfPackage.Open reads the DBPF index, BuildMetadataIndex parses
		// COBJ/OBJD entries. Both are CPU+I/O bound and benefit from parallelism.
		var prepared = new PreparedPackage[files.Length];
		int failedPackages = 0;



		for ( int i = 0; i < files.Length; i++ )
		{
			try
			{
				var package = DbpfPackage.Open( files[i] );
				var (modlMeta, objdMeta, cobjCats) = BuildMetadataIndex( package );
				prepared[i] = new PreparedPackage( package, modlMeta, objdMeta, cobjCats );
			}
			catch ( Exception ex )
			{
				Interlocked.Increment( ref failedPackages );
				Log.Error( $"Failed to open package {files[i]}: {ex}" );
			}
		}
		;

		Log.Trace( $"Phase 1 complete: {files.Length - failedPackages}/{files.Length} packages opened in {sw.ElapsedMilliseconds}ms" );

		// Phase 2 (sequential): Register resources with the mount system.
		// context.Add is NOT thread-safe (writes to a plain Dictionary),
		// so all registration must happen on this thread.
		int totalModels = 0, totalSurfaces = 0, totalCatalogs = 0;
		for ( int i = 0; i < prepared.Length; i++ )
		{
			var p = prepared[i];
			if ( p.Package == null )
				continue;

			Log.Trace( $"Phase 2: mounting package {i + 1}/{prepared.Length} — {System.IO.Path.GetFileName( files[i] )} ({p.Package.Entries.Count} entries)" );

			packages.Add( p.Package );
			var (models, surfaces, catalogs) = MountResources( context, p.Package, p.ModlMetadata, p.ObjdMetadata, p.CobjCategories );
			totalModels += models;
			totalSurfaces += surfaces;
			totalCatalogs += catalogs;
		}

		sw.Stop();
		Log.Trace( $"Mount complete in {sw.ElapsedMilliseconds}ms: {totalModels} models, {totalSurfaces} surfaces, {totalCatalogs} catalogs" );

		Instance = this;
		IsMounted = true;
		return Task.CompletedTask;
	}

	/// <summary>
	/// Metadata collected per MODL key from the COBJ → OBJD chain.
	/// </summary>
	private record struct ModlMetadata( string? Category, string? Name );

	/// <summary>
	/// Metadata from OBJD keyed by instance ID (shared with COBJ).
	/// Used to derive readable file paths and swatch variant names for catalog objects.
	/// </summary>
	internal record struct ObjdMetadata( string? Name, string? MaterialVariant );

	/// <summary>
	/// Scan COBJ and OBJD entries to build metadata (category + name) for each MODL key.
	/// Thread-safe: operates only on the given package with local dictionaries.
	///
	/// Chain: COBJ and OBJD share the same instance ID.
	///   COBJ tags → buy/build category
	///   OBJD.Name → human-readable object name (e.g. "object_diningTable_squareSteel")
	///   OBJD.Models[] → MODL resource keys
	/// </summary>
	private static (Dictionary<ResourceKey, ModlMetadata> modl, Dictionary<ulong, ObjdMetadata> objd, Dictionary<ulong, string> cobjCategories) BuildMetadataIndex( DbpfPackage package )
	{
		var modlMetadata = new Dictionary<ResourceKey, ModlMetadata>();
		var objdMetadata = new Dictionary<ulong, ObjdMetadata>();

		// 1a. Parse COBJs — extract BuyCat category from tags, keyed by instance ID.
		var cobjCategories = new Dictionary<ulong, string>();
		Parallel.ForEach( package.FindAll( Sims4Reader.ResourceType.CatalogObject ), entry =>
		{
			//foreach ( var entry in package.FindAll( Sims4Reader.ResourceType.CatalogObject ) )

			//var entry = 
			if ( entry.MemSize == 0 || entry.FileSize == 0 )
				return;

			try
			{
				var cobj = package.GetResource<CatalogObjectResource>( entry );
				var category = BuyCategoryTag.GetCategory( cobj.Tags );

				if ( category != null )
					cobjCategories[entry.Key.Instance] = category;
			}
			catch ( Exception ex )
			{
				Log.Warning( $"Failed to parse COBJ {entry.Key}: {ex.Message}" );
			}
		} );

		// 1b. Parse OBJDs — match to COBJ by instance ID, extract Model references + name + variant
		foreach ( var entry in package.FindAll( Sims4Reader.ResourceType.ObjectDefinition ) )
		{
			if ( entry.MemSize == 0 || entry.FileSize == 0 )
				continue;

			try
			{
				var objd = package.GetResource<ObjectDefinitionResource>( entry );

				cobjCategories.TryGetValue( entry.Key.Instance, out var category );

				// Clean up the OBJD name: strip "object_" prefix and convert underscores to readable form
				var objName = CleanObjectName( objd.Name );

				// Store OBJD metadata for catalog object file naming
				objdMetadata.TryAdd( entry.Key.Instance, new ObjdMetadata( objName, objd.MaterialVariant ) );

				foreach ( var modelKey in objd.Models )
				{
					if ( (uint)modelKey.Type == (uint)Sims4Reader.ResourceType.Model && modelKey.Instance != 0 )
					{
						modlMetadata.TryAdd( modelKey, new ModlMetadata( category, objName ) );
					}
				}
			}
			catch ( Exception ex )
			{
				Log.Warning( $"Failed to parse OBJD {entry.Key}: {ex.Message}" );
			}
		}

		return (modlMetadata, objdMetadata, cobjCategories);
	}

	/// <summary>
	/// Clean an OBJD name into a readable form for mount paths.
	/// "object_diningTable_squareSteel" → "diningTable_squareSteel"
	/// "object_Toilet" → "Toilet"
	/// null/empty → null
	/// </summary>
	private static string? CleanObjectName( string? name )
	{
		if ( string.IsNullOrWhiteSpace( name ) )
			return null;

		// Strip common prefixes
		if ( name.StartsWith( "object_", StringComparison.OrdinalIgnoreCase ) )
			name = name.Substring( 7 );

		// Replace characters that are invalid in paths
		name = name.Replace( ' ', '_' ).Replace( '\\', '_' ).Replace( '/', '_' );

		return string.IsNullOrWhiteSpace( name ) ? null : name;
	}

	/// <summary>
	/// Mount all resources with categorized paths. Must be called from the mount thread only.
	///
	/// MODL RCOLs contain MTST and MATD chunks internally — material resolution
	/// happens inside the ModelLoader via proper ChunkReference handling
	/// (Public/Private/Delayed reference types).
	///
	/// MODL → models/{category}/{name}
	/// </summary>
	private (int models, int surfaces, int catalogs) MountResources( MountContext context, DbpfPackage package, Dictionary<ResourceKey, ModlMetadata> modlMetadata, Dictionary<ulong, ObjdMetadata> objdMetadata, Dictionary<ulong, string> cobjCategories )
	{
		int models = 0, surfaces = 0, catalogs = 0;

		foreach ( var entry in package.Entries )
		{
			if ( entry.MemSize == 0 || entry.FileSize == 0 )
				continue;

			var key = entry.Key;
			var keyName = $"{key.Group:X}_{key.Instance:X}";

			try
			{
				switch ( key.Type )
				{
					// Buy/build models (MODL)
					case Sims4Reader.ResourceType.Model:
						{
							var meta = modlMetadata.TryGetValue( key, out var m ) ? m : default;
							var category = meta.Category ?? "objects";
							var displayName = meta.Name ?? keyName;

							context.Add( Sandbox.Mounting.ResourceType.Model,
								$"models/{category}/{displayName}",
								new ModlLoader( package, entry, packages ) );
							models++;
							break;
						}

					// Catalog surfaces (CFLR, CFLT, CWAL) — floor/wall paint materials
					case Sims4Reader.ResourceType.CatalogFloor:
					case Sims4Reader.ResourceType.CatalogFlooring:
					case Sims4Reader.ResourceType.CatalogWall:
						{
							var surfaceType = key.Type == Sims4Reader.ResourceType.CatalogWall ? "wall" : "floor";
							var path = $"catalogs/{keyName}.s4sur";
							if ( _mountedCatalogPaths.Contains( path ) )
							{
								Log.Trace( $"Duplicate catalog surface path, skipping: {path}" );
								break;
							}
							_mountedCatalogPaths.Add( path );

							context.Add( Sandbox.Mounting.ResourceType.Text,
								path,
								new CatalogSurfaceLoader( package, entry, packages, surfaceType ) );
							surfaces++;
							break;
						}

					// Catalog objects (COBJ)
					case Sims4Reader.ResourceType.CatalogObject:
						{
							var path = $"catalogs/{keyName}.s4cor";
							if ( _mountedCatalogPaths.Contains( path ) )
							{
								Log.Trace( $"Duplicate catalog object path, skipping: {path}" );
								break;
							}
							_mountedCatalogPaths.Add( path );

							context.Add( Sandbox.Mounting.ResourceType.Text,
								path,
								new CatalogObjectLoader( package, entry, packages ) );
							catalogs++;
							break;
						}
				}
			}
			catch ( Exception ex )
			{
				Log.Error( $"Error mounting {key.Type} {key}: {ex}" );
			}
		}

		return (models, surfaces, catalogs);
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
