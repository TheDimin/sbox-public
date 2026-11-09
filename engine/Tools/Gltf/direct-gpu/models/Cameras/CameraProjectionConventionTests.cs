using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using NMatrix = System.Numerics.Matrix4x4;

namespace Sandbox.Gltf.DirectGpu.CamerasValidation;

[TestClass]
public sealed class CameraProjectionConventionTests
{
	static string root;
	public static List<object> Measurements { get; } = new();
	public static void Configure( string checkout ) { root = checkout; CameraReaderTests.Configure( checkout ); Measurements.Clear(); }
	[TestMethod]
	public async Task DiscoverNativeOverrideDepthAndMatrixConventionWithPositiveControls()
	{
		await MainThread.Wait();
		var output = Path.Combine( root, "Temp/gltf-validation/direct-gpu/Cameras/convention", Guid.NewGuid().ToString( "N" ) ); Directory.CreateDirectory( output );
		using var cap = new GltfGpuTriangleGateTests.NativeCapture( output );
		GltfManagedModelResource resource = null; Model model = null; ModelRenderer renderer = null;
		try
		{
			resource = new GltfManagedModelResource( GltfReader.Read( CameraReaderTests.Source(), CameraReaderTests.Resolve ) ); model = Model.FromGltfResource( resource, "camera_convention" ); resource = null;
			using ( cap.Scene.Push() ) { renderer = cap.Scene.CreateObject().Components.Create<ModelRenderer>(); renderer.Model = model; }
			var camera = cap.Camera; camera.WorldPosition = new global::Vector3( 3, .5f, .5f ) / .0254f; camera.WorldRotation = Rotation.FromYaw( 180 );
			camera.ZNear = .01f / .0254f; camera.ZFar = 100f / .0254f; camera.Orthographic = false; camera.FovAxis = CameraComponent.Axis.Vertical; camera.FieldOfView = .7f * 180 / MathF.PI;
			var baseline = await cap.Take( "ordinary_finite" ); Assert.IsTrue( baseline.Coverage > 100 );
			Matrix forward = camera.ProjectionMatrix; // Lazily creates the query frustum.
			var nativeFrustum = (global::CFrustum)typeof( SceneCamera ).GetField( "_frustum", BindingFlags.Instance | BindingFlags.NonPublic ).GetValue( camera.SceneCamera );
			Matrix reverse = nativeFrustum.GetInvReverseZProj().Inverted;
			NMatrix reverseInfinite = reverse; reverseInfinite.M33 = 0; reverseInfinite.M34 = camera.ZNear;
			var controls = new (string Name, Matrix Value)[] { ("forward_native", forward), ("forward_transpose", forward.Transpose()), ("reverse_native", reverse), ("reverse_transpose", reverse.Transpose()), ("reverse_infinite_native", reverseInfinite), ("reverse_infinite_transpose", ((Matrix)reverseInfinite).Transpose()) };
			var exactFinite = 0;
			foreach ( var control in controls )
			{
				camera.CustomProjectionMatrix = control.Value;
				var capture = await CameraCapture.Take( cap, camera, output, control.Name );
				var union = capture.Pixels.Zip( baseline.Pixels, (a, b) => GltfGpuTriangleGateTests.NativeCapture.Foreground( a ) || GltfGpuTriangleGateTests.NativeCapture.Foreground( b ) ).Count( x => x );
				var intersection = capture.Pixels.Zip( baseline.Pixels, (a, b) => GltfGpuTriangleGateTests.NativeCapture.Foreground( a ) && GltfGpuTriangleGateTests.NativeCapture.Foreground( b ) ).Count( x => x );
				var iou = intersection / (double)union;
				if ( !control.Name.Contains( "infinite" ) && iou > .99 ) exactFinite++;
				Measurements.Add( new { control = control.Name, capture = capture.Path, capture.Sha256, capture.Coverage, baseline_coverage = baseline.Coverage, iou, native_far = camera.ZFar.ToString(), matrix = ((NMatrix)control.Value).ToString(), scope = "Native projection override convention diagnostic against unchanged ordinary finite camera. Infinite controls retain an explicit100m native cull bound; not unbounded camera acceptance." } );
			}
			Assert.IsTrue( exactFinite > 0, "No native projection override reproduces the known finite camera; override route remains unestablished." );
		}
		finally { await MainThread.Wait(); using ( cap.Scene.Push() ) { renderer?.GameObject.Destroy(); cap.Scene.EditorTick( 0, 0 ); cap.Scene.ProcessDeletes(); } model?.Destroy(); resource?.ReleaseOwner(); }
	}
}
