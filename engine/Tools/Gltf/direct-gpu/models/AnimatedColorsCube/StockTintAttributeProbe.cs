using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Sandbox.Gltf.DirectGpu.Validation;

/// <summary>Native raster calibration only. No source-model, animation or extension claim.</summary>
public static class StockTintAttributeProbe
{
    public static async Task<object> Run(string checkoutRoot)
    {
        await MainThread.Wait();
        if (!Application.IsEditor || Game.IsPlaying || Graphics.IsActive) throw new InvalidOperationException("Requires an idle editor main thread.");
        var output = Path.Combine(checkoutRoot, "Temp/gltf-validation/direct-gpu/AnimatedColorsCube/tint-calibration", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        using var capture = new GltfGpuTriangleGateTests.NativeCapture(output);
        Material red = null, green = null; SceneCustomObject obj = null;
        var errors = new ConcurrentQueue<string>(); var calls = 0; var mode = 0;
        try
        {
            red = Material.Load("materials/default/white.vmat").CreateCopy(); green = red.CreateCopy();
            if (red is not { IsValid: true, IsError: false } || green is not { IsValid: true, IsError: false }) throw new InvalidOperationException("Stock copies unavailable.");
            red.Set("g_vColorTint", new global::Vector4(.9f, .1f, .1f, 1));
            green.Set("g_vColorTint", new global::Vector4(.1f, .9f, .1f, 1));
            var vertices = new[] { V(new global::Vector3(0, 0, 0)), V(new global::Vector3(0, 39, 0)), V(new global::Vector3(0, 0, 39)) };
            var attributes = new RenderAttributes();
            obj = new SceneCustomObject(capture.Scene.SceneWorld) { Bounds = new BBox(new global::Vector3(-1,-1,-1), new global::Vector3(1,40,40)) };
            obj.Flags.IsOpaque = true; obj.Flags.IsTranslucent = false;
            obj.RenderOverride = _ =>
            {
                try
                {
                    var selected = mode == 1 ? green : red;
                    attributes.Clear(); selected.Attributes.MergeTo(attributes); Graphics.Attributes.MergeTo(attributes);
                    if (mode == 2) attributes.Set("g_vColorTint", new global::Vector4(.1f, .9f, .1f, 1));
                    if (mode == 3) attributes.Set("LightSim_MaterialTint", new global::Vector4(.1f, .9f, .1f, 1));
                    Graphics.Draw(vertices.AsSpan(), vertices.Length, selected, attributes); calls++;
                }
                catch (Exception e) { errors.Enqueue(e.ToString()); }
            };
            var redFrame = await capture.Take("red_copy"); mode = 1;
            var greenFrame = await capture.Take("green_copy"); mode = 2;
            var parameterFrame = await capture.Take("red_with_g_vColorTint_attribute"); mode = 3;
            var lightSimFrame = await capture.Take("red_with_LightSim_MaterialTint_attribute"); mode = 0;
            var restored = await capture.Take("red_restored");
            if (errors.Count != 0 || calls == 0 || redFrame.Coverage < 64 || greenFrame.Coverage < 64) throw new InvalidOperationException("Calibration failed: " + string.Join("\n", errors));
            double Difference(GltfGpuTriangleGateTests.NativeCapture.Frame a, GltfGpuTriangleGateTests.NativeCapture.Frame b) =>
                a.Pixels.Zip(b.Pixels, (x,y) => Math.Abs(x.r-y.r)+Math.Abs(x.g-y.g)+Math.Abs(x.b-y.b)).Average();
            var separation = Difference(redFrame, greenFrame); var restore = Difference(redFrame, restored);
            if (separation < .01 || restore > .002) throw new InvalidOperationException("Color positive or restore control failed.");
            var result = new { scope = "native stock material attribute calibration only", output, calls, separation, restore,
                parameterToRed = Difference(parameterFrame, redFrame), parameterToGreen = Difference(parameterFrame, greenFrame),
                lightSimToRed = Difference(lightSimFrame, redFrame), lightSimToGreen = Difference(lightSimFrame, greenFrame),
                red = redFrame.Path, green = greenFrame.Path, parameter = parameterFrame.Path, lightSim = lightSimFrame.Path };
            obj.RenderOverride = null; obj.Delete(); obj = null;
            using (capture.Scene.Push()) capture.Scene.ProcessDeletes();
            var deleted = await capture.Take("deleted");
            if (deleted.Coverage != 0) throw new InvalidOperationException("Deleted calibration geometry remains visible.");
            return result;
        }
        finally { await MainThread.Wait(); if (obj.IsValid()) { obj.RenderOverride = null; obj.Delete(); } green?.Destroy(); red?.Destroy(); }
    }
    private static Vertex V(global::Vector3 p) => new Vertex(p) { Normal = global::Vector3.Forward, Tangent = new global::Vector4(0,1,0,1), Color = new global::Color32(255,255,255,255) };
}

[Microsoft.VisualStudio.TestTools.UnitTesting.TestClass]
public sealed class StockTintAttributeCalibrationTest
{
    private static string checkout;
    public static System.Collections.Generic.List<object> Measurements { get; } = new();
    public static void Configure(string checkoutRoot) { checkout = checkoutRoot; Measurements.Clear(); }
    [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
    public async Task StockTintAttributeCalibration() { Measurements.Add(await StockTintAttributeProbe.Run(checkout)); }
}
