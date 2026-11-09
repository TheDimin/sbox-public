using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using NMatrix = System.Numerics.Matrix4x4;

namespace Sandbox.Gltf.DirectGpu.CamerasValidation;

/// <summary>Existing native camera capability probe; manual setup is not glTF camera import acceptance.</summary>
[TestClass]
public sealed class CameraNativeApiTests
{
	static string root;
	public static List<object> Measurements { get; } = new();
	public static void Configure( string checkout ) { root = checkout; CameraReaderTests.Configure( checkout ); Measurements.Clear(); }
	[TestMethod]
	public async Task NativePerspectiveOrthographicAndInfiniteProjectionCapability()
	{
		await MainThread.Wait();
		var output = Path.Combine( root, "Temp/gltf-validation/direct-gpu/Cameras/native-api", Guid.NewGuid().ToString( "N" ) ); Directory.CreateDirectory( output );
		using var cap = new GltfGpuTriangleGateTests.NativeCapture( output );
		GltfManagedModelResource resource = null; Model model = null; ModelRenderer renderer = null;
		try
		{
			resource = new GltfManagedModelResource( GltfReader.Read( CameraReaderTests.Source(), CameraReaderTests.Resolve ) );
			model = Model.FromGltfResource( resource, "camera_native_capability" ); resource = null;
			using ( cap.Scene.Push() ) { renderer = cap.Scene.CreateObject().Components.Create<ModelRenderer>(); renderer.Model = model; }
			var camera = cap.Camera;
			camera.WorldPosition = new global::Vector3( 3, .5f, .5f ) / .0254f; camera.WorldRotation = Rotation.FromYaw( 180 );
			camera.Orthographic = false; camera.FovAxis = CameraComponent.Axis.Vertical; camera.FieldOfView = .7f * 180 / MathF.PI;
			camera.ZNear = .01f / .0254f; camera.ZFar = 100f / .0254f;
			var perspective = await cap.Take( "perspective_manual_native" ); Assert.IsTrue( perspective.Coverage > 100 );
			CheckQueries( camera, false );
			var finiteMatrix = (NMatrix)camera.ProjectionMatrix;
			camera.CustomProjectionMatrix = ((Matrix)finiteMatrix).Transpose();
			var finiteOverride = await CameraCapture.Take( cap, camera, output, "finite_transposed_override" );
			Measurements.Add( new { finite_override = finiteOverride.Path, finite_override_coverage = finiteOverride.Coverage, layout = "Transpose matches VRSystem.GetProjectionMatrix native OverrideProjection route." } );
			camera.CustomProjectionMatrix = null;
			camera.ZFar = float.PositiveInfinity;
			var infinite = await CameraCapture.Take( cap, camera, output, "infinite_manual_native" );
			var infiniteMatrix = (NMatrix)camera.ProjectionMatrix;
			var projected = camera.PointToScreenNormal( new global::Vector3( 0, 0, 0 ) );
			var finiteProjection = float.IsFinite( infiniteMatrix.M33 ) && float.IsFinite( infiniteMatrix.M34 ) && float.IsFinite( projected.x ) && float.IsFinite( projected.y );
			Measurements.Add( new { scope = "Existing native CameraComponent only; not glTF scene import.", perspective = perspective.Path, infinite = infinite.Path, perspective_coverage = perspective.Coverage, infinite_coverage = infinite.Coverage, finite_matrix = finiteMatrix.ToString(), infinite_matrix = infiniteMatrix.ToString(), finiteProjection, infinite_projected_point = projected.ToString() } );
			Assert.IsTrue( finiteProjection, "Native camera cannot query a finite projection with an infinite far clip." );
			// Keep the observed unmodified native infinity failure separate. Test
			// the exact infinite projection with an ordinary finite native cull bound.
			camera.ZFar = 100f / .0254f;
			camera.CustomProjectionMatrix = Matrix.CreateProjection( camera.ZNear, float.PositiveInfinity, .7f * 180 / MathF.PI, 1 ).Transpose();
			var customInfinite = await CameraCapture.Take( cap, camera, output, "infinite_custom_native" );
			Measurements.Add( new { custom_infinite = customInfinite.Path, custom_infinite_coverage = customInfinite.Coverage, native_cull_far = camera.ZFar.ToString(), projection_far = "Infinity", scope = "Exact infinite custom projection; ordinary 100m native cull field is a diagnostic, not unbounded glTF camera acceptance. Unmodified Infinity and maxfloat failures remain separate." } );
			Assert.IsTrue( customInfinite.Coverage > 100, "Custom infinite projection cannot render with an ordinary finite native cull field." );
			camera.CustomProjectionMatrix = null;
			camera.ZFar = 100f / .0254f; camera.Orthographic = true; camera.OrthographicHeight = 2f / .0254f;
			var orthographic = await cap.Take( "orthographic_manual_native" ); Assert.IsTrue( orthographic.Coverage > 100 ); CheckQueries( camera, true );
			Measurements.Add( new { orthographic = orthographic.Path, orthographic_coverage = orthographic.Coverage, orthographic_matrix = ((NMatrix)camera.ProjectionMatrix).ToString() } );
		}
		finally { await MainThread.Wait(); using ( cap.Scene.Push() ) { renderer?.GameObject.Destroy(); cap.Scene.EditorTick( 0, 0 ); cap.Scene.ProcessDeletes(); } model?.Destroy(); resource?.ReleaseOwner(); }
	}
	[TestMethod]
	public async Task SignedOrthographicAndWidePerspectiveCustomProjectionCapabilities()
	{
		await MainThread.Wait();
		var output = Path.Combine( root, "Temp/gltf-validation/direct-gpu/Cameras/native-api", Guid.NewGuid().ToString( "N" ) ); Directory.CreateDirectory( output );
		using var cap = new GltfGpuTriangleGateTests.NativeCapture( output );
		GltfManagedModelResource resource = null; Model model = null; ModelRenderer renderer = null;
		try
		{
			resource = new GltfManagedModelResource( GltfReader.Read( CameraReaderTests.Source(), CameraReaderTests.Resolve ) ); model = Model.FromGltfResource( resource, "camera_signed_native" ); resource = null;
			using ( cap.Scene.Push() ) { renderer = cap.Scene.CreateObject().Components.Create<ModelRenderer>(); renderer.Model = model; }
			var camera = cap.Camera; camera.WorldPosition = new global::Vector3( 3, .5f, .5f ) / .0254f; camera.WorldRotation = Rotation.FromYaw( 180 );
			camera.ZNear = .01f / .0254f; camera.ZFar = 100f / .0254f; camera.Orthographic = true; camera.OrthographicHeight = 2f / .0254f;
			var ordinary = await cap.Take( "ortho_positive" );
			NMatrix signed = camera.ProjectionMatrix; signed.M11 *= -1; camera.CustomProjectionMatrix = ((Matrix)signed).Transpose();
			var mirrored = await CameraCapture.Take( cap, camera, output, "ortho_signed_x_custom" );
			var p = new global::Vector3( 0, 0, 0 ); var query = camera.PointToScreenNormal( p ); var ray = camera.ScreenNormalToRay( new global::Vector3( 1, .5f, 0 ) );
			var draws = (System.Collections.IEnumerable)typeof( GltfGpuInstance ).GetField( "draws", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic ).GetValue( renderer.GltfInstance );
			var draw = draws.Cast<object>().Single();
			var indexBuffer = (GpuBuffer)draw.GetType().GetField( "Indices", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic ).GetValue( draw );
			var original = renderer.GltfInstance.Readback().Single().Indices;
			var reversed = (uint[])original.Clone(); for ( var i = 0; i < reversed.Length; i += 3 ) (reversed[i + 1], reversed[i + 2]) = (reversed[i + 2], reversed[i + 1]);
			GltfGpuTriangleGateTests.NativeCapture.Frame corrected;
			try { indexBuffer.SetData<uint>( reversed ); corrected = await CameraCapture.Take( cap, camera, output, "ortho_signed_x_reversed_gpu_winding" ); }
			finally { indexBuffer.SetData<uint>( original ); }
			camera.CustomProjectionMatrix = null; camera.Orthographic = false; camera.FovAxis = CameraComponent.Axis.Vertical; camera.FieldOfView = 4f * 180 / MathF.PI;
			var wide = await CameraCapture.Take( cap, camera, output, "wide_yfov_4_native" );
			var wideMatrix = (NMatrix)camera.ProjectionMatrix; var wideRay = camera.ScreenNormalToRay( new global::Vector3( 1, 0, 0 ) );
			Measurements.Add( new { ordinary = ordinary.Path, ordinary.Coverage, mirrored = mirrored.Path, mirrored_coverage = mirrored.Coverage, reversed_winding = corrected.Path, reversed_winding_coverage = corrected.Coverage, signed_query = query.ToString(), signed_ray = ray.Forward.ToString(), signed_expected_x = .75f, actual_x = query.x, wide = wide.Path, wide_coverage = wide.Coverage, wide_matrix = wideMatrix.ToString(), wide_ray = wideRay.Forward.ToString(), scope = "Manual native capability diagnostic. Signed reflected projection versus actual GPU winding control; ordinary public queries independently ignore override. No imported camera acceptance." } );
			Assert.IsTrue( ordinary.Coverage > 100 && corrected.Coverage > 100, "Signed projection failed even after an explicit test-owned GPU winding reversal." );
		}
		finally { await MainThread.Wait(); using ( cap.Scene.Push() ) { renderer?.GameObject.Destroy(); cap.Scene.EditorTick( 0, 0 ); cap.Scene.ProcessDeletes(); } model?.Destroy(); resource?.ReleaseOwner(); }
	}
	internal static void CheckQueries( CameraComponent camera, bool ortho )
	{
		var center = camera.ScreenNormalToRay( new global::Vector3( .5f, .5f, 0 ) );
		Assert.AreEqual( -1f, center.Forward.x, 1e-5f ); Assert.AreEqual( 0f, center.Forward.y, 1e-5f ); Assert.AreEqual( 0f, center.Forward.z, 1e-5f );
		var edge = camera.ScreenNormalToRay( new global::Vector3( 1, 0, 0 ) );
		var expectedDirection = ortho ? new global::Vector3( -1, 0, 0 ) : new global::Vector3( -1, MathF.Tan( .35f ), MathF.Tan( .35f ) ).Normal;
		Assert.IsTrue( (edge.Forward - expectedDirection).Length < 1e-5f, "Native ray disagrees with independent pinhole/orthographic oracle." );
		if ( ortho )
		{
			var expectedOrigin = camera.WorldPosition + new global::Vector3( -.01f, 1, 1 ) / .0254f;
			Assert.IsTrue( (edge.Position - expectedOrigin).Length < 1e-4f );
		}
		var frustum = camera.GetFrustum();
		Assert.IsTrue( frustum.IsInside( camera.WorldPosition + new global::Vector3( -2, 0, 0 ) / .0254f ) );
		Assert.IsFalse( frustum.IsInside( camera.WorldPosition + new global::Vector3( -.005f, 0, 0 ) / .0254f ) );
		Assert.IsFalse( frustum.IsInside( camera.WorldPosition + new global::Vector3( -101, 0, 0 ) / .0254f ) );
		// Independently normalized literal quaternion rotates (0,1,0) to (0,c,s).
		var norm = .383 * .383 + .92375 * .92375; var c = (float)((.92375 * .92375 - .383 * .383) / norm); var s = (float)(-2 * .383 * .92375 / norm);
		foreach ( var xy in new[] { (0f, 0f), (1f, 0f), (0f, 1f), (1f, 1f) } )
		{
			var y = xy.Item2 * c; var z = xy.Item2 * s; var depth = 3 - z;
			var expected = ortho ? new global::Vector2( .5f + (xy.Item1 - .5f) / 2, .5f - (y - .5f) / 2 ) : new global::Vector2( .5f + (xy.Item1 - .5f) / (2 * depth * MathF.Tan( .35f )), .5f - (y - .5f) / (2 * depth * MathF.Tan( .35f )) );
			var actual = camera.PointToScreenNormal( new global::Vector3( z, xy.Item1, y ) / .0254f );
			Assert.IsTrue( (actual - expected).Length < 2e-5f, "Native point projection disagrees with literal source geometry: " + actual + " versus " + expected );
		}
	}
}
