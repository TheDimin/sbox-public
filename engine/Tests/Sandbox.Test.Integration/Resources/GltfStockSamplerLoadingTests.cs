using Sandbox.Gltf;
using Sandbox.Rendering;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ResourceTests;

/// <summary>
/// Positive stock-material sampler acceptance through actual Model.Load.
/// Form0 is external glTF,1 is independently packed GLB,2 is compiled-only GLB.
/// Current stock-sampler failures must remain red; the anonymous binding
/// diagnostic is a separate gate and is never substituted for these models.
/// </summary>
[TestClass]
public sealed class GltfStockSamplerLoadingTests
{
	private const int Size = 129;
	private const float Unit = 1f / .0254f;

	[TestMethod]
	[DataRow( 9728, 9728, 0 )]
	[DataRow( 9728, 9728, 1 )]
	[DataRow( 9728, 9728, 2 )]
	[DataRow( 9728, 9729, 0 )]
	[DataRow( 9728, 9729, 1 )]
	[DataRow( 9728, 9729, 2 )]
	[DataRow( 9729, 9728, 0 )]
	[DataRow( 9729, 9728, 1 )]
	[DataRow( 9729, 9728, 2 )]
	[DataRow( 9729, 9729, 0 )]
	[DataRow( 9729, 9729, 1 )]
	[DataRow( 9729, 9729, 2 )]
	[DataRow( 9984, 9728, 0 )]
	[DataRow( 9984, 9728, 1 )]
	[DataRow( 9984, 9728, 2 )]
	[DataRow( 9984, 9729, 0 )]
	[DataRow( 9984, 9729, 1 )]
	[DataRow( 9984, 9729, 2 )]
	[DataRow( 9985, 9728, 0 )]
	[DataRow( 9985, 9728, 1 )]
	[DataRow( 9985, 9728, 2 )]
	[DataRow( 9985, 9729, 0 )]
	[DataRow( 9985, 9729, 1 )]
	[DataRow( 9985, 9729, 2 )]
	[DataRow( 9986, 9728, 0 )]
	[DataRow( 9986, 9728, 1 )]
	[DataRow( 9986, 9728, 2 )]
	[DataRow( 9986, 9729, 0 )]
	[DataRow( 9986, 9729, 1 )]
	[DataRow( 9986, 9729, 2 )]
	[DataRow( 9987, 9728, 0 )]
	[DataRow( 9987, 9728, 1 )]
	[DataRow( 9987, 9728, 2 )]
	[DataRow( 9987, 9729, 0 )]
	[DataRow( 9987, 9729, 1 )]
	[DataRow( 9987, 9729, 2 )]
	public async Task EveryFilterPairPreservesMagMinAndMipSelection( int min, int mag, int form )
	{
		var image = Image( 4, 4, ( x, y ) => x % 2 == 0 ? Color.Black : Color.White );
		foreach ( var lod in new[] { -1f, .25f } )
		{
			var baseValue = (lod < 0 ? mag == 9729 : min is 9729 or 9985 or 9987) ? .75f : 1f;
			var value = lod >= 0 && min is 9986 or 9987 ? baseValue * .75f + .5f * .25f : baseValue;
			var span = Size * MathF.Pow( 2, lod ) / 4f;
			using var fixture = new Fixture( image, 4, 4, min, mag, 10497, 10497, form, .4375f, .375f, span );
			await fixture.AssertAlbedo( new Vector3( value ), $"min={min}, mag={mag}, lod={lod}, form={form}" );
		}
	}

