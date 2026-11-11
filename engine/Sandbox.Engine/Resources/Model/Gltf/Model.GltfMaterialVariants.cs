namespace Sandbox;

public sealed partial class Model
{
	/// <summary>
	/// Authored glTF material variant names in source index order. Names may
	/// repeat; an index identifies one variant within this resource generation.
	/// Native models and glTF resources without supported variants return an
	/// empty list. The returned collection cannot be mutated.
	/// </summary>
	public IReadOnlyList<string> MaterialVariantNames => GltfResource?.Variants.Names ?? GltfNativeVariants ?? Array.Empty<string>();

	/// <summary>Variant names of a native model compiled from glTF. Each variant is the native material group <c>gltf_variant_{index}</c>.</summary>
	internal IReadOnlyList<string> GltfNativeVariants { get; set; }
}
