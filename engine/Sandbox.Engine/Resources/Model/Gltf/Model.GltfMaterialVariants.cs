namespace Sandbox;

public sealed partial class Model
{
	/// <summary>
	/// Authored glTF material variant names in source index order. Names may
	/// repeat; an index identifies one variant within this resource generation.
	/// Native models and glTF resources without supported variants return an
	/// empty list. The returned collection cannot be mutated.
	/// </summary>
	public IReadOnlyList<string> MaterialVariantNames => GltfResource?.Variants.Names ?? Array.Empty<string>();
}
