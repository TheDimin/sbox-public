using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NativeEngine;
using Sandbox.Gltf;
using Sandbox.Gltf.DirectGpu;

namespace Sandbox;

using NMatrix = System.Numerics.Matrix4x4;

/// <summary>The asset uses a feature the native compiler does not write yet. The source path still loads it.</summary>
internal sealed class GltfNativeUnsupportedException( string message ) : NotSupportedException( message );

/// <summary>
/// Results of earlier compiles of one source, by unit. A unit is reused when its inputs are the same: decoded
/// primitives by their JSON, generated tangents and baked vertices by their geometry, UV routing and matrix, and
/// textures by their image bytes. The editor keeps one cache per open asset, so a change to one material or one
/// texture recompiles only what depends on it.
/// </summary>
internal sealed class GltfNativeCompileCache
{
	internal readonly Dictionary<string, GltfGpuPrimitivePlan> Primitives = new( StringComparer.Ordinal );
	internal readonly Dictionary<string, GltfGpuGeneratedTangentRoute> Tangents = new( StringComparer.Ordinal );
	internal readonly Dictionary<string, GltfNativeCompiler.BakedRow> Rows = new( StringComparer.Ordinal );
	internal readonly Dictionary<string, byte[]> Textures = new( StringComparer.Ordinal );

	/// <summary>Units compiled and reused by the last compile.</summary>
	internal (int Primitives, int Tangents, int Rows, int Textures) Compiled, Reused;

	/// <summary>Milliseconds the last compile spent per phase.</summary>
	internal (double Plans, double Tangents, double Bakes, double Materials, double Compress) Timings;

	internal void ResetCounters() { Compiled = default; Reused = default; Timings = default; }
}

/// <summary>
/// Compiles a glTF document into the native model data stored in its <c>.glb_c</c> (see <see cref="GltfNativePack"/>).
/// All per-vertex and per-texel work happens here, at import: accessor decode, Draco, generated normals and
/// MikkTSpace tangents, UV routing, node transforms and Basis to BC7 transcode. The material recipes come from the
/// same adapters the source loader uses.
/// </summary>
internal static class GltfNativeCompiler
{
	internal sealed record BakedMorph( string Name, byte[] Deltas, int Count );

	internal sealed record BakedRow( byte[] Vertices, byte[] Indices, int Stride, int VertexCount, int IndexCount, GltfNativeVertexField[] Fields, int Mode, float[] Min, float[] Max, BakedMorph[] Morphs );

	/// <summary>
	/// One drawn primitive of one node instance. <c>Matrix</c> is what its vertices are stored with. <c>World</c> is its glTF
	/// world matrix (instance transform times node world). <c>Chain</c> is its MSFT_lod chain or -1, and <c>Placement</c>
	/// the node that places it in a scene pack: the chain's base node for a LOD level, else its own node.
	/// </summary>
	private sealed record Row( int Node, int? InstanceIndex, int Mesh, int Primitive, NMatrix Matrix, bool Reversed, int LodMask, int? Skin = null,
		NMatrix World = default, int Chain = -1, int Level = 0, int Placement = -1, GltfNativeLodSettings Simplify = null );

	/// <summary>An MSFT_lod chain: its base node and the screen coverage threshold of each level.</summary>
	private sealed record Chain( int Node, float[] Thresholds );

