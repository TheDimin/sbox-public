using Sims4Reader.Material;
using Sims4Reader.Rcol;

namespace Sims4Reader.Mesh;

/// <summary>
/// A fully resolved LOD level containing geometry, materials, and texture references.
/// </summary>
public class ResolvedLod
{
    /// <summary>The LOD detail level.</summary>
    public LodId LodId { get; set; }

    /// <summary>Resolved meshes for this LOD level.</summary>
    public List<ResolvedMesh> Meshes { get; set; } = new();
}

/// <summary>
/// A single resolved mesh with its geometry data and material/texture references.
/// </summary>
public class ResolvedMesh
{
    /// <summary>Mesh name hash from the MLOD entry.</summary>
    public uint NameHash { get; set; }

    /// <summary>Decoded vertex data (positions, normals, UVs, etc.).</summary>
    public Vertex[] Vertices { get; set; } = Array.Empty<Vertex>();

    /// <summary>Triangle indices (3 per triangle).</summary>
    public int[] Indices { get; set; } = Array.Empty<int>();

    /// <summary>Primitive type for rendering (usually TriangleList).</summary>
    public ModelPrimitiveType PrimitiveType { get; set; }

    /// <summary>
    /// The material definition for this mesh, if resolved.
    /// Contains shader entries with texture references.
    /// </summary>
    public MaterialDefinition? Material { get; set; }

    /// <summary>
    /// Texture resource keys referenced by this mesh's material.
    /// Keyed by shader field type (DiffuseMap, NormalMap, SpecularMap, etc.).
    /// </summary>
    public Dictionary<ShaderFieldType, ResourceKey> TextureKeys { get; set; } = new();

    /// <summary>
    /// The resource key of the MATD used by this mesh, if it was an external reference.
    /// Null for inline (public/private) materials embedded in the MODL RCOL.
    /// </summary>
    public ResourceKey? MaterialResourceKey { get; set; }

    /// <summary>
    /// The mount path for this mesh's material. Always set when Material is non-null.
    /// For external MATDs: "materials/{group:X}_{instance:X}"
    /// For inline MATDs: "materials/inline/{modlGroup:X}_{modlInstance:X}_m{meshIndex}"
    /// </summary>
    public string? MaterialMountPath { get; set; }

    /// <summary>Bounding box minimum corner.</summary>
    public float[] BoundsMin { get; set; } = new float[3];

    /// <summary>Bounding box maximum corner.</summary>
    public float[] BoundsMax { get; set; } = new float[3];

    /// <summary>Bone name hashes referenced by this mesh (for skinned meshes).</summary>
    public List<uint> JointReferences { get; set; } = new();
}

/// <summary>
/// A fully resolved model with all LOD levels, geometry, materials, and texture references.
/// </summary>
public class ResolvedModel
{
    /// <summary>The MODL resource key.</summary>
    public ResourceKey Key { get; set; }

    /// <summary>Model bounding box.</summary>
    public BoundingBox Bounds { get; set; }

    /// <summary>Resolved LOD levels, ordered from highest to lowest detail.</summary>
    public List<ResolvedLod> Lods { get; set; } = new();

    /// <summary>
    /// All unique texture resource keys referenced across all LODs and meshes.
    /// Use these to load textures from the package.
    /// </summary>
    public HashSet<ResourceKey> AllTextureKeys { get; set; } = new();

    /// <summary>
    /// Get the highest detail LOD available, or null if no LODs exist.
    /// </summary>
    public ResolvedLod? GetBestLod()
    {
        return Lods.Count > 0 ? Lods[0] : null;
    }
}

