// Run only inside an existing s&box editor. This exercises the actual public
// Model.Load/LoadAsync route; it does not create a runtime texture or render.
var engineLocation = typeof( Model ).Assembly.Location;
var root = new System.IO.DirectoryInfo( System.IO.Path.GetDirectoryName( engineLocation ) );
while ( root is not null && !System.IO.File.Exists( System.IO.Path.Combine( root.FullName, "engine", "Sandbox-Engine.slnx" ) ) ) root = root.Parent;
if ( root is null ) throw new InvalidOperationException( "Cannot find checkout from loaded engine: " + engineLocation );
var selection = Environment.GetEnvironmentVariable( "SBOX_GLTF_TEXTURE_MODEL_CASE" );
if ( string.IsNullOrWhiteSpace( selection ) ) selection = "all";
var supported = new[] { "source-gltf", "original-embedded-gltf", "original-binary-glb", "derived-compiled-reference", "all" };
if ( !supported.Contains( selection, StringComparer.Ordinal ) ) throw new ArgumentException( "Unknown texture model case: " + selection );
var runId = Guid.NewGuid().ToString( "N" );
var virtualBase = "gltf-textured-model/" + runId;
var output = System.IO.Path.Combine( root.FullName, "Temp", "gltf-validation", "editor-model-texture-load", runId );
System.IO.Directory.CreateDirectory( output );
var receiptPath = System.IO.Path.Combine( output, "receipt.json" );
var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static;
var cases = new System.Collections.Generic.List<object>();
var messages = new System.Collections.Generic.List<string>();
var cleanupErrors = new System.Collections.Generic.List<string>();
var redirects = new System.Collections.Generic.List<string>();
var ownedModels = new System.Collections.Generic.HashSet<Model>();
var fixtureHashes = new System.Collections.Generic.Dictionary<string, string>( StringComparer.Ordinal );
var phase = "initialize";
var startFrame = Application.FrameCount;
var loadCalls = 0;
var expectedCases = selection == "all" ? 4 : 1;
var passedCases = 0;
string error = null;
string sourceHash = null, embeddedHash = null, binaryHash = null, bufferHash = null, imageHash = null, derivedHash = null;
object redirectObject = null;
IDisposable redirect = null;
BaseFileSystem managedMount = null;
BaseFileSystem mountedAggregate = null;
System.Reflection.MethodInfo unmountManaged = null;
System.Reflection.MethodInfo removeRedirect = null;
System.Reflection.MethodInfo removeLogHandler = null;
Action<LogEvent> logHandler = null;
bool mountedManaged = false;
string Hash( byte[] bytes ) => Convert.ToHexStringLower( System.Security.Cryptography.SHA256.HashData( bytes ) );
void Require( bool condition, string message ) { if ( !condition ) throw new InvalidOperationException( message ); }
void Cleanup( string name, Action action ) { try { action(); } catch ( Exception e ) { cleanupErrors.Add( name + ": " + e ); } }
bool Valid( Model value ) => value is { IsValid: true } && !value.IsError && !ReferenceEquals( value, Model.Error );
bool Near( float actual, float expected ) => MathF.Abs( actual - expected ) <= .2f;
byte[] ReadBounded( string path )
{
	var info = new System.IO.FileInfo( path );
	Require( info.Exists && info.Length > 0 && info.Length <= 16 * 1024 * 1024, "Missing or oversized corpus file: " + path );
	return System.IO.File.ReadAllBytes( path );
}

