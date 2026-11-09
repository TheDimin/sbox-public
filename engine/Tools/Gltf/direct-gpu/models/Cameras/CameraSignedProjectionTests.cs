using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using NMatrix = System.Numerics.Matrix4x4;

namespace Sandbox.Gltf.DirectGpu.CamerasValidation;

[TestClass]
public sealed class CameraSignedProjectionTests
{
	static string root;
	public static List<object> Measurements { get; } = new();
	public static void Configure( string checkout ) { root = checkout; CameraReaderTests.Configure( checkout ); Measurements.Clear(); }
	sealed class SourceMount : IDisposable
	{
		readonly string folder; readonly LocalFileSystem fileSystem;
		internal readonly string Name;
		internal SourceMount( JsonNode source )
		{
			folder = Path.Combine( Path.GetTempPath(), "gltf_camera_sign_" + Guid.NewGuid().ToString( "N" ) ); Directory.CreateDirectory( folder ); Name = "camera_sign_" + Guid.NewGuid().ToString( "N" ) + ".gltf";
			File.WriteAllText( Path.Combine( folder, Name ), source.ToJsonString() ); File.Copy( Path.Combine( CameraReaderTests.AssetDirectory, "Cameras.bin" ), Path.Combine( folder, "Cameras.bin" ) );
			fileSystem = new LocalFileSystem( folder ); FileSystem.Mounted.Mount( fileSystem );
		}
		public void Dispose() { FileSystem.Mounted.UnMount( fileSystem ); fileSystem.Dispose(); Directory.Delete( folder, true ); }
	}
	[DataTestMethod]
	[DataRow( -1f, 1f )] [DataRow( 1f, -1f )] [DataRow( -1f, -1f )]
	public async Task PublicSignedOrthographicMagnitudesKeepSourceTransformAndProjection( float xmag, float ymag )
	{
		await MainThread.Wait(); var json = JsonNode.Parse( Encoding.UTF8.GetString( CameraReaderTests.Source() ) ); json["cameras"][1]["orthographic"]["xmag"] = xmag; json["cameras"][1]["orthographic"]["ymag"] = ymag; json["nodes"][2]["translation"][0] = .25f;
		using var mount = new SourceMount( json ); var output = Path.Combine( root, "Temp/gltf-validation/direct-gpu/Cameras/signed", Guid.NewGuid().ToString( "N" ) ); Directory.CreateDirectory( output );
		using var cap = new GltfGpuTriangleGateTests.NativeCapture( output ); Model model = null; ModelRenderer renderer = null; GameObject imported = null;
		try
		{
			model = Model.Load( mount.Name ); Assert.IsTrue( model.IsValid && !model.IsError );
			using ( cap.Scene.Push() ) { renderer = cap.Scene.CreateObject().Components.Create<ModelRenderer>(); renderer.Model = model; imported = model.ImportGltfCameras( cap.Scene, new global::Vector2( 192, 192 ) ); }
			var c = imported.GetComponentsInChildren<CameraComponent>().Single( x => x.Orthographic ); c.BackgroundColor = global::Color.Black;
			Assert.IsTrue( c.Enabled && c.IsMainCamera ); Assert.AreEqual( 1, c.Priority ); Assert.AreEqual( xmag < 0, c.FlipX ); Assert.AreEqual( ymag < 0, c.FlipY );
			Assert.IsTrue( (c.WorldPosition - new global::Vector3( 3, .25f, .5f ) / .0254f).Length < .001f && (c.WorldRotation.Forward - new global::Vector3( -1, 0, 0 )).Length < 1e-5f && (c.WorldRotation.Up - global::Vector3.Up).Length < 1e-5f );
			var control = cap.Camera; control.WorldPosition = c.WorldPosition; control.WorldRotation = c.WorldRotation; control.Orthographic = true; control.OrthographicHeight = 2f / .0254f; control.ZNear = .01f / .0254f; control.ZFar = 100f / .0254f;
			var baseline = await CameraPersistentFlipTests.Take( cap, control, output, "positive_control" ); var actual = await CameraPersistentFlipTests.Take( cap, c, output, "signed_import" ); CameraPersistentFlipTests.PixelPermutation( actual, baseline, xmag < 0, ymag < 0 );
			CheckOrthographic( c, xmag, ymag );
			c.FlipX = !c.FlipX; Assert.ThrowsException<AssertFailedException>( () => CheckOrthographic( c, xmag, ymag ) );
			var wrong = await CameraPersistentFlipTests.Take( cap, c, output, "wrong_sign_control" ); Assert.ThrowsException<AssertFailedException>( () => CameraPersistentFlipTests.PixelPermutation( wrong, baseline, xmag < 0, ymag < 0 ) ); c.FlipX = xmag < 0;
			Measurements.Add( new { xmag, ymag, capture = actual.Path, actual.Hash, wrong_control = wrong.Path, scope = "Public imported signed orthographic source camera; original pose/ordinary defaults preserved. Literal signed projection/rays/frustum plus actual final-blit pixel and wrong-sign controls. Test-owned off-centre source fixture, not an extra canonical package." } );
		}
		finally { await MainThread.Wait(); cap.Widget.Camera = cap.Camera; using ( cap.Scene.Push() ) { imported?.Destroy(); renderer?.GameObject.Destroy(); cap.Scene.EditorTick( 0, 0 ); cap.Scene.ProcessDeletes(); } model?.Destroy(); }
	}
	static void CheckOrthographic( CameraComponent c, float xmag, float ymag )
	{
		var expected = new global::Vector2( .5f - .25f / (2 * xmag), .5f + .5f / (2 * ymag) ); Assert.IsTrue( (c.PointToScreenNormal( global::Vector3.Zero ) - expected).Length < 2e-5f );
		var ray = c.ScreenNormalToRay( new global::Vector3( 1, 0, 0 ) ); Assert.IsTrue( (ray.Position - c.WorldPosition - new global::Vector3( -.01f, xmag, ymag ) / .0254f).Length < .002f );
		NMatrix matrix = c.ScreenProjectionMatrix; Assert.AreEqual( .0254f / xmag, matrix.M11, 1e-6f ); Assert.AreEqual( .0254f / ymag, matrix.M22, 1e-6f );
		Assert.IsTrue( c.GetFrustum().IsInside( c.WorldPosition + new global::Vector3( -2, 0, 0 ) / .0254f ) ); Assert.IsFalse( c.GetFrustum().IsInside( c.WorldPosition + new global::Vector3( -101, 0, 0 ) / .0254f ) );
	}
	[DataTestMethod]
	[DataRow( 4f )] [DataRow( 7f )] [DataRow( 3.1415927f )]
	public async Task PublicWidePerspectiveUsesPrescribedCotangentAndQueries( float yfov )
	{
		await MainThread.Wait(); var json = JsonNode.Parse( Encoding.UTF8.GetString( CameraReaderTests.Source() ) ); json["cameras"][0]["perspective"]["yfov"] = yfov; json["nodes"][1]["translation"][0] = .25f;
		using var mount = new SourceMount( json ); var output = Path.Combine( root, "Temp/gltf-validation/direct-gpu/Cameras/wide", Guid.NewGuid().ToString( "N" ) ); Directory.CreateDirectory( output );
		using var cap = new GltfGpuTriangleGateTests.NativeCapture( output ); Model model = null; ModelRenderer renderer = null; GameObject imported = null;
		try
		{
			model = Model.Load( mount.Name ); Assert.IsTrue( model.IsValid && !model.IsError ); using ( cap.Scene.Push() ) { renderer = cap.Scene.CreateObject().Components.Create<ModelRenderer>(); renderer.Model = model; imported = model.ImportGltfCameras( cap.Scene, new global::Vector2( 192, 192 ) ); }
			var c = imported.GetComponentsInChildren<CameraComponent>().Single( x => !x.Orthographic ); c.BackgroundColor = global::Color.Black;
			Assert.IsTrue( c.Enabled && c.IsMainCamera ); Assert.AreEqual( 1, c.Priority ); Assert.IsTrue( (c.WorldPosition - new global::Vector3( 3, .25f, .5f ) / .0254f).Length < .001f );
			var tangent = Math.Tan( (double)yfov / 2 ); var cotangent = 1 / tangent; Assert.AreEqual( tangent < 0, c.FlipX ); Assert.AreEqual( tangent < 0, c.FlipY );
			var control = cap.Camera; control.WorldPosition = c.WorldPosition; control.WorldRotation = c.WorldRotation; control.Orthographic = false; control.FovAxis = CameraComponent.Axis.Vertical; control.FieldOfView = c.FieldOfView; control.ZNear = c.ZNear; control.ZFar = c.ZFar;
			var baseline = await CameraPersistentFlipTests.Take( cap, control, output, "principal_positive_control" ); var actual = await CameraPersistentFlipTests.Take( cap, c, output, "wide_import" );
			if ( yfov == MathF.PI )
			{
				// This pinned unit square projects to much less than one fixed-point
				// raster subpixel at pi. Numeric and submission gates remain required.
				Assert.AreEqual( 0, actual.Pixels.Count( GltfGpuTriangleGateTests.NativeCapture.Foreground ) );
			}
			else CameraPersistentFlipTests.PixelPermutation( actual, baseline, tangent < 0, tangent < 0 );
			Assert.IsTrue( renderer.GltfInstance.DrawCalls > 0 && renderer.GltfInstance.CallbackErrors.Length == 0 );
			NMatrix matrix = c.ScreenProjectionMatrix; Assert.AreEqual( Math.Sign( cotangent ), Math.Sign( matrix.M11 ) ); Assert.AreEqual( Math.Sign( cotangent ), Math.Sign( matrix.M22 ) );
			Assert.IsTrue( Math.Abs( matrix.M11 - cotangent ) <= 32 * 1.1920928955078125e-7 * Math.Max( 1, Math.Abs( cotangent ) ) && Math.Abs( matrix.M22 - cotangent ) <= 32 * 1.1920928955078125e-7 * Math.Max( 1, Math.Abs( cotangent ) ) );
			var expectedPoint = new global::Vector2( (float)(.5 - .25 * cotangent / 6), (float)(.5 + .5 * cotangent / 6) ); Assert.IsTrue( (c.PointToScreenNormal( global::Vector3.Zero ) - expectedPoint).Length < 2e-5f );
			if ( yfov == MathF.PI )
			{
				// A small unit fixture can hide a cotangent mismatch at the pole.
				// Project a finite large lateral point with the actual native view
				// and resolved screen/custom projection, not an ordinary FOV oracle.
				var native = (global::CFrustum)typeof( SceneCamera ).GetField( "_frustum", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic ).GetValue( c.SceneCamera );
				NMatrix view = native.GetView(); var world = new global::Vector3( 0, 1e8f, 0 );
				Assert.IsTrue( c.CustomProjectionMatrix.HasValue ); NMatrix nativeProjection = c.CustomProjectionMatrix.Value;
				Assert.IsTrue( Math.Abs( nativeProjection.M11 - Math.Abs( cotangent ) ) <= 8 * 1.1920928955078125e-7 * Math.Abs( cotangent ), "Actual native customP lost the source cotangent near the pole." );
				var viewPoint = System.Numerics.Vector4.Transform( new System.Numerics.Vector4( world.x, world.y, world.z, 1 ), NMatrix.Transpose( view ) );
				var clipPoint = System.Numerics.Vector4.Transform( viewPoint, NMatrix.Transpose( nativeProjection ) );
				var matrixExpected = new global::Vector2( .5f + .5f * clipPoint.X / clipPoint.W, .5f - .5f * clipPoint.Y / clipPoint.W );
				if ( tangent < 0 ) matrixExpected = global::Vector2.One - matrixExpected;
				var query = c.PointToScreenNormal( world );
				Measurements.Add( new { yfov, amplified_pi_world = world.ToString(), actual_native_matrix_projection = matrixExpected.ToString(), component_query = query.ToString(), error = (query - matrixExpected).Length, scope = "Amplified customP-versus-ordinary-FOV query gate; valid pi input stays applicable and any mismatch remains a failure." } );
				Assert.IsTrue( (query - matrixExpected).Length < 2e-5f, "PI custom projection and component query disagree; principal-FOV rounding must not silently approximate the actual native matrix." );
			}
			var expectedRay = new global::Vector3( -1, (float)tangent, (float)tangent ).Normal; Assert.IsTrue( (c.ScreenNormalToRay( new global::Vector3( 1, 0, 0 ) ).Forward - expectedRay).Length < 1e-5f );
			Assert.IsTrue( c.GetFrustum().IsInside( c.WorldPosition + new global::Vector3( -2, 0, 0 ) / .0254f ) );
			var saved = c.FlipX; c.FlipX = !saved; Assert.IsTrue( (c.ScreenNormalToRay( new global::Vector3( 1, 0, 0 ) ).Forward - expectedRay).Length > .01f ); c.FlipX = saved;
			Measurements.Add( new { yfov, cotangent, principal_native_fov = c.FieldOfView, capture = actual.Path, actual.Hash, foreground = actual.Pixels.Count( GltfGpuTriangleGateTests.NativeCapture.Foreground ), scope = yfov == MathF.PI ? "Valid pi input: exact prescribed screen matrix, independent ray/point/frustum and actual GPU submission. Small source square has subpixel empty raster; no positive-image acceptance claim." : "Valid discouraged wide FOV: public imported camera, prescribed cotangent matrix, independent queries, actual pixel permutation and wrong-flip controls. No source rotation/winding changes." } );
		}
		finally { await MainThread.Wait(); cap.Widget.Camera = cap.Camera; using ( cap.Scene.Push() ) { imported?.Destroy(); renderer?.GameObject.Destroy(); cap.Scene.EditorTick( 0, 0 ); cap.Scene.ProcessDeletes(); } model?.Destroy(); }
	}
}
