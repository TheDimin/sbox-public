using System;
using System.Text.Json.Nodes;

namespace Editor;

/// <summary>One lower-detail level of a node: the node to show, and the screen coverage down to which it stays visible.</summary>
/// <param name="Node">Index of a node of the same file.</param>
/// <param name="Coverage">Screen coverage (0 to 1) below which the next level takes over. 0 for the last level never culls.</param>
public readonly record struct GltfLodLevel( int Node, float Coverage );

/// <summary>
/// The MSFT_lod levels of a node. The node itself is level 0; <see cref="Levels"/> are the lower levels
/// from highest to lowest detail. glTF keeps the thresholds in extras.MSFT_screencoverage, one per level
/// including level 0.
/// </summary>
public struct GltfLodLevels
{
	public GltfLodLevels() { }

	/// <summary>Screen coverage below which level 0 (the node itself) stops showing.</summary>
	public float BaseCoverage { get; set; } = 0.5f;

	public GltfLodLevel[] Levels { get; set; } = [];

	public readonly bool IsEmpty => Levels.Length == 0;
}

/// <summary>Reads and writes MSFT_lod data of a node. Pure JSON, so it can be tested without an editor.</summary>
public static class GltfLodEditing
{
	public const string Extension = "MSFT_lod";
	const string CoverageKey = "MSFT_screencoverage";

	/// <summary>Read the levels. Missing or mismatched thresholds fall back to <see cref="DefaultCoverage"/>.</summary>
	public static GltfLodLevels Read( JsonObject node )
	{
		var ids = new List<int>();
		if ( node?["extensions"]?[Extension]?["ids"] is JsonArray array )
			foreach ( var item in array )
				if ( item is JsonValue value && value.TryGetValue<int>( out var id ) ) ids.Add( id );
		if ( ids.Count == 0 ) return new GltfLodLevels();

		var coverage = new List<float>();
		if ( node["extras"]?[CoverageKey] is JsonArray thresholds )
			foreach ( var item in thresholds )
				if ( item is JsonValue value && value.TryGetValue<float>( out var number ) ) coverage.Add( number );
		if ( coverage.Count != ids.Count + 1 ) coverage = DefaultCoverage( ids.Count + 1 ).ToList();

		return new GltfLodLevels
		{
			BaseCoverage = coverage[0],
			Levels = ids.Select( ( id, i ) => new GltfLodLevel( id, coverage[i + 1] ) ).ToArray()
		};
	}

	/// <summary>
	/// Write the levels. With no levels the extension and the thresholds are removed, and
	/// containers that become empty go with them. Other keys in extras stay.
	/// </summary>
	public static void Write( JsonObject node, GltfLodLevels levels )
	{
		if ( levels.IsEmpty )
		{
			Remove( node, "extensions", Extension );
			Remove( node, "extras", CoverageKey );
			return;
		}
		if ( node["extensions"] is not JsonObject extensions ) node["extensions"] = extensions = new JsonObject();
		if ( extensions[Extension] is not JsonObject lod ) extensions[Extension] = lod = new JsonObject();
		lod["ids"] = new JsonArray( levels.Levels.Select( x => (JsonNode)JsonValue.Create( x.Node ) ).ToArray() );
		if ( node["extras"] is not JsonObject extras ) node["extras"] = extras = new JsonObject();
		extras[CoverageKey] = new JsonArray( levels.Levels.Select( x => x.Coverage ).Prepend( levels.BaseCoverage ).Select( x => (JsonNode)JsonValue.Create( x ) ).ToArray() );
	}

	static void Remove( JsonObject node, string container, string key )
	{
		if ( node[container] is not JsonObject target ) return;
		target.Remove( key );
		if ( target.Count == 0 ) node.Remove( container );
	}

	/// <summary>Thresholds for <paramref name="count"/> levels including level 0: 0.5, 0.25, ... and 0 for the last, which never culls.</summary>
	public static float[] DefaultCoverage( int count )
	{
		var result = new float[Math.Max( count, 0 )];
		for ( var i = 0; i < result.Length - 1; i++ ) result[i] = MathF.Pow( 0.5f, i + 1 );
		return result;
	}

