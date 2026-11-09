using System.Numerics;

namespace Sandbox.Gltf;

public sealed partial class GltfDocument
{
	/// <summary>Evaluates one rest-pose vertex in glTF world coordinates.</summary>
	public Vector3 EvaluateVertexPosition( int node, int primitive, int vertex )
	{
		var nodeValue = SceneObject( ElementAt( "nodes", node ), "node" );
		var mesh = SceneIndex( GltfJson.Required( nodeValue, "mesh" ), "node mesh" );
		var geometry = ReadPrimitive( mesh, primitive );
		if ( (uint)vertex >= (uint)geometry.VertexCount ) throw new GltfException( "Vertex index is out of range." );
		if ( !geometry.Attributes.TryGetValue( "POSITION", out var positionAccessor ) )
			throw new GltfException( "Vertex position evaluation requires a POSITION attribute." );
		var positions = ReadFloatAccessor( positionAccessor );
		var offset = vertex * 3;
		var position = new Vector3( positions[offset], positions[offset + 1], positions[offset + 2] );
		var targets = ReadMorphTargets( mesh, primitive );
		var morphWeights = GetMorphWeights( node );
		if ( targets.Length != 0 && targets.Length != morphWeights.Length ) throw new GltfException( "Morph target count does not match mesh weights." );
		for ( var i = 0; i < targets.Length; i++ )
		{
			var deltas = targets[i].PositionDeltas;
			if ( deltas is null ) continue;
			position += new Vector3( deltas[offset], deltas[offset + 1], deltas[offset + 2] ) * morphWeights[i];
		}

		// A node skin influences only primitives that actually provide skinning attributes.
		if ( !nodeValue.TryGetProperty( "skin", out var skinValue ) || !geometry.Attributes.ContainsKey( "JOINTS_0" ) )
			return Vector3.Transform( position, GetWorldTransform( node ) );
		var skin = ReadSkin( SceneIndex( skinValue, "node skin" ) );
		return EvaluateSkinnedPosition( geometry, skin, vertex, position, GetWorldTransform );
	}
}
