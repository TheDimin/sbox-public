using NativeEngine;

namespace Sandbox;

public static partial class Graphics
{
	/// <summary>
	/// The frame a render block draws into: a frame Borealis draws on Borealis.Gpu (<c>BorealisGpuView</c>).
	/// <see cref="Graphics"/>, command lists, post processing and UI ask it what the camera's view is. A standalone block
	/// (<see cref="Scope.Create"/>) has none.
	/// </summary>
	internal interface IFrameView
	{
		/// <summary>
		/// The camera, as the view's frustum.
		/// </summary>
		CFrustum Frustum { get; }

		/// <summary>
		/// Whether the view has post processing on (<c>ISceneView.GetPostProcessEnabled</c>).
		/// </summary>
		bool PostProcessEnabled { get; }

		/// <summary>
		/// The view's tools visualisation mode (<c>ISceneView.GetToolsVisMode</c>).
		/// </summary>
		int ToolsVisMode { get; }

		/// <summary>
		/// Whether the view is another's child - a reflection or a render to texture from inside a view, which don't render
		/// views of their own.
		/// </summary>
		bool IsChildView { get; }

		/// <summary>
		/// Which face of a cube capture the view draws (its render target array index), or -1.
		/// </summary>
		int CubeFace { get; }

		/// <summary>
		/// Whether the view is a capture other rendering samples rather than a camera's own frame: a cube face, a child view (a
		/// refraction, <c>DrawView</c>, <c>DrawReflection</c>) or a reflection (<see cref="Rendering.CaptureFrames.IsCapture"/>).
		/// </summary>
		bool IsCapture { get; }

		/// <summary>
		/// The colour being drawn into. When <paramref name="owned"/>, it's a strong handle the caller destroys.
		/// </summary>
		ITexture GetColorTarget( out bool owned );

		/// <summary>
		/// The depth being drawn into, like <see cref="GetColorTarget"/>.
		/// </summary>
		ITexture GetDepthTarget( out bool owned );

		/// <summary>
		/// The depth being drawn into as an engine texture, where it is one (a frame on another device than Source 2's, whose
		/// targets have no native handle); null for a native view, which <see cref="GetDepthTarget"/> answers.
		/// </summary>
		Texture DepthTexture { get; }

		/// <summary>
		/// The shader mode <c>RenderTools.DrawModel</c> draws with when it has no native layer to take one from.
		/// </summary>
		StringToken DrawShaderMode { get; }

		/// <summary>
		/// Put an object's own lighting - its light probe volume - into attributes, as
		/// <c>CSceneSystem::SetupPerObjectLighting</c> does from a layer's view.
		/// </summary>
		void SetupLighting( SceneObject obj, RenderAttributes attributes );

		/// <summary>
		/// Draw a scene object with a transform, colour, material override and attributes into what's bound
		/// (<see cref="Graphics.Render(SceneObject, Transform?, Color?, Material)"/>).
		/// </summary>
		void RenderSceneObject( SceneObject obj, Transform transform, Color color, Material material, RenderAttributes attributes );

		/// <summary>
		/// Bind a render target's colour and depth in place of the frame's (<see cref="Graphics.RenderTarget"/>).
		/// </summary>
		void BindTarget( RenderTarget target );

		/// <summary>
		/// Go back to the frame's own targets and viewport, with the depth range the frame had before a render target was
		/// bound.
		/// </summary>
		void RestoreTargets( float minZ, float maxZ );
	}

	/// <summary>
	/// The frame the current render block draws into, or null in a standalone one.
	/// </summary>
	internal static IFrameView CurrentView => _state.view;

	/// <summary>
	/// The temporal view identity for the current managed camera graphics block.
	/// Use both values as the key. Different cameras or ViewSetup.ViewHash
	/// values have different histories, including editor views that borrow a camera
	/// component's command lists. Null outside a managed-camera view.
	/// This does not advance history or imply that a view completed a frame.
	/// </summary>
	public static (int CameraId, int ViewId)? NativeViewIdentity
	{
		get
		{
			return _state.view is BorealisGpuView view ? view.ViewIdentity : null;
		}
	}

