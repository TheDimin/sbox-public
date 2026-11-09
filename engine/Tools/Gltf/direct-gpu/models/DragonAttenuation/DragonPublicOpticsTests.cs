using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox;

namespace Sandbox.Gltf.DirectGpu.DragonAttenuationValidation;

[TestClass]
internal sealed class DragonPublicOpticsTests
{
	private const int Width = 640;
	private const int Height = 506;
	private const string ThicknessParameter = "g_tGltfVolumeThicknessTexture";
	private static string checkout;
	private static string importReceipt;
	public static List<object> Measurements { get; } = new();

	public static void Configure( string root, string importReceiptPath )
	{
		checkout = root;
		importReceipt = importReceiptPath;
		Measurements.Clear();
	}

	[TestMethod, DataRow( false ), DataRow( true )]
	public async Task PublicAttenuationAndSurfaceColor( bool compiled )
	{
		await MainThread.Wait();
		Assert.IsTrue( Application.IsEditor && !Game.IsPlaying && !Sandbox.Rendering.ManagedSceneRendering.Enabled );
		var owned = Path.Combine( checkout, "research/gltf-validation/direct-gpu/models/DragonAttenuation" );
		using var cameraRecipe = JsonDocument.Parse( File.ReadAllBytes( Path.Combine( owned, "reference-camera.json" ) ) );
		using var imported = JsonDocument.Parse( File.ReadAllBytes( importReceipt ) );
		var ibl = imported.RootElement;
		Assert.IsTrue( ibl.GetProperty( "passed" ).GetBoolean() );
		var environmentPath = ibl.GetProperty( "resource" ).GetString();
		Assert.IsTrue( Regex.IsMatch( environmentPath, @"^materials/codex_gltf_ibl_[a-f0-9]{32}/footprint_court\.vtex$" ) );
		Assert.AreEqual( "aa39bda8a22575f3402660b47f6855b28c1d7949c3d15b7c3aa7ec6c087c34cb",
			Hash( File.ReadAllBytes( ibl.GetProperty( "source" ).GetString() ) ) );
		var environmentSha = ibl.GetProperty( "compiledSha256" ).GetString();
		Assert.AreEqual( environmentSha, Hash( File.ReadAllBytes( ibl.GetProperty( "compiled" ).GetString() ) ) );
		var environment = Texture.Load( environmentPath );
		Assert.IsTrue( environment is { IsValid: true, IsError: false } && environment.Depth == 6 && environment.Mips > 1 );

		var source = Path.Combine( checkout, "Temp/gltf-reference/glTF-Sample-Assets/Models/DragonAttenuation/glTF" );
		var input = File.ReadAllBytes( Path.Combine( source, "DragonAttenuation.gltf" ) );
		Assert.AreEqual( "bd97e0271b94edaf6eba1b9ea338bd0c990e18c1a9085a1c72aaadd8aba4b66b", Hash( input ) );
		byte[] Resolve( string uri ) => File.ReadAllBytes( Path.Combine( source, uri ) );
		if ( compiled )
			input = GltfCompiler.Compile( input, Resolve );

		var token = "dragon_optics_" + Guid.NewGuid().ToString( "N" );
		var mountRoot = Path.Combine( Path.GetTempPath(), token );
		var bundle = Path.Combine( mountRoot, token );
		var file = compiled ? "DragonAttenuation.glb_c" : "DragonAttenuation.gltf";
		var path = token + "/" + file;
		var output = Path.Combine( checkout, "Temp/gltf-validation/direct-gpu/DragonAttenuation/optics", token );
		Directory.CreateDirectory( output );
		Directory.CreateDirectory( bundle );
		LocalFileSystem mount = null;
		Model model = null;
		SceneCubemap probe = null;
		Preview capture = null;
		try
		{
			File.WriteAllBytes( Path.Combine( bundle, file ), input );
			foreach ( var name in new[] { "DragonAttenuation.bin", "checkerboard.png", "Dragon_ThicknessMap.jpg" } )
				File.Copy( Path.Combine( source, name ), Path.Combine( bundle, name ) );
			mount = new LocalFileSystem( mountRoot );
			FileSystem.Mounted.Mount( mount );
			model = Model.Load( path );
			Assert.IsTrue( model is { IsValid: true, IsError: false } && model.native.IsNull && model.MeshCount == 2 );
			Assert.AreEqual( Resource.FixPath( path ), model.ResourcePath );
			capture = new Preview( output, cameraRecipe.RootElement );
			probe = new SceneCubemap( capture.Scene.SceneWorld, environment, BBox.FromPositionAndSize( Vector3.Zero, 100000 ) );
			probe.Projection = SceneCubemap.ProjectionMode.Sphere;
			probe.Feathering = .01f;
			probe.TintColor = Color.White;
			probe.RenderDirty();
			Assert.IsTrue( probe.IsValid() && probe.Texture == environment );

			var empty = await capture.Take( "empty_before" );
			ModelRenderer renderer;
			using ( capture.Scene.Push() )
			{
				renderer = capture.Scene.CreateObject().Components.Create<ModelRenderer>( false );
				renderer.Model = model;
				renderer.Enabled = true;
				capture.Scene.EditorTick( 0, 0 );
			}

			var instance = renderer.GltfInstance;
			Assert.IsNotNull( instance );
			var original = await capture.Settled( "attenuation" );
			var originalMaterial = CheckDraw( instance, 1 );
			var thickness = originalMaterial.GetTexture( ThicknessParameter );
			thickness.MarkUsed( 2048 );
			await capture.WaitForFrames( 4 );
			Color32[] texels = null;
			for ( var attempt = 0; attempt < 12; ++attempt )
			{
				thickness.MarkUsed( 2048 );
				await capture.WaitForFrames( 4 );
				texels = thickness.GetPixels();
				if ( texels?.Length == 2048 * 2048 )
					break;
			}
			Assert.AreEqual( 2048 * 2048, texels?.Length ?? 0, "Thickness texture failed to reach full residency." );
			int hornGreen = texels[179 * 2048 + 1726].g;
			int bodyGreen = texels[1148 * 2048 + 963].g;
			Assert.IsTrue( Math.Abs( hornGreen - 66 ) <= 3 && Math.Abs( bodyGreen - 242 ) <= 3,
				$"Bound texture did not retain the independent green texels: {hornGreen}, {bodyGreen}." );
			// Take the review frame after the bound thickness texture reached full residency.
			original = await capture.Settled( "attenuation_resident" );

			SetVariant( 1 );
			var surface = await capture.Settled( "surface_color" );
			var surfaceMaterial = CheckDraw( instance, 2 );
			Assert.AreEqual( thickness.Width, surfaceMaterial.GetTexture( ThicknessParameter ).Width );
			SetVariant( null );
			var restored = await capture.Settled( "attenuation_restored" );
			CheckDraw( instance, 1 );

			float visible = Difference( original.Pixels, empty.Pixels );
			float changed = Difference( original.Pixels, surface.Pixels );
			float reset = Difference( original.Pixels, restored.Pixels );
			Assert.IsTrue( visible > .03f && changed > .01f && reset < changed / 3,
				$"Expected visible draw, authored variant change and restoration; {visible}, {changed}, {reset}." );
			var draw = instance.SceneObject;
			using ( capture.Scene.Push() )
			{
				renderer.GameObject.Destroy();
				capture.Scene.EditorTick( 0, 0 );
				capture.Scene.ProcessDeletes();
			}
			Assert.IsFalse( draw.IsValid() );
			var removed = await capture.Take( "empty_after" );
			Assert.IsTrue( Difference( empty.Pixels, removed.Pixels ) < .005f );
			Measurements.Add( new
			{
				form = compiled ? "derived canonical glb_c" : "pinned glTF",
				inputSha256 = Hash( input ),
				environmentPath,
				environmentSha,
				camera = cameraRecipe.RootElement.Clone(),
				frames = new[] { original, surface, restored }.Select( frame => new { frame.Path, frame.Sha256, frame.EngineFrame } ),
				hornGreen, bodyGreen, visible, changed, reset,
				scope = "Public authored variant draw, bound linear thickness texels, GPU index counts and settled same-camera captures. Visual approval and exact upstream light/exposure remain separate."
			} );

			void SetVariant( int? variant )
			{
				using var active = capture.Scene.Push();
				renderer.MaterialVariant = variant;
				capture.Scene.EditorTick( 0, 0 );
			}
		}
		finally
		{
			await MainThread.Wait();
			var cleanup = new List<Action>
			{
				() => { if ( probe.IsValid() ) probe.Delete(); },
				() => capture?.Dispose(),
				() => { if ( model?.ResourcePath == Resource.FixPath( path ) ) model.Destroy(); },
				() => { if ( mount is not null ) FileSystem.Mounted.UnMount( mount ); },
				() => mount?.Dispose(),
				() => Directory.Delete( mountRoot, true )
			};
			GltfGpuCleanup.ReleaseAll( cleanup );
		}
	}

