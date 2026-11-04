using System.Numerics;
using System.Text.Json;

namespace Sandbox.Gltf;

public sealed partial class GltfDocument
{
	/// <summary>Returns the explicitly selected default scene, if present.</summary>
	public int? GetDefaultScene()
	{
		if ( !Root.TryGetProperty( "scene", out var value ) ) return null;
		var index = SceneIndex( value, "scene" );
		_ = ElementAt( "scenes", index );
		return index;
	}

	/// <summary>Gets the node's rest-pose transform in glTF coordinates.</summary>
	public Matrix4x4 GetWorldTransform( int node )
	{
		_ = SceneObject( ElementAt( "nodes", node ), "node" );
		var nodes = Root.GetProperty( "nodes" );
		var count = nodes.GetArrayLength();
		var parents = new int[count];
		Array.Fill( parents, -1 );
		for ( var parent = 0; parent < count; parent++ )
		{
			var element = SceneObject( nodes[parent], "node" );
			if ( !element.TryGetProperty( "children", out var children ) ) continue;
			if ( children.ValueKind != JsonValueKind.Array ) throw new GltfException( "Node children must be an array." );
			foreach ( var childValue in children.EnumerateArray() )
			{
				var child = SceneIndex( childValue, "node child" );
				if ( child >= count ) throw new GltfException( "Node child index is out of range." );
				if ( parents[child] != -1 ) throw new GltfException( "A node cannot have multiple parents or duplicate child references." );
				parents[child] = parent;
			}
		}

		// A node has at most one parent; visiting each chain once detects cycles without recursion.
		var states = new byte[count];
		for ( var start = 0; start < count; start++ )
		{
			var current = start;
			while ( current >= 0 && states[current] == 0 )
			{
				states[current] = 1;
				current = parents[current];
			}
			if ( current >= 0 && states[current] == 1 ) throw new GltfException( "Node hierarchy contains a cycle." );
			current = start;
			while ( current >= 0 && states[current] == 1 )
			{
				states[current] = 2;
				current = parents[current];
			}
		}

		var world = Matrix4x4.Identity;
		for ( var current = node; current >= 0; current = parents[current] )
			world *= SceneLocalTransform( nodes[current] );
		return world;
	}

	public GltfSkin ReadSkin( int index )
	{
		var skin = SceneObject( ElementAt( "skins", index ), "skin" );
		if ( !skin.TryGetProperty( "joints", out var jointArray ) || jointArray.ValueKind != JsonValueKind.Array || jointArray.GetArrayLength() == 0 )
			throw new GltfException( "A skin must have at least one joint." );
		var joints = new int[jointArray.GetArrayLength()];
		var uniqueJoints = new HashSet<int>();
		var at = 0;
		foreach ( var value in jointArray.EnumerateArray() )
		{
			var joint = SceneIndex( value, "skin joint" );
			_ = SceneObject( ElementAt( "nodes", joint ), "joint" );
			if ( !uniqueJoints.Add( joint ) ) throw new GltfException( "Skin joints must be unique." );
			joints[at++] = joint;
		}

		int? skeleton = null;
		if ( skin.TryGetProperty( "skeleton", out var skeletonValue ) )
		{
			skeleton = SceneIndex( skeletonValue, "skin skeleton" );
			_ = SceneObject( ElementAt( "nodes", skeleton.Value ), "skeleton" );
		}

		var matrices = new Matrix4x4[joints.Length];
		if ( !skin.TryGetProperty( "inverseBindMatrices", out var accessorValue ) )
		{
			Array.Fill( matrices, Matrix4x4.Identity );
		}
		else
		{
			var accessorIndex = SceneIndex( accessorValue, "inverse bind matrices" );
			var accessor = SceneObject( ElementAt( "accessors", accessorIndex ), "accessor" );
			if ( !accessor.TryGetProperty( "type", out var type ) || type.ValueKind != JsonValueKind.String || type.GetString() != "MAT4"
				|| GltfJson.Int( accessor, "componentType" ) != 5126 )
				throw new GltfException( "Inverse bind matrices must use a FLOAT MAT4 accessor." );
			var values = ReadFloatAccessor( accessorIndex );
			if ( values.Length / 16 < joints.Length ) throw new GltfException( "There are fewer inverse bind matrices than joints." );
			for ( var i = 0; i < matrices.Length; i++ ) matrices[i] = SceneMatrix( values.AsSpan( i * 16, 16 ) );
		}
		return new GltfSkin { Joints = joints, Skeleton = skeleton, InverseBindMatrices = matrices };
	}

