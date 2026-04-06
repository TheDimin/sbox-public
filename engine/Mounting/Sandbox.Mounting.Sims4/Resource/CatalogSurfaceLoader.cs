using System.Text.Json;
using Sandbox;
using Sandbox.Diagnostics;
using Sims4Reader;
using Sims4Reader.Resources;

namespace Mounting.Sims4;

/// <summary>
/// Loads CFLR (CatalogFloor), CFLT (CatalogFlooring), and CWAL (CatalogWall) entries
/// as JSON catalog metadata. These share the same CatalogCommon header as COBJ.
///
/// Material and texture resolution is deferred — this loader only records the
/// MATD resource key as a mount path so that <see cref="Sims4MaterialLoader"/> handles
/// actual parsing when the material is first accessed.
/// </summary>
public class CatalogSurfaceLoader : ResourceLoader<SimsMount>
{
	static new Logger Log = new Logger( "Sims4-CatalogSurfaceLoader" );

	private readonly DbpfPackage package;
	private readonly ResourceEntry entry;
	private readonly IReadOnlyList<DbpfPackage> allPackages;
	private readonly string surfaceType;

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		WriteIndented = true,
	};

	public CatalogSurfaceLoader( DbpfPackage package, ResourceEntry entry, IReadOnlyList<DbpfPackage> allPackages, string surfaceType )
	{
		this.package = package;
		this.entry = entry;
		this.allPackages = allPackages;
		this.surfaceType = surfaceType;
		Tags.Add( "CatalogPaint" );
	}

	protected override object? Load()
	{
		if ( entry.MemSize == 0 || entry.FileSize == 0 )
			return null;

		try
		{
			// CFLR/CFLT/CWAL share the CatalogCommon header with COBJ.
			// The body fields differ, so IsFullyParsed may be false — that's fine,
			// we only need the common block (name, tags, thumbnail, TGI refs).
			var catalog = package.GetResource<CatalogObjectResource>( entry );
			return JsonSerializer.Serialize( ToJsonObject( catalog ), JsonOptions );
		}
		catch ( Exception e )
		{
			Log.Warning( $"Failed to load catalog surface {entry.Key}: {e.Message}" );
			return null;
		}
	}

	private object ToJsonObject( CatalogObjectResource catalog )
	{
		// Resolve name/description from string tables across all packages
		var name = ResolveString( catalog.NameHash );
		var description = ResolveString( catalog.DescriptionHash );

		// Resolve thumbnail to a mounted texture path
		var thumbnail = ResolveThumbnailPath( catalog.ThumbnailHash );
		var variantThumbnail = ResolveThumbnailPath( catalog.VariantThumbImageHash );

		// Build friendly tag list
		var tags = new List<string>();
		foreach ( var tag in catalog.Tags )
		{
			var friendly = BuyCategoryTag.GetFriendlyTagName( tag );
			if ( friendly != null )
				tags.Add( friendly );
		}

		// Catalog filter colors (ARGB)
		var catalogFilterColors = new List<string>();
		foreach ( var argb in catalog.CatalogFilterColors )
		{
			catalogFilterColors.Add( $"#{argb:X8}" );
		}

		// Collect TGI references as readable strings
		var tgiRefs = new List<string>();
		foreach ( var tgi in catalog.TgiReferences )
		{
			tgiRefs.Add( $"{tgi.Type} {tgi.Group:X}_{tgi.Instance:X}" );
		}

		// Find the MATD key in TGI references — record it as a mount path so
		// Sims4MaterialLoader resolves the actual material lazily on first access.
		string? materialPath = null;
		foreach ( var tgi in catalog.TgiReferences )
		{
			if ( tgi.Type == Sims4Reader.ResourceType.MaterialDefinition )
			{
				materialPath = $"mount://sims4/materials/{tgi.Group:X}_{tgi.Instance:X}.vmat";
				break;
			}
		}

		return new
		{
			Name = name,
			Description = description,
			Thumbnail = thumbnail,
			VariantThumbnail = variantThumbnail,
			SurfaceType = surfaceType,
			MaterialPath = materialPath,
			Version = catalog.Version,
			Price = catalog.SimoleonPrice,
			NameHash = catalog.NameHash,
			DescriptionHash = catalog.DescriptionHash,
			PackId = catalog.PackId,
			SwatchSortPriority = catalog.SwatchColorsSortPriority,
			CatalogFilterColors = catalogFilterColors,
			Tags = tags,
			TgiReferences = tgiRefs,
		};
	}

	#region String / Thumbnail Resolution (shared pattern with CatalogObjectLoader)

	private string? ResolveString( uint hash )
	{
		if ( hash == 0 )
			return null;

		var stbl = package.GetStringTable();
		var result = stbl?.GetString( hash );
		if ( result != null )
			return result;

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

	private string? ResolveThumbnailPath( ulong thumbnailHash )
	{
		if ( thumbnailHash == 0 )
			return null;

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
			var found = pkg.Find( type, 0, thumbnailHash );
			if ( found.HasValue )
			{
				var key = found.Value.Key;
				return $"mount://sims4/textures/{key.Group:X}_{key.Instance:X}.vtex";
			}
		}

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

	#endregion
}