	private static Material CheckDraw( GltfGpuInstance instance, int materialIndex )
	{
		Assert.IsTrue( instance.DrawCalls > 0 && instance.SubmittedRevision == instance.PoseRevision );
		Assert.AreEqual( 0, instance.CallbackErrors.Length );
		var rows = instance.Readback();
		Assert.AreEqual( 2, rows.Count );
		var cloth = rows.Single( row => row.Node == 0 );
		var dragon = rows.Single( row => row.Node == 1 );
		Assert.AreEqual( 131337, cloth.Indices.Length );
		Assert.AreEqual( 273648, dragon.Indices.Length );
		Assert.IsTrue( rows.All( row => row.IndexDataOrigin == "gpu-readback" && !row.SubmittedMaterialOverride ) );
		Assert.AreEqual( 0, cloth.SubmittedMaterialIndex );
		Assert.AreEqual( materialIndex, dragon.SubmittedMaterialIndex );
		var material = dragon.SubmittedMaterial;
		Assert.IsTrue( material is { IsValid: true, IsError: false } );
		foreach ( var parameter in new[] { "g_flGltfOpticalTransmissionFactor", "g_flGltfVolumeThicknessMeshMeters", "g_flGltfAttenuationDistanceInches" } )
			Assert.IsTrue( material.HasParameter( parameter ), $"Submitted shader lacks {parameter}." );
		var baseColor = material.GetColor( "g_vGltfBaseColor" );
		var attenuationColor = material.GetColor( "g_vGltfAttenuationColor" );
		var yellow = new Color( .921f, .640f, .064f );
		CheckRgb( materialIndex == 1 ? Color.White : yellow, baseColor );
		CheckRgb( materialIndex == 1 ? yellow : Color.White, attenuationColor );
		Assert.IsTrue( material.GetTexture( ThicknessParameter ) is { IsValid: true, IsError: false } );
		return material;
	}

