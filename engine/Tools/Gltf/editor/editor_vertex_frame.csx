// Run as a method body inside the existing editor. Compiles the exact unbuilt
// stock vertex builder and its self-contained tests without replacing engine DLLs.
var engineLocation = typeof( Model ).Assembly.Location;
var root = new System.IO.DirectoryInfo( System.IO.Path.GetDirectoryName( engineLocation ) );
while ( root is not null && !System.IO.File.Exists( System.IO.Path.Combine( root.FullName, "engine", "Sandbox-Engine.slnx" ) ) ) root = root.Parent;
if ( root is null ) throw new InvalidOperationException( "Cannot locate checkout from loaded engine: " + engineLocation );
var diagnosticClear = Environment.GetEnvironmentVariable( "SBOX_GLTF_CAMERA_CLEAR" ) == "1";
var expectRed = Environment.GetEnvironmentVariable( "SBOX_GLTF_EXPECT_VERTEX_FRAME_RED" ) == "1";
var managedCapture = Environment.GetEnvironmentVariable( "SBOX_GLTF_CAPTURE_RENDERER" ) == "managed";
var output = System.IO.Path.Combine( root.FullName, "Temp", "gltf-validation", (expectRed ? "editor-vertex-frame-red" : "editor-vertex-frame") + (diagnosticClear ? "-camera-clear" : "") + (managedCapture ? "-managed" : "") );
System.IO.Directory.CreateDirectory( output );
var receiptPath = System.IO.Path.Combine( output, "receipt.json" );
var sourcePath = System.IO.Path.Combine( root.FullName, "engine", "Sandbox.Engine", "Resources", "Model", "Gltf", "GltfModelPrimitiveBuilder.cs" );
var testPath = System.IO.Path.Combine( root.FullName, "engine", "Tests", "Sandbox.Test.Unit", "Resources", "GltfStockVertexFrameTests.cs" );
var frameworkPath = System.IO.Path.Combine( root.FullName, "engine", "Tests", "Sandbox.Test.Unit", "bin", "Release", "net10.0", "Microsoft.VisualStudio.TestPlatform.TestFramework.dll" );
var phase = "initialize";
var completed = new System.Collections.Generic.List<string>();
var failures = new System.Collections.Generic.List<string>();
var cleanupErrors = new System.Collections.Generic.List<string>();
var compilerDiagnostics = new System.Collections.Generic.List<string>();
var testCases = new System.Collections.Generic.List<object>();
var captures = new System.Collections.Generic.List<object>();
var comparisons = new System.Collections.Generic.List<object>();
var models = new System.Collections.Generic.List<Model>();
var sceneObjects = new System.Collections.Generic.List<SceneModel>();
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
var startFrame = Application.FrameCount;
string error = null, sourceHash = null, testHash = null, helperMvid = null, fixtureHash = null, boxHash = null, boxBufferHash = null;
var unitPassed = false;
var renderPassed = false;
var previewFrames = 0;
Scene scene = null;
Editor.SceneRenderingWidget preview = null;
Texture target = null;
Material material = null;
ScenePointLight pointLight = null;
CameraComponent commandCamera = null;
Sandbox.Rendering.CommandList cameraClearCommands = null;
Action onPreviewFrame = null;
System.Runtime.Loader.AssemblyLoadContext helperContext = null;
object managedRenderer = null;
System.Reflection.MethodInfo managedRenderToTexture = null;
bool? managedViewportEnabled = null;
string Hash( byte[] bytes ) => Convert.ToHexStringLower( System.Security.Cryptography.SHA256.HashData( bytes ) );
void Require( bool condition, string message ) { if ( !condition ) throw new InvalidOperationException( message ); }
void Cleanup( string name, Action action ) { try { action(); } catch ( Exception e ) { cleanupErrors.Add( name + ": " + e ); } }
bool Different( Color a, Color b ) => MathF.Abs( a.r - b.r ) > .03f || MathF.Abs( a.g - b.g ) > .03f || MathF.Abs( a.b - b.b ) > .03f;
int Difference( Color[] a, Color[] b ) => a.Zip( b, ( x, y ) => Different( x, y ) ).Count( x => x );
int Nonblack( Color[] pixels ) => pixels.Count( x => Different( x, Color.Black ) );
try
{
	await MainThread.Wait();
	Require( Application.IsEditor && !Graphics.IsActive, "Run in the editor on the main thread outside a graphics scope." );
	var managedRenderingType = typeof( Model ).Assembly.GetType( "Sandbox.Rendering.ManagedSceneRendering", true );
	managedViewportEnabled = (bool)managedRenderingType.GetProperty( "Enabled" ).GetValue( null );
	if ( managedCapture )
	{
		managedRenderer = managedRenderingType.GetProperty( "Renderer" ).GetValue( null ) ?? throw new InvalidOperationException( "Managed renderer unavailable." );
		managedRenderToTexture = managedRenderer.GetType().GetMethod( "RenderToTexture", new[] { typeof( CameraComponent ), typeof( Texture ) } ) ?? throw new InvalidOperationException( "Managed texture rendering API unavailable." );
	}
	phase = "compile-exact-source";
	var gltfAssembly = typeof( Sandbox.Gltf.GltfDocument ).Assembly;
	var gltfPath = gltfAssembly.Location;
	using ( var stream = System.IO.File.OpenRead( gltfPath ) )
	using ( var pe = new System.Reflection.PortableExecutable.PEReader( stream ) )
	{
		var metadata = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader( pe );
		var diskMvid = metadata.GetGuid( metadata.GetModuleDefinition().Mvid );
		Require( diskMvid == gltfAssembly.ManifestModule.ModuleVersionId, "Loaded glTF assembly differs from disk; use matching binaries." );
	}
	using ( var stream = System.IO.File.OpenRead( engineLocation ) )
	using ( var pe = new System.Reflection.PortableExecutable.PEReader( stream ) )
	{
		var metadata = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader( pe );
		Require( metadata.GetGuid( metadata.GetModuleDefinition().Mvid ) == typeof( Model ).Assembly.ManifestModule.ModuleVersionId, "Loaded engine assembly differs from disk; use matching binaries." );
	}
	var sourceBytes = System.IO.File.ReadAllBytes( sourcePath );
	var testBytes = System.IO.File.ReadAllBytes( testPath );
	sourceHash = Hash( sourceBytes ); testHash = Hash( testBytes );
	Require( System.IO.File.Exists( frameworkPath ), "The existing MSTest framework DLL is missing." );
	var metadataPaths = new System.Collections.Generic.Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
	foreach ( var assembly in AppDomain.CurrentDomain.GetAssemblies() )
	{
		try
		{
			if ( assembly.IsDynamic || assembly.GetName().Name.StartsWith( "sbox-mcp-script-", StringComparison.OrdinalIgnoreCase ) ) continue;
			var path = assembly.Location;
			if ( string.IsNullOrWhiteSpace( path ) || !System.IO.File.Exists( path ) ) continue;
			var name = System.Reflection.AssemblyName.GetAssemblyName( path ).Name;
			if ( name == "Microsoft.VisualStudio.TestPlatform.TestFramework" ) continue;
			if ( !metadataPaths.ContainsKey( name ) ) metadataPaths.Add( name, path );
		}
		catch ( Exception e ) when ( e is System.NotSupportedException || e is System.IO.IOException || e is BadImageFormatException || e is UnauthorizedAccessException ) { }
	}
	metadataPaths["Microsoft.VisualStudio.TestPlatform.TestFramework"] = frameworkPath;
	var refs = metadataPaths.Values.Select( x => Microsoft.CodeAnalysis.MetadataReference.CreateFromFile( x ) ).ToArray();
	var parseOptions = new Microsoft.CodeAnalysis.CSharp.CSharpParseOptions( Microsoft.CodeAnalysis.CSharp.LanguageVersion.Preview );
	var implicitUsings = "global using System; global using System.Collections.Generic; global using System.IO; global using System.Linq; global using System.Net.Http; global using System.Threading; global using System.Threading.Tasks; global using Microsoft.VisualStudio.TestTools.UnitTesting;";
	var trees = new[]
	{
		Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText( implicitUsings, parseOptions, path: "SDKImplicitUsings.cs" ),
		Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText( System.Text.Encoding.UTF8.GetString( sourceBytes ), parseOptions, path: sourcePath ),
		Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText( System.Text.Encoding.UTF8.GetString( testBytes ), parseOptions, path: testPath )
	};
	var helperName = "sbox-mcp-script-gltf-vertex-frame-" + Guid.NewGuid().ToString( "N" );
	var options = new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions( Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary, optimizationLevel: Microsoft.CodeAnalysis.OptimizationLevel.Release );
	var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create( helperName, trees, refs, options );
	using var image = new System.IO.MemoryStream();
	var emitted = compilation.Emit( image );
	compilerDiagnostics.AddRange( emitted.Diagnostics.Select( x => x.ToString() ) );
	Require( emitted.Success, "Exact helper/test source did not compile; inspect compiler diagnostics." );
	helperContext = new System.Runtime.Loader.AssemblyLoadContext( helperName, isCollectible: true );
	helperContext.Resolving += ( context, requested ) =>
	{
		if ( requested.Name == "Microsoft.VisualStudio.TestPlatform.TestFramework" ) return context.LoadFromAssemblyPath( frameworkPath );
		return AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault( x => !x.IsDynamic && System.Runtime.Loader.AssemblyLoadContext.GetLoadContext( x ) == System.Runtime.Loader.AssemblyLoadContext.Default && System.Reflection.AssemblyName.ReferenceMatchesDefinition( x.GetName(), requested ) );
	};
	image.Position = 0;
	var helper = helperContext.LoadFromStream( image );
	helperMvid = helper.ManifestModule.ModuleVersionId.ToString();
	var builderType = helper.GetType( "Sandbox.GltfModelPrimitiveBuilder", true );
	var build = builderType.GetMethod( "Build", flags );
	var testType = helper.GetType( "Sandbox.Tests.GltfStockVertexFrameTests", true );
	Require( !testType.GetMethods( System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public ).Any( method => method.GetCustomAttributesData().Any( a => a.AttributeType.Name == "DataTestMethodAttribute" ) ), "Unsupported DataTestMethod; do not silently skip test cases." );
	var testMethods = testType.GetMethods( System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public )
		.Where( x => x.GetCustomAttributesData().Any( a => a.AttributeType.FullName == "Microsoft.VisualStudio.TestTools.UnitTesting.TestMethodAttribute" ) ).OrderBy( x => x.Name, StringComparer.Ordinal ).ToArray();
	Require( testMethods.Length > 0, "No test methods found." );
	Require( !testType.GetMethods( flags ).Any( method => method.GetCustomAttributesData().Any( a => a.AttributeType.Name is "TestInitializeAttribute" or "TestCleanupAttribute" or "ClassInitializeAttribute" or "ClassCleanupAttribute" ) ), "Unsupported test lifecycle attributes." );
	phase = "execute-exact-tests";
	foreach ( var test in testMethods )
	{
		cancellationToken.ThrowIfCancellationRequested();
		var attributes = test.GetCustomAttributesData();
		Require( test.GetParameters().Length == 0 && test.ReturnType == typeof( void ) && !attributes.Any( a => a.AttributeType.Name is "DataRowAttribute" or "DynamicDataAttribute" or "ExpectedExceptionAttribute" ), "Unsupported parameterized or special MSTest method: " + test.Name );
		string caseError = null;
		try { test.Invoke( Activator.CreateInstance( testType ), null ); }
		catch ( System.Reflection.TargetInvocationException e ) { caseError = (e.InnerException ?? e).ToString(); }
		catch ( Exception e ) { caseError = e.ToString(); }
		testCases.Add( new { name = test.Name, passed = caseError is null, error = caseError } );
		if ( caseError is not null ) failures.Add( "Test " + test.Name + ": " + caseError );
	}
	unitPassed = failures.Count == 0;
	completed.Add( "compile-and-execute-exact-tests" );
	if ( !expectRed ) Require( unitPassed, "One or more exact-source tests failed." );
	if ( !expectRed )
	{
		phase = "build-lighting-fixtures";
		var corpusRoot = Environment.GetEnvironmentVariable( "SBOX_GLTF_CORPUS" );
		if ( string.IsNullOrWhiteSpace( corpusRoot ) ) corpusRoot = System.IO.Path.Combine( root.FullName, "Temp", "gltf-reference", "glTF-Sample-Assets" );
		var directory = System.IO.Path.Combine( corpusRoot, "Models", "Triangle", "glTF" );
		var triangle = System.IO.File.ReadAllBytes( System.IO.Path.Combine( directory, "Triangle.gltf" ) );
		fixtureHash = Hash( triangle );
		Require( fixtureHash == "6f4a8a1fbefbac2d0adde0800864279fd98c3f119077380e6840810ed62b6cfc", "Pinned Khronos Triangle source differs." );
		byte[] Resolve( string uri )
		{
			Require( uri == "Triangle.bin", "Unexpected Triangle dependency: " + uri );
			var bytes = System.IO.File.ReadAllBytes( System.IO.Path.Combine( directory, uri ) );
			Require( Hash( bytes ) == "898ed7f560dca9907a80c98be57fbbc0f3a092d2e3683b964ffcfa75c549ac62", "Pinned Triangle binary differs." );
			return bytes;
		}
		using var original = Sandbox.Gltf.GltfReader.Read( triangle, Resolve );
		using var compiled = Sandbox.Gltf.GltfReader.Read( Sandbox.Gltf.GltfCompiler.Compile( triangle, Resolve ) );
		var stock = Material.Load( "materials/default/white.vmat" );
		Require( stock is { IsValid: true } && !stock.IsError && stock.ShaderName.Contains( "complex", StringComparison.OrdinalIgnoreCase ), "Stock complex material unavailable." );
		var suffix = Guid.NewGuid().ToString( "N" );
		material = stock.CreateCopy( "gltf_vertex_frame_material_" + suffix );
		Require( material is { IsValid: true } && !material.IsError, "Could not copy stock material." );
		material.SetFeature( "F_RENDER_BACKFACES", 1 );
		material.SetFeature( "F_ALPHA_TEST", 0 );
		material.SetFeature( "F_TRANSLUCENT", 0 );
		material.Set( "g_vColorTint", new Vector4( .2f, .7f, .3f, 1 ) );
		Model BuildStaged( Sandbox.Gltf.GltfDrawData data, string label )
		{
			var model = (Model)build.Invoke( null, new object[] { data, material, "gltf_vertex_frame_" + label + "_" + suffix } );
			Require( model is { IsValid: true } && !model.IsError, "Could not stage model: " + label );
			models.Add( model );
			return model;
		}
		var sourceModel = BuildStaged( original.CreateDrawData( 0, 0 ), "source" );
		var compiledModel = BuildStaged( compiled.CreateDrawData( 0, 0 ), "compiled" );
		const float unit = 1f / .0254f;
		var refMesh = new Mesh( material );
		refMesh.CreateVertexBuffer( 3, new Vertex[] {
			new() { Position = new Vector3( 0, 0, 0 ), Normal = Vector3.Forward, Tangent = new Vector4( 0, 1, 0, 1 ), Color = Color32.White },
			new() { Position = new Vector3( 0, unit, 0 ), Normal = Vector3.Forward, Tangent = new Vector4( 0, 1, 0, 1 ), Color = Color32.White },
			new() { Position = new Vector3( 0, 0, unit ), Normal = Vector3.Forward, Tangent = new Vector4( 0, 1, 0, 1 ), Color = Color32.White } } );
		refMesh.CreateIndexBuffer( 3, new[] { 0, 1, 2 } );
		var bounds = new BBox( Vector3.Zero, new Vector3( 0, unit, unit ) ); refMesh.Bounds = bounds;
		var reference = Model.Builder.WithName( "gltf_vertex_frame_reference_" + suffix ).AddMesh( refMesh ).WithViewBounds( bounds ).Create();
		Require( reference is { IsValid: true } && !reference.IsError, "Native reference model failed to build." );
		models.Add( reference );
		completed.Add( phase );

		phase = "private-preview";
		scene = Scene.CreateEditorScene();
		CameraComponent camera;
		using ( scene.Push() )
		{
			camera = scene.CreateObject().Components.Create<CameraComponent>();
			camera.WorldPosition = new Vector3( 80, 19.68504f, 19.68504f ); camera.WorldRotation = Rotation.FromYaw( 180 );
			camera.BackgroundColor = Color.Black; camera.ZNear = 1; camera.FieldOfView = 50; camera.EnablePostProcessing = false;
		}
		if ( diagnosticClear )
		{
			cameraClearCommands = new Sandbox.Rendering.CommandList( "glTF vertex frame diagnostic camera color clear" );
			cameraClearCommands.Clear( Color.Black, true, false, false );
			camera.AddCommandList( cameraClearCommands, Sandbox.Rendering.Stage.AfterDepthPrepass ); commandCamera = camera;
		}
		preview = new Editor.SceneRenderingWidget { Scene = scene, Camera = camera, WindowTitle = "glTF stock vertex frame lighting", Size = new Vector2( 384, 384 ) };
		onPreviewFrame = () => previewFrames++; preview.OnPreFrame += onPreviewFrame; preview.Show();
		target = Texture.CreateRenderTarget().WithSize( 96, 96 ).WithFormat( ImageFormat.RGBA8888 ).Create( "gltf_vertex_frame_target_" + suffix );
		pointLight = new ScenePointLight( scene.SceneWorld, new Vector3( 80, 19.68504f, 19.68504f ), 500, Color.White * 4 );
		pointLight.ShadowsEnabled = false; pointLight.RenderDiffuse = true; pointLight.RenderSpecular = true; pointLight.SetBakedLightIndex( -1 );
		// SceneObject.RenderingEnabled does not disable a light's contribution.
		// Set its radiance to zero for a verifiable no-light control.
		pointLight.LightColor = Color.Black;
		Require( pointLight.RenderDiffuse && pointLight.RenderSpecular && !pointLight.ShadowsEnabled && pointLight.Radius == 500, "Point-light state did not retain the requested settings." );
		async Task WaitFrames()
		{
			var start = previewFrames; var timer = System.Diagnostics.Stopwatch.StartNew();
			while ( previewFrames < start + 3 )
			{
				cancellationToken.ThrowIfCancellationRequested();
				if ( timer.Elapsed > TimeSpan.FromSeconds( 5 ) ) throw new TimeoutException( $"Preview stalled at {previewFrames} callbacks after {start}." );
				await Task.Delay( 50, cancellationToken ); await MainThread.Wait();
			}
		}
		async Task<Color[]> Capture( string name, SceneCameraDebugMode mode )
		{
			phase = name; camera.DebugMode = mode; await WaitFrames(); var before = previewFrames;
			using ( scene.Push() )
			{
				target.Clear( Color.Black );
				var submitted = managedCapture ? (bool)managedRenderToTexture.Invoke( managedRenderer, new object[] { camera, target } ) : camera.RenderToTexture( target );
				if ( !submitted ) throw new InvalidOperationException( "Camera submit failed: " + name );
			}
			await WaitFrames(); cancellationToken.ThrowIfCancellationRequested();
			using var bitmap = target.GetBitmap(); Require( bitmap is not null, "Camera readback failed: " + name );
			var png = bitmap.ToPng(); var path = System.IO.Path.Combine( output, name + ".png" );
			System.IO.File.WriteAllBytes( path, png );
			captures.Add( new { name, mode = (int)mode, path, sha256 = Hash( png ), frame = Application.FrameCount, preview_frames_at_submit = before, preview_frames_at_readback = previewFrames } );
			return bitmap.GetPixels();
		}
		var background = await Capture( "background_initial", SceneCameraDebugMode.Albedo );
		if ( Nonblack( background ) != 0 ) failures.Add( "Initial empty albedo background is not black." );
		var outputs = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, Color[]>>();
		foreach ( var item in new[] { (Name: "reference", Model: reference), (Name: "source", Model: sourceModel), (Name: "compiled", Model: compiledModel) } )
		{
			var obj = new SceneModel( scene.SceneWorld, item.Model, Transform.Zero ); sceneObjects.Add( obj );
			var images = new System.Collections.Generic.Dictionary<string, Color[]>(); outputs.Add( item.Name, images );
			pointLight.LightColor = Color.Black;
			images["albedo"] = await Capture( item.Name + "_albedo", SceneCameraDebugMode.Albedo );
			images["normal"] = await Capture( item.Name + "_world_normal", SceneCameraDebugMode.NormalMap );
			images["unlit_diffuse"] = await Capture( item.Name + "_no_light_diffuse", (SceneCameraDebugMode)2 );
			images["unlit_specular"] = await Capture( item.Name + "_no_light_specular", SceneCameraDebugMode.Reflect );
			foreach ( var side in new[] { 1, -1 } )
			{
				pointLight.Position = new Vector3( side * 80, 19.68504f, 19.68504f ); pointLight.LightColor = Color.White * 4;
				var label = side == 1 ? "front" : "back";
				images[label + "_diffuse"] = await Capture( item.Name + "_" + label + "_diffuse", (SceneCameraDebugMode)2 );
				images[label + "_specular"] = await Capture( item.Name + "_" + label + "_specular", SceneCameraDebugMode.Reflect );
			}
			pointLight.LightColor = Color.Black; obj.Delete(); sceneObjects.Remove( obj );
			var empty = await Capture( item.Name + "_empty", SceneCameraDebugMode.Albedo );
			var emptyDifference = Difference( empty, background );
			if ( emptyDifference > 5 || Nonblack( empty ) != 0 ) failures.Add( item.Name + ": empty control failed, difference " + emptyDifference );
		}
		var referenceImages = outputs["reference"];
		var mask = Enumerable.Range( 0, background.Length ).Where( i => Different( referenceImages["albedo"][i], background[i] ) ).ToArray();
		Require( mask.Length > 50, "Reference triangle lacks visible coverage." );
		var normalR = mask.Average( i => referenceImages["normal"][i].r );
		var normalG = mask.Average( i => referenceImages["normal"][i].g );
		var normalB = mask.Average( i => referenceImages["normal"][i].b );
		if ( normalR < .95f || MathF.Abs( normalG - .5f ) > .03f || MathF.Abs( normalB - .5f ) > .03f ) failures.Add( $"Reference world normal is not +X: {normalR}, {normalG}, {normalB}." );
		var diffuseChanged = mask.Count( i => Different( referenceImages["unlit_diffuse"][i], referenceImages["front_diffuse"][i] ) );
		var specularChanged = mask.Count( i => Different( referenceImages["unlit_specular"][i], referenceImages["front_specular"][i] ) );
		if ( diffuseChanged <= 50 || specularChanged <= 50 ) failures.Add( $"Front light failed to affect diffuse/specular: {diffuseChanged}/{specularChanged}." );
		if ( Nonblack( referenceImages["unlit_diffuse"] ) != 0 || Nonblack( referenceImages["unlit_specular"] ) != 0 || Nonblack( referenceImages["back_diffuse"] ) != 0 || Nonblack( referenceImages["back_specular"] ) != 0 ) failures.Add( "No-light or back-light reference was not black." );
		comparisons.Add( new { name = "reference_lighting_controls", coverage = mask.Length, normal_r = normalR, normal_g = normalG, normal_b = normalB, front_diffuse_changed = diffuseChanged, front_specular_changed = specularChanged } );
		foreach ( var label in new[] { "source", "compiled" } )
			foreach ( var mode in new[] { "albedo", "normal", "unlit_diffuse", "unlit_specular", "front_diffuse", "front_specular", "back_diffuse", "back_specular" } )
			{
				var difference = Difference( referenceImages[mode], outputs[label][mode] );
				var passed = difference <= 5;
				if ( !passed ) failures.Add( $"{label} {mode} differs from the independently authored native reference by {difference} pixels." );
				comparisons.Add( new { name = label + "_" + mode, difference, passed } );
			}
		renderPassed = failures.Count == 0;
		completed.Add( "private-editor-lighting-comparisons" );
	}

}
catch ( OperationCanceledException e ) { error = "Cancelled: " + e; }
catch ( Exception e ) { error = e.ToString(); }
finally
{
	await MainThread.Wait();
	if ( preview is not null && preview.IsValid && onPreviewFrame is not null ) Cleanup( "preview callback", () => preview.OnPreFrame -= onPreviewFrame );
	if ( commandCamera.IsValid() && cameraClearCommands is not null ) Cleanup( "camera command", () => commandCamera.RemoveCommandList( cameraClearCommands ) );
	if ( preview is not null && preview.IsValid ) Cleanup( "preview", () => { preview.Close(); preview.Destroy(); } );
	foreach ( var obj in sceneObjects ) if ( obj.IsValid() ) Cleanup( "scene model", () => obj.Delete() );
	if ( pointLight.IsValid() ) Cleanup( "point light", () => pointLight.Delete() );
	if ( scene is not null ) Cleanup( "scene", () => scene.Destroy() );
	if ( target is not null ) Cleanup( "target", () => target.Dispose() );
	var destroyModel = typeof( Model ).GetMethod( "Destroy", flags );
	foreach ( var model in models ) if ( model is not null && model.IsValid ) Cleanup( "model", () => destroyModel.Invoke( model, null ) );
	if ( material is not null && material.IsValid ) Cleanup( "material", () => typeof( Material ).GetMethod( "Destroy", flags ).Invoke( material, null ) );
	if ( helperContext is not null ) Cleanup( "helper context", () => helperContext.Unload() );
}
var receipt = new
{
	passed = error is null && failures.Count == 0 && cleanupErrors.Count == 0 && unitPassed && renderPassed,
	expected_red_run = expectRed,
	scope = "exact-source stock vertex frame tests and private editor stock-shader lighting; no skinning, morph or full glTF appearance claim",
	phase, error, failures, cleanup_errors = cleanupErrors, completed, compiler_diagnostics = compilerDiagnostics,
	test_cases = testCases, unit_passed = unitPassed, render_passed = renderPassed, captures, comparisons,
	helper_source_sha256 = sourceHash, test_source_sha256 = testHash, framework_sha256 = System.IO.File.Exists( frameworkPath ) ? Hash( System.IO.File.ReadAllBytes( frameworkPath ) ) : null, helper_mvid = helperMvid,
	triangle_sha256 = fixtureHash,
	engine_location = engineLocation, engine_mvid = typeof( Model ).Assembly.ManifestModule.ModuleVersionId.ToString(),
	gltf_mvid = typeof( Sandbox.Gltf.GltfReader ).Assembly.ManifestModule.ModuleVersionId.ToString(),
	is_editor = Application.IsEditor, capture_renderer = managedCapture ? "managed SceneBridge.RenderToTexture" : "native CameraComponent.RenderToTexture", managed_viewport_enabled = managedViewportEnabled,
	diagnostic_camera_clear = diagnosticClear, preview_frames = previewFrames, starting_frame = startFrame, final_frame = Application.FrameCount, output
};
System.IO.File.WriteAllText( receiptPath, System.Text.Json.JsonSerializer.Serialize( receipt, new System.Text.Json.JsonSerializerOptions { WriteIndented = true } ) );
return receipt;
