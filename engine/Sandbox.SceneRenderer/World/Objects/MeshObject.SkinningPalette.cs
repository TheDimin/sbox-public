namespace Sandbox.SceneRenderer;

public sealed partial class MeshObject
{
	Dictionary<int, Matrix3x4[]> skinningPalettes;

	/// <summary>
	/// Override a model mesh's skin matrices in its native render-bone order.
	/// These are final world-space matrices, including inverse binds and object
	/// placement. They bypass the legacy bone/remap/inverse-bind composition.
	/// The input is copied and remains instance-local. Submit before preparation
	/// and update bounds separately; this does not pose the native SceneModel.
	/// </summary>
	internal void SetSkinningPalette( int modelMesh, ReadOnlySpan<Matrix3x4> worldPalette )
	{
		if ( mesh is null || (uint)modelMesh >= (uint)mesh.ModelMeshCount )
			throw new ArgumentOutOfRangeException( nameof( modelMesh ) );
		var skin = mesh.SkinFor( modelMesh );
		if ( skin is null || skin.BoneCount == 0 )
			throw new InvalidOperationException( "The model mesh has no skinning palette." );
		if ( skin.BlendWeightCount <= 0 )
			throw new InvalidOperationException( "Direct skinning palettes require a weighted vertex layout." );
		if ( worldPalette.Length != skin.BoneCount )
			throw new ArgumentException( "The palette must contain one matrix per render bone.", nameof( worldPalette ) );

		// Preflight the entire input before mutating a previous valid palette.
		foreach ( var matrix in worldPalette )
			if ( !Finite( matrix.Row0 ) || !Finite( matrix.Row1 ) || !Finite( matrix.Row2 ) )
				throw new ArgumentException( "Skinning matrices must be finite.", nameof( worldPalette ) );

		skinningPalettes ??= new();
		if ( !skinningPalettes.TryGetValue( modelMesh, out var destination ) || destination.Length != worldPalette.Length )
		{
			destination = new Matrix3x4[worldPalette.Length];
			skinningPalettes[modelMesh] = destination;
		}
		worldPalette.CopyTo( destination );
		SkinGeneration = -1;
	}

	/// <summary>
	/// Restore the mesh's legacy bone or bind-pose composition. Other mesh overrides
	/// remain unchanged. SetBones may update that fallback while an override is active.
	/// </summary>
	internal void ClearSkinningPalette( int modelMesh )
	{
		if ( modelMesh < 0 ) throw new ArgumentOutOfRangeException( nameof( modelMesh ) );
		if ( skinningPalettes?.Remove( modelMesh ) == true ) SkinGeneration = -1;
	}

	/// <summary>Final matrices for this instance and model mesh, or empty for the legacy path.</summary>
	internal ReadOnlySpan<Matrix3x4> SkinningPaletteFor( int modelMesh ) =>
		skinningPalettes is not null && skinningPalettes.TryGetValue( modelMesh, out var palette ) ? palette : default;

	void ClearSkinningPalettes()
	{
		if ( skinningPalettes is null || skinningPalettes.Count == 0 ) return;
		skinningPalettes.Clear();
		SkinGeneration = -1;
	}

	static bool Finite( Vector4 value ) => float.IsFinite( value.x ) && float.IsFinite( value.y ) && float.IsFinite( value.z ) && float.IsFinite( value.w );
}
