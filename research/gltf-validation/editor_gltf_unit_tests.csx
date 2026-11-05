// Run inside the existing editor. Compile selected, self-contained MSTest sources
// against the actual loaded glTF library, without compiling production sources.
var engineLocation = typeof( Model ).Assembly.Location;
var checkout = new System.IO.DirectoryInfo( System.IO.Path.GetDirectoryName( engineLocation ) );
while ( checkout is not null && !System.IO.File.Exists( System.IO.Path.Combine( checkout.FullName, "engine", "Sandbox-Engine.slnx" ) ) ) checkout = checkout.Parent;
if ( checkout is null ) throw new InvalidOperationException( "Cannot locate checkout from " + engineLocation );
var selection = Environment.GetEnvironmentVariable( "SBOX_GLTF_UNIT_SUITE" );
if ( string.IsNullOrWhiteSpace( selection ) ) selection = "quantization-declarations";
// Versioned inventory: update deliberately when reviewed tests are added/removed.
var suites = new System.Collections.Generic.Dictionary<string, (string File, string Type, int Methods, int DataRows, int Cases)>( StringComparer.Ordinal ) {
	["quantization-declarations"] = ("GltfQuantizationDeclarationTests.cs", "Sandbox.Test.Gltf.GltfQuantizationDeclarationTests", 5, 10, 12),
	["basis-fallbacks"] = ("GltfBasisFallbackTests.cs", "Sandbox.Tests.Gltf.GltfBasisFallbackTests", 4, 4, 6),
	["primitive-reader"] = ("GltfPrimitiveTests.cs", "Sandbox.Tests.Gltf.GltfPrimitiveTests", 8, 11, 17),
	["animation-sampler"] = ("GltfAnimationTests.cs", "Sandbox.Test.Gltf.GltfAnimationTests", 8, 0, 8),
	["animation-channels"] = ("GltfAnimationChannelTests.cs", "Sandbox.Tests.Gltf.GltfAnimationChannelTests", 16, 6, 21)
};
var runId = Guid.NewGuid().ToString( "N" );
var output = System.IO.Path.Combine( checkout.FullName, "Temp", "gltf-validation", "editor-gltf-unit-tests", runId );
System.IO.Directory.CreateDirectory( output );
var frameworkPath = System.IO.Path.Combine( checkout.FullName, "engine", "Tests", "Sandbox.Test.Gltf", "bin", "Release", "net10.0", "Microsoft.VisualStudio.TestPlatform.TestFramework.dll" );
var hashes = new System.Collections.Generic.Dictionary<string, string>();
var tests = new System.Collections.Generic.List<object>();
var diagnostics = new System.Collections.Generic.List<string>();
var referenceNames = Array.Empty<string>();
var phase = "initialize";
string error = null, helperMvid = null, diskGltfMvid = null, frameworkHash = null;
var startingFrame = Application.FrameCount;
var expectedCases = 0; var passedCases = 0;
System.Runtime.Loader.AssemblyLoadContext context = null;
string Hash( byte[] bytes ) => Convert.ToHexStringLower( System.Security.Cryptography.SHA256.HashData( bytes ) );
void Require( bool condition, string message ) { if ( !condition ) throw new InvalidOperationException( message ); }
try
{
	await MainThread.Wait();
	Require( Application.IsEditor && !Graphics.IsActive, "Run in the editor on its main thread outside rendering." );
	Require( selection == "all" || suites.ContainsKey( selection ), "Unknown SBOX_GLTF_UNIT_SUITE: " + selection );
	var selected = selection == "all" ? suites.Keys.OrderBy( name => name, StringComparer.Ordinal ).ToArray() : new[] { selection };
	var gltf = typeof( Sandbox.Gltf.GltfReader ).Assembly;
	Require( System.Runtime.Loader.AssemblyLoadContext.GetLoadContext( gltf ) == System.Runtime.Loader.AssemblyLoadContext.Default, "This proof requires the engine's default-context glTF library." );
	using ( var stream = System.IO.File.OpenRead( gltf.Location ) )
	using ( var pe = new System.Reflection.PortableExecutable.PEReader( stream ) )
	{
		var metadata = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader( pe );
		var disk = metadata.GetGuid( metadata.GetModuleDefinition().Mvid );
		diskGltfMvid = disk.ToString();
		Require( disk == gltf.ManifestModule.ModuleVersionId, "The loaded glTF library differs from disk." );
	}
	phase = "compile-test-sources";
	Require( System.IO.File.Exists( frameworkPath ), "Existing MSTest framework DLL is missing." );
	frameworkHash = Hash( System.IO.File.ReadAllBytes( frameworkPath ) );
	var parse = new Microsoft.CodeAnalysis.CSharp.CSharpParseOptions( Microsoft.CodeAnalysis.CSharp.LanguageVersion.Preview );
	var trees = new System.Collections.Generic.List<Microsoft.CodeAnalysis.SyntaxTree> {
		Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText( "global using System; global using System.Collections.Generic; global using System.IO; global using System.Linq; global using System.Net.Http; global using System.Threading; global using System.Threading.Tasks;", parse, path: "ImplicitUsings.cs" )
	};
	foreach ( var suite in selected )
	{
		var file = suites[suite].File;
		var path = System.IO.Path.Combine( checkout.FullName, "engine", "Tests", "Sandbox.Test.Gltf", file );
		var info = new System.IO.FileInfo( path );
		Require( info.Exists && info.Length > 0 && info.Length <= 1024 * 1024, "Missing or oversized test source: " + path );
		var bytes = System.IO.File.ReadAllBytes( path ); hashes[file] = Hash( bytes );
		trees.Add( Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText( System.Text.Encoding.UTF8.GetString( bytes ), parse, path: path ) );
	}
	// Match the pure glTF test project's dependencies. Engine/editor/game types
	// are not test dependencies and must not change namespace/name resolution.
	var references = new System.Collections.Generic.Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
	foreach ( var assembly in AppDomain.CurrentDomain.GetAssemblies() )
	{
		try
		{
			if ( assembly.IsDynamic ) continue;
			var name = assembly.GetName().Name;
			if ( name != "mscorlib" && name != "netstandard" && name != "System" && !name.StartsWith( "System.", StringComparison.Ordinal ) ) continue;
			if ( string.IsNullOrWhiteSpace( assembly.Location ) || !System.IO.File.Exists( assembly.Location ) ) continue;
			if ( !references.ContainsKey( name ) ) references.Add( name, assembly.Location );
		}
		catch ( Exception ex ) when ( ex is System.NotSupportedException || ex is System.IO.IOException || ex is BadImageFormatException || ex is UnauthorizedAccessException ) { }
	}
	references[gltf.GetName().Name] = gltf.Location;
	references["Microsoft.VisualStudio.TestPlatform.TestFramework"] = frameworkPath;
	referenceNames = references.Keys.OrderBy( name => name, StringComparer.Ordinal ).ToArray();
	var helperName = "sbox-mcp-script-gltf-tests-" + runId;
	var options = new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions( Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary, optimizationLevel: Microsoft.CodeAnalysis.OptimizationLevel.Release );
	var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create( helperName, trees, references.Values.Select( path => Microsoft.CodeAnalysis.MetadataReference.CreateFromFile( path ) ), options );
	using var image = new System.IO.MemoryStream();
	var emitted = compilation.Emit( image, cancellationToken: cancellationToken );
	diagnostics.AddRange( emitted.Diagnostics.Select( item => item.ToString() ) );
	Require( emitted.Success, "Test sources did not compile against the loaded glTF library." );
	context = new System.Runtime.Loader.AssemblyLoadContext( helperName, isCollectible: true );
	context.Resolving += ( loader, requested ) => requested.Name == "Microsoft.VisualStudio.TestPlatform.TestFramework"
		? loader.LoadFromAssemblyPath( frameworkPath )
		: AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault( assembly => !assembly.IsDynamic && System.Runtime.Loader.AssemblyLoadContext.GetLoadContext( assembly ) == System.Runtime.Loader.AssemblyLoadContext.Default && System.Reflection.AssemblyName.ReferenceMatchesDefinition( assembly.GetName(), requested ) );
	image.Position = 0;
	var helper = context.LoadFromStream( image ); helperMvid = helper.ManifestModule.ModuleVersionId.ToString();
	var planned = new System.Collections.Generic.List<(string Suite, System.Reflection.MethodInfo Test, object[] Arguments)>();
	foreach ( var suite in selected )
	{
		var firstCase = planned.Count;
		var type = helper.GetType( suites[suite].Type, true );
		var all = type.GetMethods( System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static );
		string[] unsupported = ["DataTestMethodAttribute", "DynamicDataAttribute", "TestInitializeAttribute", "TestCleanupAttribute", "ClassInitializeAttribute", "ClassCleanupAttribute", "ExpectedExceptionAttribute", "IgnoreAttribute"];
		Require( !type.GetCustomAttributesData().Any( attribute => unsupported.Contains( attribute.AttributeType.Name ) ) && !all.Any( method => method.GetCustomAttributesData().Any( attribute => unsupported.Contains( attribute.AttributeType.Name ) ) ), "Unsupported test lifecycle/data: " + suite );
		var methods = all.Where( method => method.GetCustomAttributesData().Any( attribute => attribute.AttributeType.FullName == "Microsoft.VisualStudio.TestTools.UnitTesting.TestMethodAttribute" ) ).OrderBy( method => method.Name, StringComparer.Ordinal ).ToArray();
		var discoveredRows = methods.Sum( method => method.GetCustomAttributesData().Count( attribute => attribute.AttributeType.Name == "DataRowAttribute" ) );
		Require( methods.Length == suites[suite].Methods && discoveredRows == suites[suite].DataRows, "Test method/DataRow inventory differs from manifest version 1: " + suite );
		foreach ( var test in methods )
		{
			Require( test.IsPublic && !test.IsStatic && test.ReturnType == typeof( void ), "Unsupported test shape: " + test.Name );
			var rows = test.GetCustomAttributesData().Where( attribute => attribute.AttributeType.Name == "DataRowAttribute" ).ToArray();
			var parameters = test.GetParameters();
			if ( rows.Length == 0 )
			{
				Require( parameters.Length == 0, "Parameters without DataRows: " + test.Name );
				planned.Add( (suite, test, Array.Empty<object>()) );
			}
			foreach ( var row in rows )
			{
				Require( row.NamedArguments.Count == 0, "Unsupported named DataRow arguments: " + test.Name );
				var arguments = row.ConstructorArguments.Select( argument => argument.Value ).ToArray();
				if ( arguments.Length == 1 && arguments[0] is System.Collections.Generic.IReadOnlyCollection<System.Reflection.CustomAttributeTypedArgument> values ) arguments = values.Select( value => value.Value ).ToArray();
				Require( arguments.Length == parameters.Length, "DataRow argument count mismatch: " + test.Name );
				for ( var i = 0; i < arguments.Length; i++ ) Require( arguments[i] is not null && parameters[i].ParameterType.IsInstanceOfType( arguments[i] ), "Unsupported DataRow value: " + test.Name );
				planned.Add( (suite, test, arguments) );
			}
		}
		Require( planned.Count - firstCase == suites[suite].Cases, "Planned case count differs from manifest version 1: " + suite );
	}
	expectedCases = selected.Sum( suite => suites[suite].Cases );
	phase = "run-loaded-library-tests";
	foreach ( var item in planned )
	{
		cancellationToken.ThrowIfCancellationRequested();
		var before = Application.FrameCount; string failure = null;
		try { item.Test.Invoke( Activator.CreateInstance( item.Test.DeclaringType ), item.Arguments ); }
		catch ( System.Reflection.TargetInvocationException ex ) { failure = (ex.InnerException ?? ex).ToString(); }
		catch ( Exception ex ) { failure = ex.ToString(); }
		var after = Application.FrameCount;
		if ( before != after ) failure = (failure ?? "") + $" Synchronous test changed frame count: {before}->{after}.";
		tests.Add( new { suite = item.Suite, name = item.Test.Name, arguments = item.Arguments, passed = failure is null, error = failure, before_frame = before, after_frame = after } );
		if ( failure is null ) passedCases++;
	}
	phase = "complete";
}
catch ( Exception ex ) { error = ex.ToString(); }
finally { context?.Unload(); }
var receipt = new {
	passed = error is null && phase == "complete" && expectedCases > 0 && passedCases == expectedCases, selection, phase, error,
	scope = "allowlisted reader/animation MSTest sources executed in the editor against the actual loaded glTF library; not the complete glTF test project or Khronos corpus; no production source compilation, rendered appearance, GPU or performance proof",
	complete_gltf_suite = false, manifest_version = 1, available_suites = suites.Keys.OrderBy( name => name, StringComparer.Ordinal ).ToArray(),
	expected_cases = expectedCases, passed_cases = passedCases, tests, diagnostics, test_source_sha256 = hashes, framework_sha256 = frameworkHash, metadata_reference_names = referenceNames, helper_mvid = helperMvid,
	gltf_loaded_mvid = typeof( Sandbox.Gltf.GltfReader ).Assembly.ManifestModule.ModuleVersionId.ToString(), gltf_disk_mvid = diskGltfMvid, engine_mvid = typeof( Model ).Assembly.ManifestModule.ModuleVersionId.ToString(),
	is_editor = Application.IsEditor, starting_frame = startingFrame, final_frame = Application.FrameCount, output
};
System.IO.File.WriteAllText( System.IO.Path.Combine( output, "receipt.json" ), System.Text.Json.JsonSerializer.Serialize( receipt, new System.Text.Json.JsonSerializerOptions { WriteIndented = true } ) );
return receipt;