	[TestMethod]
	[DataRow( 10497, 10497, 0 )]
	[DataRow( 10497, 10497, 1 )]
	[DataRow( 10497, 10497, 2 )]
	[DataRow( 10497, 33648, 0 )]
	[DataRow( 10497, 33648, 1 )]
	[DataRow( 10497, 33648, 2 )]
	[DataRow( 10497, 33071, 0 )]
	[DataRow( 10497, 33071, 1 )]
	[DataRow( 10497, 33071, 2 )]
	[DataRow( 33648, 10497, 0 )]
	[DataRow( 33648, 10497, 1 )]
	[DataRow( 33648, 10497, 2 )]
	[DataRow( 33648, 33648, 0 )]
	[DataRow( 33648, 33648, 1 )]
	[DataRow( 33648, 33648, 2 )]
	[DataRow( 33648, 33071, 0 )]
	[DataRow( 33648, 33071, 1 )]
	[DataRow( 33648, 33071, 2 )]
	[DataRow( 33071, 10497, 0 )]
	[DataRow( 33071, 10497, 1 )]
	[DataRow( 33071, 10497, 2 )]
	[DataRow( 33071, 33648, 0 )]
	[DataRow( 33071, 33648, 1 )]
	[DataRow( 33071, 33648, 2 )]
	[DataRow( 33071, 33071, 0 )]
	[DataRow( 33071, 33071, 1 )]
	[DataRow( 33071, 33071, 2 )]
	public async Task WrapAxesSelectIndependentTexelsAcrossPositiveAndNegativeUv( int wrapS, int wrapT, int form )
	{
		Color[] colors = [Color.Red, Color.Green, Color.Blue, Color.Yellow];
		var image = Image( 2, 2, ( x, y ) => colors[y * 2 + x] );
		foreach ( var uv in new[] { new Vector2( 1.25f, -.25f ), new Vector2( -.25f, 1.25f ) } )
		{
			var pixel = colors[Texel( uv.y, wrapT, 2 ) * 2 + Texel( uv.x, wrapS, 2 )];
			using var fixture = new Fixture( image, 2, 2, 9728, 9728, wrapS, wrapT, form, uv.x, uv.y, 0 );
			await fixture.AssertAlbedo( new Vector3( pixel.r, pixel.g, pixel.b ), $"S={wrapS}, T={wrapT}, uv={uv}, form={form}" );
		}
	}

	[TestMethod]
	[DataRow( 0 )]
	[DataRow( 1 )]
	[DataRow( 2 )]
	public async Task NonPowerOfTwoImagesRetainDimensionsAndWrapTexelCenters( int form )
	{
		var image = Image( 3, 5, ( x, y ) => new Color( x / 2f, y / 4f, 0 ) );
		using var fixture = new Fixture( image, 3, 5, 9987, 9728, 10497, 10497, form, 1 + 1f / 6, 1 + 1f / 10, 0 );
		var texture = fixture.Model.Materials[0].GetTexture( "g_tColor" );
		Assert.IsNotNull( texture );
		Assert.AreEqual( 3, texture.Width, "NPOT width must not be silently rescaled." );
		Assert.AreEqual( 5, texture.Height, "NPOT height must not be silently rescaled." );
		await fixture.AssertAlbedo( Vector3.Zero, "NPOT repeat texel-center lookup" );
	}

	[TestMethod]
	[DataRow( 0 )]
	[DataRow( 1 )]
	[DataRow( 2 )]
	public async Task ColorTransferAndFactorsAreAppliedInLinearLight( int form )
	{
		var image = Image( 1, 1, ( x, y ) => new Color( 128f / 255, 128f / 255, 128f / 255 ) );
		using var fixture = new Fixture( image, 1, 1, 9728, 9728, 10497, 10497, form, .5f, .5f, 0, colorFactor: .5f );
		await fixture.AssertAlbedo( new Vector3( Decode( 128f / 255 ) * .5f ), "sRGB image * linear material factor" );
	}

	[TestMethod]
	[DataRow( 0 )]
	[DataRow( 1 )]
	[DataRow( 2 )]
	public async Task SharedImageKeepsPerRoleColorSpaceAndIndependentSamplers( int form )
	{
		var image = Image( 2, 1, ( x, y ) => x == 0 ? new Color( 1, 64f / 255, 32f / 255 ) : new Color( 0, 192f / 255, 224f / 255 ) );
		using var fixture = new Fixture( image, 2, 1, 9728, 9728, 33071, 10497, form, 1.25f, .5f, 0, separateMetallicSampler: true );
		await fixture.AssertAlbedo( new Vector3( 0, Decode( 192f / 255 ), Decode( 224f / 255 ) ), "Base color clamps the shared image and decodes sRGB." );
		await fixture.AssertRoughness( 64f / 255, "Metallic/roughness repeats the shared image and reads G linearly." );
	}

