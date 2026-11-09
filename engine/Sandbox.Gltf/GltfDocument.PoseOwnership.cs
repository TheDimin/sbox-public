namespace Sandbox.Gltf;

public sealed partial class GltfDocument
{
	/// <summary>
	/// Verify only that a pose was evaluated by this exact document instance.
	/// This does not validate pose contents, geometry, extensions or asset validity,
	/// and does not resolve or decode buffers or allocate staging data.
	/// </summary>
	public void ValidatePoseOwnership( GltfPose pose )
	{
		ArgumentNullException.ThrowIfNull( pose );
		if ( !ReferenceEquals( pose.Owner, this ) ) throw new ArgumentException( "The pose must belong to this document.", nameof( pose ) );
	}
}
