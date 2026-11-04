using System;
using System.Collections.Generic;
using System.Linq;
using NVector3 = System.Numerics.Vector3;
using NVector4 = System.Numerics.Vector4;
using NMatrix4x4 = System.Numerics.Matrix4x4;
using System.Text.Json;

namespace Sandbox.Gltf;

/// <summary>A node instance and its static primitive, transformed into s&amp;box model space.</summary>
public sealed class GltfStaticScenePrimitive
{
	public int Node { get; init; }
	public int Mesh { get; init; }
	public int Primitive { get; init; }
	public GltfDrawData Data { get; init; }
}

/// <summary>Owned static geometry from the explicitly selected default scene.</summary>
public sealed class GltfStaticScene
{
	public IReadOnlyList<GltfStaticScenePrimitive> Primitives { get; init; }
	public bool HasBounds { get; init; }
	public NVector3 BoundsMin { get; init; }
	public NVector3 BoundsMax { get; init; }
}

public static class GltfStaticSceneAssembler
{
	/// <summary>
	/// Assemble all selected static mesh instances. Skinning, morphs, node animation and
	/// GPU instancing require a different path and are rejected instead of baked silently.
	/// </summary>
	public static GltfStaticScene Assemble( GltfDocument document )
	{
		ArgumentNullException.ThrowIfNull( document );
		var sceneIndex = document.GetDefaultScene() ?? throw new GltfException( "The asset has no default scene to assemble." );
		var scene = SceneElement( document, "scenes", sceneIndex );
		if ( scene.ValueKind != JsonValueKind.Object ) throw new GltfException( "Scene must be an object." );
		var selected = new List<int>();
		var seen = new HashSet<int>();
		var pending = new Stack<int>();
		if ( scene.TryGetProperty( "nodes", out var roots ) )
		{
			if ( roots.ValueKind != JsonValueKind.Array ) throw new GltfException( "Scene nodes must be an array." );
			foreach ( var root in roots.EnumerateArray().Reverse() ) pending.Push( SceneIndex( root, "scene node" ) );
		}
		while ( pending.Count != 0 )
		{
			var index = pending.Pop();
			if ( !seen.Add( index ) ) throw new GltfException( "A selected scene node is referenced more than once." );
			var node = SceneElement( document, "nodes", index );
			if ( node.ValueKind != JsonValueKind.Object ) throw new GltfException( "Node must be an object." );
			selected.Add( index );
			if ( node.TryGetProperty( "children", out var children ) )
			{
				if ( children.ValueKind != JsonValueKind.Array ) throw new GltfException( "Node children must be an array." );
				foreach ( var child in children.EnumerateArray().Reverse() ) pending.Push( SceneIndex( child, "node child" ) );
			}
		}
		RejectSelectedAnimation( document, seen );
		var output = new List<GltfStaticScenePrimitive>();
		var hasBounds = false;
		var min = NVector3.Zero;
		var max = NVector3.Zero;
		foreach ( var nodeIndex in selected )
		{
			var node = SceneElement( document, "nodes", nodeIndex );
			if ( !node.TryGetProperty( "mesh", out var meshValue ) ) continue;
			if ( node.TryGetProperty( "skin", out _ ) || node.TryGetProperty( "weights", out _ ) ) throw new GltfException( "Static scene assembly does not support skinned or morphed nodes." );
			if ( node.TryGetProperty( "extensions", out var extensions ) && extensions.ValueKind == JsonValueKind.Object && extensions.TryGetProperty( "EXT_mesh_gpu_instancing", out _ ) )
				throw new GltfException( "Static scene assembly does not support GPU instancing." );
			var meshIndex = SceneIndex( meshValue, "node mesh" );
			var mesh = SceneElement( document, "meshes", meshIndex );
			if ( mesh.ValueKind != JsonValueKind.Object ) throw new GltfException( "Mesh must be an object." );
			if ( !mesh.TryGetProperty( "primitives", out var primitives ) || primitives.ValueKind != JsonValueKind.Array ) throw new GltfException( "Mesh primitives must be an array." );
			var transform = GltfGeometry.ConvertTransform( document.GetWorldTransform( nodeIndex ) );
			if ( !NMatrix4x4.Invert( transform, out var inverse ) ) throw new GltfException( "Static scene node has a singular transform." );
			var normalTransform = NMatrix4x4.Transpose( inverse );
			var determinant = transform.GetDeterminant();
			if ( !float.IsFinite( determinant ) || determinant == 0 ) throw new GltfException( "Static scene node has an invalid transform." );
			for ( var primitiveIndex = 0; primitiveIndex < primitives.GetArrayLength(); primitiveIndex++ )
			{
				var source = document.CreateDrawData( meshIndex, primitiveIndex );
				if ( source.HasSkinning || source.HasMorphTargets ) throw new GltfException( "Static scene assembly does not support skinning or morph targets." );
				var positions = source.Positions.ToArray();
				for ( var i = 0; i < positions.Length; i++ )
				{
					positions[i] = NVector3.Transform( positions[i], transform );
					if ( !FiniteSceneVector( positions[i] ) ) throw new GltfException( "Static scene position is not finite." );
					if ( !hasBounds ) { min = max = positions[i]; hasBounds = true; }
					else { min = NVector3.Min( min, positions[i] ); max = NVector3.Max( max, positions[i] ); }
				}
				var normals = source.Normals.ToArray();
				for ( var i = 0; i < normals.Length; i++ ) normals[i] = NormalizeSceneDirection( NVector3.TransformNormal( normals[i], normalTransform ) );
				var tangents = source.Tangents.ToArray();
				for ( var i = 0; i < tangents.Length; i++ )
				{
					var tangent = tangents[i];
					var direction = NormalizeSceneDirection( NVector3.TransformNormal( new NVector3( tangent.X, tangent.Y, tangent.Z ), transform ) );
					tangents[i] = new NVector4( direction, determinant < 0 ? -tangent.W : tangent.W );
				}
				var indices = source.Indices.ToArray();
				if ( determinant < 0 && source.Mode == 4 )
					for ( var i = 0; i < indices.Length; i += 3 ) (indices[i + 1], indices[i + 2]) = (indices[i + 2], indices[i + 1]);
				output.Add( new GltfStaticScenePrimitive { Node = nodeIndex, Mesh = meshIndex, Primitive = primitiveIndex, Data = new GltfDrawData
				{
					Mode = source.Mode, Material = source.Material, Indices = indices, Positions = positions,
					Normals = normals, Tangents = tangents, Colors = source.Colors, TexCoords = source.TexCoords
				} } );
			}
		}
		return new GltfStaticScene { Primitives = output.AsReadOnly(), HasBounds = hasBounds, BoundsMin = min, BoundsMax = max };
	}