	public GltfMorphTarget[] ReadMorphTargets( int mesh, int primitive )
	{
		var meshElement = SceneObject( ElementAt( "meshes", mesh ), "mesh" );
		var primitiveElement = SceneObject( SceneArrayElement( meshElement, "primitives", primitive ), "primitive" );
		if ( !primitiveElement.TryGetProperty( "targets", out var targets ) ) return [];
		if ( targets.ValueKind != JsonValueKind.Array ) throw new GltfException( "Morph targets must be an array." );

		int? vertexCount = null;
		if ( primitiveElement.TryGetProperty( "attributes", out var attributes ) && attributes.ValueKind == JsonValueKind.Object
			&& attributes.TryGetProperty( "POSITION", out var position ) )
		{
			var positionAccessor = SceneObject( ElementAt( "accessors", SceneIndex( position, "position accessor" ) ), "accessor" );
			vertexCount = ScenePropertyIndex( positionAccessor, "count" );
		}
		var result = new GltfMorphTarget[targets.GetArrayLength()];
		var i = 0;
		foreach ( var target in targets.EnumerateArray() )
		{
			if ( target.ValueKind != JsonValueKind.Object ) throw new GltfException( "A morph target must be an object." );
			result[i++] = new GltfMorphTarget
			{
				PositionDeltas = SceneMorphAttribute( target, "POSITION", vertexCount ),
				NormalDeltas = SceneMorphAttribute( target, "NORMAL", vertexCount ),
				TangentDeltas = SceneMorphAttribute( target, "TANGENT", vertexCount )
			};
		}
		return result;
	}

	public float[] GetMorphWeights( int node )
	{
		var nodeElement = SceneObject( ElementAt( "nodes", node ), "node" );
		if ( !nodeElement.TryGetProperty( "mesh", out var meshValue ) ) return [];
		var mesh = SceneObject( ElementAt( "meshes", SceneIndex( meshValue, "node mesh" ) ), "mesh" );
		if ( !mesh.TryGetProperty( "primitives", out var primitives ) || primitives.ValueKind != JsonValueKind.Array || primitives.GetArrayLength() == 0 )
			throw new GltfException( "A mesh must contain primitives." );
		var count = 0;
		foreach ( var primitive in primitives.EnumerateArray() )
		{
			SceneObject( primitive, "primitive" );
			if ( !primitive.TryGetProperty( "targets", out var targets ) ) continue;
			if ( targets.ValueKind != JsonValueKind.Array ) throw new GltfException( "Morph targets must be an array." );
			var targetCount = targets.GetArrayLength();
			if ( count != 0 && targetCount != 0 && count != targetCount )
				throw new GltfException( "All primitives defining morph targets must have the same target count." );
			count = Math.Max( count, targetCount );
		}
		if ( nodeElement.TryGetProperty( "weights", out var weights ) || mesh.TryGetProperty( "weights", out weights ) )
			return SceneFloatArray( weights, count, "morph weights" );
		return new float[count];
	}

	private float[] SceneMorphAttribute( JsonElement target, string semantic, int? vertexCount )
	{
		if ( !target.TryGetProperty( semantic, out var value ) ) return null;
		var accessorIndex = SceneIndex( value, "morph target accessor" );
		var accessor = SceneObject( ElementAt( "accessors", accessorIndex ), "accessor" );
		if ( !accessor.TryGetProperty( "type", out var type ) || type.ValueKind != JsonValueKind.String || type.GetString() != "VEC3" )
			throw new GltfException( "Morph target deltas must use VEC3 accessors." );
		var component = GltfJson.Int( accessor, "componentType" );
		var normalized = GltfJson.Bool( accessor, "normalized" );
		if ( component != 5126 || normalized )
		{
			var signedInteger = component is 5120 or 5122;
			var validFormat = signedInteger && (semantic == "POSITION" || normalized);
			if ( !validFormat || !SupportsExtension( "KHR_mesh_quantization" ) || !SceneRequiresQuantization() )
				throw new GltfException( $"Unsupported component format for morph target {semantic}." );
		}
		if ( vertexCount.HasValue && ScenePropertyIndex( accessor, "count" ) != vertexCount.Value )
			throw new GltfException( "Morph target and base attribute counts must match." );
		return ReadFloatAccessor( accessorIndex );
	}

