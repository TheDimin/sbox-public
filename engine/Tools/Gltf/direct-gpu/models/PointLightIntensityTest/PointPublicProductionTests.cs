using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;
using Sandbox.SceneRenderer.Gpu;

namespace Sandbox.Gltf.DirectGpu.PointLightIntensityValidation;

[TestClass]
internal sealed class PointPublicProductionTests
{
	private const string Extension = "KHR_lights_punctual";
	private const string SampleHash = "7913fe3e6829beba77f65ea749e40b3e95906bc2894afc443c22e3d91e263733";
	private const string ReceiverHash = "d83fbbde05a3071d54ceabf4aac34901a2a79bb52165ec5cd22404dbc5e0543f";
	public static string CheckoutRoot { get; set; }
	public static List<object> Measurements { get; } = new();

	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public async Task PinnedAssetUsesDefaultMaterialsAndOwnsNativeLights( bool compiled )
	{
		await MainThread.Wait();
		var sourceDir = Path.Combine( CheckoutRoot, "Temp/gltf-reference/glTF-Sample-Assets/Models/PointLightIntensityTest/glTF" );
		var source = File.ReadAllBytes( Path.Combine( sourceDir, "PointLightIntensityTest.gltf" ) );
		Assert.AreEqual( SampleHash, Hash( source ) );
		byte[] Resolve( string uri ) => File.ReadAllBytes( Path.Combine( sourceDir, uri ) );
		Assert.AreEqual( "29c2e0d139c8fe4a190654179177166c5da1932a9378d19e8f7f74622e6b16d6", Hash( Resolve( "PointLightIntensityTest.bin" ) ) );
		Assert.AreEqual( "92433d3e05d124a8e37d89f4f0f4863a02654409210a31b35c82154b0ea0a3cd", Hash( Resolve( "LampColorNames.png" ) ) );
		using var files = new MountedFixture();
		var path = files.Write( "pinned", source, compiled, Resolve );
		using var capture = CreateCapture( new Vector3( 300, 0, -49 ), new Vector3( 0, 0, -49 ), 260 );
		Model model = null;
		ModelRenderer first = null, second = null;
		try
		{
			model = Model.Load( path );
			AssertManaged( model );
			var shaders = model.Materials.Select( material => material.ShaderName ).Distinct().ToArray();
			Assert.IsTrue( shaders.Contains( "shaders/gltf_surface_five_uv.shader", StringComparer.OrdinalIgnoreCase ) );
			Assert.IsTrue( shaders.Contains( "shaders/gltf_unlit_five_uv.shader", StringComparer.OrdinalIgnoreCase ) );

			first = AddRenderer( capture.Scene, model, Vector3.Zero );
			var imported = Imported( capture.Scene );
			Assert.AreEqual( 8, imported.Length );
			Assert.IsTrue( imported.All( light => Math.Abs( light.Radius - 1.125 / .0254 ) < .0001 && light.Position.x > 7 && light.Position.x < 9 ) );
			Assert.IsTrue( imported.All( light => !light.RenderDiffuse && !light.RenderSpecular && !light.RenderTransmissive && light.FogLighting == SceneLight.FogLightingMode.None ) );
			Assert.IsTrue( imported.All( light => RenderContext.PackSceneLight( light, out var data, out _, out _ ) &&
				data.LightCookieTextureIndex == (uint)light.LightCookie.Index && (data.Flags & 14) == 0 ) );
			Assert.AreEqual( 1, imported.Select( light => light.LightCookie.Index ).Distinct().Count() );
			var lit = await Read( capture, "pinned_lit" );
			first.GltfInstance.SetNodeVisibilityOverrides( new Dictionary<int, bool> { [0] = false, [3] = false, [5] = false, [7] = false, [9] = false, [11] = false, [12] = false, [13] = false } );
			var dark = await Read( capture, "pinned_lights_hidden" );
			var changed = PositiveDifference( lit, dark );
			Assert.IsTrue( changed.Count > 100 && changed.Max > .05f, "Default projected glTF material did not receive the finite lights." );
			first.GltfInstance.SetNodeVisibilityOverrides( null );
			var restored = await Read( capture, "pinned_lights_restored" );
			Assert.IsTrue( MaxDifference( lit, restored ) < .006f, "Restored light visibility did not restore pixels." );
			Assert.IsTrue( first.GltfInstance.DrawCalls > 0 && first.GltfInstance.CallbackErrors.Length == 0 );

			second = AddRenderer( capture.Scene, model, new Vector3( 500, 0, 0 ) );
			Assert.AreEqual( 16, Imported( capture.Scene ).Length );
			Assert.AreEqual( 8, Imported( capture.Scene ).Count( light => light.Position.x > 500 ) );
			first.Enabled = false;
			await WaitForDeletes( capture );
			var remaining = Imported( capture.Scene );
			Assert.AreEqual( 8, remaining.Length );
			Assert.IsTrue( remaining.All( light => light.Position.x > 500 ) );
			second.GameObject.Destroy();
			second = null;
			await WaitForDeletes( capture );
			Assert.AreEqual( 0, Imported( capture.Scene ).Length );
			Measurements.Add( new { compiled, shaders, initialLights = 8, twoInstances = 16, retained = 8, changed.Count, changed.Max } );
		}
		finally
		{
			await MainThread.Wait();
			first?.GameObject.Destroy();
			second?.GameObject.Destroy();
			await WaitForDeletes( capture );
			model?.Destroy();
		}
	}

	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public async Task RequiredUnboundedRejectsAndOptionalUnboundedFallsBack( bool compiled )
	{
		await MainThread.Wait();
		using var files = new MountedFixture();
		var required = files.Write( "required", LightOnly( required: true, range: null ), compiled );
		var optional = files.Write( "optional", LightOnly( required: false, range: null ), compiled );
		var failure = Assert.ThrowsException<GltfException>( () => Model.Load( required ) );
		StringAssert.Contains( failure.Message, "unbounded point" );
		var model = Model.Load( optional );
		using var capture = CreateCapture( new Vector3( 200, 0, 0 ), Vector3.Zero, 100 );
		try
		{
			AssertManaged( model );
			var renderer = AddRenderer( capture.Scene, model, Vector3.Zero );
			Assert.IsNotNull( renderer.GltfInstance );
			Assert.AreEqual( 0, Imported( capture.Scene ).Length );
			renderer.GameObject.Destroy();
			await WaitForDeletes( capture );
		}
		finally { model?.Destroy(); }
		Measurements.Add( new { compiled, requiredFailure = failure.Message, optionalFallbackLights = 0 } );
	}

