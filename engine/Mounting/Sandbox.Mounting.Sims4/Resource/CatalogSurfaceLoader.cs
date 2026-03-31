using System.Text.Json;
using Sandbox;
using Sandbox.Diagnostics;
using Sims4Reader;
using Sims4Reader.Material;
using Sims4Reader.Mesh;
using Sims4Reader.Rcol;
using Sims4Reader.Resources;

namespace Mounting.Sims4;

/// <summary>
/// Loads CFLR (CatalogFloor), CFLT (CatalogFlooring), and CWAL (CatalogWall) entries.
/// These share the CatalogCommon header with COBJ. Parses the common header inline
/// to extract name, description, thumbnail, tags, and colors. Scans TGI references
/// for a MaterialDefinition to resolve material and individual texture paths.
/// Outputs JSON tagged with "CatalogPaint".
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
			var data = package.GetBytes( entry );
			var parsed = ParseCatalogCommon( data );
			if ( parsed == null )
				return null;

			return JsonSerializer.Serialize( BuildJsonObject( parsed ), JsonOptions );
		}
		catch ( Exception e )
		{
			Log.Warning( $"Failed to load catalog surface {entry.Key}: {e.Message}" );
			return null;
		}
	}

	private object BuildJsonObject( CatalogCommonData common )
	{
		// Resolve name/description from string tables
		var name = ResolveString( common.NameHash );
		var description = ResolveString( common.DescriptionHash );
		var thumbnail = ResolveThumbnailPath( common.ThumbnailHash );

		// Build friendly tag list
		var tags = new List<string>();
		foreach ( var tag in common.Tags )
		{
			var friendly = BuyCategoryTag.GetFriendlyTagName( tag );
			if ( friendly != null )
				tags.Add( friendly );
		}

		// Catalog filter colors
		var colors = new List<string>();
		foreach ( var argb in common.CatalogFilterColors )
		{
			colors.Add( $"#{argb:X8}" );
		}

		// Resolve material and texture paths from TGI references
		string materialPath = null;
		Dictionary<string, string> textures = null;

		foreach ( var tgi in common.TgiReferences )
		{
			if ( tgi.Type == Sims4Reader.ResourceType.MaterialDefinition )
			{
				materialPath = $"mount://sims4/materials/{tgi.Group:X}_{tgi.Instance:X}.vmat";
				textures = ResolveTexturesFromMatd( tgi );
				break;
			}
		}

		return new
		{
			Name = name,
			Description = description,
			Thumbnail = thumbnail,
			MaterialPath = materialPath,
			SurfaceType = surfaceType,
			NameHash = common.NameHash,
			CatalogFilterColors = colors,
			Tags = tags,
			Textures = textures,
		};
	}

	/// <summary>
	/// Parse the MATD resource to extract individual texture keys (Diffuse, Normal, Specular).
	/// </summary>
	private Dictionary<string, string> ResolveTexturesFromMatd( ResourceKey matdKey )
	{
		var result = new Dictionary<string, string>();

		try
		{
			// Find the MATD resource across all packages
			ResourceEntry? matdEntry = null;
			DbpfPackage matdPackage = null;

			foreach ( var pkg in allPackages )
			{
				var found = pkg.Find( matdKey.Type, matdKey.Group, matdKey.Instance );
				if ( found.HasValue )
				{
					matdEntry = found.Value;
					matdPackage = pkg;
					break;
				}
			}

			// Also check local package first
			if ( matdEntry == null )
			{
				var found = package.Find( matdKey.Type, matdKey.Group, matdKey.Instance );
				if ( found.HasValue )
				{
					matdEntry = found.Value;
					matdPackage = package;
				}
			}

			if ( matdEntry == null || matdPackage == null )
				return result;

			var rcol = matdPackage.GetResource<RcolContainer>( matdEntry.Value );
			var matd = rcol.GetChunk<MaterialDefinition>();
			if ( matd == null )
				return result;

			var textureKeys = ModlModelLoader.ExtractTextureKeys( matd, rcol.ExternalReferences );

			foreach ( var (field, key) in textureKeys )
			{
				var texPath = $"mount://sims4/textures/{key.Group:X}_{key.Instance:X}.vtex";

				switch ( field )
				{
					case ShaderFieldType.DiffuseMap:
						result["Diffuse"] = texPath;
						break;
					case ShaderFieldType.NormalMap:
						result["Normal"] = texPath;
						break;
					case ShaderFieldType.SpecularMap:
						result["Specular"] = texPath;
						break;
				}
			}
		}
		catch ( Exception e )
		{
			Log.Warning( $"Failed to resolve textures from MATD {matdKey}: {e.Message}" );
		}

		return result;
	}

	#region CatalogCommon Parsing

	/// <summary>
	/// Minimal data extracted from the CatalogCommon header, shared by COBJ/CFLR/CFLT/CWAL.
	/// </summary>
	private class CatalogCommonData
	{
		public uint NameHash;
		public uint DescriptionHash;
		public ulong ThumbnailHash;
		public CatalogTag[] Tags = Array.Empty<CatalogTag>();
		public uint[] CatalogFilterColors = Array.Empty<uint>();
		public ResourceKey[] TgiReferences = Array.Empty<ResourceKey>();
	}

	/// <summary>
	/// Parse CatalogCommon header from raw bytes. Same binary format as COBJ common block.
	/// Skips the type-specific body, then scans backwards for TGI references.
	/// </summary>
	private static CatalogCommonData? ParseCatalogCommon( ReadOnlyMemory<byte> data )
	{
		using var ms = new MemoryStream( data.ToArray() );
		using var reader = new BinaryReader( ms );
		var result = new CatalogCommonData();

		try
		{
			// Version
			reader.ReadUInt32();

			// CatalogCommon block — identical to CatalogObjectResource.ParseCatalogCommon
			uint commonBlockVersion = reader.ReadUInt32();
			result.NameHash = reader.ReadUInt32();
			result.DescriptionHash = reader.ReadUInt32();
			reader.ReadUInt32(); // SimoleonPrice
			result.ThumbnailHash = reader.ReadUInt64();
			reader.ReadUInt32(); // DevCategoryFlags

			// ProductStyles
			byte styleCount = reader.ReadByte();
			for ( int i = 0; i < styleCount; i++ )
				reader.ReadBytes( 16 );

			if ( commonBlockVersion >= 10 )
			{
				reader.ReadInt16(); // PackId
				reader.ReadByte();  // PackFlags
				reader.ReadBytes( 9 ); // ReservedBytes
			}
			else
			{
				byte unused2 = reader.ReadByte();
				if ( unused2 > 0 )
					reader.ReadByte();
			}

			// Tags
			uint tagCount = reader.ReadUInt32();
			if ( tagCount > 1000 ) tagCount = 0;
			result.Tags = new CatalogTag[tagCount];
			for ( int i = 0; i < tagCount; i++ )
			{
				result.Tags[i] = new CatalogTag
				{
					Category = reader.ReadUInt16(),
					Value = reader.ReadUInt16(),
				};
			}

			// SellingPoints
			uint sellingPointCount = reader.ReadUInt32();
			if ( sellingPointCount > 1000 ) sellingPointCount = 0;
			for ( int i = 0; i < sellingPointCount; i++ )
			{
				reader.ReadUInt16();
				reader.ReadInt32();
			}

			reader.ReadUInt32(); // UnlockByHash
			reader.ReadUInt32(); // UnlockedByHash
			reader.ReadUInt16(); // SwatchColorsSortPriority
			reader.ReadUInt64(); // VariantThumbImageHash

			// Skip the type-specific body — we don't need it.
			// Instead, scan from the end for TGI references.
			result.TgiReferences = ScanTgiReferences( data );

			// Try to read CatalogFilterColors from body if accessible
			// These appear after some fixed body fields in COBJ; format varies for CFLR/CWAL
			// but we can try to find them at a fixed offset or just skip.
			result.CatalogFilterColors = Array.Empty<uint>();
		}
		catch ( EndOfStreamException )
		{
			// Partial parse is OK — CatalogCommon fields are still usable
		}

		if ( result.Tags.Length == 0 && result.NameHash == 0 )
			return null;

		return result;
	}

	/// <summary>
	/// Scan the tail of the resource data for a TGI reference block.
	/// TS4 catalog resources typically end with: byte count + N × 16-byte ITG entries.
	/// We try reading the last byte as the count and validate the block size.
	/// </summary>
	private static ResourceKey[] ScanTgiReferences( ReadOnlyMemory<byte> data )
	{
		var bytes = data.Span;
		if ( bytes.Length < 17 ) // need at least 1 count byte + 1 TGI entry
			return Array.Empty<ResourceKey>();

		// The TGI block is at the very end: [count:byte] [entries:count×16]
		// Try different positions near the end to find a valid TGI block
		for ( int offset = 1; offset <= Math.Min( 64, bytes.Length - 16 ); offset++ )
		{
			int countPos = bytes.Length - offset * 16 - 1;
			if ( countPos < 0 )
				break;

			byte count = bytes[countPos];
			if ( count == 0 || count > 32 )
				continue;

			int blockSize = count * 16;
			if ( countPos + 1 + blockSize != bytes.Length )
				continue;

			// Validate: each entry should have a recognizable resource type
			var entries = new ResourceKey[count];
			bool valid = true;
			int pos = countPos + 1;

			for ( int i = 0; i < count; i++ )
			{
				ulong instance = BitConverter.ToUInt64( bytes.Slice( pos, 8 ) );
				uint type = BitConverter.ToUInt32( bytes.Slice( pos + 8, 4 ) );
				uint group = BitConverter.ToUInt32( bytes.Slice( pos + 12, 4 ) );

				// Basic sanity: type should be non-zero for valid references
				if ( type == 0 && instance == 0 )
				{
					valid = false;
					break;
				}

				entries[i] = new ResourceKey( (Sims4Reader.ResourceType)type, group, instance );
				pos += 16;
			}

			if ( valid )
				return entries;
		}

		return Array.Empty<ResourceKey>();
	}

	#endregion

	#region String / Thumbnail Resolution (same patterns as CatalogObjectLoader)

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
