namespace Editor;

/// <summary>
/// A glTF thing the editor can select that is not JSON data. Undo keeps the same instance in the selection
/// instead of serializing it (see SceneUndoSnapshot).
/// </summary>
public interface IGltfSelectable
{
	GltfAuthoringDocument Document { get; }
}

/// <summary>One material variant (KHR_materials_variants) as one mesh uses it. Selecting it previews the mesh in that variant.</summary>
public sealed record GltfMeshVariant( GltfElement Mesh, GltfElement Variant ) : IGltfSelectable
{
	public GltfAuthoringDocument Document => Mesh.Document;
	public override string ToString() => $"{Mesh.Name} / {Variant.Name}";
}

/// <summary>One joint of a skin. Selecting it previews the weights of that joint.</summary>
public sealed record GltfJoint( GltfElement Skin, int Joint ) : IGltfSelectable
{
	public GltfAuthoringDocument Document => Skin.Document;

	/// <summary>The node that is this joint, or null if the index is invalid.</summary>
	public GltfElement Node => Skin.Document.GetJointNodes( Skin ) is var nodes && Joint >= 0 && Joint < nodes.Count ? nodes[Joint] : null;

	public override string ToString() => Node?.Name ?? $"Joint {Joint}";
}

/// <summary>The glTF file as a whole. Selecting it (the top row of the hierarchy) shows the settings of the file.</summary>
public sealed record GltfFileSelection( GltfEditorSession Session ) : IGltfSelectable
{
	public GltfAuthoringDocument Document => Session.Document;
	public override string ToString() => System.IO.Path.GetFileName( Session.Asset?.Path ?? "glTF" );
}
