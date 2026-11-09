using System.Threading.Tasks;
using NMatrix = System.Numerics.Matrix4x4;

namespace Sandbox.Gltf.DirectGpu.CamerasValidation;

[TestClass]
public sealed class CameraCustomProjectionQueryTests
{
	[DataTestMethod]
	[DataRow( false, false, false )]
	[DataRow( true, false, false )]
	[DataRow( true, true, false )]
	[DataRow( true, false, true )]
	public async Task CustomProjectionUsesTheSameFlippedImageForProjectionRaysAndClip( bool reverse, bool infinite, bool ortho )
	{
		await MainThread.Wait();
		var scene = Scene.CreateEditorScene();
		try
		{
			using var scope = scene.Push();
			var camera = scene.CreateObject().Components.Create<CameraComponent>();
			camera.CustomSize = new Vector2( 192, 192 );
			camera.ZNear = .5f;
			camera.ZFar = 300;
			camera.FieldOfView = 70;
			camera.Orthographic = ortho;
			camera.OrthographicHeight = 16;
			camera.FlipX = true;
			camera.FlipY = true;
			camera.InitializeRendering( false );

			const float near = 2;
			const float far = 20;
			var projection = ortho
				? new NMatrix( 2, 0, 0, .25f, 0, 3, 0, 0, 0, 0, 1 / (far - near), far / (far - near), 0, 0, 0, 1 )
				: new NMatrix( 2, 0, .25f, 0, 0, 3, 0, 0, 0, 0,
					infinite ? 0 : reverse ? near / (far - near) : -far / (far - near),
					infinite ? near : (reverse ? 1 : -1) * near * far / (far - near), 0, 0, -1, 0 );
			camera.CustomProjectionMatrix = projection;

			var world = ortho ? new Vector3( 6, -.25f, .25f ) : new Vector3( 6, -1.5f, 1 );
			var expectedScreen = ortho ? new Vector2( .125f, .875f ) : new Vector2( .375f, .75f );
			var expectedNear = ortho ? new Vector3( 2, -.25f, .25f ) : new Vector3( 2, -.5f, 1f / 3 );
			var pixels = expectedScreen * 192;
			Assert.IsTrue( (camera.PointToScreenNormal( world ) - expectedScreen).Length < 1e-5f );
			Assert.IsTrue( (camera.PointToScreenPixels( world, out var behind ) - pixels).Length < 1e-3f );
			Assert.IsFalse( behind );
			camera.PointToScreenNormal( new Vector3( -1, 0, 0 ), out behind );
			Assert.IsTrue( behind );

			var actualNear = camera.ScreenToWorld( pixels );
			Assert.IsTrue( (actualNear - expectedNear).Length < 1e-5f );
			var ray = camera.ScreenPixelToRay( pixels );
			Assert.IsTrue( (ray.Position - (ortho ? expectedNear : Vector3.Zero)).Length < 1e-5f );
			Assert.IsTrue( (ray.Forward - (ortho ? Vector3.Forward : world.Normal)).Length < 1e-5f );

			var frustum = camera.GetFrustum();
			Assert.IsTrue( frustum.IsInside( world ) );
			Assert.IsFalse( frustum.IsInside( new Vector3( 1, 0, 0 ) ) );
			Assert.AreEqual( infinite, frustum.IsInside( new Vector3( 30, 0, 0 ) ) );
			var subrect = new Rect( pixels - new Vector2( 4, 4 ), new Vector2( 8, 8 ) );
			Assert.IsTrue( camera.GetFrustum( subrect ).IsInside( world ) );
			Assert.IsFalse( camera.GetFrustum( subrect ).IsInside( new Vector3( 6, 0, 0 ) ) );
		}
		finally
		{
			scene.Destroy();
		}
	}
}
