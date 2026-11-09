using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Sandbox.Gltf.DirectGpu.CamerasValidation;

[TestClass]
public sealed class CameraIntegrationTests
{
	static string root;
	public static List<object> Measurements { get; } = new();
	public static void Configure( string checkout ) { root = checkout; CameraReaderTests.Configure( checkout ); Measurements.Clear(); }
	[DataTestMethod]
	[DataRow( false )] [DataRow( true )]
	public async Task PublicSelectedSceneImportsSourceCamerasAndOwnsOnlyItsHierarchy( bool compiled )
	{
		await MainThread.Wait();
		var output = Path.Combine( root, "Temp/gltf-validation/direct-gpu/Cameras/import", compiled ? "canonical" : "source" ); Directory.CreateDirectory( output );
		var temporary = Path.Combine( Path.GetTempPath(), "gltf_camera_import_" + Guid.NewGuid().ToString( "N" ) ); Directory.CreateDirectory( temporary );
		var name = "cameras_" + Guid.NewGuid().ToString( "N" ) + (compiled ? ".glb_c" : ".gltf");
		var bytes = compiled ? CameraReaderTests.Canonical() : CameraReaderTests.Source();
		if ( compiled )
		{
			var canonicalDirectory = Path.Combine( root, "Temp/gltf-validation/direct-gpu/Cameras/canonical" ); Directory.CreateDirectory( canonicalDirectory );
			File.WriteAllBytes( Path.Combine( canonicalDirectory, "Cameras.glb_c" ), bytes );
		}
		File.WriteAllBytes( Path.Combine( temporary, name ), bytes ); if ( !compiled ) File.Copy( Path.Combine( CameraReaderTests.AssetDirectory, "Cameras.bin" ), Path.Combine( temporary, "Cameras.bin" ) );
		var mount = new LocalFileSystem( temporary ); FileSystem.Mounted.Mount( mount );
		using var cap = new GltfGpuTriangleGateTests.NativeCapture( output );
		Model model = null; ModelRenderer renderer = null; GameObject imported = null;
		try
		{
			model = Model.Load( name ); Assert.IsTrue( model is { IsValid: true, IsError: false } && model.native.IsNull ); Assert.AreEqual( 0, model.GltfSelectedScene );
			using ( cap.Scene.Push() )
			{
				renderer = cap.Scene.CreateObject().Components.Create<ModelRenderer>(); renderer.Model = model;
				imported = model.ImportGltfCameras( cap.Scene, new global::Vector2( 192, 192 ) ); cap.Scene.EditorTick( 0, 0 );
			}
			var cameras = imported.GetComponentsInChildren<CameraComponent>().OrderBy( c => c.Orthographic ).ToArray(); Assert.AreEqual( 2, cameras.Length );
			Assert.IsTrue( cap.Camera.Enabled && cap.Camera.IsMainCamera ); Assert.AreEqual( 1, cap.Camera.Priority );
			Assert.AreSame( cap.Camera, cap.Scene.Camera, "Import changed existing equal-priority main-camera selection." );
			using ( cap.Scene.Push() )
			{
				var independent = model.ImportGltfCameras( cap.Scene, new global::Vector2( 192, 192 ) );
				try
				{
					var second = independent.GetComponentsInChildren<CameraComponent>().First(); var before = cameras[0].WorldPosition;
					Assert.AreNotSame( cameras[0], second ); independent.LocalPosition += new global::Vector3( 20, 0, 0 ); Assert.AreEqual( before, cameras[0].WorldPosition );
				}
				finally { independent.Destroy(); cap.Scene.ProcessDeletes(); }
			}
			GltfGpuTriangleGateTests.NativeCapture.Frame perspectiveFrame = null;
			foreach ( var camera in cameras )
			{
				Assert.IsTrue( camera.Enabled && camera.IsMainCamera ); Assert.AreEqual( 1, camera.Priority );
				Assert.IsTrue( (camera.WorldPosition - new global::Vector3( 3, .5f, .5f ) / .0254f).Length < 1e-4f );
				Assert.AreEqual( .01f / .0254f, camera.ZNear, 1e-6f ); Assert.AreEqual( 100f / .0254f, camera.ZFar, 1e-3f );
				// Test-owned presentation changes, recorded separately from source camera parameters.
				camera.BackgroundColor = global::Color.Black; camera.EnablePostProcessing = true;
				var capture = await CameraCapture.Take( cap, camera, output, camera.Orthographic ? "orthographic" : "perspective" );
				Assert.IsTrue( capture.Coverage > 100 ); CameraNativeApiTests.CheckQueries( camera, camera.Orthographic );
				CheckSourceGpuGeometry( renderer.GltfInstance );
				if ( !camera.Orthographic ) perspectiveFrame = capture;
				Measurements.Add( new { form = compiled ? "canonical-glb_c" : "source-gltf", input_sha256 = CameraReaderTests.Hash( bytes ), camera = camera.Orthographic ? "orthographic" : "perspective", capture = capture.Path, capture.Sha256, capture.Coverage, capture.CameraStateJson, reference = "Temp/gltf-reference/glTF-Sample-Assets/Models/Cameras/screenshot/screenshot.png", recipe = "Authored camera pose and projection. Inferred 192-square output, diagnostic black background and existing native point light/postprocessing. No exact reference renderer/lighting match or visual approval claim." } );
			}
			var perspective = cameras[0]; var correctFov = perspective.FieldOfView; perspective.FieldOfView *= 1.5f;
			Assert.ThrowsException<AssertFailedException>( () => CameraNativeApiTests.CheckQueries( perspective, false ), "Wrong imported yfov must fail independent ray oracle." );
			var wrongFov = await CameraCapture.Take( cap, perspective, output, "wrong_yfov_control" ); Assert.IsTrue( Math.Abs( wrongFov.Coverage - perspectiveFrame.Coverage ) > perspectiveFrame.Coverage / 4 ); perspective.FieldOfView = correctFov;
			perspective.ZNear = 4 / .0254f; var nearClipped = await CameraCapture.Take( cap, perspective, output, "wrong_near_control" ); Assert.AreEqual( 0, nearClipped.Coverage ); perspective.ZNear = .01f / .0254f;
			perspective.ZFar = 2 / .0254f; var farClipped = await CameraCapture.Take( cap, perspective, output, "wrong_far_control" ); Assert.AreEqual( 0, farClipped.Coverage ); perspective.ZFar = 100 / .0254f;
			var correctRotation = perspective.WorldRotation; perspective.WorldRotation = Rotation.Identity; var wrongDirection = await CameraCapture.Take( cap, perspective, output, "wrong_direction_control" ); Assert.AreEqual( 0, wrongDirection.Coverage ); perspective.WorldRotation = correctRotation;
			Measurements.Add( new { controls = "Actual imported camera FOV, near, far and orientation mutations; each restored before cleanup.", wrong_fov_coverage = wrongFov.Coverage, near_clipped_coverage = nearClipped.Coverage, far_clipped_coverage = farClipped.Coverage, wrong_direction_coverage = wrongDirection.Coverage } );
			var survivor = cameras[1]; var position = survivor.WorldPosition;
			using ( cap.Scene.Push() ) { cameras[0].GameObject.Destroy(); cap.Scene.ProcessDeletes(); }
			Assert.IsTrue( survivor.IsValid() && survivor.Enabled ); Assert.AreEqual( position, survivor.WorldPosition );
			using ( cap.Scene.Push() ) { imported.Destroy(); imported = null; cap.Scene.ProcessDeletes(); }
			Assert.IsFalse( survivor.IsValid() ); Assert.IsTrue( renderer.IsValid() && cap.Camera.IsValid() );
			var oldInstance = renderer.GltfInstance; using ( cap.Scene.Push() ) { renderer.GameObject.Destroy(); renderer = null; cap.Scene.EditorTick( 0, 0 ); cap.Scene.ProcessDeletes(); }
			cap.Widget.Camera = cap.Camera;
			Assert.AreEqual( 0, (await cap.Take( "deleted_geometry" )).Coverage ); Assert.ThrowsException<ObjectDisposedException>( () => oldInstance.Readback() );
		}
		finally
		{
			await MainThread.Wait(); cap.Widget.Camera = cap.Camera; using ( cap.Scene.Push() ) { imported?.Destroy(); renderer?.GameObject.Destroy(); cap.Scene.EditorTick( 0, 0 ); cap.Scene.ProcessDeletes(); }
			model?.Destroy(); FileSystem.Mounted.UnMount( mount ); mount.Dispose(); Directory.Delete( temporary, true );
		}
	}
	static void CheckSourceGpuGeometry( GltfGpuInstance instance )
	{
		Assert.IsTrue( instance.DrawCalls > 0 && instance.SubmittedRevision == instance.PoseRevision && instance.CallbackErrors.Length == 0 );
		var row = instance.Readback().Single(); Assert.AreEqual( 0, row.Node ); Assert.AreEqual( 0, row.Mesh ); Assert.AreEqual( 0, row.Primitive ); Assert.AreEqual( "gpu-readback", row.IndexDataOrigin );
		CollectionAssert.AreEqual( new uint[] { 0, 1, 2, 1, 3, 2 }, row.Indices.Select( index => row.SourceVertices[index] ).ToArray() );
		var position = row.Fields.Single( field => field.Semantic == "POSITION" && field.SemanticIndex == 0 ); Assert.AreEqual( 5126, position.ComponentType ); Assert.AreEqual( 3, position.Components );
		var norm = .383 * .383 + .92375 * .92375; var cosine = (.92375 * .92375 - .383 * .383) / norm; var sine = -2 * .383 * .92375 / norm;
		var source = new[] { (X: 0d, Y: 0d), (X: 1d, Y: 0d), (X: 0d, Y: 1d), (X: 1d, Y: 1d) };
		for ( var vertex = 0; vertex < row.VertexCount; vertex++ )
		{
			var literal = source[row.SourceVertices[vertex]]; var expected = new[] { literal.Y * sine / .0254, literal.X / .0254, literal.Y * cosine / .0254 };
			for ( var axis = 0; axis < 3; axis++ )
			{
				var actual = System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian( row.Bytes.AsSpan( vertex * row.VertexStride + position.Offset + axis * 4, 4 ) );
				// 32 float rounding steps at the unit-square inch conversion scale.
				Assert.IsTrue( Math.Abs( actual - expected[axis] ) <= 32 * 1.1920928955078125e-7 / .0254, "Actual bound GPU geometry disagrees with the independent literal source square/quaternion oracle." );
			}
		}
	}
	[TestMethod]
	public async Task PublicAbsentFarAndAspectRenderWithoutFiniteSentinel()
	{
		await MainThread.Wait();
		var source = JsonNode.Parse( Encoding.UTF8.GetString( CameraReaderTests.Source() ) );
		source["cameras"][0]["perspective"].AsObject().Remove( "zfar" ); source["cameras"][0]["perspective"].AsObject().Remove( "aspectRatio" );
		var temporary = Path.Combine( Path.GetTempPath(), "camera_infinite_import_" + Guid.NewGuid().ToString( "N" ) ); Directory.CreateDirectory( temporary );
		var name = "camera_infinite_" + Guid.NewGuid().ToString( "N" ) + ".gltf"; File.WriteAllText( Path.Combine( temporary, name ), source.ToJsonString() ); File.Copy( Path.Combine( CameraReaderTests.AssetDirectory, "Cameras.bin" ), Path.Combine( temporary, "Cameras.bin" ) );
		var output = Path.Combine( root, "Temp/gltf-validation/direct-gpu/Cameras/import/infinite" ); Directory.CreateDirectory( output );
		var mount = new LocalFileSystem( temporary ); FileSystem.Mounted.Mount( mount );
		using var cap = new GltfGpuTriangleGateTests.NativeCapture( output ); Model model = null; ModelRenderer renderer = null; GameObject imported = null;
		try
		{
			model = Model.Load( name ); Assert.IsTrue( model.IsValid && !model.IsError );
			using ( cap.Scene.Push() ) { renderer = cap.Scene.CreateObject().Components.Create<ModelRenderer>(); renderer.Model = model; imported = model.ImportGltfCameras( cap.Scene, new global::Vector2( 192, 192 ) ); }
			var camera = imported.GetComponentsInChildren<CameraComponent>().Single( x => !x.Orthographic ); camera.BackgroundColor = global::Color.Black;
			Assert.IsTrue( float.IsPositiveInfinity( camera.ZFar ) ); Assert.IsTrue( camera.CustomProjectionMatrix.HasValue );
			System.Numerics.Matrix4x4 matrix = camera.CustomProjectionMatrix.Value; Assert.AreEqual( 0f, matrix.M33 ); Assert.AreEqual( camera.ZNear, matrix.M34 ); Assert.AreEqual( -1f, matrix.M43 );
			var capture = await CameraCapture.Take( cap, camera, output, "perspective_no_far_or_aspect" ); Assert.IsTrue( capture.Coverage > 100 );
			Assert.IsTrue( camera.GetFrustum().IsInside( camera.WorldPosition + new global::Vector3( -1e6f, 0, 0 ) ) );
			Assert.IsFalse( camera.GetFrustum().IsInside( camera.WorldPosition + new global::Vector3( -.005f, 0, 0 ) / .0254f ) );
			var ray = camera.ScreenNormalToRay( new global::Vector3( 1, 0, 0 ) ); var expected = new global::Vector3( -1, MathF.Tan( .35f ), MathF.Tan( .35f ) ).Normal; Assert.IsTrue( (ray.Forward - expected).Length < 1e-5f );
			Measurements.Add( new { scope = "Public Model.Load and actual imported perspective camera with omitted zfar/aspect; no sentinel, exact infinite reverseZ native projection, independent far/near/ray controls.", capture = capture.Path, capture.Sha256, capture.Coverage, capture.CameraStateJson } );
		}
		finally { await MainThread.Wait(); cap.Widget.Camera = cap.Camera; using ( cap.Scene.Push() ) { imported?.Destroy(); renderer?.GameObject.Destroy(); cap.Scene.EditorTick( 0, 0 ); cap.Scene.ProcessDeletes(); } model?.Destroy(); FileSystem.Mounted.UnMount( mount ); mount.Dispose(); Directory.Delete( temporary, true ); }
	}

