using System;

namespace Sandbox.Rendering;

/// <summary>
/// Narrow type-library contract for optional native vegetation motion drawing.
/// Games can bind by name without a compile-time dependency on engine extensions.
/// </summary>
[Expose]
public static class VegetationMotionVectorApi
{
	/// <summary>
	/// Records a callback in the command list. The callback runs on the render
	/// worker for each replay and must not modify scene or material state.
	/// </summary>
	public static void AddAction( CommandList commands, Action action )
	{
		ArgumentNullException.ThrowIfNull( commands );
		ArgumentNullException.ThrowIfNull( action );
		commands.AddAction( action );
	}

	/// <summary>Records the existing scene object draw without replacing its render path.</summary>
	public static void DrawSceneObject( CommandList commands, SceneObject sceneObject )
	{
		ArgumentNullException.ThrowIfNull( commands );
		ArgumentNullException.ThrowIfNull( sceneObject );
		commands.DrawSceneObject( sceneObject );
	}

	/// <summary>The native camera and temporal view identity, or null outside rendering.</summary>
	public static (int CameraId, int ViewId)? NativeViewIdentity => Graphics.NativeViewIdentity;
}