try
{
	await MainThread.Wait();
	Require( Application.IsEditor && !Graphics.IsActive, "Run inside the editor on its main thread, outside a Graphics scope." );
	phase = "verify-pinned-corpus";
	var corpus = Environment.GetEnvironmentVariable( "SBOX_GLTF_CORPUS" );
	if ( string.IsNullOrWhiteSpace( corpus ) ) corpus = System.IO.Path.Combine( root.FullName, "Temp", "gltf-reference", "glTF-Sample-Assets" );
	var sourceDir = System.IO.Path.Combine( corpus, "Models", "BoxTextured", "glTF" );
	var binaryPath = System.IO.Path.Combine( corpus, "Models", "BoxTextured", "glTF-Binary", "BoxTextured.glb" );
	var embeddedPath = System.IO.Path.Combine( corpus, "Models", "BoxTextured", "glTF-Embedded", "BoxTextured.gltf" );
	var source = ReadBounded( System.IO.Path.Combine( sourceDir, "BoxTextured.gltf" ) );
	var geometry = ReadBounded( System.IO.Path.Combine( sourceDir, "BoxTextured0.bin" ) );
	var image = ReadBounded( System.IO.Path.Combine( sourceDir, "CesiumLogoFlat.png" ) );
	var binary = ReadBounded( binaryPath ); var embedded = ReadBounded( embeddedPath );
	sourceHash = Hash( source ); bufferHash = Hash( geometry ); imageHash = Hash( image ); binaryHash = Hash( binary ); embeddedHash = Hash( embedded );
	Require( sourceHash == "1e9003a4a2a8822ff60da529357bd8e4dec4a59b1a479017993e7e2ad5fcebef" &&
		bufferHash == "2e8c0483fa6665c686ec345f89dcbb2a694a587442584d09f8a83a59633327bc" &&
		imageHash == "9c22b05c5b136d03c5621a8765e50a8322be6c35b9de53e9fe22685840d7f469" &&
		binaryHash == "b510eca2e2ef33f62f9ed57d6e7ce2d10ebb2bdebc4a8e59d347719ba81abdf4" &&
		embeddedHash == "879feb174dbf48e9e0cb22d2402ab35ff8519e13f0c5a100676788712dc3c8c7", "Pinned BoxTextured source, dependency, image, embedded or original binary changed." );
	byte[] Resolve( string uri ) => uri switch { "BoxTextured0.bin" => geometry, "CesiumLogoFlat.png" => image, _ => throw new InvalidOperationException( "Unexpected BoxTextured dependency: " + uri ) };
	using ( var originalBinary = Sandbox.Gltf.GltfReader.Read( binary ) )
		Require( Hash( originalBinary.ReadImage( 0 ).Bytes ) == imageHash, "The pinned binary's embedded image differs from the source PNG." );
	using ( var originalEmbedded = Sandbox.Gltf.GltfReader.Read( embedded ) )
		Require( Hash( originalEmbedded.ReadImage( 0 ).Bytes ) == imageHash, "The pinned embedded asset's image differs from the source PNG." );
	using var expectedBitmap = Sandbox.Bitmap.CreateFromBytes( image );
	Require( expectedBitmap is not null && expectedBitmap.Width == 256 && expectedBitmap.Height == 256, "CPU image decode or expected dimensions differ." );
	var expectedPixels = expectedBitmap.GetPixels32();
	Require( expectedPixels.Length == 256 * 256, "CPU image pixel count differs." );

	phase = "prepare-derived-container";
	// The derived .glb_c below is packaging-only, not a GPU-optimized compiler artifact.
	var packed = Sandbox.Gltf.GltfCompiler.Compile( source, Resolve );
	var bundle = Sandbox.Gltf.GltfArtifactDependencies.Prepare( packed, Resolve );
	derivedHash = Hash( bundle.Artifact );
	var files = new System.Collections.Generic.Dictionary<string, byte[]>( StringComparer.Ordinal )
	{
		["source/BoxTextured.gltf"] = source, ["source/BoxTextured0.bin"] = geometry, ["source/CesiumLogoFlat.png"] = image,
		["embedded/BoxTextured.gltf"] = embedded, ["binary/BoxTextured.glb"] = binary, ["derived/compiled-only.glb_c"] = bundle.Artifact
	};
	foreach ( var dependency in bundle.Dependencies )
	{
		Require( Hash( dependency.Value ) == imageHash, "Derived image dependency does not match the original PNG." );
		files.Add( "derived/" + dependency.Key, dependency.Value );
	}
	Require( bundle.Dependencies.Count == 1, "Expected exactly one packaged texture dependency." );

	phase = "mount-fixtures";
	var redirectType = typeof( Model ).Assembly.GetType( "Sandbox.Internal.RedirectFileSystem", true );
	redirectObject = redirectType.GetMethod( "Create", flags ).Invoke( null, new object[] { output } );
	redirect = (IDisposable)redirectObject;
	var addRedirect = redirectType.GetMethod( "AddAbsFile", flags );
	removeRedirect = redirectType.GetMethod( "RemoveAbsFile", flags );
	foreach ( var file in files )
	{
		var physical = System.IO.Path.Combine( output, file.Key.Replace( '/', System.IO.Path.DirectorySeparatorChar ) );
		System.IO.Directory.CreateDirectory( System.IO.Path.GetDirectoryName( physical ) );
		System.IO.File.WriteAllBytes( physical, file.Value );
		fixtureHashes.Add( file.Key, Hash( file.Value ) );
		var alias = "/" + virtualBase + "/" + file.Key;
		addRedirect.Invoke( redirectObject, new object[] { alias, physical } );
		redirects.Add( alias );
	}
	var wrapperConstructor = typeof( BaseFileSystem ).GetConstructors( flags ).Single( ctor =>
	{
		var parameters = ctor.GetParameters();
		return parameters.Length == 1 && parameters[0].ParameterType.IsInstanceOfType( redirectObject );
	} );
	managedMount = (BaseFileSystem)wrapperConstructor.Invoke( new[] { redirectObject } );
	mountedAggregate = Sandbox.FileSystem.Mounted ?? throw new InvalidOperationException( "Current game mount aggregate is unavailable." );
	var mountManaged = typeof( BaseFileSystem ).GetMethod( "Mount", flags, null, new[] { typeof( BaseFileSystem ) }, null ) ?? throw new InvalidOperationException( "Managed mount API unavailable." );
	unmountManaged = typeof( BaseFileSystem ).GetMethod( "UnMount", flags, null, new[] { typeof( BaseFileSystem ) }, null ) ?? throw new InvalidOperationException( "Managed unmount API unavailable." );
	mountManaged.Invoke( mountedAggregate, new object[] { managedMount } ); mountedManaged = true;
	foreach ( var file in files )
	{
		var mounted = virtualBase + "/" + file.Key;
		Require( mountedAggregate.FileExists( mounted ) && Hash( mountedAggregate.ReadAllBytes( mounted ).ToArray() ) == fixtureHashes[file.Key], "Mounted fixture differs: " + file.Key );
	}
	Require( !mountedAggregate.FileExists( virtualBase + "/derived/compiled-only.glb" ) && !mountedAggregate.FileExists( virtualBase + "/derived/compiled-only.gltf" ), "Derived compiled fixture has a source sibling." );
	Require( !mountedAggregate.FileExists( virtualBase + "/binary/BoxTextured.gltf" ) && !mountedAggregate.FileExists( virtualBase + "/binary/CesiumLogoFlat.png" ), "Original binary fixture must not have external source/image siblings." );
	Require( !mountedAggregate.FileExists( virtualBase + "/embedded/BoxTextured0.bin" ) && !mountedAggregate.FileExists( virtualBase + "/embedded/CesiumLogoFlat.png" ), "Embedded fixture must not have external geometry/image siblings." );
	typeof( Model ).Assembly.GetType( "NativeEngine.g_pResourceSystem", true ).GetMethod( "InvalidateDatabase", flags ).Invoke( null, null );
	var logging = typeof( LogEvent ).Assembly.GetType( "Sandbox.Diagnostics.Logging", true );
	var logEvent = logging.GetEvent( "OnMessage", flags ) ?? throw new InvalidOperationException( "Existing log event unavailable." );
	logHandler = entry =>
	{
		var text = (entry.Message ?? "") + (entry.Exception is null ? "" : " " + entry.Exception.Message);
		if ( !text.Contains( runId, StringComparison.OrdinalIgnoreCase ) ) return;
		if ( text.Length > 1024 ) text = text[..1024];
		lock ( messages ) if ( messages.Count < 60 ) messages.Add( entry.Level + ": " + text );
	};
	logEvent.GetAddMethod( true ).Invoke( null, new object[] { logHandler } ); removeLogHandler = logEvent.GetRemoveMethod( true );

	var requested = selection == "all" ? supported.Where( x => x != "all" ).ToArray() : new[] { selection };
	foreach ( var name in requested )
	{
		cancellationToken.ThrowIfCancellationRequested();
		phase = name;
		var assetPath = virtualBase + (name switch { "source-gltf" => "/source/BoxTextured.gltf", "original-embedded-gltf" => "/embedded/BoxTextured.gltf", "original-binary-glb" => "/binary/BoxTextured.glb", _ => "/derived/compiled-only.glb" } );
		var result = new System.Collections.Generic.Dictionary<string, object>
		{
			["name"] = name, ["asset_path"] = assetPath,
			["fixture_origin"] = name == "derived-compiled-reference" ? "derived packaging-only GLB_c, cold canonical .glb reference" : "untouched pinned Khronos corpus asset",
			["passed"] = false
		};
		int logStart; lock ( messages ) logStart = messages.Count;
		try
		{
			loadCalls++;
			var model = Model.Load( assetPath );
			result["model_null"] = model is null; result["model_valid"] = model?.IsValid; result["model_error"] = model?.IsError;
			Require( Valid( model ), "Model.Load did not return a valid non-error model for " + assetPath );
			ownedModels.Add( model );
			var info = model.MeshInfo;
			Require( info is not null && info.TotalTriangles == 12 && info.TotalDrawCalls > 0, "Expected 12 triangles and at least one draw." );
			var bounds = model.RenderBounds;
			// The pinned source rotates an axis-aligned cube centered at the origin;
			// after glTF's node matrix and the meter-to-inch basis, each bound is ±0.5 m.
			const float half = .5f / .0254f;
			Require( Near( bounds.Mins.x, -half ) && Near( bounds.Mins.y, -half ) && Near( bounds.Mins.z, -half ) &&
				Near( bounds.Maxs.x, half ) && Near( bounds.Maxs.y, half ) && Near( bounds.Maxs.z, half ), "World-baked bounds differ from the fixed BoxTextured reference." );
			var material = info.Meshes.SelectMany( x => x.DrawCalls ).Select( x => x.Material ).FirstOrDefault( x => x is { IsValid: true } && !x.IsError );
			Require( material is not null && material.ShaderName.Contains( "complex", StringComparison.OrdinalIgnoreCase ), "A valid stock complex material is required." );
			var texture = material.GetTexture( "g_tColor" );
			Require( texture is { IsValid: true } && !texture.IsError, "The material has no valid native g_tColor resource." );
			Require( texture.Width == expectedBitmap.Width && texture.Height == expectedBitmap.Height, "Loaded texture dimensions differ from the source image." );
			var actualPixels = texture.GetPixels();
			Require( actualPixels.Length == expectedPixels.Length, "Native texture pixel count differs from the source image." );
			var pixelDifferences = 0;
			for ( var i = 0; i < actualPixels.Length; i++ )
				if ( actualPixels[i].r != expectedPixels[i].r || actualPixels[i].g != expectedPixels[i].g || actualPixels[i].b != expectedPixels[i].b || actualPixels[i].a != expectedPixels[i].a ) pixelDifferences++;
			Require( pixelDifferences == 0, "Native texture differs from the decoded source at " + pixelDifferences + " pixels." );
			// The currently used stock complex path takes constant metalness when this
			// feature is off. A roughness scale of 0 yielded roughness 1 in the
			// controlled editor probe; this does not establish its general equation.
			Require( material.HasParameter( "g_flMetalness" ) && material.HasParameter( "g_flRoughnessScaleFactor" ), "Cannot verify stock complex metallic and roughness parameters on this material." );
			var metalFeature = material.GetFeature( "F_METALNESS_TEXTURE" );
			var metallic = material.GetVector4( "g_flMetalness" ).x;
			var roughnessScale = material.GetVector4( "g_flRoughnessScaleFactor" ).x;
			Require( metalFeature == 0 && MathF.Abs( metallic ) < .0001f && MathF.Abs( roughnessScale ) < .0001f, "Stock complex metallic=0 / roughness=1 mapping differs." );
			loadCalls++; var repeated = Model.Load( assetPath );
			if ( Valid( repeated ) ) ownedModels.Add( repeated );
			loadCalls++; var equivalent = Model.Load( "/" + assetPath );
			if ( Valid( equivalent ) ) ownedModels.Add( equivalent );
			loadCalls++; var asynchronous = await Model.LoadAsync( assetPath ); await MainThread.Wait();
			if ( Valid( asynchronous ) ) ownedModels.Add( asynchronous );
			var sameRepeat = ReferenceEquals( model, repeated ); var sameEquivalent = ReferenceEquals( model, equivalent ); var sameAsync = ReferenceEquals( model, asynchronous );
			Require( sameRepeat && sameEquivalent && sameAsync, "Repeat, equivalent-path and asynchronous calls do not share the model." );
			bool? sameCompiledAlias = null;
			if ( name == "derived-compiled-reference" )
			{
				loadCalls++; var direct = Model.Load( virtualBase + "/derived/compiled-only.glb_c" );
				if ( Valid( direct ) ) ownedModels.Add( direct );
				sameCompiledAlias = ReferenceEquals( model, direct );
				Require( sameCompiledAlias == true, "Cold canonical reference and direct GLB_c load do not share a model." );
			}
			result["passed"] = true; result["triangles"] = info.TotalTriangles; result["draw_calls"] = info.TotalDrawCalls;
			result["bounds_min"] = bounds.Mins.ToString(); result["bounds_max"] = bounds.Maxs.ToString();
			result["shader"] = material.ShaderName; result["metalness_texture_feature"] = metalFeature; result["metallic"] = metallic; result["roughness_scale"] = roughnessScale;
			result["texture_width"] = texture.Width; result["texture_height"] = texture.Height; result["texture_pixel_count"] = actualPixels.Length; result["texture_pixel_differences"] = pixelDifferences;
			result["texture_resource_path"] = texture.ResourcePath; result["texture_mips"] = texture.Mips; result["texture_format"] = texture.ImageFormat.ToString();
			result["same_repeat"] = sameRepeat; result["same_equivalent_path"] = sameEquivalent; result["same_async"] = sameAsync; result["same_compiled_alias"] = sameCompiledAlias;
			passedCases++;
		}
		catch ( OperationCanceledException ) { throw; }
		catch ( Exception e ) { result["error"] = e.ToString(); }
		await Task.Delay( 100, cancellationToken ); await MainThread.Wait();
		lock ( messages ) result["asset_messages"] = messages.Skip( logStart ).Where( message => message.Contains( assetPath, StringComparison.OrdinalIgnoreCase ) ).ToArray();
		cases.Add( result );
	}
	phase = "complete";
}
catch ( OperationCanceledException e ) { error = "Cancelled: " + e; }
catch ( Exception e ) { error = e.ToString(); }
finally
{
	await MainThread.Wait();
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
	passed = error is null && cleanupErrors.Count == 0 && phase == "complete" && cases.Count == expectedCases && passedCases == expectedCases,
	selection, phase, error, cases, expected_cases = expectedCases, passed_cases = passedCases, load_calls = loadCalls,
	scope = "actual editor Model.Load/LoadAsync of pinned textured Box assets; native texture base-mip readback and stock parameter metadata only; no rendering, optimized GLB layout, sampler or full material-fidelity claim",
	source_min_filter = 9986, sample_requires_mipmaps = true, mip_chain_verified = false, precompiled_texture_lookup_verified = false, sample_parity_verified = false,
	source_sha256 = sourceHash, embedded_sha256 = embeddedHash, binary_sha256 = binaryHash, buffer_sha256 = bufferHash, image_sha256 = imageHash, derived_packaging_sha256 = derivedHash,
	fixture_sha256 = fixtureHashes, relevant_messages = observedMessages, cleanup_errors = cleanupErrors, managed_redirect_mounted = mountedManaged,
	virtual_base = virtualBase, engine_location = engineLocation, engine_mvid = typeof( Model ).Assembly.ManifestModule.ModuleVersionId.ToString(),
	gltf_mvid = typeof( Sandbox.Gltf.GltfReader ).Assembly.ManifestModule.ModuleVersionId.ToString(),
	is_editor = Application.IsEditor, starting_frame = startFrame, final_frame = Application.FrameCount, output
};
System.IO.File.WriteAllText( receiptPath, System.Text.Json.JsonSerializer.Serialize( receipt, new System.Text.Json.JsonSerializerOptions { WriteIndented = true } ) );
return receipt;
