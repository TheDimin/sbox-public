using System;
using System.Collections.Generic;
using NMatrix = System.Numerics.Matrix4x4;

namespace Sandbox.Gltf;

/// <summary>
/// Immutable skin definition used to produce independent full-affine skinning palettes.
/// Matrix values use the s&amp;box coordinate basis and System.Numerics row-vector order.
/// This is CPU staging; it does not establish GPU or native model integration.
/// </summary>
public sealed class GltfSkinningPalette
{
	private readonly int[] joints;
	private readonly NMatrix[] inverseBinds;

	/// <summary>Snapshot joint order and inverse binds from the decoded skin.</summary>
	public GltfSkinningPalette( GltfSkin skin )
	{
		ArgumentNullException.ThrowIfNull( skin );
		if ( skin.Joints is null || skin.InverseBindMatrices is null || skin.Joints.Length == 0 || skin.Joints.Length != skin.InverseBindMatrices.Length )
			throw new GltfException( "Skin joints and inverse bind matrices must have the same nonzero count." );
		joints = (int[])skin.Joints.Clone();
		inverseBinds = (NMatrix[])skin.InverseBindMatrices.Clone();
		var unique = new HashSet<int>();
		for ( var i = 0; i < joints.Length; i++ )
		{
			if ( joints[i] < 0 || !unique.Add( joints[i] ) ) throw new GltfException( "Skin joint indices must be nonnegative and unique." );
			RequireAffine( inverseBinds[i], "inverse bind" );
		}
	}

	/// <summary>Number of entries, in the skin's original joint order.</summary>
	public int JointCount => joints.Length;

	/// <summary>
	/// Evaluate a pose belonging to the same glTF document as the skin. The optional
	/// object-to-world matrix is already in s&amp;box coordinates and is applied once
	/// after glTF's skin transform; the skinned mesh node transform is not applied.
	/// </summary>
	public NMatrix[] Evaluate( GltfPose pose, NMatrix? objectToWorld = null )
	{
		ArgumentNullException.ThrowIfNull( pose );
		var result = new NMatrix[joints.Length];
		Write( pose, result, objectToWorld );
		return result;
	}

	/// <summary>
	/// Write into a reusable caller-owned destination of at least <see cref="JointCount"/>
	/// matrices. Entries after that count remain unchanged. All inputs and resulting
	/// matrices are checked before the destination is touched, so validation failure
	/// leaves it unchanged. The pose is immutable and may be shared across readers.
	/// </summary>
	public void Write( GltfPose pose, Span<NMatrix> destination, NMatrix? objectToWorld = null )
	{
		ArgumentNullException.ThrowIfNull( pose );
		if ( destination.Length < joints.Length ) throw new ArgumentException( "Palette destination is shorter than the joint count.", nameof( destination ) );
		var placement = objectToWorld ?? NMatrix.Identity;
		RequireAffine( placement, "object-to-world" );
		// Preflight all entries before the first write. GltfPose stores immutable world
		// matrices, so a second direct pass avoids a temporary array or delegate.
		for ( var i = 0; i < joints.Length; i++ ) _ = Compose( inverseBinds[i], pose.GetWorldTransform( joints[i] ), placement );
		for ( var i = 0; i < joints.Length; i++ ) destination[i] = Compose( inverseBinds[i], pose.GetWorldTransform( joints[i] ), placement );
	}

	/// <summary>
	/// Evaluate with a caller-supplied glTF-space joint scene transform provider.
	/// Callers must supply joint transforms for the document from which the skin was decoded.
	/// The returned array is owned by the caller and is not shared across instances.
	/// </summary>
	public NMatrix[] Evaluate( Func<int, NMatrix> jointSceneTransform, NMatrix? objectToWorld = null )
	{
		ArgumentNullException.ThrowIfNull( jointSceneTransform );
		var placement = objectToWorld ?? NMatrix.Identity;
		RequireAffine( placement, "object-to-world" );
		var result = new NMatrix[joints.Length];
		for ( var i = 0; i < joints.Length; i++ )
		{
			var joint = jointSceneTransform( joints[i] );
			result[i] = Compose( inverseBinds[i], joint, placement );
		}
		return result;
	}

	private static NMatrix Compose( NMatrix inverseBind, NMatrix joint, NMatrix placement )
	{
		RequireAffine( joint, "joint transform" );
		// glTF row vectors: apply inverse bind, then posed joint world, then
		// change basis and finally apply the scene object's placement.
		var matrix = GltfGeometry.ConvertTransform( inverseBind * joint ) * placement;
		RequireAffine( matrix, "skin palette" );
		return matrix;
	}

	private static void RequireAffine( NMatrix matrix, string description )
	{
		if ( !float.IsFinite( matrix.M11 ) || !float.IsFinite( matrix.M12 ) || !float.IsFinite( matrix.M13 ) || !float.IsFinite( matrix.M14 ) ||
			!float.IsFinite( matrix.M21 ) || !float.IsFinite( matrix.M22 ) || !float.IsFinite( matrix.M23 ) || !float.IsFinite( matrix.M24 ) ||
			!float.IsFinite( matrix.M31 ) || !float.IsFinite( matrix.M32 ) || !float.IsFinite( matrix.M33 ) || !float.IsFinite( matrix.M34 ) ||
			!float.IsFinite( matrix.M41 ) || !float.IsFinite( matrix.M42 ) || !float.IsFinite( matrix.M43 ) || !float.IsFinite( matrix.M44 ) ||
			matrix.M14 != 0 || matrix.M24 != 0 || matrix.M34 != 0 || matrix.M44 != 1 )
			throw new GltfException( $"Invalid non-affine or non-finite {description} matrix." );
	}
}
