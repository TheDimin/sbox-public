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

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		WriteIndented = true,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
	};

	public CatalogObjectLoader( DbpfPackage Package, ResourceEntry Entry )
	{
		entry = Entry;
		package = Package;
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
		var tags = new List<object>();
		foreach ( var tag in cobj.Tags )
		{
			tags.Add( new
			{
				Category = tag.Category,
				Value = tag.Value,
				CategoryName = BuyCategoryTag.GetCategoryName( tag.Category ),
				ValueName = BuyCategoryTag.GetCategoryName( tag.Value ),
			} );
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

		// Resolve name/description from string table
		var stbl = package.GetStringTable();
		var name = stbl?.GetString( cobj.NameHash );
		var description = stbl?.GetString( cobj.DescriptionHash );

		// Resolve thumbnails to mounted texture paths
		var thumbnail = ResolveThumbnailPath( cobj.ThumbnailHash );
		var variantThumbnail = ResolveThumbnailPath( cobj.VariantThumbImageHash );

		return new
		{
			Name = name,
			Description = description,
			Thumbnail = thumbnail,
			VariantThumbnail = variantThumbnail,
			Version = cobj.Version,
			Price = cobj.SimoleonPrice,
			BuyCategory = BuyCategoryTag.GetCategory( cobj.Tags ) ?? "unknown",
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
	/// Try to find a mounted texture path for a thumbnail instance hash.
	/// Searches all known thumbnail resource types in the package.
	/// </summary>
	private string? ResolveThumbnailPath( ulong thumbnailHash )
	{
		if ( thumbnailHash == 0 )
			return null;

		// Thumbnail types to search — TS4 stores thumbnails under various type IDs
		ReadOnlySpan<Sims4Reader.ResourceType> thumbnailTypes = stackalloc[]
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

		foreach ( var type in thumbnailTypes )
		{
			// Try group 0 first (most common for thumbnails)
			var found = package.Find( type, 0, thumbnailHash );
			if ( found.HasValue )
			{
				var key = found.Value.Key;
				return $"mount://sims4/textures/{key.Group:X}_{key.Instance:X}.vtex";
			}
		}

		// Fallback: search all entries of thumbnail types for matching instance
		foreach ( var type in thumbnailTypes )
		{
			foreach ( var thumbEntry in package.FindAll( type ) )
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