	private static void CheckRgb( Color expected, Color actual )
	{
		Assert.AreEqual( expected.r, actual.r, .00001f );
		Assert.AreEqual( expected.g, actual.g, .00001f );
		Assert.AreEqual( expected.b, actual.b, .00001f );
	}

	private static string Hash( byte[] bytes ) => Convert.ToHexStringLower( SHA256.HashData( bytes ) );
	private static float Difference( Color[] first, Color[] second ) => first.Zip( second,
		( a, b ) => (Math.Abs( a.r - b.r ) + Math.Abs( a.g - b.g ) + Math.Abs( a.b - b.b )) / 3 ).Average();

	private sealed class Preview : IDisposable
	{
		internal sealed record Frame( Color[] Pixels, string Path, string Sha256, ulong EngineFrame );
		internal readonly Scene Scene;
		private readonly CameraComponent camera;
		private readonly Editor.SceneRenderingWidget widget;
		private readonly string output;
		private int frames;

		internal Preview( string output, JsonElement recipe )
		{
			this.output = output;
			Scene = Scene.CreateEditorScene();
			using ( Scene.Push() )
			{
				camera = Scene.CreateObject().Components.Create<CameraComponent>();
				camera.WorldPosition = Position( recipe.GetProperty( "position" ) );
				camera.WorldRotation = Rotation.LookAt( Position( recipe.GetProperty( "target" ) ) - camera.WorldPosition, Vector3.Up );
				camera.Orthographic = false;
				camera.FovAxis = CameraComponent.Axis.Vertical;
				camera.FieldOfView = recipe.GetProperty( "verticalFov" ).GetSingle();
				camera.CustomSize = new Vector2( Width, Height );
				camera.ZNear = .01f;
				camera.ZFar = 1000;
				camera.BackgroundColor = Color.Black;
				camera.ClearFlags = ClearFlags.All;
				camera.EnablePostProcessing = true;
				camera.DebugMode = SceneCameraDebugMode.Normal;
				var tonemap = camera.GameObject.Components.Create<Tonemapping>();
				tonemap.Mode = Tonemapping.TonemappingMode.ACES;
				tonemap.AutoExposureEnabled = true;
				tonemap.MinimumExposure = 1;
				tonemap.MaximumExposure = 1;
				Scene.EditorTick( 0, 0 );
			}
			widget = new Editor.SceneRenderingWidget { Scene = Scene, Camera = camera, WindowTitle = "DragonAttenuation optics validation", Size = new Vector2( Width, Height ) };
			widget.OnPreFrame += Tick;
			widget.Show();
		}