/// <summary>
/// Resolves MODL resources into fully loaded model data with geometry, materials, and texture references.
///
/// Pipeline: MODL → LODEntry → MLOD → LodMesh → VBUF/IBUF/VRTF (geometry)
///                                              + MATD (material) → texture ResourceKeys
///
/// Chunk references use three resolution modes based on their upper 4 bits:
///   Public (0x0):  ChunkEntries[index]
///   Private (0x1): ChunkEntries[index + PublicChunks]
///   Delayed (0x3): ExternalReferences[index] → load from package
///
/// Supports cross-package resource lookup: TS4 spreads MODL, MATD, and texture
/// resources across many .package files. When <c>allPackages</c> is provided,
/// delayed references that can't be found in the primary package are searched
/// across all loaded packages.
/// </summary>
public static class ModlModelLoader
{
    /// <summary>
    /// Load a fully resolved model from a MODL resource entry.
    /// Resolves the entire chain: MODL → MLOD → geometry + materials + textures.
    /// </summary>
    /// <param name="package">The primary package containing the MODL entry.</param>
    /// <param name="modlEntry">The MODL resource entry to load.</param>
    /// <param name="allPackages">
    /// Optional list of all loaded packages for cross-package reference lookup.
    /// When provided, delayed references not found in <paramref name="package"/>
    /// are searched across all packages. Pass null for single-package lookup.
    /// </param>
    public static ResolvedModel LoadModel(
        DbpfPackage package,
        ResourceEntry modlEntry,
        IReadOnlyList<DbpfPackage>? allPackages = null )
    {
        var rcol = package.GetResource<RcolContainer>(modlEntry);
        var modl = rcol.GetChunk<ModlChunk>()
            ?? throw new InvalidDataException("MODL chunk not found in RCOL container");

        var model = new ResolvedModel
        {
            Key = modlEntry.Key,
            Bounds = modl.Bounds,
        };

        foreach (var lodEntry in modl.LodEntries)
        {
            var resolvedLod = ResolveLod(package, rcol, lodEntry, modlEntry.Key, allPackages);
            if (resolvedLod != null)
                model.Lods.Add(resolvedLod);
        }

        // Sort LODs: highest detail first
        model.Lods.Sort((a, b) => a.LodId.CompareTo(b.LodId));

        // Collect all unique texture keys
        foreach (var lod in model.Lods)
            foreach (var mesh in lod.Meshes)
                foreach (var texKey in mesh.TextureKeys.Values)
                    model.AllTextureKeys.Add(texKey);

        return model;
    }

    /// <summary>
    /// Find a resource entry by key, searching the primary package first,
    /// then all other loaded packages if provided.
    /// Returns both the found entry and the package it was found in (needed
    /// to call GetResource on the correct package).
    /// </summary>
    private static (DbpfPackage Package, ResourceEntry Entry)? FindEntry(
        ResourceKey key,
        DbpfPackage primaryPackage,
        IReadOnlyList<DbpfPackage>? allPackages )
    {
        var entry = primaryPackage.Find(key);
        if (entry != null)
            return (primaryPackage, entry.Value);

        if (allPackages != null)
        {
            foreach (var pkg in allPackages)
            {
                if (ReferenceEquals(pkg, primaryPackage))
                    continue;

                entry = pkg.Find(key);
                if (entry != null)
                    return (pkg, entry.Value);
            }
        }

        return null;
    }