	[TestMethod]
	[DataRow( 0 )]
	[DataRow( 1 )]
	[DataRow( 2 )]
	public async Task ComponentOverrideDoesNotChangeAnotherInstanceOrStockTemplate( int form )
	{
		var image = Image( 2, 1, ( x, y ) => x == 0 ? Color.Red : Color.Blue );
		using var fixture = new Fixture( image, 2, 1, 9728, 9728, 33071, 10497, form, 1.25f, .5f, 0 );
		await fixture.AssertIndependentInstances();
	}

	private static int Texel( float coordinate, int wrap, int size )
	{
		var value = wrap switch
		{
			10497 => coordinate - MathF.Floor( coordinate ),
			33648 => 1 - MathF.Abs( (coordinate - 2 * MathF.Floor( coordinate / 2 )) - 1 ),
			_ => Math.Clamp( coordinate, 0, 1 )
		};
		return Math.Clamp( (int)MathF.Floor( value * size ), 0, size - 1 );
	}
	private static float Decode( float x ) => x <= .04045f ? x / 12.92f : MathF.Pow( (x + .055f) / 1.055f, 2.4f );
	private static float Encode( float x ) => x <= .0031308f ? 12.92f * x : 1.055f * MathF.Pow( x, 1f / 2.4f ) - .055f;
	private static bool Visible( Color c ) => MathF.Abs( c.r ) > .02f || MathF.Abs( c.g ) > .02f || MathF.Abs( c.b ) > .02f;

	private sealed class Fixture : IDisposable
	{
		private readonly string directory;
		private readonly BaseFileSystem aggregate;
		private LocalFileSystem mount;
		private Scene scene;
		private CameraComponent camera;
		private ModelRenderer renderer;
		private readonly List<Material> materials = [];
		private readonly List<Texture> textures = [];
		internal Model Model { get; private set; }

		internal Fixture( byte[] image, int width, int height, int min, int mag, int wrapS, int wrapT, int form,
			float u, float v, float span, float colorFactor = 1, bool separateMetallicSampler = false )
		{
			if ( !Graphics.IsAvailable ) Assert.Inconclusive( "Requires an authorized in-engine graphics session." );
			Assert.IsTrue( ThreadSafe.IsMainThread && !Graphics.IsActive );
			Assert.IsNotNull( ManagedSceneRendering.Renderer, "This is the managed scene capture gate; native/default route requires its own controls." );
			directory = Path.Combine( Path.GetTempPath(), "gltf_stock_sampler_" + Guid.NewGuid().ToString( "N" ) );
			Directory.CreateDirectory( directory ); aggregate = Sandbox.FileSystem.Mounted;
			try
			{
				Assert.IsTrue( Material.Load( "materials/default/white.vmat" ) is { IsValid: true, IsError: false } );
				var stem = "sampler_" + Guid.NewGuid().ToString( "N" );
				var source = Source( image, min, mag, wrapS, wrapT, u, v, span, colorFactor, separateMetallicSampler, binary: form != 0 );
				string filename;
				if ( form == 0 )
				{
					filename = stem + ".gltf"; File.WriteAllBytes( Path.Combine( directory, "geometry.bin" ), source.Geometry );
					File.WriteAllBytes( Path.Combine( directory, "image.png" ), image ); File.WriteAllBytes( Path.Combine( directory, filename ), source.Json );
				}
				else
				{
					filename = stem + (form == 1 ? ".glb" : ".glb_c");
					var glb = Glb( source.Json, source.Geometry );
					File.WriteAllBytes( Path.Combine( directory, filename ), form == 1 ? glb : GltfCompiler.Compile( glb ) );
				}
				mount = new LocalFileSystem( directory ); aggregate.Mount( mount );
				Model = Sandbox.Model.Load( filename ); Assert.IsTrue( Model is { IsValid: true, IsError: false }, "Actual Model.Load must accept this valid core sampler asset." );
				scene = Scene.CreateEditorScene();
				using var scope = scene.Push();
				camera = scene.CreateObject().Components.Create<CameraComponent>(); camera.BackgroundColor = Color.Black;
				camera.Orthographic = true; camera.OrthographicHeight = 2 * Unit; camera.WorldPosition = new Vector3( 4 * Unit, 0, 0 );
				camera.WorldRotation = Rotation.LookAt( Vector3.Backward, Vector3.Up ); camera.ZNear = 1; camera.ZFar = 8 * Unit;
				camera.DebugMode = SceneCameraDebugMode.Albedo; camera.EnablePostProcessing = false;
			}
			catch { Dispose(); throw; }
		}

