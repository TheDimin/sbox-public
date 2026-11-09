// Run only inside an existing editor. The default selects all existing acceptance
// cases; SBOX_GLTF_MODEL_CASE still selects one explicitly. An optional same-session
// gltf-model-request.json selects existing case/render controls without changing
// their behavior. No user scene or project asset is modified.
var engineLocation = typeof( Model ).Assembly.Location;
var root = new System.IO.DirectoryInfo( System.IO.Path.GetDirectoryName( engineLocation ) );
while ( root is not null && !System.IO.File.Exists( System.IO.Path.Combine( root.FullName, "engine", "Sandbox-Engine.slnx" ) ) ) root = root.Parent;
if ( root is null ) throw new InvalidOperationException( "Cannot find checkout from loaded engine: " + engineLocation );
var selection = Environment.GetEnvironmentVariable( "SBOX_GLTF_MODEL_CASE" );
var selectionSource = string.IsNullOrWhiteSpace( selection ) ? "default" : "environment";
if ( selectionSource == "default" ) selection = "all";
var allowed = new[] { "source-gltf", "khronos-triangle", "khronos-triangle-embedded", "source-glb", "compiled-reference", "compiled-glb-c", "multi-scene", "nonwhite-color", "two-color-scene", "optional-fallback", "unsupported-required", "missing-dependency", "resolver", "unsupported-color", "unsupported-animation", "all" };
var renderRequested = Environment.GetEnvironmentVariable( "SBOX_GLTF_MODEL_RENDER" ) == "1";
var managedCapture = Environment.GetEnvironmentVariable( "SBOX_GLTF_CAPTURE_RENDERER" ) == "managed";
var diagnosticClear = Environment.GetEnvironmentVariable( "SBOX_GLTF_CAMERA_CLEAR" ) == "1";
var runId = Guid.NewGuid().ToString( "N" );
var virtualBase = "gltf-model-load/" + runId;
var outputGroup = "editor-model-load" + (renderRequested ? "-render" : "") + (renderRequested && diagnosticClear ? "-camera-clear" : "") + (renderRequested && managedCapture ? "-managed" : "");
var output = System.IO.Path.Combine( root.FullName, "Temp", "gltf-validation", outputGroup, runId );
var receiptPath = System.IO.Path.Combine( output, "receipt.json" );
var runnerPath = System.IO.Path.Combine( root.FullName, "engine", "Tools", "Gltf", "editor", "editor_model_load.csx" );
var requestPath = System.IO.Path.Combine( exchangeDirectory, "gltf-model-request.json" );
string requestHash = null, requestId = null, runnerHash = null;
System.Text.Json.JsonElement? requestValues = null;
var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static;
var cases = new System.Collections.Generic.List<object>();
var messages = new System.Collections.Generic.List<string>();
var cleanupErrors = new System.Collections.Generic.List<string>();
var redirects = new System.Collections.Generic.List<string>();
var ownedModels = new System.Collections.Generic.HashSet<Model>();
var fixtureHashes = new System.Collections.Generic.Dictionary<string, string>();
var renderCases = new System.Collections.Generic.List<object>();
var captures = new System.Collections.Generic.List<object>();
var renderReferences = new System.Collections.Generic.List<Model>();
var renderObjects = new System.Collections.Generic.List<SceneModel>();
var phase = "initialize";
var startFrame = Application.FrameCount;
var loadCalls = 0;
string error = null;
bool sphereValid = false;
object redirectObject = null;
IDisposable redirect = null;
BaseFileSystem managedMount = null;
BaseFileSystem mountedAggregate = null;
System.Reflection.MethodInfo unmountManaged = null;
bool mountedManaged = false;
System.Reflection.MethodInfo removeRedirect = null;
System.Reflection.MethodInfo removeLogHandler = null;
Action<LogEvent> logHandler = null;
Scene renderScene = null;
Editor.SceneRenderingWidget renderPreview = null;
Texture renderTarget = null;
Material renderMaterial = null;
Material renderMaterialSecondary = null;
ScenePointLight renderLight = null;
CameraComponent renderCamera = null;
Sandbox.Rendering.CommandList renderClear = null;
Action onRenderFrame = null;
int renderFrames = 0;
int completedRenderCases = 0;
bool? managedViewportEnabled = null;
object managedRenderer = null;
System.Reflection.MethodInfo managedRenderToTexture = null;
string missingName = "missing_" + runId + ".bin";
const string unsupportedName = "VENDOR_unimplemented_model_loader_probe";
string Hash( byte[] bytes ) => Convert.ToHexStringLower( System.Security.Cryptography.SHA256.HashData( bytes ) );
void Require( bool condition, string message ) { if ( !condition ) throw new InvalidOperationException( message ); }
// Independent IEC 61966-2-1 transfer oracle; the stock shader decodes its tint.
float ExpectedStockTint( float linear ) => (float)(linear <= .0031308 ? linear * 12.92 : 1.055 * Math.Pow( linear, 1.0 / 2.4 ) - .055);
byte[] ReadBounded( string path, long maximum = 1024 * 1024, bool optional = false )
{
	System.IO.FileStream stream;
	try { stream = System.IO.File.Open( path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read ); }
	catch ( System.IO.FileNotFoundException ) when ( optional ) { return null; }
	catch ( System.IO.DirectoryNotFoundException ) when ( optional ) { return null; }
	using ( stream )
	{
		var length = stream.Length;
		Require( length > 0 && length <= maximum && length <= int.MaxValue, "Empty or oversized input: " + path );
		var bytes = new byte[(int)length];
		stream.ReadExactly( bytes );
		Require( stream.ReadByte() == -1, "Input grew while being read: " + path );
		return bytes;
	}
}
bool IsLowerHex( string value, int length ) => value is not null && value.Length == length && value.All( c => c >= '0' && c <= '9' || c >= 'a' && c <= 'f' );
void Cleanup( string name, Action action ) { try { action(); } catch ( Exception e ) { cleanupErrors.Add( name + ": " + e ); } }
bool Valid( Model value ) => value is { IsValid: true } && !value.IsError && !ReferenceEquals( value, Model.Error );
bool Near( float actual, float expected ) => MathF.Abs( actual - expected ) <= .2f;
bool PixelDifferent( Color a, Color b ) => MathF.Abs( a.r - b.r ) > .03f || MathF.Abs( a.g - b.g ) > .03f || MathF.Abs( a.b - b.b ) > .03f;
int PixelDifference( Color[] a, Color[] b ) => a.Zip( b, ( x, y ) => PixelDifferent( x, y ) ).Count( x => x );
int Nonblack( Color[] pixels ) => pixels.Count( x => PixelDifferent( x, Color.Black ) );
void CleanupRender()
{
	if ( renderPreview is not null && renderPreview.IsValid && onRenderFrame is not null ) Cleanup( "preview callback", () => renderPreview.OnPreFrame -= onRenderFrame );
	if ( renderCamera.IsValid() && renderClear is not null ) Cleanup( "camera clear", () => renderCamera.RemoveCommandList( renderClear ) );
	if ( renderPreview is not null && renderPreview.IsValid ) Cleanup( "preview", () => { renderPreview.Close(); renderPreview.Destroy(); } );
	foreach ( var obj in renderObjects ) if ( obj.IsValid() ) Cleanup( "render object", () => obj.Delete() );
	if ( renderLight.IsValid() ) Cleanup( "render light", () => renderLight.Delete() );
	if ( renderScene is not null ) Cleanup( "render scene", () => renderScene.Destroy() );
	if ( renderTarget is not null ) Cleanup( "render target", () => renderTarget.Dispose() );
	var destroyModel = typeof( Model ).GetMethod( "Destroy", flags );
	foreach ( var model in renderReferences ) if ( Valid( model ) ) Cleanup( "reference model", () => destroyModel.Invoke( model, null ) );
	if ( renderMaterial is not null && renderMaterial.IsValid ) Cleanup( "reference material", () => typeof( Material ).GetMethod( "Destroy", flags ).Invoke( renderMaterial, null ) );
	if ( renderMaterialSecondary is not null && renderMaterialSecondary.IsValid ) Cleanup( "secondary reference material", () => typeof( Material ).GetMethod( "Destroy", flags ).Invoke( renderMaterialSecondary, null ) );
	renderObjects.Clear(); renderReferences.Clear(); renderPreview = null; onRenderFrame = null; renderClear = null; renderCamera = null; renderLight = null; renderScene = null; renderTarget = null; renderMaterial = null; renderMaterialSecondary = null;
}

