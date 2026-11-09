using Sandbox.Gltf;
using Sandbox.Rendering;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ResourceTests;

/// <summary>
/// Positive GPU acceptance for authored rest geometry through both existing
/// renderer components. Analytic native references isolate geometry from material
/// fidelity, animation playback, source skeleton APIs and screenshot camera choice.
/// </summary>
[TestClass]
public sealed class GltfModelRestPoseTests
{
	[TestMethod]
	[DataRow( false, false, false )]
	[DataRow( false, false, true )]
	[DataRow( false, true, false )]
	[DataRow( false, true, true )]
	[DataRow( true, false, false )]
	[DataRow( true, false, true )]
	[DataRow( true, true, false )]
	[DataRow( true, true, true )]
	public async Task RendererComponentDisplaysTheAuthoredRestGeometry( bool skinned, bool compiled, bool animatedComponent )
	{
		if ( !Graphics.IsAvailable ) Assert.Inconclusive( "Requires an authorized in-engine graphics session." );
		Assert.IsTrue( ThreadSafe.IsMainThread );
		Assert.IsNotNull( ManagedSceneRendering.Renderer, "The managed capture route must be installed; this test makes no native/default-renderer claim." );
		using var fixture = new Fixture( skinned, compiled );
		var empty = await fixture.Capture();
		Assert.AreEqual( 0, Coverage( empty ), "A fresh target must show an empty scene before the reference." );
		var reference = fixture.CreateRenderer( fixture.Reference, animatedComponent: false );
		var expected = await fixture.Capture();
		Assert.IsTrue( Coverage( expected ) > 100, "The independent native reference must be visible." );
		fixture.DestroyRenderer( reference );
		Assert.AreEqual( 0, Coverage( await fixture.Capture() ), "The native reference must disappear before loading the actual component." );
		var renderer = fixture.CreateRenderer( fixture.Imported, animatedComponent );
		var actual = await fixture.Capture();
		var union = 0;
		var intersection = 0;
		for ( var pixel = 0; pixel < expected.Length; pixel++ )
		{
			var a = Visible( expected[pixel] );
			var b = Visible( actual[pixel] );
			if ( a || b ) union++;
			if ( a && b ) intersection++;
		}
		var overlap = union == 0 ? 0 : (double)intersection / union;
		Assert.IsTrue( Coverage( actual ) > 100 && overlap >= .99,
			$"{renderer.GetType().Name}: loaded glTF rest geometry differs from the analytic native reference (skin={skinned}, compiled={compiled}, IoU={overlap}, pixels={Coverage( actual )})." );
		fixture.DestroyRenderer( renderer );
		Assert.AreEqual( 0, Coverage( await fixture.Capture() ), "Actual geometry must disappear after normal GameObject destruction." );
	}

	private static bool Visible( Color value ) => MathF.Abs( value.r ) > .03f || MathF.Abs( value.g ) > .03f || MathF.Abs( value.b ) > .03f;
	private static int Coverage( Color[] pixels ) => pixels.Count( Visible );

	private sealed class Fixture : IDisposable
	{
		private const float Unit = 1f / .0254f;
		private readonly string directory;
		private readonly BaseFileSystem aggregate;
		private LocalFileSystem sourceMount;
		private Scene scene;
		private CameraComponent camera;
		private Material material;
		private Mesh mesh;
		internal Model Imported { get; private set; }
		internal Model Reference { get; private set; }

