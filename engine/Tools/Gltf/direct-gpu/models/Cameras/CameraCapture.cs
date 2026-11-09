using System.IO;
using System.Threading.Tasks;

namespace Sandbox.Gltf.DirectGpu.CamerasValidation;

internal static class CameraCapture
{
	internal static async Task<GltfGpuTriangleGateTests.NativeCapture.Frame> Take( GltfGpuTriangleGateTests.NativeCapture cap, CameraComponent camera, string output, string name )
	{
		cap.Widget.Camera = camera;
		await cap.WaitFrames();
		using ( cap.Scene.Push() ) { cap.Scene.PreCameraRender(); camera.InitializeRendering( false ); }
		using var target = Texture.CreateRenderTarget().WithSize( 192, 192 ).WithFormat( ImageFormat.RGBA8888 ).WithInitialColor( global::Color.Magenta ).Create( "camera_validation_" + Guid.NewGuid().ToString( "N" ) );
		using ( cap.Scene.Push() ) { target.Clear( global::Color.Magenta ); Assert.IsTrue( camera.RenderToTexture( target ), "Native camera refused capture." ); }
		await cap.WaitFrames(); using var bitmap = target.GetBitmap(); var pixels = bitmap.GetPixels(); var png = bitmap.ToPng();
		var path = Path.Combine( output, name + ".png" ); File.WriteAllBytes( path, png );
		return new( pixels, pixels.Count( GltfGpuTriangleGateTests.NativeCapture.Foreground ), path, CameraReaderTests.Hash( png ), 192, 192, Application.FrameCount, 0,
			System.Text.Json.JsonSerializer.Serialize( new { camera = camera.GameObject.Name, position = camera.WorldPosition.ToString(), rotation = camera.WorldRotation.ToString(), camera.FieldOfView, camera.FovAxis, camera.Orthographic, camera.OrthographicHeight, near = camera.ZNear.ToString(), far = camera.ZFar.ToString(), viewport = camera.Viewport.ToString(), size = camera.CustomSize.ToString() } ) );
	}
}
