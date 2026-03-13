using Sandbox;
using Sandbox.Diagnostics;
using Sims4Reader;
using Sims4Reader.Material;
using Sims4Reader.Mesh;
using Sims4Reader.Rcol;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mounting.Sims4;

/// <summary>
/// Console commands for debugging Sims 4 mounted resources.
/// Usage: sims4_inspect models/furniture/0_DEADBEEF1234
/// </summary>
public static class SimsDebug
{
	static Logger Log = new Logger( "Sims4-Debug" );

	/// <summary>
	/// Inspect a mounted Sims 4 model by its mount path.
	/// Dumps the full RCOL tree as JSON: LODs, meshes, materials, textures, chunk references.
	/// </summary>
	[ConCmd( "sims4_inspect" )]
	public static void InspectModel( string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) )
		{
			Log.Info( "Usage: sims4_inspect <mount-path>" );
			Log.Info( "Example: sims4_inspect models/furniture/0_DEADBEEF1234" );
			return;
		}

		// Find the MODL entry across all mounted packages
		var mount = SimsMount.Instance;
		if ( mount == null )
		{
			Log.Warning( "Sims 4 mount not active." );
			return;
		}

		// Parse the mount path to extract the resource key
		// Mount path format: models/{category}/{Group:X}_{Instance:X}
		var fileName = System.IO.Path.GetFileName( path );
		var parts = fileName.Split( '_', 2 );
		if ( parts.Length != 2 )
		{
			Log.Warning( $"Invalid path format. Expected: models/category/GROUP_INSTANCE" );
			return;
		}

		if ( !uint.TryParse( parts[0], System.Globalization.NumberStyles.HexNumber, null, out var group ) )
		{
			Log.Warning( $"Cannot parse group '{parts[0]}' as hex." );
			return;
		}

		if ( !ulong.TryParse( parts[1], System.Globalization.NumberStyles.HexNumber, null, out var instance ) )
		{
			Log.Warning( $"Cannot parse instance '{parts[1]}' as hex." );
			return;
		}

		var targetKey = new ResourceKey( Sims4Reader.ResourceType.Model, group, instance );

		// Search across all packages
		DbpfPackage? foundPackage = null;
		ResourceEntry? foundEntry = null;

		foreach ( var pkg in mount.GetPackages() )
		{
			var entry = pkg.Find( targetKey );
			if ( entry != null )
			{
				foundPackage = pkg;
				foundEntry = entry;
				break;
			}
		}

		if ( foundPackage == null || foundEntry == null )
		{
			Log.Warning( $"MODL {targetKey} not found in any package." );
			return;
		}

		try
		{
			var info = BuildModelInfo( foundPackage, foundEntry.Value );
			var json = JsonSerializer.Serialize( info, new JsonSerializerOptions
			{
				WriteIndented = true,
				DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
				Converters = { new JsonStringEnumConverter() }
			} );

			Log.Info( $"=== sims4_inspect: {path} ===" );
			Log.Info( json );
		}
		catch ( Exception ex )
		{
			Log.Error( $"Failed to inspect {path}: {ex.Message}" );
		}
	}

	/// <summary>
	/// List all mounted Sims 4 models matching an optional filter.
	/// </summary>
	[ConCmd( "sims4_list" )]
	public static void ListModels( string filter = "" )
	{
		var mount = SimsMount.Instance;
		if ( mount == null )
		{
			Log.Warning( "Sims 4 mount not active." );
			return;
		}

		int count = 0;
		int matched = 0;
		foreach ( var pkg in mount.GetPackages() )
		{
			foreach ( var entry in pkg.FindAll( Sims4Reader.ResourceType.Model ) )
			{
				if ( entry.MemSize == 0 || entry.FileSize == 0 ) continue;
				count++;

				var name = $"{entry.Key.Group:X}_{entry.Key.Instance:X}";
				if ( !string.IsNullOrEmpty( filter ) && !name.Contains( filter, StringComparison.OrdinalIgnoreCase ) )
					continue;

				matched++;
				if ( matched <= 50 )
					Log.Info( $"  {name} (type=0x{(uint)entry.Key.Type:X8} size={entry.MemSize})" );
			}
		}

		if ( matched > 50 )
			Log.Info( $"  ... and {matched - 50} more" );

		Log.Info( $"Total MODLs: {count}, matched: {matched}" );
	}

	private static ModelInfo BuildModelInfo( DbpfPackage package, ResourceEntry modlEntry )
	{
		var rcol = package.GetResource<RcolContainer>( modlEntry );
		var modl = rcol.GetChunk<ModlChunk>();

		var info = new ModelInfo
		{
			ResourceKey = FormatKey( modlEntry.Key ),
			FileSize = modlEntry.FileSize,
			MemSize = modlEntry.MemSize,
			RcolVersion = rcol.Version,
			PublicChunks = rcol.PublicChunks,
			ChunkCount = rcol.ChunkEntries.Count,
			ExternalRefCount = rcol.ExternalReferences.Length,
		};

		// Chunks
		info.Chunks = new List<ChunkInfo>();
		for ( int i = 0; i < rcol.ChunkEntries.Count; i++ )
		{
			var ce = rcol.ChunkEntries[i];
			var ci = new ChunkInfo
			{
				Index = i,
				Region = i < rcol.PublicChunks ? "Public" : "Private",
				Type = ce.Chunk.GetType().Name,
				TypeId = $"0x{(uint)ce.Key.Type:X8}",
			};
			info.Chunks.Add( ci );
		}

		// External references
		info.ExternalRefs = new List<string>();
		for ( int i = 0; i < rcol.ExternalReferences.Length; i++ )
		{
			var ext = rcol.ExternalReferences[i];
			var resolved = package.Find( ext ) != null ? "found" : "MISSING";
			info.ExternalRefs.Add( $"[{i}] {ext.Type} 0x{(uint)ext.Type:X8} G={ext.Group:X} I={ext.Instance:X} ({resolved})" );
		}

		// MODL chunk
		if ( modl != null )
		{
			info.ModlVersion = $"0x{modl.Version:X}";
			info.Bounds = FormatBounds( modl.Bounds );

			// LODs
			info.Lods = new List<LodInfo>();
			foreach ( var lodEntry in modl.LodEntries )
			{
				var lodInfo = BuildLodInfo( package, rcol, lodEntry );
				info.Lods.Add( lodInfo );
			}
		}

		// MTST
		var mtst = rcol.GetChunk<MaterialState>();
		if ( mtst != null )
		{
			info.Mtst = BuildMtstInfo( mtst );
		}

		// Full model resolution
		try
		{
			var resolved = ModlModelLoader.LoadModel( package, modlEntry );
			info.Resolution = new ResolutionInfo
			{
				TotalLods = resolved.Lods.Count,
				TotalTextureKeys = resolved.AllTextureKeys.Count,
				Lods = new List<ResolvedLodInfo>()
			};

			foreach ( var lod in resolved.Lods )
			{
				var lodRes = new ResolvedLodInfo
				{
					LodId = lod.LodId.ToString(),
					Meshes = new List<ResolvedMeshInfo>()
				};

				foreach ( var mesh in lod.Meshes )
				{
					lodRes.Meshes.Add( new ResolvedMeshInfo
					{
						NameHash = $"0x{mesh.NameHash:X8}",
						VertexCount = mesh.Vertices.Length,
						IndexCount = mesh.Indices.Length,
						TriangleCount = mesh.Indices.Length / 3,
						PrimitiveType = mesh.PrimitiveType.ToString(),
						HasMaterial = mesh.Material != null,
						ShaderEntryCount = mesh.Material?.ShaderEntries.Count ?? 0,
						TextureKeys = mesh.TextureKeys.ToDictionary(
							kv => kv.Key.ToString(),
							kv => FormatKey( kv.Value )
						),
						ShaderParams = mesh.Material != null
							? BuildShaderParams( mesh.Material )
							: null,
						BoundsMin = $"({mesh.BoundsMin[0]:F2}, {mesh.BoundsMin[1]:F2}, {mesh.BoundsMin[2]:F2})",
						BoundsMax = $"({mesh.BoundsMax[0]:F2}, {mesh.BoundsMax[1]:F2}, {mesh.BoundsMax[2]:F2})",
						JointCount = mesh.JointReferences.Count,
					} );
				}

				info.Resolution.Lods.Add( lodRes );
			}
		}
		catch ( Exception ex )
		{
			info.ResolutionError = ex.Message;
		}

		return info;
	}

	private static LodInfo BuildLodInfo( DbpfPackage package, RcolContainer rcol, LodEntry lodEntry )
	{
		uint mlodRef = lodEntry.MlodChunkRef;
		var refType = ChunkReference.GetReferenceType( mlodRef );
		int localIdx = ChunkReference.GetTgiIndex( mlodRef );

		var info = new LodInfo
		{
			LodId = lodEntry.Id.ToString(),
			ChunkRef = $"0x{mlodRef:X8}",
			RefType = refType.ToString(),
			LocalIndex = localIdx,
		};

		if ( ChunkReference.IsDelayed( mlodRef ) && localIdx >= 0 && localIdx < rcol.ExternalReferences.Length )
		{
			var extKey = rcol.ExternalReferences[localIdx];
			info.ExternalKey = FormatKey( extKey );
			info.ExternalFound = package.Find( extKey ) != null;
		}
		else if ( !ChunkReference.IsDelayed( mlodRef ) )
		{
			int absIdx = ChunkReference.ResolveChunkIndex( mlodRef, rcol.PublicChunks );
			info.AbsoluteIndex = absIdx;
			if ( absIdx >= 0 && absIdx < rcol.ChunkEntries.Count )
				info.ChunkType = rcol.ChunkEntries[absIdx].Chunk.GetType().Name;
		}

		return info;
	}

	private static MtstInfo BuildMtstInfo( MaterialState mtst )
	{
		var info = new MtstInfo
		{
			Version = $"0x{mtst.Version:X}",
			NameHash = $"0x{mtst.NameHash:X8}",
			DefaultMatdRef = $"0x{mtst.DefaultMatdIndex:X8}",
			Entries = new List<MtstEntryInfo>()
		};

		if ( mtst.Entries200 != null )
		{
			foreach ( var e in mtst.Entries200 )
			{
				info.Entries.Add( new MtstEntryInfo
				{
					State = e.State.ToString(),
					MatdRef = $"0x{e.MatdIndex:X8}",
					RefType = ChunkReference.GetReferenceType( e.MatdIndex ).ToString(),
					LocalIndex = ChunkReference.GetTgiIndex( e.MatdIndex ),
				} );
			}
		}

		if ( mtst.Entries300 != null )
		{
			foreach ( var e in mtst.Entries300 )
			{
				info.Entries.Add( new MtstEntryInfo
				{
					State = e.State.ToString(),
					MatdRef = $"0x{e.MatdIndex:X8}",
					RefType = ChunkReference.GetReferenceType( e.MatdIndex ).ToString(),
					LocalIndex = ChunkReference.GetTgiIndex( e.MatdIndex ),
					MaterialVariant = e.MaterialVariant,
				} );
			}
		}

		return info;
	}

	private static List<ShaderParamInfo> BuildShaderParams( MaterialDefinition matd )
	{
		var result = new List<ShaderParamInfo>();
		foreach ( var entry in matd.ShaderEntries )
		{
			var param = new ShaderParamInfo
			{
				Field = entry.Field.ToString(),
				Type = entry.GetType().Name,
			};

			switch ( entry )
			{
				case ShaderFloat f:
					param.Value = f.Value.ToString( "F4" );
					break;
				case ShaderFloat2 f2:
					param.Value = $"({f2.X:F4}, {f2.Y:F4})";
					break;
				case ShaderFloat3 f3:
					param.Value = $"({f3.X:F4}, {f3.Y:F4}, {f3.Z:F4})";
					break;
				case ShaderFloat4 f4:
					param.Value = $"({f4.X:F4}, {f4.Y:F4}, {f4.Z:F4}, {f4.W:F4})";
					break;
				case ShaderInt si:
					param.Value = si.Value.ToString();
					break;
				case ShaderTextureRef texRef:
					param.Value = FormatKey( texRef.Key );
					break;
				case ShaderTextureKey texKey:
					param.Value = FormatKey( texKey.Key );
					break;
				case ShaderImageMapKey imgKey:
					param.Value = FormatKey( imgKey.Key );
					break;
				case ShaderTextureIndex texIdx:
					param.Value = $"ExternalRef[{texIdx.Index}]";
					break;
			}

			result.Add( param );
		}
		return result;
	}

	private static string FormatKey( ResourceKey key )
	{
		return $"{key.Type} (0x{(uint)key.Type:X8}) G=0x{key.Group:X} I=0x{key.Instance:X}";
	}

	private static string FormatBounds( BoundingBox bb )
	{
		return $"({bb.MinX:F2},{bb.MinY:F2},{bb.MinZ:F2}) → ({bb.MaxX:F2},{bb.MaxY:F2},{bb.MaxZ:F2})";
	}

	// --- JSON data classes ---

	class ModelInfo
	{
		public string ResourceKey { get; set; } = "";
		public uint FileSize { get; set; }
		public uint MemSize { get; set; }
		public uint RcolVersion { get; set; }
		public int PublicChunks { get; set; }
		public int ChunkCount { get; set; }
		public int ExternalRefCount { get; set; }
		public List<ChunkInfo>? Chunks { get; set; }
		public List<string>? ExternalRefs { get; set; }
		public string? ModlVersion { get; set; }
		public string? Bounds { get; set; }
		public List<LodInfo>? Lods { get; set; }
		public MtstInfo? Mtst { get; set; }
		public ResolutionInfo? Resolution { get; set; }
		public string? ResolutionError { get; set; }
	}

	class ChunkInfo
	{
		public int Index { get; set; }
		public string Region { get; set; } = "";
		public string Type { get; set; } = "";
		public string TypeId { get; set; } = "";
	}

	class LodInfo
	{
		public string LodId { get; set; } = "";
		public string ChunkRef { get; set; } = "";
		public string RefType { get; set; } = "";
		public int LocalIndex { get; set; }
		public int? AbsoluteIndex { get; set; }
		public string? ChunkType { get; set; }
		public string? ExternalKey { get; set; }
		public bool? ExternalFound { get; set; }
	}

	class MtstInfo
	{
		public string Version { get; set; } = "";
		public string NameHash { get; set; } = "";
		public string DefaultMatdRef { get; set; } = "";
		public List<MtstEntryInfo>? Entries { get; set; }
	}

	class MtstEntryInfo
	{
		public string State { get; set; } = "";
		public string MatdRef { get; set; } = "";
		public string RefType { get; set; } = "";
		public int LocalIndex { get; set; }
		public uint? MaterialVariant { get; set; }
	}

	class ResolutionInfo
	{
		public int TotalLods { get; set; }
		public int TotalTextureKeys { get; set; }
		public List<ResolvedLodInfo>? Lods { get; set; }
	}

	class ResolvedLodInfo
	{
		public string LodId { get; set; } = "";
		public List<ResolvedMeshInfo>? Meshes { get; set; }
	}

	class ResolvedMeshInfo
	{
		public string NameHash { get; set; } = "";
		public int VertexCount { get; set; }
		public int IndexCount { get; set; }
		public int TriangleCount { get; set; }
		public string PrimitiveType { get; set; } = "";
		public bool HasMaterial { get; set; }
		public int ShaderEntryCount { get; set; }
		public Dictionary<string, string>? TextureKeys { get; set; }
		public List<ShaderParamInfo>? ShaderParams { get; set; }
		public string BoundsMin { get; set; } = "";
		public string BoundsMax { get; set; } = "";
		public int JointCount { get; set; }
	}

	class ShaderParamInfo
	{
		public string Field { get; set; } = "";
		public string Type { get; set; } = "";
		public string? Value { get; set; }
	}
}
