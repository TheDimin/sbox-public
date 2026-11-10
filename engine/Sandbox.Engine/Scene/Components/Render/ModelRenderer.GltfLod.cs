using Sandbox.Gltf.DirectGpu;

namespace Sandbox;

public partial class ModelRenderer
{
	private int? gltfLodLevel;
	private int gltfLodBias;

	/// <summary>
	/// For a glTF model with MSFT_lod chains, show this level of detail of every chain (0 is the most detailed)
	/// instead of choosing from screen coverage. Null chooses automatically. A level past the end of a chain shows its last level.
	/// </summary>
	public int? GltfLodLevel
	{
		get => gltfLodLevel;
		set { gltfLodLevel = value; ApplyGltfLod(); }
	}

	/// <summary>
	/// For a glTF model with MSFT_lod chains, move the automatic choice toward lower detail (positive) or higher detail (negative) by this many levels.
	/// </summary>
	public int GltfLodBias
	{
		get => gltfLodBias;
		set { gltfLodBias = value; ApplyGltfLod(); }
	}

	private void ApplyGltfLod() => gltfInstance?.SetLodOverride( gltfLodLevel, gltfLodBias );
}
