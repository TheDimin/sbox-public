using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Sandbox.Gltf.DirectGpu.AnisotropyBarnLampValidation;

internal sealed class BarnLampCapture : IDisposable
{
	internal sealed record Frame( string Path, string Sha256, int Coverage, Color[] Pixels );
	internal Scene Scene { get; }
	private const int Width = 704, Height = 802;
	private readonly string output;
	private readonly CameraComponent camera;
	private readonly Editor.SceneRenderingWidget widget;
	private readonly Action onFrame;
	private readonly SceneCubemap cubemap;
	private readonly ScenePointLight pointLight;
	private readonly JsonElement fit;
	private int frames;
	private const string ReferenceHash = "3ee16e1ae1c7c1d490b91f92da482518e5d8f6da5025cfbd39048c19692966bf";
	private const string CameraFitHash = "132bde89f0641e47d5ba4141f97a7dc3e708632c63423f696c79045acfc5df66";
	private const string FallbackCubemap = "textures/cubemaps/default.vtex";

	internal BarnLampCapture( string root, string output, bool directionalControl = false )
	{
		this.output = output;
		var directory = Path.Combine( root, "research/gltf-validation/direct-gpu/models/AnisotropyBarnLamp" );
		var fitBytes = File.ReadAllBytes( Path.Combine( directory, "camera-fit.json" ) );
		Assert.AreEqual( CameraFitHash, Hash( fitBytes ) );
		using var fitDocument = JsonDocument.Parse( fitBytes );
		fit = fitDocument.RootElement.Clone();
		var reference = Path.Combine( root, "Temp/gltf-reference/glTF-Sample-Assets/Models/AnisotropyBarnLamp", fit.GetProperty( "reference" ).GetString() );
		Assert.AreEqual( ReferenceHash, fit.GetProperty( "referenceSha256" ).GetString() );
		Assert.AreEqual( ReferenceHash, Hash( File.ReadAllBytes( reference ) ) );
		RequireNativeRenderer();
		var fallback = !directionalControl ? Texture.Load( FallbackCubemap ) : null;
		if ( !directionalControl ) Assert.IsTrue( fallback is { IsValid: true, IsError: false, Depth: 6 } && fallback.Mips > 1 );
		Scene = Scene.CreateEditorScene();
		using ( Scene.Push() )
		{
			camera = Scene.CreateObject().Components.Create<CameraComponent>();
			camera.WorldPosition = Vector( "cameraPositionInches" );
			camera.WorldRotation = Rotation.LookAt( Vector( "cameraForward" ), Vector( "cameraUp" ) );
			camera.Orthographic = false;
			camera.FovAxis = CameraComponent.Axis.Vertical;
			camera.FieldOfView = fit.GetProperty( "verticalFovDegrees" ).GetSingle();
			camera.ZNear = .1f;
			camera.ZFar = 1000;
			camera.CustomSize = new Vector2( Width, Height );
			camera.BackgroundColor = new Color( .35f, .35f, .35f, 1 );
			camera.ClearFlags = ClearFlags.All;
			camera.EnablePostProcessing = true;
			camera.DebugMode = SceneCameraDebugMode.Normal;
			var tone = camera.GameObject.Components.Create<Tonemapping>();
			tone.Mode = Tonemapping.TonemappingMode.ACES;
			tone.AutoExposureEnabled = true;
			tone.MinimumExposure = 1;
			tone.MaximumExposure = 1;
			tone.ExposureCompensation = 1;
			Scene.EditorTick( 0, 0 );
		}
		if ( directionalControl )
			pointLight = new ScenePointLight( Scene.SceneWorld, new Vector3( 28, -12, 18 ), 200, Color.White * 4 );
		else
		{
			cubemap = new SceneCubemap( Scene.SceneWorld, fallback, BBox.FromPositionAndSize( Vector3.Zero, 1000 ) );
			cubemap.Projection = SceneCubemap.ProjectionMode.Sphere;
			cubemap.TintColor = Color.White;
			cubemap.Feathering = .01f;
			cubemap.RenderDirty();
		}
		widget = new Editor.SceneRenderingWidget { Scene = Scene, Camera = camera, WindowTitle = "glTF Barn Lamp validation", Size = new Vector2( 528, 602 ) };
		onFrame = () => frames++;
		widget.OnPreFrame += onFrame;
		widget.Show();
	}

