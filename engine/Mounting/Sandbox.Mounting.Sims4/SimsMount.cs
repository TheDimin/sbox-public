using System;
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

	string? _gameDir;

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
				return;
			}
		}

		//Detecting it trough EA app requires us to know if the user owns the game...
		return;
	}

	protected override Task Mount( MountContext context )
	{
		if ( string.IsNullOrWhiteSpace( _gameDir ) || !System.IO.Directory.Exists( _gameDir ) )
			return Task.CompletedTask;

		var dataDir = System.IO.Path.Combine( _gameDir, "Data" );
		if ( !System.IO.Directory.Exists( dataDir ) )
			return Task.CompletedTask;

		//MountPackage( context, "E:\\SteamLibrary\\steamapps\\common\\The Sims 4\\Data\\Client\\ClientDeltaBuild0.package" );

		foreach ( var file in System.IO.Directory.EnumerateFiles( dataDir, "*.package", SearchOption.AllDirectories ) )
		{
			MountPackage( context, file );
		}

		Instance = this;
		IsMounted = true;
		return Task.CompletedTask;
	}

	private void MountPackage( MountContext context, string file )
	{
		Log.Info( $"Mounting package: {file}" );

		var package = DbpfPackage.Open( file );
		packages.Add( package );

		// Pass 1: Build a MODL key → category mapping from the COBJ → OBJD → MODL chain
		var modlCategories = BuildCategoryIndex( package );

		// Pass 2: Mount resources using categorized paths
		// Pass the full packages list for cross-package resource lookup.
		// Resources load lazily, so by the time a MODL actually resolves,
		// all packages will have been added to the list.
		MountResources( context, package, modlCategories );
	}

	/// <summary>
	/// Pass 1: Scan COBJ and OBJD entries to build a mapping from MODL resource keys
	/// to buy/build category names.
	///
	/// Link: COBJ and OBJD share the same instance ID. COBJ tags identify the buy
	/// category (BUY_CAT_EE/PA/LD/SS, BUILD_*), OBJD.Models[] provides MODL keys.
	/// </summary>
	private Dictionary<ResourceKey, string> BuildCategoryIndex( DbpfPackage package )
	{
		var modlCategories = new Dictionary<ResourceKey, string>();

		// 1a. Parse COBJs — extract BuyCat category from tags, keyed by instance ID.
		// In TS4, COBJ and OBJD for the same object share the same instance ID.
		var cobjCategories = new Dictionary<ulong, string>();

		foreach ( var entry in package.FindAll( Sims4Reader.ResourceType.CatalogObject ) )
		{
			if ( entry.MemSize == 0 || entry.FileSize == 0 )
				continue;

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
		}

		// 1b. Parse OBJDs — match to COBJ by instance ID, extract Model references
		foreach ( var entry in package.FindAll( Sims4Reader.ResourceType.ObjectDefinition ) )
		{
			if ( entry.MemSize == 0 || entry.FileSize == 0 )
				continue;

			if ( !cobjCategories.TryGetValue( entry.Key.Instance, out var category ) )
				continue;

			try
			{
				var objd = package.GetResource<ObjectDefinitionResource>( entry );

				foreach ( var modelKey in objd.Models )
				{
					if ( (uint)modelKey.Type == (uint)Sims4Reader.ResourceType.Model && modelKey.Instance != 0 )
					{
						modlCategories.TryAdd( modelKey, category );
					}
				}
			}
			catch ( Exception ex )
			{
				Log.Warning( $"Failed to parse OBJD {entry.Key}: {ex.Message}" );
			}
		}

		Log.Info( $"Category index: {cobjCategories.Count} categorized COBJs, {modlCategories.Count} categorized MODLs" );

		return modlCategories;
	}

	/// <summary>
	/// Pass 2: Mount all resources with categorized paths.
	///
	/// MODL RCOLs contain MTST and MATD chunks internally — material resolution
	/// happens inside the ModelLoader via proper ChunkReference handling
	/// (Public/Private/Delayed reference types).
	///
	/// GEOM → models/cas/, MODL → models/{category}/
	/// </summary>
	private void MountResources( MountContext context, DbpfPackage package, Dictionary<ResourceKey, string> modlCategories )
	{
		foreach ( var entry in package.Entries )
		{
			if ( entry.MemSize == 0 || entry.FileSize == 0 )
				continue;

			var key = entry.Key;
			var name = $"{key.Group:X}_{key.Instance:X}";

			try
			{
				switch ( key.Type )
				{
					// Images (DST, RLE)
					case Sims4Reader.ResourceType.DstImage:
					case Sims4Reader.ResourceType.RleImage:
					case Sims4Reader.ResourceType.RleImageAlt:
						context.Add( Sandbox.Mounting.ResourceType.Texture,
							$"textures/{name}",
							new Sims4TextureLoader( package, entry ) );
						break;

					// Materials (MATD in RCOL)
					case Sims4Reader.ResourceType.MaterialDefinition:
						context.Add( Sandbox.Mounting.ResourceType.Material,
							$"materials/{name}",
							new Sims4MaterialLoader( package, entry ) );
						break;

					// CAS meshes (GEOM) — always human/CAS content
					case Sims4Reader.ResourceType.Geometry:
						context.Add( Sandbox.Mounting.ResourceType.Model,
							$"models/cas/{name}",
							new ModelLoader( package, entry ) );
						break;

					// Buy/build models (MODL)
					case Sims4Reader.ResourceType.Model:
					{
						var category = modlCategories.TryGetValue( key, out var cat )
							? cat
							: "objects";
						context.Add( Sandbox.Mounting.ResourceType.Model,
							$"models/{category}/{name}",
							new ModlLoader( package, entry, packages ) );
						break;
					}

					// Catalog objects (COBJ)
					case Sims4Reader.ResourceType.CatalogObject:
						context.Add( Sandbox.Mounting.ResourceType.Text,
							$"objects/{name}",
							new CatalogObjectLoader( package, entry ) );
						break;
				}
			}
			catch ( Exception ex )
			{
				Log.Error( $"Error mounting {key.Type} {key}: {ex.Message}" );
			}
		}
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
