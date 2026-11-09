namespace Sandbox;

public sealed partial class SceneModel
{
	private Matrix3x4[][] gltfSkinningPalettes;
	internal void SetGltfSkinningPalettes( Matrix3x4[][] palettes )
	{
		gltfSkinningPalettes = palettes;
		NotifyChanged( Rendering.SceneObjectChange.Bones );
	}

	/// <summary>Final world palettes in the compiled native render-bone order, independently owned by this instance.</summary>
	internal ReadOnlySpan<Matrix3x4> GetGltfSkinningPalette( int mesh ) => Model?.GltfAnimation?.Rig is not null && gltfSkinningPalettes is not null && (uint)mesh < (uint)gltfSkinningPalettes.Length ? gltfSkinningPalettes[mesh] : ReadOnlySpan<Matrix3x4>.Empty;
}
