namespace Sandbox;

public sealed partial class SceneCamera
{
	/// <summary>
	/// Flip the final colour image horizontally. Screen queries follow this
	/// orientation; native depth and custom projection matrices stay unflipped.
	/// </summary>
	public bool FlipX { get; set; }

	/// <summary>Flip the final colour image vertically. See <see cref="FlipX"/>.</summary>
	public bool FlipY { get; set; }

	internal Matrix ScreenProjectionMatrix
	{
		get
		{
			var matrix = CustomProjectionMatrix ?? ProjectionMatrix;
			if ( !FlipX && !FlipY ) return matrix;
			System.Numerics.Matrix4x4 result = matrix;
			// Native projection matrices are column-vector matrices. Compose
			// a clip-space reflection on the left, without modifying depth.
			if ( FlipX ) { result.M11 = -result.M11; result.M12 = -result.M12; result.M13 = -result.M13; result.M14 = -result.M14; }
			if ( FlipY ) { result.M21 = -result.M21; result.M22 = -result.M22; result.M23 = -result.M23; result.M24 = -result.M24; }
			return result;
		}
	}

	private Vector2 ApplyScreenFlips( Vector2 point, Vector2 size )
	{
		if ( FlipX ) point.x = size.x - point.x;
		if ( FlipY ) point.y = size.y - point.y;
		return point;
	}
}
