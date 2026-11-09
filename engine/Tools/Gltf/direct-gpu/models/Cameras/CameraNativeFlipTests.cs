using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Sandbox.Rendering;

namespace Sandbox.Gltf.DirectGpu.CamerasValidation;

/// <summary>Existing per-submission flip contract, kept separate from camera-import support.</summary>
[TestClass]
public sealed class CameraNativeFlipTests
{
	static string root;
	public static List<object> Measurements { get; } = new();
	public static void Configure( string checkout ) { root = checkout; CameraReaderTests.Configure( checkout ); Measurements.Clear(); }
	[TestMethod]
	public async Task ExistingNativeFinalBlitFlipsPixelsAndPreservesCameraQueries()
	{
		await MainThread.Wait();
		var output = Path.Combine( root, "Temp/gltf-validation/direct-gpu/Cameras/native-flip", Guid.NewGuid().ToString( "N" ) ); Directory.CreateDirectory( output );
		using var cap = new GltfGpuTriangleGateTests.NativeCapture( output );
		GltfManagedModelResource resource = null; Model model = null; ModelRenderer renderer = null;
		try
		{
			resource = new GltfManagedModelResource( GltfReader.Read( CameraReaderTests.Source(), CameraReaderTests.Resolve ) ); model = Model.FromGltfResource( resource, "camera_flip_contract" ); resource = null;
			using ( cap.Scene.Push() ) { renderer = cap.Scene.CreateObject().Components.Create<ModelRenderer>(); renderer.Model = model; }
			var camera = cap.Camera; camera.WorldPosition = new global::Vector3( 3, .25f, .5f ) / .0254f; camera.WorldRotation = Rotation.FromYaw( 180 );
			camera.ZNear = .01f / .0254f; camera.ZFar = 100f / .0254f; camera.Orthographic = true; camera.OrthographicHeight = 2f / .0254f;
			var baseline = await Capture( cap, output, "ordinary", false, false ); Assert.IsTrue( baseline.Coverage > 100 );
			var pointBefore = camera.PointToScreenNormal( global::Vector3.Zero ); var rayBefore = camera.ScreenNormalToRay( new global::Vector3( 1, .5f, 0 ) );
			Assert.AreEqual( .375f, pointBefore.x, 2e-5f );
			foreach ( var flip in new[] { (X: true, Y: false), (X: false, Y: true), (X: true, Y: true) } )
			{
				var capture = await Capture( cap, output, $"flip_{flip.X}_{flip.Y}", flip.X, flip.Y );
				var intersection = 0; var union = 0; double error = 0; var changed = 0;
				for ( var y = 0; y < 192; y++ ) for ( var x = 0; x < 192; x++ )
				{
					var actual = capture.Pixels[y * 192 + x]; var expected = baseline.Pixels[(flip.Y ? 191 - y : y) * 192 + (flip.X ? 191 - x : x)];
					var a = GltfGpuTriangleGateTests.NativeCapture.Foreground( actual ); var b = GltfGpuTriangleGateTests.NativeCapture.Foreground( expected ); if ( a && b ) intersection++; if ( a || b ) union++;
					error += Math.Abs( actual.r - expected.r ) + Math.Abs( actual.g - expected.g ) + Math.Abs( actual.b - expected.b );
					if ( actual != baseline.Pixels[y * 192 + x] ) changed++;
				}
				var iou = intersection / (double)union; var mean = error / (192 * 192 * 3);
				Assert.IsTrue( iou >= .99 && mean <= 1d / 255, "Existing native final-blit flip disagrees with literal pixel permutation by more than one average8-bit quantization step." );
				Assert.IsTrue( changed > 100, "Off-center camera must distinguish a flip from identity." );
				Assert.AreEqual( pointBefore, camera.PointToScreenNormal( global::Vector3.Zero ) ); Assert.AreEqual( rayBefore.Forward, camera.ScreenNormalToRay( new global::Vector3( 1, .5f, 0 ) ).Forward );
				Measurements.Add( new { flip.X, flip.Y, capture = capture.Path, capture.Sha256, capture.Coverage, iou, mean_rgb_error = mean, changed_pixels_vs_identity = changed, camera_query_x = pointBefore.x, scope = "Actual existing per-submission ViewSetup.FlipX/Y final-blit contract. Component queries intentionally retain the base camera; signed imported cameras require a separate propagated/query-aware representation." } );
			}
			var explicitReset = await Capture( cap, output, "explicit_false_overrides_prior_submission", false, false );
			var defaultReset = await Capture( cap, output, "null_default_after_prior_submission", null, null );
			double MaximumDifference( GltfGpuTriangleGateTests.NativeCapture.Frame frame ) => frame.Pixels.Zip( baseline.Pixels, (a, b) => Math.Max( Math.Abs( a.r - b.r ), Math.Max( Math.Abs( a.g - b.g ), Math.Abs( a.b - b.b ) ) ) ).Max();
			Assert.IsTrue( MaximumDifference( explicitReset ) <= 1d / 255 && MaximumDifference( defaultReset ) <= 1d / 255, "Explicit false or null/default inherited a preceding submission's flip." );
			Measurements.Add( new { scope = "Per-submission override isolation: explicit false and null/default restore base camera after both-axis true submission. Persistent camera flip fields do not exist in this producer; their future explicit-false priority needs a separate installed test.", explicit_false_capture = explicitReset.Path, default_capture = defaultReset.Path, explicit_false_max_error = MaximumDifference( explicitReset ), default_max_error = MaximumDifference( defaultReset ) } );
		}
		finally { await MainThread.Wait(); using ( cap.Scene.Push() ) { renderer?.GameObject.Destroy(); cap.Scene.EditorTick( 0, 0 ); cap.Scene.ProcessDeletes(); } model?.Destroy(); resource?.ReleaseOwner(); }
	}
	static async Task<GltfGpuTriangleGateTests.NativeCapture.Frame> Capture( GltfGpuTriangleGateTests.NativeCapture cap, string output, string name, bool? flipX, bool? flipY )
	{
		await cap.WaitFrames(); var camera = cap.Camera;
		using ( cap.Scene.Push() ) { cap.Scene.PreCameraRender(); camera.InitializeRendering( false ); }
		using var target = Texture.CreateRenderTarget().WithSize( 192, 192 ).WithFormat( ImageFormat.RGBA8888 ).WithInitialColor( global::Color.Magenta ).Create( "camera_flip_contract_" + Guid.NewGuid().ToString( "N" ) );
		using ( cap.Scene.Push() ) Assert.IsTrue( camera.RenderToTexture( target, new ViewSetup { FlipX = flipX, FlipY = flipY } ) );
		await cap.WaitFrames(); using var bitmap = target.GetBitmap(); var pixels = bitmap.GetPixels(); var png = bitmap.ToPng(); var path = Path.Combine( output, name + ".png" ); File.WriteAllBytes( path, png );
		return new( pixels, pixels.Count( GltfGpuTriangleGateTests.NativeCapture.Foreground ), path, CameraReaderTests.Hash( png ), 192, 192, Application.FrameCount, 0, "Per-submission ViewSetup flip" );
	}
}
