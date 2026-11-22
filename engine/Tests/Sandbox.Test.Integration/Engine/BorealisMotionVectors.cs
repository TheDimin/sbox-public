using NativeEngine;
using Sandbox.Rendering;
using System;

namespace EngineTests;

[TestClass, DoNotParallelize]
public class BorealisMotionVectorsTest
{
	[TestMethod]
	public void CameraMotionReachesTheMotionStageWithoutUpscaling()
	{
		TestGraphics.Require();

		const int size = 64;
		const int viewHash = 4107;
		var scene = new Scene();
		using var scope = scene.Push();
		using var target = CreateTarget( "motion-test-output" );
		using var vectors = CreateTarget( "motion-test-readback" );
		try
		{
			var camera = scene.CreateObject().AddComponent<CameraComponent>();
			camera.WorldRotation = Rotation.Identity;
			camera.FieldOfView = 90;
			camera.ZNear = 1;
			camera.ZFar = 4096;
			camera.AutoExposure.Enabled = false;

			var model = scene.CreateObject().AddComponent<ModelRenderer>();
			model.WorldPosition = new Vector3( 128, 0, 0 );
			model.Model = Model.Cube;

			int motionStageCalls = 0;
			bool hasDepth = false;
			(int CameraId, int ViewId)? motionView = null;
			var opaque = new CommandList( "Read opaque targets" );
			var opaqueTargets = Graphics.NativeLayerTargetInfo;
			opaque.AddAction( () => opaqueTargets = Graphics.NativeLayerTargetInfo );
			camera.AddCommandList( opaque, Stage.AfterOpaque );
			var motion = new CommandList( "Read motion vectors" );
			motion.AddAction( () =>
			{
				motionStageCalls++;
				motionView = Graphics.NativeViewIdentity;
				hasDepth = Graphics.RenderTarget?.DepthTarget is not null;
				if ( Graphics.Attributes.GetTexture( "MotionVectors" ) is { } texture )
					Graphics.CopyTexture( texture, vectors );
			} );
			camera.AddCommandList( motion, Stage.MotionVectors );

			RenderFrame();
			RenderFrame();
			var still = SampleVelocity();
			camera.WorldPosition = new Vector3( 0, 12, 0 );
			RenderFrame();
			var moving = SampleVelocity();

			Assert.AreEqual( 3, motionStageCalls );
			Assert.IsTrue( hasDepth, "Motion draws must depth-test against the scene." );
			Assert.IsTrue( opaqueTargets.HasValue, "The opaque stage must report its attachments." );
			var layer = opaqueTargets.Value;
			Assert.AreEqual( (size, size, 1), layer.Color );
			Assert.AreEqual( (size, size, 1), layer.Depth );
			Assert.AreEqual( viewHash, layer.ViewId );
			Assert.AreEqual( (layer.CameraId, layer.ViewId), motionView );
			Assert.IsNull( Graphics.NativeViewIdentity );
			Assert.IsNull( Graphics.NativeLayerTargetInfo );
			Assert.IsTrue( still.IsFinite && still.Length < 0.1f, $"Stationary camera produced {still}." );
			Assert.IsTrue( moving.IsFinite && MathF.Abs( moving.x ) > 1 && MathF.Abs( moving.y ) < 0.1f,
				$"Expected horizontal motion after translating the camera. Read {moving}." );

			void RenderFrame()
			{
				Application.FrameCount++;
				SceneRenderer.BeginFrame();
				EngineGlobal.SourceEnginePanelAppFrame();
				scene.GameTick( 1.0 / 60 );
				scene.PreRender();
				Assert.IsTrue( camera.RenderToTexture( target, new ViewSetup
				{
					EnablePostprocessing = false,
					EnableUI = false,
					EnableDebugOverlays = false,
					ViewHash = viewHash
				} ) );
				g_pRenderDevice.Present( IntPtr.Zero );
				g_pRenderDevice.ForceFlushGPU( IntPtr.Zero );
				EngineLoop.DrainFrameEndDisposables();
				RenderTarget.EndOfFrame();
				EngineLoop.RunAsyncTasks();
			}

			Vector2 SampleVelocity()
			{
				Span<ushort> pixels = stackalloc ushort[4];
				vectors.GetPixels( (size / 2, size / 2, 1, 1), 0, 0, pixels, ImageFormat.RGBA16161616F );
				return new Vector2( (float)BitConverter.UInt16BitsToHalf( pixels[0] ), (float)BitConverter.UInt16BitsToHalf( pixels[1] ) );
			}
		}
		finally
		{
			scene.Destroy();
		}

		static Texture CreateTarget( string name ) => Texture.CreateRenderTarget().WithSize( size, size )
			.WithFormat( ImageFormat.RGBA16161616F ).Create( name );
	}
}
