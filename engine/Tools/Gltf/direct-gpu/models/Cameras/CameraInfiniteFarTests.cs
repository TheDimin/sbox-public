using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using NMatrix = System.Numerics.Matrix4x4;

namespace Sandbox.Gltf.DirectGpu.CamerasValidation;

[TestClass]
public sealed class CameraInfiniteFarTests
{
	static string root;
	public static List<object> Measurements { get; } = new();
	public static void Configure( string checkout ) { root = checkout; CameraReaderTests.Configure( checkout ); Measurements.Clear(); }
	[TestMethod]
	public async Task InfiniteReverseZProjectionWithUnboundedNativeFarAndQueries()
	{
		await MainThread.Wait();
		var output = Path.Combine( root, "Temp/gltf-validation/direct-gpu/Cameras/infinite", Guid.NewGuid().ToString( "N" ) ); Directory.CreateDirectory( output );
		using var cap = new GltfGpuTriangleGateTests.NativeCapture( output );
		GltfManagedModelResource resource = null; Model model = null; ModelRenderer renderer = null;
		try
		{
			resource = new GltfManagedModelResource( GltfReader.Read( CameraReaderTests.Source(), CameraReaderTests.Resolve ) ); model = Model.FromGltfResource( resource, "camera_infinite" ); resource = null;
			using ( cap.Scene.Push() ) { renderer = cap.Scene.CreateObject().Components.Create<ModelRenderer>(); renderer.Model = model; }
			var camera = cap.Camera; camera.WorldPosition = new global::Vector3( 3, .5f, .5f ) / .0254f; camera.WorldRotation = Rotation.FromYaw( 180 );
			camera.ZNear = .01f / .0254f; camera.ZFar = 100f / .0254f; camera.Orthographic = false; camera.FovAxis = CameraComponent.Axis.Vertical; camera.FieldOfView = .7f * 180 / MathF.PI;
			var baseline = await cap.Take( "finite_positive" ); Assert.IsTrue( baseline.Coverage > 100 );
			var cot = 1f / MathF.Tan( .35f );
			camera.CustomProjectionMatrix = new NMatrix( cot, 0, 0, 0, 0, cot, 0, 0, 0, 0, 0, camera.ZNear, 0, 0, -1, 0 );
			var unbounded = false;
			foreach ( var far in new[] { float.PositiveInfinity, float.MaxValue } )
			{
				camera.ZFar = far; var capture = await CameraCapture.Take( cap, camera, output, float.IsPositiveInfinity( far ) ? "native_infinity_reverse_projection" : "native_maxfloat_reverse_projection" );
				var frustum = camera.GetFrustum(); var beyondAuthoredFinite = camera.WorldPosition + new global::Vector3( -101, 0, 0 ) / .0254f;
				var containsBeyond = frustum.IsInside( beyondAuthoredFinite ); var rejectsNear = !frustum.IsInside( camera.WorldPosition + new global::Vector3( -.005f, 0, 0 ) / .0254f );
				var point = camera.PointToScreenNormal( global::Vector3.Zero ); var queryFinite = float.IsFinite( point.x ) && float.IsFinite( point.y );
				var ray = camera.ScreenNormalToRay( new global::Vector3( 1, 0, 0 ) ); var expected = new global::Vector3( -1, MathF.Tan( .35f ), MathF.Tan( .35f ) ).Normal; var rayMatches = (ray.Forward - expected).Length < 1e-5f;
				Measurements.Add( new { capture = capture.Path, capture.Sha256, capture.Coverage, native_far = far.ToString(), containsBeyond, rejectsNear, queryFinite, rayMatches, source_far = "absent", projection = "Exact reverse-Z infinite, M33=0 M34=znear M43=-1, as-is native column convention", scope = "Native unbounded far-field rendering and independent ray/frustum/query controls, not glTF importer acceptance." } );
				if ( float.IsPositiveInfinity( far ) && capture.Coverage > 100 && containsBeyond && rejectsNear && queryFinite && rayMatches ) unbounded = true;
			}
			Assert.IsTrue( unbounded, "Exact reverse-Z projection did not establish native Infinity render and independent camera query behavior." );
		}
		finally { await MainThread.Wait(); using ( cap.Scene.Push() ) { renderer?.GameObject.Destroy(); cap.Scene.EditorTick( 0, 0 ); cap.Scene.ProcessDeletes(); } model?.Destroy(); resource?.ReleaseOwner(); }
	}
}
