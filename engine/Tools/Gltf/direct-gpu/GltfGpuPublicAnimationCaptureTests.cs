using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Sandbox.Gltf.DirectGpu;

/// <summary>Actual public component captures; numerical and material acceptance remain separate.</summary>
[TestClass]
public sealed class GltfGpuPublicAnimationCaptureTests
{
	private static string checkout;
	public static List<object> Measurements { get; } = new();
	public static void Configure( string root ) { checkout = root; Measurements.Clear(); }

	[TestMethod]
	public Task PublicCubeSourceThreeAuthoredKeys() => Capture( false );

	[TestMethod]
	public Task PublicCubeCompiledThreeAuthoredKeys() => Capture( true );

	private static async Task Capture( bool compiled )
	{
		await MainThread.Wait();
		try
		{
			var result = await Validation.AnimatedMorphCubePublicGpuProbe.RunCapturedAuthored(
				checkout, Path.Combine( checkout, "Temp/gltf-validation/direct-gpu/AnimatedMorphCube-reference.json" ), compiled );
			Measurements.Add( new { model = "AnimatedMorphCube", compiled, result,
				scope = "Public Model.Load and SkinnedModelRenderer, named compiled deformation shader, three authored poses and actual GPU/native capture controls. Numeric limits, independent matched images, source material fidelity and wall-clock cadence remain pending." } );
		}
		finally
		{
			Measurements.Add( new { model = "AnimatedMorphCube", compiled,
				retained_native_captures = GltfGpuTriangleGateTests.Measurements.ToArray() } );
		}
	}
}