try
{
	await MainThread.Wait();
	Require( Application.IsEditor && !Graphics.IsActive, "Run inside the editor on the main thread outside a Graphics scope." );
	phase = "read-runner-source";
	runnerHash = Hash( ReadBounded( runnerPath ) );
	phase = "read-selection-request";
	byte[] requestBytes;
	try { requestBytes = ReadBounded( requestPath, maximum: 4096, optional: true ); }
	catch { selectionSource = "request"; throw; }
	if ( requestBytes is not null )
	{
		selectionSource = "request";
		requestHash = Hash( requestBytes );
		using var request = System.Text.Json.JsonDocument.Parse( new System.Text.UTF8Encoding( false, true ).GetString( requestBytes ), new System.Text.Json.JsonDocumentOptions { MaxDepth = 4 } );
		var value = request.RootElement;
		requestValues = value.Clone();
		Require( value.ValueKind == System.Text.Json.JsonValueKind.Object, "Model request must be a JSON object." );
		var fields = new System.Collections.Generic.HashSet<string>( StringComparer.Ordinal );
		foreach ( var field in value.EnumerateObject() ) Require( fields.Add( field.Name ), "Duplicate model request property: " + field.Name );
		Require( fields.SetEquals( new[] { "version", "request_id", "selection", "render", "capture_renderer", "camera_clear" } ), "Model request requires exactly version, request_id, selection, render, capture_renderer and camera_clear." );
		Require( value.GetProperty( "version" ).ValueKind == System.Text.Json.JsonValueKind.Number && value.GetProperty( "version" ).TryGetInt32( out var version ) && version == 1, "Expected model request version 1." );
		Require( new[] { "request_id", "selection", "capture_renderer" }.All( name => value.GetProperty( name ).ValueKind == System.Text.Json.JsonValueKind.String ), "Model request id, selection and capture renderer must be strings." );
		Require( new[] { "render", "camera_clear" }.All( name => value.GetProperty( name ).ValueKind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False ), "Model request render and camera_clear must be booleans." );
		requestId = value.GetProperty( "request_id" ).GetString();
		var requestedSelection = value.GetProperty( "selection" ).GetString();
		var requestedRenderer = value.GetProperty( "capture_renderer" ).GetString();
		Require( IsLowerHex( requestId, 32 ), "Model request_id must contain 32 lowercase hexadecimal characters." );
		Require( allowed.Contains( requestedSelection, StringComparer.Ordinal ), "Unknown model request selection: " + requestedSelection );
		Require( requestedRenderer is "native" or "managed", "Model request capture_renderer must be native or managed." );
		selection = requestedSelection;
		renderRequested = value.GetProperty( "render" ).GetBoolean();
		managedCapture = requestedRenderer == "managed";
		diagnosticClear = value.GetProperty( "camera_clear" ).GetBoolean();
	}
	Require( allowed.Contains( selection, StringComparer.Ordinal ), "Unknown SBOX_GLTF_MODEL_CASE: " + selection );
	outputGroup = "editor-model-load" + (renderRequested ? "-render" : "") + (renderRequested && diagnosticClear ? "-camera-clear" : "") + (renderRequested && managedCapture ? "-managed" : "");
	output = System.IO.Path.Combine( root.FullName, "Temp", "gltf-validation", outputGroup, runId );
	receiptPath = System.IO.Path.Combine( output, "receipt.json" );
	System.IO.Directory.CreateDirectory( output );
	sphereValid = Valid( Model.Sphere );
	Require( sphereValid, "Existing vmdl sphere is unavailable before glTF loading." );
	phase = "prepare-fixtures";
	var corpusRoot = Environment.GetEnvironmentVariable( "SBOX_GLTF_CORPUS" );
	if ( string.IsNullOrWhiteSpace( corpusRoot ) ) corpusRoot = System.IO.Path.Combine( root.FullName, "Temp", "gltf-reference", "glTF-Sample-Assets" );
	var directory = System.IO.Path.Combine( corpusRoot, "Models", "Triangle", "glTF" );
	var original = System.IO.File.ReadAllBytes( System.IO.Path.Combine( directory, "Triangle.gltf" ) );
	var buffer = System.IO.File.ReadAllBytes( System.IO.Path.Combine( directory, "Triangle.bin" ) );
	Require( Hash( original ) == "6f4a8a1fbefbac2d0adde0800864279fd98c3f119077380e6840810ed62b6cfc" && Hash( buffer ) == "898ed7f560dca9907a80c98be57fbbc0f3a092d2e3683b964ffcfa75c549ac62", "Pinned Khronos Triangle files differ." );
	var triangleNode = System.Text.Json.Nodes.JsonNode.Parse( original );
	triangleNode["materials"] = System.Text.Json.Nodes.JsonNode.Parse( """[{}]""" );
	triangleNode["meshes"][0]["primitives"][0]["material"] = 0;
	var triangle = System.Text.Encoding.UTF8.GetBytes( triangleNode.ToJsonString() );
	var colored = System.Text.Json.Nodes.JsonNode.Parse( triangle );
	colored["materials"][0]["pbrMetallicRoughness"] = System.Text.Json.Nodes.JsonNode.Parse( """{"baseColorFactor":[0.2,0.7,0.3,1]}""" );
	var coloredBytes = System.Text.Encoding.UTF8.GetBytes( colored.ToJsonString() );
	byte[] Resolve( string uri ) => uri == "Triangle.bin" ? buffer : throw new InvalidOperationException( "Unexpected Triangle dependency " + uri );
	var glb = Sandbox.Gltf.GltfCompiler.Compile( triangle, Resolve );
	// This is only a valid portable GLB packaging fixture. It is not evidence of
	// GPU layout optimization or a production build pipeline.
	const string multiJson = """{"asset":{"version":"2.0"},"buffers":[{"uri":"data:application/octet-stream;base64,MzMzv5qZmb4AAAAAzcxMvpqZmb4AAAAAZmbmvpqZmT4AAAAAzcxMPpqZmb4AAAAAMzMzP5qZmb4AAAAAZmbmPpqZmT4AAAAA","byteLength":72}],"bufferViews":[{"buffer":0,"byteOffset":0,"byteLength":36},{"buffer":0,"byteOffset":36,"byteLength":36}],"accessors":[{"bufferView":0,"componentType":5126,"count":3,"type":"VEC3","min":[-0.7,-0.3,0],"max":[-0.2,0.3,0]},{"bufferView":1,"componentType":5126,"count":3,"type":"VEC3","min":[0.2,-0.3,0],"max":[0.7,0.3,0]}],"materials":[{},{}],"meshes":[{"primitives":[{"attributes":{"POSITION":0},"material":0},{"attributes":{"POSITION":1},"material":1}]}],"nodes":[{"translation":[0,0.5,0],"children":[1,2]},{"mesh":0,"translation":[-0.9,0,0]},{"mesh":0,"translation":[0.9,0,0]}],"scenes":[{"nodes":[0]}],"scene":0}""";
	var multi = System.Text.Encoding.UTF8.GetBytes( multiJson );
	var twoColor = System.Text.Json.Nodes.JsonNode.Parse( multi );
	twoColor["materials"][0]["pbrMetallicRoughness"] = System.Text.Json.Nodes.JsonNode.Parse( """{"baseColorFactor":[0.2,0.7,0.3,0.25]}""" );
	twoColor["materials"][1]["pbrMetallicRoughness"] = System.Text.Json.Nodes.JsonNode.Parse( """{"baseColorFactor":[0.8,0.1,0.4,0.75]}""" );
	var unsupported = System.Text.Json.Nodes.JsonNode.Parse( triangle );
	unsupported["extensionsUsed"] = System.Text.Json.Nodes.JsonNode.Parse( "[\"" + unsupportedName + "\"]" );
	unsupported["extensionsRequired"] = System.Text.Json.Nodes.JsonNode.Parse( "[\"" + unsupportedName + "\"]" );
	var optional = System.Text.Json.Nodes.JsonNode.Parse( triangle );
	optional["extensionsUsed"] = System.Text.Json.Nodes.JsonNode.Parse( "[\"" + unsupportedName + "\"]" );
	optional["extensions"] = System.Text.Json.Nodes.JsonNode.Parse( "{\"" + unsupportedName + "\":{\"probe\":true}}" );
	var missing = System.Text.Json.Nodes.JsonNode.Parse( triangle );
	missing["buffers"][0]["uri"] = missingName;
	var animated = System.Text.Json.Nodes.JsonNode.Parse( triangle );
	var animationBytes = new byte[32];
	var animationValues = new[] { 0f, 1f, 0f, 0f, 0f, 1f, 0f, 0f };
	for ( var i = 0; i < animationValues.Length; i++ ) System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian( animationBytes.AsSpan( i * 4, 4 ), animationValues[i] );
	var animationData = Convert.ToBase64String( animationBytes );
	animated["buffers"].AsArray().Add( System.Text.Json.Nodes.JsonNode.Parse( "{\"byteLength\":32,\"uri\":\"data:application/octet-stream;base64," + animationData + "\"}" ) );
	animated["bufferViews"].AsArray().Add( System.Text.Json.Nodes.JsonNode.Parse( "{\"buffer\":1,\"byteOffset\":0,\"byteLength\":8}" ) );
	animated["bufferViews"].AsArray().Add( System.Text.Json.Nodes.JsonNode.Parse( "{\"buffer\":1,\"byteOffset\":8,\"byteLength\":24}" ) );
	animated["accessors"].AsArray().Add( System.Text.Json.Nodes.JsonNode.Parse( "{\"bufferView\":2,\"componentType\":5126,\"count\":2,\"type\":\"SCALAR\",\"min\":[0],\"max\":[1]}" ) );
	animated["accessors"].AsArray().Add( System.Text.Json.Nodes.JsonNode.Parse( "{\"bufferView\":3,\"componentType\":5126,\"count\":2,\"type\":\"VEC3\"}" ) );
	animated["animations"] = System.Text.Json.Nodes.JsonNode.Parse( """[{"samplers":[{"input":2,"output":3}],"channels":[{"sampler":0,"target":{"node":0,"path":"translation"}}]}]""" );
	var files = new System.Collections.Generic.Dictionary<string, byte[]>( StringComparer.Ordinal )
	{
		["identity.gltf"] = triangle, ["khronos-triangle.gltf"] = original, ["Triangle.bin"] = buffer, ["identity.glb"] = glb, ["compiled-only.glb_c"] = glb, ["direct-only.glb_c"] = glb,
		["multi.gltf"] = multi,
		["two-color.gltf"] = System.Text.Encoding.UTF8.GetBytes( twoColor.ToJsonString() ),
		["colored.gltf"] = coloredBytes,
		["optional.gltf"] = System.Text.Encoding.UTF8.GetBytes( optional.ToJsonString() ),
		["animated.gltf"] = System.Text.Encoding.UTF8.GetBytes( animated.ToJsonString() ),
		["unsupported.gltf"] = System.Text.Encoding.UTF8.GetBytes( unsupported.ToJsonString() ),
		["missing.gltf"] = System.Text.Encoding.UTF8.GetBytes( missing.ToJsonString() )
	};
	if ( selection is "khronos-triangle-embedded" or "all" )
	{
		var embeddedPath = System.IO.Path.Combine( corpusRoot, "Models", "Triangle", "glTF-Embedded", "Triangle.gltf" );
		var embedded = System.IO.File.ReadAllBytes( embeddedPath );
		Require( Hash( embedded ) == "6bf10f2d1e07643cbda968fc6941929d52d4dfd4250182c852103668997f706a", "Pinned Khronos embedded Triangle differs." );
		files.Add( "khronos-triangle-embedded.gltf", embedded );
		if ( selection == "khronos-triangle-embedded" ) files.Remove( "Triangle.bin" );
	}
	var redirectType = typeof( Model ).Assembly.GetType( "Sandbox.Internal.RedirectFileSystem", true );
	redirectObject = redirectType.GetMethod( "Create", flags ).Invoke( null, new object[] { output } );
	redirect = (IDisposable)redirectObject;
	var addRedirect = redirectType.GetMethod( "AddAbsFile", flags );
	removeRedirect = redirectType.GetMethod( "RemoveAbsFile", flags );
	foreach ( var file in files )
	{
		var physical = System.IO.Path.Combine( output, file.Key );
		System.IO.File.WriteAllBytes( physical, file.Value );
		fixtureHashes.Add( file.Key, Hash( file.Value ) );
		var virtualPath = "/" + virtualBase + "/" + file.Key;
		addRedirect.Invoke( redirectObject, new object[] { virtualPath, physical } );
		redirects.Add( virtualPath );
	}
	// RedirectFileSystem registers native GAME symlinks. The managed loader reads
	// the separate game mount aggregate, so mount the same isolated redirect there.
	var wrapperConstructor = typeof( BaseFileSystem ).GetConstructors( flags ).Single( ctor =>
	{
		var parameters = ctor.GetParameters();
		return parameters.Length == 1 && parameters[0].ParameterType.IsInstanceOfType( redirectObject );
	} );
	managedMount = (BaseFileSystem)wrapperConstructor.Invoke( new[] { redirectObject } );
	mountedAggregate = Sandbox.FileSystem.Mounted ?? throw new InvalidOperationException( "Current game mount aggregate is unavailable." );
	var mountManaged = typeof( BaseFileSystem ).GetMethod( "Mount", flags, null, new[] { typeof( BaseFileSystem ) }, null ) ?? throw new InvalidOperationException( "Managed mount API unavailable." );
	unmountManaged = typeof( BaseFileSystem ).GetMethod( "UnMount", flags, null, new[] { typeof( BaseFileSystem ) }, null ) ?? throw new InvalidOperationException( "Managed unmount API unavailable." );
	mountManaged.Invoke( mountedAggregate, new object[] { managedMount } );
	mountedManaged = true;
	var mountedIdentity = virtualBase + "/identity.gltf";
	Require( mountedAggregate.FileExists( mountedIdentity ) && Hash( mountedAggregate.ReadAllBytes( mountedIdentity ).ToArray() ) == fixtureHashes["identity.gltf"], "The isolated fixture is not readable through the managed mount aggregate." );
	Require( mountedAggregate.FileExists( virtualBase + "/compiled-only.glb_c" ) && mountedAggregate.FileExists( virtualBase + "/direct-only.glb_c" ), "Both isolated compiled fixtures must be mounted." );
	Require( !mountedAggregate.FileExists( virtualBase + "/compiled-only.glb" ) && !mountedAggregate.FileExists( virtualBase + "/compiled-only.gltf" ), "The compiled-only fixture must not have source siblings." );
	Require( !mountedAggregate.FileExists( virtualBase + "/direct-only.glb" ) && !mountedAggregate.FileExists( virtualBase + "/direct-only.gltf" ), "The direct compiled fixture must not have source siblings." );
	if ( selection == "khronos-triangle-embedded" ) Require( !mountedAggregate.FileExists( virtualBase + "/Triangle.bin" ), "The embedded-only case must not mount the external Triangle buffer." );
	typeof( Model ).Assembly.GetType( "NativeEngine.g_pResourceSystem", true ).GetMethod( "InvalidateDatabase", flags ).Invoke( null, null );
	var logging = typeof( LogEvent ).Assembly.GetType( "Sandbox.Diagnostics.Logging", true );
	var logEvent = logging.GetEvent( "OnMessage", flags ) ?? throw new InvalidOperationException( "Existing log event unavailable; cannot verify diagnostic failures." );
	logHandler = entry =>
	{
		var text = (entry.Message ?? "") + (entry.Exception is null ? "" : " " + entry.Exception.Message);
		if ( !text.Contains( runId, StringComparison.OrdinalIgnoreCase ) && !text.Contains( unsupportedName, StringComparison.OrdinalIgnoreCase ) && !text.Contains( missingName, StringComparison.OrdinalIgnoreCase ) && !(selection == "unsupported-color" && text.Contains( "baseColor", StringComparison.OrdinalIgnoreCase )) && !(selection == "unsupported-animation" && text.Contains( "animat", StringComparison.OrdinalIgnoreCase )) ) return;
		if ( text.Length > 1024 ) text = text[..1024];
		lock ( messages ) if ( messages.Count < 40 ) messages.Add( entry.Level + ": " + text );
	};
	logEvent.GetAddMethod( true ).Invoke( null, new object[] { logHandler } );
	removeLogHandler = logEvent.GetRemoveMethod( true );
	if ( renderRequested )
	{
		var managedType = typeof( Model ).Assembly.GetType( "Sandbox.Rendering.ManagedSceneRendering", true );
		managedViewportEnabled = (bool)managedType.GetProperty( "Enabled" ).GetValue( null );
		if ( managedCapture )
		{
			managedRenderer = managedType.GetProperty( "Renderer" ).GetValue( null ) ?? throw new InvalidOperationException( "Managed renderer unavailable." );
			managedRenderToTexture = managedRenderer.GetType().GetMethod( "RenderToTexture", new[] { typeof( CameraComponent ), typeof( Texture ) } ) ?? throw new InvalidOperationException( "Managed texture capture unavailable." );
		}
	}

	async Task RenderAcceptance( string name, Model loaded )
	{
		try
		{
			phase = name + "-render-setup";
			var multiScene = name is "multi-scene" or "two-color-scene";
			var twoColors = name == "two-color-scene";
			var stock = Material.Load( "materials/default/white.vmat" );
			Require( stock is { IsValid: true } && !stock.IsError && stock.ShaderName.Contains( "complex", StringComparison.OrdinalIgnoreCase ), "Reference stock complex material unavailable." );
			renderMaterial = stock.CreateCopy( "gltf_loader_reference_" + runId );
			Require( renderMaterial is { IsValid: true } && !renderMaterial.IsError, "Reference material copy failed." );
			renderMaterial.SetFeature( "F_RENDER_BACKFACES", 0 );
			renderMaterial.SetFeature( "F_METALNESS_TEXTURE", 0 );
			renderMaterial.SetFeature( "F_ALPHA_TEST", 0 );
			renderMaterial.SetFeature( "F_TRANSLUCENT", 0 );
			renderMaterial.Set( "g_flMetalness", 1f );
			renderMaterial.Set( "g_flRoughnessScaleFactor", 0f );
			if ( name is "nonwhite-color" or "two-color-scene" ) renderMaterial.Set( "g_vColorTint", new Vector4( ExpectedStockTint( .2f ), ExpectedStockTint( .7f ), ExpectedStockTint( .3f ), 1 ) );
			if ( twoColors )
			{
				renderMaterialSecondary = stock.CreateCopy( "gltf_loader_reference_secondary_" + runId );
				Require( renderMaterialSecondary is { IsValid: true } && !renderMaterialSecondary.IsError, "Secondary reference material copy failed." );
				renderMaterialSecondary.SetFeature( "F_RENDER_BACKFACES", 0 );
				renderMaterialSecondary.SetFeature( "F_METALNESS_TEXTURE", 0 );
				renderMaterialSecondary.SetFeature( "F_ALPHA_TEST", 0 );
				renderMaterialSecondary.SetFeature( "F_TRANSLUCENT", 0 );
				renderMaterialSecondary.Set( "g_flMetalness", 1f );
				renderMaterialSecondary.Set( "g_flRoughnessScaleFactor", 0f );
				renderMaterialSecondary.Set( "g_vColorTint", new Vector4( ExpectedStockTint( .8f ), ExpectedStockTint( .1f ), ExpectedStockTint( .4f ), 1 ) );
			}
			const float units = 1f / .0254f;
			var triangleX = new[] { new[] { -.7f, -.2f, -.45f }, new[] { .2f, .7f, .45f } };
			var triangleY = new[] { -.3f, -.3f, .3f };
			Model Reference( int omit )
			{
				var builder = Model.Builder.WithName( "gltf_loader_reference_" + name + "_" + omit + "_" + runId );
				var count = multiScene ? 4 : 1;
				for ( var primitive = 0; primitive < count; primitive++ )
				{
					if ( primitive == omit ) continue;
					var positions = new Vector3[3];
					if ( multiScene )
					{
						var offset = primitive < 2 ? -.9f : .9f;
						for ( var i = 0; i < 3; i++ ) positions[i] = new Vector3( 0, (triangleX[primitive % 2][i] + offset) * units, (triangleY[i] + .5f) * units );
					}
					else { positions[0] = Vector3.Zero; positions[1] = new Vector3( 0, units, 0 ); positions[2] = new Vector3( 0, 0, units ); }
					var vertices = positions.Select( position => new Vertex { Position = position, Normal = Vector3.Forward, Tangent = new Vector4( 0, 1, 0, 1 ), Color = Color32.White } ).ToArray();
					var mesh = new Mesh( twoColors && primitive % 2 == 1 ? renderMaterialSecondary : renderMaterial ); mesh.CreateVertexBuffer( 3, vertices ); mesh.CreateIndexBuffer( 3, new[] { 0, 1, 2 } );
					mesh.Bounds = new BBox( new Vector3( -1, -100, -100 ), new Vector3( 1, 100, 100 ) );
					builder.AddMesh( mesh );
				}
				builder.WithViewBounds( new BBox( new Vector3( -1, -100, -100 ), new Vector3( 1, 100, 100 ) ) );
				var reference = builder.Create();
				Require( Valid( reference ), "Independent reference model failed to build." );
				renderReferences.Add( reference ); return reference;
			}
			var referenceModel = Reference( -1 );
			renderScene = Scene.CreateEditorScene();
			using ( renderScene.Push() )
			{
				renderCamera = renderScene.CreateObject().Components.Create<CameraComponent>();
				renderCamera.WorldPosition = multiScene ? new Vector3( 230, 0, 20 ) : new Vector3( 80, 19.68504f, 19.68504f );
				renderCamera.WorldRotation = Rotation.FromYaw( 180 ); renderCamera.BackgroundColor = Color.Black;
				renderCamera.ZNear = 1; renderCamera.FieldOfView = 50; renderCamera.EnablePostProcessing = false;
			}
			if ( diagnosticClear )
			{
				renderClear = new Sandbox.Rendering.CommandList( "glTF loader diagnostic camera color clear" );
				renderClear.Clear( Color.Black, true, false, false );
				renderCamera.AddCommandList( renderClear, Sandbox.Rendering.Stage.AfterDepthPrepass );
			}
			renderPreview = new Editor.SceneRenderingWidget { Scene = renderScene, Camera = renderCamera, WindowTitle = "glTF loaded model validation", Size = new Vector2( 384, 384 ) };
			onRenderFrame = () => renderFrames++; renderPreview.OnPreFrame += onRenderFrame; renderPreview.Show();
			var size = multiScene ? 128 : 96;
			renderTarget = Texture.CreateRenderTarget().WithSize( size, size ).WithFormat( ImageFormat.RGBA8888 ).Create( "gltf_loader_capture_" + name + "_" + runId );
			var centerY = multiScene ? 0 : 19.68504f; var centerZ = multiScene ? 20 : 19.68504f;
			renderLight = new ScenePointLight( renderScene.SceneWorld, new Vector3( 80, centerY, centerZ ), 500, Color.White * 4 );
			renderLight.ShadowsEnabled = false; renderLight.RenderDiffuse = true; renderLight.RenderSpecular = true; renderLight.SetBakedLightIndex( -1 );
			renderLight.LightColor = Color.Black;
			async Task WaitFrames()
			{
				var start = renderFrames; var timer = System.Diagnostics.Stopwatch.StartNew();
				while ( renderFrames < start + 3 )
				{
					cancellationToken.ThrowIfCancellationRequested();
					if ( timer.Elapsed > TimeSpan.FromSeconds( 5 ) ) throw new TimeoutException( $"Preview stalled at {renderFrames} callbacks after {start}." );
					await Task.Delay( 50, cancellationToken ); await MainThread.Wait();
				}
			}
			async Task<Color[]> Capture( string label, SceneCameraDebugMode mode )
			{
				phase = name + "-" + label; renderCamera.DebugMode = mode; await WaitFrames(); var before = renderFrames;
				using ( renderScene.Push() )
				{
					renderTarget.Clear( Color.Black );
					var submitted = managedCapture ? (bool)managedRenderToTexture.Invoke( managedRenderer, new object[] { renderCamera, renderTarget } ) : renderCamera.RenderToTexture( renderTarget );
					if ( !submitted ) throw new InvalidOperationException( "Camera submission failed: " + label );
				}
				await WaitFrames(); cancellationToken.ThrowIfCancellationRequested();
				using var bitmap = renderTarget.GetBitmap(); Require( bitmap is not null, "Render readback failed: " + label );
				var png = bitmap.ToPng(); var file = System.IO.Path.Combine( output, name + "-" + label + ".png" );
				System.IO.File.WriteAllBytes( file, png );
				captures.Add( new { name, label, mode = (int)mode, path = file, sha256 = Hash( png ), preview_frames_at_submit = before, preview_frames_at_readback = renderFrames } );
				return bitmap.GetPixels();
			}
			var background = await Capture( "background", SceneCameraDebugMode.Albedo );
			Require( Nonblack( background ) == 0, "Empty render background is not black." );
			async Task<System.Collections.Generic.Dictionary<string, Color[]>> Record( Model candidate, string label )
			{
				var obj = new SceneModel( renderScene.SceneWorld, candidate, Transform.Zero ); renderObjects.Add( obj );
				var images = new System.Collections.Generic.Dictionary<string, Color[]>();
				renderLight.LightColor = Color.Black;
				images["albedo"] = await Capture( label + "-albedo", SceneCameraDebugMode.Albedo );
				images["normal"] = await Capture( label + "-world-normal", SceneCameraDebugMode.NormalMap );
				images["no-light"] = await Capture( label + "-no-light-specular", SceneCameraDebugMode.Reflect );
				foreach ( var side in new[] { 1, -1 } )
				{
					renderLight.Position = new Vector3( side * 80, centerY, centerZ ); renderLight.LightColor = Color.White * 4;
					images[side == 1 ? "front" : "back"] = await Capture( label + (side == 1 ? "-front-specular" : "-back-specular"), SceneCameraDebugMode.Reflect );
				}
				renderLight.LightColor = Color.Black; obj.Delete(); renderObjects.Remove( obj );
				var empty = await Capture( label + "-empty", SceneCameraDebugMode.Albedo );
				Require( Nonblack( empty ) == 0 && PixelDifference( empty, background ) <= 5, label + " empty control failed." );
				return images;
			}
			var reference = await Record( referenceModel, "reference" );
			var actual = await Record( loaded, "loaded" );
			var mask = Enumerable.Range( 0, background.Length ).Where( i => PixelDifferent( reference["albedo"][i], background[i] ) ).ToArray();
			Require( mask.Length > 50, "Independent reference has insufficient visible coverage." );
			var normalR = mask.Average( i => reference["normal"][i].r ); var normalG = mask.Average( i => reference["normal"][i].g ); var normalB = mask.Average( i => reference["normal"][i].b );
			Require( normalR > .95f && MathF.Abs( normalG - .5f ) < .03f && MathF.Abs( normalB - .5f ) < .03f, "Reference world normal is not +X." );
			Require( Nonblack( reference["no-light"] ) == 0 && Nonblack( actual["no-light"] ) == 0 && Nonblack( reference["back"] ) == 0 && Nonblack( actual["back"] ) == 0, "No-light or back-light specular control is not black." );
			var frontChanged = mask.Count( i => PixelDifferent( reference["no-light"][i], reference["front"][i] ) );
			Require( frontChanged > 50, "Front light did not produce a visible reference specular response." );
			foreach ( var mode in new[] { "albedo", "normal", "no-light", "front", "back" } )
			{
				var different = PixelDifference( reference[mode], actual[mode] );
				renderCases.Add( new { name, mode, different_pixels = different, tolerance = 5, passed = different <= 5 } );
				Require( different <= 5, $"Loaded {name} {mode} differs from the native reference by {different} pixels." );
			}
			if ( multiScene )
				for ( var omit = 0; omit < 4; omit++ )
				{
					var partial = Reference( omit );
					var obj = new SceneModel( renderScene.SceneWorld, partial, Transform.Zero ); renderObjects.Add( obj );
					renderLight.LightColor = Color.Black;
					var pixels = await Capture( "omit-" + omit, SceneCameraDebugMode.Albedo );
					obj.Delete(); renderObjects.Remove( obj );
					var empty = await Capture( "omit-" + omit + "-empty", SceneCameraDebugMode.Albedo );
					var changed = PixelDifference( reference["albedo"], pixels );
					var passed = changed > 30 && Nonblack( empty ) == 0 && PixelDifference( empty, background ) <= 5;
					renderCases.Add( new { name, omission = omit, changed_pixels = changed, passed } );
					Require( passed, "Multi-scene primitive omission or empty control failed: " + omit );
				}
			renderCases.Add( new { name, stage = "render-controls", coverage = mask.Length, normal_r = normalR, normal_g = normalG, normal_b = normalB, front_specular_changed = frontChanged, passed = true } );
			completedRenderCases++;
		}
		finally { await MainThread.Wait(); CleanupRender(); }
	}

	// The prototype-rejection controls are selected individually and excluded from
	// all. In particular unsupported-color is a historical control that must fail
	// once RGB factors are supported; nonwhite-color is the positive acceptance.
	var requested = selection == "all" ? allowed.Where( x => x is not ("all" or "unsupported-color" or "unsupported-animation") ).ToArray() : new[] { selection };
	var eligibleRenderCases = requested.Count( x => x is "source-gltf" or "khronos-triangle" or "khronos-triangle-embedded" or "source-glb" or "compiled-reference" or "compiled-glb-c" or "multi-scene" or "nonwhite-color" or "two-color-scene" );
	Require( !renderRequested || eligibleRenderCases > 0, "The selected acceptance case has no render fixture." );
	foreach ( var name in requested )
	{
		cancellationToken.ThrowIfCancellationRequested();
		phase = name;
		if ( name == "resolver" )
		{
			var loader = typeof( Model ).Assembly.GetType( "Sandbox.GltfModelLoader", true );
			var resolveUri = loader.GetMethod( "ResolveMountedUri", flags, null, new[] { typeof( string ), typeof( string ) }, null ) ?? throw new InvalidOperationException( "Mounted URI resolver is unavailable." );
			foreach ( var entry in new[] { (Uri: "./mesh.bin", Expected: "models/part/mesh.bin"), (Uri: "../textures/a%20b.png", Expected: "models/textures/a b.png") } )
			{
				var actual = (string)resolveUri.Invoke( null, new object[] { "models/part/model.gltf", entry.Uri } );
				Require( actual == entry.Expected, $"Mounted URI {entry.Uri} resolved to {actual}, expected {entry.Expected}." );
			}
			foreach ( var uri in new[] { "/absolute.bin", "https://example.invalid/a", "a.bin?query", "a.bin#fragment", "a%ZZ.bin", "../../../escape.bin", "%2e%2e/%2e%2e/%2e%2e/escape.bin", "%2fabsolute.bin" } )
			{
				var rejected = false;
				try { resolveUri.Invoke( null, new object[] { "models/part/model.gltf", uri } ); }
				catch ( System.Reflection.TargetInvocationException e ) when ( e.InnerException is not null ) { rejected = true; }
				Require( rejected, "Unsafe mounted URI was accepted: " + uri );
			}
			cases.Add( new { name, passed = true, accepted = 2, rejected = 8 } );
			continue;
		}
		var filename = name switch { "source-gltf" => "identity.gltf", "khronos-triangle" => "khronos-triangle.gltf", "khronos-triangle-embedded" => "khronos-triangle-embedded.gltf", "source-glb" => "identity.glb", "compiled-reference" => "compiled-only.glb", "compiled-glb-c" => "direct-only.glb_c", "multi-scene" => "multi.gltf", "two-color-scene" => "two-color.gltf", "nonwhite-color" or "unsupported-color" => "colored.gltf", "optional-fallback" => "optional.gltf", "unsupported-animation" => "animated.gltf", "unsupported-required" => "unsupported.gltf", _ => "missing.gltf" };
		var path = virtualBase + "/" + filename;
		var negative = name is "unsupported-required" or "missing-dependency" or "unsupported-color" or "unsupported-animation";
		int messageStart; lock ( messages ) messageStart = messages.Count;
		Model model = null;
		Exception loadError = null;
		try { loadCalls++; model = Model.Load( path ); }
		catch ( Exception e ) { loadError = e; }
		if ( negative )
		{
			// Allow queued logging to reach the existing in-editor message event.
			await Task.Delay( 150, cancellationToken ); await MainThread.Wait();
			var expectedDetail = name switch { "unsupported-required" => unsupportedName, "missing-dependency" => missingName, "unsupported-color" => "baseColor", _ => "animat" };
			string[] captured; lock ( messages ) captured = messages.Skip( messageStart ).ToArray();
			var diagnostic = (loadError?.ToString() ?? "") + "\n" + string.Join( "\n", captured );
			var rejected = loadError is not null || !Valid( model );
			var clear = diagnostic.Contains( expectedDetail, StringComparison.OrdinalIgnoreCase );
			if ( Valid( model ) ) ownedModels.Add( model );
			cases.Add( new { name, path, passed = rejected && clear, rejected, diagnostic_contains_expected_detail = clear, exception = loadError?.ToString(), model_null = model is null, model_valid = model?.IsValid, model_error = model?.IsError } );
			Require( rejected && clear, name + " must reject the asset and identify " + expectedDetail + "." );
			continue;
		}
		cases.Add( new { name, path, stage = "first-sync-load", passed = loadError is null && Valid( model ), exception = loadError?.ToString(), model_null = model is null, model_valid = model?.IsValid, model_error = model?.IsError } );
		Require( loadError is null && Valid( model ), "Model.Load did not return a valid, non-error model for " + path + "." );
		ownedModels.Add( model );
		var normalizedPath = path.EndsWith( "_c", StringComparison.OrdinalIgnoreCase ) ? path[..^2] : path;
		Require( model.Guid == Guid.Empty && string.Equals( model.Name, normalizedPath, StringComparison.OrdinalIgnoreCase ) &&
			string.Equals( model.ResourcePath, normalizedPath, StringComparison.OrdinalIgnoreCase ) &&
			!model.Name.Contains( "gltf_model_", StringComparison.OrdinalIgnoreCase ), "The model retained a temporary procedural identity instead of its mounted asset path." );
		if ( name == "optional-fallback" )
		{
			await Task.Delay( 150, cancellationToken ); await MainThread.Wait();
			string[] captured; lock ( messages ) captured = messages.Skip( messageStart ).ToArray();
			var warning = captured.Any( message => message.StartsWith( "Warn:", StringComparison.OrdinalIgnoreCase ) &&
				message.Contains( path, StringComparison.OrdinalIgnoreCase ) && message.Contains( unsupportedName, StringComparison.OrdinalIgnoreCase ) &&
				message.Contains( "fallback", StringComparison.OrdinalIgnoreCase ) );
			cases.Add( new { name, path, stage = "optional-extension-warning", passed = warning, extension = unsupportedName } );
			Require( warning, "The optional-extension fallback must emit a warning identifying the asset path, extension and fallback." );
		}
		var info = model.MeshInfo;
		var isMultiScene = name is "multi-scene" or "two-color-scene";
		var expectedTriangles = isMultiScene ? 4 : 1;
		Require( info is not null && info.TotalTriangles == expectedTriangles && info.TotalDrawCalls > 0, $"{name}: expected {expectedTriangles} triangles and nonzero draws." );
		var bounds = model.RenderBounds;
		const float units = 1f / .0254f;
		if ( isMultiScene )
			Require( Near( bounds.Mins.x, 0 ) && Near( bounds.Maxs.x, 0 ) && Near( bounds.Mins.y, -1.6f * units ) && Near( bounds.Maxs.y, 1.6f * units ) && Near( bounds.Mins.z, .2f * units ) && Near( bounds.Maxs.z, .8f * units ), "Multi-node scene bounds do not retain both node transforms and primitives." );
		else
			Require( Near( bounds.Mins.x, 0 ) && Near( bounds.Maxs.x, 0 ) && Near( bounds.Mins.y, 0 ) && Near( bounds.Maxs.y, units ) && Near( bounds.Mins.z, 0 ) && Near( bounds.Maxs.z, units ), "Identity Triangle bounds differ from the expected glTF-to-engine geometry." );
		var firstMaterial = info.Meshes.SelectMany( x => x.DrawCalls ).Select( x => x.Material ).FirstOrDefault( x => x is { IsValid: true } && !x.IsError );
		Require( firstMaterial is not null, "glTF default material was not mapped to a valid native material." );
		if ( name == "nonwhite-color" )
		{
			var tint = firstMaterial.GetVector4( "g_vColorTint" );
			Require( MathF.Abs( tint.x - ExpectedStockTint( .2f ) ) < .0001f && MathF.Abs( tint.y - ExpectedStockTint( .7f ) ) < .0001f && MathF.Abs( tint.z - ExpectedStockTint( .3f ) ) < .0001f && MathF.Abs( tint.w - 1 ) < .0001f, "glTF linear base-color factor was not encoded for the stock shader's sRGB tint." );
		}
		if ( name == "two-color-scene" )
		{
			var materials = info.Meshes.SelectMany( x => x.DrawCalls ).Select( x => x.Material ).Where( x => x is { IsValid: true } && !x.IsError ).ToArray();
			bool Matches( Material candidate, float r, float g, float b )
			{
				var tint = candidate.GetVector4( "g_vColorTint" );
				return MathF.Abs( tint.x - ExpectedStockTint( r ) ) < .0001f && MathF.Abs( tint.y - ExpectedStockTint( g ) ) < .0001f && MathF.Abs( tint.z - ExpectedStockTint( b ) ) < .0001f && MathF.Abs( tint.w - 1 ) < .0001f;
			}
			var first = materials.FirstOrDefault( x => Matches( x, .2f, .7f, .3f ) );
			var second = materials.FirstOrDefault( x => Matches( x, .8f, .1f, .4f ) );
			Require( first is not null && second is not null && !ReferenceEquals( first, second ), "Different source RGB factors must use distinct cached native materials, and OPAQUE alpha must resolve to one." );
			cases.Add( new { name, path, stage = "distinct-materials", passed = true, first_rgb = new[] { .2f, .7f, .3f }, second_rgb = new[] { .8f, .1f, .4f }, authored_alpha = new[] { .25f, .75f }, effective_alpha = 1f } );
		}
		loadCalls++; var repeated = Model.Load( path );
		loadCalls++; var equivalent = Model.Load( "/" + path );
		loadCalls++; var asynchronous = await Model.LoadAsync( path ); await MainThread.Wait();
		if ( Valid( repeated ) ) ownedModels.Add( repeated );
		if ( Valid( equivalent ) ) ownedModels.Add( equivalent );
		if ( Valid( asynchronous ) ) ownedModels.Add( asynchronous );
		var shared = ReferenceEquals( model, repeated ) && ReferenceEquals( model, equivalent ) && ReferenceEquals( model, asynchronous );
		cases.Add( new { name, path, stage = "geometry-material-cache", passed = shared, model_guid = model.Guid.ToString(), model_name = model.Name, resource_path = model.ResourcePath, triangles = info.TotalTriangles, draw_calls = info.TotalDrawCalls, bounds_min = bounds.Mins.ToString(), bounds_max = bounds.Maxs.ToString(), material_shader = firstMaterial.ShaderName, same_repeat = ReferenceEquals( model, repeated ), same_equivalent_path = ReferenceEquals( model, equivalent ), same_async = ReferenceEquals( model, asynchronous ) } );
		Require( shared, "Repeated, equivalent-path and async loads must share the same cached model." );
		if ( renderRequested && (name is "source-gltf" or "khronos-triangle" or "khronos-triangle-embedded" or "source-glb" or "compiled-reference" or "compiled-glb-c" or "multi-scene" or "nonwhite-color" or "two-color-scene") ) await RenderAcceptance( name, model );
	}
	Require( !renderRequested || completedRenderCases == eligibleRenderCases, "Not all requested render cases completed." );
	Require( Valid( Model.Sphere ), "Existing vmdl sphere stopped working after glTF loads." );
	phase = "complete";
}
catch ( OperationCanceledException e ) { error = "Cancelled: " + e; }
catch ( Exception e ) { error = e.ToString(); }
finally
{
	await MainThread.Wait();
	CleanupRender();
	if ( removeLogHandler is not null && logHandler is not null ) Cleanup( "log observer", () => removeLogHandler.Invoke( null, new object[] { logHandler } ) );
	var destroyModel = typeof( Model ).GetMethod( "Destroy", flags );
	foreach ( var owned in ownedModels ) if ( Valid( owned ) && !ReferenceEquals( owned, Model.Sphere ) ) Cleanup( "unique loaded model", () => destroyModel.Invoke( owned, null ) );
	if ( mountedManaged ) Cleanup( "managed mount", () => unmountManaged.Invoke( mountedAggregate, new object[] { managedMount } ) );
	if ( removeRedirect is not null ) foreach ( var path in redirects ) Cleanup( "redirect " + path, () => removeRedirect.Invoke( redirectObject, new object[] { path } ) );
	if ( redirect is not null ) Cleanup( "redirect filesystem", () => redirect.Dispose() );
}
string[] observedMessages; lock ( messages ) observedMessages = messages.ToArray();
var receipt = new
{
	passed = error is null && cleanupErrors.Count == 0 && phase == "complete", selection, selection_source = selectionSource,
	request_path = requestPath, request_sha256 = requestHash, request_id = requestId, request_values = requestValues,
	runner_path = runnerPath, runner_source_sha256 = runnerHash, phase, error, cases, load_calls = loadCalls,
	scope = renderRequested ? "actual editor Model.Load/LoadAsync plus stock-material reference rendering; GLB_c fixture is not GPU-optimized and this is not full glTF appearance conformance" : "actual editor Model.Load/LoadAsync acceptance on isolated source and packaging fixtures; GLB_c fixture is not GPU-optimized and rendering is not exercised",
	sphere_vmdl_valid_before = sphereValid, relevant_messages = observedMessages, cleanup_errors = cleanupErrors, fixture_sha256 = fixtureHashes,
	render_requested = renderRequested, completed_render_cases = completedRenderCases, render_cases = renderCases, captures,
	capture_renderer = renderRequested ? (managedCapture ? "managed SceneBridge.RenderToTexture" : "native CameraComponent.RenderToTexture") : null,
	managed_viewport_enabled = managedViewportEnabled, diagnostic_camera_clear = diagnosticClear, preview_frames = renderFrames,
	virtual_base = virtualBase, managed_redirect_mounted = mountedManaged, engine_location = engineLocation, engine_mvid = typeof( Model ).Assembly.ManifestModule.ModuleVersionId.ToString(),
	gltf_mvid = typeof( Sandbox.Gltf.GltfReader ).Assembly.ManifestModule.ModuleVersionId.ToString(),
	is_editor = Application.IsEditor, starting_frame = startFrame, final_frame = Application.FrameCount, output
};
System.IO.Directory.CreateDirectory( output );
System.IO.File.WriteAllText( receiptPath, System.Text.Json.JsonSerializer.Serialize( receipt, new System.Text.Json.JsonSerializerOptions { WriteIndented = true } ) );
return receipt;
