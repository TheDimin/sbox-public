namespace Sandbox.Gltf;

/// <summary>
/// Chooses the level of detail of an MSFT_lod chain from screen coverage. Pure, so it can be tested
/// without a renderer.
/// </summary>
public static class GltfLodSelector
{
	/// <summary>Fraction of a threshold that coverage must pass before the level changes again.</summary>
	public const float Hysteresis = 0.1f;

	/// <summary>
	/// Thresholds when the asset gives none: 0.5, 0.25, 0.125 ... with 0 for the last level, which never hides.
	/// <paramref name="levelCount"/> counts the base node.
	/// </summary>
	public static float[] DefaultThresholds( int levelCount )
	{
		var result = new float[Math.Max( levelCount, 0 )];
		for ( var i = 0; i < result.Length - 1; i++ ) result[i] = MathF.Pow( 0.5f, i + 1 );
		return result;
	}

	/// <summary>
	/// Level 0 is the base node. Level <c>i</c> shows while coverage is at least <c>thresholds[i]</c> and below
	/// <c>thresholds[i-1]</c>. Returns -1 when coverage is below the last threshold (the spec's "nothing rendered").
	/// <paramref name="current"/> adds hysteresis so a level does not flicker at a boundary; use -2 for none.
	/// </summary>
	public static int Select( IReadOnlyList<float> thresholds, float coverage, int current = -2 )
	{
		var next = -1;
		for ( var i = 0; i < thresholds.Count; i++ )
			if ( coverage >= thresholds[i] ) { next = i; break; }
		if ( current < 0 || current >= thresholds.Count || next == current ) return next;
		var staysAbove = coverage >= thresholds[current] * (1 - Hysteresis);
		var staysBelow = current == 0 || coverage < thresholds[current - 1] * (1 + Hysteresis);
		return staysAbove && staysBelow ? current : next;
	}

	/// <summary>
	/// Apply a forced level and a bias to a chosen level. A bias moves toward lower detail when positive.
	/// The result stays inside the chain; a culled level (-1) stays culled.
	/// </summary>
	public static int Adjust( int level, int levelCount, int? forced, int bias )
	{
		if ( levelCount <= 0 ) return -1;
		if ( forced.HasValue ) return Math.Clamp( forced.Value, 0, levelCount - 1 );
		return level < 0 ? -1 : Math.Clamp( level + bias, 0, levelCount - 1 );
	}

	/// <summary>
	/// Screen coverage of a bounding sphere: its radius as a fraction of half the view height at its distance.
	/// <paramref name="verticalFovDegrees"/> is the camera's vertical field of view.
	/// </summary>
	public static float Coverage( float radius, float distance, float verticalFovDegrees )
	{
		if ( radius <= 0 ) return 0;
		var tan = MathF.Tan( Math.Clamp( verticalFovDegrees, 1f, 170f ) * MathF.PI / 360f );
		if ( distance <= radius ) return 1;
		return Math.Clamp( radius / (distance * tan), 0f, 1f );
	}
}