	private static void RejectSelectedAnimation( GltfDocument document, HashSet<int> selected )
	{
		if ( !document.Root.TryGetProperty( "animations", out var animations ) ) return;
		if ( animations.ValueKind != JsonValueKind.Array ) throw new GltfException( "Animations must be an array." );
		foreach ( var animation in animations.EnumerateArray() )
		{
			if ( animation.ValueKind != JsonValueKind.Object ) throw new GltfException( "Animation must be an object." );
			if ( !animation.TryGetProperty( "channels", out var channels ) || channels.ValueKind != JsonValueKind.Array ) throw new GltfException( "Animation channels must be an array." );
			foreach ( var channel in channels.EnumerateArray() )
			{
				if ( channel.ValueKind != JsonValueKind.Object ) throw new GltfException( "Animation channel must be an object." );
				if ( !channel.TryGetProperty( "target", out var target ) || target.ValueKind != JsonValueKind.Object ) throw new GltfException( "Animation channel target must be an object." );
				if ( target.TryGetProperty( "node", out var value ) && selected.Contains( SceneIndex( value, "animation target node" ) ) )
					throw new GltfException( "Static scene assembly does not support animated nodes." );
			}
		}
	}

	private static int SceneIndex( JsonElement value, string name )
	{
		if ( value.ValueKind != JsonValueKind.Number || !value.TryGetInt32( out var index ) || index < 0 ) throw new GltfException( $"Invalid {name} index." );
		return index;
	}

	private static JsonElement SceneElement( GltfDocument document, string arrayName, int index )
	{
		if ( !document.Root.TryGetProperty( arrayName, out var array ) || array.ValueKind != JsonValueKind.Array || (uint)index >= (uint)array.GetArrayLength() )
			throw new GltfException( $"Invalid {arrayName} index {index}." );
		return array[index];
	}

	private static bool FiniteSceneVector( NVector3 value ) => float.IsFinite( value.X ) && float.IsFinite( value.Y ) && float.IsFinite( value.Z );
	private static NVector3 NormalizeSceneDirection( NVector3 value )
	{
		var length = value.Length();
		if ( !FiniteSceneVector( value ) || !float.IsFinite( length ) || length <= 0 ) throw new GltfException( "Static scene direction is invalid." );
		return value / length;
	}
}