		internal Fixture( bool skinned, bool compiled )
		{
			directory = Path.Combine( Path.GetTempPath(), "gltf_rest_" + Guid.NewGuid().ToString( "N" ) );
			Directory.CreateDirectory( directory );
			aggregate = Sandbox.FileSystem.Mounted;
			try
			{
				var template = Material.Load( "materials/default/white.vmat" );
				Assert.IsTrue( template is { IsValid: true, IsError: false } );
				material = template.CreateCopy(); material.SetFeature( "F_RENDER_BACKFACES", 1 );
				var source = Source( skinned );
				var filename = "rest_" + Guid.NewGuid().ToString( "N" ) + (compiled ? ".glb_c" : ".gltf");
				File.WriteAllBytes( Path.Combine( directory, filename ), compiled ? GltfCompiler.Compile( source ) : source );
				sourceMount = new LocalFileSystem( directory ); aggregate.Mount( sourceMount );
				Imported = Model.Load( filename ); Assert.IsTrue( Imported is { IsValid: true, IsError: false } );
				// Independent rest positions: either source Ry(180)*T(1,2,3),
				// or inverseBind T(-.25,-.5,-1) * joint T(1,2,3), then (z,x,y)/.0254.
				Vector3[] points = skinned
					? [new( 2 * Unit, .75f * Unit, 1.5f * Unit ), new( 2 * Unit, 1.75f * Unit, 1.5f * Unit ), new( 2 * Unit, .75f * Unit, 2.5f * Unit )]
					: [new( 3 * Unit, Unit, 2 * Unit ), new( 3 * Unit, 0, 2 * Unit ), new( 3 * Unit, Unit, 3 * Unit )];
				var bounds = BBox.FromPoints( points );
				var vertices = points.Select( point => new Vertex( point ) { Normal = Vector3.Forward, Tangent = new Vector4( 0, 1, 0, 1 ) } ).ToArray();
				mesh = new Mesh( "analytic_rest_reference", material ); mesh.CreateVertexBuffer( 3, vertices.AsSpan() ); mesh.CreateIndexBuffer( 3, new[] { 0, 1, 2 }.AsSpan() ); mesh.Bounds = bounds;
				Reference = Model.Builder.WithName( "rest_reference_" + Guid.NewGuid().ToString( "N" ) ).AddMesh( mesh ).Create();
				scene = Scene.CreateEditorScene();
				using var scope = scene.Push();
				camera = scene.CreateObject().Components.Create<CameraComponent>(); camera.BackgroundColor = Color.Black;
				camera.DebugMode = SceneCameraDebugMode.Albedo; camera.EnablePostProcessing = false; camera.FieldOfView = 50;
				var center = bounds.Center;
				camera.WorldPosition = center + Vector3.Forward * (6 * Unit); camera.WorldRotation = Rotation.LookAt( center - camera.WorldPosition ); camera.ZNear = 1; camera.ZFar = 20 * Unit;
			}
			catch { Dispose(); throw; }
		}

		internal ModelRenderer CreateRenderer( Model model, bool animatedComponent )
		{
			using var scope = scene.Push();
			var gameObject = scene.CreateObject();
			ModelRenderer renderer = animatedComponent ? gameObject.Components.Create<SkinnedModelRenderer>( false ) : gameObject.Components.Create<ModelRenderer>( false );
			renderer.Model = model; renderer.MaterialOverride = material;
			if ( renderer is SkinnedModelRenderer animated ) { animated.UseAnimGraph = false; animated.PlaybackRate = 0; animated.PlayAnimationsInEditorScene = true; }
			renderer.Enabled = true; scene.EditorTick( 0, 0 );
			Assert.IsTrue( renderer.SceneObject.IsValid() );
			return renderer;
		}

		internal void DestroyRenderer( ModelRenderer renderer )
		{
			using var scope = scene.Push();
			renderer.GameObject.Destroy();
			scene.EditorTick( 0, 0 );
		}

		internal async Task<Color[]> Capture()
		{
			await WaitFrames();
			using var target = Texture.CreateRenderTarget().WithSize( 192, 192 ).WithFormat( ImageFormat.RGBA8888 ).WithInitialColor( Color.Magenta ).Create();
			using ( scene.Push() )
			{
				scene.EditorTick( 0, 0 );
				// A direct submission has no SceneRenderingWidget to prepare the
				// camera for us. Follow the normal camera rendering lifecycle.
				scene.PreCameraRender();
				camera.InitializeRendering( renderUI: false );
				target.Clear( Color.Magenta );
				Assert.IsTrue( ManagedSceneRendering.Renderer.RenderToTexture( camera, target ), "Actual scene capture submission must succeed." );
			}
			await WaitFrames();
			using var bitmap = target.GetBitmap(); Assert.IsNotNull( bitmap, "The fresh GPU target must be readable." );
			var pixels = bitmap.GetPixels(); Assert.AreEqual( 192 * 192, pixels.Length, "Readback must contain the actual target dimensions." );
			return pixels;
		}