    private static ResolvedLod? ResolveLod(
        DbpfPackage package,
        RcolContainer rcol,
        LodEntry lodEntry,
        ResourceKey modlKey,
        IReadOnlyList<DbpfPackage>? allPackages )
    {
        uint mlodRef = lodEntry.MlodChunkRef;
        int mlodLocalIdx = ChunkReference.GetTgiIndex(mlodRef);
        if (mlodLocalIdx < 0)
            return null;

        MeshLod? mlodChunk = null;
        RcolContainer mlodRcol = rcol;

        if (ChunkReference.IsDelayed(mlodRef))
        {
            // Delayed: ExternalReferences[index] → load from package (or cross-package)
            if (mlodLocalIdx < rcol.ExternalReferences.Length)
            {
                var extKey = rcol.ExternalReferences[mlodLocalIdx];
                var found = FindEntry(extKey, package, allPackages);
                if (found != null)
                {
                    mlodRcol = found.Value.Package.GetResource<RcolContainer>(found.Value.Entry);
                    mlodChunk = mlodRcol.GetChunk<MeshLod>();
                }
            }
        }
        else
        {
            // Public/Private: resolve to absolute chunk index
            int absIdx = ChunkReference.ResolveChunkIndex(mlodRef, rcol.PublicChunks);
            if (absIdx >= 0 && absIdx < rcol.ChunkEntries.Count)
                mlodChunk = rcol.ChunkEntries[absIdx].Chunk as MeshLod;
        }

        if (mlodChunk == null)
            return null;

        var resolvedLod = new ResolvedLod { LodId = lodEntry.Id };

        for (int meshIdx = 0; meshIdx < mlodChunk.Meshes.Count; meshIdx++)
        {
            var resolvedMesh = ResolveMesh(package, mlodRcol, mlodChunk.Meshes[meshIdx], modlKey, meshIdx, allPackages);
            resolvedLod.Meshes.Add(resolvedMesh);
        }

        // Deduplicate inline material mount paths.
        // Multiple meshes often reference different MTST chunks that all resolve
        // to the same underlying MATD. Dedup by the resolved MATD identity
        // (MaterialNameHash) so they share a single mount path and Material object.
        var matdToPath = new Dictionary<uint, string>();
        for (int i = 0; i < resolvedLod.Meshes.Count; i++)
        {
            var mesh = resolvedLod.Meshes[i];
            if (mesh.MaterialResourceKey != null || mesh.Material == null || mesh.MaterialMountPath == null)
                continue;

            var matdHash = mesh.Material.MaterialNameHash;
            if (matdToPath.TryGetValue(matdHash, out var canonicalPath))
                mesh.MaterialMountPath = canonicalPath;
            else
                matdToPath[matdHash] = mesh.MaterialMountPath;
        }

        return resolvedLod;
    }

    private static ResolvedMesh ResolveMesh(
        DbpfPackage package,
        RcolContainer rcol,
        LodMesh lodMesh,
        ResourceKey modlKey,
        int meshIndex,
        IReadOnlyList<DbpfPackage>? allPackages )
    {
        var resolved = new ResolvedMesh
        {
            NameHash = lodMesh.Name,
            PrimitiveType = lodMesh.PrimitiveType,
            BoundsMin = lodMesh.BoundsMin,
            BoundsMax = lodMesh.BoundsMax,
            JointReferences = lodMesh.JointReferences,
        };

        int publicChunks = rcol.PublicChunks;

        // Resolve vertex format (VRTF)
        VertexFormat? vrtf = ResolveChunkRef<VertexFormat>(package, rcol, lodMesh.VertexFormatRef, publicChunks, allPackages);

        // Resolve vertex buffer (VBUF)
        VertexBuffer? vbuf = ResolveChunkRef<VertexBuffer>(package, rcol, lodMesh.VertexBufferRef, publicChunks, allPackages);

        // Resolve index buffer (IBUF)
        IndexBuffer? ibuf = ResolveChunkRef<IndexBuffer>(package, rcol, lodMesh.IndexBufferRef, publicChunks, allPackages);

        // Decode vertices
        if (vbuf != null && vrtf != null)
        {
            resolved.Vertices = vbuf.GetVertices(
                vrtf,
                lodMesh.StreamOffset,
                lodMesh.VertexCount);
        }

        // Decode indices and rebase from shared-buffer positions to the 0-based
        // extracted vertex slice. TS4 models may store indices as:
        //   (a) Absolute offsets into the shared VBUF — need MinVertexIndex subtracted
        //   (b) Already relative to the mesh's own vertex range — use as-is
        // We detect the correct mode by checking if rebased indices fall in range.
        if (ibuf != null)
        {
            resolved.Indices = ibuf.GetIndices(
                lodMesh.StartIndex,
                lodMesh.PrimitiveCount);

            int minVertex = lodMesh.MinVertexIndex;
            int vertexCount = resolved.Vertices.Length;

            if (minVertex > 0 && vertexCount > 0)
            {
                // Check if indices are absolute (need rebasing)
                bool needsRebasing = false;
                for (int i = 0; i < resolved.Indices.Length; i++)
                {
                    if (resolved.Indices[i] >= vertexCount)
                    {
                        needsRebasing = true;
                        break;
                    }
                }

                if (needsRebasing)
                {
                    for (int i = 0; i < resolved.Indices.Length; i++)
                        resolved.Indices[i] -= minVertex;

                    // Validate post-rebasing: if any index is still out of range,
                    // the rebasing was wrong — clear indices so the mesh is skipped downstream.
                    for (int i = 0; i < resolved.Indices.Length; i++)
                    {
                        if ((uint)resolved.Indices[i] >= (uint)vertexCount)
                        {
                            resolved.Indices = Array.Empty<int>();
                            break;
                        }
                    }
                }
            }
        }

        // Resolve material (MATD)
        // The material ref might point to a MATD directly or to an MTST.
        // If it's an MTST, follow DefaultMatdIndex to get the actual MATD.
        var matdResult = ResolveMaterial(package, rcol, lodMesh.MaterialRef, publicChunks, allPackages);

        if (matdResult != null)
        {
            resolved.Material = matdResult.Value.Matd;
            resolved.MaterialResourceKey = matdResult.Value.MatdResourceKey;
            resolved.TextureKeys = ExtractTextureKeys(matdResult.Value.Matd, matdResult.Value.ExternalReferences);

            // Set MaterialMountPath for both external and inline MATDs
            if (matdResult.Value.MatdResourceKey is { } matKey)
            {
                // External: use the MATD's own resource key
                resolved.MaterialMountPath = $"materials/{matKey.Group:X}_{matKey.Instance:X}";
            }
            else
            {
                // Inline: use MODL key + mesh index for a deterministic path
                resolved.MaterialMountPath = $"materials/inline/{modlKey.Group:X}_{modlKey.Instance:X}_m{meshIndex}";
            }
        }

        return resolved;
    }

