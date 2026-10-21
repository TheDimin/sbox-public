namespace Sandbox.Clutter;

/// <summary>
/// Optional interaction fields for anchor-based clutter coverage. XYZ holds displacement
/// components in world inches. Regions contain XY origin and Z reciprocal world coverage.
/// These borrowed textures remain owned by the caller. Null textures disable that frame's field.
/// </summary>
public sealed class ClutterCoverageContactField
{
	public Texture CurrentTexture { get; set; }
	public Texture PreviousTexture { get; set; }
	public Vector4 CurrentRegion { get; set; }
	public Vector4 PreviousRegion { get; set; }
	/// <summary>Material's current local-Z height multiplier, supported from 0.25 through 1.</summary>
	public float HeightScale { get; set; } = 1f;
}

public sealed partial class ClutterGridSystem
{
	/// <summary>
	/// Runtime-only anchor coverage for selected painted models. Materials must expose
	/// ClutterAnchorHeight, ClutterAnchorWidth and ClutterAnchorWindAngle and consume instance Alpha.
	/// The density value selects guaranteed retained anchors, not a final instance fraction.
	/// Unsupported materials and uncertain overlap retain all placements.
	/// </summary>
	public HashSet<string> PaintedLod2AnchorModels { get; } = new( StringComparer.OrdinalIgnoreCase );
	/// <summary>Optional borrowed contact fields. Replace or clear only the value owned by your caller.</summary>
	public ClutterCoverageContactField PaintedLod2ContactField { get; set; }
	internal bool PaintedAnchorCameraValid { get; private set; }
	internal Matrix PaintedDrawWorldToProjection { get; private set; }
	internal Vector3 PaintedDrawCameraRight { get; private set; }
	internal Vector3 PaintedDrawCameraUp { get; private set; }
}
