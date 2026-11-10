using System.Text.Json;

namespace Sandbox.Gltf;

/// <summary>
/// Decides where each buffer view goes in a compiled file so the lowest levels of detail come first.
/// A loader that reads the file front to back can then draw the lowest level while the finer
/// levels are still arriving.
/// </summary>
public static class GltfStreamOrder
{
	/// <summary>The key of a view that every level needs, or that no level owns. These come first.</summary>
	public const int Shared = -1;

	/// <summary>
	/// One key per buffer view. A view used by only one MSFT_lod level gets that level counted from the lowest
	/// detail of its chain (0 for the lowest). Everything else, such as textures shared by levels, skins,
	/// animation and nodes outside any chain, gets <see cref="Shared"/>. Sort ascending to get the file order.
	/// </summary>
	public static int[] ViewKeys( GltfDocument document )
	{
		var root = document.Root;
		var count = root.TryGetProperty( "bufferViews", out var views ) && views.ValueKind == JsonValueKind.Array ? views.GetArrayLength() : 0;
		var keys = new int[count];
		Array.Fill( keys, Shared );
		var chains = document.ReadNodeLods();
		if ( chains.Count == 0 || count == 0 ) return keys;

		var rank = new Dictionary<int, int>();
		foreach ( var chain in chains )
		{
			rank[chain.Node] = chain.Levels.Count;
			for ( var level = 0; level < chain.Levels.Count; level++ ) rank[chain.Levels[level]] = chain.Levels.Count - 1 - level;
		}

		const int NotSeen = int.MinValue;
		Array.Fill( keys, NotSeen );
		if ( !root.TryGetProperty( "nodes", out var nodes ) || nodes.ValueKind != JsonValueKind.Array ) return keys.Select( _ => Shared ).ToArray();
		var index = 0;
		foreach ( var node in nodes.EnumerateArray() )
		{
			var owner = rank.TryGetValue( index++, out var r ) ? r : Shared;
			foreach ( var view in ViewsOfNode( root, node ) )
			{
				if ( (uint)view >= (uint)count ) continue;
				keys[view] = keys[view] == NotSeen || keys[view] == owner ? owner : Shared;
			}
		}
		for ( var i = 0; i < count; i++ ) if ( keys[i] == NotSeen ) keys[i] = Shared;
		return keys;
	}

	/// <summary>Buffer view indices for the geometry and images a node draws with.</summary>
	static IEnumerable<int> ViewsOfNode( JsonElement root, JsonElement node )
	{
		if ( !node.TryGetProperty( "mesh", out var meshIndex ) || !TryIndex( root, "meshes", meshIndex, out var mesh ) ) yield break;
		if ( node.TryGetProperty( "extensions", out var nodeExtensions ) && nodeExtensions.ValueKind == JsonValueKind.Object &&
			nodeExtensions.TryGetProperty( "EXT_mesh_gpu_instancing", out var instancing ) && instancing.TryGetProperty( "attributes", out var instanceAttributes ) )
			foreach ( var view in AccessorMap( root, instanceAttributes ) ) yield return view;
		if ( !mesh.TryGetProperty( "primitives", out var primitives ) || primitives.ValueKind != JsonValueKind.Array ) yield break;
		foreach ( var primitive in primitives.EnumerateArray() )
		{
			if ( primitive.TryGetProperty( "attributes", out var attributes ) ) foreach ( var view in AccessorMap( root, attributes ) ) yield return view;
			if ( primitive.TryGetProperty( "indices", out var indices ) ) foreach ( var view in AccessorViews( root, indices ) ) yield return view;
			if ( primitive.TryGetProperty( "targets", out var targets ) && targets.ValueKind == JsonValueKind.Array )
				foreach ( var target in targets.EnumerateArray() ) foreach ( var view in AccessorMap( root, target ) ) yield return view;
			if ( primitive.TryGetProperty( "extensions", out var extensions ) && extensions.ValueKind == JsonValueKind.Object )
			{
				if ( extensions.TryGetProperty( "KHR_draco_mesh_compression", out var draco ) && draco.TryGetProperty( "bufferView", out var dracoView ) && dracoView.TryGetInt32( out var dracoIndex ) )
					yield return dracoIndex;
				if ( extensions.TryGetProperty( "KHR_materials_variants", out var variants ) && variants.TryGetProperty( "mappings", out var mappings ) && mappings.ValueKind == JsonValueKind.Array )
					foreach ( var mapping in mappings.EnumerateArray() )
						if ( mapping.TryGetProperty( "material", out var variantMaterial ) && TryIndex( root, "materials", variantMaterial, out var m ) )
							foreach ( var view in MaterialViews( root, m ) ) yield return view;
			}
			if ( primitive.TryGetProperty( "material", out var materialIndex ) && TryIndex( root, "materials", materialIndex, out var material ) )
				foreach ( var view in MaterialViews( root, material ) ) yield return view;
		}
	}