	/// <summary>
	/// Compile the selected scene. Throws <see cref="GltfNativeUnsupportedException"/> for content the native path does
	/// not cover yet. A file on disk compresses its blobs; a preview that is built at once leaves them raw. A static
	/// asset compiles to a scene of shared geometries unless <paramref name="scene"/> is false, which gives one model.
	/// </summary>
	internal static (GltfNativePackHeader Header, List<byte[]> Blobs) Compile( GltfDocument document, int? sceneIndex = null, GltfNativeCompileCache cache = null, bool compress = true, bool scene = true )
	{
		ArgumentNullException.ThrowIfNull( document );
		ThreadSafe.AssertIsMainThread();
		cache ??= new GltfNativeCompileCache();
		cache.ResetCounters();
		var root = document.Root;
		var variantNames = document.ReadMaterialVariantNames();
		var animated = root.TryGetProperty( "skins", out var skins ) && skins.GetArrayLength() != 0 ||
			root.TryGetProperty( "animations", out var animations ) && animations.GetArrayLength() != 0;
		if ( root.TryGetProperty( "animations", out var clips ) )
			foreach ( var clip in clips.EnumerateArray() )
				foreach ( var channel in clip.GetProperty( "channels" ).EnumerateArray() )
					if ( channel.GetProperty( "target" ).TryGetProperty( "path", out var path ) && path.GetString() == "weights" )
						throw new GltfNativeUnsupportedException( "Animated morph weights have no native animation track yet." );
		var warnings = new List<string>();
		var (rows, lodDistances, order, chains) = CollectRows( document, sceneIndex, warnings );
		// A skinned or animated asset gets a skeleton: one bone per scene node. Its rows are stored relative to their binds.
		var skeleton = animated ? GltfNativeSkeleton.Create( document, order, document.EvaluatePose() ) : null;
		if ( skeleton is not null )
		{
			if ( rows.Any( x => x.InstanceIndex is not null ) ) throw new GltfNativeUnsupportedException( "EXT_mesh_gpu_instancing in a skinned or animated asset is not compiled to native data yet." );
			rows = rows.Select( x => x with { Matrix = skeleton.VertexMatrix( x.Node, x.Skin is not null ), Reversed = false } ).ToList();
		}
		// A static asset is a scene: shared geometries placed by nodes, so each piece culls and switches LOD on its own.
		Dictionary<(int Node, int? Instance), Transform> placements = null;
		if ( skeleton is null && scene ) rows = PlaceRows( document, rows, out placements );

		var blobs = new List<byte[]>();
		var textureBlobs = new Dictionary<byte[], int>( ReferenceEqualityComparer.Instance );
		var textureTable = new List<int>();
		var materials = new List<GltfNativePackMaterial>();
		var materialIds = new Dictionary<string, int>( StringComparer.Ordinal );
		var meshes = new List<GltfNativePackMesh>();
		var prepared = new Dictionary<int, object>();
		var compileTexture = TextureCompiler( cache );
		var blobIds = new Dictionary<byte[], int>( ReferenceEqualityComparer.Instance );
		int Blob( byte[] bytes )
		{
			if ( blobIds.TryGetValue( bytes, out var id ) ) return id;
			id = blobs.Count; blobs.Add( bytes ); blobIds.Add( bytes, id );
			return id;
		}
		int TextureIndex( byte[] resource )
		{
			if ( textureBlobs.TryGetValue( resource, out var index ) ) return index;
			index = textureTable.Count;
			textureTable.Add( Blob( resource ) ); textureBlobs.Add( resource, index );
			return index;
		}

		Warm( document, rows, skeleton, cache );
		foreach ( var row in rows )
		{
			// Warm made every unit of these rows and counted them; read them back without counting again.
			var materialIndex = document.GetMaterialForVariant( row.Mesh, row.Primitive, null );
			var material = materialIndex is int index ? document.ReadMaterial( index ) : null;
			var plan = ProjectedPlan( document, row.Mesh, row.Primitive, materialIndex, material, cache, out var primitiveKey, count: false );
			var routeKey = RouteKey( plan );
			var baked = cache.Rows[BakeKey( row, primitiveKey + "|" + routeKey, skeleton )];
			var phase = System.Diagnostics.Stopwatch.StartNew();

			int MaterialId( GltfMaterial selected, int? selectedIndex, GltfGpuPrimitivePlan selectedPlan )
			{
				var recipe = Recipe( document, selected, selectedIndex, selectedPlan, prepared, compileTexture );
				var packMaterial = new GltfNativePackMaterial( recipe.Shader, recipe.TemplateFeatures, recipe.Features,
					recipe.Vectors.Select( x => new KeyValuePair<string, float[]>( x.Key, [x.Value.x, x.Value.y, x.Value.z, x.Value.w] ) ).ToArray(), recipe.Scalars,
					recipe.Textures.Select( x => new GltfNativePackTexture( x.Parameter, TextureIndex( x.Resource ), GltfNativeSampler.From( x.Sampler ), x.SamplerParameter ) ).ToArray() );
				var materialKey = JsonSerializer.Serialize( packMaterial, GltfNativePack.Json );
				if ( !materialIds.TryGetValue( materialKey, out var id ) ) { id = materials.Count; materials.Add( packMaterial ); materialIds.Add( materialKey, id ); }
				return id;
			}
			var materialId = MaterialId( material, materialIndex, plan );
			cache.Timings.Materials += phase.Elapsed.TotalMilliseconds;
			// A variant swaps materials only: it must read the vertices this mesh stores.
			var variantMaterials = new int[variantNames.Count];
			for ( var variant = 0; variant < variantNames.Count; variant++ )
			{
				var variantIndex = document.GetMaterialForVariant( row.Mesh, row.Primitive, variant );
				if ( variantIndex == materialIndex ) { variantMaterials[variant] = materialId; continue; }
				var variantMaterial = variantIndex is int vi ? document.ReadMaterial( vi ) : null;
				var variantPlan = ProjectedPlan( document, row.Mesh, row.Primitive, variantIndex, variantMaterial, cache, out var variantPrimitiveKey );
				Tangent( variantPlan, variantPrimitiveKey, cache, out var variantRouteKey );
				if ( variantPrimitiveKey != primitiveKey || variantRouteKey != routeKey )
					throw new GltfNativeUnsupportedException( $"Variant '{variantNames[variant]}' of mesh {row.Mesh} primitive {row.Primitive} needs other vertex data (UV sets or normal map tangents) than its default material; native material groups swap materials only." );
				variantMaterials[variant] = MaterialId( variantMaterial, variantIndex, variantPlan );
			}

			// Rows with the same bake share their stored bytes.
			var vertexBlob = Blob( baked.Vertices );
			var indexBlob = Blob( baked.Indices );
			meshes.Add( new GltfNativePackMesh
			{
				Name = $"gltf_node_{row.Node}_mesh_{row.Mesh}_primitive_{row.Primitive}" + (row.InstanceIndex is int instance ? $"_instance_{instance}" : ""),
				Node = row.Node,
				Mesh = row.Mesh,
				Primitive = row.Primitive,
				Material = materialId,
				Vertices = new GltfNativeBlob( vertexBlob, 0 ),
				Stride = baked.Stride,
				VertexCount = baked.VertexCount,
				Fields = baked.Fields,
				Indices = new GltfNativeBlob( indexBlob, 0 ),
				IndexCount = baked.IndexCount,
				Mode = baked.Mode,
				Min = baked.Min,
				Max = baked.Max,
				LodMask = row.LodMask,
				Morphs = baked.Morphs.Select( x => new GltfNativePackMorph( x.Name, new GltfNativeBlob( Blob( x.Deltas ), 0 ), x.Count ) ).ToArray(),
				VariantMaterials = variantMaterials
			} );
		}
		var sampled = skeleton?.SampleAnimations( document, Blob ) ?? [];
		GltfNativePackGeometry[] geometries = [];
		GltfNativePackNode[] sceneNodes = [];
		if ( placements is not null )
		{
			(meshes, geometries, sceneNodes) = AssembleScene( rows, meshes, placements, chains, variantNames.Count );
			lodDistances = [0];
		}
		var groups = variantNames.Count == 0 || placements is not null ? [] : MaterialGroups( meshes, variantNames.Count );

		// Blob indices become byte ranges now that the order is final.
		var compressClock = System.Diagnostics.Stopwatch.StartNew();
		var (stored, rawLengths) = compress ? GltfNativePack.Compress( blobs ) : (blobs.ToArray(), new int[blobs.Count]);
		cache.Timings.Compress = compressClock.Elapsed.TotalMilliseconds;
		var layout = GltfNativePack.Layout( stored, rawLengths );
		var header = new GltfNativePackHeader
		{
			Version = GltfNativePack.CurrentVersion,
			Textures = textureTable.Select( x => layout[x] ).ToArray(),
			Materials = materials.ToArray(),
			Meshes = meshes.Select( x => x with
			{
				Vertices = layout[(int)x.Vertices.Offset],
				Indices = layout[(int)x.Indices.Offset],
				Morphs = x.Morphs.Select( m => m with { Deltas = layout[(int)m.Deltas.Offset] } ).ToArray()
			} ).ToArray(),
			LodDistances = lodDistances,
			Warnings = warnings.ToArray(),
			Bones = skeleton?.Bones ?? [],
			Variants = variantNames.ToArray(),
			MaterialGroups = groups,
			Geometries = geometries,
			Nodes = sceneNodes,
			Animations = sampled.Select( x => x with { Frames = layout[(int)x.Frames.Offset] } ).ToArray()
		};
		return (header, stored.ToList());
	}

