using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Sandbox.Gltf.DirectGpu;

using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

/// <summary>Two matched public poses per form, with explicitly reviewed bounded limits.</summary>
[TestClass]
public sealed class GltfGpuPublicSkinnedCaptureTests
{
	private static string checkout;
	public static List<object> Measurements { get; } = new();
	public static void Configure( string root ) { checkout = root; Measurements.Clear(); }

	[TestMethod]
	public Task PublicSimpleSkinSourceTwoPoses() => Capture( false );

	[TestMethod]
	public Task PublicSimpleSkinCompiledTwoPoses() => Capture( true );

	private static async Task Capture( bool compiled )
	{
		await MainThread.Wait();
		var output = Path.Combine( checkout, "Temp/gltf-validation/direct-gpu/public-model-smokes/SimpleSkin", Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( output );
		GltfGpuTriangleGateTests.Measurements.Clear();
		using var capture = new GltfGpuTriangleGateTests.NativeCapture( output );
		try
		{
			// Reviewed only for these two source-derived poses and matched native views.
			var result = await Validation.SimpleSkinPublicCapture.Run( checkout, capture.Scene, capture.Camera,
				capture.WaitFrames, capture.Take, 1e-4f, 1e-5f, 1, 0, 0, compiled );
			Measurements.Add( new { model = "SimpleSkin", compiled, output, result,
				limits = new { position_inches = 1e-4, normal = 1e-5, minimum_iou = 1, mean_rgb = 0, maximum_rgb = 0 },
				scope = "Public Model.Load and SkinnedModelRenderer, actual GPU output and native images against independent literal geometry. Shared source materials isolate geometry and binding; no independent final material fidelity or wall-clock cadence claim." } );
			Assert.IsTrue( result.Passed, result.Error );
		}
		finally
		{
			Measurements.Add( new { model = "SimpleSkin", compiled,
				retained_native_captures = GltfGpuTriangleGateTests.Measurements.ToArray() } );
		}
	}
}
