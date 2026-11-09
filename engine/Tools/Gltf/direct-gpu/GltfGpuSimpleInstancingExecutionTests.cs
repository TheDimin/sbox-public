using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Sandbox.Gltf.DirectGpu;

using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;
using Instancing = SimpleInstancingValidation.SimpleInstancingReference;
using Checks = SimpleInstancingValidation.SimpleInstancingGpuChecks;
using Probe = SimpleInstancingValidation.SimpleInstancingGpuProbe;
using Result = SimpleInstancingValidation.SimpleInstancingCoreTests.Result;
using Matrix = System.Numerics.Matrix4x4;

/// <summary>Runs existing checker controls and the existing four-form GPU probe.</summary>
[TestClass]
public sealed class GltfGpuSimpleInstancingExecutionTests
{
	private static string checkout;
	private static bool controlsPassed;
	public static List<object> Measurements { get; } = new();
	public static void Configure( string root ) { checkout = root; Measurements.Clear(); }
	public static Result[] RunControls( string root )
	{
		var results = Checks.RunContract().Concat( Checks.RunVerifierControls( root ) ).ToArray();
		controlsPassed = results.Length == 11 && results.All( row => row.Passed );
		return results;
	}

	[TestMethod]
	public async Task All125AuthoredInstancesCompleteTheExistingTwentyStateProbe()
	{
		await MainThread.Wait();
		Assert.IsTrue( controlsPassed, "The eleven existing API/checker controls must pass before GPU allocation." );
		Assert.AreEqual( "43101e5f4c4ae9727f5a21c2aecc6e391bf16b303099395a703babb490006e47", Hash( "game/core/shaders/gltf_deform_cs.shader" ) );
		Assert.AreEqual( "f15a8f245dd6d9f0293ee895c54a5f46a1b41eb094140153f1a5f7539178e5fc", Hash( "game/core/shaders/gltf_deform_cs.shader_c" ) );
		var reference = new Instancing( checkout );
		var minimum = new[] { double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity };
		var maximum = new[] { double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity };
		foreach ( var placement in new[] { Matrix.Identity, Instancing.AffinePlacement, Instancing.MirroredPlacement } )
			for ( var instance = 0; instance < Instancing.InstanceCount; instance++ )
				for ( var vertex = 0; vertex < Instancing.VertexCount; vertex++ )
				{
					var expected = reference.Engine( instance, vertex, placement );
					for ( var axis = 0; axis < 3; axis++ ) { minimum[axis] = Math.Min( minimum[axis], expected[axis] ); maximum[axis] = Math.Max( maximum[axis], expected[axis] ); }
				}
		var center = new Vector3( (float)((minimum[0] + maximum[0]) * .5), (float)((minimum[1] + maximum[1]) * .5), (float)((minimum[2] + maximum[2]) * .5) );
		var radius = (float)(.5 * Math.Sqrt( minimum.Select( (value, axis) => Math.Pow( maximum[axis] - value, 2 ) ).Sum() ));
		Assert.IsTrue( float.IsFinite( radius ) && radius > 0, "Independent bounds must be finite and nonempty." );
		var output = Path.Combine( checkout, "Temp/gltf-validation/direct-gpu/instancing-gpu", Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( output ); GltfGpuTriangleGateTests.Measurements.Clear();
		using var capture = new GltfGpuTriangleGateTests.NativeCapture( output, center + new Vector3( radius * 3, radius * 3, radius * 2 ), center, radius * 2.3f );
		capture.Camera.ZNear = Math.Max( .1f, radius * .01f ); capture.Camera.ZFar = radius * 12;
		var namedShader = Shader.Load( GltfManagedModelResource.ComputeShaderPath );
		Assert.IsTrue( namedShader is { IsValid: true, IsError: false, IsAnonymous: false }, "Actual named compiled compute shader required." );
		var compute = Material.FromShader( GltfManagedModelResource.ComputeShaderPath );
		Assert.IsTrue( compute is { IsValid: true, IsError: false }, "Actual named compute material required." );
		Measurements.Add( new { phase = "independent_camera_fit", minimum, maximum, center = center.ToString(), radius,
			compute = compute.ShaderName, source_geometry_rescaled = false, candidate_position_limit_inches = .0005, candidate_normal_limit = 2e-6,
			scope = "Original candidate threshold pass/fail and all measured maxima; no blanket numerical approval, public loader, image parity or hardware-instancing efficiency claim." } );
		try
		{
			Assert.AreEqual( 0, (await capture.Take( "empty_before" )).Coverage );
			var waits = 0;
			var rows = await Probe.Run( checkout, capture.Scene.SceneWorld, compute, async () =>
			{
				await capture.WaitFrames();
				if ( waits++ == 0 )
				{
					var frame = await capture.Take( "initial_two_runtime_instances" );
					Assert.IsTrue( frame.Coverage > 64, "Initial default-material instancing scene must visibly draw." );
				}
			} );
			Measurements.Add( new { phase = "actual_gpu_instancing", rows = rows.ToArray(), actual_measurements = rows.Count, actual_waits = waits } );
			Assert.AreEqual( 20, rows.Count );
			Assert.AreEqual( 0, (await capture.Take( "empty_after_disposal" )).Coverage );
		}
		finally { Measurements.Add( new { retained_native_captures = GltfGpuTriangleGateTests.Measurements.ToArray() } ); }
	}

	private static string Hash( string relative ) => Convert.ToHexStringLower( SHA256.HashData( File.ReadAllBytes( Path.Combine( checkout, relative ) ) ) );
}