	/// <summary>
	/// Store every row relative to the node that places it. A placement is the engine-space transform of that node
	/// (its instance transform included); a LOD level row is stored relative to its chain's base node, so a chain is
	/// one geometry. A node whose transform has shear or a mirror cannot be a native transform: its rows keep the full
	/// world and it is placed at the origin.
	/// </summary>
	private static List<Row> PlaceRows( GltfDocument document, List<Row> rows, out Dictionary<(int Node, int? Instance), Transform> placements )
	{
		var rest = document.EvaluatePose();
		var worlds = new Dictionary<(int Node, int? Instance), NMatrix>();
		placements = new Dictionary<(int Node, int? Instance), Transform>();
		var result = new List<Row>( rows.Count );
		foreach ( var row in rows )
		{
			var key = (row.Placement, row.Chain >= 0 ? null : row.InstanceIndex);
			if ( !worlds.TryGetValue( key, out var placementWorld ) )
			{
				placementWorld = row.Chain >= 0 ? rest.GetWorldTransform( row.Placement ) : row.World;
				if ( TryTransform( GltfNativeSkeleton.Engine( placementWorld ), out var transform ) ) placements[key] = transform;
				else { placementWorld = NMatrix.Identity; placements[key] = global::Transform.Zero; }
				worlds[key] = placementWorld;
			}
			NMatrix.Invert( placementWorld, out var inverse );
			var relative = row.World * inverse;
			result.Add( row with { Matrix = relative * GltfGpuRig.Basis, Reversed = relative.GetDeterminant() < 0 } );
		}
		return result;
	}

	/// <summary>A native transform for an engine-space matrix, when it has no shear or mirror.</summary>
	private static bool TryTransform( NMatrix matrix, out Transform transform )
	{
		transform = default;
		if ( matrix.GetDeterminant() <= 0 || !NMatrix.Decompose( matrix, out var scale, out var rotation, out var position ) ) return false;
		var rebuilt = NMatrix.CreateScale( scale ) * NMatrix.CreateFromQuaternion( rotation ) * NMatrix.CreateTranslation( position );
		for ( var row = 0; row < 3; row++ )
			for ( var column = 0; column < 3; column++ )
				if ( MathF.Abs( rebuilt[row, column] - matrix[row, column] ) > 1e-4f * MathF.Max( 1, MathF.Abs( matrix[row, column] ) ) ) return false;
		transform = new Transform( position, rotation, scale );
		return true;
	}

	/// <summary>
	/// Group the rows of each placement into geometries. Placements with the same stored meshes share a geometry, and
	/// their different materials (black and white pieces) become material groups of it. A LOD chain's geometry gets
	/// native LOD switch distances from its own thresholds and its own size.
	/// </summary>
	private static (List<GltfNativePackMesh> Meshes, GltfNativePackGeometry[] Geometries, GltfNativePackNode[] Nodes) AssembleScene(
		List<Row> rows, List<GltfNativePackMesh> meshes, Dictionary<(int Node, int? Instance), Transform> placements, List<Chain> chains, int variants )
	{
		var outMeshes = new List<GltfNativePackMesh>();
		var geometries = new List<(string Key, List<int> Meshes, List<int[]> Tuples, float[] Distances)>();
		var nodes = new List<GltfNativePackNode>();
		var byPlacement = rows.Select( ( row, index ) => (Row: row, Mesh: meshes[index]) )
			.GroupBy( x => (x.Row.Placement, x.Row.Chain >= 0 ? null : x.Row.InstanceIndex) );
		foreach ( var placement in byPlacement )
		{
			var entries = placement.ToList();
			// The geometry is the stored meshes and their LOD levels; materials do not change it.
			var key = string.Join( ";", entries.Select( x => $"{x.Mesh.Vertices.Offset}/{x.Mesh.Indices.Offset}/{x.Mesh.LodMask}/{x.Mesh.Mode}" ) );
			var tuples = new List<int[]> { entries.Select( x => x.Mesh.Material ).ToArray() };
			for ( var variant = 0; variant < variants; variant++ ) tuples.Add( entries.Select( x => x.Mesh.VariantMaterials[variant] ).ToArray() );
			var geometry = geometries.FindIndex( x => x.Key == key && tuples.All( t => CanGroup( x.Tuples[0], t ) ) );
			if ( geometry < 0 )
			{
				var first = outMeshes.Count;
				outMeshes.AddRange( entries.Select( x => x.Mesh ) );
				var chain = entries[0].Row.Chain;
				float[] distances = [0];
				if ( chain >= 0 )
				{
					var level0 = entries.Where( x => x.Row.Level == 0 ).Select( x => x.Mesh ).ToList();
					var radius = level0.Count == 0 ? 1f : BBox.FromBoxes( level0.Select( m => new BBox( new Vector3( m.Min[0], m.Min[1], m.Min[2] ), new Vector3( m.Max[0], m.Max[1], m.Max[2] ) ) ) ).Size.Length * 0.5f;
					var thresholds = chains[chain].Thresholds;
					var levels = Math.Min( 8, entries.Max( x => x.Row.Level ) + 1 );
					distances = new float[Math.Min( 8, levels + (thresholds[^1] > 0 ? 1 : 0) )];
					for ( var level = 1; level < distances.Length; level++ ) distances[level] = SwitchDistance( radius, thresholds[level - 1] );
				}
				geometries.Add( (key, Enumerable.Range( first, entries.Count ).ToList(), [tuples[0]], distances) );
				geometry = geometries.Count - 1;
			}
			var known = geometries[geometry].Tuples;
			int Group( int[] tuple )
			{
				var index = known.FindIndex( x => x.SequenceEqual( tuple ) );
				if ( index < 0 ) { known.Add( tuple ); index = known.Count - 1; }
				return index;
			}
			var (node, _) = placement.Key;
			var transform = placements[placement.Key];
			nodes.Add( new GltfNativePackNode( node, geometry, [transform.Position.x, transform.Position.y, transform.Position.z],
				[transform.Rotation.x, transform.Rotation.y, transform.Rotation.z, transform.Rotation.w], [transform.Scale.x, transform.Scale.y, transform.Scale.z],
				Group( tuples[0] ), tuples.Skip( 1 ).Select( Group ).ToArray() ) );
		}
		var packGeometries = geometries.Select( ( g, index ) => new GltfNativePackGeometry( $"geometry_{index}", g.Meshes.ToArray(),
			g.Tuples.Select( ( tuple, group ) => new GltfNativePackMaterialGroup( GeometryGroupName( group ), SlotMaterials( g.Tuples[0], tuple ) ) ).ToArray(), g.Distances ) ).ToArray();
		return (outMeshes, packGeometries, nodes.ToArray());
	}

	/// <summary>Native material group name of a geometry's material assignment. Group 0 is the stored materials.</summary>
	internal static string GeometryGroupName( int group ) => group == 0 ? "default" : $"gltf_materials_{group}";

