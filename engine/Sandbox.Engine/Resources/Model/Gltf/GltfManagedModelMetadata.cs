using Sandbox.Gltf;
using NMatrix = System.Numerics.Matrix4x4;

namespace Sandbox;

/// <summary>Authored identities, independent of GPU palette slot order and inverse binds.</summary>
internal sealed class GltfManagedModelMetadata
{
	internal sealed record Bone( int Node, int Parent, string Name, NMatrix Rest );
	internal sealed record Morph( int Node, int Target, string Name );
	internal GltfModelAnimation Animation { get; private set; }
	internal IReadOnlyList<Bone> Bones { get; }
	internal IReadOnlyList<Morph> Morphs { get; }

	/// <summary>Drop the document graph after the last asset/instance lease, even if an invalid Model survives.</summary>
	internal void ReleaseDocument()
	{
		var animation = Animation;
		Animation = null;
		animation?.Dispose();
	}

	internal GltfManagedModelMetadata( GltfDocument document, int? selectedScene = null )
	{
		selectedScene ??= document.GetDefaultScene();
		if ( selectedScene.HasValue && (!document.Root.TryGetProperty( "scenes", out var scenes ) || selectedScene.Value < 0 || selectedScene.Value >= scenes.GetArrayLength()) )
			throw new ArgumentOutOfRangeException( nameof( selectedScene ), "The requested glTF scene does not exist." );
		Animation = new GltfModelAnimation( document );
		var rest = document.EvaluatePose();
		if ( !selectedScene.HasValue || !document.Root.TryGetProperty( "nodes", out var nodes ) )
		{
			Bones = Array.Empty<Bone>(); Morphs = Array.Empty<Morph>(); return;
		}
		var parents = Enumerable.Repeat( -1, nodes.GetArrayLength() ).ToArray();
		for ( var i = 0; i < parents.Length; i++ )
			if ( nodes[i].TryGetProperty( "children", out var children ) )
				foreach ( var child in children.EnumerateArray() ) parents[child.GetInt32()] = i;
		var selected = new List<int>();
		var sceneIndex = selectedScene.Value;
		var scene = document.Root.GetProperty( "scenes" )[sceneIndex];
		var pending = new Stack<int>();
		if ( scene.TryGetProperty( "nodes", out var roots ) )
			foreach ( var root in roots.EnumerateArray().Reverse() ) pending.Push( root.GetInt32() );
		while ( pending.TryPop( out var node ) )
		{
			selected.Add( node );
			if ( nodes[node].TryGetProperty( "children", out var children ) )
				foreach ( var child in children.EnumerateArray().Reverse() ) pending.Push( child.GetInt32() );
		}

		var required = new HashSet<int>();
		foreach ( var node in selected )
			if ( nodes[node].TryGetProperty( "skin", out var skin ) )
				foreach ( var joint in document.ReadSkin( skin.GetInt32() ).Joints )
					for ( var ancestor = joint; ancestor >= 0 && required.Add( ancestor ); ancestor = parents[ancestor] ) { }
		// Parent-first ordering is deterministic even if source nodes are not topological.
		var ordered = new List<int>();
		var emitted = new HashSet<int>();
		foreach ( var node in required.Order() ) AddParentFirst( node );
		void AddParentFirst( int node )
		{
			if ( !emitted.Add( node ) ) return;
			if ( parents[node] >= 0 ) AddParentFirst( parents[node] );
			ordered.Add( node );
		}
		var indices = ordered.Select( ( node, index ) => (node, index) ).ToDictionary( pair => pair.node, pair => pair.index );
		var names = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		var bones = new List<Bone>();
		foreach ( var node in ordered )
		{
			var authored = nodes[node].TryGetProperty( "name", out var name ) ? name.GetString() : null;
			var identity = string.IsNullOrWhiteSpace( authored ) ? $"gltf_node_{node}" : authored;
			var unique = identity;
			for ( var suffix = 1; !names.Add( unique ); suffix++ ) unique = $"{identity}_{suffix}";
			bones.Add( new( node, parents[node] < 0 ? -1 : indices[parents[node]], unique, GltfGeometry.ConvertTransform( rest.GetWorldTransform( node ) ) ) );
		}
		Bones = bones.AsReadOnly();

		var morphs = new List<Morph>();
		names.Clear();
		foreach ( var node in selected )
		{
			if ( !nodes[node].TryGetProperty( "mesh", out var meshIndex ) ) continue;
			var mesh = document.Root.GetProperty( "meshes" )[meshIndex.GetInt32()];
			var count = rest.GetMorphWeights( node ).Length;
			System.Text.Json.JsonElement targetNames = default;
			var hasNames = mesh.TryGetProperty( "extras", out var extras ) && extras.ValueKind == System.Text.Json.JsonValueKind.Object &&
				extras.TryGetProperty( "targetNames", out targetNames ) && targetNames.ValueKind == System.Text.Json.JsonValueKind.Array;
			for ( var target = 0; target < count; target++ )
			{
				var authored = hasNames && target < targetNames.GetArrayLength() && targetNames[target].ValueKind == System.Text.Json.JsonValueKind.String ? targetNames[target].GetString() : null;
				var identity = string.IsNullOrWhiteSpace( authored ) ? $"gltf_node_{node}_target_{target}" : authored;
				var unique = identity;
				for ( var suffix = 1; !names.Add( unique ); suffix++ ) unique = $"{identity}_{suffix}";
				morphs.Add( new( node, target, unique ) );
			}
		}
		Morphs = morphs.AsReadOnly();
	}

	internal static Transform ToPublicTransform( NMatrix matrix )
	{
		// Pure affine-to-TRS check; this helper neither constructs nor touches native models.
		if ( GltfModelRig.TryNativeTransform( matrix, out var transform ) ) return transform;
		throw new NotSupportedException( "The current glTF bone conversion could not reconstruct this matrix as a public Transform within its accuracy bound. This may indicate shear or a decomposition limitation; GPU animation retains its full affine matrix." );
	}
}
