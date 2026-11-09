using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Sandbox.Gltf.DirectGpu;

using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

/// <summary>Actual public movie capture; numeric acceptance and lossless encoding follow separately.</summary>
[TestClass]
public sealed class GltfGpuPublicMovieTests
{
	private static string checkout;
	public static List<object> Measurements { get; } = new();
	public static void Configure( string root ) { checkout = root; Measurements.Clear(); }

	[TestMethod]
	public async Task PublicCubeSourceCapturesAll252RequestedStates()
	{
		await MainThread.Wait();
		const string shaderSource = "43101e5f4c4ae9727f5a21c2aecc6e391bf16b303099395a703babb490006e47";
		Assert.AreEqual( shaderSource, Hash( Path.Combine( checkout, "game/core/shaders/gltf_deform_cs.shader" ) ) );
		Assert.AreEqual( "f15a8f245dd6d9f0293ee895c54a5f46a1b41eb094140153f1a5f7539178e5fc",
			Hash( Path.Combine( checkout, "game/core/shaders/gltf_deform_cs.shader_c" ) ) );
		try
		{
			var result = await Validation.AnimatedMorphCubePublicGpuProbe.RunCapturedMovie( checkout,
				Path.Combine( checkout, "Temp/gltf-validation/direct-gpu/AnimatedMorphCube-reference.json" ), shaderSource,
				compiled: false, configureCapture: capture => GltfGpuPublicLightingCalibrationTests.ConfigureOwnedLight( capture, 4 ) );
			var renderer = AppDomain.CurrentDomain.GetAssemblies().Single( assembly => assembly.GetName().Name == "Sandbox.SceneRenderer" );
			Measurements.Add( new { model = "AnimatedMorphCube", result, actual_native_renderer_mvid = renderer.ManifestModule.ModuleVersionId,
				selected_point_light_intensity = 4, calibration_receipt_sha256 = "df4e6ab96677f0c6100dc86059725d6a265a49332c1db5fa6cb65f1f4429bd91",
				scope = "252 actual public component captures, prescribed full Scene.EditorTick deltas, current GPU buffer files and separate endpoint. Numerical acceptance and APNG packaging remain separate; actual wall-clock playback has its own receipt." } );
		}
		finally
		{
			Measurements.Add( new { retained_native_captures = GltfGpuTriangleGateTests.Measurements.ToArray() } );
		}
	}

	private static string Hash( string path ) => Convert.ToHexStringLower( SHA256.HashData( File.ReadAllBytes( path ) ) );
}
