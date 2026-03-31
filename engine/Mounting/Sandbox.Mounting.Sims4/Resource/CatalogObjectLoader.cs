using System.Text.Json;
using System.Text.Json.Serialization;
using Sandbox;
using Sandbox.Diagnostics;
using Sims4Reader;
using Sims4Reader.Resources;

namespace Mounting.Sims4;

public class CatalogObjectLoader : ResourceLoader<SimsMount>
{
	static new Logger Log = new Logger( "Sims4-CatalogObjectLoader" );
	private ResourceEntry entry;
	private DbpfPackage package;
	private IReadOnlyList<DbpfPackage> allPackages;

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		WriteIndented = true,
	};

	public CatalogObjectLoader( DbpfPackage Package, ResourceEntry Entry, IReadOnlyList<DbpfPackage> AllPackages )
	{
		entry = Entry;
		package = Package;
		allPackages = AllPackages;
		Tags.Add( "CatalogDefintion" );
	}

	protected override object? Load()
	{
		if ( entry.MemSize == 0 || entry.FileSize == 0 )
			return null;

		try
		{
			var cobj = package.GetResource<CatalogObjectResource>( entry );
			return JsonSerializer.Serialize( ToJsonObject( cobj ), JsonOptions );
		}
		catch ( Exception e )
		{
			Log.Warning( $"Failed to load catalog object {entry.Key}: {e.Message}" );
			return null;
		}
	}

	private object ToJsonObject( CatalogObjectResource cobj )
	{
		var tags = new List<string>();
		foreach ( var tag in cobj.Tags )
		{
			var friendly = BuyCategoryTag.GetFriendlyTagName( tag );
			if ( friendly != null )
				tags.Add( friendly );
		}

		var tgiRefs = new List<string>();
		foreach ( var tgi in cobj.TgiReferences )
		{
			tgiRefs.Add( $"{tgi.Type} {tgi.Group:X}_{tgi.Instance:X}" );
		}

		var catalogFilterColors = new List<string>();
		foreach ( var argb in cobj.CatalogFilterColors )
		{
			catalogFilterColors.Add( $"#{argb:X8}" );
		}

		// Resolve name/description from string table.
		// String tables are often in separate locale packages, so search all packages.
		var name = ResolveString( cobj.NameHash );
		var description = ResolveString( cobj.DescriptionHash );

		// Resolve thumbnails to mounted texture paths
		var thumbnail = ResolveThumbnailPath( cobj.ThumbnailHash );
		var variantThumbnail = ResolveThumbnailPath( cobj.VariantThumbImageHash );

		// Derive placement type from flags
		var placementType = GetPlacementType( cobj.Placement );

		// Resolve model path from the OBJD linked by shared instance ID
		var modelPath = ResolveModelPath( entry.Key.Instance, cobj.Tags );

		// Collect active placement flags as a readable list
		var placementFlags = new List<string>();
		foreach ( PlacementFlags flag in Enum.GetValues( typeof( PlacementFlags ) ) )
		{
			if ( flag != PlacementFlags.None && cobj.Placement.HasFlag( flag ) )
				placementFlags.Add( flag.ToString() );
		}

		// Collect active dev category flags as a readable list
		var devCategoryFlags = new List<string>();
		foreach ( DevCategoryFlags flag in Enum.GetValues( typeof( DevCategoryFlags ) ) )
		{
			if ( flag != DevCategoryFlags.None && cobj.DevCategoryFlags.HasFlag( flag ) )
				devCategoryFlags.Add( flag.ToString() );
		}
		//
		return new
		{
			Name = name,
			Description = description,
			Thumbnail = thumbnail,
			VariantThumbnail = variantThumbnail,
			PlacementType = placementType,
			PlacementFlags = placementFlags,
			ModelPath = modelPath,
			Version = cobj.Version,
			Price = cobj.SimoleonPrice,
			BuyCategory = BuyCategoryTag.GetCategory( cobj.Tags ) ?? "unknown",
			BuySubCategory = BuyCategoryTag.GetSubCategory( cobj.Tags ),
			DevCategoryFlags = devCategoryFlags,
			NameHash = cobj.NameHash,
			DescriptionHash = cobj.DescriptionHash,
			PackId = cobj.PackId,
			SwatchSortPriority = cobj.SwatchColorsSortPriority,
			IsStackable = cobj.IsStackable,
			CanDepreciate = cobj.CanDepreciate,
			CatalogFilterColors = catalogFilterColors,
			Tags = tags,
			TgiReferences = tgiRefs,
			IsFullyParsed = cobj.IsFullyParsed,
		};
	}

	/// <summary>
	/// Determine wall/floor/ceiling placement from COBJ placement flags.
	/// </summary>
	private static string GetPlacementType( PlacementFlags flags )
	{
		if ( flags.HasFlag( PlacementFlags.Ceiling ) )
			return "ceiling";

		if ( flags.HasFlag( PlacementFlags.Roof ) )
			return "roof";

		bool isWall = flags.HasFlag( PlacementFlags.CenterOnWall )
			|| flags.HasFlag( PlacementFlags.EdgeAgainstWall )
			|| flags.HasFlag( PlacementFlags.AdjustHeightOnWall )
			|| flags.HasFlag( PlacementFlags.OnWallTop );

		if ( isWall )
			return "wall";

		return flags.ToString();
	}

	/// <summary>
	/// Find the OBJD sharing this instance ID, get its first Model key,
	/// and construct the same mounted path that MountResources uses.
	/// </summary>
	private string? ResolveModelPath( ulong instanceId, CatalogTag[] cobjTags )
	{
		// Search all packages for a matching OBJD
		foreach ( var pkg in allPackages )
		{
			var objdEntry = pkg.Find( Sims4Reader.ResourceType.ObjectDefinition, 0, instanceId );
			if ( !objdEntry.HasValue )
				continue;

			try
			{
				var objd = pkg.GetResource<ObjectDefinitionResource>( objdEntry.Value );
				if ( objd.Models == null || objd.Models.Length == 0 )
					continue;

				var modelKey = objd.Models[0];
				if ( modelKey.Instance == 0 )
					continue;

				// Reconstruct the mount path using the same logic as MountResources
				var category = BuyCategoryTag.GetCategory( cobjTags ) ?? "objects";
				var objName = CleanObjectName( objd.Name ) ?? $"{modelKey.Group:X}_{modelKey.Instance:X}";

				return $"mount://sims4/models/{category}/{objName}.vmdl";
			}
			catch
			{
				// Skip broken OBJD entries
			}
		}

		return null;
	}

	/// <summary>
	/// Clean an OBJD name — same logic as SimsMount.CleanObjectName.
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
	/// Resolve a string hash by searching string tables across all packages.
	/// Checks the local package first, then falls back to all other packages.
	/// </summary>
	private string? ResolveString( uint hash )
	{
		if ( hash == 0 )
			return null;

		// Try local package first
		var stbl = package.GetStringTable();
		var result = stbl?.GetString( hash );
		if ( result != null )
			return result;

		// Search all other packages for string tables containing this hash
		foreach ( var pkg in allPackages )
		{
			if ( ReferenceEquals( pkg, package ) )
				continue;

			stbl = pkg.GetStringTable();
			result = stbl?.GetString( hash );
			if ( result != null )
				return result;
		}

		return null;
	}

	// Thumbnail types to search — TS4 stores thumbnails under various type IDs
	private static readonly Sims4Reader.ResourceType[] ThumbnailTypes = new[]
	{
		Sims4Reader.ResourceType.Thumbnail_0D,
		Sims4Reader.ResourceType.Thumbnail_16,
		Sims4Reader.ResourceType.Thumbnail_3B,
		Sims4Reader.ResourceType.Thumbnail_3C,
		Sims4Reader.ResourceType.Thumbnail_3C2,
		Sims4Reader.ResourceType.Thumbnail_5B,
		Sims4Reader.ResourceType.Thumbnail_CD,
		Sims4Reader.ResourceType.Thumbnail_E1,
		Sims4Reader.ResourceType.Thumbnail_E2,
		Sims4Reader.ResourceType.Thumbnail_16C,
	};

	/// <summary>
	/// Try to find a mounted texture path for a thumbnail instance hash.
	/// Searches all known thumbnail resource types across all packages.
	/// </summary>
	private string? ResolveThumbnailPath( ulong thumbnailHash )
	{
		if ( thumbnailHash == 0 )
			return null;

		// Search local package first, then all others
		var result = ResolveThumbnailInPackage( package, thumbnailHash );
		if ( result != null )
			return result;

		foreach ( var pkg in allPackages )
		{
			if ( ReferenceEquals( pkg, package ) )
				continue;

			result = ResolveThumbnailInPackage( pkg, thumbnailHash );
			if ( result != null )
				return result;
		}

		return null;
	}

	private static string? ResolveThumbnailInPackage( DbpfPackage pkg, ulong thumbnailHash )
	{
		foreach ( var type in ThumbnailTypes )
		{
			// Try group 0 first (most common for thumbnails)
			var found = pkg.Find( type, 0, thumbnailHash );
			if ( found.HasValue )
			{
				var key = found.Value.Key;
				return $"mount://sims4/textures/{key.Group:X}_{key.Instance:X}.vtex";
			}
		}

		// Fallback: search all entries of thumbnail types for matching instance
		foreach ( var type in ThumbnailTypes )
		{
			foreach ( var thumbEntry in pkg.FindAll( type ) )
			{
				if ( thumbEntry.Key.Instance == thumbnailHash )
				{
					return $"mount://sims4/textures/{thumbEntry.Key.Group:X}_{thumbEntry.Key.Instance:X}.vtex";
				}
			}
		}

		return null;
	}
}
