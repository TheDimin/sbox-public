// Run inside the existing s&box editor. Compile the worktree palette helper and
// its tests under one renamed type identifier. No engine DLL or scene is changed.
var engineLocation = typeof( Model ).Assembly.Location;
var checkout = new System.IO.DirectoryInfo( System.IO.Path.GetDirectoryName( engineLocation ) );
while ( checkout is not null && !System.IO.File.Exists( System.IO.Path.Combine( checkout.FullName, "engine", "Sandbox-Engine.slnx" ) ) ) checkout = checkout.Parent;
if ( checkout is null ) throw new InvalidOperationException( "Cannot find checkout from loaded engine: " + engineLocation );
var runId = Guid.NewGuid().ToString( "N" );
var output = System.IO.Path.Combine( checkout.FullName, "Temp", "gltf-validation", "editor-skinning-palette", runId );
System.IO.Directory.CreateDirectory( output );
var helperPath = System.IO.Path.Combine( checkout.FullName, "engine", "Sandbox.Gltf", "GltfSkinningPalette.cs" );
var testPath = System.IO.Path.Combine( checkout.FullName, "engine", "Tests", "Sandbox.Test.Gltf", "GltfSkinningPaletteTests.cs" );
var frameworkPath = System.IO.Path.Combine( checkout.FullName, "engine", "Tests", "Sandbox.Test.Gltf", "bin", "Release", "net10.0", "Microsoft.VisualStudio.TestPlatform.TestFramework.dll" );
var proofTypeName = "GltfSkinningPaletteProof" + runId;
var tests = new System.Collections.Generic.List<object>();
var diagnostics = new System.Collections.Generic.List<string>();
var phase = "initialize";
string error = null, helperHash = null, testHash = null, renamedHelperHash = null, renamedTestHash = null, helperMvid = null, diskGltfMvid = null;
var startingFrame = Application.FrameCount;
var expectedTests = 0;
var passedTests = 0;
System.Runtime.Loader.AssemblyLoadContext context = null;
string Hash( byte[] bytes ) => Convert.ToHexStringLower( System.Security.Cryptography.SHA256.HashData( bytes ) );
void Require( bool condition, string message ) { if ( !condition ) throw new InvalidOperationException( message ); }
byte[] ReadSource( string path )
{
	var info = new System.IO.FileInfo( path );
	Require( info.Exists && info.Length > 0 && info.Length <= 1024 * 1024, "Missing or oversized source: " + path );
	return System.IO.File.ReadAllBytes( path );
}
try
{
	await MainThread.Wait();
	Require( Application.IsEditor && !Graphics.IsActive, "Run in the editor on its main thread outside rendering." );
	var gltf = typeof( Sandbox.Gltf.GltfDocument ).Assembly;
	using ( var stream = System.IO.File.OpenRead( gltf.Location ) )
	using ( var pe = new System.Reflection.PortableExecutable.PEReader( stream ) )
	{
		var metadata = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader( pe );
		var disk = metadata.GetGuid( metadata.GetModuleDefinition().Mvid );
		diskGltfMvid = disk.ToString();
		Require( disk == gltf.ManifestModule.ModuleVersionId, "Loaded glTF module differs from disk; matching references are required." );
	}
	phase = "compile-worktree-source";
	var helperBytes = ReadSource( helperPath ); var testBytes = ReadSource( testPath );
	helperHash = Hash( helperBytes ); testHash = Hash( testBytes );
	string Rename( byte[] source ) => System.Text.RegularExpressions.Regex.Replace( System.Text.Encoding.UTF8.GetString( source ), @"\bGltfSkinningPalette\b", proofTypeName );
	var renamedHelper = Rename( helperBytes ); var renamedTests = Rename( testBytes );
	renamedHelperHash = Hash( System.Text.Encoding.UTF8.GetBytes( renamedHelper ) ); renamedTestHash = Hash( System.Text.Encoding.UTF8.GetBytes( renamedTests ) );
	Require( renamedHelper.Contains( proofTypeName, StringComparison.Ordinal ) && renamedTests.Contains( proofTypeName, StringComparison.Ordinal ), "Proof type rename failed." );
	Require( System.IO.File.Exists( frameworkPath ), "Existing MSTest framework DLL is missing." );
	var references = new System.Collections.Generic.Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
	foreach ( var assembly in AppDomain.CurrentDomain.GetAssemblies() )
	{
		try
		{
			if ( assembly.IsDynamic || assembly.GetName().Name.StartsWith( "sbox-mcp-script-", StringComparison.OrdinalIgnoreCase ) ) continue;
			var path = assembly.Location;
			if ( string.IsNullOrWhiteSpace( path ) || !System.IO.File.Exists( path ) ) continue;
			var name = System.Reflection.AssemblyName.GetAssemblyName( path ).Name;
			if ( name == "Microsoft.VisualStudio.TestPlatform.TestFramework" ) continue;
			if ( !references.ContainsKey( name ) ) references.Add( name, path );
		}
		catch ( Exception ex ) when ( ex is System.NotSupportedException || ex is System.IO.IOException || ex is BadImageFormatException || ex is UnauthorizedAccessException ) { }
	}
	references["Microsoft.VisualStudio.TestPlatform.TestFramework"] = frameworkPath;
	var parse = new Microsoft.CodeAnalysis.CSharp.CSharpParseOptions( Microsoft.CodeAnalysis.CSharp.LanguageVersion.Preview );
	var trees = new[] {
		Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText( "global using System; global using System.Collections.Generic; global using System.Linq; global using System.Threading; global using System.Threading.Tasks;", parse, path: "ImplicitUsings.cs" ),
		Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText( renamedHelper, parse, path: helperPath ),
		Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText( renamedTests, parse, path: testPath )
	};
	var nameForAssembly = "sbox-mcp-script-gltf-palette-" + runId;
	var options = new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions( Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary, optimizationLevel: Microsoft.CodeAnalysis.OptimizationLevel.Release );
	var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create( nameForAssembly, trees, references.Values.Select( path => Microsoft.CodeAnalysis.MetadataReference.CreateFromFile( path ) ), options );
	using var image = new System.IO.MemoryStream();
	cancellationToken.ThrowIfCancellationRequested();
	var result = compilation.Emit( image, cancellationToken: cancellationToken );
	diagnostics.AddRange( result.Diagnostics.Select( diagnostic => diagnostic.ToString() ) );
	Require( result.Success, "Palette helper/tests did not compile; inspect diagnostics." );
	context = new System.Runtime.Loader.AssemblyLoadContext( nameForAssembly, isCollectible: true );
	context.Resolving += ( loader, requested ) => requested.Name == "Microsoft.VisualStudio.TestPlatform.TestFramework"
		? loader.LoadFromAssemblyPath( frameworkPath )
		: AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault( assembly => !assembly.IsDynamic && System.Runtime.Loader.AssemblyLoadContext.GetLoadContext( assembly ) == System.Runtime.Loader.AssemblyLoadContext.Default && System.Reflection.AssemblyName.ReferenceMatchesDefinition( assembly.GetName(), requested ) );
	image.Position = 0;
	var helperAssembly = context.LoadFromStream( image ); helperMvid = helperAssembly.ManifestModule.ModuleVersionId.ToString();
	var testType = helperAssembly.GetType( "Sandbox.Tests.Gltf.GltfSkinningPaletteTests", true );
	var allMethods = testType.GetMethods( System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic );
	var methods = testType.GetMethods( System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public );
	Require( !allMethods.Any( method => method.GetCustomAttributesData().Any( attribute => attribute.AttributeType.Name is "DataTestMethodAttribute" or "TestInitializeAttribute" or "TestCleanupAttribute" or "ClassInitializeAttribute" or "ClassCleanupAttribute" ) ), "Unsupported test lifecycle or data method; do not silently skip it." );
	var cases = methods.Where( method => method.GetCustomAttributesData().Any( attribute => attribute.AttributeType.FullName == "Microsoft.VisualStudio.TestTools.UnitTesting.TestMethodAttribute" ) ).OrderBy( method => method.Name, StringComparer.Ordinal ).ToArray();
	Require( allMethods.Count( method => method.GetCustomAttributesData().Any( attribute => attribute.AttributeType.FullName == "Microsoft.VisualStudio.TestTools.UnitTesting.TestMethodAttribute" ) ) == cases.Length, "A private or static test would be skipped." );
	expectedTests = cases.Length; Require( expectedTests > 0, "No palette tests discovered." );
	phase = "run-worktree-tests";
	foreach ( var test in cases )
	{
		cancellationToken.ThrowIfCancellationRequested();
		Require( test.GetParameters().Length == 0 && test.ReturnType == typeof( void ) && !test.GetCustomAttributesData().Any( attribute => attribute.AttributeType.Name is "DataRowAttribute" or "DynamicDataAttribute" or "ExpectedExceptionAttribute" ), "Unsupported test shape: " + test.Name );
		string failure = null;
		try { test.Invoke( Activator.CreateInstance( testType ), null ); }
		catch ( System.Reflection.TargetInvocationException ex ) { failure = (ex.InnerException ?? ex).ToString(); }
		catch ( Exception ex ) { failure = ex.ToString(); }
		tests.Add( new { name = test.Name, passed = failure is null, error = failure } );
		if ( failure is null ) passedTests++;
	}
	phase = "complete";
}
catch ( Exception ex ) { error = ex.ToString(); }
finally { context?.Unload(); }
var receipt = new {
	passed = error is null && phase == "complete" && expectedTests > 0 && passedTests == expectedTests, phase, error,
	scope = "worktree CPU skinning palette helper and MSTest source compiled in the editor with one renamed type identifier; no native model, GPU, playback, compiled engine or performance proof",
	expected_tests = expectedTests, passed_tests = passedTests, tests, diagnostics,
	helper_source_sha256 = helperHash, test_source_sha256 = testHash, renamed_helper_sha256 = renamedHelperHash, renamed_test_sha256 = renamedTestHash, proof_type_name = proofTypeName,
	framework_sha256 = System.IO.File.Exists( frameworkPath ) ? Hash( System.IO.File.ReadAllBytes( frameworkPath ) ) : null, helper_mvid = helperMvid, disk_gltf_mvid = diskGltfMvid,
	engine_mvid = typeof( Model ).Assembly.ManifestModule.ModuleVersionId.ToString(), gltf_mvid = typeof( Sandbox.Gltf.GltfDocument ).Assembly.ManifestModule.ModuleVersionId.ToString(),
	is_editor = Application.IsEditor, starting_frame = startingFrame, final_frame = Application.FrameCount, output
};
System.IO.File.WriteAllText( System.IO.Path.Combine( output, "receipt.json" ), System.Text.Json.JsonSerializer.Serialize( receipt, new System.Text.Json.JsonSerializerOptions { WriteIndented = true } ) );
return receipt;