    /// <summary>
    /// Result of material resolution: the MATD plus the correct external references
    /// array from the RCOL container that contains the MATD. This is important because
    /// ShaderTextureIndex entries reference the MATD's own RCOL external references,
    /// not necessarily the MLOD's.
    /// </summary>
    private record struct MaterialResult(MaterialDefinition Matd, ResourceKey[] ExternalReferences, ResourceKey? MatdResourceKey = null);

    /// <summary>
    /// Resolve a material chunk reference. If it points to a MATD, return it directly.
    /// If it points to an MTST (MaterialState), follow DefaultMatdIndex to get the MATD.
    /// Returns both the MATD and its owning RCOL's external references.
    /// </summary>
    private static MaterialResult? ResolveMaterial(
        DbpfPackage package,
        RcolContainer rcol,
        uint materialRef,
        int publicChunks,
        IReadOnlyList<DbpfPackage>? allPackages )
    {
        int localIdx = ChunkReference.GetTgiIndex(materialRef);
        if (localIdx < 0)
            return null;

        if (ChunkReference.IsDelayed(materialRef))
        {
            // External material resource
            if (localIdx < rcol.ExternalReferences.Length)
            {
                var extKey = rcol.ExternalReferences[localIdx];
                var found = FindEntry(extKey, package, allPackages);
                if (found != null)
                {
                    var extRcol = found.Value.Package.GetResource<RcolContainer>(found.Value.Entry);
                    // Try MATD first, then MTST
                    var matd = extRcol.GetChunk<MaterialDefinition>();
                    if (matd != null) return new MaterialResult(matd, extRcol.ExternalReferences, extKey);

                    var mtst = extRcol.GetChunk<MaterialState>();
                    if (mtst != null)
                        return ResolveMatdFromMtst(mtst, extRcol, package, allPackages);
                }
            }
            return null;
        }

        // Public/Private: resolve to absolute chunk index
        int absIdx = ChunkReference.ResolveChunkIndex(materialRef, publicChunks);
        if (absIdx < 0 || absIdx >= rcol.ChunkEntries.Count)
            return null;

        var chunk = rcol.ChunkEntries[absIdx].Chunk;

        if (chunk is MaterialDefinition directMatd)
            return new MaterialResult(directMatd, rcol.ExternalReferences);

        // If we hit an MTST, follow its DefaultMatdIndex
        if (chunk is MaterialState mtst2)
            return ResolveMatdFromMtst(mtst2, rcol, package, allPackages);

        return null;
    }

