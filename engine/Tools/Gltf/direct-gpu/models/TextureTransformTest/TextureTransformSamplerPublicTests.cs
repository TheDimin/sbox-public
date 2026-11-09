using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Sandbox.Gltf.DirectGpu.TextureTransformTestValidation;

/// <summary>
/// Public stock-material sampler boundary. The 2x2 literal PNG and source UVs
/// are independent of the producer. No UV transform is used in this fixture.
/// </summary>
[TestClass]
public sealed class TextureTransformSamplerPublicTests
{
	public static string CheckoutRoot { get; set; }
	public static List<object> Measurements { get; } = new();
	private const int Size = 192;
	private const float Height = 115;
	// Source rows: red/green, then blue/yellow. Alpha is straight and opaque.
	private const string Png = "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAF0lEQVR4nGP4z8DwHwwZGP7///+f4T8AR8oI+P1do8oAAAAASUVORK5CYII=";

	public static void VerifyTools( string expectedSha256 )
	{
		var assembly = typeof( Editor.SceneRenderingWidget ).Assembly;
		using var file = File.OpenRead( assembly.Location );
		using var pe = new PEReader( file );
		var metadata = pe.GetMetadataReader();
		Assert.AreEqual( assembly.ManifestModule.ModuleVersionId, metadata.GetGuid( metadata.GetModuleDefinition().Mvid ), "Loaded Tools differs from its disk image." );
		Assert.AreEqual( expectedSha256, Convert.ToHexStringLower( SHA256.HashData( File.ReadAllBytes( assembly.Location ) ) ), "Loaded Tools image differs from the requested stage." );
	}

	[TestMethod]
	public Task Source() => Run( false );
	[TestMethod]
	public Task Compiled() => Run( true );