	/// <summary>
	/// A native material group lists one material per distinct material of the default group, in first-use order. A
	/// tuple can be a group only when meshes that share a default material also share their material in it.
	/// </summary>
	private static bool CanGroup( int[] defaults, int[] tuple )
	{
		var map = new Dictionary<int, int>();
		for ( var i = 0; i < defaults.Length; i++ )
		{
			if ( map.TryGetValue( defaults[i], out var existing ) && existing != tuple[i] ) return false;
			map[defaults[i]] = tuple[i];
		}
		return true;
	}

	private static int[] SlotMaterials( int[] defaults, int[] tuple )
	{
		var slots = defaults.Distinct().ToList();
		return slots.Select( slot => tuple[Array.IndexOf( defaults, slot )] ).ToArray();
	}

	/// <summary>
	/// Native material groups: "default" lists the model's materials in the order meshes first use them; each variant
	/// lists its replacement for each of them. A default material that two variants' meshes replace differently cannot
	/// be one group slot.
	/// </summary>
	internal static GltfNativePackMaterialGroup[] MaterialGroups( IReadOnlyList<GltfNativePackMesh> meshes, int variants )
	{
		var defaults = meshes.Select( x => x.Material ).Distinct().ToList();
		var groups = new List<GltfNativePackMaterialGroup> { new( "default", defaults.ToArray() ) };
		for ( var variant = 0; variant < variants; variant++ )
		{
			var replacements = new int[defaults.Count];
			for ( var slot = 0; slot < defaults.Count; slot++ )
			{
				var choices = meshes.Where( x => x.Material == defaults[slot] ).Select( x => x.VariantMaterials[variant] ).Distinct().ToArray();
				if ( choices.Length != 1 ) throw new GltfNativeUnsupportedException( $"Variant {variant} replaces one default material with {choices.Length} different materials; a native material group has one replacement per material." );
				replacements[slot] = choices[0];
			}
			groups.Add( new( GroupName( variant ), replacements ) );
		}
		return groups.ToArray();
	}

	/// <summary>The native material group of a variant. Groups are named by index because variant names may repeat.</summary>
	internal static string GroupName( int variant ) => $"gltf_variant_{variant}";

	/// <summary>Compile images through the cache: the same image bytes, transfer and mip demand compile once.</summary>
	private static GltfTextureCompile TextureCompiler( GltfNativeCompileCache cache ) => ( image, srgb, mipmaps, create ) =>
	{
		var key = Convert.ToHexStringLower( SHA256.HashData( image.Bytes ) ) + $"|{image.MimeType}|{srgb}|{mipmaps}";
		lock ( cache.Textures )
			if ( cache.Textures.TryGetValue( key, out var stored ) ) { cache.Reused.Textures++; return stored; }
		var result = create();
		lock ( cache.Textures ) { cache.Textures[key] = result; cache.Compiled.Textures++; }
		return result;
	};

	/// <summary>
	/// Do the expensive part of <see cref="Compile"/> on any thread: decode, generated tangents, vertex bakes and
	/// texture transcodes, into the cache. <see cref="Compile"/> on the main thread then only reads the cache and makes
	/// the material recipes. The document and the cache must not be used by another thread meanwhile.
	/// </summary>
	internal static void Prepare( GltfDocument document, GltfNativeCompileCache cache, int? sceneIndex = null )
	{
		ArgumentNullException.ThrowIfNull( document ); ArgumentNullException.ThrowIfNull( cache );
		cache.ResetCounters();
		var root = document.Root;
		if ( root.TryGetProperty( "skins", out var skins ) && skins.GetArrayLength() != 0 || root.TryGetProperty( "animations", out var animations ) && animations.GetArrayLength() != 0 )
			return; // The skeleton needs the main-thread compile; the previews have no skins or clips.
		var (rows, _, _, _) = CollectRows( document, sceneIndex, [] );
		rows = PlaceRows( document, rows, out _ );
		Warm( document, rows, null, cache );
		var compileTexture = TextureCompiler( cache );
		var clock = System.Diagnostics.Stopwatch.StartNew();
		foreach ( var materialIndex in rows.Select( x => document.GetMaterialForVariant( x.Mesh, x.Primitive, null ) ).Distinct() )
		{
			var material = materialIndex is int index ? document.ReadMaterial( index ) : null;
			if ( material?.Unlit == true ) GltfUnlitSourceMaterialAdapter.Prepare( document, material, materialIndex, document.ReadImage, compileTexture );
			else GltfStockMaterialAdapter.PrepareSource( document, material, document.ReadImage, compileTexture );
		}
		cache.Timings.Materials += clock.Elapsed.TotalMilliseconds;
	}

	/// <summary>Compile and append the native chunk to a compiled GLB.</summary>
	internal static byte[] AppendTo( byte[] compiledGlb, GltfDocument document, int? sceneIndex = null, GltfNativeCompileCache cache = null )
	{
		var (header, blobs) = Compile( document, sceneIndex, cache );
		return GltfNativePack.Append( compiledGlb, header, blobs );
	}

