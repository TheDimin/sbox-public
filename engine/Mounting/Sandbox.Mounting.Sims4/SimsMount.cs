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

		// Pass 1: Build MODL key → category and name mappings from COBJ → OBJD → MODL chain
		var (modlMetadata, objdMetadata) = BuildMetadataIndex( package );

		// Pass 2: Mount resources using categorized paths
		// Pass the full packages list for cross-package resource lookup.
		// Resources load lazily, so by the time a MODL actually resolves,
		// all packages will have been added to the list.
		MountResources( context, package, modlMetadata, objdMetadata );
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
	/// Pass 1: Scan COBJ and OBJD entries to build metadata (category + name) for each MODL key.
	///
	/// Chain: COBJ and OBJD share the same instance ID.
	///   COBJ tags → buy/build category
	///   OBJD.Name → human-readable object name (e.g. "object_diningTable_squareSteel")
	///   OBJD.Models[] → MODL resource keys
	/// </summary>
	private (Dictionary<ResourceKey, ModlMetadata> modl, Dictionary<ulong, ObjdMetadata> objd) BuildMetadataIndex( DbpfPackage package )
	{
		var modlMetadata = new Dictionary<ResourceKey, ModlMetadata>();
		var objdMetadata = new Dictionary<ulong, ObjdMetadata>();

		// 1a. Parse COBJs — extract BuyCat category from tags, keyed by instance ID.
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

		Log.Info( $"Metadata index: {cobjCategories.Count} categorized COBJs, {modlMetadata.Count} MODLs with metadata, {objdMetadata.Count} OBJDs" );

		return (modlMetadata, objdMetadata);
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
	/// Pass 2: Mount all resources with categorized paths.
	///
	/// MODL RCOLs contain MTST and MATD chunks internally — material resolution
	/// happens inside the ModelLoader via proper ChunkReference handling
	/// (Public/Private/Delayed reference types).
	///
	/// GEOM → models/cas/, MODL → models/{category}/{name}
	/// </summary>
	private void MountResources( MountContext context, DbpfPackage package, Dictionary<ResourceKey, ModlMetadata> modlMetadata, Dictionary<ulong, ObjdMetadata> objdMetadata )
	{
		foreach ( var entry in package.Entries )
		{
			if ( entry.MemSize == 0 || entry.FileSize == 0 )
				continue;

			var key = entry.Key;
			var keyName = $"{key.Group:X}_{key.Instance:X}";
			//
			try
			{
				switch ( key.Type )
				{
					// Images (DST, RLE)
					case Sims4Reader.ResourceType.DstImage:
					case Sims4Reader.ResourceType.RleImage:
					case Sims4Reader.ResourceType.RleImageAlt:
						context.Add( Sandbox.Mounting.ResourceType.Texture,
							$"textures/{keyName}",
							new Sims4TextureLoader( package, entry ) );
						break;

					// Materials (MATD in RCOL)
					case Sims4Reader.ResourceType.MaterialDefinition:
						context.Add( Sandbox.Mounting.ResourceType.Material,
							$"materials/{keyName}",
							new Sims4MaterialLoader( package, entry ) );
						break;

					// CAS meshes (GEOM) — always human/CAS content
					case Sims4Reader.ResourceType.Geometry:
						context.Add( Sandbox.Mounting.ResourceType.Model,
							$"models/cas/{keyName}",
							new ModelLoader( package, entry ) );
						break;

					// Buy/build models (MODL)
					case Sims4Reader.ResourceType.Model:
					{
						var meta = modlMetadata.TryGetValue( key, out var m ) ? m : default;
						var category = meta.Category ?? "objects";
						var displayName = meta.Name ?? keyName;

						context.Add( Sandbox.Mounting.ResourceType.Model,
							$"models/{category}/{displayName}",
							new ModlLoader( package, entry, packages ) );

						// Mount inline MATDs embedded in this MODL's RCOL
						MountInlineMaterials( context, package, entry );
						break;
					}

					// Catalog objects (COBJ)
					case Sims4Reader.ResourceType.CatalogObject:
					{
						// Build a readable path: objects/{category}/{objectName}/{variant}.s4cor
						var cobjResource = package.GetResource<CatalogObjectResource>( entry );
						var category = BuyCategoryTag.GetCategory( cobjResource.Tags ) ?? "misc";
						objdMetadata.TryGetValue( key.Instance, out var objd );

						var objName = objd.Name ?? keyName;
						var variant = objd.MaterialVariant;
						var fileName = !string.IsNullOrEmpty( variant ) ? variant : keyName;

						// Strip variant suffix from folder name so variants group under the base object
						// Variant is e.g. "set5-materialVariant", name ends with "set5"
						// Extract the set identifier (part before first '-') and strip it from the name
						if ( !string.IsNullOrEmpty( variant ) )
						{
							var variantPrefix = variant;
							var dashIdx = variant.IndexOf( '-' );
							if ( dashIdx > 0 )
								variantPrefix = variant.Substring( 0, dashIdx );

							if ( objName.EndsWith( variantPrefix, StringComparison.OrdinalIgnoreCase ) )
							{
								objName = objName.Substring( 0, objName.Length - variantPrefix.Length ).TrimEnd( '_' );
							}
						}

						context.Add( Sandbox.Mounting.ResourceType.Text,
							$"objects/{category}/{objName}/{fileName}.s4cor",
							new CatalogObjectLoader( package, entry ) );
						break;
					}
				}
			}
			catch ( Exception ex )
			{
				Log.Error( $"Error mounting {key.Type} {key}: {ex.Message}" );
			}
		}
	}


	/// <summary>
	/// Scan a MODL RCOL for inline MATD chunks and mount them as material resources.
	/// Uses a lightweight scan that only checks chunk reference types (no geometry decode).
	/// Path scheme matches ModelLoader.ResolveMesh:
	///   inline → materials/inline/{modlGroup:X}_{modlInstance:X}_m{meshIndex}
	/// </summary>
	private void MountInlineMaterials( MountContext context, DbpfPackage package, ResourceEntry modlEntry )
	{
		try
		{
			var inlineMeshes = Sims4Reader.Mesh.ModlModelLoader.ScanInlineMaterialMeshes( package, modlEntry );
			if ( inlineMeshes == null || inlineMeshes.Count == 0 ) return;

			foreach ( var meshIdx in inlineMeshes )
			{
				var mountPath = $"materials/inline/{modlEntry.Key.Group:X}_{modlEntry.Key.Instance:X}_m{meshIdx}";
				context.Add( Sandbox.Mounting.ResourceType.Material,
					mountPath,
					new InlineMaterialLoader( package, modlEntry, meshIdx, packages ) );
			}
		}
		catch ( Exception ex )
		{
			Log.Warning( $"Failed to scan MODL {modlEntry.Key} for inline materials: {ex.Message}" );
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