	private static async Task Run( bool compiled )
	{
		await MainThread.Wait();
		var output = Path.Combine( CheckoutRoot, "Temp/gltf-validation/direct-gpu/public-model-smokes/TextureTransformSampler", Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( output );
		using var capture = new GltfGpuTriangleGateTests.NativeCapture( output );
		var scene = capture.Scene; var camera = capture.Camera;
		camera.WorldPosition = new Vector3( 160, 0, 0 ); camera.WorldRotation = Rotation.LookAt( -camera.WorldPosition, Vector3.Up );
		camera.Orthographic = true; camera.OrthographicHeight = Height; camera.ZNear = 1; camera.ZFar = 1000;
		camera.CustomSize = new Vector2( Size, Size ); camera.BackgroundColor = Color.Black; camera.ClearFlags = ClearFlags.All;
		camera.EnablePostProcessing = false; camera.DebugMode = SceneCameraDebugMode.Albedo;
		var before = await capture.Take( "empty_before" ); Assert.AreEqual( 0, before.Coverage );
		using var mount = new PublicMount( CreateFixture(), compiled );
		ModelRenderer renderer = null;
		var observations = new List<object>();
		var failures = new List<string>();
		try
		{
			var model = mount.Load();
			Assert.IsTrue( model is { IsValid: true, IsError: false } && model.native.IsNull && model.GltfResource is not null, "Public Model.Load must select the managed renderer." );
			using ( scene.Push() )
			{
				renderer = scene.CreateObject().Components.Create<ModelRenderer>( false );
				renderer.WorldTransform = Transform.Zero; renderer.Model = model; renderer.Enabled = true; scene.EditorTick( 0, 0 );
			}
			var frame = await capture.Take( compiled ? "compiled_sampler_grid" : "source_sampler_grid" );
			var instance = renderer.GltfInstance;
			Assert.IsTrue( instance is not null && instance.CallbackErrors.Length == 0 && instance.DrawCalls > 0 && instance.SubmittedRevision == instance.PoseRevision, "No successful public draw submission." );
			Assert.IsTrue( frame.Coverage > 64, "Sampler grid did not render." );
			for ( var row = 0; row < 6; row++ )
			{
				var axis = row < 3 ? "U" : "V"; var wrap = row % 3;
				var first = Sample( frame.Pixels, row, 0 ); var second = Sample( frame.Pixels, row, 1 );
				var firstColor = ColorClass( first ); var secondColor = ColorClass( second );
				var expectedSecond = axis == "U" ? "green" : "blue";
				if ( firstColor != "red" || secondColor != expectedSecond ) failures.Add( $"{axis} row {wrap}: expected in-range red/{expectedSecond}, got {firstColor}/{secondColor}." );
				var separation = Distance( first, second );
				if ( separation < .25f ) failures.Add( $"{axis} row {wrap}: independent in-range colour controls are indistinguishable ({separation:R})." );
				for ( var col = 2; col < 4; col++ )
				{
					var useFirst = wrap == 0 ? col == 2 : wrap == 1 ? false : col == 3;
					var expected = useFirst ? first : second; var observed = Sample( frame.Pixels, row, col );
					var error = Distance( expected, observed );
					observations.Add( new { axis, wrap = new[] { "repeat", "clamp", "mirror" }[wrap], uv = col == 2 ? 1.25 : 1.75,
						expected = useFirst ? firstColor : secondColor, observed = ColorClass( observed ), error, tolerance = 4f / 255f } );
					if ( error > 4f / 255f ) failures.Add( $"{axis} row {wrap}, UV { (col == 2 ? 1.25 : 1.75) }: max error {error:R}." );
				}
			}
			Measurements.Add( new { form = compiled ? "derived compiled GLB" : "source glTF", frame.Path, frame.Sha256,
				frame.CameraStateJson, observations, failures, scope = "Public stock material: point-filter S/T wrapping and 2x2 source orientation, not mip or linear-filter proof." } );
		}
		finally
		{
			await MainThread.Wait();
			if ( renderer is not null ) using ( scene.Push() ) { renderer.GameObject.Destroy(); scene.EditorTick( 0, 0 ); scene.ProcessDeletes(); }
		}
		var after = await capture.Take( "empty_after" ); Assert.AreEqual( 0, after.Coverage );
		Assert.IsTrue( failures.Count == 0, string.Join( "\n", failures ) );
	}

	private static Color Sample( Color[] pixels, int row, int column )
	{
		var scale = (double)Size / Height / .0254;
		var x = (int)Math.Round( Size * .5 + (column - 1.5) * .55 * scale );
		var y = (int)Math.Round( Size * .5 - (2.5 - row) * .4 * scale );
		double r = 0, g = 0, b = 0;
		for ( var dy = -2; dy <= 2; dy++ ) for ( var dx = -2; dx <= 2; dx++ )
		{
			var p = pixels[(y + dy) * Size + x + dx]; r += p.r; g += p.g; b += p.b;
		}
		return new Color( (float)(r / 25), (float)(g / 25), (float)(b / 25), 1 );
	}
	private static float Distance( Color a, Color b ) => MathF.Max( MathF.Abs( a.r - b.r ), MathF.Max( MathF.Abs( a.g - b.g ), MathF.Abs( a.b - b.b ) ) );
	private static string ColorClass( Color p ) => p.r > p.g + .2f && p.r > p.b + .2f ? "red" :
		p.g > p.r + .2f && p.g > p.b + .2f ? "green" : p.b > p.r + .2f && p.b > p.g + .2f ? "blue" : "other";

	private static byte[] CreateFixture()
	{
		using var stream = new MemoryStream(); using var writer = new BinaryWriter( stream );
		foreach ( var (x, y) in new[] { (-.21f, -.15f), (.21f, -.15f), (.21f, .15f), (-.21f, .15f) } ) { writer.Write( x ); writer.Write( y ); writer.Write( 0f ); }
		for ( var vertex = 0; vertex < 4; vertex++ ) { writer.Write( 0f ); writer.Write( 0f ); writer.Write( 1f ); }
		foreach ( ushort index in new ushort[] { 0, 1, 2, 0, 2, 3 } ) writer.Write( index );
		var coords = new[] { .25f, .75f, 1.25f, 1.75f };
		for ( var axis = 0; axis < 2; axis++ ) foreach ( var value in coords )
			for ( var vertex = 0; vertex < 4; vertex++ ) { writer.Write( axis == 0 ? value : .25f ); writer.Write( axis == 1 ? value : .25f ); }
		var bytes = stream.ToArray();
		var root = JsonNode.Parse( """
			{"asset":{"version":"2.0"},"buffers":[{}],"bufferViews":[{"buffer":0,"byteOffset":0,"byteLength":48},{"buffer":0,"byteOffset":48,"byteLength":48},{"buffer":0,"byteOffset":96,"byteLength":12}],
			"accessors":[{"bufferView":0,"componentType":5126,"count":4,"type":"VEC3","min":[-0.21,-0.15,0],"max":[0.21,0.15,0]},
			{"bufferView":1,"componentType":5126,"count":4,"type":"VEC3"},{"bufferView":2,"componentType":5123,"count":6,"type":"SCALAR"}],
			"images":[{}],"samplers":[],"textures":[],"materials":[],"meshes":[],"nodes":[],"scenes":[{"nodes":[]}],"scene":0}
			""" ).AsObject();
		root["buffers"][0]["uri"] = "data:application/octet-stream;base64," + Convert.ToBase64String( bytes ); root["buffers"][0]["byteLength"] = bytes.Length;
		root["images"][0]["uri"] = "data:image/png;base64," + Png;
		for ( var uv = 0; uv < 8; uv++ )
		{
			root["bufferViews"].AsArray().Add( new JsonObject { ["buffer"] = 0, ["byteOffset"] = 108 + uv * 32, ["byteLength"] = 32 } );
			root["accessors"].AsArray().Add( new JsonObject { ["bufferView"] = 3 + uv, ["componentType"] = 5126, ["count"] = 4, ["type"] = "VEC2" } );
		}
		var modes = new[] { 10497, 33071, 33648 };
		for ( var mode = 0; mode < 3; mode++ )
		{
			root["samplers"].AsArray().Add( new JsonObject { ["wrapS"] = modes[mode], ["wrapT"] = modes[mode], ["minFilter"] = 9728, ["magFilter"] = 9728 } );
			root["textures"].AsArray().Add( new JsonObject { ["source"] = 0, ["sampler"] = mode } );
			root["materials"].AsArray().Add( new JsonObject { ["pbrMetallicRoughness"] = new JsonObject { ["baseColorTexture"] = new JsonObject { ["index"] = mode }, ["metallicFactor"] = 0, ["roughnessFactor"] = 1 } } );
		}
		for ( var row = 0; row < 6; row++ ) for ( var column = 0; column < 4; column++ )
		{
			var mesh = row * 4 + column; var uv = (row < 3 ? 0 : 4) + column;
			root["meshes"].AsArray().Add( new JsonObject { ["primitives"] = new JsonArray { new JsonObject { ["attributes"] = new JsonObject { ["POSITION"] = 0, ["NORMAL"] = 1, ["TEXCOORD_0"] = 3 + uv }, ["indices"] = 2, ["material"] = row % 3 } } } );
			root["nodes"].AsArray().Add( new JsonObject { ["mesh"] = mesh, ["translation"] = new JsonArray( (column - 1.5) * .55, (2.5 - row) * .4, 0 ) } );
			root["scenes"][0]["nodes"].AsArray().Add( mesh );
		}
		return Encoding.UTF8.GetBytes( root.ToJsonString() );
	}

	private sealed class PublicMount : IDisposable
	{
		private readonly string directory, path;
		private readonly BaseFileSystem aggregate;
		private LocalFileSystem mount;
		private Model model;
		internal PublicMount( byte[] source, bool compiled )
		{
			directory = Path.Combine( Path.GetTempPath(), "gltf_sampler_" + Guid.NewGuid().ToString( "N" ) );
			try
			{
				Directory.CreateDirectory( directory ); path = "sampler_" + Guid.NewGuid().ToString( "N" ) + (compiled ? ".glb_c" : ".gltf");
				File.WriteAllBytes( Path.Combine( directory, path ), compiled ? GltfCompiler.Compile( source ) : source );
				aggregate = FileSystem.Mounted ?? throw new InvalidOperationException( "Mounted filesystem unavailable." );
				mount = new LocalFileSystem( directory ); aggregate.Mount( mount );
			}
			catch { Dispose(); throw; }
		}
		internal Model Load() => model = Model.Load( path );
		public void Dispose()
		{
			var errors = new List<Exception>(); void Clean( Action action ) { try { action(); } catch ( Exception error ) { errors.Add( error ); } }
			if ( model is not null && model.ResourcePath == Resource.FixPath( path ) ) Clean( model.Destroy ); model = null;
			if ( mount is not null ) { Clean( () => aggregate.UnMount( mount ) ); Clean( mount.Dispose ); mount = null; }
			Clean( () => { if ( Directory.Exists( directory ) ) Directory.Delete( directory, true ); } );
			if ( errors.Count != 0 ) throw new AggregateException( "Sampler grid cleanup failed.", errors );
		}
	}
}