	/// <summary>
	/// Every drawn row of the scene in model space, with its native LOD mask. MSFT_lod chains become native LOD
	/// levels; their screen coverage thresholds become switch distances.
	/// </summary>
	private static (List<Row> Rows, float[] LodDistances, List<int> Order, List<Chain> Chains) CollectRows( GltfDocument document, int? sceneIndex, List<string> warnings )
	{
		var root = document.Root;
		var scene = sceneIndex ?? document.GetDefaultScene() ?? throw new GltfNativeUnsupportedException( "The asset has no default scene to compile." );
		var rest = document.EvaluatePose();
		var visible = document.EvaluateNodeVisibility();
		var lods = document.ReadNodeLods();
		var levelNodes = lods.SelectMany( x => x.Levels ).ToHashSet();
		var lodOfNode = new Dictionary<int, (int Chain, int Level)>();
		var selected = new List<int>();
		var pending = new Stack<int>();
		var seen = new HashSet<int>();
		var order = new List<int>();
		if ( root.GetProperty( "scenes" )[scene].TryGetProperty( "nodes", out var roots ) )
			foreach ( var node in roots.EnumerateArray().Reverse() ) pending.Push( node.GetInt32() );
		while ( pending.Count != 0 )
		{
			var index = pending.Pop();
			if ( !seen.Add( index ) ) throw new GltfException( "Selected node is repeated." );
			order.Add( index );
			var node = root.GetProperty( "nodes" )[index];
			if ( node.TryGetProperty( "children", out var children ) ) foreach ( var child in children.EnumerateArray().Reverse() ) pending.Push( child.GetInt32() );
			if ( node.TryGetProperty( "mesh", out _ ) && !levelNodes.Contains( index ) ) selected.Add( index );
		}

		// Native LOD switches the whole model by distance. The first chain sets the distances; a chain with other
		// thresholds switches at the first chain's distances.
		float[] distances = [0];
		var chains = lods.Where( x => seen.Contains( x.Node ) ).ToList();
		for ( var chain = 0; chain < chains.Count; chain++ )
		{
			var nodes = chains[chain].Levels.Prepend( chains[chain].Node ).ToArray();
			if ( nodes.Length > 8 ) { warnings.Add( $"MSFT_lod chain of node {chains[chain].Node} has {nodes.Length} levels; native models keep the first 8." ); nodes = nodes[..8]; }
			for ( var level = 0; level < nodes.Length; level++ )
			{
				lodOfNode[nodes[level]] = (chain, level);
				if ( level > 0 && root.GetProperty( "nodes" )[nodes[level]].TryGetProperty( "mesh", out _ ) ) selected.Add( nodes[level] );
			}
		}

		var rows = new List<Row>();
		foreach ( var nodeIndex in selected )
		{
			if ( (uint)nodeIndex < (uint)visible.Length && !visible[nodeIndex] ) continue;
			var node = root.GetProperty( "nodes" )[nodeIndex];
			var instancing = GltfGpuInstancingPlan.Create( document, nodeIndex );
			var world = rest.GetWorldTransform( nodeIndex );
			var inChain = lodOfNode.TryGetValue( nodeIndex, out var lod );
			var lodMask = inChain ? 1 << lod.Level : 0xFF;
			var primitives = root.GetProperty( "meshes" )[instancing.Mesh].GetProperty( "primitives" );
			for ( var primitive = 0; primitive < primitives.GetArrayLength(); primitive++ )
			{
				var json = primitives[primitive];
				if ( !json.GetProperty( "attributes" ).TryGetProperty( "POSITION", out _ ) ) continue;
				if ( json.TryGetProperty( "targets", out var targets ) && targets.GetArrayLength() != 0 && rest.GetMorphWeights( nodeIndex ).Any( x => x != 0 ) )
					throw new GltfNativeUnsupportedException( $"Node {nodeIndex} has non-zero default morph weights; native morphs start at zero." );
				int? skin = node.TryGetProperty( "skin", out var skinValue ) ? skinValue.GetInt32() : null;
				foreach ( var instance in instancing.Instances )
				{
					var local = instance.LocalTransform;
					var matrix = local * world * GltfGpuRig.Basis;
					var reversed = (local * world).GetDeterminant() < 0;
					rows.Add( new Row( nodeIndex, instance.InstanceIndex, instancing.Mesh, primitive, matrix, reversed, lodMask, skin,
						local * world, inChain ? lod.Chain : -1, inChain ? lod.Level : 0, inChain ? chains[lod.Chain].Node : nodeIndex, GltfNativeLodSettings.Read( node ) ) );
				}
			}
		}

		if ( chains.Count != 0 )
		{
			var first = chains[0];
			var levels = Math.Min( 8, first.Levels.Count + 1 );
			var thresholds = first.ScreenCoverage.Count == first.Levels.Count + 1 ? first.ScreenCoverage.ToArray() : GltfLodSelector.DefaultThresholds( first.Levels.Count + 1 );
			if ( chains.Skip( 1 ).Any( x => !x.ScreenCoverage.SequenceEqual( first.ScreenCoverage ) ) )
				warnings.Add( "MSFT_lod chains have different screen coverage thresholds; native LOD uses the first chain's switch distances for all." );
			var chainRows = rows.Where( x => lodOfNode.TryGetValue( x.Node, out var l ) && l.Chain == 0 && l.Level == 0 ).ToList();
			var radius = chainRows.Count == 0 ? 1f : RowBounds( document, chainRows ).Size.Length * 0.5f;
			distances = new float[Math.Min( 8, levels + (thresholds[^1] > 0 ? 1 : 0) )];
			// Level i shows while coverage is at least thresholds[i]; level i + 1 starts where coverage drops below it.
			for ( var level = 1; level < distances.Length; level++ )
				distances[level] = SwitchDistance( radius, thresholds[level - 1] );
		}
		var chainInfo = chains.Select( x => new Chain( x.Node, x.ScreenCoverage.Count == x.Levels.Count + 1 ? x.ScreenCoverage.ToArray() : GltfLodSelector.DefaultThresholds( x.Levels.Count + 1 ) ) ).ToList();
		return (rows, distances, order, chainInfo);
	}

	/// <summary>The distance at which a sphere of this radius covers the given fraction of a 60 degree vertical view.</summary>
	internal static float SwitchDistance( float radius, float coverage )
	{
		const float verticalFov = 60;
		if ( coverage <= 0 ) return float.MaxValue;
		var tan = MathF.Tan( verticalFov * MathF.PI / 360f );
		return MathF.Max( radius, radius / (coverage * tan) );
	}

	private static BBox RowBounds( GltfDocument document, List<Row> rows )
	{
		var boxes = new List<BBox>();
		foreach ( var row in rows )
		{
			var json = document.Root.GetProperty( "meshes" )[row.Mesh].GetProperty( "primitives" )[row.Primitive].GetProperty( "attributes" ).GetProperty( "POSITION" ).GetInt32();
			var accessor = document.Root.GetProperty( "accessors" )[json];
			if ( !accessor.TryGetProperty( "min", out var min ) || !accessor.TryGetProperty( "max", out var max ) ) continue;
			var a = new System.Numerics.Vector3( min[0].GetSingle(), min[1].GetSingle(), min[2].GetSingle() );
			var b = new System.Numerics.Vector3( max[0].GetSingle(), max[1].GetSingle(), max[2].GetSingle() );
			var corners = new[] { a, b, new( a.X, a.Y, b.Z ), new( a.X, b.Y, a.Z ), new( b.X, a.Y, a.Z ), new( a.X, b.Y, b.Z ), new( b.X, a.Y, b.Z ), new( b.X, b.Y, a.Z ) };
			boxes.Add( BBox.FromPoints( corners.Select( x => (Vector3)System.Numerics.Vector3.Transform( x, row.Matrix ) ) ) );
		}
		return boxes.Count == 0 ? new BBox( Vector3.Zero, Vector3.One ) : BBox.FromBoxes( boxes );
	}