	[TestMethod]
	public async Task LightOnlyModelUsesStockGltfShaderWithoutChangingNativeVmdl()
	{
		await MainThread.Wait();
		using var files = new MountedFixture();
		var lightPath = files.Write( "point", LightOnly( required: true, range: 4 ), false );
		var receiverPath = files.Write( "receiver", Receiver(), false );
		using var capture = CreateCapture( new Vector3( 200, 0, 0 ), Vector3.Zero, 100 );
		var lightModel = Model.Load( lightPath );
		var receiverModel = Model.Load( receiverPath );
		SceneModel native = null;
		ModelRenderer light = null, receiver = null;
		try
		{
			AssertManaged( lightModel );
			Assert.IsFalse( lightModel.HasRenderMeshes(), "A light-only model cannot fabricate geometry." );
			AssertManaged( receiverModel );
			Assert.AreEqual( "shaders/gltf_stock_sampler.shader", receiverModel.Materials.Single().ShaderName, ignoreCase: true );
			var ordinary = capture.Scene.SceneWorld.SceneObjects.OfType<ScenePointLight>().Single();
			ordinary.Position = new Vector3( 80, 0, 0 );
			ordinary.Radius = 200;
			ordinary.LightColor = new Color( .5f, .5f, .5f );
			var box = Model.Load( "models/dev/box.vmdl" );
			Assert.IsTrue( box is { IsValid: true, IsError: false } && !box.native.IsNull );
			native = new SceneModel( capture.Scene.SceneWorld, box, Transform.Zero );
			native.SetMaterialOverride( capture.Material );
			native.Flags.CastShadows = false;
			var nativeBefore = await Read( capture, "vmdl_ordinary_only" );
			Assert.IsTrue( PositiveDifference( nativeBefore, new float[nativeBefore.Length] ).Count > 100 );
			light = AddRenderer( capture.Scene, lightModel, Vector3.Zero );
			Assert.AreEqual( 1, Imported( capture.Scene ).Length );
			var nativeAfter = await Read( capture, "vmdl_ordinary_with_marked_point" );
			Assert.IsTrue( MaxDifference( nativeBefore, nativeAfter ) < .006f, "Marked point changed stock vmdl pixels." );
			native.Delete();
			native = null;
			await WaitForDeletes( capture );
			receiver = AddRenderer( capture.Scene, receiverModel, Vector3.Zero );
			var both = await Read( capture, "stock_gltf_both" );
			light.Enabled = false;
			await WaitForDeletes( capture );
			var ordinaryOnly = await Read( capture, "stock_gltf_ordinary_only" );
			ordinary.LightColor = Color.Black;
			var dark = await Read( capture, "stock_gltf_dark" );
			var ordinaryDelta = PositiveDifference( ordinaryOnly, dark );
			var markedDelta = PositiveDifference( both, ordinaryOnly );
			Assert.IsTrue( ordinaryDelta.Count > 100, "Ordinary native point lost its response on the stock glTF material." );
			Assert.IsTrue( markedDelta.Count > 100, "Light-only model did not illuminate another glTF instance." );
			light.Enabled = true;
			var markedOnly = await Read( capture, "stock_gltf_marked_only" );
			light.GameObject.WorldPosition = new Vector3( 4 / .0254f, 0, 0 );
			var beyondRange = await Read( capture, "stock_gltf_marked_beyond_range" );
			Assert.IsTrue( Imported( capture.Scene ).Single().Position.x > 4 / .0254f );
			Assert.IsTrue( PositiveDifference( markedOnly, dark ).Count > 100 );
			Assert.IsTrue( MaxDifference( beyondRange, dark ) < .006f, "Moved marked point exceeded its finite cutoff." );
			Assert.IsTrue( receiver.GltfInstance.DrawCalls > 0 && receiver.GltfInstance.CallbackErrors.Length == 0 );
			Measurements.Add( new { ordinaryDelta, markedDelta, nativeMaxDifference = MaxDifference( nativeBefore, nativeAfter ), beyondMaxDifference = MaxDifference( dark, beyondRange ) } );
		}
		finally
		{
			await MainThread.Wait();
			native?.Delete();
			light?.GameObject.Destroy();
			receiver?.GameObject.Destroy();
			await WaitForDeletes( capture );
			receiverModel?.Destroy();
			lightModel?.Destroy();
		}
	}