	[TestMethod]
	public async Task ExplicitSelectionNoDefaultHierarchyAspectAndRollback()
	{
		await MainThread.Wait();
		var json = JsonNode.Parse( Encoding.UTF8.GetString( CameraReaderTests.Source() ) );
		json.AsObject().Remove( "scene" ); json["scenes"] = JsonNode.Parse( "[{\"nodes\":[3]},{\"nodes\":[2]}]" );
		json["nodes"].AsArray().Add( JsonNode.Parse( "{\"name\":\"Parent\",\"translation\":[1,2,3],\"rotation\":[0,0,0.70710678,0.70710678],\"scale\":[2,2,2],\"children\":[1]}" ) );
		json["cameras"][0]["perspective"].AsObject().Remove( "aspectRatio" ); json["cameras"][0]["perspective"].AsObject().Remove( "zfar" );
		json["cameras"][1]["orthographic"]["xmag"] = 2; json["cameras"][1]["orthographic"]["ymag"] = 1;
		var temporary = Path.Combine( Path.GetTempPath(), "camera_selection_" + Guid.NewGuid().ToString( "N" ) ); Directory.CreateDirectory( temporary );
		var name = "selection_" + Guid.NewGuid().ToString( "N" ) + ".gltf"; File.WriteAllText( Path.Combine( temporary, name ), json.ToJsonString() ); File.Copy( Path.Combine( CameraReaderTests.AssetDirectory, "Cameras.bin" ), Path.Combine( temporary, "Cameras.bin" ) );
		var mount = new LocalFileSystem( temporary ); FileSystem.Mounted.Mount( mount );
		var scene = Scene.CreateEditorScene(); Model unselected = null, selected = null, other = null; GameObject imported = null;
		try
		{
			Assert.IsTrue( Model.Cube.IsValid && !Model.Cube.native.IsNull );
			using ( scene.Push() ) Assert.ThrowsException<InvalidOperationException>( () => Model.Cube.ImportGltfCameras( scene, new global::Vector2( 192, 192 ) ) );
			Assert.IsTrue( Model.Cube.IsValid && !Model.Cube.native.IsNull ); Assert.AreEqual( 0, scene.Children.Count );
			unselected = Model.Load( name ); Assert.IsTrue( unselected.IsValid && !unselected.IsError ); Assert.IsNull( unselected.GltfSelectedScene );
			using ( scene.Push() ) Assert.ThrowsException<InvalidOperationException>( () => unselected.ImportGltfCameras( scene, new global::Vector2( 384, 192 ) ) );
			Assert.IsTrue( unselected.IsValid ); Assert.AreEqual( 0, scene.Children.Count );
			selected = Model.Load( name, 0 ); other = Model.Load( name, 1 ); Assert.AreNotSame( unselected, selected ); Assert.AreNotSame( other, selected );
			using ( scene.Push() ) imported = selected.ImportGltfCameras( scene, new global::Vector2( 384, 192 ) );
			var camera = imported.GetComponentsInChildren<CameraComponent>().Single();
			// Independently: scale (0.5,0.5,3) by 2, rotate +90 about Z, translate (1,2,3) => (0,3,9).
			Assert.IsTrue( (camera.WorldPosition - new global::Vector3( 9, 0, 3 ) / .0254f).Length < 5e-4f );
			Assert.IsTrue( (camera.WorldRotation.Forward - new global::Vector3( -1, 0, 0 )).Length < 1e-5f );
			Assert.IsTrue( (camera.WorldRotation.Up - new global::Vector3( 0, -1, 0 )).Length < 1e-5f );
			Assert.AreEqual( "Parent", camera.GameObject.Parent.Parent.Name ); Assert.IsTrue( float.IsPositiveInfinity( camera.ZFar ) );
			Assert.AreEqual( new global::Vector4( 0, 0, 1, 1 ), camera.Viewport ); Assert.AreEqual( new global::Vector2( 384, 192 ), camera.CustomSize.Value );
			using ( scene.Push() ) { imported.Destroy(); scene.ProcessDeletes(); imported = other.ImportGltfCameras( scene, new global::Vector2( 192, 192 ) ); }
			camera = imported.GetComponentsInChildren<CameraComponent>().Single(); Assert.IsTrue( camera.Orthographic );
			Assert.AreEqual( new global::Vector4( 0, .25f, 1, .5f ), camera.Viewport ); Assert.AreEqual( 2f / .0254f, camera.OrthographicHeight, 1e-5f );
			var before = scene.Children.Count; using ( scene.Push() ) Assert.ThrowsException<ArgumentOutOfRangeException>( () => other.ImportGltfCameras( scene, new global::Vector2( 0, 192 ) ) ); Assert.AreEqual( before, scene.Children.Count );
			other.Destroy(); other = null; Assert.IsTrue( camera.IsValid() && camera.Enabled, "Imported camera snapshot must not borrow a disposed resource document." );
		}
		finally { using ( scene.Push() ) { imported?.Destroy(); scene.ProcessDeletes(); } scene.Destroy(); unselected?.Destroy(); selected?.Destroy(); other?.Destroy(); FileSystem.Mounted.UnMount( mount ); mount.Dispose(); Directory.Delete( temporary, true ); }
	}
}
