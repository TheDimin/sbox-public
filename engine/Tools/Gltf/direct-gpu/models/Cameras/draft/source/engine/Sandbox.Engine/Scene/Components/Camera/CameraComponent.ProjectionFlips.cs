using System.Text.Json.Serialization;

namespace Sandbox;

public sealed partial class CameraComponent
{
	/// <summary>
	/// Flip the rendered colour image horizontally. Screen queries follow this
	/// orientation; depth and native projection overrides retain their base orientation.
	/// A ViewSetup flip explicitly overrides this default for that submission only.
	/// </summary>
	[Property]
	public bool FlipX { get; set; }

	/// <summary>Flip the rendered colour image vertically. See <see cref="FlipX"/>.</summary>
	[Property]
	public bool FlipY { get; set; }

	/// <summary>
	/// The resolved custom or ordinary projection, composed with this camera's
	/// persistent image flips. Use <see cref="ProjectionMatrix"/> for the unflipped
	/// native/oblique path; feeding this matrix back into rendering would apply
	/// persistent flips twice. Temporary ViewSetup overrides do not change this value.
	/// </summary>
	[JsonIgnore, Hide]
	public Matrix ScreenProjectionMatrix
	{
		get
		{
			EnsureSceneCameraCreated();
			UpdateSceneCameraTransform( sceneCamera );
			return sceneCamera.ScreenProjectionMatrix;
		}
	}
}
