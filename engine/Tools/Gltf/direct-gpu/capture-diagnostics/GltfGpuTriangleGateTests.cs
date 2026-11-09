using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Sandbox.Gltf.DirectGpu;

using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

/// <summary>
/// First GPU execution gate. The reference is a literal independently authored
/// triangle. No Model or Mesh resource is constructed by either draw path.
/// </summary>
[TestClass]
public sealed class GltfGpuTriangleGateTests
{
	public static string CheckoutRoot { get; set; }
	public static List<object> Measurements { get; } = new();
	public static void Configure( string checkoutRoot ) { CheckoutRoot = checkoutRoot; Measurements.Clear(); }

	[TestMethod]
	public async Task DescriptorDrivenComputeRawDrawReadbackAndDeletionControls()
	{
		Assert.IsTrue( Application.IsEditor && !Game.IsPlaying && !Graphics.IsActive );
		var output = Path.Combine( CheckoutRoot, "Temp/gltf-validation/direct-gpu/raw-triangle", Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( output );
		var capture = new NativeCapture( output );
		Shader shader = null; Material compute = null; GltfGpuAsset asset = null; GltfGpuInstance instance = null; GltfDocument document = null;
		SceneCustomObject reference = null; var referenceErrors = new ConcurrentQueue<string>();
		var referenceCalls = 0;
		try
		{
			await capture.WaitFrames();
			var empty = await capture.Take( "empty_before" ); AssertEmpty( empty );
			var material = capture.Material;
			var vertices = new[]
			{
				LiteralVertex( new global::Vector3( 0, 0, 0 ) ),
				LiteralVertex( new global::Vector3( 0, 1f / .0254f, 0 ) ),
				LiteralVertex( new global::Vector3( 0, 0, 1f / .0254f ) )
			};
			reference = new SceneCustomObject( capture.Scene.SceneWorld ) { Bounds = new BBox( new global::Vector3( -1, -1, -1 ), new global::Vector3( 1, 41, 41 ) ) };
			reference.Flags.IsOpaque = true; reference.Flags.IsTranslucent = false;
			reference.RenderOverride = _ =>
			{
				try { referenceCalls++; Graphics.Draw( vertices.AsSpan(), vertices.Length, material ); }
				catch ( Exception e ) { referenceErrors.Enqueue( e.ToString() ); }
			};
			var expected = await capture.Take( "literal_reference" );
			Assert.IsTrue( expected.Coverage > 64 && referenceCalls > 0 && referenceErrors.IsEmpty, "The independent reference must actually draw without callback errors." );
			reference.RenderOverride = null; reference.Delete(); reference = null;
			using ( capture.Scene.Push() ) capture.Scene.ProcessDeletes();
			await capture.WaitFrames(); var referenceCallsAfterDelete = referenceCalls;
			AssertEmpty( await capture.Take( "empty_after_reference" ) );
			Assert.AreEqual( referenceCallsAfterDelete, referenceCalls, "Deleted reference callback still ran." );

			var shaderPath = Path.Combine( CheckoutRoot, "engine/Tools/Gltf/direct-gpu/shaders/gltf_direct_deform.shader" );
			var shaderBytes = File.ReadAllBytes( shaderPath );
			File.WriteAllBytes( Path.Combine( output, "compute-source.shader" ), shaderBytes );
			Measurements.Add( new { phase = "anonymous_compute_requested", shader_sha256 = Hash( shaderBytes ), output } );
			using ( var timeout = new CancellationTokenSource( TimeSpan.FromSeconds( 90 ) ) )
				shader = await Shader.CompileAsync( System.Text.Encoding.UTF8.GetString( shaderBytes ), timeout.Token );
			await MainThread.Wait();
			Assert.IsTrue( shader is { IsValid: true, IsAnonymous: true }, "Anonymous research compute shader did not compile/load." );
			Assert.AreEqual( Hash( shaderBytes ), Hash( File.ReadAllBytes( shaderPath ) ), "Shader source changed during compilation." );
			compute = Material.Create( "gltf_direct_triangle_" + Guid.NewGuid().ToString( "N" ), shader );
			Assert.IsTrue( compute is { IsValid: true, IsError: false } );
			Measurements.Add( new { phase = "anonymous_compute_compiled", shader_sha256 = Hash( shaderBytes ), shader.IsAnonymous, scope = "Anonymous diagnostic shader; no watched core shader or global render mode changed." } );
			document = GltfGpuBackendContractTests.InterleavedTriangle();
			asset = GltfGpuAsset.Create( document, compute, _ => material );
			instance = asset.CreateInstance( capture.Scene.SceneWorld );
			var actual = await capture.Take( "direct_gpu_actual" );
			Assert.IsTrue( actual.Coverage > 64, "Raw GPU draw produced no visible geometry." );
			Assert.IsTrue( instance.CallbackErrors.Length == 0, string.Join( "\n", instance.CallbackErrors ) );
			Measurements.Add( GltfGpuRawTriangleChecks.Validate( instance ) );
			var intersection = 0; var union = 0; var maximumRgb = 0f; var sumRgb = 0d;
			for ( var i = 0; i < actual.Pixels.Length; ++i )
			{
				var a = NativeCapture.Foreground( actual.Pixels[i] ); var b = NativeCapture.Foreground( expected.Pixels[i] );
				if ( a && b ) intersection++; if ( a || b ) union++;
				foreach ( var difference in new[] { MathF.Abs( actual.Pixels[i].r - expected.Pixels[i].r ), MathF.Abs( actual.Pixels[i].g - expected.Pixels[i].g ), MathF.Abs( actual.Pixels[i].b - expected.Pixels[i].b ) } )
				{ maximumRgb = Math.Max( maximumRgb, difference ); sumRgb += difference; }
			}
			var iou = union == 0 ? 0 : intersection / (double)union;
			Measurements.Add( new { phase = "literal_vs_direct_gpu", intersection, union, iou, maximum_rgb_error = maximumRgb, mean_rgb_error = sumRgb / (actual.Pixels.Length * 3), scope = "Matched literal triangle geometry coverage; RGB measured separately. No model material, animation, Model.Load or performance parity claim." } );
			Assert.IsTrue( iou >= .99, "Direct GPU triangle coverage disagrees with the independent literal reference: " + iou );
			// This owned-buffer mutation distinguishes actual bound GPU index
			// readback from a clone of the CPU submission metadata.
			var draws = (System.Collections.IEnumerable)typeof( GltfGpuInstance ).GetField( "draws", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic ).GetValue( instance );
			var draw = draws.Cast<object>().Single();
			var indexBuffer = (GpuBuffer)draw.GetType().GetField( "Indices", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic ).GetValue( draw );
			var originalIndices = new uint[3]; indexBuffer.GetData<uint>( originalIndices );
			try
			{
				indexBuffer.SetData<uint>( new uint[] { 0, 2, 1 } );
				var reversed = await capture.Take( "reversed_bound_gpu_indices" );
				var observedIndices = instance.Readback().Single();
				Assert.AreEqual( "gpu-readback", observedIndices.IndexDataOrigin );
				CollectionAssert.AreEqual( new uint[] { 0, 2, 1 }, observedIndices.Indices );
				Assert.ThrowsException<InvalidOperationException>( () => GltfGpuRawTriangleChecks.Validate( instance ), "The independent checker must reject actual GPU index corruption." );
				AssertEmpty( reversed );
				Measurements.Add( new { phase = "gpu_index_negative_control", observedIndices.IndexDataOrigin, indices = observedIndices.Indices, coverage = reversed.Coverage, scope = "Only test-owned bound index bytes changed; CPU topology metadata and pose revision unchanged." } );
			}
			finally { indexBuffer.SetData<uint>( originalIndices ); await capture.WaitFrames(); }
			var restored = await capture.Take( "restored_bound_gpu_indices" );
			Assert.AreEqual( actual.Coverage, restored.Coverage );
			Measurements.Add( GltfGpuRawTriangleChecks.Validate( instance ) );
			var oldInstance = instance; var beforeDeleteDraws = instance.DrawCalls;
			instance.Dispose(); instance = null;
			using ( capture.Scene.Push() ) capture.Scene.ProcessDeletes();
			AssertEmpty( await capture.Take( "empty_after_direct_gpu" ) );
			Assert.AreEqual( beforeDeleteDraws, oldInstance.DrawCalls, "Deleted GPU instance continued drawing." );
			Assert.IsTrue( oldInstance.CallbackErrors.Length == 0, string.Join( "\n", oldInstance.CallbackErrors ) );
			asset.Dispose(); asset = null;
			Assert.IsTrue( compute.IsValid && !compute.IsError, "Asset destroyed the caller's borrowed compute material." );
			Measurements.Add( new { phase = "complete", passed = true, output, scope = "One descriptor-driven raw GPU triangle plus independent readback and fresh native reference/deletion controls on the current Linux GPU only." } );
		}
		finally
		{
			await MainThread.Wait();
			if ( reference.IsValid() ) { reference.RenderOverride = null; reference.Delete(); }
			instance?.Dispose(); asset?.Dispose(); document?.Dispose(); compute?.Destroy(); shader?.Destroy(); capture.Dispose();
		}
	}

	private static Vertex LiteralVertex( global::Vector3 position ) => new Vertex( position )
	{
		Normal = global::Vector3.Forward, Tangent = new global::Vector4( 0, 1, 0, 1 ),
		Color = new global::Color32( 64, 128, 192, 255 )
	};
	private static string Hash( byte[] bytes ) => Convert.ToHexStringLower( SHA256.HashData( bytes ) );
	private static void AssertEmpty( NativeCapture.Frame frame ) => Assert.AreEqual( 0, frame.Coverage, "Fresh empty scene retained pixels: " + frame.Path );

	public sealed class NativeCapture : IDisposable
	{
		public sealed record Frame( global::Color[] Pixels, int Coverage, string Path, string Sha256, int Width, int Height, ulong EngineFrame, double ObservedWallclockSeconds, string CameraStateJson );
		public readonly Scene Scene;
		public readonly Material Material;
		public CameraComponent Camera => camera;
		public Editor.SceneRenderingWidget Widget => widget;
		private readonly CameraComponent camera;
		private readonly Editor.SceneRenderingWidget widget;
		private readonly ScenePointLight light;
		private readonly string output;
		private readonly Action onFrame;
		private readonly Stopwatch stopwatch = Stopwatch.StartNew();
		private int frames;
		public System.Threading.CancellationToken Cancellation { get; set; }
		private const int Size = 192;

		public NativeCapture( string output, global::Vector3? position = null, global::Vector3? target = null, float orthographicHeight = 100 )
		{
			this.output = output;
			RequireNativeRenderer();
			Scene = Scene.CreateEditorScene();
			Material = Material.Load( "materials/default/white.vmat" ).CreateCopy( "gltf_triangle_capture_" + Guid.NewGuid().ToString( "N" ) );
			Assert.IsTrue( Material is { IsValid: true, IsError: false } ); Material.SetFeature( "F_RENDER_BACKFACES", 0 );
			using ( Scene.Push() )
			{
				camera = Scene.CreateObject().Components.Create<CameraComponent>();
				camera.WorldPosition = position ?? new global::Vector3( 200, 20, 20 );
				camera.WorldRotation = position.HasValue ? Rotation.LookAt( (target ?? global::Vector3.Zero) - position.Value, global::Vector3.Up ) : Rotation.FromYaw( 180 );
				camera.Orthographic = true; camera.OrthographicHeight = orthographicHeight; camera.ZNear = 1; camera.ZFar = 1000;
				camera.CustomSize = new global::Vector2( Size, Size ); camera.BackgroundColor = global::Color.Black; camera.ClearFlags = ClearFlags.All;
				camera.EnablePostProcessing = true; camera.DebugMode = SceneCameraDebugMode.Normal;
			}
			light = new ScenePointLight( Scene.SceneWorld, new global::Vector3( 200, -30, 60 ), 1000, global::Color.White * 4 );
			widget = new Editor.SceneRenderingWidget { Scene = Scene, Camera = camera, WindowTitle = "glTF direct GPU triangle validation", Size = new global::Vector2( 384, 384 ) };
			onFrame = () => frames++; widget.OnPreFrame += onFrame; widget.Show();
		}

		public static bool Foreground( global::Color p ) => p.r > .03f || p.g > .03f || p.b > .03f;
		private static void RequireNativeRenderer()
		{
			var type = typeof( Scene ).Assembly.GetType( "Sandbox.Rendering.ManagedSceneRendering", true );
			var property = type.GetProperty( "Enabled", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public );
			Assert.IsFalse( (bool)property.GetValue( null ), "Keep the existing native renderer; this probe never changes the global renderer." );
		}
		private async Task Safe()
		{
			Cancellation.ThrowIfCancellationRequested(); await MainThread.Wait(); Cancellation.ThrowIfCancellationRequested(); Assert.IsTrue( Application.IsEditor && !Game.IsPlaying ); RequireNativeRenderer();
			var timer = Stopwatch.StartNew();
			while ( Graphics.IsActive ) { Assert.IsTrue( timer.Elapsed < TimeSpan.FromSeconds( 5 ) ); await Task.Delay( 20, Cancellation ); await MainThread.Wait(); }
		}
		public async Task WaitFrames()
		{
			await Safe(); var start = frames; var timer = Stopwatch.StartNew();
			while ( frames < start + 3 ) { Assert.IsTrue( timer.Elapsed < TimeSpan.FromSeconds( 10 ), "Visible preview stopped receiving real frames." ); await Task.Delay( 20, Cancellation ); await Safe(); }
		}
		public async Task<Frame> Take( string name )
		{
			await WaitFrames();
			using ( Scene.Push() ) { Scene.PreCameraRender(); camera.InitializeRendering( false ); }
			Assert.AreEqual( new global::Vector2( Size, Size ), camera.CustomSize.Value );
			using var target = Texture.CreateRenderTarget().WithSize( Size, Size ).WithFormat( ImageFormat.RGBA8888 ).WithInitialColor( global::Color.Magenta ).Create( "gltf_direct_gate_" + Guid.NewGuid().ToString( "N" ) );
			bool submitted;
			using ( Scene.Push() ) { target.Clear( global::Color.Magenta ); submitted = camera.RenderToTexture( target ); }
			Assert.IsTrue( submitted, "Native camera rejected the capture." );
			await WaitFrames(); using var bitmap = target.GetBitmap(); Assert.IsNotNull( bitmap );
			var pixels = bitmap.GetPixels(); Assert.AreEqual( Size * Size, pixels.Length );
			var png = bitmap.ToPng(); var path = Path.Combine( output, name + ".png" ); File.WriteAllBytes( path, png );
			var coverage = pixels.Count( Foreground );
			RequireNativeRenderer();
			Measurements.Add( new { phase = "capture", name, path, sha256 = Hash( png ), width = Size, height = Size, coverage, engine_frame = Application.FrameCount, widget_frames = frames,
				elapsed_seconds = stopwatch.Elapsed.TotalSeconds, camera_position = camera.WorldPosition.ToString(), camera_rotation = camera.WorldRotation.ToString(), custom_size = camera.CustomSize?.ToString(),
				scene_camera_size = camera.SceneCamera.Size.ToString(), screen_rect = camera.ScreenRect.ToString(), viewport = camera.Viewport.ToString(), camera.DebugMode, camera.EnablePostProcessing,
				camera.Orthographic, camera.OrthographicHeight, camera.ZNear, camera.ZFar, point_light_position = light.Position.ToString(), point_light_radius = light.Radius, point_light_color = light.LightColor.ToString(),
				scope = "Fresh192px diagnostic native camera capture; not a1080p performance benchmark." } );
			var cameraState = JsonSerializer.Serialize( new { position = camera.WorldPosition.ToString(), rotation = camera.WorldRotation.ToString(), custom_size = camera.CustomSize?.ToString(), scene_size = camera.SceneCamera.Size.ToString(), viewport = camera.Viewport.ToString(), debug_mode = camera.DebugMode.ToString(), camera.EnablePostProcessing,
				camera.Orthographic, camera.OrthographicHeight, camera.ZNear, camera.ZFar, point_light_position = light.Position.ToString(), point_light_radius = light.Radius, point_light_color = light.LightColor.ToString() } );
			return new Frame( pixels, coverage, path, Hash( png ), Size, Size, Application.FrameCount, stopwatch.Elapsed.TotalSeconds, cameraState );
		}
		public void Dispose()
		{
			if ( widget.IsValid ) { widget.OnPreFrame -= onFrame; widget.Close(); widget.Destroy(); }
			if ( light.IsValid() ) light.Delete(); Scene.Destroy(); Material.Destroy();
		}
	}
}
