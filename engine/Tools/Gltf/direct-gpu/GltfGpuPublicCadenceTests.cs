using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Sandbox.Gltf.DirectGpu;

/// <summary>Ordinary public scene ticks driven by observed monotonic time.</summary>
[TestClass]
public sealed class GltfGpuPublicCadenceTests
{
	private static string checkout;
	public static List<object> Measurements { get; } = new();
	public static void Configure( string root ) { checkout = root; Measurements.Clear(); }

	[TestMethod]
	public async Task PublicCubeSourceAdvancesThroughTwoObservedLoops()
	{
		await MainThread.Wait();
		try
		{
			var result = await Validation.AnimatedMorphCubePublicGpuProbe.RunCapturedCadence(
				checkout, Path.Combine( checkout, "Temp/gltf-validation/direct-gpu/AnimatedMorphCube-reference.json" ),
				compiled: false, playbackRate: 1, minimumLoops: 2 );
			Measurements.Add( new { model = "AnimatedMorphCube", result,
				scope = "One actual public source instance, ordinary Scene.EditorTick from measured Stopwatch gaps for two observed loops. No dense movie, compiled-form cadence, performance target or final material fidelity claim." } );
		}
		finally
		{
			Measurements.Add( new { retained_native_captures = GltfGpuTriangleGateTests.Measurements.ToArray() } );
		}
	}
}
