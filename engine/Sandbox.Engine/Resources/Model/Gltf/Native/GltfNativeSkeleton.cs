using System.Runtime.InteropServices;
using Sandbox.Gltf;
using Sandbox.Gltf.DirectGpu;

namespace Sandbox;

using NMatrix = System.Numerics.Matrix4x4;

/// <summary>
/// The native skeleton of a skinned or animated glTF: one bone per scene node, in parent-first order.
/// <para>
/// Native skinning computes <c>pose * inverse(bind)</c> from bind bones that have no scale. With every bind bone set to
/// the glTF bind (a joint's inverse bind matrix inverted, or a node's rest world), both expressed in engine space, the
/// native result equals the glTF one exactly: skinned vertices are stored in engine space, and a rigid mesh is stored
/// as <c>vertex * Basis * bind</c> and weighted fully to its node's bone. This needs every bind to have unit scale;
/// a scaled bind stops the compile with <see cref="GltfNativeUnsupportedException"/>.
/// </para>
/// Clips are sampled at a fixed rate into parent-local transforms. Native frames keep scale, so animated scale works.
/// </summary>
internal sealed class GltfNativeSkeleton
{
	internal const float FrameRate = 30;
	private const float ScaleTolerance = 1e-3f;

	internal GltfNativePackBone[] Bones { get; private init; }
	private int[] boneOfNode;
	private int[] parentOfBone;
	private int[] nodeOfBone;
	private NMatrix[] bindWorld;
	private readonly Dictionary<int, int[]> skinBones = new();

	internal int BoneOf( int node ) => (uint)node < (uint)boneOfNode.Length ? boneOfNode[node] : -1;

	/// <summary>Engine space: the engine-unit form of a glTF transform.</summary>
	internal static NMatrix Engine( NMatrix gltf )
	{
		NMatrix.Invert( GltfGpuRig.Basis, out var inverse );
		return inverse * gltf * GltfGpuRig.Basis;
	}

	/// <summary>The matrix a row's vertices are stored with: engine space for a skinned mesh, its node's bind for a rigid one.</summary>
	internal NMatrix VertexMatrix( int node, bool skinned ) => skinned ? GltfGpuRig.Basis : GltfGpuRig.Basis * bindWorld[boneOfNode[node]];

	/// <summary>Native bone of each joint of a skin.</summary>
	internal int[] SkinBones( int skin ) => skinBones[skin];

	internal static GltfNativeSkeleton Create( GltfDocument document, IReadOnlyList<int> nodes, GltfPose rest )
	{
		var root = document.Root;
		var nodeCount = root.GetProperty( "nodes" ).GetArrayLength();
		var parents = Enumerable.Repeat( -1, nodeCount ).ToArray();
		for ( var i = 0; i < nodeCount; i++ )
			if ( root.GetProperty( "nodes" )[i].TryGetProperty( "children", out var children ) )
				foreach ( var child in children.EnumerateArray() ) parents[child.GetInt32()] = i;

		// A joint binds where its inverse bind matrix says; every other node binds at its rest world.
		var binds = new Dictionary<int, NMatrix>();
		var skinCount = root.TryGetProperty( "skins", out var skins ) ? skins.GetArrayLength() : 0;
		for ( var skin = 0; skin < skinCount; skin++ )
		{
			var data = document.ReadSkin( skin );
			for ( var joint = 0; joint < data.Joints.Length; joint++ )
			{
				if ( !NMatrix.Invert( data.InverseBindMatrices[joint], out var bind ) ) throw new GltfNativeUnsupportedException( $"Skin {skin} joint {joint} has a singular inverse bind matrix." );
				if ( binds.TryGetValue( data.Joints[joint], out var existing ) && !Near( existing, bind ) )
					throw new GltfNativeUnsupportedException( $"Node {data.Joints[joint]} has different bind matrices in two skins; native bones have one bind." );
				binds[data.Joints[joint]] = bind;
			}
		}

		var order = nodes.ToList();
		foreach ( var joint in binds.Keys ) if ( !order.Contains( joint ) ) order.Add( joint );
		// Parents first, so each bone's parent already exists.
		var depth = new Func<int, int>( n => { var d = 0; for ( var p = parents[n]; p >= 0; p = parents[p] ) d++; return d; } );
		order = order.Distinct().OrderBy( depth ).ThenBy( order.IndexOf ).ToList();

		var skeleton = new GltfNativeSkeleton
		{
			Bones = new GltfNativePackBone[order.Count],
			boneOfNode = Enumerable.Repeat( -1, nodeCount ).ToArray(),
			parentOfBone = new int[order.Count],
			nodeOfBone = order.ToArray(),
			bindWorld = new NMatrix[order.Count]
		};
		var names = new HashSet<string>( StringComparer.Ordinal );
		for ( var bone = 0; bone < order.Count; bone++ ) skeleton.boneOfNode[order[bone]] = bone;
		for ( var bone = 0; bone < order.Count; bone++ )
		{
			var node = order[bone];
			var world = Engine( binds.TryGetValue( node, out var bind ) ? bind : rest.GetWorldTransform( node ) );
			RequireUnitScale( world, $"Node {node} binds with scale; native bones have no scale." );
			skeleton.bindWorld[bone] = world;
			var parent = parents[node] >= 0 ? skeleton.boneOfNode[parents[node]] : -1;
			skeleton.parentOfBone[bone] = parent;
			NMatrix local = world;
			if ( parent >= 0 ) { NMatrix.Invert( skeleton.bindWorld[parent], out var inverse ); local = world * inverse; }
			if ( !NMatrix.Decompose( local, out _, out var rotation, out var position ) ) throw new GltfNativeUnsupportedException( $"Node {node} has a bind transform with shear." );
			var json = document.Root.GetProperty( "nodes" )[node];
			var name = json.TryGetProperty( "name", out var value ) && value.GetString() is { Length: > 0 } text ? text : $"node_{node}";
			for ( var suffix = 1; !names.Add( name ); suffix++ ) name = $"{(json.TryGetProperty( "name", out var n ) ? n.GetString() : "node")}_{node}_{suffix}";
			skeleton.Bones[bone] = new GltfNativePackBone( name, parent, [position.X, position.Y, position.Z], [rotation.X, rotation.Y, rotation.Z, rotation.W] );
		}
		for ( var skin = 0; skin < skinCount; skin++ )
			skeleton.skinBones[skin] = document.ReadSkin( skin ).Joints.Select( joint => skeleton.boneOfNode[joint] ).ToArray();
		return skeleton;
	}