    /// <summary>
    /// Follow an MTST to get the default MATD chunk.
    /// First tries DefaultMatdIndex, then falls back to the entry with state Default (0x2EA8FB98).
    /// </summary>
    private static MaterialResult? ResolveMatdFromMtst(
        MaterialState mtst,
        RcolContainer rcol,
        DbpfPackage package,
        IReadOnlyList<DbpfPackage>? allPackages )
    {
        // Try DefaultMatdIndex first
        var matd = ResolveChunkRefAsMatd(mtst.DefaultMatdIndex, rcol, package, allPackages);
        if (matd != null) return matd;

        // Fall back to the Default state entry in the MTST entries
        uint defaultMatdRef = 0;
        if (mtst.Entries200 != null)
        {
            foreach (var e in mtst.Entries200)
                if (e.State == MaterialStateType.Default)
                { defaultMatdRef = e.MatdIndex; break; }
        }
        else if (mtst.Entries300 != null)
        {
            foreach (var e in mtst.Entries300)
                if (e.State == MaterialStateType.Default)
                { defaultMatdRef = e.MatdIndex; break; }
        }

        if (defaultMatdRef != 0)
            return ResolveChunkRefAsMatd(defaultMatdRef, rcol, package, allPackages);

        // Last resort: try the first entry regardless of state
        uint firstRef = 0;
        if (mtst.Entries200 is { Count: > 0 })
            firstRef = mtst.Entries200[0].MatdIndex;
        else if (mtst.Entries300 is { Count: > 0 })
            firstRef = mtst.Entries300[0].MatdIndex;

        return firstRef != 0 ? ResolveChunkRefAsMatd(firstRef, rcol, package, allPackages) : null;
    }

    private static MaterialResult? ResolveChunkRefAsMatd(
        uint matdRef,
        RcolContainer rcol,
        DbpfPackage package,
        IReadOnlyList<DbpfPackage>? allPackages )
    {
        int localIdx = ChunkReference.GetTgiIndex(matdRef);
        if (localIdx < 0) return null;

        if (ChunkReference.IsDelayed(matdRef))
        {
            if (localIdx < rcol.ExternalReferences.Length)
            {
                var extKey = rcol.ExternalReferences[localIdx];
                var found = FindEntry(extKey, package, allPackages);
                if (found != null)
                {
                    var extRcol = found.Value.Package.GetResource<RcolContainer>(found.Value.Entry);
                    var matd = extRcol.GetChunk<MaterialDefinition>();
                    if (matd != null)
                        return new MaterialResult(matd, extRcol.ExternalReferences, extKey);
                }
            }
            return null;
        }

        int absIdx = ChunkReference.ResolveChunkIndex(matdRef, rcol.PublicChunks);
        if (absIdx >= 0 && absIdx < rcol.ChunkEntries.Count)
        {
            var matd = rcol.ChunkEntries[absIdx].Chunk as MaterialDefinition;
            if (matd != null)
                return new MaterialResult(matd, rcol.ExternalReferences);
        }

        return null;
    }

    /// <summary>
    /// Resolve a typed chunk from a raw chunk reference, handling Public/Private/Delayed.
    /// </summary>
    private static T? ResolveChunkRef<T>(
        DbpfPackage package,
        RcolContainer rcol,
        uint chunkRef,
        int publicChunks,
        IReadOnlyList<DbpfPackage>? allPackages = null ) where T : RcolChunk
    {
        int localIdx = ChunkReference.GetTgiIndex(chunkRef);
        if (localIdx < 0)
            return null;

        if (ChunkReference.IsDelayed(chunkRef))
        {
            if (localIdx < rcol.ExternalReferences.Length)
            {
                var extKey = rcol.ExternalReferences[localIdx];
                var found = FindEntry(extKey, package, allPackages);
                if (found != null)
                {
                    var extRcol = found.Value.Package.GetResource<RcolContainer>(found.Value.Entry);
                    return extRcol.GetChunk<T>();
                }
            }
            return null;
        }

        int absIdx = ChunkReference.ResolveChunkIndex(chunkRef, publicChunks);
        if (absIdx >= 0 && absIdx < rcol.ChunkEntries.Count)
            return rcol.ChunkEntries[absIdx].Chunk as T;

        return null;
    }

