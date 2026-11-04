using System.Numerics;
using System.Text.Json;

namespace Sandbox.Gltf;

public sealed partial class GltfDocument
{
	/// <summary>Evaluates independent rest or animation state in glTF world coordinates.</summary>
	public GltfPose EvaluatePose( int? animation = null, float time = 0 )
	{
		if ( !float.IsFinite( time ) ) throw new GltfException( "Animation sample time must be finite." );
		var nodeArray = Root.TryGetProperty( "nodes", out var nodes ) ? RequireArray( nodes, "nodes" ) : default;
		var count = nodeArray.ValueKind == JsonValueKind.Array ? nodeArray.GetArrayLength() : 0;
		var locals = new Matrix4x4[count];
		var weights = new float[count][];
		var parents = new int[count];
		Array.Fill( parents, -1 );
		// GetWorldTransform also validates the graph, including multiple parents and cycles.
		if ( count > 0 ) _ = GetWorldTransform( 0 );
		for ( var i = 0; i < count; i++ )
		{
			var node = SceneObject( nodeArray[i], "node" );
			locals[i] = SceneLocalTransform( node );
			weights[i] = GetMorphWeights( i );
			if ( node.TryGetProperty( "children", out var children ) )
				foreach ( var child in children.EnumerateArray() ) parents[SceneIndex( child, "node child" )] = i;
		}

		if ( animation.HasValue )
		{
			var value = SceneObject( ElementAt( "animations", animation.Value ), "animation" );
			var channels = RequireArray( GltfJson.Required( value, "channels" ), "animation channels" );
			var samplers = RequireArray( GltfJson.Required( value, "samplers" ), "animation samplers" );
			if ( channels.GetArrayLength() == 0 || samplers.GetArrayLength() == 0 ) throw new GltfException( "Animation needs channels and samplers." );
			var updates = new Dictionary<(int Node, string Path), float[]>();
			foreach ( var channel in channels.EnumerateArray() )
			{
				SceneObject( channel, "animation channel" );
				var target = SceneObject( GltfJson.Required( channel, "target" ), "animation target" );
				if ( !target.TryGetProperty( "node", out var targetNode ) ) continue;
				var nodeIndex = SceneIndex( targetNode, "animation target node" );
				if ( nodeIndex >= count ) throw new GltfException( "Animation target node is out of range." );
				if ( nodeArray[nodeIndex].TryGetProperty( "matrix", out _ ) ) throw new GltfException( "An animated node cannot define a matrix." );
				var path = GltfJson.String( target, "path" );
				var components = path switch { "translation" or "scale" => 3, "rotation" => 4, "weights" => weights[nodeIndex].Length, _ => 0 };
				if ( components == 0 ) throw new GltfException( $"Invalid animation target path '{path}' or missing morph targets." );
				var samplerIndex = GltfJson.Int( channel, "sampler" );
				if ( (uint)samplerIndex >= (uint)samplers.GetArrayLength() ) throw new GltfException( "Animation sampler index is out of range." );
				var sampler = SceneObject( samplers[samplerIndex], "animation sampler" );
				var inputIndex = GltfJson.Int( sampler, "input" );
				var outputIndex = GltfJson.Int( sampler, "output" );
				var input = Accessor( inputIndex );
				var output = Accessor( outputIndex );
				if ( GltfJson.String( input.Json, "type" ) != "SCALAR" || input.ComponentType != 5126 || input.Normalized )
					throw new GltfException( "Animation input must be a FLOAT SCALAR accessor." );
				var outputType = path == "rotation" ? "VEC4" : path == "weights" ? "SCALAR" : "VEC3";
				var validFloat = output.ComponentType == 5126 && !output.Normalized;
				var validNormalized = (path is "rotation" or "weights") && output.Normalized && (output.ComponentType is 5120 or 5121 or 5122 or 5123);
				if ( GltfJson.String( output.Json, "type" ) != outputType || !(validFloat || validNormalized) )
					throw new GltfException( $"Invalid accessor format for animation path '{path}'." );
				var interpolation = GltfJson.String( sampler, "interpolation", "LINEAR" );
				var sample = GltfAnimationSampler.Sample( ReadFloatAccessor( inputIndex ), ReadFloatAccessor( outputIndex ), components, interpolation, time, path == "rotation" );
				if ( !updates.TryAdd( (nodeIndex, path), sample ) ) throw new GltfException( "An animation cannot target the same node property twice." );
			}

			foreach ( var entry in updates )
				if ( entry.Key.Path == "weights" ) weights[entry.Key.Node] = entry.Value;
			for ( var i = 0; i < count; i++ )
			{
				if ( !updates.ContainsKey( (i, "translation") ) && !updates.ContainsKey( (i, "rotation") ) && !updates.ContainsKey( (i, "scale") ) ) continue;
				var node = nodeArray[i];
				var translation = PoseTrs( updates, i, "translation", node, [0, 0, 0] );
				var rotation = PoseTrs( updates, i, "rotation", node, [0, 0, 0, 1] );
				var scale = PoseTrs( updates, i, "scale", node, [1, 1, 1] );
				var quaternion = new Quaternion( rotation[0], rotation[1], rotation[2], rotation[3] );
				if ( !float.IsFinite( quaternion.LengthSquared() ) || quaternion.LengthSquared() < 1e-20f ) throw new GltfException( "Invalid animated rotation." );
				locals[i] = Matrix4x4.CreateScale( scale[0], scale[1], scale[2] ) * Matrix4x4.CreateFromQuaternion( Quaternion.Normalize( quaternion ) )
					* Matrix4x4.CreateTranslation( translation[0], translation[1], translation[2] );
			}
		}

		var worlds = new Matrix4x4[count];
		for ( var i = 0; i < count; i++ )
		{
			var world = Matrix4x4.Identity;
			for ( var current = i; current >= 0; current = parents[current] ) world *= locals[current];
			worlds[i] = world;
		}
		return new GltfPose( worlds, weights );
	}

	private static float[] PoseTrs( Dictionary<(int Node, string Path), float[]> updates, int index, string name, JsonElement node, float[] defaults )
	{
		if ( updates.TryGetValue( (index, name), out var value ) ) return value;
		return node.TryGetProperty( name, out var property ) ? SceneFloatArray( property, defaults.Length, name ) : defaults;
	}
}

public sealed partial class GltfPose
{
	private readonly Matrix4x4[] poseWorlds;
	private readonly float[][] poseWeights;

	internal GltfPose( Matrix4x4[] worlds, float[][] weights ) => (poseWorlds, poseWeights) = (worlds, weights);

	public Matrix4x4 GetWorldTransform( int node )
	{
		if ( (uint)node >= (uint)poseWorlds.Length ) throw new GltfException( "Pose node index is out of range." );
		return poseWorlds[node];
	}

	public float[] GetMorphWeights( int node )
	{
		if ( (uint)node >= (uint)poseWeights.Length ) throw new GltfException( "Pose node index is out of range." );
		return (float[])poseWeights[node].Clone();
	}
}
