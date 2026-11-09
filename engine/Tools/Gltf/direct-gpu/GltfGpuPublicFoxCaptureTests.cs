using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Sandbox.Gltf.DirectGpu;

using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

/// <summary>Executes the frozen public Fox capture; numeric errors remain measurements.</summary>
[TestClass]
public sealed class GltfGpuPublicFoxCaptureTests
{
	private static string checkout;
	public static List<object> Measurements { get; } = new();
	public static void Configure( string root ) { checkout = root; Measurements.Clear(); }

	[TestMethod]
	public Task PublicFoxSourceCapturesThreeClipsAndIndependentInstances() => Capture( compiled: false );

	internal static async Task Capture( bool compiled )
	{
		await MainThread.Wait();
		const string sourceHash = "43101e5f4c4ae9727f5a21c2aecc6e391bf16b303099395a703babb490006e47";
		const string compiledHash = "f15a8f245dd6d9f0293ee895c54a5f46a1b41eb094140153f1a5f7539178e5fc";
		Assert.AreEqual( sourceHash, Hash( Path.Combine( checkout, "game/core/shaders/gltf_deform_cs.shader" ) ) );
		Assert.AreEqual( compiledHash, Hash( Path.Combine( checkout, "game/core/shaders/gltf_deform_cs.shader_c" ) ) );
		try
		{
			var result = await Tests.FoxPublicCapture.RunCaptured( checkout, compiled );
			var renderer = AppDomain.CurrentDomain.GetAssemblies().Single( assembly => assembly.GetName().Name == "Sandbox.SceneRenderer" );
			Measurements.Add( new { model = "Fox", result, actual_native_renderer_mvid = renderer.ManifestModule.ModuleVersionId,
				compute_source_sha256 = sourceHash, compute_compiled_sha256 = compiledHash,
				scope = "Public Model.Load/SkinnedModelRenderer sampled poses, actual GPU readbacks, motion, instance isolation and native empty/deletion controls. Numeric errors measured; original CPU precision failures retained. Material fidelity and real-time cadence remain separate." } );
			Assert.IsTrue( result.Passed, result.Error );
		}
		finally
		{
			Measurements.Add( new { retained_native_captures = GltfGpuTriangleGateTests.Measurements.ToArray() } );
		}
	}

	private static string Hash( string path ) => Convert.ToHexStringLower( SHA256.HashData( File.ReadAllBytes( path ) ) );
}

[TestClass]
public sealed class GltfGpuPublicFoxCompiledCaptureTests
{
	public static List<object> Measurements => GltfGpuPublicFoxCaptureTests.Measurements;
	public static void Configure( string root ) => GltfGpuPublicFoxCaptureTests.Configure( root );

	[TestMethod]
	public Task PublicFoxCompiledCapturesThreeClipsAndIndependentInstances() => GltfGpuPublicFoxCaptureTests.Capture( compiled: true );
}
