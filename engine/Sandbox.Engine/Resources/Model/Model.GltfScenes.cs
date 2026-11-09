namespace Sandbox;

public sealed partial class Model
{
	/// <summary>
	/// Load one explicitly selected scene from a glTF asset, preserving the source
	/// default scene. Each scene selection has a separate stable resource identity.
	/// </summary>
	public static Model Load( string filename, int sceneIndex )
	{
		if ( !GltfModelLoader.IsGltfPath( filename ) ) throw new ArgumentException( "Explicit scene selection requires a glTF asset.", nameof( filename ) );
		return GltfModelLoader.Load( filename, sceneIndex );
	}

	/// <summary>Number of authored scenes in this glTF asset, or zero for other model formats.</summary>
	public int GltfSceneCount => GltfResource?.SceneCount ?? 0;

	/// <summary>Authored default scene index, or null if the glTF asset has no default scene.</summary>
	public int? GltfDefaultScene => GltfResource?.DefaultSceneIndex;

	/// <summary>
	/// Selected glTF presentation scene, or null for an unselected glTF asset and
	/// other model formats. An unselected asset is valid; select a scene before rendering.
	/// Mesh count, materials, bounds, bones and morphs describe the selected scene.
	/// </summary>
	public int? GltfSelectedScene => GltfResource?.SelectedSceneIndex;
}