	/// <summary>
	/// Scene layer target metadata for the current render callback. Null outside a camera scene
	/// stage with both attachments, in child views, or while Graphics.RenderTarget overrides the targets.
	/// Read at the start of an unmodified main-camera AfterOpaque callback, before target changes.
	/// Match CameraId and ViewId to the intended view; this property does not identify the main camera.
	/// The viewport is current context state. Target sizes describe mip zero of the layer resources,
	/// not a resolved copy or swap chain. Samples is the exact texture sample count (one means no MSAA).
	/// In Borealis, these are the attachments of the scene stage before any resolve.
	/// </summary>
	public static (int CameraId, int ViewId, SceneLayerType Layer, Rect Viewport,
		(int Width, int Height, int Samples) Color, (int Width, int Height, int Samples) Depth)? NativeLayerTargetInfo
	{
		get
		{
			if ( !IsActive || _state.renderTarget is not null || _state.view is not BorealisGpuView view
				|| view.ViewIdentity is not { } identity || view.StageTargetSamples is not { } samples
				|| view.Target?.ColorTarget is not { } color || view.Target.DepthTarget is not { } depth )
				return null;

			return (identity.CameraId, identity.ViewId, LayerType, Viewport,
				(color.Width, color.Height, samples.Color), (depth.Width, depth.Height, samples.Depth));
		}
	}

	/// <summary>
	/// The frame, for what only a view can answer - which a standalone render block has none of.
	/// </summary>
	static IFrameView View => _state.view ?? throw new InvalidOperationException( "A standalone render block has no view." );

	/// <summary>
	/// The shader mode <c>RenderTools.DrawModel</c> draws with when there's no native layer to take it from: a managed
	/// frame's, or none (a material's default) in a standalone render block.
	/// </summary>
	internal static StringToken DrawShaderMode => _state.view?.DrawShaderMode ?? default;

	/// <summary>
	/// The current view's frustum.
	/// </summary>
	internal static CFrustum ViewFrustum => View.Frustum;

	/// <summary>
	/// Whether the current view has post processing on.
	/// </summary>
	internal static bool PostProcessEnabled => View.PostProcessEnabled;

	/// <summary>
	/// The current view's tools visualisation mode.
	/// </summary>
	internal static int ToolsVisMode => View.ToolsVisMode;

	/// <summary>
	/// Whether the current view is another's child: a native child view, or a managed frame rendered inside another.
	/// </summary>
	internal static bool IsChildView => View.IsChildView;

	/// <summary>
	/// Which face of a cube capture the current view draws (<c>SceneCamera.RenderToCubeTexture</c>), or -1.
	/// </summary>
	internal static int CubeFace => View.CubeFace;

	/// <summary>
	/// Whether the current view is a capture other rendering samples rather than a camera's own frame (<see cref="Rendering.CaptureFrames"/>).
	/// </summary>
	internal static bool IsCapture => View.IsCapture;

	/// <summary>
	/// The current frame's colour target. When <paramref name="owned"/>, it's a strong handle the caller destroys.
	/// </summary>
	internal static ITexture GetColorTarget( out bool owned ) => View.GetColorTarget( out owned );

	/// <summary>
	/// The current frame's depth target, like <see cref="GetColorTarget"/>.
	/// </summary>
	internal static ITexture GetDepthTarget( out bool owned ) => View.GetDepthTarget( out owned );

	/// <summary>
	/// The current frame's depth target as an engine texture, where it is one (a frame on Borealis.Gpu); null where
	/// <see cref="GetDepthTarget"/> answers (a native view).
	/// </summary>
	internal static Texture DepthTexture => View.DepthTexture;
}