	private bool SceneRequiresQuantization()
	{
		if ( !Root.TryGetProperty( "extensionsRequired", out var required ) || required.ValueKind != JsonValueKind.Array ) return false;
		foreach ( var extension in required.EnumerateArray() )
			if ( extension.ValueKind == JsonValueKind.String && extension.GetString() == "KHR_mesh_quantization" ) return true;
		return false;
	}

	private static Matrix4x4 SceneLocalTransform( JsonElement node )
	{
		var hasMatrix = node.TryGetProperty( "matrix", out var matrix );
		var hasTranslation = node.TryGetProperty( "translation", out var translation );
		var hasRotation = node.TryGetProperty( "rotation", out var rotation );
		var hasScale = node.TryGetProperty( "scale", out var scale );
		if ( hasMatrix )
		{
			if ( hasTranslation || hasRotation || hasScale ) throw new GltfException( "A node cannot combine matrix and TRS." );
			return SceneMatrix( SceneFloatArray( matrix, 16, "node matrix" ) );
		}
		var t = hasTranslation ? SceneFloatArray( translation, 3, "node translation" ) : [0f, 0f, 0f];
		var r = hasRotation ? SceneFloatArray( rotation, 4, "node rotation" ) : [0f, 0f, 0f, 1f];
		var s = hasScale ? SceneFloatArray( scale, 3, "node scale" ) : [1f, 1f, 1f];
		var quaternion = new Quaternion( r[0], r[1], r[2], r[3] );
		if ( quaternion.LengthSquared() < 1e-20f ) throw new GltfException( "Node rotation cannot be a zero quaternion." );
		return Matrix4x4.CreateScale( s[0], s[1], s[2] ) * Matrix4x4.CreateFromQuaternion( Quaternion.Normalize( quaternion ) ) * Matrix4x4.CreateTranslation( t[0], t[1], t[2] );
	}

	private static Matrix4x4 SceneMatrix( ReadOnlySpan<float> m ) => new(
		m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7],
		m[8], m[9], m[10], m[11], m[12], m[13], m[14], m[15] );

	private static float[] SceneFloatArray( JsonElement value, int count, string description )
	{
		if ( value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != count ) throw new GltfException( $"Invalid {description} length." );
		var result = new float[count];
		var i = 0;
		foreach ( var number in value.EnumerateArray() )
		{
			if ( number.ValueKind != JsonValueKind.Number || !number.TryGetSingle( out var component ) || !float.IsFinite( component ) )
				throw new GltfException( $"Invalid value in {description}." );
			result[i++] = component;
		}
		return result;
	}

	private static int ScenePropertyIndex( JsonElement element, string property )
	{
		if ( !element.TryGetProperty( property, out var value ) ) throw new GltfException( $"Missing {property}." );
		return SceneIndex( value, property );
	}

	private static int SceneIndex( JsonElement value, string description )
	{
		var index = GltfJson.Integer( value, description );
		if ( index < 0 ) throw new GltfException( $"Invalid {description}." );
		return index;
	}

	private static JsonElement SceneObject( JsonElement value, string description )
	{
		if ( value.ValueKind != JsonValueKind.Object ) throw new GltfException( $"Expected {description} object." );
		return value;
	}

	private static JsonElement SceneArrayElement( JsonElement element, string property, int index )
	{
		if ( element.ValueKind != JsonValueKind.Object || !element.TryGetProperty( property, out var array ) || array.ValueKind != JsonValueKind.Array || index < 0 || index >= array.GetArrayLength() )
			throw new GltfException( $"Invalid {property} index." );
		return array[index];
	}
}