		private ModelRenderer Show( Material material = null )
		{
			using var scope = scene.Push();
			var value = scene.CreateObject().Components.Create<ModelRenderer>( false ); value.Model = Model; value.MaterialOverride = material;
			value.Enabled = true; scene.EditorTick( 0, 0 ); return value;
		}
		private void Remove( ModelRenderer value )
		{
			using var scope = scene.Push(); value.GameObject.Destroy(); scene.EditorTick( 0, 0 );
		}
		internal async Task AssertAlbedo( Vector3 linear, string label )
		{
			camera.DebugMode = SceneCameraDebugMode.Albedo;
			await AssertAgainstControl( Control( linear ), label );
		}
		internal async Task AssertRoughness( float roughness, string label )
		{
			camera.DebugMode = SceneCameraDebugMode.Roughness;
			var control = Control( Vector3.One );
			float[] data = [.5019608f, .5019608f, roughness, 1];
			var texture = Texture.Create( 1, 1, ImageFormat.RGBA32323232F ).WithMips( 1 ).WithData( MemoryMarshal.AsBytes( data.AsSpan() ).ToArray() ).WithStaticUsage().Finish(); textures.Add( texture );
			Assert.IsTrue( control.Set( "g_tNormal", texture ) );
			await AssertAgainstControl( control, label );
		}
		private Material Control( Vector3 linear )
		{
			var material = Material.Load( "materials/default/white.vmat" ).CreateCopy(); materials.Add( material );
			material.SetFeature( "F_RENDER_BACKFACES", 1 ); material.Set( "g_vColorTint", new Vector4( Encode( linear.x ), Encode( linear.y ), Encode( linear.z ), 1 ) ); return material;
		}
		private async Task AssertAgainstControl( Material control, string label )
		{
			Assert.AreEqual( 0, (await Capture()).Count( Visible ), label + ": fresh empty-before control." );
			var visibleControl = Show( Control( Vector3.One ) );
			Assert.IsTrue( (await Capture()).Count( Visible ) > 100, label + ": independently white geometry control must be visible, including cases whose expected sample is black." );
			Remove( visibleControl );
			Assert.AreEqual( 0, (await Capture()).Count( Visible ), label + ": control geometry must disappear." );
			renderer = Show(); var actual = await Capture();
			renderer.MaterialOverride = control; var reference = await Capture();
			var a = actual[(Size / 2) * Size + Size / 2]; var b = reference[(Size / 2) * Size + Size / 2];
			Assert.IsTrue( float.IsFinite( a.r ) && float.IsFinite( a.g ) && float.IsFinite( a.b ), label + ": finite readback." );
			Assert.IsTrue( MathF.Abs( a.r - b.r ) <= .025f && MathF.Abs( a.g - b.g ) <= .025f && MathF.Abs( a.b - b.b ) <= .025f,
				$"{label}: actual ({a}) differs from independent stock-color/control ({b})." );
			Remove( renderer ); renderer = null;
			Assert.AreEqual( 0, (await Capture()).Count( Visible ), label + ": fresh empty-after control." );
		}
		internal async Task AssertIndependentInstances()
		{
			var stock = Material.Load( "materials/default/white.vmat" ); var originalTint = stock.GetVector4( "g_vColorTint" );
			var green = Control( new Vector3( 0, 1, 0 ) ); var first = Show(); var second = Show( green );
			first.WorldScale = .4f; second.WorldScale = .4f; first.WorldPosition = new Vector3( 0, -.5f * Unit, 0 ); second.WorldPosition = new Vector3( 0, .5f * Unit, 0 );
			var pixels = await Capture(); var left = pixels[(Size / 2) * Size + Size / 4]; var right = pixels[(Size / 2) * Size + 3 * Size / 4];
			Assert.IsTrue( left.b > .8f && left.r < .05f && left.g < .05f, "The unmodified instance must retain its authored clamp sampler/material." );
			Assert.IsTrue( right.g > .8f && right.r < .05f && right.b < .05f, "The second instance's override must be independently rendered." );
			Assert.AreEqual( originalTint, stock.GetVector4( "g_vColorTint" ), "Overrides must not mutate the stock template." );
			Remove( first ); Remove( second ); Assert.AreEqual( 0, (await Capture()).Count( Visible ) );
		}
		private async Task<Color[]> Capture()
		{
			await Frames();
			using var target = Texture.CreateRenderTarget().WithSize( Size, Size ).WithFormat( ImageFormat.RGBA8888 ).WithInitialColor( Color.Magenta ).Create();
			using ( scene.Push() ) { scene.EditorTick( 0, 0 ); scene.PreCameraRender(); camera.InitializeRendering( false ); target.Clear( Color.Magenta ); Assert.IsTrue( ManagedSceneRendering.Renderer.RenderToTexture( camera, target ) ); }
			await Frames(); using var bitmap = target.GetBitmap(); Assert.IsNotNull( bitmap ); var pixels = bitmap.GetPixels(); Assert.AreEqual( Size * Size, pixels.Length ); return pixels;
		}
		private static async Task Frames()
		{
			var frame = Application.FrameCount; var timer = Stopwatch.StartNew();
			while ( Application.FrameCount < frame + 3 ) { Assert.IsTrue( timer.Elapsed < TimeSpan.FromSeconds( 8 ), "The engine must keep receiving real frames." ); await Task.Delay( 20 ); await MainThread.Wait(); }
		}
		public void Dispose()
		{
			scene?.Destroy(); scene = null; Model?.Destroy(); Model = null;
			foreach ( var material in materials ) material.Destroy(); foreach ( var texture in textures ) texture.Dispose();
			if ( mount is not null ) { aggregate.UnMount( mount ); mount.Dispose(); mount = null; }
			if ( directory is not null && Directory.Exists( directory ) ) Directory.Delete( directory, true );
		}
	}

