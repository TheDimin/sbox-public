using System.Text.Json;

namespace Sandbox.Gltf;

public sealed partial class GltfDocument
{
	/// <summary>
	/// Evaluates selected KHR_node_visibility values for every node. Overrides
	/// are local mutable values; a hidden ancestor still hides its descendants.
	/// They do not alter transforms, skinning, animation data, or this document.
	/// Unsupported optional visibility falls back to all nodes being visible.
	/// </summary>
	public bool[] EvaluateNodeVisibility( IReadOnlyDictionary<int, bool> overrides = null )
	{
		var nodes = RootArray( "nodes" );
		var parents = NodeParents();
		var enabled = SupportsExtension( "KHR_node_visibility" );
		var used = enabled && ExtensionNames( "extensionsUsed" ).Contains( "KHR_node_visibility", StringComparer.Ordinal );
		if ( overrides is not null )
		{
			if ( !enabled && overrides.Count != 0 ) throw new GltfException( "KHR_node_visibility overrides require extension support." );
			foreach ( var index in overrides.Keys )
				if ( (uint)index >= (uint)nodes.Length ) throw new GltfException( "Visibility override node index is out of range." );
		}

		var local = new bool[nodes.Length];
		for ( var i = 0; i < nodes.Length; i++ )
		{
			var node = SceneObject( nodes[i], "node" );
			local[i] = true;
			if ( enabled && node.TryGetProperty( "extensions", out var extensions ) )
			{
				SceneObject( extensions, "node extensions" );
				if ( extensions.TryGetProperty( "KHR_node_visibility", out var visibility ) )
				{
					if ( !used ) throw new GltfException( "KHR_node_visibility must be listed in extensionsUsed." );
					SceneObject( visibility, "KHR_node_visibility" );
					if ( visibility.TryGetProperty( "visible", out var value ) )
					{
						if ( value.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ) throw new GltfException( "Node visibility must be a boolean." );
						local[i] = value.GetBoolean();
					}
				}
			}
			if ( overrides is not null && overrides.TryGetValue( i, out var visible ) ) local[i] = visible;
		}

		// Resolve each chain iteratively so deeply nested valid hierarchies cannot
		// overflow the stack. Parent validation has already excluded cycles.
		var result = new bool[nodes.Length];
		var resolved = new bool[nodes.Length];
		var path = new List<int>();
		for ( var i = 0; i < nodes.Length; i++ )
		{
			path.Clear();
			var current = i;
			while ( current >= 0 && !resolved[current] ) { path.Add( current ); current = parents[current]; }
			var inherited = current < 0 || result[current];
			for ( var j = path.Count - 1; j >= 0; j-- )
			{
				var index = path[j];
				inherited &= local[index];
				result[index] = inherited;
				resolved[index] = true;
			}
		}
		return result;
	}
}