	static IEnumerable<int> AccessorMap( JsonElement root, JsonElement map )
	{
		if ( map.ValueKind != JsonValueKind.Object ) yield break;
		foreach ( var property in map.EnumerateObject() )
			foreach ( var view in AccessorViews( root, property.Value ) ) yield return view;
	}

	static IEnumerable<int> AccessorViews( JsonElement root, JsonElement accessorIndex )
	{
		if ( !TryIndex( root, "accessors", accessorIndex, out var accessor ) ) yield break;
		if ( accessor.TryGetProperty( "bufferView", out var view ) && view.TryGetInt32( out var v ) ) yield return v;
		if ( !accessor.TryGetProperty( "sparse", out var sparse ) || sparse.ValueKind != JsonValueKind.Object ) yield break;
		foreach ( var part in new[] { "indices", "values" } )
			if ( sparse.TryGetProperty( part, out var p ) && p.TryGetProperty( "bufferView", out var sv ) && sv.TryGetInt32( out var s ) ) yield return s;
	}

	/// <summary>The embedded images of every texture a material names. Texture slots are objects called ...Texture with an index.</summary>
	static IEnumerable<int> MaterialViews( JsonElement root, JsonElement material )
	{
		var pending = new Stack<JsonElement>();
		pending.Push( material );
		while ( pending.Count != 0 )
		{
			var current = pending.Pop();
			if ( current.ValueKind != JsonValueKind.Object ) continue;
			foreach ( var property in current.EnumerateObject() )
			{
				if ( property.Value.ValueKind != JsonValueKind.Object ) continue;
				if ( property.Name.EndsWith( "Texture", StringComparison.Ordinal ) && property.Value.TryGetProperty( "index", out var textureIndex ) &&
					TryIndex( root, "textures", textureIndex, out var texture ) )
				{
					foreach ( var image in TextureImages( texture ) )
						if ( TryIndex( root, "images", image, out var imageJson ) && imageJson.TryGetProperty( "bufferView", out var view ) && view.TryGetInt32( out var v ) )
							yield return v;
				}
				pending.Push( property.Value );
			}
		}
	}

	static IEnumerable<JsonElement> TextureImages( JsonElement texture )
	{
		if ( texture.TryGetProperty( "source", out var source ) ) yield return source;
		if ( !texture.TryGetProperty( "extensions", out var extensions ) || extensions.ValueKind != JsonValueKind.Object ) yield break;
		foreach ( var extension in extensions.EnumerateObject() )
			if ( extension.Value.ValueKind == JsonValueKind.Object && extension.Value.TryGetProperty( "source", out var alternate ) ) yield return alternate;
	}

	static bool TryIndex( JsonElement root, string array, JsonElement index, out JsonElement element )
	{
		element = default;
		if ( index.ValueKind != JsonValueKind.Number || !index.TryGetInt32( out var i ) || i < 0 ) return false;
		if ( !root.TryGetProperty( array, out var values ) || values.ValueKind != JsonValueKind.Array || i >= values.GetArrayLength() ) return false;
		element = values[i];
		return true;
	}
}