	private sealed record Asset( byte[] Json, byte[] Geometry );
	private static Asset Source( byte[] image, int min, int mag, int wrapS, int wrapT, float u, float v, float span, float factor, bool separateMetallicSampler, bool binary )
	{
		using var geometry = new MemoryStream(); using var writer = new BinaryWriter( geometry );
		foreach ( var value in new float[] { -1, -1, 0, 1, -1, 0, 1, 1, 0, -1, 1, 0 } ) writer.Write( value );
		foreach ( var value in new float[] { u - span / 2, v - span / 2, u + span / 2, v - span / 2, u + span / 2, v + span / 2, u - span / 2, v + span / 2 } ) writer.Write( value );
		for ( var vertex = 0; vertex < 4; vertex++ ) { writer.Write( 0f ); writer.Write( 0f ); writer.Write( 1f ); }
		foreach ( var index in new ushort[] { 0, 1, 2, 0, 2, 3 } ) writer.Write( index );
		var views = new List<object> { new { buffer = 0, byteOffset = 0, byteLength = 48 }, new { buffer = 0, byteOffset = 48, byteLength = 32 }, new { buffer = 0, byteOffset = 80, byteLength = 48 }, new { buffer = 0, byteOffset = 128, byteLength = 12 } };
		var imageDescriptor = new Dictionary<string, object>();
		if ( binary ) { imageDescriptor["bufferView"] = views.Count; imageDescriptor["mimeType"] = "image/png"; views.Add( new { buffer = 0, byteOffset = (int)geometry.Position, byteLength = image.Length } ); writer.Write( image ); }
		else imageDescriptor["uri"] = "image.png";
		var data = geometry.ToArray();
		var buffer = new Dictionary<string, object> { ["byteLength"] = data.Length }; if ( !binary ) buffer["uri"] = "geometry.bin";
		var pbr = new Dictionary<string, object> { ["baseColorTexture"] = new { index = 0 }, ["baseColorFactor"] = new[] { factor, factor, factor, 1 }, ["metallicFactor"] = separateMetallicSampler ? 1 : 0, ["roughnessFactor"] = 1 };
		var samplers = new List<object> { new { minFilter = min, magFilter = mag, wrapS, wrapT } };
		var textureDefinitions = new List<object> { new { source = 0, sampler = 0 } };
		if ( separateMetallicSampler ) { samplers.Add( new { minFilter = 9728, magFilter = 9728, wrapS = 10497, wrapT = 10497 } ); textureDefinitions.Add( new { source = 0, sampler = 1 } ); pbr["metallicRoughnessTexture"] = new { index = 1 }; }
		return new Asset( JsonSerializer.SerializeToUtf8Bytes( new
		{
			asset = new { version = "2.0", generator = "Independent core stock sampler acceptance" },
			buffers = new[] { buffer },
			bufferViews = views,
			accessors = new object[] { new { bufferView = 0, componentType = 5126, count = 4, type = "VEC3", min = new[] { -1, -1, 0 }, max = new[] { 1, 1, 0 } }, new { bufferView = 1, componentType = 5126, count = 4, type = "VEC2" }, new { bufferView = 2, componentType = 5126, count = 4, type = "VEC3" }, new { bufferView = 3, componentType = 5123, count = 6, type = "SCALAR" } },
			images = new[] { imageDescriptor },
			textures = textureDefinitions,
			samplers,
			materials = new[] { new { doubleSided = true, pbrMetallicRoughness = pbr } },
			meshes = new[] { new { primitives = new[] { new { attributes = new Dictionary<string, int> { ["POSITION"] = 0, ["TEXCOORD_0"] = 1, ["NORMAL"] = 2 }, indices = 3, material = 0 } } } },
			nodes = new[] { new { mesh = 0 } },
			scenes = new[] { new { nodes = new[] { 0 } } },
			scene = 0
		} ), data );
	}

