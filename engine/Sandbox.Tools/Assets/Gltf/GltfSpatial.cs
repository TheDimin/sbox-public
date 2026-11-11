using System;
using System.Numerics;
using System.Text.Json.Nodes;
using NVector3 = System.Numerics.Vector3;

namespace Editor;

/// <summary>
/// Converts glTF node transforms to scene transforms and back. glTF is Y up in meters;
/// the engine loader maps (x, y, z) to (z, x, y) in inches, so the preview scene must do the same.
/// </summary>
public static class GltfSpatial
{
	const float InchesPerMeter = 1f / 0.0254f;

	/// <summary>The local position, rotation and scale of a node as scene values.</summary>
	public static (Vector3 Position, Rotation Rotation, Vector3 Scale) ReadPose( GltfElement node )
	{
		var json = node.Json;
		NVector3 translation = NVector3.Zero, scale = NVector3.One;
		var rotation = Quaternion.Identity;
		try
		{
			if ( json["matrix"] is JsonArray { Count: 16 } m )
			{
				var f = new float[16];
				for ( var i = 0; i < 16; i++ ) f[i] = m[i].GetValue<float>();
				var matrix = new Matrix4x4( f[0], f[1], f[2], f[3], f[4], f[5], f[6], f[7], f[8], f[9], f[10], f[11], f[12], f[13], f[14], f[15] );
				if ( !Matrix4x4.Decompose( matrix, out scale, out rotation, out translation ) )
				{
					translation = NVector3.Zero; scale = NVector3.One; rotation = Quaternion.Identity;
				}
			}
			else
			{
				if ( TryVector( json["translation"], out var t ) ) translation = t;
				if ( TryVector( json["scale"], out var s ) ) scale = s;
				if ( json["rotation"] is JsonArray { Count: 4 } r )
					rotation = Quaternion.Normalize( new Quaternion( r[0].GetValue<float>(), r[1].GetValue<float>(), r[2].GetValue<float>(), r[3].GetValue<float>() ) );
			}
		}
		catch ( Exception e ) when ( e is InvalidOperationException or FormatException ) { }

		return (new Vector3( translation.Z, translation.X, translation.Y ) * InchesPerMeter,
			new Rotation( rotation.Z, rotation.X, rotation.Y, rotation.W ),
			new Vector3( scale.Z, scale.X, scale.Y ));
	}

	/// <summary>A glTF translation (meters, Y up) as a scene position.</summary>
	public static Vector3 PositionToScene( float[] t ) => new Vector3( t[2], t[0], t[1] ) * InchesPerMeter;

	/// <summary>A glTF rotation (xyzw) as a scene rotation.</summary>
	public static Rotation RotationToScene( float[] r ) => new( r[2], r[0], r[1], r[3] );

	/// <summary>A glTF scale as a scene scale.</summary>
	public static Vector3 ScaleToScene( float[] s ) => new( s[2], s[0], s[1] );

	/// <summary>A scene position as a glTF translation.</summary>
	public static float[] PositionToGltf( Vector3 p ) => [p.y / InchesPerMeter, p.z / InchesPerMeter, p.x / InchesPerMeter];

	/// <summary>A scene rotation as a glTF rotation (xyzw).</summary>
	public static float[] RotationToGltf( Rotation r ) => [r.y, r.z, r.x, r.w];

	/// <summary>A scene scale as a glTF scale.</summary>
	public static float[] ScaleToGltf( Vector3 s ) => [s.y, s.z, s.x];

	/// <summary>A scene local transform as a glTF local matrix (row-vector form).</summary>
	internal static Matrix4x4 LocalMatrix( Vector3 position, Rotation rotation, Vector3 scale )
	{
		var t = PositionToGltf( position );
		var r = RotationToGltf( rotation );
		var s = ScaleToGltf( scale );
		return Matrix4x4.CreateScale( s[0], s[1], s[2] ) * Matrix4x4.CreateFromQuaternion( new Quaternion( r[0], r[1], r[2], r[3] ) ) * Matrix4x4.CreateTranslation( t[0], t[1], t[2] );
	}

