using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Sandbox.Gltf.DirectGpu;

using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

/// <summary>One bounded native light comparison, preserving public materials, camera and GPU bytes.</summary>
[TestClass]
public sealed class GltfGpuPublicLightingCalibrationTests
{
	private static string checkout;
	public static List<object> Measurements { get; } = new();
	public static void Configure( string root ) { checkout = root; Measurements.Clear(); }

	[TestMethod]
	public async Task ThreePointIntensitiesKeepThreeAuthoredGpuStatesIdentical()
	{
		await MainThread.Wait();
		string[] originalFingerprints = null;
		string originalCamera = null;
		foreach ( var intensity in new[] { 4f, 1f, .25f } )
		{
			var result = await Validation.AnimatedMorphCubePublicGpuProbe.RunCapturedAuthored( checkout,
				Path.Combine( checkout, "Temp/gltf-validation/direct-gpu/AnimatedMorphCube-reference.json" ), compiled: false,
				configureCapture: capture => ConfigureOwnedLight( capture, intensity ) );
			var json = JsonSerializer.SerializeToElement( result );
			Measurements.Add( new { intensity, result,
				scope = "Only the private point light intensity changes. Same public source material, geometry, three authored keys, camera, native Normal and postprocessing. Images remain unedited; no material parity claim." } );
			var fingerprints = json.GetProperty( "rows" ).EnumerateArray().SelectMany( row =>
				row.GetProperty( "Fingerprints" ).EnumerateArray().Select( buffer =>
					buffer.GetProperty( "VertexSha256" ).GetString() + ":" + buffer.GetProperty( "IndexSha256" ).GetString() ) ).ToArray();
			using var camera = JsonDocument.Parse( json.GetProperty( "captures" )[0].GetProperty( "CameraStateJson" ).GetString() );
			var fixedCamera = JsonSerializer.Serialize( camera.RootElement.EnumerateObject()
				.Where( field => field.Name != "point_light_color" ).ToDictionary( field => field.Name, field => field.Value.Clone() ) );
			Assert.AreEqual( 3, fingerprints.Length );
			if ( originalFingerprints is null ) { originalFingerprints = fingerprints; originalCamera = fixedCamera; }
			else
			{
				CollectionAssert.AreEqual( originalFingerprints, fingerprints, "Light changes mutated actual GPU geometry or bound indices." );
				Assert.AreEqual( originalCamera, fixedCamera, "Camera or postprocessing changed during light calibration." );
			}
		}
	}

	internal static void ConfigureOwnedLight( GltfGpuTriangleGateTests.NativeCapture capture, float intensity )
	{
		if ( !float.IsFinite( intensity ) || intensity < 0 ) throw new ArgumentOutOfRangeException( nameof( intensity ) );
		var field = typeof( GltfGpuTriangleGateTests.NativeCapture ).GetField( "light", BindingFlags.Instance | BindingFlags.NonPublic );
		var light = field?.GetValue( capture ) as ScenePointLight;
		Assert.IsTrue( light.IsValid(), "The existing capture's owned point light is unavailable." );
		light.LightColor = Color.White * intensity;
	}
}
