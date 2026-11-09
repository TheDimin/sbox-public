// Actual Model.Load material-sampling proof. No helper constructs the tested
// models/textures. Native stock-material overrides are labeled reference controls.
var engine = typeof( Model ).Assembly;
var checkout = new System.IO.DirectoryInfo( System.IO.Path.GetDirectoryName( engine.Location ) );
while ( checkout is not null && !System.IO.File.Exists( System.IO.Path.Combine( checkout.FullName, "engine", "Sandbox-Engine.slnx" ) ) ) checkout = checkout.Parent;
if ( checkout is null ) throw new InvalidOperationException( "Cannot locate checkout from the loaded engine." );
var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static;
var runId = Guid.NewGuid().ToString( "N" );
var output = System.IO.Path.Combine( checkout.FullName, "Temp", "gltf-validation", "editor-model-sampling", runId );
var virtualBase = "gltf-model-sampling/" + runId;
var cases = new System.Collections.Generic.List<object>();
var cleanupErrors = new System.Collections.Generic.List<string>();
var aliases = new System.Collections.Generic.List<string>();
var fixtureHashes = new System.Collections.Generic.Dictionary<string, string>();
var messages = new System.Collections.Generic.List<string>();
var jsonOptions = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
var startingFrame = Application.FrameCount;
string phase = "initialize", error = null;
Scene scene = null; Editor.SceneRenderingWidget preview = null; CameraComponent camera = null;
SceneModel obj = null; Model loaded = null; Material controlMaterial = null;
BaseFileSystem aggregate = null, mount = null; object redirectObject = null; IDisposable redirect = null;
System.Reflection.MethodInfo unmount = null, removeRedirect = null, removeLog = null;
Action<LogEvent> logHandler = null; Action onFrame = null; int frames = 0;
bool mounted = false, ownedLoaded = false;
object renderer = null; System.Reflection.MethodInfo renderToTexture = null;
string rendererMvid = null, rendererHash = null;
var captures = new System.Collections.Generic.List<object>();
void Require( bool condition, string message ) { if ( !condition ) throw new InvalidOperationException( message ); }
void Clean( string name, Action action ) { try { action(); } catch ( Exception ex ) { cleanupErrors.Add( name + ": " + ex ); } }
bool Valid( Model model ) => model is { IsValid: true } && !model.IsError && !ReferenceEquals( model, Model.Error );
string Hash( byte[] bytes ) => Convert.ToHexStringLower( System.Security.Cryptography.SHA256.HashData( bytes ) );
string FileHash( string path ) => Hash( System.IO.File.ReadAllBytes( path ) );
double Linear( double encoded ) => encoded <= .04045 ? encoded / 12.92 : Math.Pow( (encoded + .055) / 1.055, 2.4 );
float Encoded( double linear ) => (float)(linear <= .0031308 ? linear * 12.92 : 1.055 * Math.Pow( linear, 1 / 2.4 ) - .055);
async Task SafePoint()
{
	await Task.Delay( 1, cancellationToken ); await MainThread.Wait();
	Require( Application.IsEditor && !Game.IsPlaying, "Sampling proof deferred while the user is playing." );
	var timer = System.Diagnostics.Stopwatch.StartNew();
	while ( Graphics.IsActive )
	{
		Require( timer.Elapsed < TimeSpan.FromSeconds( 5 ), "No safe main-thread graphics boundary." );
		await Task.Delay( 20, cancellationToken ); await MainThread.Wait();
		Require( !Game.IsPlaying, "User entered play mode; sampling proof deferred." );
	}
}
void Be32( System.IO.Stream stream, uint value )
{
	Span<byte> bytes = stackalloc byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian( bytes, value ); stream.Write( bytes );
}
uint Crc( byte[] bytes )
{
	uint value = 0xffffffff;
	foreach ( var b in bytes )
	{
		value ^= b;
		for ( var bit = 0; bit < 8; bit++ ) value = (value >> 1) ^ ((value & 1) != 0 ? 0xedb88320u : 0);
	}
	return value ^ 0xffffffff;
}
byte[] Png( byte[] rgba )
{
	Require( rgba.Length == 8, "Fixture PNG is exactly2x1RGBA8." );
	using var outputPng = new System.IO.MemoryStream(); outputPng.Write( new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 } );
	void Chunk( string name, byte[] data )
	{
		Be32( outputPng, (uint)data.Length ); var type = System.Text.Encoding.ASCII.GetBytes( name ); outputPng.Write( type ); outputPng.Write( data );
		Be32( outputPng, Crc( type.Concat( data ).ToArray() ) );
	}
	using var header = new System.IO.MemoryStream(); Be32( header, 2 ); Be32( header, 1 ); header.Write( new byte[] { 8, 6, 0, 0, 0 } ); Chunk( "IHDR", header.ToArray() );
	using var compressed = new System.IO.MemoryStream();
	using ( var zlib = new System.IO.Compression.ZLibStream( compressed, System.IO.Compression.CompressionLevel.SmallestSize, leaveOpen: true ) ) { zlib.WriteByte( 0 ); zlib.Write( rgba ); }
	Chunk( "IDAT", compressed.ToArray() ); Chunk( "IEND", Array.Empty<byte>() ); return outputPng.ToArray();
}
byte[] Geometry( float u, bool minify )
{
	using var bytes = new System.IO.MemoryStream(); using var writer = new System.IO.BinaryWriter( bytes );
	// glTF XY quad facing+Z maps to the engine YZ plane facing+X.
	foreach ( var p in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) } ) { writer.Write( p.Item1 ); writer.Write( p.Item2 ); writer.Write( 0f ); }
	for ( var i = 0; i < 4; i++ ) { writer.Write( 0f ); writer.Write( 0f ); writer.Write( 1f ); }
	for ( var i = 0; i < 4; i++ ) { writer.Write( minify ? (i == 1 || i == 2 ? 2048f : 0f) : u ); writer.Write( .5f ); }
	foreach ( ushort index in new ushort[] { 0, 1, 2, 0, 2, 3 } ) writer.Write( index );
	return bytes.ToArray(); // positions48 +normals48 +UV32 +indices12 =140bytes.
}
System.Text.Json.Nodes.JsonObject Description( int wrap, int mag, int min, double factor, int geometryBytes, int pngBytes, bool glb )
{
	var json = System.Text.Json.Nodes.JsonNode.Parse( """
		{"asset":{"version":"2.0","generator":"s&box independent material-sampling fixture"},"buffers":[{"byteLength":0}],"bufferViews":[{"buffer":0,"byteOffset":0,"byteLength":48},{"buffer":0,"byteOffset":48,"byteLength":48},{"buffer":0,"byteOffset":96,"byteLength":32},{"buffer":0,"byteOffset":128,"byteLength":12}],"accessors":[{"bufferView":0,"componentType":5126,"count":4,"type":"VEC3","min":[-1,-1,0],"max":[1,1,0]},{"bufferView":1,"componentType":5126,"count":4,"type":"VEC3"},{"bufferView":2,"componentType":5126,"count":4,"type":"VEC2"},{"bufferView":3,"componentType":5123,"count":6,"type":"SCALAR"}],"images":[{}],"samplers":[{"wrapS":10497,"wrapT":10497,"magFilter":9728,"minFilter":9728}],"textures":[{"source":0,"sampler":0}],"materials":[{"doubleSided":true,"pbrMetallicRoughness":{"baseColorFactor":[1,1,1,1],"baseColorTexture":{"index":0},"metallicFactor":0,"roughnessFactor":1}}],"meshes":[{"primitives":[{"attributes":{"POSITION":0,"NORMAL":1,"TEXCOORD_0":2},"indices":3,"material":0}]}],"nodes":[{"mesh":0}],"scenes":[{"nodes":[0]}],"scene":0}
		""" ).AsObject();
	json["buffers"][0]["byteLength"] = geometryBytes + (glb ? pngBytes : 0);
	if ( glb )
	{
		json["bufferViews"].AsArray().Add( new System.Text.Json.Nodes.JsonObject { ["buffer"] = 0, ["byteOffset"] = geometryBytes, ["byteLength"] = pngBytes } );
		json["images"][0] = new System.Text.Json.Nodes.JsonObject { ["bufferView"] = 4, ["mimeType"] = "image/png" };
	}
	else { json["buffers"][0]["uri"] = "geometry.bin"; json["images"][0]["uri"] = "image.png"; }
	json["samplers"][0]["wrapS"] = wrap; json["samplers"][0]["magFilter"] = mag; json["samplers"][0]["minFilter"] = min;
	json["materials"][0]["pbrMetallicRoughness"]["baseColorFactor"] = new System.Text.Json.Nodes.JsonArray( factor, factor, factor, 1 );
	return json;
}
byte[] Glb( byte[] json, byte[] geometry, byte[] png )
{
	using var outputGlb = new System.IO.MemoryStream(); using var writer = new System.IO.BinaryWriter( outputGlb );
	var jsonLength = (json.Length + 3) & ~3; var binLength = (geometry.Length + png.Length + 3) & ~3;
	writer.Write( 0x46546c67u ); writer.Write( 2u ); writer.Write( 12 + 8 + jsonLength + 8 + binLength );
	writer.Write( jsonLength ); writer.Write( 0x4e4f534au ); writer.Write( json ); for ( var i = json.Length; i < jsonLength; i++ ) writer.Write( (byte)32 );
	writer.Write( binLength ); writer.Write( 0x004e4942u ); writer.Write( geometry ); writer.Write( png );
	for ( var i = geometry.Length + png.Length; i < binLength; i++ ) writer.Write( (byte)0 ); return outputGlb.ToArray();
}
void Put( string relative, byte[] bytes )
{
	var path = System.IO.Path.Combine( output, "input", relative.Replace( '/', System.IO.Path.DirectorySeparatorChar ) );
	System.IO.Directory.CreateDirectory( System.IO.Path.GetDirectoryName( path ) ); System.IO.File.WriteAllBytes( path, bytes );
	var alias = "/" + virtualBase + "/" + relative;
	redirectObject.GetType().GetMethod( "AddAbsFile", flags ).Invoke( redirectObject, new object[] { alias, path } ); aliases.Add( alias ); fixtureHashes[relative] = Hash( bytes );
}
async Task WaitFrames()
{
	var before = frames; var timer = System.Diagnostics.Stopwatch.StartNew();
	while ( frames < before + 3 )
	{
		Require( timer.Elapsed < TimeSpan.FromSeconds( 8 ), "Private sampling preview stopped receiving frames." );
		await Task.Delay( 30, cancellationToken ); await SafePoint();
	}
}
async Task<Color[]> Capture( string label )
{
	await WaitFrames();
	using var target = Texture.CreateRenderTarget().WithSize( 192, 192 ).WithFormat( ImageFormat.RGBA8888 ).WithInitialColor( Color.Magenta ).Create( "gltf_sampling_" + Guid.NewGuid().ToString( "N" ) );
	using ( scene.Push() )
	{
		target.Clear( Color.Magenta );
		Require( (bool)renderToTexture.Invoke( renderer, new object[] { camera, target } ), "Managed renderer rejected capture: " + label );
	}
	await WaitFrames(); await SafePoint();
	using var bitmap = target.GetBitmap(); Require( bitmap is not null, "Capture readback failed: " + label );
	var png = bitmap.ToPng(); var path = System.IO.Path.Combine( output, label + ".png" ); System.IO.File.WriteAllBytes( path, png );
	captures.Add( new { label, path, sha256 = Hash( png ), fresh_target = true, width = 192, height = 192, frames, engine_frame = Application.FrameCount } );
	return bitmap.GetPixels();
}
Color[] Center( Color[] pixels )
{
	Require( pixels.Length == 192 * 192, "Unexpected capture dimensions." );
	var center = new System.Collections.Generic.List<Color>();
	for ( var y = 88; y < 104; y++ ) for ( var x = 88; x < 104; x++ ) center.Add( pixels[y * 192 + x] );
	return center.ToArray();
}
bool Black( Color pixel ) => Math.Abs( pixel.r ) < .01 && Math.Abs( pixel.g ) < .01 && Math.Abs( pixel.b ) < .01;
float[] Average( Color[] pixels ) => new[] { pixels.Average( p => p.r ), pixels.Average( p => p.g ), pixels.Average( p => p.b ) };
void DestroyLoaded()
{
	Exception first = null;
	try { if ( obj.IsValid() ) obj.Delete(); } catch ( Exception ex ) { first = ex; } finally { obj = null; }
	try { if ( ownedLoaded && loaded is not null ) typeof( Model ).GetMethod( "Destroy", flags ).Invoke( loaded, null ); }
	catch ( Exception ex ) { first ??= ex; }
	finally { loaded = null; ownedLoaded = false; }
	if ( first is not null ) throw first;
}
try
{
	await SafePoint(); System.IO.Directory.CreateDirectory( output ); phase = "verify-renderer";
	var rendererAssembly = AppDomain.CurrentDomain.GetAssemblies().Single( a => !a.IsDynamic && a.GetName().Name == "Sandbox.SceneRenderer" && System.Runtime.Loader.AssemblyLoadContext.GetLoadContext( a ) == System.Runtime.Loader.AssemblyLoadContext.Default );
	rendererMvid = rendererAssembly.ManifestModule.ModuleVersionId.ToString(); rendererHash = FileHash( rendererAssembly.Location );
	var rendererType = engine.GetType( "Sandbox.Rendering.ManagedSceneRendering", true ); renderer = rendererType.GetProperty( "Renderer", flags ).GetValue( null );
	Require( renderer is not null && renderer.GetType().Assembly == rendererAssembly, "Default-context managed renderer is unavailable." );
	renderToTexture = renderer.GetType().GetMethod( "RenderToTexture", flags, null, new[] { typeof( CameraComponent ), typeof( Texture ) }, null ); Require( renderToTexture is not null, "Managed camera capture API is unavailable." );
	phase = "private-mount";
	var redirectType = engine.GetType( "Sandbox.Internal.RedirectFileSystem", true ); redirectObject = redirectType.GetMethod( "Create", flags ).Invoke( null, new object[] { output } ); redirect = (IDisposable)redirectObject;
	removeRedirect = redirectType.GetMethod( "RemoveAbsFile", flags );
	var ctor = typeof( BaseFileSystem ).GetConstructors( flags ).Single( c => { var p = c.GetParameters(); return p.Length == 1 && p[0].ParameterType.IsInstanceOfType( redirectObject ); } );
	mount = (BaseFileSystem)ctor.Invoke( new[] { redirectObject } ); aggregate = Sandbox.FileSystem.Mounted; Require( aggregate is not null, "Mounted filesystem unavailable." );
	unmount = typeof( BaseFileSystem ).GetMethod( "UnMount", flags, null, new[] { typeof( BaseFileSystem ) }, null );
	typeof( BaseFileSystem ).GetMethod( "Mount", flags, null, new[] { typeof( BaseFileSystem ) }, null ).Invoke( aggregate, new object[] { mount } ); mounted = true;
	var logging = typeof( LogEvent ).Assembly.GetType( "Sandbox.Diagnostics.Logging", true ); var logEvent = logging.GetEvent( "OnMessage", flags );
	logHandler = entry => { var text = (entry.Message ?? "") + (entry.Exception is null ? "" : " " + entry.Exception.Message); if ( !text.Contains( virtualBase, StringComparison.OrdinalIgnoreCase ) ) return; lock ( messages ) if ( messages.Count < 128 ) messages.Add( entry.Level + ": " + text[..Math.Min( text.Length, 2048 )] ); };
	logEvent.GetAddMethod( true ).Invoke( null, new object[] { logHandler } ); removeLog = logEvent.GetRemoveMethod( true );
	scene = Scene.CreateEditorScene();
	using ( scene.Push() )
	{
		camera = scene.CreateObject().Components.Create<CameraComponent>(); camera.WorldPosition = new Vector3( 100, 0, 0 ); camera.WorldRotation = Rotation.LookAt( new Vector3( -1, 0, 0 ) );
		camera.Orthographic = true; camera.OrthographicHeight = 100; camera.ZNear = .1f; camera.ZFar = 1000;
		camera.BackgroundColor = Color.Black; camera.EnablePostProcessing = false; camera.DebugMode = SceneCameraDebugMode.Albedo;
	}
	preview = new Editor.SceneRenderingWidget { Scene = scene, Camera = camera, WindowTitle = "glTF Model.Load sampling proof", Size = new Vector2( 384, 384 ) }; onFrame = () => frames++; preview.OnPreFrame += onFrame; preview.Show();
	var stock = Material.Load( "materials/default/white.vmat" ); Require( stock is { IsValid: true } && !stock.IsError, "Native stock control material unavailable." );
	var copiedControl = stock.CreateCopy( "gltf_sampling_control_" + runId ); Require( copiedControl is { IsValid: true } && !copiedControl.IsError, "Native control copy failed." ); controlMaterial = copiedControl;
	controlMaterial.SetFeature( "F_RENDER_BACKFACES", 1 ); controlMaterial.SetFeature( "F_METALNESS_TEXTURE", 0 ); controlMaterial.SetFeature( "F_ALPHA_TEST", 0 ); controlMaterial.SetFeature( "F_TRANSLUCENT", 0 );
	controlMaterial.Set( "g_flMetalness", 0f ); controlMaterial.Set( "g_flRoughnessScaleFactor", 0f );
	phase = "empty-control"; Require( (await Capture( "empty_before" )).All( Black ), "Initial fresh-target empty control failed." );
	var definitions = new[]
	{
		new { name = "repeat_1_25_nearest", wrap = 10497, mag = 9728, min = 9728, u = 1.25f, factor = 1.0, gray = false, minify = false, expected = new[] { 1.0, 0.0, 0.0 }, feature = "wrapS REPEAT" },
		new { name = "clamp_1_25_nearest", wrap = 33071, mag = 9728, min = 9728, u = 1.25f, factor = 1.0, gray = false, minify = false, expected = new[] { 0.0, 0.0, 1.0 }, feature = "wrapS CLAMP_TO_EDGE" },
		new { name = "mirror_1_25_nearest", wrap = 33648, mag = 9728, min = 9728, u = 1.25f, factor = 1.0, gray = false, minify = false, expected = new[] { 0.0, 0.0, 1.0 }, feature = "wrapS MIRRORED_REPEAT" },
		new { name = "repeat_1_75_nearest", wrap = 10497, mag = 9728, min = 9728, u = 1.75f, factor = 1.0, gray = false, minify = false, expected = new[] { 0.0, 0.0, 1.0 }, feature = "wrapS REPEAT" },
		new { name = "mirror_1_75_nearest", wrap = 33648, mag = 9728, min = 9728, u = 1.75f, factor = 1.0, gray = false, minify = false, expected = new[] { 1.0, 0.0, 0.0 }, feature = "wrapS MIRRORED_REPEAT" },
		new { name = "nearest_0_4", wrap = 10497, mag = 9728, min = 9728, u = .4f, factor = 1.0, gray = false, minify = false, expected = new[] { 1.0, 0.0, 0.0 }, feature = "magFilter NEAREST" },
		new { name = "linear_0_4", wrap = 10497, mag = 9729, min = 9729, u = .4f, factor = 1.0, gray = false, minify = false, expected = new[] { .7, 0.0, .3 }, feature = "magFilter LINEAR" },
		new { name = "srgb_gray128", wrap = 10497, mag = 9728, min = 9728, u = .25f, factor = 1.0, gray = true, minify = false, expected = new[] { Linear( 128.0 / 255 ), Linear( 128.0 / 255 ), Linear( 128.0 / 255 ) }, feature = "baseColor sRGB decode" },
		new { name = "srgb_gray128_factor_half", wrap = 10497, mag = 9728, min = 9728, u = .25f, factor = .5, gray = true, minify = false, expected = new[] { Linear( 128.0 / 255 ) * .5, Linear( 128.0 / 255 ) * .5, Linear( 128.0 / 255 ) * .5 }, feature = "baseColor factor multiplication in linear space" },
		new { name = "minified_nearest_mip", wrap = 10497, mag = 9728, min = 9984, u = 0f, factor = 1.0, gray = false, minify = true, expected = new[] { .5, 0.0, .5 }, feature = "minFilter NEAREST_MIPMAP_NEAREST" }
	};
	foreach ( var definition in definitions ) foreach ( var form in new[] { "gltf", "glb" } )
	{
		await SafePoint(); phase = definition.name + "_" + form;
		var row = new System.Collections.Generic.Dictionary<string, object> { ["name"] = definition.name, ["form"] = form, ["feature"] = definition.feature, ["passed"] = false, ["load"] = "not_run", ["numeric_appearance"] = "not_run", ["expected_linear_rgb"] = definition.expected, ["wrapS"] = definition.wrap, ["magFilter"] = definition.mag, ["minFilter"] = definition.min, ["u"] = definition.u, ["minify"] = definition.minify };
		try
		{
			var geometry = Geometry( definition.u, definition.minify ); var png = Png( definition.gray ? new byte[] { 128, 128, 128, 255, 128, 128, 128, 255 } : new byte[] { 255, 0, 0, 255, 0, 0, 255, 255 } );
			var glb = form == "glb"; var json = System.Text.Encoding.UTF8.GetBytes( Description( definition.wrap, definition.mag, definition.min, definition.factor, geometry.Length, png.Length, glb ).ToJsonString() );
			var relative = definition.name + "/" + form + "/model." + form;
			if ( glb ) Put( relative, Glb( json, geometry, png ) );
			else { Put( relative, json ); Put( definition.name + "/" + form + "/geometry.bin", geometry ); Put( definition.name + "/" + form + "/image.png", png ); }
			NativeInvalidate();
			var assetPath = virtualBase + "/" + relative; row["asset_path"] = assetPath;
			loaded = Model.Load( assetPath ); ownedLoaded = Valid( loaded ) && string.Equals( loaded.ResourcePath, assetPath, StringComparison.OrdinalIgnoreCase );
			row["load"] = Valid( loaded ) ? "passed" : "failed"; Require( Valid( loaded ), "Actual Model.Load returned null/error/invalid." ); Require( ownedLoaded, "Loaded model does not have the unique task-owned identity." );
			obj = new SceneModel( scene.SceneWorld, loaded, Transform.Zero ); var actual = Center( await Capture( definition.name + "_" + form + "_actual" ) );
			Require( actual.Any( p => !Black( p ) ), "Actual model has no center visibility." );
			var expected = definition.expected;
			controlMaterial.Set( "g_vColorTint", new Vector4( Encoded( expected[0] ), Encoded( expected[1] ), Encoded( expected[2] ), 1 ) );
			obj.SetMaterialOverride( controlMaterial );
			var control = Center( await Capture( definition.name + "_" + form + "_native_control" ) ); Require( control.Any( p => !Black( p ) ), "Matched native stock control has no visibility." );
			var maxError = actual.Zip( control, (a, b) => MathF.Max( MathF.Abs( a.r - b.r ), MathF.Max( MathF.Abs( a.g - b.g ), MathF.Abs( a.b - b.b ) ) ) ).Max();
			row["actual_rgb_mean"] = Average( actual ); row["control_rgb_mean"] = Average( control ); row["max_rgb_error"] = maxError; row["tolerance"] = 4f / 255;
			row["control"] = "Same loaded geometry; explicit copied native stock material with independently calculated sRGB tint. No glTF material adapter used for the control material.";
			row["numeric_appearance"] = maxError <= 4f / 255 ? "passed" : "failed";
			Require( maxError <= 4f / 255, "Actual texture sample differs from the independent stock-color control." );
			row["passed"] = true;
		}
		catch ( Exception ex ) when ( ex is not OutOfMemoryException && ex is not OperationCanceledException ) { row["error"] = ex.ToString(); }
		finally
		{
			await MainThread.Wait(); Clean( "owned case model", DestroyLoaded );
		}
		Require( cleanupErrors.Count == 0, "Sampling model cleanup failed; stop allocating." );
		var empty = await Capture( definition.name + "_" + form + "_empty_after" ); var emptyPassed = empty.All( Black ); row["empty_after"] = emptyPassed ? "passed" : "failed";
		if ( !emptyPassed ) row["passed"] = false;
		cases.Add( row );
		System.IO.File.WriteAllText( System.IO.Path.Combine( output, "progress.json" ), System.Text.Json.JsonSerializer.Serialize( cases, jsonOptions ) );
		Require( emptyPassed, "Fresh-target empty control failed after deleting the tested model." );
	}
	phase = "complete";
}
catch ( Exception ex ) { error = ex.ToString(); }
finally
{
	await MainThread.Wait();
	Clean( "remaining owned model", DestroyLoaded );
	if ( preview is not null && preview.IsValid && onFrame is not null ) Clean( "preview callback", () => preview.OnPreFrame -= onFrame );
	if ( preview is not null && preview.IsValid ) Clean( "private preview", () => { preview.Close(); preview.Destroy(); } );
	if ( scene is not null ) Clean( "private scene", () => scene.Destroy() );
	if ( controlMaterial is not null ) Clean( "native control material", () => typeof( Material ).GetMethod( "Destroy", flags ).Invoke( controlMaterial, null ) );
	if ( mounted ) Clean( "private unmount", () => unmount.Invoke( aggregate, new object[] { mount } ) );
	if ( redirectObject is not null ) foreach ( var alias in aliases ) Clean( "private redirect", () => removeRedirect.Invoke( redirectObject, new object[] { alias } ) );
	if ( redirect is not null ) Clean( "private redirect filesystem", () => redirect.Dispose() );
	if ( removeLog is not null && logHandler is not null ) Clean( "log callback", () => removeLog.Invoke( null, new object[] { logHandler } ) );
}
void NativeInvalidate() => engine.GetType( "NativeEngine.g_pResourceSystem", true ).GetMethod( "InvalidateDatabase", flags ).Invoke( null, null );
var receipt = new
{
	passed = error is null && phase == "complete" && cleanupErrors.Count == 0 && cases.Count == 20 && cases.All( c => (bool)((System.Collections.Generic.Dictionary<string, object>)c)["passed"] ),
	phase, error, expected_cases = 20, cases, captures, cleanup_errors = cleanupErrors, fixture_sha256 = fixtureHashes, relevant_messages = messages.ToArray(),
	scope = "Actual Model.Load source glTF and independent hand-packed GLB; stock shader color transfer, factor multiplication, U wrapping, magnification filtering and one forced-minification mip case. No claim for V wrapping, complete sampling spec, shader appearance parity, source compiler optimization or unrelated materials.",
	fixture_provenance = "Self-generated CC0 diagnostic2x1PNG RGBA8 and quad; PNG/GLB encoded by this script independently of glTF/texture compiler under test.",
	sampling_oracle = "Analytic glTF normalized texel centers/linear interpolation plus IEC sRGB transfer; rendered comparison against same loaded geometry with explicit native stock-color control.",
	engine_mvid = engine.ManifestModule.ModuleVersionId.ToString(), engine_sha256 = FileHash( engine.Location ),
	gltf_mvid = typeof( Sandbox.Gltf.GltfReader ).Assembly.ManifestModule.ModuleVersionId.ToString(), gltf_sha256 = FileHash( typeof( Sandbox.Gltf.GltfReader ).Assembly.Location ),
	renderer_mvid = rendererMvid, renderer_sha256 = rendererHash,
	source_sha256 = FileHash( System.IO.Path.Combine( checkout.FullName, "engine", "Tools", "Gltf", "editor", "editor_model_sampling.csx" ) ),
	is_editor = Application.IsEditor, starting_frame = startingFrame, final_frame = Application.FrameCount, output
};
System.IO.File.WriteAllText( System.IO.Path.Combine( output, "receipt.json" ), System.Text.Json.JsonSerializer.Serialize( receipt, jsonOptions ) );
return receipt;