	/// <summary>True if the node JSON already holds this pose, within a small tolerance.</summary>
	public static bool IsPose( GltfElement node, Vector3 position, Rotation rotation, Vector3 scale )
	{
		var (p, r, s) = ReadPose( node );
		return p.Distance( position ) < 0.001f && s.Distance( scale ) < 0.0001f && MathF.Abs( Quaternion.Dot( ToNumerics( r ), ToNumerics( rotation ) ) ) > 0.999999f;
	}

	/// <summary>Write a scene pose into node JSON as translation, rotation and scale. A matrix is replaced.</summary>
	internal static void WritePose( JsonObject json, Vector3 position, Rotation rotation, Vector3 scale )
	{
		json.Remove( "matrix" );
		SetOrRemove( json, "translation", [position.y / InchesPerMeter, position.z / InchesPerMeter, position.x / InchesPerMeter], 0f );
		SetOrRemove( json, "rotation", [rotation.y, rotation.z, rotation.x, rotation.w], null, identity: [0f, 0f, 0f, 1f] );
		SetOrRemove( json, "scale", [scale.y, scale.z, scale.x], 1f );
	}

	static void SetOrRemove( JsonObject json, string key, float[] values, float? defaultValue, float[] identity = null )
	{
		var isDefault = identity is not null ? values.Zip( identity ).All( x => MathF.Abs( x.First - x.Second ) < 1e-6f ) : values.All( x => x == defaultValue );
		if ( isDefault ) json.Remove( key );
		else json[key] = new JsonArray( values.Select( x => (JsonNode)JsonValue.Create( x ) ).ToArray() );
	}

	/// <summary>The world matrix of every node reachable from a scene or a root, in glTF space (row-vector form).</summary>
	internal static Dictionary<GltfElement, Matrix4x4> WorldMatrices( GltfAuthoringDocument document )
	{
		var worlds = new Dictionary<GltfElement, Matrix4x4>();
		var roots = document.GetElements( GltfElementKind.Scene ).SelectMany( document.GetChildren ).Concat( document.GetUnreachableNodes() );
		var stack = new Stack<(GltfElement Node, Matrix4x4 Parent)>( roots.Select( x => (x, Matrix4x4.Identity) ) );
		while ( stack.TryPop( out var item ) )
		{
			if ( worlds.ContainsKey( item.Node ) ) continue;
			var world = LocalMatrix( item.Node ) * item.Parent;
			worlds[item.Node] = world;
			foreach ( var child in document.GetChildren( item.Node ) ) stack.Push( (child, world) );
		}
		return worlds;
	}

	static Matrix4x4 LocalMatrix( GltfElement node )
	{
		var json = node.Json;
		try
		{
			if ( json["matrix"] is JsonArray { Count: 16 } m )
			{
				var f = m.Select( x => x.GetValue<float>() ).ToArray();
				return new Matrix4x4( f[0], f[1], f[2], f[3], f[4], f[5], f[6], f[7], f[8], f[9], f[10], f[11], f[12], f[13], f[14], f[15] );
			}
			var scale = TryVector( json["scale"], out var s ) ? s : NVector3.One;
			var translation = TryVector( json["translation"], out var t ) ? t : NVector3.Zero;
			var rotation = json["rotation"] is JsonArray { Count: 4 } r
				? Quaternion.Normalize( new Quaternion( r[0].GetValue<float>(), r[1].GetValue<float>(), r[2].GetValue<float>(), r[3].GetValue<float>() ) )
				: Quaternion.Identity;
			return Matrix4x4.CreateScale( scale ) * Matrix4x4.CreateFromQuaternion( rotation ) * Matrix4x4.CreateTranslation( translation );
		}
		catch ( Exception e ) when ( e is InvalidOperationException or FormatException )
		{
			return Matrix4x4.Identity;
		}
	}

	static Quaternion ToNumerics( Rotation r ) => new( r.x, r.y, r.z, r.w );

	static bool TryVector( JsonNode node, out NVector3 value )
	{
		value = default;
		if ( node is not JsonArray { Count: >= 3 } array ) return false;
		try { value = new NVector3( array[0].GetValue<float>(), array[1].GetValue<float>(), array[2].GetValue<float>() ); }
		catch ( Exception e ) when ( e is InvalidOperationException or FormatException ) { return false; }
		return true;
	}
}