	/// <summary>Add a lower level at the end. The old last level gets a real threshold; the new one never culls.</summary>
	public static GltfLodLevels Add( GltfLodLevels levels, int node )
	{
		var items = levels.Levels.ToList();
		if ( items.Count == 0 ) return new GltfLodLevels { BaseCoverage = 0.5f, Levels = [new GltfLodLevel( node, 0 )] };
		var previous = items.Count >= 2 ? items[^2].Coverage : levels.BaseCoverage;
		if ( items[^1].Coverage <= 0 ) items[^1] = items[^1] with { Coverage = previous * 0.5f };
		items.Add( new GltfLodLevel( node, 0 ) );
		return levels with { Levels = items.ToArray() };
	}

	/// <summary>Remove a lower level. Removing the last one leaves a level that never culls.</summary>
	public static GltfLodLevels RemoveAt( GltfLodLevels levels, int index )
	{
		var items = levels.Levels.ToList();
		var wasLast = index == items.Count - 1;
		var removed = items[index];
		items.RemoveAt( index );
		if ( wasLast && items.Count > 0 ) items[^1] = items[^1] with { Coverage = removed.Coverage };
		return levels with { Levels = items.ToArray() };
	}

	/// <summary>Swap a level with its neighbour. Nodes move; the thresholds stay in place, since they belong to the position.</summary>
	public static GltfLodLevels Move( GltfLodLevels levels, int index, int delta )
	{
		var to = index + delta;
		if ( index < 0 || to < 0 || index >= levels.Levels.Length || to >= levels.Levels.Length ) return levels;
		var items = levels.Levels.ToArray();
		(items[index], items[to]) = (items[to] with { Coverage = items[index].Coverage }, items[index] with { Coverage = items[to].Coverage });
		return levels with { Levels = items };
	}

	/// <summary>
	/// Set a threshold. Position -1 is level 0. The value stays between its neighbours, so thresholds never increase.
	/// </summary>
	public static GltfLodLevels SetCoverage( GltfLodLevels levels, int position, float value )
	{
		var values = levels.Levels.Select( x => x.Coverage ).Prepend( levels.BaseCoverage ).ToArray();
		var i = position + 1;
		var upper = i == 0 ? 1f : values[i - 1];
		var lower = i == values.Length - 1 ? 0f : values[i + 1];
		values[i] = Math.Clamp( value, lower, upper );
		return new GltfLodLevels { BaseCoverage = values[0], Levels = levels.Levels.Select( ( x, n ) => x with { Coverage = values[n + 1] } ).ToArray() };
	}

	/// <summary>Replace the node of a level, keeping the threshold.</summary>
	public static GltfLodLevels SetNode( GltfLodLevels levels, int index, int node )
	{
		var items = levels.Levels.ToArray();
		items[index] = items[index] with { Node = node };
		return levels with { Levels = items };
	}

	/// <summary>
	/// A new node for a lower level: same mesh, skin and transform as <paramref name="baseNode"/>, so the user only
	/// has to point it at a lower-detail mesh.
	/// </summary>
	public static JsonObject CreateLevelNode( JsonObject baseNode, string baseName, int level )
	{
		var result = new JsonObject { ["name"] = $"{baseName}_LOD{level}" };
		foreach ( var key in new[] { "mesh", "skin", "translation", "rotation", "scale", "matrix", "weights" } )
			if ( baseNode[key] is { } value ) result[key] = value.DeepClone();
		return result;
	}

	/// <summary>
	/// Nodes a level of <paramref name="node"/> can use: not the node itself, not a node that starts a chain,
	/// and not a level of another chain. Nodes already in this chain are included only when <paramref name="includeCurrent"/> is set.
	/// </summary>
	public static IEnumerable<int> EligibleNodes( IReadOnlyList<JsonObject> nodes, int node, bool includeCurrent = false )
	{
		var taken = new HashSet<int>();
		for ( var i = 0; i < nodes.Count; i++ )
		{
			// Levels of the node itself are only free to pick when replacing one of them.
			if ( i == node && includeCurrent ) continue;
			foreach ( var level in Read( nodes[i] ).Levels ) taken.Add( level.Node );
		}
		for ( var i = 0; i < nodes.Count; i++ )
		{
			if ( i == node || taken.Contains( i ) || !Read( nodes[i] ).IsEmpty ) continue;
			yield return i;
		}
	}
}
