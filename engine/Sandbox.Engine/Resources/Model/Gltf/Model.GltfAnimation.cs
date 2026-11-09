namespace Sandbox;

public sealed partial class Model
{
	internal GltfModelAnimation GltfAnimation { get; private set; }

	/// <summary>Transfers ownership after the loader has successfully built the native model.</summary>
	internal void SetGltfAnimation( GltfModelAnimation animation )
	{
		ArgumentNullException.ThrowIfNull( animation );
		if ( GltfAnimation is not null ) throw new InvalidOperationException( "glTF animation metadata has already been assigned." );
		GltfAnimation = animation;
		_animationNames = null;
		_sequenceNames = null;
	}

	internal void ReleaseGltfAnimation()
	{
		GltfAnimation?.Dispose();
		GltfAnimation = null;
	}
}
