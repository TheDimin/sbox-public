// Run as an async method body in the existing editor after its loaded engine and
// renderer have been coordinated. Compiles only the worktree tests and the
// unchanged TestScenes helper against the LOADED producer assemblies. CPU-side
// transform-buffer checks only: no native draws, GPU proof, or real-frame pump.
var engineAssembly = typeof( Model ).Assembly;
var engineLocation = engineAssembly.Location;
var checkout = new System.IO.DirectoryInfo( System.IO.Path.GetDirectoryName( engineLocation ) );
while ( checkout is not null && !System.IO.File.Exists( System.IO.Path.Combine( checkout.FullName, "engine", "Sandbox-Engine.slnx" ) ) ) checkout = checkout.Parent;
if ( checkout is null ) throw new InvalidOperationException( "Cannot find checkout from loaded engine: " + engineLocation );
var runId = Guid.NewGuid().ToString( "N" );
var output = System.IO.Path.Combine( checkout.FullName, "Temp", "gltf-validation", "editor-direct-skinning-palette", runId );
System.IO.Directory.CreateDirectory( output );
var runnerPath = System.IO.Path.Combine( checkout.FullName, "research", "gltf-validation", "editor_direct_skinning_palette.csx" );
var testsPath = System.IO.Path.Combine( checkout.FullName, "engine", "Tests", "Sandbox.Test.Unit", "SceneRenderer", "DirectSkinningPaletteTests.cs" );
var scenesPath = System.IO.Path.Combine( checkout.FullName, "engine", "Tests", "Sandbox.Test.Unit", "SceneRenderer", "TestScenes.cs" );
var frameworkPath = System.IO.Path.Combine( checkout.FullName, "engine", "Tests", "Sandbox.Test.Gltf", "bin", "Release", "net10.0", "Microsoft.VisualStudio.TestPlatform.TestFramework.dll" );
var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static;
var tests = new System.Collections.Generic.List<object>();
var diagnostics = new System.Collections.Generic.List<string>();
var cleanupErrors = new System.Collections.Generic.List<string>();
var missingApi = new System.Collections.Generic.List<string>();
var phase = "initialize";
string error = null, runnerHash = null, testsHash = null, scenesHash = null, frameworkHash = null, helperMvid = null;
string engineDiskMvid = null, rendererDiskMvid = null, rendererLoadedMvid = null, rendererLocation = null;
var initialFrame = Application.FrameCount;
var expectedCases = 0; var passedCases = 0;
bool? unitTestMode = null;
System.Runtime.Loader.AssemblyLoadContext context = null;
string Hash( byte[] bytes ) => Convert.ToHexStringLower( System.Security.Cryptography.SHA256.HashData( bytes ) );
void Require( bool condition, string message ) { if ( !condition ) throw new InvalidOperationException( message ); }
byte[] ReadSource( string path )
{
	var info = new System.IO.FileInfo( path );
	Require( info.Exists && info.Length > 0 && info.Length <= 1024 * 1024, "Missing or oversized source: " + path );
	return System.IO.File.ReadAllBytes( path );
}
Guid DiskMvid( string path )
{
	using var stream = System.IO.File.OpenRead( path );
	using var pe = new System.Reflection.PortableExecutable.PEReader( stream );
	var metadata = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader( pe );
	return metadata.GetGuid( metadata.GetModuleDefinition().Mvid );
}
try
{
	await MainThread.Wait();
	Require( Application.IsEditor && !Graphics.IsActive, "Run in the existing editor on its main thread outside a graphics scope." );
	unitTestMode = Application.IsUnitTest;
	var runnerBytes = ReadSource( runnerPath ); var testBytes = ReadSource( testsPath ); var sceneBytes = ReadSource( scenesPath );
	runnerHash = Hash( runnerBytes ); testsHash = Hash( testBytes ); scenesHash = Hash( sceneBytes );
	var frameworkBytes = ReadSource( frameworkPath ); frameworkHash = Hash( frameworkBytes );
	phase = "identify-loaded-producers";
	engineDiskMvid = DiskMvid( engineLocation ).ToString();
	Require( engineDiskMvid == engineAssembly.ManifestModule.ModuleVersionId.ToString(), "Loaded engine module differs from disk." );
	var renderer = AppDomain.CurrentDomain.GetAssemblies().SingleOrDefault( assembly => !assembly.IsDynamic && assembly.GetName().Name == "Sandbox.SceneRenderer" );
	Require( renderer is not null, "Sandbox.SceneRenderer is not loaded in this editor." );
	rendererLocation = renderer.Location;
	rendererLoadedMvid = renderer.ManifestModule.ModuleVersionId.ToString();
	rendererDiskMvid = DiskMvid( rendererLocation ).ToString();
	Require( rendererDiskMvid == rendererLoadedMvid, "Loaded scene renderer module differs from disk." );
	phase = "check-loaded-direct-palette-api";
	var meshObject = renderer.GetType( "Sandbox.SceneRenderer.MeshObject", true );
	var set = meshObject.GetMethod( "SetSkinningPalette", flags, null, new[] { typeof( int ), typeof( ReadOnlySpan<Matrix3x4> ) }, null );
	var clear = meshObject.GetMethod( "ClearSkinningPalette", flags, null, new[] { typeof( int ) }, null );
	var get = meshObject.GetMethod( "SkinningPaletteFor", flags, null, new[] { typeof( int ) }, null );
	if ( set is null || set.ReturnType != typeof( void ) ) missingApi.Add( "void MeshObject.SetSkinningPalette(int, ReadOnlySpan<Matrix3x4>)" );
	if ( clear is null || clear.ReturnType != typeof( void ) ) missingApi.Add( "void MeshObject.ClearSkinningPalette(int)" );
	if ( get is null || get.ReturnType != typeof( ReadOnlySpan<Matrix3x4> ) ) missingApi.Add( "ReadOnlySpan<Matrix3x4> MeshObject.SkinningPaletteFor(int)" );
	Require( missingApi.Count == 0, "The loaded scene renderer lacks the required direct palette API: " + string.Join( ", ", missingApi ) );

	phase = "compile-tests-against-loaded-producer";
	var references = new System.Collections.Generic.Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
	foreach ( var assembly in AppDomain.CurrentDomain.GetAssemblies() )
	{
		try
		{
			if ( assembly.IsDynamic ) continue;
			var name = assembly.GetName().Name;
			if ( name.StartsWith( "sbox-mcp-script-", StringComparison.OrdinalIgnoreCase ) || name == "Sandbox.Test.Unit" || name == "Microsoft.VisualStudio.TestPlatform.TestFramework" ) continue;
			var file = assembly.Location;
			if ( string.IsNullOrWhiteSpace( file ) || !System.IO.File.Exists( file ) ) continue;
			if ( !references.ContainsKey( name ) ) references.Add( name, file );
		}
		catch ( Exception ex ) when ( ex is System.NotSupportedException || ex is System.IO.IOException || ex is BadImageFormatException || ex is UnauthorizedAccessException ) { }
	}
	// Explicit producer identities prevent an unrelated context's assembly from
	// winning the reference-name scan. No producer source is compiled here.
	references[engineAssembly.GetName().Name] = engineLocation;
	references[renderer.GetName().Name] = rendererLocation;
	references["Microsoft.VisualStudio.TestPlatform.TestFramework"] = frameworkPath;
	var parse = new Microsoft.CodeAnalysis.CSharp.CSharpParseOptions( Microsoft.CodeAnalysis.CSharp.LanguageVersion.Preview );
	var imports = "global using System; global using System.Linq; global using System.Threading.Tasks; global using Sandbox; global using Microsoft.VisualStudio.TestTools.UnitTesting; global using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;";
	var trees = new[]
	{
		Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText( imports, parse, path: "EditorTestGlobalUsings.cs" ),
		Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText( System.Text.Encoding.UTF8.GetString( sceneBytes ), parse, path: scenesPath ),
		Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText( System.Text.Encoding.UTF8.GetString( testBytes ), parse, path: testsPath )
	};
	var options = new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions( Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary, optimizationLevel: Microsoft.CodeAnalysis.OptimizationLevel.Release );
	// Existing Engine, System, and SceneRenderer IVT declarations grant access
	// to this exact unsigned test-assembly name, including in this load context.
	var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create( "Sandbox.Test.Unit", trees, references.Values.Select( file => Microsoft.CodeAnalysis.MetadataReference.CreateFromFile( file ) ), options );
	using var image = new System.IO.MemoryStream();
	var emitted = compilation.Emit( image, cancellationToken: cancellationToken );
	diagnostics.AddRange( emitted.Diagnostics.Select( item => item.ToString() ) );
	Require( emitted.Success, "Direct palette test helper did not compile against loaded producers; inspect diagnostics." );
	context = new System.Runtime.Loader.AssemblyLoadContext( "gltf-direct-palette-tests-" + runId, isCollectible: true );
	context.Resolving += ( loader, requested ) => requested.Name == "Microsoft.VisualStudio.TestPlatform.TestFramework"
		? loader.LoadFromAssemblyPath( frameworkPath )
		: AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault( assembly => !assembly.IsDynamic && System.Runtime.Loader.AssemblyLoadContext.GetLoadContext( assembly ) == System.Runtime.Loader.AssemblyLoadContext.Default && System.Reflection.AssemblyName.ReferenceMatchesDefinition( assembly.GetName(), requested ) );
	image.Position = 0;
	var testAssembly = context.LoadFromStream( image ); helperMvid = testAssembly.ManifestModule.ModuleVersionId.ToString();
	Require( testAssembly.GetName().Name == "Sandbox.Test.Unit", "Unexpected test assembly identity." );
	var testType = testAssembly.GetType( "SceneRendererTests.DirectSkinningPaletteTests", true );
	var allMethods = testType.GetMethods( flags );
	var methods = testType.GetMethods( System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public );
	string[] unsupported = ["DataTestMethodAttribute", "DynamicDataAttribute", "TestInitializeAttribute", "TestCleanupAttribute", "ClassInitializeAttribute", "ClassCleanupAttribute", "AssemblyInitializeAttribute", "AssemblyCleanupAttribute", "ExpectedExceptionAttribute", "IgnoreAttribute"];
	Require( !testType.GetCustomAttributesData().Any( attribute => unsupported.Contains( attribute.AttributeType.Name ) ) &&
		!allMethods.Any( method => method.GetCustomAttributesData().Any( attribute => unsupported.Contains( attribute.AttributeType.Name ) ) ), "Unsupported test attribute or lifecycle; no test may be silently skipped." );
	var cases = methods.Where( method => method.GetCustomAttributesData().Any( attribute => attribute.AttributeType.FullName == "Microsoft.VisualStudio.TestTools.UnitTesting.TestMethodAttribute" ) ).OrderBy( method => method.Name, StringComparer.Ordinal ).ToArray();
	Require( allMethods.Count( method => method.GetCustomAttributesData().Any( attribute => attribute.AttributeType.FullName == "Microsoft.VisualStudio.TestTools.UnitTesting.TestMethodAttribute" ) ) == cases.Length, "A static or non-public test would be skipped." );
	Require( cases.Length > 0, "No palette tests discovered." );
	phase = "run-cpu-tests-main-thread";
	foreach ( var test in cases )
	{
		cancellationToken.ThrowIfCancellationRequested();
		var rows = test.GetCustomAttributesData().Where( attribute => attribute.AttributeType.Name == "DataRowAttribute" ).ToArray();
		var parameters = test.GetParameters();
		Require( test.ReturnType == typeof( void ), "Only synchronous void tests are supported: " + test.Name );
		var arguments = new System.Collections.Generic.List<object[]>();
		if ( rows.Length == 0 )
		{
			Require( parameters.Length == 0, "A test with parameters has no data rows: " + test.Name );
			arguments.Add( Array.Empty<object>() );
		}
		else
		{
			Require( parameters.Length == 1 && parameters[0].ParameterType == typeof( int ), "Only the expected single-integer DataRows are supported: " + test.Name );
			foreach ( var row in rows )
			{
				Require( row.NamedArguments.Count == 0 && row.ConstructorArguments.Count == 1, "Unsupported DataRow shape: " + test.Name );
				var argument = row.ConstructorArguments[0];
				object value = argument.Value;
				if ( value is System.Collections.Generic.IReadOnlyCollection<System.Reflection.CustomAttributeTypedArgument> items )
				{
					Require( items.Count == 1, "Expected exactly one DataRow value: " + test.Name );
					value = items.Single().Value;
				}
				Require( value is int, "Expected an integer DataRow value: " + test.Name );
				arguments.Add( new[] { value } );
			}
		}
		foreach ( var args in arguments )
		{
			cancellationToken.ThrowIfCancellationRequested(); expectedCases++;
			var beforeFrame = Application.FrameCount; string failure = null;
			try { test.Invoke( Activator.CreateInstance( testType ), args ); }
			catch ( System.Reflection.TargetInvocationException ex ) { failure = (ex.InnerException ?? ex).ToString(); }
			catch ( Exception ex ) { failure = ex.ToString(); }
			var afterFrame = Application.FrameCount;
			if ( beforeFrame != afterFrame ) failure = (failure is null ? "" : failure + "\n") + $"Application.FrameCount changed during synchronous test: {beforeFrame} -> {afterFrame}.";
			tests.Add( new { name = test.Name, arguments = args, passed = failure is null, error = failure, before_frame = beforeFrame, after_frame = afterFrame } );
			if ( failure is null ) passedCases++;
		}
	}
	phase = "complete";
}
catch ( Exception ex ) { error = ex.ToString(); }
finally
{
	if ( context is not null ) try { context.Unload(); } catch ( Exception ex ) { cleanupErrors.Add( "unload test context: " + ex ); }
}
var receipt = new
{
	passed = error is null && cleanupErrors.Count == 0 && phase == "complete" && expectedCases > 0 && passedCases == expectedCases,
	phase, error, missing_api = missingApi, cleanup_errors = cleanupErrors,
	scope = "worktree CPU renderer tests and unchanged TestScenes compiled against the loaded engine and scene-renderer assemblies; no producer source, rendered pixels, GPU timing, native model, or glTF integration proof",
	expected_cases = expectedCases, passed_cases = passedCases, tests, diagnostics,
	runner_source_sha256 = runnerHash, test_source_sha256 = testsHash, test_scenes_source_sha256 = scenesHash, framework_sha256 = frameworkHash, helper_mvid = helperMvid,
	engine_location = engineLocation, engine_loaded_mvid = engineAssembly.ManifestModule.ModuleVersionId.ToString(), engine_disk_mvid = engineDiskMvid,
	renderer_location = rendererLocation, renderer_loaded_mvid = rendererLoadedMvid, renderer_disk_mvid = rendererDiskMvid,
	is_editor = Application.IsEditor, application_is_unit_test = unitTestMode, starting_frame = initialFrame, final_frame = Application.FrameCount, output
};
System.IO.File.WriteAllText( System.IO.Path.Combine( output, "receipt.json" ), System.Text.Json.JsonSerializer.Serialize( receipt, new System.Text.Json.JsonSerializerOptions { WriteIndented = true } ) );
return receipt;
