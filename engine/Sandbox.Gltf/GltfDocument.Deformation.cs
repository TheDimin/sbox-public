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
		var result = Vector3.Zero;
		for ( var set = 0; geometry.Attributes.TryGetValue( "JOINTS_" + set, out var jointsAccessor ); set++ )
		{
			if ( !geometry.Attributes.TryGetValue( "WEIGHTS_" + set, out var weightsAccessor ) ) throw new GltfException( "Missing skinning weight set." );
			var joints = ReadFloatAccessor( jointsAccessor );
			var weights = ReadFloatAccessor( weightsAccessor );
			var baseIndex = vertex * 4;
			for ( var influence = 0; influence < 4; influence++ )
			{
				var joint = (int)joints[baseIndex + influence];
				var weight = weights[baseIndex + influence];
				if ( (uint)joint >= (uint)skin.Joints.Length ) throw new GltfException( "Skinning joint index is out of range." );
				if ( weight < 0 || !float.IsFinite( weight ) ) throw new GltfException( "Skinning weights must be finite and non-negative." );
				if ( weight == 0 ) continue;
				var world = skin.InverseBindMatrices[joint] * GetWorldTransform( skin.Joints[joint] );
				result += Vector3.Transform( position, world ) * weight;
			}
		}
		return result;
	}
}
