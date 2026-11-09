using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Sandbox.Gltf.DirectGpu.CamerasValidation;

[TestClass]
public sealed class CameraTransformTests
{
	static string root;
	public static List<object> Measurements { get; } = new();
	public static void Configure( string checkout ) { root = checkout; CameraReaderTests.Configure( checkout ); Measurements.Clear(); }
	static JsonNode Source( string parentScale, string childRotation )
	{
		var source = JsonNode.Parse( Encoding.UTF8.GetString( CameraReaderTests.Source() ) );
		source["scenes"][0]["nodes"] = JsonNode.Parse( "[0,3]" );
		source["nodes"].AsArray().Add( JsonNode.Parse( "{\"name\":\"Scaled parent\",\"translation\":[1,2,3],\"scale\":" + parentScale + ",\"children\":[1]}" ) );
		source["nodes"][1]["rotation"] = JsonNode.Parse( childRotation ); return source;
	}
	sealed class Mounted : IDisposable
	{
		readonly string folder; readonly LocalFileSystem system;
		internal readonly string Name;
		internal Mounted( JsonNode source )
		{
			folder = Path.Combine( Path.GetTempPath(), "gltf_camera_transform_" + Guid.NewGuid().ToString( "N" ) ); Directory.CreateDirectory( folder ); Name = "camera_transform_" + Guid.NewGuid().ToString( "N" ) + ".gltf";
			File.WriteAllText( Path.Combine( folder, Name ), source.ToJsonString() ); File.Copy( Path.Combine( CameraReaderTests.AssetDirectory, "Cameras.bin" ), Path.Combine( folder, "Cameras.bin" ) ); system = new LocalFileSystem( folder ); FileSystem.Mounted.Mount( system );
		}
		public void Dispose() { FileSystem.Mounted.UnMount( system ); system.Dispose(); Directory.Delete( folder, true ); }
	}
	[DataTestMethod]
	[DataRow( false )] [DataRow( true )]
	public async Task NonuniformAncestorWithOrthogonalGlobalAxesAcceptsTrsAndEquivalentMatrix( bool matrixForm )
	{
		await MainThread.Wait(); var source = Source( "[2,3,4]", "[0,0,0.7071067811865476,0.7071067811865476]" );
		if ( matrixForm )
		{
			source["nodes"][1].AsObject().Remove( "rotation" ); source["nodes"][1].AsObject().Remove( "translation" );
			source["nodes"][1]["matrix"] = JsonNode.Parse( "[0,1,0,0,-1,0,0,0,0,0,1,0,0.5,0.5,3,1]" );
		}
		using var mount = new Mounted( source ); var scene = Scene.CreateEditorScene(); Model model = null; GameObject imported = null;
		try
		{
			model = Model.Load( mount.Name ); Assert.IsTrue( model.IsValid && !model.IsError );
			using ( scene.Push() ) imported = model.ImportGltfCameras( scene, new global::Vector2( 192, 192 ) );
			var camera = imported.GetComponentsInChildren<CameraComponent>().Single();
			// Independent: (0.5,0.5,3)*parentScale + parentTranslation = (2,3.5,15).
			Assert.IsTrue( (camera.WorldPosition - new global::Vector3( 15, 2, 3.5f ) / .0254f).Length < .002f );
			Assert.IsTrue( (camera.WorldRotation.Forward - new global::Vector3( -1, 0, 0 )).Length < 2e-5f );
			Assert.IsTrue( (camera.WorldRotation.Up - new global::Vector3( 0, -1, 0 )).Length < 2e-5f );
			Assert.IsTrue( camera.Enabled && camera.IsMainCamera ); Assert.AreEqual( 1, camera.Priority );
			Measurements.Add( new { matrixForm, supported = true, scope = "Positive camera-global TRS with nonuniform scaled ancestor and90degree axis permutation; equivalent source local TRS/matrix, independent pose and source-axis oracle. No global shear/reflection claim." } );
		}
		finally { using ( scene.Push() ) { imported?.Destroy(); scene.ProcessDeletes(); } scene.Destroy(); model?.Destroy(); }
	}
	[TestMethod]
	public async Task ValidLocalTrsWithGlobalShearIsClearUnsupportedAndTransactional()
	{
		await Unsupported( Source( "[2,3,4]", "[0,0,0.3826834323650898,0.9238795325112867]" ), "scale cannot be separated", "global-shear" );
	}
	[TestMethod]
	public async Task ValidReflectedScaleIsClearUnsupportedAndResourceRemainsValid()
	{
		await Unsupported( Source( "[-2,3,4]", "[0,0,0,1]" ), "Reflected", "reflected-global-transform" );
	}
	static async Task Unsupported( JsonNode source, string message, string kind )
	{
		await MainThread.Wait(); using var mount = new Mounted( source ); var scene = Scene.CreateEditorScene(); Model model = null;
		try
		{
			CameraComponent sentinel; using ( scene.Push() ) sentinel = scene.CreateObject().Components.Create<CameraComponent>();
			var count = scene.Children.Count; var position = sentinel.WorldPosition; var selected = scene.Camera;
			model = Model.Load( mount.Name ); Assert.IsTrue( model.IsValid && !model.IsError && model.MeshCount == 1 );
			GltfException error; using ( scene.Push() ) error = Assert.ThrowsException<GltfException>( () => model.ImportGltfCameras( scene, new global::Vector2( 192, 192 ) ) );
			StringAssert.Contains( error.Message, message ); Assert.IsTrue( model.IsValid && !model.IsError ); Assert.AreEqual( count, scene.Children.Count ); Assert.AreSame( selected, scene.Camera ); Assert.AreEqual( position, sentinel.WorldPosition ); Assert.IsTrue( sentinel.Enabled && sentinel.IsMainCamera ); Assert.AreEqual( 1, sentinel.Priority );
			Measurements.Add( new { kind, resource_valid = true, import_supported = false, applicable_core_pending = true, diagnostic = error.Message, scope = "Clear unsupported import regression only; NOT a camera-transform feature pass or corpus exclusion. No scene mutation and no unrelated-camera change." } );
		}
		finally { model?.Destroy(); scene.Destroy(); }
	}
}