	/// <summary>The decoded primitive, projected for its material: the same route the source loader's projected path uses.</summary>
	private static GltfGpuPrimitivePlan ProjectedPlan( GltfDocument document, int mesh, int primitive, int? materialIndex, GltfMaterial material, GltfNativeCompileCache cache, out string primitiveKey, bool count = true )
	{
		primitiveKey = PrimitiveKey( document, mesh, primitive );
		if ( !cache.Primitives.TryGetValue( primitiveKey, out var raw ) )
		{
			raw = GltfGpuPrimitivePlan.Create( document, mesh, primitive );
			cache.Primitives[primitiveKey] = raw; cache.Compiled.Primitives++;
		}
		else { if ( count ) cache.Reused.Primitives++; raw = raw.WithOwner( document ); }
		// Generated tangents need one vertex per triangle corner, as in the source loader.
		var authoredFrame = raw.Upload.Attributes.ContainsKey( "TANGENT" ) && raw.Upload.Attributes.ContainsKey( "NORMAL" );
		if ( raw.Mode == 4 && !authoredFrame && document.MaterialMayUseNormalTexture( materialIndex ) )
		{
			var cornerKey = primitiveKey + "|corners";
			if ( !cache.Primitives.TryGetValue( cornerKey, out var corners ) )
			{
				var topology = GltfGpuGeneratedTangentRoute.ExpandTopology( raw );
				corners = raw.WithCornerTopology( topology.SourceVertices, topology.Indices );
				cache.Primitives[cornerKey] = corners;
			}
			raw = corners.WithOwner( document ); primitiveKey = cornerKey;
		}
		var uvCount = material?.DiffuseTransmission is not null ? 7 : 5;
		int[] uvSemantics = uvCount == 7 ? [0, 1, 4, 5, 6, 7, 8] : [0, 1, 4, 5, 6];
		IEnumerable<GltfMaterialTextureRole> roles = material?.Unlit == true ? [GltfMaterialTextureRole.BaseColor] : null;
		return raw.ProjectMaterial( materialIndex, uvCount, roles, uvSemantics );
	}

	/// <summary>The output layout and the UV sources of each slot select the route; the normal map's UV set and transform select its tangents.</summary>
	private static string RouteKey( GltfGpuPrimitivePlan plan )
	{
		var slots = string.Join( ",", plan.UvProjection.Sources.Select( x => x?.TexCoord ?? -1 ) );
		var normal = plan.MaterialUv.Bindings.TryGetValue( GltfMaterialTextureRole.Normal, out var binding )
			? $"{binding.TexCoord}:{binding.Transform.RowU}:{binding.Transform.RowV}" : "none";
		return $"{plan.VertexStride}|{slots}|{normal}";
	}

	private static bool HasTangentRoute( GltfGpuPrimitivePlan plan ) => !(plan.Mode is 0 or 1 && !plan.Upload.Attributes.ContainsKey( "NORMAL" ));

	private static GltfGpuGeneratedTangentRoute ComputeTangent( GltfGpuPrimitivePlan plan )
	{
		try { return GltfGpuGeneratedTangentRoute.Prepare( plan, hasSkin: false ); }
		catch ( GltfException e ) when ( plan.MorphTargetCount != 0 ) { throw new GltfNativeUnsupportedException( e.Message ); }
	}

	private static GltfGpuGeneratedTangentRoute Tangent( GltfGpuPrimitivePlan plan, string primitiveKey, GltfNativeCompileCache cache, out string routeKey )
	{
		routeKey = RouteKey( plan );
		if ( !HasTangentRoute( plan ) ) return null;
		var key = primitiveKey + "|" + routeKey;
		if ( cache.Tangents.TryGetValue( key, out var stored ) ) { cache.Reused.Tangents++; return stored; }
		var tangent = ComputeTangent( plan );
		cache.Tangents[key] = tangent; cache.Compiled.Tangents++;
		return tangent;
	}

	private static string BakeKey( Row row, string routeKey, GltfNativeSkeleton skeleton )
	{
		var m = row.Matrix;
		var bone = skeleton is null ? "" : row.Skin is int skin ? $"skin:{skin}" : $"bone:{skeleton.BoneOf( row.Node )}";
		var key = $"{routeKey}|{m.M11},{m.M12},{m.M13},{m.M21},{m.M22},{m.M23},{m.M31},{m.M32},{m.M33},{m.M41},{m.M42},{m.M43}|{row.Reversed}|{bone}";
		return row.Simplify is null ? key : key + "|" + row.Simplify.Key;
	}

	/// <summary>A generated level of detail: the base bake's vertices with reduced indices. It shares the base's vertex blob.</summary>
	private static BakedRow SimplifyBake( BakedRow source, GltfNativeLodSettings settings )
	{
		var indices = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, int>( source.Indices ).ToArray();
		var reduced = GltfNativeSimplify.Indices( source.Vertices, source.Stride, source.Fields, indices, source.Mode, settings );
		return source with { Indices = MemoryMarshal.AsBytes( reduced.AsSpan() ).ToArray(), IndexCount = reduced.Length };
	}

	/// <summary>The document reads a bake needs, done on the calling thread so <see cref="ComputeBake"/> touches no document.</summary>
	private static (Func<int, IEnumerable<(ushort Bone, float Weight)>> Influences, BakedMorph[] Morphs) BakeInputs( GltfDocument document, GltfGpuPrimitivePlan plan, Row row, GltfNativeSkeleton skeleton ) =>
		(skeleton is null ? null : Influences( document, row, skeleton ), Morphs( document, plan, row ));

	private static BakedRow ComputeBake( GltfGpuPrimitivePlan plan, GltfGpuGeneratedTangentRoute tangent, Row row, Func<int, IEnumerable<(ushort Bone, float Weight)>> influences, BakedMorph[] morphs )
	{
		var vertices = GltfNativeVertexBake.Bake( GltfNativeVertexRoute.For( plan, tangent ), row.Matrix, row.Reversed, out var min, out var max );
		GltfNativeVertexPacking.Skin skinData = null;
		if ( influences is not null )
		{
			var (quantized, truncated) = GltfNativeVertexPacking.Quantize( plan.SourceVertices, influences );
			skinData = quantized;
			if ( truncated ) Log.Warning( $"glTF mesh {row.Mesh} primitive {row.Primitive}: vertices with more than four joint influences keep the four strongest." );
		}
		var (compact, fields, stride) = GltfNativeVertexPacking.Pack( vertices, plan, skinData );
		// Morph deltas address stored vertices, so morphed meshes keep every vertex.
		var (welded, remap) = morphs.Length == 0 ? GltfNativeVertexPacking.Weld( compact, stride ) : (compact, Enumerable.Range( 0, compact.Length / stride ).ToArray());
		var indices = new int[plan.Indices.Length];
		for ( var i = 0; i < indices.Length; i++ ) indices[i] = remap[checked((int)plan.Indices[i])];
		if ( row.Reversed && plan.Mode == 4 )
			for ( var i = 0; i + 2 < indices.Length; i += 3 ) (indices[i + 1], indices[i + 2]) = (indices[i + 2], indices[i + 1]);
		return new BakedRow( welded, MemoryMarshal.AsBytes( indices.AsSpan() ).ToArray(), stride, welded.Length / stride, indices.Length,
			fields, plan.Mode, [min.X, min.Y, min.Z], [max.X, max.Y, max.Z], morphs );
	}

