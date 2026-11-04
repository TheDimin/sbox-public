// Run as a method body inside the existing editor. Compiles the exact unbuilt
// static-scene helper and its self-contained tests without replacing engine DLLs.
var engineLocation = typeof( Model ).Assembly.Location;
var root = new System.IO.DirectoryInfo( System.IO.Path.GetDirectoryName( engineLocation ) );
while ( root is not null && !System.IO.File.Exists( System.IO.Path.Combine( root.FullName, "engine", "Sandbox-Engine.slnx" ) ) ) root = root.Parent;
if ( root is null ) throw new InvalidOperationException( "Cannot locate checkout from loaded engine: " + engineLocation );
var diagnosticClear = Environment.GetEnvironmentVariable( "SBOX_GLTF_CAMERA_CLEAR" ) == "1";
var managedCapture = Environment.GetEnvironmentVariable( "SBOX_GLTF_CAPTURE_RENDERER" ) == "managed";
var output = System.IO.Path.Combine( root.FullName, "Temp", "gltf-validation", (diagnosticClear ? "editor-scene-camera-clear" : "editor-scene") + (managedCapture ? "-managed" : "") );
System.IO.Directory.CreateDirectory( output );
var receiptPath = System.IO.Path.Combine( output, "receipt.json" );
var sourcePath = System.IO.Path.Combine( root.FullName, "engine", "Sandbox.Gltf", "GltfDocument.StaticScene.cs" );
var testPath = System.IO.Path.Combine( root.FullName, "engine", "Tests", "Sandbox.Test.Gltf", "GltfStaticSceneAssemblyTests.cs" );
var frameworkPath = System.IO.Path.Combine( root.FullName, "engine", "Tests", "Sandbox.Test.Gltf", "bin", "Release", "net10.0", "Microsoft.VisualStudio.TestPlatform.TestFramework.dll" );
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
	Require( gltfAssembly.GetType( "Sandbox.Gltf.GltfStaticSceneAssembler", false ) is null, "Loaded glTF assembly already contains the helper; this isolated proof requires the old assembly." );
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
	var implicitUsings = "global using System; global using System.Collections.Generic; global using System.IO; global using System.Linq; global using System.Net.Http; global using System.Threading; global using System.Threading.Tasks;";
	var trees = new[]
	{
		Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText( implicitUsings, parseOptions, path: "SDKImplicitUsings.cs" ),
		Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText( System.Text.Encoding.UTF8.GetString( sourceBytes ), parseOptions, path: sourcePath ),
		Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText( System.Text.Encoding.UTF8.GetString( testBytes ), parseOptions, path: testPath )
	};
	var helperName = "sbox-mcp-script-gltf-scene-" + Guid.NewGuid().ToString( "N" );
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
	var assemblerType = helper.GetType( "Sandbox.Gltf.GltfStaticSceneAssembler", true );
	var assemble = assemblerType.GetMethod( "Assemble", flags );
	var testType = helper.GetType( "Sandbox.Tests.Gltf.GltfStaticSceneAssemblyTests", true );
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
	Require( unitPassed, "One or more exact-source tests failed." );
	phase = "build-fixture";
	const string fixtureJson = """{"asset":{"version":"2.0"},"buffers":[{"uri":"data:application/octet-stream;base64,MzMzv5qZmb4AAAAAzcxMvpqZmb4AAAAAZmbmvpqZmT4AAAAAzcxMPpqZmb4AAAAAMzMzP5qZmb4AAAAAZmbmPpqZmT4AAAAA","byteLength":72}],"bufferViews":[{"buffer":0,"byteOffset":0,"byteLength":36},{"buffer":0,"byteOffset":36,"byteLength":36}],"accessors":[{"bufferView":0,"componentType":5126,"count":3,"type":"VEC3","min":[-0.7,-0.3,0],"max":[-0.2,0.3,0]},{"bufferView":1,"componentType":5126,"count":3,"type":"VEC3","min":[0.2,-0.3,0],"max":[0.7,0.3,0]}],"meshes":[{"primitives":[{"attributes":{"POSITION":0}},{"attributes":{"POSITION":1}}]}],"nodes":[{"translation":[0,0.5,0],"children":[1,2]},{"mesh":0,"translation":[-0.9,0,0]},{"mesh":0,"translation":[0.9,0,0]}],"scenes":[{"nodes":[0]}],"scene":0}""";
	var fixtureBytes = System.Text.Encoding.UTF8.GetBytes( fixtureJson );
	fixtureHash = Hash( fixtureBytes );
	Require( fixtureHash == "de32632ec289aa5d6fff3adf4ee5e7f2dcdc5e3138c074d0a99e795379340fb1", "Synthetic scene differs from the pinned fixture." );
	using var sourceDocument = Sandbox.Gltf.GltfReader.Read( fixtureBytes );
	using var compiledDocument = Sandbox.Gltf.GltfReader.Read( Sandbox.Gltf.GltfCompiler.Compile( fixtureBytes ) );
	object[] Primitives( Sandbox.Gltf.GltfDocument document )
	{
		var result = assemble.Invoke( null, new object[] { document } ) ?? throw new InvalidOperationException( "Static assembler returned null." );
		var items = (System.Collections.IEnumerable)result.GetType().GetProperty( "Primitives" ).GetValue( result );
		return items.Cast<object>().ToArray();
	}
	var sourcePrimitives = Primitives( sourceDocument );
	var compiledPrimitives = Primitives( compiledDocument );
	Require( sourcePrimitives.Length == 4 && compiledPrimitives.Length == 4, "Two nodes with two primitives each must stage four draws." );
	var stock = Material.Load( "materials/default/white.vmat" );
	Require( stock is { IsValid: true } && !stock.IsError, "Stock white material unavailable." );
	material = stock.CreateCopy( "gltf_scene_probe_" + Guid.NewGuid().ToString( "N" ) );
	Require( material is { IsValid: true }, "Could not copy stock material." );
	material.SetFeature( "F_RENDER_BACKFACES", 1 );
	var nativeBuilder = typeof( Model ).Assembly.GetType( "Sandbox.GltfModelPrimitiveBuilder", true );
	var buildPrimitive = nativeBuilder.GetMethod( "Build", flags ) ?? throw new InvalidOperationException( "Native primitive builder unavailable." );
	var suffix = Guid.NewGuid().ToString( "N" );
	Model[] Stage( object[] primitives, string label )
	{
		var result = new Model[primitives.Length];
		for ( var i = 0; i < primitives.Length; i++ )
		{
			var data = (Sandbox.Gltf.GltfDrawData)primitives[i].GetType().GetProperty( "Data" ).GetValue( primitives[i] );
			result[i] = (Model)buildPrimitive.Invoke( null, new object[] { data, material, "gltf_scene_" + label + "_" + i + "_" + suffix } );
			Require( result[i] is { IsValid: true } && !result[i].IsError, "A staged scene primitive could not build." );
			models.Add( result[i] );
		}
		return result;
	}
	var stagedSource = Stage( sourcePrimitives, "source" );
	var stagedCompiled = Stage( compiledPrimitives, "compiled" );
	// Independent reference: authored triangle positions and node translations are
	// spelled out here rather than obtained from the assembler or its draw data.
	var referenceModels = new System.Collections.Generic.List<Model>();
	const float units = 1f / .0254f;
	var triangleX = new[] { new[] { -.7f, -.2f, -.45f }, new[] { .2f, .7f, .45f } };
	var triangleY = new[] { -.3f, -.3f, .3f };
	foreach ( var offset in new[] { -.9f, .9f } )
		for ( var primitive = 0; primitive < 2; primitive++ )
		{
			var vertices = new Vertex[3];
			for ( var i = 0; i < vertices.Length; i++ ) vertices[i] = new Vertex
			{
				Position = new Vector3( 0, (triangleX[primitive][i] + offset) * units, (triangleY[i] + .5f) * units ),
				Normal = Vector3.Forward, Color = Color32.White
			};
			var mesh = new Mesh( material );
			mesh.CreateVertexBuffer( vertices.Length, vertices );
			mesh.CreateIndexBuffer( 3, new[] { 0, 1, 2 } );
			var referenceBounds = new BBox( new Vector3( -1, -100, -100 ), new Vector3( 1, 100, 100 ) );
			mesh.Bounds = referenceBounds;
			var reference = Model.Builder.WithName( "gltf_scene_reference_" + referenceModels.Count + "_" + suffix ).AddMesh( mesh ).WithViewBounds( referenceBounds ).Create();
			Require( reference is { IsValid: true } && !reference.IsError, "Reference model could not build." );
			models.Add( reference ); referenceModels.Add( reference );
		}
	var corpusRoot = Environment.GetEnvironmentVariable( "SBOX_GLTF_CORPUS" );
	if ( string.IsNullOrWhiteSpace( corpusRoot ) ) corpusRoot = System.IO.Path.Combine( root.FullName, "Temp", "gltf-reference", "glTF-Sample-Assets" );
	var boxDirectory = System.IO.Path.Combine( corpusRoot, "Models", "Box", "glTF" );
	var box = System.IO.File.ReadAllBytes( System.IO.Path.Combine( boxDirectory, "Box.gltf" ) );
	var boxBuffer = System.IO.File.ReadAllBytes( System.IO.Path.Combine( boxDirectory, "Box0.bin" ) );
	boxHash = Hash( box ); boxBufferHash = Hash( boxBuffer );
	Require( boxHash == "4a0d69eecfce0672a50b71dc218cbacec6c53fe2445040c235c6314b1b2c41b9" && boxBufferHash == "3266a8e39b9f425b3341cbe5eec7849f44310256bfa651e6b8b40c85ce0ccafb", "Khronos Box differs from pinned files." );
	byte[] ResolveBox( string uri ) => uri == "Box0.bin" ? boxBuffer : throw new InvalidOperationException( "Unexpected Box dependency: " + uri );
	using var boxSource = Sandbox.Gltf.GltfReader.Read( box, ResolveBox );
	using var boxCompiled = Sandbox.Gltf.GltfReader.Read( Sandbox.Gltf.GltfCompiler.Compile( box, ResolveBox ) );
	var boxSourcePrimitives = Primitives( boxSource ); var boxCompiledPrimitives = Primitives( boxCompiled );
	Require( boxSourcePrimitives.Length == 1 && boxCompiledPrimitives.Length == 1, "Unexpected Khronos Box primitive count." );
	var boxSourceModels = Stage( boxSourcePrimitives, "box_source" );
	var boxCompiledModels = Stage( boxCompiledPrimitives, "box_compiled" );
	completed.Add( phase );

	phase = "private-editor-preview";
	scene = Scene.CreateEditorScene();
	CameraComponent camera;
	using ( scene.Push() )
	{
		camera = scene.CreateObject().Components.Create<CameraComponent>();
		camera.WorldPosition = new Vector3( 230, 0, 20 ); camera.WorldRotation = Rotation.FromYaw( 180 );
		camera.BackgroundColor = Color.Black; camera.ZNear = 1; camera.FieldOfView = 50;
		camera.EnablePostProcessing = false; camera.DebugMode = SceneCameraDebugMode.Albedo;
	}
	if ( diagnosticClear )
	{
		cameraClearCommands = new Sandbox.Rendering.CommandList( "glTF scene diagnostic camera color clear" );
		cameraClearCommands.Clear( Color.Black, true, false, false );
		camera.AddCommandList( cameraClearCommands, Sandbox.Rendering.Stage.AfterDepthPrepass ); commandCamera = camera;
	}
	preview = new Editor.SceneRenderingWidget { Scene = scene, Camera = camera, WindowTitle = "glTF static scene capability", Size = new Vector2( 384, 384 ) };
	onPreviewFrame = () => previewFrames++;
	preview.OnPreFrame += onPreviewFrame; preview.Show();
	target = Texture.CreateRenderTarget().WithSize( 128, 128 ).WithFormat( ImageFormat.RGBA8888 ).Create( "gltf_scene_capture_" + suffix );
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
	async Task<Color[]> Capture( string name )
	{
		phase = name; await WaitFrames(); var before = previewFrames;
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
		captures.Add( new { name, path, sha256 = Hash( png ), frame = Application.FrameCount, preview_frames_at_submit = before, preview_frames_at_readback = previewFrames } );
		return bitmap.GetPixels();
	}
	async Task<(Color[] Pixels, Color[] Empty)> Render( Model[] renderModels, string name, int omit = -1 )
	{
		var current = new System.Collections.Generic.List<SceneModel>();
		for ( var i = 0; i < renderModels.Length; i++ )
		{
			if ( i == omit ) continue;
			var obj = new SceneModel( scene.SceneWorld, renderModels[i], Transform.Zero ); current.Add( obj ); sceneObjects.Add( obj );
		}
		var pixels = await Capture( name );
		foreach ( var obj in current ) { obj.Delete(); sceneObjects.Remove( obj ); }
		var empty = await Capture( name + "_empty" );
		return (pixels, empty);
	}
	var initial = await Capture( "background_initial" );
	var background = await Capture( "background" );
	if ( Nonblack( initial ) != 0 || Nonblack( background ) != 0 || Difference( initial, background ) > 5 ) failures.Add( "Initial empty-scene controls failed." );
	var nativeReference = await Render( referenceModels.ToArray(), "fixture_reference" );
	var sourceRender = await Render( stagedSource, "fixture_source" );
	var compiledRender = await Render( stagedCompiled, "fixture_compiled" );
	var fixtureCoverage = Difference( nativeReference.Pixels, background );
	var sourceDiff = Difference( nativeReference.Pixels, sourceRender.Pixels );
	var compiledDiff = Difference( nativeReference.Pixels, compiledRender.Pixels );
	var referenceEmpty = Difference( nativeReference.Empty, background );
	var sourceEmpty = Difference( sourceRender.Empty, background );
	var compiledEmpty = Difference( compiledRender.Empty, background );
	var fixturePass = fixtureCoverage > 100 && sourceDiff <= 5 && compiledDiff <= 5 && referenceEmpty <= 5 && sourceEmpty <= 5 && compiledEmpty <= 5;
	if ( !fixturePass ) failures.Add( $"Fixture comparison failed: coverage {fixtureCoverage}, source {sourceDiff}, compiled {compiledDiff}, empty {referenceEmpty}/{sourceEmpty}/{compiledEmpty}." );
	comparisons.Add( new { name = "multi_node_multi_primitive", coverage = fixtureCoverage, source_difference = sourceDiff, compiled_difference = compiledDiff, reference_empty = referenceEmpty, source_empty = sourceEmpty, compiled_empty = compiledEmpty, passed = fixturePass } );
	for ( var i = 0; i < referenceModels.Count; i++ )
	{
		var missing = await Render( referenceModels.ToArray(), "fixture_omit_" + i, i );
		var changed = Difference( nativeReference.Pixels, missing.Pixels ); var emptyDiff = Difference( missing.Empty, background );
		var passed = changed > 30 && emptyDiff <= 5;
		if ( !passed ) failures.Add( $"Fixture omission {i} control failed: changed {changed}, empty {emptyDiff}." );
		comparisons.Add( new { name = "omission_" + i, changed, empty_difference = emptyDiff, passed } );
	}
	camera.WorldPosition = new Vector3( 130, 0, 0 );
	var boxBackground = await Capture( "box_background" );
	var boxFirst = await Render( boxSourceModels, "box_source" );
	var boxSecond = await Render( boxCompiledModels, "box_compiled" );
	var boxCoverage = Difference( boxFirst.Pixels, boxBackground ); var boxDiff = Difference( boxFirst.Pixels, boxSecond.Pixels );
	var boxSourceEmpty = Difference( boxFirst.Empty, boxBackground ); var boxCompiledEmpty = Difference( boxSecond.Empty, boxBackground );
	var boxPass = Nonblack( boxBackground ) == 0 && boxCoverage > 100 && boxDiff <= 5 && boxSourceEmpty <= 5 && boxCompiledEmpty <= 5;
	if ( !boxPass ) failures.Add( $"Khronos Box comparison failed: coverage {boxCoverage}, difference {boxDiff}, empty {boxSourceEmpty}/{boxCompiledEmpty}." );
	comparisons.Add( new { name = "khronos_box_source_compiled", coverage = boxCoverage, source_compiled_difference = boxDiff, source_empty = boxSourceEmpty, compiled_empty = boxCompiledEmpty, passed = boxPass } );
	renderPassed = failures.Count == 0;
	completed.Add( "private-editor-render-controls" );
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
	scope = "exact-source isolated static-scene tests and private editor stock-material scene rendering; no skinning, morph, scene integration or full glTF appearance claim",
	phase, error, failures, cleanup_errors = cleanupErrors, completed, compiler_diagnostics = compilerDiagnostics,
	test_cases = testCases, unit_passed = unitPassed, render_passed = renderPassed, captures, comparisons,
	helper_source_sha256 = sourceHash, test_source_sha256 = testHash, framework_sha256 = System.IO.File.Exists( frameworkPath ) ? Hash( System.IO.File.ReadAllBytes( frameworkPath ) ) : null, helper_mvid = helperMvid,
	fixture_sha256 = fixtureHash, box_sha256 = boxHash, box_buffer_sha256 = boxBufferHash,
	engine_location = engineLocation, engine_mvid = typeof( Model ).Assembly.ManifestModule.ModuleVersionId.ToString(),
	gltf_mvid = typeof( Sandbox.Gltf.GltfReader ).Assembly.ManifestModule.ModuleVersionId.ToString(),
	is_editor = Application.IsEditor, capture_renderer = managedCapture ? "managed SceneBridge.RenderToTexture" : "native CameraComponent.RenderToTexture", managed_viewport_enabled = managedViewportEnabled,
	diagnostic_camera_clear = diagnosticClear, preview_frames = previewFrames, starting_frame = startFrame, final_frame = Application.FrameCount, output
};
System.IO.File.WriteAllText( receiptPath, System.Text.Json.JsonSerializer.Serialize( receipt, new System.Text.Json.JsonSerializerOptions { WriteIndented = true } ) );
return receipt;