		private static Vector3 Position( JsonElement point ) => new Vector3(
			point[2].GetSingle(), point[0].GetSingle(), point[1].GetSingle() ) / .0254f;
		private void Tick() => frames++;

		internal async Task WaitForFrames( int count )
		{
			await MainThread.Wait();
			int start = frames;
			var timer = Stopwatch.StartNew();
			while ( frames < start + count || Graphics.IsActive )
			{
				Assert.IsTrue( timer.Elapsed.TotalSeconds < 20, "Native preview stopped advancing." );
				await Task.Delay( 20 );
				await MainThread.Wait();
			}
		}

		internal async Task<Frame> Settled( string label )
		{
			await WaitForFrames( 45 );
			var warm = await Take( label + "_warm" );
			var final = await Take( label );
			Assert.IsTrue( Difference( warm.Pixels, final.Pixels ) < .008f, "Successive native frames did not settle." );
			return final;
		}

		internal async Task<Frame> Take( string label )
		{
			await WaitForFrames( 3 );
			using ( Scene.Push() )
			{
				Scene.PreCameraRender();
				camera.InitializeRendering( false );
			}
			using var target = Texture.CreateRenderTarget().WithSize( Width, Height ).WithFormat( ImageFormat.RGBA8888 )
				.WithInitialColor( Color.Magenta ).Create( "dragon_optics_" + Guid.NewGuid().ToString( "N" ) );
			using ( Scene.Push() )
			{
				target.Clear( Color.Magenta );
				Assert.IsTrue( camera.RenderToTexture( target ) );
			}
			await WaitForFrames( 3 );
			using var bitmap = target.GetBitmap();
			var pixels = bitmap.GetPixels();
			Assert.AreEqual( Width * Height, pixels.Length );
			Assert.IsFalse( pixels.All( color => color == Color.Magenta ) );
			var png = bitmap.ToPng();
			var path = Path.Combine( output, label + ".png" );
			File.WriteAllBytes( path, png );
			return new Frame( pixels, path, Hash( png ), Application.FrameCount );
		}

		public void Dispose()
		{
			if ( widget.IsValid )
			{
				widget.OnPreFrame -= Tick;
				widget.Close();
				widget.Destroy();
			}
			Scene.Destroy();
		}
	}
}