	private static byte[] Glb( byte[] json, byte[] binary )
	{
		var jsonSize = (json.Length + 3) & ~3; var binSize = (binary.Length + 3) & ~3;
		using var stream = new MemoryStream(); using var writer = new BinaryWriter( stream );
		writer.Write( 0x46546c67 ); writer.Write( 2 ); writer.Write( 12 + 8 + jsonSize + 8 + binSize );
		writer.Write( jsonSize ); writer.Write( 0x4e4f534a ); writer.Write( json ); for ( var i = json.Length; i < jsonSize; i++ ) writer.Write( (byte)32 );
		writer.Write( binSize ); writer.Write( 0x004e4942 ); writer.Write( binary ); for ( var i = binary.Length; i < binSize; i++ ) writer.Write( (byte)0 );
		return stream.ToArray();
	}
	private static byte[] Image( int width, int height, Func<int, int, Color> color )
	{
		using var png = new MemoryStream(); png.Write( new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 } );
		void Chunk( string name, byte[] bytes )
		{
			Span<byte> size = stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian( size, bytes.Length ); png.Write( size ); var label = Encoding.ASCII.GetBytes( name ); png.Write( label ); png.Write( bytes );
			uint crc = 0xffffffff; foreach ( var value in label.Concat( bytes ) ) { crc ^= value; for ( var bit = 0; bit < 8; bit++ ) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0u : 0xedb88320u); }
			BinaryPrimitives.WriteUInt32BigEndian( size, ~crc ); png.Write( size );
		}
		var header = new byte[13]; BinaryPrimitives.WriteInt32BigEndian( header, width ); BinaryPrimitives.WriteInt32BigEndian( header.AsSpan( 4 ), height ); header[8] = 8; header[9] = 6; Chunk( "IHDR", header );
		using var raw = new MemoryStream();
		for ( var y = 0; y < height; y++ ) { raw.WriteByte( 0 ); for ( var x = 0; x < width; x++ ) { var c = color( x, y ); raw.WriteByte( (byte)MathF.Round( c.r * 255 ) ); raw.WriteByte( (byte)MathF.Round( c.g * 255 ) ); raw.WriteByte( (byte)MathF.Round( c.b * 255 ) ); raw.WriteByte( 255 ); } }
		using var encoded = new MemoryStream(); using ( var zlib = new ZLibStream( encoded, CompressionLevel.Optimal, leaveOpen: true ) ) zlib.Write( raw.ToArray() );
		Chunk( "IDAT", encoded.ToArray() ); Chunk( "IEND", [] ); return png.ToArray();
	}
}