	internal async Task Settle()
	{
		for ( var i = 0; i < 8; i++ ) await WaitFrames();
	}

	private async Task WaitFrames()
	{
		await MainThread.Wait();
		var initial = frames;
		var timer = Stopwatch.StartNew();
		while ( frames < initial + 3 || Graphics.IsActive )
		{
			Assert.IsTrue( timer.Elapsed < TimeSpan.FromSeconds( 15 ), "Native preview stopped receiving real frames." );
			await Task.Delay( 20 );
			await MainThread.Wait();
		}
		RequireNativeRenderer();
	}

	internal async Task<Frame> Take( string name )
	{
		await WaitFrames();
		using ( Scene.Push() ) { Scene.PreCameraRender(); camera.InitializeRendering( false ); }
		using var target = Texture.CreateRenderTarget().WithSize( Width, Height ).WithFormat( ImageFormat.RGBA8888 ).WithInitialColor( Color.Magenta ).Create( "barn_lamp_" + Guid.NewGuid().ToString( "N" ) );
		bool submitted;
		using ( Scene.Push() ) { target.Clear( Color.Magenta ); submitted = camera.RenderToTexture( target ); }
		Assert.IsTrue( submitted );
		await WaitFrames();
		using var bitmap = target.GetBitmap();
		var pixels = bitmap.GetPixels();
		Assert.AreEqual( Width * Height, pixels.Length );
		Assert.IsFalse( pixels[0].r > .95f && pixels[0].b > .95f && pixels[0].g < .05f, "Native target retained sentinel magenta." );
		var background = pixels[0];
		var coverage = pixels.Count( p => Math.Max( Math.Abs( p.r - background.r ), Math.Max( Math.Abs( p.g - background.g ), Math.Abs( p.b - background.b ) ) ) > .03f );
		var path = Path.Combine( output, name + ".png" );
		var png = bitmap.ToPng();
		File.WriteAllBytes( path, png );
		return new( path, Hash( png ), coverage, pixels );
	}

	internal object ObservedPresentation()
	{
		var exposure = camera.AutoExposure;
		Assert.IsTrue( exposure.Enabled && Math.Abs( exposure.MinimumExposure - 1 ) < .0001f && Math.Abs( exposure.MaximumExposure - 1 ) < .0001f && Math.Abs( exposure.Compensation - 1 ) < .0001f );
		return new
		{
			Width, Height, camera.FieldOfView, camera.ZNear, camera.ZFar,
			position = camera.WorldPosition.ToString(), rotation = camera.WorldRotation.ToString(), sceneSize = camera.SceneCamera.Size.ToString(),
			camera.EnablePostProcessing, debugMode = camera.DebugMode.ToString(), nativeToneMapping = "s&box ACES",
			exposure.Enabled, exposure.MinimumExposure, exposure.MaximumExposure, exposure.Compensation,
			environment = cubemap.IsValid() ? FallbackCubemap : null,
			pointLight = pointLight.IsValid() ? pointLight.Position.ToString() : null,
			fit
		};
	}

	private Vector3 Vector( string name )
	{
		var x = fit.GetProperty( name );
		return new( x[0].GetSingle(), x[1].GetSingle(), x[2].GetSingle() );
	}

	private static void RequireNativeRenderer()
	{
		var type = typeof( Scene ).Assembly.GetType( "Sandbox.Rendering.ManagedSceneRendering", true );
		var enabled = type.GetProperty( "Enabled", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public );
		Assert.IsTrue( Application.IsEditor && !Game.IsPlaying && !(bool)enabled.GetValue( null ) );
	}

	private static string Hash( byte[] bytes ) => Convert.ToHexStringLower( SHA256.HashData( bytes ) );
	public void Dispose()
	{
		if ( widget.IsValid ) { widget.OnPreFrame -= onFrame; widget.Close(); widget.Destroy(); }
		if ( cubemap.IsValid() ) cubemap.Delete();
		if ( pointLight.IsValid() ) pointLight.Delete();
		Scene.Destroy();
	}
}
