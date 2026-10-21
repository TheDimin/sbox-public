using Sandbox.Rendering;

namespace Sandbox.Clutter;

/// <summary>
/// Creates optional draw preparation owned by one painted-clutter batch command path.
/// Return null for unsupported models or LODs. Providers must not own or dispose the visible transforms.
/// </summary>
public interface IClutterDrawPreparationProvider
{
    IClutterDrawPreparation Create( Model model, int lod, int capacity, GpuBuffer<GpuInstanceTransform> visibleTransforms );
}

/// <summary>
/// Resources for one retained draw command path. The batch resets all command lists before disposal.
/// Record commands only; do not mutate bound attribute objects after recording. Commands execute for
/// every view replay. Main and shadow paths receive separate instances from the provider.
/// </summary>
public interface IClutterDrawPreparation : IDisposable
{
    /// <summary>Record preparation after culling and the visible-transform read transition.</summary>
    void RecordBeforeDraw( CommandList commands );

    /// <summary>Record removal of draw-local bindings before the next LOD.</summary>
    void RecordAfterDraw( CommandList commands );

    /// <summary>Clear transient draw state even when command playback throws. Runs in the render block.
    /// This must not access owned GPU resources and must remain safe after Dispose.</summary>
    void ResetDrawState();
}