		private static async Task WaitFrames()
		{
			var start = Application.FrameCount; var timer = Stopwatch.StartNew();
			while ( Application.FrameCount < start + 3 )
			{
				Assert.IsTrue( timer.Elapsed < TimeSpan.FromSeconds( 8 ), "The authorized engine session must keep receiving real frames." );
				await Task.Delay( 20 ); await MainThread.Wait();
			}
		}

		public void Dispose()
		{
			scene?.Destroy(); scene = null; Imported?.Destroy(); Imported = null; Reference?.Destroy(); Reference = null;
			if ( mesh is not null ) { GltfModelLoader.ReleaseMeshes( [mesh] ); mesh = null; }
			material?.Destroy(); material = null;
			if ( sourceMount is not null ) { aggregate.UnMount( sourceMount ); sourceMount.Dispose(); sourceMount = null; }
			if ( Directory.Exists( directory ) ) Directory.Delete( directory, recursive: true );
		}
	}

	private static byte[] Source( bool skinned )
	{
		using var stream = new MemoryStream();
		using ( var writer = new BinaryWriter( stream, Encoding.UTF8, leaveOpen: true ) )
		{
			foreach ( var value in new float[] { 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 1, 1, 2, 3, 2, 2, 3 } ) writer.Write( value );
			if ( skinned )
			{
				writer.Write( new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 } );
				foreach ( var value in new float[] { 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, -.25f, -.5f, -1, 1 } ) writer.Write( value );
			}
		}
		var binary = stream.ToArray();
		var extraViews = skinned ? ",{\"buffer\":0,\"byteOffset\":68,\"byteLength\":12},{\"buffer\":0,\"byteOffset\":80,\"byteLength\":48},{\"buffer\":0,\"byteOffset\":128,\"byteLength\":64}" : "";
		var extraAccessors = skinned ? ",{\"bufferView\":3,\"componentType\":5121,\"count\":3,\"type\":\"VEC4\"},{\"bufferView\":4,\"componentType\":5126,\"count\":3,\"type\":\"VEC4\"},{\"bufferView\":5,\"componentType\":5126,\"count\":1,\"type\":\"MAT4\"}" : "";
		var attributes = skinned ? ",\"JOINTS_0\":3,\"WEIGHTS_0\":4" : "";
		var nodes = skinned ? "[{\"mesh\":0,\"skin\":0,\"translation\":[100,200,300]},{\"translation\":[1,2,3]}]" : "[{\"mesh\":0,\"rotation\":[0,-1,0,0],\"translation\":[1,2,3]}]";
		var skin = skinned ? ",\"skins\":[{\"joints\":[1],\"inverseBindMatrices\":5}]" : "";
		return Encoding.UTF8.GetBytes( "{\"asset\":{\"version\":\"2.0\"},\"buffers\":[{\"uri\":\"data:application/octet-stream;base64," + Convert.ToBase64String( binary ) + "\",\"byteLength\":" + binary.Length + "}],"
			+ "\"bufferViews\":[{\"buffer\":0,\"byteLength\":36},{\"buffer\":0,\"byteOffset\":36,\"byteLength\":8},{\"buffer\":0,\"byteOffset\":44,\"byteLength\":24}" + extraViews + "],"
			+ "\"accessors\":[{\"bufferView\":0,\"componentType\":5126,\"count\":3,\"type\":\"VEC3\",\"min\":[0,0,0],\"max\":[1,1,0]},{\"bufferView\":1,\"componentType\":5126,\"count\":2,\"type\":\"SCALAR\",\"min\":[0],\"max\":[1]},{\"bufferView\":2,\"componentType\":5126,\"count\":2,\"type\":\"VEC3\"}" + extraAccessors + "],"
			+ "\"meshes\":[{\"primitives\":[{\"attributes\":{\"POSITION\":0" + attributes + "}}]}],\"nodes\":" + nodes + skin
			+ ",\"animations\":[{\"name\":\"Move\",\"samplers\":[{\"input\":1,\"output\":2}],\"channels\":[{\"sampler\":0,\"target\":{\"node\":" + (skinned ? 1 : 0) + ",\"path\":\"translation\"}}]}],\"scenes\":[{\"nodes\":" + (skinned ? "[0,1]" : "[0]") + "}],\"scene\":0}" );
	}
}