	[TestMethod]
	public async Task OrdinaryNativePointStillCastsShadowsOnDefaultGltfMaterial()
	{
		await MainThread.Wait();
		using var files = new MountedFixture();
		var receiverPath = files.Write( "receiver", Receiver(), false );
		var lightPath = files.Write( "point", LightOnly( required: false, range: 4 ), false );
		using var capture = CreateCapture( new Vector3( 200, 0, 0 ), Vector3.Zero, 100 );
		Model receiverModel = null, lightModel = null;
		ModelRenderer receiver = null, marked = null;
		SceneModel occluder = null;
		try
		{
			receiverModel = Model.Load( receiverPath );
			lightModel = Model.Load( lightPath );
			AssertManaged( receiverModel );
			AssertManaged( lightModel );
			receiver = AddRenderer( capture.Scene, receiverModel, Vector3.Zero );
			var ordinary = capture.Scene.SceneWorld.SceneObjects.OfType<ScenePointLight>().Single();
			ordinary.Position = new Vector3( 100, -80, 0 );
			ordinary.Radius = 300;
			ordinary.LightColor = Color.White * 4;
			var box = Model.Load( "models/dev/box.vmdl" );
			Assert.IsTrue( box is { IsValid: true, IsError: false } && !box.native.IsNull );
			var transform = new Transform( new Vector3( 50, -40, 0 ), Rotation.Identity, .3f );
			occluder = new SceneModel( capture.Scene.SceneWorld, box, transform );
			occluder.Flags.CastShadows = true;
			ordinary.ShadowsEnabled = false;
			var baselineUnshadowed = await Read( capture, "ordinary_alone_shadows_disabled" );
			ordinary.ShadowsEnabled = true;
			var baselineShadowed = await Read( capture, "ordinary_alone_shadows_enabled" );
			var baselineShadow = Center( baselineUnshadowed ) - Center( baselineShadowed );
			Measurements.Add( new { baselineShadow, baselineUnshadowed = Center( baselineUnshadowed ), baselineShadowed = Center( baselineShadowed ) } );
			Assert.IsTrue( baselineShadow > .02f, "The unmarked native shadow setup must darken the receiver." );

			marked = AddRenderer( capture.Scene, lightModel, Vector3.Zero );
			Assert.AreEqual( 1, Imported( capture.Scene ).Length );
			var shadowed = await Read( capture, "mixed_shadows_enabled" );
			ordinary.ShadowsEnabled = false;
			var unshadowed = await Read( capture, "mixed_shadows_disabled" );
			var centerShadow = Center( unshadowed ) - Center( shadowed );
			Measurements.Add( new { centerShadow, centerUnshadowed = Center( unshadowed ), centerShadowed = Center( shadowed ) } );
			Assert.IsTrue( Math.Abs( centerShadow - baselineShadow ) < .01f, "Marked light changed the ordinary point's native shadow contrast." );
		}
		finally
		{
			await MainThread.Wait();
			occluder?.Delete();
			receiver?.GameObject.Destroy();
			marked?.GameObject.Destroy();
			await WaitForDeletes( capture );
			receiverModel?.Destroy();
			lightModel?.Destroy();
		}
	}

