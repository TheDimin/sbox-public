using System.Text.Json;

namespace Sandbox.Gltf;

/// <summary>
/// One MSFT_lod chain: a base node and the lower-detail nodes of the same file,
/// ordered from highest to lowest detail. Levels[0] is the first lower LOD.
/// </summary>
public sealed class GltfNodeLod
{
	public const string ExtensionName = "MSFT_lod";
	public int Node { get; init; }
	public IReadOnlyList<int> Levels { get; init; }
	/// <summary>
	/// Screen coverage at which each level starts, one value per level including the
	/// base node (Levels.Count + 1 values, descending). Empty when absent or invalid.
	/// </summary>
	public IReadOnlyList<float> ScreenCoverage { get; init; }
	/// <summary>Why ScreenCoverage was dropped, or null.</summary>
	public string Warning { get; init; }
}

/// <summary>A material with MSFT_lod: lower-detail materials from highest to lowest detail.</summary>
public sealed class GltfMaterialLod
{
	public int Material { get; init; }
	public IReadOnlyList<int> Levels { get; init; }
}

public sealed partial class GltfDocument
{
	/// <summary>
	/// Reads node-level MSFT_lod chains. Returns nothing when the extension is
	/// unsupported, so the base node alone is used (the spec fallback). Chains are
	/// validated: ids are in range, unique, never the base node, never shared by two
	/// chains, and a LOD node cannot start its own chain.
	/// </summary>
	public IReadOnlyList<GltfNodeLod> ReadNodeLods()
	{
		var result = new List<GltfNodeLod>();
		if ( !SupportsExtension( GltfNodeLod.ExtensionName ) ) return result;
		var nodes = RootArray( "nodes" );
		var chains = new Dictionary<int, int[]>();
		for ( var i = 0; i < nodes.Length; i++ )
		{
			var ids = ReadLodIds( SceneObject( nodes[i], "node" ), "node", nodes.Length );
			if ( ids is not null ) chains.Add( i, ids );
		}
		var owner = new Dictionary<int, int>();
		foreach ( var (node, ids) in chains )
		{
			if ( Array.IndexOf( ids, node ) >= 0 ) throw new GltfException( "A node cannot be its own MSFT_lod level." );
			foreach ( var id in ids )
			{
				if ( chains.ContainsKey( id ) ) throw new GltfException( "A MSFT_lod level cannot have its own MSFT_lod chain." );
				if ( !owner.TryAdd( id, node ) ) throw new GltfException( "A node cannot be a MSFT_lod level of two chains." );
			}
		}
		foreach ( var (node, ids) in chains.OrderBy( x => x.Key ) )
		{
			var coverage = ReadScreenCoverage( SceneObject( nodes[node], "node" ), ids.Length + 1, out var warning );
			result.Add( new GltfNodeLod { Node = node, Levels = ids, ScreenCoverage = coverage, Warning = warning } );
		}
		return result;
	}

	/// <summary>Reads material-level MSFT_lod chains with the same validation rules as nodes.</summary>
	public IReadOnlyList<GltfMaterialLod> ReadMaterialLods()
	{
		var result = new List<GltfMaterialLod>();
		if ( !SupportsExtension( GltfNodeLod.ExtensionName ) ) return result;
		var materials = RootArray( "materials" );
		var chains = new Dictionary<int, int[]>();
		for ( var i = 0; i < materials.Length; i++ )
		{
			var ids = ReadLodIds( SceneObject( materials[i], "material" ), "material", materials.Length );
			if ( ids is not null ) chains.Add( i, ids );
		}
		var owner = new Dictionary<int, int>();
		foreach ( var (material, ids) in chains )
		{
			if ( Array.IndexOf( ids, material ) >= 0 ) throw new GltfException( "A material cannot be its own MSFT_lod level." );
			foreach ( var id in ids )
			{
				if ( chains.ContainsKey( id ) ) throw new GltfException( "A MSFT_lod level cannot have its own MSFT_lod chain." );
				// Lower materials may be shared by several chains; only chain recursion is invalid.
				owner.TryAdd( id, material );
			}
		}
		foreach ( var (material, ids) in chains.OrderBy( x => x.Key ) )
			result.Add( new GltfMaterialLod { Material = material, Levels = ids } );
		return result;
	}

	private int[] ReadLodIds( JsonElement element, string kind, int count )
	{
		if ( !element.TryGetProperty( "extensions", out var extensions ) ) return null;
		SceneObject( extensions, kind + " extensions" );
		if ( !extensions.TryGetProperty( GltfNodeLod.ExtensionName, out var lod ) ) return null;
		if ( !ExtensionNames( "extensionsUsed" ).Contains( GltfNodeLod.ExtensionName, StringComparer.Ordinal ) )
			throw new GltfException( "MSFT_lod must be listed in extensionsUsed." );
		SceneObject( lod, "MSFT_lod" );
		if ( !lod.TryGetProperty( "ids", out var ids ) || ids.ValueKind != JsonValueKind.Array || ids.GetArrayLength() == 0 )
			throw new GltfException( "MSFT_lod ids must be a nonempty array." );
		var values = new List<int>();
		foreach ( var id in ids.EnumerateArray() )
		{
			var index = SceneIndex( id, "MSFT_lod id" );
			if ( index >= count ) throw new GltfException( $"MSFT_lod id {index} is out of range." );
			if ( values.Contains( index ) ) throw new GltfException( "MSFT_lod ids must be unique." );
			values.Add( index );
		}
		return values.ToArray();
	}

	private static float[] ReadScreenCoverage( JsonElement node, int expected, out string warning )
	{
		warning = null;
		if ( !node.TryGetProperty( "extras", out var extras ) || extras.ValueKind != JsonValueKind.Object ||
			!extras.TryGetProperty( "MSFT_screencoverage", out var value ) ) return [];
		var result = new List<float>();
		if ( value.ValueKind == JsonValueKind.Array )
			foreach ( var item in value.EnumerateArray() )
			{
				if ( item.ValueKind != JsonValueKind.Number || !item.TryGetSingle( out var number ) || float.IsNaN( number ) || number < 0 || number > 1 )
				{ warning = "MSFT_screencoverage must hold numbers from 0 to 1."; return []; }
				result.Add( number );
			}
		else { warning = "MSFT_screencoverage must be an array."; return []; }
		if ( result.Count != expected ) { warning = $"MSFT_screencoverage has {result.Count} values; {expected} expected."; return []; }
		for ( var i = 1; i < result.Count; i++ )
			if ( result[i] > result[i - 1] ) { warning = "MSFT_screencoverage must not increase."; return []; }
		return result.ToArray();
	}
}