    /// <summary>
    /// Lightweight scan: returns the number of meshes in the best LOD that use inline materials.
    /// Does NOT decode any geometry (VBUF/IBUF). Only parses RCOL → MODL → MLOD chunk headers
    /// and checks material reference types.
    ///
    /// Returns a list of mesh indices that have inline (non-delayed) material references.
    /// Returns null if the MODL cannot be parsed.
    /// </summary>
    public static List<int>? ScanInlineMaterialMeshes(
        DbpfPackage package,
        ResourceEntry modlEntry )
    {
        var rcol = package.GetResource<RcolContainer>(modlEntry);
        var modl = rcol.GetChunk<ModlChunk>();
        if (modl == null || modl.LodEntries.Count == 0)
            return null;

        // Sort LODs by ID to get best (lowest ID = highest detail)
        var sortedLods = modl.LodEntries.OrderBy(l => l.Id).ToList();

        // Try to get the MLOD chunk for the best LOD
        foreach (var lodEntry in sortedLods)
        {
            uint mlodRef = lodEntry.MlodChunkRef;
            int mlodLocalIdx = ChunkReference.GetTgiIndex(mlodRef);
            if (mlodLocalIdx < 0)
                continue;

            MeshLod? mlodChunk = null;
            RcolContainer mlodRcol = rcol;

            if (ChunkReference.IsDelayed(mlodRef))
            {
                if (mlodLocalIdx < rcol.ExternalReferences.Length)
                {
                    var extKey = rcol.ExternalReferences[mlodLocalIdx];
                    var entry = package.Find(extKey);
                    if (entry != null)
                    {
                        mlodRcol = package.GetResource<RcolContainer>(entry.Value);
                        mlodChunk = mlodRcol.GetChunk<MeshLod>();
                    }
                }
            }
            else
            {
                int absIdx = ChunkReference.ResolveChunkIndex(mlodRef, rcol.PublicChunks);
                if (absIdx >= 0 && absIdx < rcol.ChunkEntries.Count)
                    mlodChunk = rcol.ChunkEntries[absIdx].Chunk as MeshLod;
            }

            if (mlodChunk == null)
                continue;

            var inlineMeshes = new List<int>();
            var seenMatdHashes = new HashSet<uint>();
            int publicChunks = mlodRcol.PublicChunks;
            for (int i = 0; i < mlodChunk.Meshes.Count; i++)
            {
                var materialRef = mlodChunk.Meshes[i].MaterialRef;
                int matIdx = ChunkReference.GetTgiIndex(materialRef);
                if (matIdx < 0)
                    continue;

                // Delayed = external MATD (already mounted as standalone entry)
                if (ChunkReference.IsDelayed(materialRef))
                    continue;

                // Resolve the inline ref to the actual MATD (following MTST if needed)
                // to dedup by resolved material identity, not chunk ref.
                var matdResult = ResolveMaterial(package, mlodRcol, materialRef, publicChunks, null);
                if (matdResult == null)
                    continue;

                // Only mount one loader per unique resolved MATD
                if (seenMatdHashes.Add(matdResult.Value.Matd.MaterialNameHash))
                    inlineMeshes.Add(i);
            }

            return inlineMeshes;
        }

        return null;
    }

    /// <summary>
    /// Extract texture resource keys from a material definition's shader entries.
    /// </summary>
    /// <param name="matd">The material definition.</param>
    /// <param name="externalReferences">
    /// The external references array from the RCOL that contains this MATD.
    /// Used to resolve ShaderTextureIndex entries (GEOM context).
    /// </param>
    public static Dictionary<ShaderFieldType, ResourceKey> ExtractTextureKeys(
        MaterialDefinition matd,
        ResourceKey[] externalReferences)
    {
        var textures = new Dictionary<ShaderFieldType, ResourceKey>();

        foreach (var entry in matd.ShaderEntries)
        {
            ResourceKey? key = entry switch
            {
                ShaderTextureRef texRef => texRef.Key,
                ShaderTextureKey texKey => texKey.Key,
                ShaderImageMapKey imgKey => imgKey.Key,
                ShaderTextureIndex texIdx when texIdx.Index >= 0 && texIdx.Index < externalReferences.Length
                    => externalReferences[texIdx.Index],
                _ => null,
            };

            if (key.HasValue && (uint)key.Value.Type != 0)
                textures[entry.Field] = key.Value;
        }

        return textures;
    }
}
