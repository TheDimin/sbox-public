using Sandbox.Gltf;

namespace Sandbox;

/// <summary>Choose only capabilities exercised by the public material route.</summary>
internal static class GltfPublicMaterialRoute
{
	// Every explicit lit material uses the source-lit glTF shader, so all glTF surfaces share one
	// lighting model (environment diffuse, clearcoat, anisotropy). The older stock route lit
	// simple materials differently from textured ones.
	internal static bool NeedsSourceLit( GltfMaterial material ) => material is not null && !material.Unlit;

	internal static bool RequiresProjected( GltfDocument document, int? sceneIndex )
	{
		ArgumentNullException.ThrowIfNull( document );
		if ( !sceneIndex.HasValue ) return false;
		var root = document.Root;
		if ( !root.TryGetProperty( "scenes", out var scenes ) || sceneIndex.Value < 0 || sceneIndex.Value >= scenes.GetArrayLength() )
			throw new ArgumentOutOfRangeException( nameof( sceneIndex ) );
		var pending = new Stack<int>();
		var scene = scenes[sceneIndex.Value];
		if ( scene.TryGetProperty( "nodes", out var roots ) ) foreach ( var node in roots.EnumerateArray() ) pending.Push( node.GetInt32() );
		var visited = new HashSet<int>();
		var variants = document.ReadMaterialVariantNames();
		// Variant selections need packet ownership even when every reachable
		// material uses the legacy stock adapter.
		bool projected = variants.Count != 0;
		while ( pending.TryPop( out var index ) )
		{
			if ( !visited.Add( index ) ) throw new GltfException( "Selected node is repeated." );
			var node = root.GetProperty( "nodes" )[index];
			if ( node.TryGetProperty( "children", out var children ) ) foreach ( var child in children.EnumerateArray() ) pending.Push( child.GetInt32() );
			if ( !node.TryGetProperty( "mesh", out var meshValue ) ) continue;
			var mesh = meshValue.GetInt32();
			var primitives = root.GetProperty( "meshes" )[mesh].GetProperty( "primitives" );
			for ( var primitive = 0; primitive < primitives.GetArrayLength(); primitive++ )
			{
				if ( !primitives[primitive].GetProperty( "attributes" ).TryGetProperty( "POSITION", out _ ) ) continue;
				for ( var variant = -1; variant < variants.Count; variant++ )
				{
					var material = document.GetMaterialForVariant( mesh, primitive, variant < 0 ? null : variant );
					if ( material.HasValue )
					{
						var selected = document.ReadMaterial( material.Value );
						if ( selected.Unlit || NeedsSourceLit( selected ) ) projected = true;
					}
				}
			}
		}
		return projected;
	}
}