	private static void AssertManaged( Model model ) =>
		Assert.IsTrue( model is { IsValid: true, IsError: false } && model.native.IsNull && model.GltfResource is not null );

	private static ScenePointLight[] Imported( Scene scene ) =>
		scene.SceneWorld.SceneObjects.OfType<ScenePointLight>().Where( light => light.IsValid() && light.LightCookie.IsValid() ).ToArray();

	private static ModelRenderer AddRenderer( Scene scene, Model model, Vector3 position )
	{
		using var scope = scene.Push();
		var go = scene.CreateObject();
		go.WorldPosition = position;
		var renderer = go.Components.Create<ModelRenderer>( false );
		renderer.Model = model;
		renderer.Enabled = true;
		scene.EditorTick( 0, 0 );
		return renderer;
	}

	private static async Task WaitForDeletes( GltfGpuTriangleGateTests.NativeCapture capture )
	{
		using ( capture.Scene.Push() ) { capture.Scene.EditorTick( 0, 0 ); capture.Scene.ProcessDeletes(); }
		await capture.WaitFrames();
		using ( capture.Scene.Push() ) capture.Scene.ProcessDeletes();
	}

	private static GltfGpuTriangleGateTests.NativeCapture CreateCapture( Vector3 position, Vector3 target, float height )
	{
		var output = Path.Combine( CheckoutRoot, "Temp/gltf-validation/direct-gpu/point-public-production", Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( output );
		var capture = new GltfGpuTriangleGateTests.NativeCapture( output, position, target, height );
		capture.Camera.EnablePostProcessing = false;
		capture.Scene.SceneWorld.AmbientLightColor = Color.Black;
		var light = capture.Scene.SceneWorld.SceneObjects.OfType<ScenePointLight>().Single();
		light.LightColor = Color.Black;
		light.ShadowsEnabled = false;
		return capture;
	}

	private static async Task<float[]> Read( GltfGpuTriangleGateTests.NativeCapture capture, string label )
	{
		await capture.WaitFrames();
		using ( capture.Scene.Push() ) { capture.Scene.PreCameraRender(); capture.Camera.InitializeRendering( false ); }
		using var target = Texture.CreateRenderTarget().WithSize( 192, 192 ).WithFormat( ImageFormat.RGBA32323232F ).Create( "gltf_public_point_" + Guid.NewGuid().ToString( "N" ) );
		target.Clear( Color.Magenta );
		await capture.WaitFrames();
		var pixels = new float[192 * 192 * 4];
		target.GetPixels( (0, 0, 192, 192), 0, 0, pixels.AsSpan(), ImageFormat.RGBA32323232F );
		Assert.IsTrue( pixels.Select( (value, i) => Math.Abs( value - (i % 4 == 1 ? 0 : 1) ) ).Max() < .000001f );
		using ( capture.Scene.Push() ) Assert.IsTrue( capture.Camera.RenderToTexture( target ) );
		await capture.WaitFrames();
		Array.Fill( pixels, float.NaN );
		target.GetPixels( (0, 0, 192, 192), 0, 0, pixels.AsSpan(), ImageFormat.RGBA32323232F );
		Assert.IsTrue( pixels.All( float.IsFinite ) && pixels.Select( (value, i) => Math.Abs( value - (i % 4 == 1 ? 0 : 1) ) ).Max() > .01f );
		Measurements.Add( new { label, center = Center( pixels ), float_sha256 = Hash( System.Runtime.InteropServices.MemoryMarshal.AsBytes( pixels.AsSpan() ) ) } );
		return pixels;
	}

	private static float Center( float[] pixels ) => Enumerable.Range( 92, 8 )
		.SelectMany( y => Enumerable.Range( 92, 8 ).Select( x => pixels[(y * 192 + x) * 4] ) ).Average();
	private static (int Count, float Max) PositiveDifference( float[] a, float[] b )
	{
		int count = 0;
		float max = 0;
		for ( var i = 0; i < a.Length; i += 4 )
		{
			float difference = Math.Max( a[i] - b[i], Math.Max( a[i + 1] - b[i + 1], a[i + 2] - b[i + 2] ) );
			if ( difference > .01f ) count++;
			max = Math.Max( max, difference );
		}
		return (count, max);
	}
	private static float MaxDifference( float[] a, float[] b ) =>
		Enumerable.Range( 0, a.Length ).Where( index => index % 4 != 3 ).Max( index => Math.Abs( a[index] - b[index] ) );

	private static byte[] LightOnly( bool required, float? range )
	{
		var definition = new JsonObject { ["type"] = "point", ["intensity"] = 2 };
		if ( range.HasValue ) definition["range"] = range.Value;
		var json = JsonNode.Parse( """
			{"asset":{"version":"2.0"},"scene":0,"scenes":[{"nodes":[0]}],
			"nodes":[{"translation":[0,0,1],"extensions":{"KHR_lights_punctual":{"light":0}}}],
			"extensionsUsed":["KHR_lights_punctual"],"extensions":{"KHR_lights_punctual":{}}}
			""" );
		json["extensions"][Extension]["lights"] = new JsonArray( definition );
		if ( required ) json["extensionsRequired"] = new JsonArray( Extension );
		return Encoding.UTF8.GetBytes( json.ToJsonString() );
	}

	private static byte[] Receiver()
	{
		var owned = Path.Combine( CheckoutRoot, "research/gltf-validation/direct-gpu/models/PointLightIntensityTest/receiver.gltf" );
		var bytes = File.ReadAllBytes( owned );
		Assert.AreEqual( ReceiverHash, Hash( bytes ) );
		var json = JsonNode.Parse( bytes );
		json.AsObject().Remove( "extensionsUsed" );
		json["materials"][0].AsObject().Remove( "extensions" );
		json["materials"][0]["pbrMetallicRoughness"]["baseColorFactor"] = new JsonArray( .8f, .8f, .8f, 1f );
		return Encoding.UTF8.GetBytes( json.ToJsonString() );
	}

	private static string Hash( ReadOnlySpan<byte> bytes ) => Convert.ToHexStringLower( SHA256.HashData( bytes ) );

	private sealed class MountedFixture : IDisposable
	{
		private readonly string directory = Path.Combine( Path.GetTempPath(), "gltf_public_point_" + Guid.NewGuid().ToString( "N" ) );
		private readonly LocalFileSystem mount;
		internal MountedFixture()
		{
			Directory.CreateDirectory( directory );
			mount = new LocalFileSystem( directory );
			FileSystem.Mounted.Mount( mount );
		}
		internal string Write( string name, byte[] source, bool compiled, Func<string, byte[]> resolve = null )
		{
			var path = Path.GetFileName( directory ) + "_" + name + (compiled ? ".glb" : ".gltf");
			if ( compiled )
			{
				var extensions = GltfModelLoader.SupportedExtensions.Append( Extension ).ToHashSet( StringComparer.Ordinal );
				File.WriteAllBytes( Path.Combine( directory, path + "_c" ), GltfCompiler.Compile( source, resolve, extensions ) );
			}
			else
			{
				File.WriteAllBytes( Path.Combine( directory, path ), source );
				if ( resolve is not null )
					foreach ( var dependency in new[] { "PointLightIntensityTest.bin", "LampColorNames.png" } )
						File.WriteAllBytes( Path.Combine( directory, dependency ), resolve( dependency ) );
			}
			return path;
		}
		public void Dispose()
		{
			FileSystem.Mounted.UnMount( mount );
			mount.Dispose();
			Directory.Delete( directory, true );
		}
	}
}
