using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Sandbox.Rendering;
using NMatrix = System.Numerics.Matrix4x4;

namespace Sandbox.Gltf.DirectGpu.CamerasValidation;

[TestClass]
public sealed class CameraPersistentFlipTests
{
	static string root;
	public static List<object> Measurements { get; } = new();
	public static void Configure( string checkout ) { root = checkout; CameraReaderTests.Configure( checkout ); Measurements.Clear(); }
	internal sealed record Capture( global::Color[] Pixels, string Path, string Hash, int Width, int Height, ViewData View, float[] Depth );
	internal static async Task<Capture> Take( GltfGpuTriangleGateTests.NativeCapture cap, CameraComponent camera, string output, string name, ViewSetup config = default, bool depth = false )
	{
		cap.Widget.Camera = camera; await cap.WaitFrames();
		using ( cap.Scene.Push() ) { cap.Scene.PreCameraRender(); camera.InitializeRendering( false ); }
		var size = camera.CustomSize.Value; var width = (int)size.x; var height = (int)size.y;
		using var target = Texture.CreateRenderTarget().WithSize( width, height ).WithFormat( ImageFormat.RGBA8888 ).WithInitialColor( global::Color.Magenta ).Create( "camera_persistent_" + Guid.NewGuid().ToString( "N" ) );
		using var depthTarget = depth ? Texture.CreateRenderTarget().WithSize( width, height ).WithFormat( ImageFormat.D24S8 ).Create( "camera_depth_" + Guid.NewGuid().ToString( "N" ) ) : null;
		ViewData view; using ( cap.Scene.Push() ) Assert.IsTrue( camera.RenderToTexture( target, depthTarget, config, out view ) );
		Assert.IsTrue( view.IsValid ); await cap.WaitFrames();
		using var bitmap = target.GetBitmap(); var pixels = bitmap.GetPixels(); var png = bitmap.ToPng(); var path = Path.Combine( output, name + ".png" ); File.WriteAllBytes( path, png );
		float[] values = null;
		if ( depth ) { values = new float[width * height]; depthTarget.GetPixels( (0, 0, width, height), 0, 0, values.AsSpan(), ImageFormat.R32F ); }
		return new( pixels, path, CameraReaderTests.Hash( png ), width, height, view, values );
	}
	internal static void PixelPermutation( Capture actual, Capture baseline, bool xFlip, bool yFlip )
	{
		Assert.AreEqual( baseline.Width, actual.Width ); Assert.AreEqual( baseline.Height, actual.Height );
		var intersection = 0; var union = 0; double error = 0;
		for ( var y = 0; y < actual.Height; y++ ) for ( var x = 0; x < actual.Width; x++ )
		{
			var a = actual.Pixels[y * actual.Width + x]; var b = baseline.Pixels[(yFlip ? actual.Height - 1 - y : y) * actual.Width + (xFlip ? actual.Width - 1 - x : x)];
			var fa = GltfGpuTriangleGateTests.NativeCapture.Foreground( a ); var fb = GltfGpuTriangleGateTests.NativeCapture.Foreground( b ); if ( fa && fb ) intersection++; if ( fa || fb ) union++;
			error += Math.Abs( a.r - b.r ) + Math.Abs( a.g - b.g ) + Math.Abs( a.b - b.b );
		}
		Assert.IsTrue( union > 100 && intersection / (double)union >= .99 && error / (actual.Width * actual.Height * 3) <= 1d / 255, "Image disagrees with independently permuted baseline pixels." );
	}
	[DataTestMethod]
	[DataRow( false, false )] [DataRow( true, false )] [DataRow( false, true )] [DataRow( true, true )]
	public async Task PersistentFlipsFollowViewportLocalProjectionQueriesAndPixels( bool xFlip, bool yFlip )
	{
		await MainThread.Wait(); var output = Path.Combine( root, "Temp/gltf-validation/direct-gpu/Cameras/persistent", Guid.NewGuid().ToString( "N" ) ); Directory.CreateDirectory( output );
		using var cap = new GltfGpuTriangleGateTests.NativeCapture( output ); Model model = null; ModelRenderer renderer = null;
		try
		{
			model = Model.FromGltfResource( new GltfManagedModelResource( GltfReader.Read( CameraReaderTests.Source(), CameraReaderTests.Resolve ) ), "persistent_camera_test_" + Guid.NewGuid().ToString( "N" ) );
			using ( cap.Scene.Push() ) { renderer = cap.Scene.CreateObject().Components.Create<ModelRenderer>(); renderer.Model = model; }
			var c = cap.Camera; Assert.IsFalse( c.FlipX || c.FlipY );
			c.WorldPosition = new global::Vector3( 3, .25f, .5f ) / .0254f; c.WorldRotation = Rotation.FromYaw( 180 ); c.Orthographic = true; c.OrthographicHeight = 2f / .0254f; c.ZNear = .01f / .0254f; c.ZFar = 100f / .0254f;
			c.CustomSize = new global::Vector2( 240, 160 ); c.Viewport = new global::Vector4( .1f, .2f, .8f, .6f );
			var basePoint = c.PointToScreenNormal( global::Vector3.Zero ); var baseProjection = c.ProjectionMatrix;
			Assert.IsTrue( (basePoint - new global::Vector2( .4375f, .75f )).Length < 2e-5f );
			var baseline = await Take( cap, c, output, "unflipped_viewport" );
			c.FlipX = xFlip; c.FlipY = yFlip;
			var expected = new global::Vector2( xFlip ? .5625f : .4375f, yFlip ? .25f : .75f );
			Assert.IsTrue( (c.PointToScreenNormal( global::Vector3.Zero ) - expected).Length < 2e-5f );
			Assert.IsTrue( (c.PointToScreenNormal( global::Vector3.Zero, out var behind ) - expected).Length < 2e-5f ); Assert.IsFalse( behind );
			Assert.IsTrue( (c.PointToScreenPixels( global::Vector3.Zero ) - expected * c.ScreenRect.Size).Length < .002f );
			Assert.IsTrue( (c.PointToScreenPixels( global::Vector3.Zero, out behind ) - expected * c.ScreenRect.Size).Length < .002f ); Assert.IsFalse( behind );
			var sx = xFlip ? -1f : 1f; var sy = yFlip ? -1f : 1f;
			var near = c.WorldPosition + new global::Vector3( -.01f, sx * 2, sy ) / .0254f;
			var ray = c.ScreenPixelToRay( c.ScreenRect.Size * new global::Vector2( 1, 0 ) );
			Assert.IsTrue( (ray.Position - near).Length < .002f && (ray.Forward - new global::Vector3( -1, 0, 0 )).Length < 1e-5f );
			Assert.IsTrue( (c.ScreenNormalToRay( new global::Vector3( 1, 0, 0 ) ).Position - near).Length < .002f );
			Assert.IsTrue( (c.ScreenToWorld( c.ScreenRect.Size * new global::Vector2( 1, 0 ) ) - near).Length < .002f );
			Assert.IsTrue( c.GetFrustum().IsInside( c.WorldPosition + new global::Vector3( -2, 0, .75f ) / .0254f ), "Full viewport frustum used an absolute screen offset." );
			var crop = c.GetFrustum( new Rect( 96, 0, 96, 48 ) );
			Assert.IsTrue( crop.IsInside( c.WorldPosition + new global::Vector3( -2, sx, sy * .5f ) / .0254f ) );
			Assert.IsFalse( crop.IsInside( c.WorldPosition + new global::Vector3( -2, -sx, sy * .5f ) / .0254f ) );
			Assert.IsTrue( c.ProjectionMatrix == baseProjection, "Existing native projection getter must stay unflipped." );
			NMatrix screen = c.ScreenProjectionMatrix, unflipped = baseProjection; Assert.AreEqual( unflipped.M11 * sx, screen.M11 ); Assert.AreEqual( unflipped.M22 * sy, screen.M22 ); Assert.AreEqual( unflipped.M33, screen.M33 );
			var capture = await Take( cap, c, output, "persistent_flip" ); PixelPermutation( capture, baseline, xFlip, yFlip );
			// Independent literal square/quaternion projected into the centred viewport.
			var foreground = capture.Pixels.Select( (p, i) => (p, i) ).Where( row => GltfGpuTriangleGateTests.NativeCapture.Foreground( row.p ) ).Select( row => row.i ).ToArray();
			var norm = .383 * .383 + .92375 * .92375; var cosine = (.92375 * .92375 - .383 * .383) / norm;
			var xs = new[] { .5 + (0 - .25) / 4, .5 + (1 - .25) / 4 }.Select( x => 24 + (xFlip ? 1 - x : x) * 192 ).ToArray();
			var ys = new[] { .5 - (0 - .5) / 2, .5 - (cosine - .5) / 2 }.Select( y => 32 + (yFlip ? 1 - y : y) * 96 ).ToArray();
			Assert.IsTrue( Math.Abs( foreground.Min( i => i % 240 ) - xs.Min() ) <= 2 && Math.Abs( foreground.Max( i => i % 240 ) + 1 - xs.Max() ) <= 2 );
			Assert.IsTrue( Math.Abs( foreground.Min( i => i / 240 ) - ys.Min() ) <= 2 && Math.Abs( foreground.Max( i => i / 240 ) + 1 - ys.Max() ) <= 2, "Native viewport aspect disagrees with literal source projection beyond two raster edge pixels." );
			Measurements.Add( new { xFlip, yFlip, capture = capture.Path, capture.Hash, scope = "Persistent native flips; viewport-local point/pixel/ray/ToWorld/subfrustum, unflipped native matrix path, literal projection bounds and exact image permutation." } );
		}
		finally { await MainThread.Wait(); using ( cap.Scene.Push() ) { renderer?.GameObject.Destroy(); cap.Scene.EditorTick( 0, 0 ); cap.Scene.ProcessDeletes(); } model?.Destroy(); }
	}
	[TestMethod]
	public async Task PersistentFlipsPreserveSubmissionPriorityObliqueAndCallerDepth()
	{
		await MainThread.Wait(); var output = Path.Combine( root, "Temp/gltf-validation/direct-gpu/Cameras/interactions", Guid.NewGuid().ToString( "N" ) ); Directory.CreateDirectory( output );
		using var cap = new GltfGpuTriangleGateTests.NativeCapture( output ); Model model = null; ModelRenderer renderer = null;
		try
		{
			model = Model.FromGltfResource( new GltfManagedModelResource( GltfReader.Read( CameraReaderTests.Source(), CameraReaderTests.Resolve ) ), "camera_interactions_" + Guid.NewGuid().ToString( "N" ) );
			using ( cap.Scene.Push() ) { renderer = cap.Scene.CreateObject().Components.Create<ModelRenderer>(); renderer.Model = model; }
			var c = cap.Camera; c.WorldPosition = new global::Vector3( 3, .25f, .5f ) / .0254f; c.WorldRotation = Rotation.FromYaw( 180 ); c.Orthographic = false; c.FovAxis = CameraComponent.Axis.Vertical; c.FieldOfView = .7f * 180 / MathF.PI; c.ZNear = .01f / .0254f; c.ZFar = 100f / .0254f;
			var baseline = await Take( cap, c, output, "base_with_depth", depth: true ); var baseMatrix = c.ProjectionMatrix;
			Assert.IsTrue( baseline.Depth.All( float.IsFinite ) && baseline.Depth.Count( value => value > 1e-6f ) > 100 );
			var foregroundDepth = baseline.Depth.Where( (value, index) => GltfGpuTriangleGateTests.NativeCapture.Foreground( baseline.Pixels[index] ) ).Average();
			var backgroundDepth = baseline.Depth.Where( (value, index) => !GltfGpuTriangleGateTests.NativeCapture.Foreground( baseline.Pixels[index] ) ).Average();
			Assert.IsTrue( Math.Abs( foregroundDepth - backgroundDepth ) > 1e-6f, "Caller-owned depth does not distinguish actual geometry from clear pixels." );
			var clip = new Plane( new global::Vector3( 50, 0, 0 ), new global::Vector3( -1, 0, 0 ) ); var baseOblique = c.CalculateObliqueMatrix( clip );
			c.FlipX = c.FlipY = true; var flipped = await Take( cap, c, output, "persistent_both_depth", depth: true ); PixelPermutation( flipped, baseline, true, true );
			CollectionAssert.AreEqual( baseline.Depth, flipped.Depth, "Image flips changed caller-owned depth orientation." );
			Assert.AreEqual( new global::Vector4( -1, -1, 1, 1 ), flipped.View.DepthUvScaleBias ); Assert.IsTrue( flipped.View.ProjectionToView == baseline.View.ProjectionToView );
			Assert.IsTrue( c.ProjectionMatrix == baseMatrix && c.CalculateObliqueMatrix( clip ) == baseOblique, "A display flip leaked into the native/oblique projection path." );
			var explicitFalse = await Take( cap, c, output, "submission_false_over_persistent_true", new ViewSetup { FlipX = false, FlipY = false }, true ); PixelPermutation( explicitFalse, baseline, false, false );
			Assert.AreEqual( new global::Vector4( 1, 1, 0, 0 ), explicitFalse.View.DepthUvScaleBias ); Assert.IsTrue( c.FlipX && c.FlipY );
			c.CustomProjectionMatrix = baseMatrix; var customCopy = await Take( cap, c, output, "public_unflipped_projection_reused" ); PixelPermutation( customCopy, baseline, true, true ); Assert.IsTrue( c.CustomProjectionMatrix.Value == baseMatrix );
			NMatrix narrow = baseMatrix; narrow.M11 *= 2; c.CustomProjectionMatrix = narrow;
			var perSubmissionP = await Take( cap, c, output, "submission_projection_overrides_custom", new ViewSetup { ProjectionMatrix = baseMatrix, FlipX = false, FlipY = false } ); PixelPermutation( perSubmissionP, baseline, false, false ); Assert.IsTrue( c.CustomProjectionMatrix.Value == (Matrix)narrow );
			c.CustomProjectionMatrix = baseOblique; var oblique = await Take( cap, c, output, "unflipped_oblique_with_persistent_both" ); PixelPermutation( oblique, baseline, true, true );
			Measurements.Add( new { baseline = baseline.Path, flipped = flipped.Path, explicitFalse = explicitFalse.Path, customCopy = customCopy.Path, perSubmissionP = perSubmissionP.Path, oblique = oblique.Path, depth_nonzero = baseline.Depth.Count( x => x > 1e-6f ), foregroundDepth, backgroundDepth, depth_sha256 = Convert.ToHexStringLower( System.Security.Cryptography.SHA256.HashData( System.Runtime.InteropServices.MemoryMarshal.AsBytes( baseline.Depth.AsSpan() ) ) ), flipped_depth_uv_bias = flipped.View.DepthUvScaleBias.ToString(), explicit_false_depth_uv_bias = explicitFalse.View.DepthUvScaleBias.ToString(), scope = "Actual caller-depth pixels remain unflipped; view reconstruction UV bias follows resolved persistent/per-submission flags. Public native matrix, oblique and custom projection paths do not double-flip; explicit per-submission false/projection overrides preserve priority." } );
		}
		finally { await MainThread.Wait(); using ( cap.Scene.Push() ) { renderer?.GameObject.Destroy(); cap.Scene.EditorTick( 0, 0 ); cap.Scene.ProcessDeletes(); } model?.Destroy(); }
	}
}
