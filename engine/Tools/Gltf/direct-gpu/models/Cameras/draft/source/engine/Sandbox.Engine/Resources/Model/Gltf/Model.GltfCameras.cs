namespace Sandbox;

public sealed partial class Model
{
	/// <summary>
	/// Import the selected glTF scene's cameras into a new child hierarchy. This is
	/// an authored rest-pose snapshot; destroying the returned object removes the
	/// imported cameras. Existing objects and cameras are never changed. Cameras
	/// retain the ordinary enabled, main-camera and priority defaults.
	/// </summary>
	/// <param name="parent">Parent and scene receiving the imported hierarchy.</param>
	/// <param name="viewportSize">Presentation size used for authored aspect-ratio
	/// letterboxing. Reimport after changing this size. Null uses the current screen.</param>
	public GameObject ImportGltfCameras( GameObject parent, Vector2? viewportSize = null )
	{
		ArgumentNullException.ThrowIfNull( parent );
		if ( !parent.IsValid() ) throw new ArgumentException( "The camera parent must be valid.", nameof( parent ) );
		var resource = GltfResource ?? throw new InvalidOperationException( "Camera import requires a glTF resource." );
		ObjectDisposedException.ThrowIf( !resource.IsValid, this );
		if ( resource.SelectedSceneIndex is not int selected )
			throw new InvalidOperationException( "Select a glTF scene before importing cameras. The unselected resource remains valid." );
		return Gltf.Cameras.GltfCameraImport.Create( resource.Asset.Document, selected, parent, viewportSize ?? Screen.Size );
	}
}