	/// <summary>Sample every clip into native frames: per frame, one parent-local transform per bone, in bone order.</summary>
	internal GltfNativePackAnimation[] SampleAnimations( GltfDocument document, Func<byte[], int> addBlob )
	{
		var result = new List<GltfNativePackAnimation>();
		foreach ( var clip in GltfModelAnimation.ReadClips( document ) )
		{
			var frames = Math.Max( 1, (int)MathF.Ceiling( clip.Duration * FrameRate ) + 1 );
			var transforms = new Transform[frames * Bones.Length];
			for ( var frame = 0; frame < frames; frame++ )
			{
				var pose = document.EvaluatePose( clip.Index, MathF.Min( frame / FrameRate, clip.Duration ) );
				for ( var bone = 0; bone < Bones.Length; bone++ )
				{
					var world = Engine( pose.GetWorldTransform( nodeOfBone[bone] ) );
					var local = world;
					if ( parentOfBone[bone] >= 0 )
					{
						NMatrix.Invert( Engine( pose.GetWorldTransform( nodeOfBone[parentOfBone[bone]] ) ), out var inverse );
						local = world * inverse;
					}
					if ( !NMatrix.Decompose( local, out var scale, out var rotation, out var position ) )
						throw new GltfNativeUnsupportedException( $"Animation '{clip.Name}' poses node {nodeOfBone[bone]} with shear; native frames have no shear." );
					transforms[frame * Bones.Length + bone] = new Transform( position, rotation, scale );
				}
			}
			var blob = addBlob( MemoryMarshal.AsBytes( transforms.AsSpan() ).ToArray() );
			result.Add( new GltfNativePackAnimation( clip.Name, FrameRate, true, frames, Bones.Length, new GltfNativeBlob( blob, 0 ) ) );
		}
		return result.ToArray();
	}

	private static void RequireUnitScale( NMatrix matrix, string message )
	{
		var x = new System.Numerics.Vector3( matrix.M11, matrix.M12, matrix.M13 ).Length();
		var y = new System.Numerics.Vector3( matrix.M21, matrix.M22, matrix.M23 ).Length();
		var z = new System.Numerics.Vector3( matrix.M31, matrix.M32, matrix.M33 ).Length();
		if ( MathF.Abs( x - 1 ) > ScaleTolerance || MathF.Abs( y - 1 ) > ScaleTolerance || MathF.Abs( z - 1 ) > ScaleTolerance || matrix.GetDeterminant() < 0 )
			throw new GltfNativeUnsupportedException( message );
	}

	private static bool Near( NMatrix a, NMatrix b )
	{
		for ( var row = 0; row < 4; row++ )
			for ( var column = 0; column < 4; column++ )
				if ( MathF.Abs( a[row, column] - b[row, column] ) > 1e-4f * MathF.Max( 1, MathF.Abs( a[row, column] ) ) ) return false;
		return true;
	}
}