	/// <summary>
	/// Compute every missing tangent route and bake on all cores before the serial loop, which then finds them in the
	/// cache. Plans and document reads stay on the calling thread; the parallel work reads only decoded arrays.
	/// </summary>
	private static void Warm( GltfDocument document, List<Row> rows, GltfNativeSkeleton skeleton, GltfNativeCompileCache cache )
	{
		var clock = System.Diagnostics.Stopwatch.StartNew();
		var work = new List<(Row Row, GltfGpuPrimitivePlan Plan, string PrimitiveKey, string RouteKey)>();
		foreach ( var row in rows )
		{
			var materialIndex = document.GetMaterialForVariant( row.Mesh, row.Primitive, null );
			var material = materialIndex is int index ? document.ReadMaterial( index ) : null;
			var plan = ProjectedPlan( document, row.Mesh, row.Primitive, materialIndex, material, cache, out var primitiveKey );
			work.Add( (row, plan, primitiveKey, RouteKey( plan )) );
		}
		cache.Timings.Plans += clock.Elapsed.TotalMilliseconds; clock.Restart();

		var routed = work.Where( x => HasTangentRoute( x.Plan ) ).Select( x => (Key: x.PrimitiveKey + "|" + x.RouteKey, x.Plan) ).ToArray();
		var tangentJobs = routed.Where( x => !cache.Tangents.ContainsKey( x.Key ) ).DistinctBy( x => x.Key ).ToArray();
		cache.Reused.Tangents += routed.Length - tangentJobs.Length;
		var tangents = new GltfGpuGeneratedTangentRoute[tangentJobs.Length];
		System.Threading.Tasks.Parallel.For( 0, tangentJobs.Length, i => tangents[i] = ComputeTangent( tangentJobs[i].Plan ) );
		for ( var i = 0; i < tangentJobs.Length; i++ ) { cache.Tangents[tangentJobs[i].Key] = tangents[i]; cache.Compiled.Tangents++; }
		cache.Timings.Tangents += clock.Elapsed.TotalMilliseconds; clock.Restart();

		var bakeJobs = new List<(string Key, GltfGpuPrimitivePlan Plan, GltfGpuGeneratedTangentRoute Tangent, Row Row, Func<int, IEnumerable<(ushort, float)>> Influences, BakedMorph[] Morphs)>();
		var simplifyJobs = new List<(string Key, string BaseKey, GltfNativeLodSettings Settings)>();
		var queued = new HashSet<string>( StringComparer.Ordinal );
		foreach ( var (row, plan, primitiveKey, routeKey) in work )
		{
			var key = BakeKey( row, primitiveKey + "|" + routeKey, skeleton );
			if ( cache.Rows.ContainsKey( key ) || !queued.Add( key ) ) { cache.Reused.Rows++; continue; }
			// A generated level of detail reduces the base bake of the same row: queue that bake, then the reduction.
			var baseRow = row with { Simplify = null };
			var baseKey = BakeKey( baseRow, primitiveKey + "|" + routeKey, skeleton );
			if ( row.Simplify is not null ) simplifyJobs.Add( (key, baseKey, row.Simplify) );
			if ( row.Simplify is not null && (cache.Rows.ContainsKey( baseKey ) || !queued.Add( baseKey )) ) continue;
			var tangent = HasTangentRoute( plan ) ? cache.Tangents[primitiveKey + "|" + routeKey] : null;
			var (influences, morphs) = BakeInputs( document, plan, baseRow, skeleton );
			bakeJobs.Add( (baseKey, plan, tangent, baseRow, influences, morphs) );
		}
		var baked = new BakedRow[bakeJobs.Count];
		System.Threading.Tasks.Parallel.For( 0, bakeJobs.Count, i =>
		{
			var job = bakeJobs[i];
			baked[i] = ComputeBake( job.Plan, job.Tangent, job.Row, job.Influences, job.Morphs );
		} );
		for ( var i = 0; i < bakeJobs.Count; i++ ) { cache.Rows[bakeJobs[i].Key] = baked[i]; cache.Compiled.Rows++; }
		var simplified = new BakedRow[simplifyJobs.Count];
		System.Threading.Tasks.Parallel.For( 0, simplifyJobs.Count, i => simplified[i] = SimplifyBake( cache.Rows[simplifyJobs[i].BaseKey], simplifyJobs[i].Settings ) );
		for ( var i = 0; i < simplifyJobs.Count; i++ ) { cache.Rows[simplifyJobs[i].Key] = simplified[i]; cache.Compiled.Rows++; }
		cache.Timings.Bakes += clock.Elapsed.TotalMilliseconds;
	}

	/// <summary>Every (bone, weight) of a source vertex: its JOINTS/WEIGHTS sets for a skinned mesh, its node's bone for a rigid one.</summary>
	private static Func<int, IEnumerable<(ushort Bone, float Weight)>> Influences( GltfDocument document, Row row, GltfNativeSkeleton skeleton )
	{
		if ( row.Skin is not int skin )
		{
			var single = (ushort)skeleton.BoneOf( row.Node );
			return _ => [(single, 1f)];
		}
		var bones = skeleton.SkinBones( skin );
		var geometry = document.ReadPrimitive( row.Mesh, row.Primitive );
		var sets = new List<(float[] Joints, float[] Weights)>();
		for ( var set = 0; geometry.Attributes.TryGetValue( "JOINTS_" + set, out var joints ); set++ )
			sets.Add( (document.ReadFloatAccessor( joints ), document.ReadFloatAccessor( geometry.Attributes["WEIGHTS_" + set] )) );
		if ( sets.Count == 0 ) throw new GltfException( "A skinned primitive requires joint and weight accessors." );
		return vertex => sets.SelectMany( x => Enumerable.Range( 0, 4 ).Select( c =>
		{
			var joint = (int)x.Joints[vertex * 4 + c];
			if ( (uint)joint >= (uint)bones.Length ) throw new GltfException( "Skinning joint index is outside the selected skin." );
			return ((ushort)bones[joint], x.Weights[vertex * 4 + c]);
		} ) );
	}

