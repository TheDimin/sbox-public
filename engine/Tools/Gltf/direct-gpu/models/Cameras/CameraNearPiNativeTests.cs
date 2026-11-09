using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using NMatrix = System.Numerics.Matrix4x4;

namespace Sandbox.Gltf.DirectGpu.CamerasValidation;

[TestClass]
public sealed class CameraNearPiNativeTests
{
	static string checkout;
	public static List<object> Measurements { get; } = new();

	public static void Configure( string root )
	{
		checkout = root;
		CameraReaderTests.Configure( root );
		Measurements.Clear();
	}

	[TestMethod]
	public async Task NativeCustomProjectionAndPointQueriesAgreeAtNearPi()
	{
		await MainThread.Wait();
		var output = Path.Combine( checkout, "Temp/gltf-validation/direct-gpu/Cameras/near-pi-native", Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( output );
		using var capture = new GltfGpuTriangleGateTests.NativeCapture( output );
		GltfManagedModelResource resource = null;
		Model model = null;
		ModelRenderer renderer = null;
		try
		{
			resource = new GltfManagedModelResource( GltfReader.Read( CameraReaderTests.Source(), CameraReaderTests.Resolve ) );
			model = Model.FromGltfResource( resource, "camera_near_pi_native" );
			resource = null;
			using ( capture.Scene.Push() )
			{
				renderer = capture.Scene.CreateObject().Components.Create<ModelRenderer>();
				renderer.Model = model;
			}

			var camera = capture.Camera;
			camera.WorldPosition = new global::Vector3( 3, .25f, .5f ) / .0254f;
			camera.WorldRotation = Rotation.FromYaw( 180 );
			camera.ZNear = .01f / .0254f;
			camera.ZFar = 100f / .0254f;
			camera.Orthographic = false;
			camera.FovAxis = CameraComponent.Axis.Vertical;
			camera.FieldOfView = 45;
			var narrow = await CameraCapture.Take( capture, camera, output, "ordinary_45" );
			camera.FieldOfView = 70;
			var wide = await CameraCapture.Take( capture, camera, output, "ordinary_70" );
			camera.CustomProjectionMatrix = ReverseZ( camera, (float)(1 / Math.Tan( Math.PI / 8 )) );
			var overridden = await CameraCapture.Take( capture, camera, output, "ordinary_70_custom_45" );
			var intersection = 0;
			var union = 0;
			for ( var i = 0; i < narrow.Pixels.Length; ++i )
			{
				var expected = GltfGpuTriangleGateTests.NativeCapture.Foreground( narrow.Pixels[i] );
				var actual = GltfGpuTriangleGateTests.NativeCapture.Foreground( overridden.Pixels[i] );
				if ( expected && actual ) intersection++;
				if ( expected || actual ) union++;
			}
			var iou = intersection / (double)union;
			Measurements.Add( new { narrow.Path, narrow.Sha256, narrow.Coverage, wide_path = wide.Path, wide_hash = wide.Sha256,
				wide_coverage = wide.Coverage, overridden_path = overridden.Path, overridden_hash = overridden.Sha256,
				overridden_coverage = overridden.Coverage, iou, scope = "Native reverse-Z override at ordinary 70 degrees reproduces a separate ordinary 45 degree raster." } );
			Assert.IsTrue( wide.Coverage > 100 && narrow.Coverage > 1.5 * wide.Coverage && iou >= .99,
				"Native custom projection raster control did not distinguish the override from the ordinary FOV." );

			// The positive cotangent is the native pre-flip half of the import convention.
			// Amplify the difference between exact cotangent and rounded principal FOV.
			const float authoredYFov = MathF.PI;
			var tangent = Math.Tan( (double)authoredYFov / 2 );
			var cotangent = (float)Math.Abs( 1 / tangent );
			// An exact identity orientation keeps tiny sine errors from a 180-degree
			// turn from contaminating the amplified lateral-coordinate test.
			camera.WorldPosition = global::Vector3.Zero;
			camera.WorldRotation = Rotation.Identity;
			camera.FieldOfView = Math.Min( (float)(2 * Math.Atan( Math.Abs( tangent ) ) * (180 / Math.PI)), float.BitDecrement( 180 ) );
			camera.CustomProjectionMatrix = null;
			camera.InitializeRendering( false );
			var world = new global::Vector3( 3 / .0254f, -1e8f, 0 );
			var ordinary = camera.PointToScreenNormal( world );
			camera.CustomProjectionMatrix = ReverseZ( camera, cotangent );
			var actualPoint = camera.PointToScreenNormal( world );
			var expectedX = .5 + .5 * cotangent * -world.y / world.x;
			var error = Math.Abs( actualPoint.x - expectedX );
			Measurements.Add( new { authoredYFov, cotangent, principal_degrees = camera.FieldOfView,
				ordinary_x = ordinary.x, overridden_x = actualPoint.x, expected_x = expectedX, error,
				scope = "Amplified pre-flip point query against independent glTF cotangent arithmetic; near-pi raster coverage is not measured." } );
			Assert.AreEqual( expectedX, actualPoint.x, 2e-5, "PointToScreenNormal disagrees with the installed custom projection at near pi." );
		}
		finally
		{
			await MainThread.Wait();
			using ( capture.Scene.Push() )
			{
				renderer?.GameObject.Destroy();
				capture.Scene.EditorTick( 0, 0 );
				capture.Scene.ProcessDeletes();
			}
			model?.Destroy();
			resource?.ReleaseOwner();
		}
	}

	static NMatrix ReverseZ( CameraComponent camera, float cotangent )
	{
		var depth = camera.ZFar - camera.ZNear;
		return new NMatrix(
			cotangent, 0, 0, 0,
			0, cotangent, 0, 0,
			0, 0, camera.ZNear / depth, camera.ZNear * (camera.ZFar / depth),
			0, 0, -1, 0 );
	}
}
