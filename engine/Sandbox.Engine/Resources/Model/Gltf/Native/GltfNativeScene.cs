namespace Sandbox;

/// <summary>
/// A static glTF scene compiled to native models: shared geometry models and the nodes that place them. Its
/// <see cref="Model"/> draws nothing itself; a <see cref="ModelRenderer"/> draws one scene object per node.
/// </summary>
internal sealed class GltfNativeScene( Model[] geometries, GltfNativeScene.Node[] nodes, BBox bounds, Material[] materials )
{
	/// <summary>A placed geometry: its transform relative to the model, its material group, and its group per glTF variant.</summary>
	internal sealed record Node( int Index, int Geometry, Transform Transform, string Group, string[] VariantGroups );

	internal Model[] Geometries { get; } = geometries;
	internal Node[] Nodes { get; } = nodes;
	internal BBox Bounds { get; } = bounds;
	internal Material[] Materials { get; } = materials;

	internal void Destroy()
	{
		foreach ( var geometry in Geometries ) geometry.Destroy();
	}
}