	/// <summary>
	/// Native morph controllers of a row: a positive and a negative controller per glTF target, since native weights
	/// are not negative. Deltas are transformed like the row's vertices: positions by the row matrix, normals by its
	/// inverse transpose.
	/// </summary>
	private static BakedMorph[] Morphs( GltfDocument document, GltfGpuPrimitivePlan plan, Row row )
	{
		if ( plan.MorphTargetCount == 0 ) return [];
		var targets = document.ReadMorphTargets( row.Mesh, row.Primitive );
		var meshJson = document.Root.GetProperty( "meshes" )[row.Mesh];
		string[] names = meshJson.TryGetProperty( "extras", out var extras ) && extras.TryGetProperty( "targetNames", out var targetNames )
			? targetNames.EnumerateArray().Select( x => x.GetString() ).ToArray() : [];
		var linear = row.Matrix; linear.M41 = 0; linear.M42 = 0; linear.M43 = 0;
		NMatrix.Invert( linear, out var inverse );
		// Base normals are normalized after the transform; remove the uniform part of the scale from their deltas too.
		var normalMatrix = NMatrix.Transpose( inverse ) * MathF.Cbrt( MathF.Abs( linear.GetDeterminant() ) );
		var result = new List<BakedMorph>();
		for ( var target = 0; target < targets.Length; target++ )
		{
			var source = targets[target];
			if ( source.TangentDeltas?.Any( x => x != 0 ) == true ) throw new GltfNativeUnsupportedException( "Native morphs have no tangent deltas." );
			var positive = new List<MorphDelta>();
			for ( var vertex = 0; vertex < plan.SourceVertices.Length; vertex++ )
			{
				var offset = (int)plan.SourceVertices[vertex] * 3;
				var p = source.PositionDeltas is null ? System.Numerics.Vector3.Zero : new System.Numerics.Vector3( source.PositionDeltas[offset], source.PositionDeltas[offset + 1], source.PositionDeltas[offset + 2] );
				var n = source.NormalDeltas is null ? System.Numerics.Vector3.Zero : new System.Numerics.Vector3( source.NormalDeltas[offset], source.NormalDeltas[offset + 1], source.NormalDeltas[offset + 2] );
				if ( p == System.Numerics.Vector3.Zero && n == System.Numerics.Vector3.Zero ) continue;
				positive.Add( new MorphDelta( vertex, System.Numerics.Vector3.TransformNormal( p, linear ), System.Numerics.Vector3.TransformNormal( n, normalMatrix ) ) );
			}
			if ( positive.Count == 0 ) continue;
			var negative = positive.Select( x => new MorphDelta( x.VertexIndex, -x.PositionDelta, -x.NormalDelta ) ).ToArray();
			var name = target < names.Length && !string.IsNullOrWhiteSpace( names[target] ) ? names[target] : $"morph_{target}";
			result.Add( new BakedMorph( name, MemoryMarshal.AsBytes( positive.ToArray().AsSpan() ).ToArray(), positive.Count ) );
			result.Add( new BakedMorph( name + "_negative", MemoryMarshal.AsBytes( negative.AsSpan() ).ToArray(), negative.Length ) );
		}
		return result.ToArray();
	}

	private static GltfNativeMaterialRecipe Recipe( GltfDocument document, GltfMaterial material, int? materialIndex, GltfGpuPrimitivePlan plan, Dictionary<int, object> prepared, GltfTextureCompile compileTexture )
	{
		var (shader, templateFeatures) = GltfNativeMaterialTemplates.Describe( material );
		var template = GltfNativeMaterialTemplates.Get( shader, templateFeatures );
		var key = materialIndex ?? -1;
		GltfNativeMaterialRecipe recipe;
		if ( material?.Unlit == true )
		{
			GltfUnlitMaterialPlan.Create( material, materialIndex ).Bind( document, plan.Upload ).RequireRealizable( GltfUnlitMaterialPlan.Features.All );
			if ( !prepared.TryGetValue( key, out var value ) ) prepared[key] = value = GltfUnlitSourceMaterialAdapter.Prepare( document, material, materialIndex, document.ReadImage, compileTexture );
			recipe = GltfUnlitSourceMaterialAdapter.BuildRecipe( (GltfUnlitSourceMaterialAdapter.Prepared)value, template, plan.MaterialUv, plan.UvProjection );
		}
		else
		{
			GltfStockMaterialAdapter.RequireTemplateContract( template, material );
			if ( !prepared.TryGetValue( key, out var value ) ) prepared[key] = value = GltfStockMaterialAdapter.PrepareSource( document, material, document.ReadImage, compileTexture );
			recipe = GltfStockMaterialAdapter.BuildSourceRecipe( (GltfStockMaterialAdapter.SourcePrepared)value, template, plan.MaterialUv, plan.UvProjection, featureSidedness: true );
		}
		return recipe with { Shader = shader, TemplateFeatures = templateFeatures };
	}

	/// <summary>
	/// The geometry identity of a primitive: its attributes, indices, mode, morph targets and compression, and the
	/// accessors and buffer views they read. The material is not part of it, so meshes that share accessors with
	/// different materials (black and white pieces) share one decode, one bake and one stored vertex buffer.
	/// </summary>
	private static string PrimitiveKey( GltfDocument document, int mesh, int primitive )
	{
		var root = document.Root;
		var json = root.GetProperty( "meshes" )[mesh].GetProperty( "primitives" )[primitive];
		var text = new StringBuilder();
		foreach ( var property in json.EnumerateObject() )
		{
			if ( property.Name is "material" or "extras" ) continue;
			if ( property.Name == "extensions" )
			{
				foreach ( var extension in property.Value.EnumerateObject() )
					if ( extension.Name != "KHR_materials_variants" ) text.Append( extension.Name ).Append( '=' ).Append( extension.Value.GetRawText() ).Append( ';' );
				continue;
			}
			text.Append( property.Name ).Append( '=' ).Append( property.Value.GetRawText() ).Append( ';' );
		}
		var accessors = new List<int>();
		foreach ( var attribute in json.GetProperty( "attributes" ).EnumerateObject() ) accessors.Add( attribute.Value.GetInt32() );
		if ( json.TryGetProperty( "indices", out var indices ) ) accessors.Add( indices.GetInt32() );
		foreach ( var accessor in accessors.Distinct().Order() )
		{
			var a = root.GetProperty( "accessors" )[accessor];
			text.Append( '|' ).Append( a.GetRawText() );
			if ( a.TryGetProperty( "bufferView", out var view ) ) text.Append( '|' ).Append( root.GetProperty( "bufferViews" )[view.GetInt32()].GetRawText() );
		}
		return Convert.ToHexStringLower( SHA256.HashData( Encoding.UTF8.GetBytes( text.ToString() ) ) );
	}
}
