// Run in the existing s&box editor. Compile manifest-selected test sources
// against its loaded glTF library; never compile or replace producer code.
// Native codec preflights and project cases are recorded separately.
var engineLocation = typeof( Model ).Assembly.Location;
var checkout = new System.IO.DirectoryInfo( System.IO.Path.GetDirectoryName( engineLocation ) );
while ( checkout is not null && !System.IO.File.Exists( System.IO.Path.Combine( checkout.FullName, "engine", "Sandbox-Engine.slnx" ) ) ) checkout = checkout.Parent;
if ( checkout is null ) throw new InvalidOperationException( "Cannot locate checkout from " + engineLocation );
var selection = Environment.GetEnvironmentVariable( "SBOX_GLTF_UNIT_SUITE" );
var selectionSource = string.IsNullOrWhiteSpace( selection ) ? "default" : "environment";
if ( selectionSource == "default" ) selection = "quantization-declarations";
var runId = Guid.NewGuid().ToString( "N" );
var output = System.IO.Path.Combine( checkout.FullName, "Temp", "gltf-validation", "editor-gltf-unit-tests", runId );
System.IO.Directory.CreateDirectory( output );
var receiptPath = System.IO.Path.Combine( output, "receipt.json" );
var stageRoot = System.IO.Path.Combine( checkout.FullName, "Temp/gltf-validation/staged-build/f9fd58602bbf47a6ac1bfa51f0d07845" );
var testRoot = System.IO.Path.Combine( stageRoot, "pure-test-source-snapshot" );
var manifestPath = System.IO.Path.Combine( testRoot, "gltf-test-manifest.json" );
var runnerPath = System.IO.Path.Combine( checkout.FullName, "engine", "Tools", "Gltf", "editor", "editor_gltf_unit_tests.csx" );
var requestPath = System.IO.Path.Combine( exchangeDirectory, "gltf-unit-request.json" );
string requestHash = null, requestId = null;
System.Text.Json.JsonElement? requestValues = null;
var frameworkPath = System.IO.Path.Combine( checkout.FullName, "engine", "Tests", "Sandbox.Test.Gltf", "bin", "Release", "net10.0", "Microsoft.VisualStudio.TestPlatform.TestFramework.dll" );
var suites = new System.Collections.Generic.Dictionary<string, (string File, string Type, int Methods, int Rows, int Cases, string[] Sources, string[] Resources, string[] Codecs)>( StringComparer.Ordinal );
var codecs = new System.Collections.Generic.Dictionary<string, (string File, string Type, string Method, object[] Args, string[] Sources, string[] Resources)>( StringComparer.Ordinal );
var inputHashes = new System.Collections.Generic.Dictionary<string, string>( StringComparer.Ordinal );
var resourceHashes = new System.Collections.Generic.Dictionary<string, string>( StringComparer.Ordinal );
var tests = new System.Collections.Generic.List<object>();
var preflightResults = new System.Collections.Generic.List<object>();
var availability = new System.Collections.Generic.Dictionary<string, bool>( StringComparer.Ordinal );
var diagnostics = new System.Collections.Generic.List<string>();
var cleanupErrors = new System.Collections.Generic.List<string>();
var referencesUsed = Array.Empty<string>();
var availableSuites = Array.Empty<string>();
var phase = "initialize";
string error = null, helperMvid = null, gltfDiskMvid = null, gltfHash = null, manifestHash = null, frameworkHash = null, runnerHash = null;
string frameworkExpectedMvid = null, frameworkLoadedMvid = null, frameworkLoadedLocation = null, frameworkLoadedContext = null, frameworkLoadedHash = null;
int expectedCases = 0, passedCases = 0, failedCases = 0, blockedCases = 0;
var initialFrame = Application.FrameCount;
System.Runtime.Loader.AssemblyLoadContext context = null;
string Hash( byte[] bytes ) => Convert.ToHexStringLower( System.Security.Cryptography.SHA256.HashData( bytes ) );
void Require( bool condition, string message ) { if ( !condition ) throw new InvalidOperationException( message ); }
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
string TopLevelSource( string file )
{
	Require( !string.IsNullOrEmpty( file ) && file == System.IO.Path.GetFileName( file ) && file.EndsWith( ".cs", StringComparison.Ordinal ), "Invalid test source filename: " + file );
	return System.IO.Path.Combine( testRoot, file );
}
string ResourcePath( string relative )
{
	Require( !string.IsNullOrWhiteSpace( relative ) && !System.IO.Path.IsPathRooted( relative ) && !relative.Contains( '\\' ), "Invalid resource path: " + relative );
	var root = System.IO.Path.GetFullPath( testRoot ) + System.IO.Path.DirectorySeparatorChar;
	var full = System.IO.Path.GetFullPath( System.IO.Path.Combine( testRoot, relative ) );
	Require( full.StartsWith( root, StringComparison.Ordinal ), "Resource escapes test project: " + relative );
	return full;
}
string[] StringArray( System.Text.Json.JsonElement parent, string property ) => parent.GetProperty( property ).EnumerateArray().Select( item => item.GetString() ?? throw new InvalidOperationException( "Null manifest string: " + property ) ).ToArray();
object JsonArgument( System.Text.Json.JsonElement value ) => value.ValueKind switch
{
	System.Text.Json.JsonValueKind.String => value.GetString(),
	System.Text.Json.JsonValueKind.Number when value.TryGetInt32( out var number ) => number,
	System.Text.Json.JsonValueKind.True => true,
	System.Text.Json.JsonValueKind.False => false,
	_ => throw new InvalidOperationException( "Unsupported manifest preflight argument." )
};
object Unwrap( System.Reflection.CustomAttributeTypedArgument argument )
{
	if ( argument.Value is System.Collections.Generic.IReadOnlyCollection<System.Reflection.CustomAttributeTypedArgument> list ) return list.Select( Unwrap ).ToArray();
	return argument.Value;
}
bool IsTest( System.Reflection.MethodInfo method ) => method.GetCustomAttributesData().Any( attribute => attribute.AttributeType.FullName is "Microsoft.VisualStudio.TestTools.UnitTesting.TestMethodAttribute" or "Microsoft.VisualStudio.TestTools.UnitTesting.DataTestMethodAttribute" );
System.Collections.Generic.List<object[]> ArgumentsFor( System.Reflection.MethodInfo method )
{
	Require( method.IsPublic && !method.IsStatic && method.ReturnType == typeof( void ), "Unsupported test shape: " + method.Name );
	var attributes = method.GetCustomAttributesData();
	Require( attributes.Count( a => a.AttributeType.FullName is "Microsoft.VisualStudio.TestTools.UnitTesting.TestMethodAttribute" or "Microsoft.VisualStudio.TestTools.UnitTesting.DataTestMethodAttribute" ) == 1, "Ambiguous test method attribute: " + method.Name );
	var rows = attributes.Where( attribute => attribute.AttributeType.Name == "DataRowAttribute" ).ToArray();
	var parameters = method.GetParameters();
	var result = new System.Collections.Generic.List<object[]>();
	if ( rows.Length == 0 )
	{
		Require( parameters.Length == 0 && !attributes.Any( a => a.AttributeType.Name == "DataTestMethodAttribute" ), "Test requires explicit DataRows: " + method.Name );
		result.Add( Array.Empty<object>() );
	}
	foreach ( var row in rows )
	{
		Require( row.NamedArguments.Count == 0, "Named DataRow arguments are unsupported: " + method.Name );
		var values = row.ConstructorArguments.Select( Unwrap ).ToArray();
		if ( values.Length == 1 && values[0] is object[] array ) values = array;
		Require( values.Length == parameters.Length, "DataRow argument count mismatch: " + method.Name );
		for ( var i = 0; i < values.Length; i++ )
		{
			Require( values[i] is string or int or uint or byte or bool or float, "Unsupported DataRow value: " + method.Name );
			Require( parameters[i].ParameterType == values[i].GetType(), "DataRow type mismatch: " + method.Name );
		}
		result.Add( values );
	}
	return result;
}
try
{
	await MainThread.Wait();
	Require( Application.IsEditor && !Graphics.IsActive, "Run on the existing editor's main thread outside rendering." );
	phase = "read-and-check-manifest";
	var runnerBytes = ReadBounded( runnerPath ); runnerHash = Hash( runnerBytes );
	var manifestBytes = ReadBounded( manifestPath ); manifestHash = Hash( manifestBytes );
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
		Require( value.ValueKind == System.Text.Json.JsonValueKind.Object, "Selection request must be a JSON object." );
		var fields = new System.Collections.Generic.HashSet<string>( StringComparer.Ordinal );
		foreach ( var field in value.EnumerateObject() ) Require( fields.Add( field.Name ), "Duplicate selection request property: " + field.Name );
		Require( fields.SetEquals( new[] { "version", "request_id", "suite", "manifest_sha256" } ), "Selection request requires exactly version, request_id, suite and manifest_sha256." );
		Require( value.GetProperty( "version" ).ValueKind == System.Text.Json.JsonValueKind.Number && value.GetProperty( "version" ).TryGetInt32( out var version ) && version == 1, "Expected selection request version 1." );
		Require( new[] { "request_id", "suite", "manifest_sha256" }.All( name => value.GetProperty( name ).ValueKind == System.Text.Json.JsonValueKind.String ), "Selection request id, suite and manifest hash must be strings." );
		requestId = value.GetProperty( "request_id" ).GetString();
		var requestedSuite = value.GetProperty( "suite" ).GetString();
		var requestedManifest = value.GetProperty( "manifest_sha256" ).GetString();
		Require( IsLowerHex( requestId, 32 ), "Selection request_id must contain 32 lowercase hexadecimal characters." );
		Require( IsLowerHex( requestedManifest, 64 ) && requestedManifest == manifestHash, "Selection request manifest_sha256 does not match the manifest input." );
		Require( !string.IsNullOrWhiteSpace( requestedSuite ) && requestedSuite.Length <= 128, "Selection request suite is empty or oversized." );
		selection = requestedSuite;
	}
	phase = "read-and-check-manifest";
	using var manifest = System.Text.Json.JsonDocument.Parse( manifestBytes );
	var root = manifest.RootElement;
	Require( root.GetProperty( "version" ).GetInt32() == 7, "Expected manifest version 7." );
	foreach ( var entry in root.GetProperty( "suites" ).EnumerateArray() )
	{
		var name = entry.GetProperty( "name" ).GetString(); var file = entry.GetProperty( "file" ).GetString(); var type = entry.GetProperty( "type" ).GetString();
		Require( !string.IsNullOrWhiteSpace( name ) && !string.IsNullOrWhiteSpace( type ), "A suite has no name or type." );
		_ = TopLevelSource( file );
		Require( suites.TryAdd( name, (file, type, entry.GetProperty( "methods" ).GetInt32(), entry.GetProperty( "data_rows" ).GetInt32(), entry.GetProperty( "cases" ).GetInt32(), StringArray( entry, "source_dependencies" ), StringArray( entry, "resources" ), StringArray( entry, "codecs" )) ), "Duplicate suite alias: " + name );
	}
	foreach ( var entry in root.GetProperty( "codec_preflights" ).EnumerateArray() )
	{
		var name = entry.GetProperty( "name" ).GetString(); var file = entry.GetProperty( "file" ).GetString(); var type = entry.GetProperty( "type" ).GetString(); var method = entry.GetProperty( "method" ).GetString();
		Require( !string.IsNullOrWhiteSpace( name ) && !string.IsNullOrWhiteSpace( type ) && !string.IsNullOrWhiteSpace( method ), "Invalid codec preflight." );
		_ = TopLevelSource( file );
		Require( codecs.TryAdd( name, (file, type, method, entry.GetProperty( "arguments" ).EnumerateArray().Select( JsonArgument ).ToArray(), StringArray( entry, "source_dependencies" ), StringArray( entry, "resources" )) ), "Duplicate codec preflight: " + name );
	}
	var sourceNames = suites.Values.Select( suite => suite.File ).ToArray();
	var typeNames = suites.Values.Select( suite => suite.Type ).ToArray();
	Require( sourceNames.Distinct( StringComparer.Ordinal ).Count() == suites.Count && typeNames.Distinct( StringComparer.Ordinal ).Count() == suites.Count, "Duplicate source file or test type in manifest." );
	var diskNames = System.IO.Directory.EnumerateFiles( testRoot, "*.cs", System.IO.SearchOption.TopDirectoryOnly ).Select( System.IO.Path.GetFileName ).ToHashSet( StringComparer.Ordinal );
	Require( diskNames.SetEquals( sourceNames ), "Top-level glTF test sources differ from the manifest: missing=" + string.Join( ",", sourceNames.Except( diskNames ) ) + "; unlisted=" + string.Join( ",", diskNames.Except( sourceNames ) ) );
	Require( suites.Count == root.GetProperty( "source_class_count" ).GetInt32() && suites.Values.Sum( x => x.Methods ) == root.GetProperty( "test_method_count" ).GetInt32() && suites.Values.Sum( x => x.Rows ) == root.GetProperty( "data_row_count" ).GetInt32() && suites.Values.Sum( x => x.Cases ) == root.GetProperty( "expanded_case_count" ).GetInt32(), "Manifest totals do not equal suite entries." );
	Require( suites.Count == 44 && suites.Values.Sum( x => x.Methods ) == 432 && suites.Values.Sum( x => x.Rows ) == 432 && suites.Values.Sum( x => x.Cases ) == 764, "Version 7 inventory changed; review the manifest." );
	foreach ( var suite in suites.Values )
	{
		Require( suite.Methods > 0 && suite.Rows >= 0 && suite.Cases >= suite.Methods, "Invalid suite counts." );
		foreach ( var dependency in suite.Sources ) Require( diskNames.Contains( dependency ), "Missing source dependency: " + dependency );
		foreach ( var codec in suite.Codecs ) Require( codecs.ContainsKey( codec ), "Unknown codec: " + codec );
		foreach ( var resource in suite.Resources ) _ = ResourcePath( resource );
	}
	foreach ( var codec in codecs.Values )
	{
		Require( diskNames.Contains( codec.File ), "Codec preflight source is not inventoried: " + codec.File );
		foreach ( var dependency in codec.Sources ) Require( diskNames.Contains( dependency ), "Missing codec source dependency: " + dependency );
		foreach ( var resource in codec.Resources ) _ = ResourcePath( resource );
	}
	availableSuites = suites.Keys.OrderBy( name => name, StringComparer.Ordinal ).ToArray();
	Require( selection == "all" || suites.ContainsKey( selection ), "Unknown glTF unit suite from " + selectionSource + ": " + selection );
	var selected = selection == "all" ? availableSuites : new[] { selection };
	expectedCases = selected.Sum( name => suites[name].Cases );
	var requiredCodecs = selected.SelectMany( name => suites[name].Codecs ).Distinct( StringComparer.Ordinal ).OrderBy( x => x, StringComparer.Ordinal ).ToArray();
	var sources = selected.SelectMany( name => new[] { suites[name].File }.Concat( suites[name].Sources ) )
		.Concat( requiredCodecs.SelectMany( name => new[] { codecs[name].File }.Concat( codecs[name].Sources ) ) ).Distinct( StringComparer.Ordinal ).OrderBy( x => x, StringComparer.Ordinal ).ToArray();
	var resourceNames = selected.SelectMany( name => suites[name].Resources ).Concat( requiredCodecs.SelectMany( name => codecs[name].Resources ) ).Distinct( StringComparer.Ordinal ).OrderBy( x => x, StringComparer.Ordinal ).ToArray();

	phase = "verify-loaded-library";
	var gltf = typeof( Sandbox.Gltf.GltfReader ).Assembly;
	Require( System.Runtime.Loader.AssemblyLoadContext.GetLoadContext( gltf ) == System.Runtime.Loader.AssemblyLoadContext.Default, "The loaded glTF library is not in the default context." );
	using ( var stream = System.IO.File.OpenRead( gltf.Location ) )
	using ( var pe = new System.Reflection.PortableExecutable.PEReader( stream ) )
	{
		var metadata = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader( pe );
		var disk = metadata.GetGuid( metadata.GetModuleDefinition().Mvid ); gltfDiskMvid = disk.ToString();
		Require( disk == gltf.ManifestModule.ModuleVersionId, "Loaded glTF library differs from disk." );
	}
	gltfHash = Hash( ReadBounded( gltf.Location, 128L * 1024 * 1024 ) );
	using ( var frozen = System.Text.Json.JsonDocument.Parse( ReadBounded( System.IO.Path.Combine( stageRoot, "source-snapshot-manifest.json" ), 4L * 1024 * 1024 ) ) )
		Require( frozen.RootElement.GetProperty( "gltf_sha256" ).GetString() == gltfHash, "Frozen pure tests belong to another producer." );
	frameworkHash = Hash( ReadBounded( frameworkPath ) );
	phase = "compile-test-sources";
	var parse = new Microsoft.CodeAnalysis.CSharp.CSharpParseOptions( Microsoft.CodeAnalysis.CSharp.LanguageVersion.Preview );
	var trees = new System.Collections.Generic.List<Microsoft.CodeAnalysis.SyntaxTree> {
		Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText( "global using System; global using System.Collections.Generic; global using System.IO; global using System.Linq; global using System.Net.Http; global using System.Threading; global using System.Threading.Tasks;", parse, path: "ImplicitUsings.cs" )
	};
	foreach ( var file in sources )
	{
		var path = TopLevelSource( file ); var bytes = ReadBounded( path ); inputHashes[file] = Hash( bytes );
		trees.Add( Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText( System.Text.Encoding.UTF8.GetString( bytes ), parse, path: path ) );
	}
	var resourceDescriptions = new System.Collections.Generic.List<Microsoft.CodeAnalysis.ResourceDescription>();
	var suffixes = new System.Collections.Generic.HashSet<string>( StringComparer.Ordinal );
	foreach ( var name in resourceNames )
	{
		var bytes = ReadBounded( ResourcePath( name ) ); resourceHashes[name] = Hash( bytes );
		Require( suffixes.Add( System.IO.Path.GetFileName( name ) ), "Ambiguous embedded-resource suffix: " + name );
		resourceDescriptions.Add( new Microsoft.CodeAnalysis.ResourceDescription( "Sandbox.Test.Gltf." + name.Replace( '/', '.' ), () => new System.IO.MemoryStream( bytes, writable: false ), true ) );
	}
	var references = new System.Collections.Generic.Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
	foreach ( var assembly in AppDomain.CurrentDomain.GetAssemblies() )
	{
		try
		{
			if ( assembly.IsDynamic || System.Runtime.Loader.AssemblyLoadContext.GetLoadContext( assembly ) != System.Runtime.Loader.AssemblyLoadContext.Default ) continue;
			var name = assembly.GetName().Name;
			if ( name != "mscorlib" && name != "netstandard" && name != "System" && !name.StartsWith( "System.", StringComparison.Ordinal ) ) continue;
			if ( string.IsNullOrWhiteSpace( assembly.Location ) || !System.IO.File.Exists( assembly.Location ) ) continue;
			if ( !references.ContainsKey( name ) ) references.Add( name, assembly.Location );
		}
		catch ( Exception ex ) when ( ex is System.NotSupportedException || ex is System.IO.IOException || ex is BadImageFormatException || ex is UnauthorizedAccessException ) { }
	}
	references[gltf.GetName().Name] = gltf.Location; references["Microsoft.VisualStudio.TestPlatform.TestFramework"] = frameworkPath;
	referencesUsed = references.Keys.OrderBy( name => name, StringComparer.Ordinal ).ToArray();
	var helperName = "sbox-mcp-script-gltf-tests-" + runId;
	var options = new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions( Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary, optimizationLevel: Microsoft.CodeAnalysis.OptimizationLevel.Release );
	var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create( helperName, trees, references.Values.Select( path => Microsoft.CodeAnalysis.MetadataReference.CreateFromFile( path ) ), options );
	using var image = new System.IO.MemoryStream();
	var emitted = compilation.Emit( image, manifestResources: resourceDescriptions, cancellationToken: cancellationToken );
	diagnostics.AddRange( emitted.Diagnostics.Select( item => item.ToString() ) );
	Require( emitted.Success, "Test sources did not compile against the loaded glTF library." );
	context = new System.Runtime.Loader.AssemblyLoadContext( helperName, isCollectible: true );
	context.Resolving += ( loader, requested ) => requested.Name == "Microsoft.VisualStudio.TestPlatform.TestFramework" ? loader.LoadFromAssemblyPath( frameworkPath ) :
		AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault( assembly => !assembly.IsDynamic && System.Runtime.Loader.AssemblyLoadContext.GetLoadContext( assembly ) == System.Runtime.Loader.AssemblyLoadContext.Default && System.Reflection.AssemblyName.ReferenceMatchesDefinition( assembly.GetName(), requested ) );
	image.Position = 0; var helper = context.LoadFromStream( image ); helperMvid = helper.ManifestModule.ModuleVersionId.ToString();
	foreach ( var name in resourceNames ) Require( helper.GetManifestResourceNames().Count( x => x.EndsWith( System.IO.Path.GetFileName( name ), StringComparison.Ordinal ) ) == 1, "Expected one embedded fixture: " + name );

	phase = "verify-resolved-framework";
	using ( var stream = System.IO.File.OpenRead( frameworkPath ) )
	using ( var pe = new System.Reflection.PortableExecutable.PEReader( stream ) )
	{
		var metadata = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader( pe );
		frameworkExpectedMvid = metadata.GetGuid( metadata.GetModuleDefinition().Mvid ).ToString();
	}
	var frameworkAssemblies = new System.Collections.Generic.HashSet<System.Reflection.Assembly>();
	foreach ( var name in selected )
	{
		var testType = helper.GetType( suites[name].Type, true );
		var classAttributes = testType.GetCustomAttributesData().Where( a => a.AttributeType.FullName == "Microsoft.VisualStudio.TestTools.UnitTesting.TestClassAttribute" ).ToArray();
		Require( classAttributes.Length == 1, "Expected one TestClass attribute: " + name );
		frameworkAssemblies.Add( classAttributes[0].AttributeType.Assembly );
		var methodAttributes = testType.GetMethods( System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static )
			.SelectMany( method => method.GetCustomAttributesData() )
			.Where( a => a.AttributeType.FullName is "Microsoft.VisualStudio.TestTools.UnitTesting.TestMethodAttribute" or "Microsoft.VisualStudio.TestTools.UnitTesting.DataTestMethodAttribute" ).ToArray();
		Require( methodAttributes.Length > 0, "No TestMethod attributes resolved: " + name );
		foreach ( var attribute in methodAttributes ) frameworkAssemblies.Add( attribute.AttributeType.Assembly );
	}
	Require( frameworkAssemblies.Count == 1, "Test attributes resolved from multiple framework assemblies." );
	var actualFramework = frameworkAssemblies.Single();
	frameworkLoadedMvid = actualFramework.ManifestModule.ModuleVersionId.ToString();
	frameworkLoadedLocation = actualFramework.Location;
	frameworkLoadedContext = System.Runtime.Loader.AssemblyLoadContext.GetLoadContext( actualFramework )?.Name ?? "<unnamed>";
	Require( !string.IsNullOrWhiteSpace( frameworkLoadedLocation ), "Resolved test framework has no disk location for identity verification." );
	frameworkLoadedHash = Hash( ReadBounded( frameworkLoadedLocation ) );
	Require( frameworkHash == Hash( ReadBounded( frameworkPath ) ) && frameworkLoadedHash == frameworkHash && frameworkLoadedMvid == frameworkExpectedMvid,
		"Resolved test framework differs from the expected framework binary or it changed during compilation." );

	phase = "discover-and-verify-tests";
	string[] unsupported = ["DynamicDataAttribute", "TestInitializeAttribute", "TestCleanupAttribute", "ClassInitializeAttribute", "ClassCleanupAttribute", "AssemblyInitializeAttribute", "AssemblyCleanupAttribute", "ExpectedExceptionAttribute", "IgnoreAttribute"];
	var planned = new System.Collections.Generic.List<(string Suite, System.Reflection.MethodInfo Test, object[] Arguments)>();
	foreach ( var name in selected )
	{
		var first = planned.Count; var suite = suites[name]; var type = helper.GetType( suite.Type, true );
		var methods = type.GetMethods( System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static );
		Require( !type.GetCustomAttributesData().Any( attribute => unsupported.Contains( attribute.AttributeType.Name ) ) && !methods.Any( method => method.GetCustomAttributesData().Any( attribute => unsupported.Contains( attribute.AttributeType.Name ) ) ), "Unsupported test lifecycle/data: " + name );
		var testMethods = methods.Where( IsTest ).OrderBy( method => method.Name, StringComparer.Ordinal ).ToArray();
		Require( testMethods.Length == suite.Methods && testMethods.Sum( method => method.GetCustomAttributesData().Count( attribute => attribute.AttributeType.Name == "DataRowAttribute" ) ) == suite.Rows, "Test method/DataRow counts differ from manifest: " + name );
		foreach ( var method in testMethods ) foreach ( var args in ArgumentsFor( method ) ) planned.Add( (name, method, args) );
		Require( planned.Count - first == suite.Cases, "Expanded case count differs from manifest: " + name );
	}
	Require( planned.Count == expectedCases, "Selected case count differs from manifest." );

	phase = "codec-preflights";
	foreach ( var name in requiredCodecs )
	{
		cancellationToken.ThrowIfCancellationRequested(); await Task.Delay( 1, cancellationToken ); await MainThread.Wait();
		Require( Application.IsEditor && !Graphics.IsActive, "Preflight must run outside rendering." );
		var spec = codecs[name];
		var before = Application.FrameCount; string failure = null;
		try
		{
			var type = helper.GetType( spec.Type, true );
			var method = type.GetMethod( spec.Method, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance ) ?? throw new InvalidOperationException( "Missing codec preflight method: " + name );
			Require( IsTest( method ), "Codec preflight is not an existing test: " + name );
			var preflightArgs = ArgumentsFor( method );
			Require( preflightArgs.Any( args => args.Length == spec.Args.Length && args.Zip( spec.Args, ( a, b ) => a?.GetType() == b?.GetType() && Equals( a, b ) ).All( x => x ) ), "Codec preflight arguments do not match a test row: " + name );
			method.Invoke( Activator.CreateInstance( type ), spec.Args );
		}
		catch ( System.Reflection.TargetInvocationException ex ) { failure = (ex.InnerException ?? ex).ToString(); }
		catch ( Exception ex ) { failure = ex.ToString(); }
		var after = Application.FrameCount;
		if ( before != after ) failure = (failure ?? "") + $" Synchronous preflight changed frame count: {before}->{after}.";
		availability[name] = failure is null;
		preflightResults.Add( new { codec = name, test = spec.Type + "." + spec.Method, arguments = spec.Args, argument_types = spec.Args.Select( argument => argument.GetType().FullName ).ToArray(), passed = failure is null, error = failure, before_frame = before, after_frame = after } );
	}

	phase = "run-loaded-library-tests";
	foreach ( var item in planned )
	{
		cancellationToken.ThrowIfCancellationRequested();
		var unavailable = suites[item.Suite].Codecs.Where( name => !availability.TryGetValue( name, out var available ) || !available ).ToArray();
		if ( unavailable.Length > 0 )
		{
			blockedCases++;
			tests.Add( new { suite = item.Suite, name = item.Test.Name, arguments = item.Arguments, argument_types = item.Arguments.Select( argument => argument.GetType().FullName ).ToArray(), status = "blocked", error = "Codec preflight failed: " + string.Join( ", ", unavailable ), before_frame = (ulong?)null, after_frame = (ulong?)null } );
			continue;
		}
		await Task.Delay( 1, cancellationToken ); await MainThread.Wait();
		Require( Application.IsEditor && !Graphics.IsActive, "Test must run outside rendering." );
		var before = Application.FrameCount; string failure = null;
		try { item.Test.Invoke( Activator.CreateInstance( item.Test.DeclaringType ), item.Arguments ); }
		catch ( System.Reflection.TargetInvocationException ex ) { failure = (ex.InnerException ?? ex).ToString(); }
		catch ( Exception ex ) { failure = ex.ToString(); }
		var after = Application.FrameCount;
		if ( before != after ) failure = (failure ?? "") + $" Synchronous test changed frame count: {before}->{after}.";
		if ( failure is null ) passedCases++; else failedCases++;
		tests.Add( new { suite = item.Suite, name = item.Test.Name, arguments = item.Arguments, argument_types = item.Arguments.Select( argument => argument.GetType().FullName ).ToArray(), status = failure is null ? "passed" : "failed", error = failure, before_frame = (ulong?)before, after_frame = (ulong?)after } );
	}
	phase = "complete";
}
catch ( OperationCanceledException ex ) { error = "Cancelled: " + ex; }
catch ( Exception ex ) { error = ex.ToString(); }
finally
{
	if ( context is not null ) try { context.Unload(); } catch ( Exception ex ) { cleanupErrors.Add( "Unload test context: " + ex ); }
}
var partialCases = Math.Max( 0, expectedCases - passedCases - failedCases - blockedCases );
var selectedPassed = error is null && cleanupErrors.Count == 0 && phase == "complete" && expectedCases > 0 && passedCases == expectedCases && failedCases == 0 && blockedCases == 0 && partialCases == 0;
var allProjectCasesPassed = selection == "all" && selectedPassed && expectedCases == 764;
var receipt = new
{
	passed = selectedPassed, selection, selection_source = selectionSource, request_path = requestPath, request_sha256 = requestHash, request_id = requestId, request_values = requestValues, phase, error, scope = "Manifest-selected Sandbox.Test.Gltf cases against the editor's loaded glTF library; does not prove Khronos corpus, complete glTF specification, GPU, editor features or performance.",
	complete_gltf_suite = false, all_project_cases_passed = allProjectCasesPassed, manifest_version = 7, available_suites = availableSuites,
	expected_cases = expectedCases, passed_cases = passedCases, failed_cases = failedCases, blocked_cases = blockedCases, partial_cases = partialCases,
	preflights = preflightResults, tests, diagnostics, cleanup_errors = cleanupErrors, test_source_sha256 = inputHashes, resource_sha256 = resourceHashes, manifest_sha256 = manifestHash, runner_source_sha256 = runnerHash, framework_sha256 = frameworkHash, gltf_binary_sha256 = gltfHash,
	metadata_reference_names = referencesUsed, helper_mvid = helperMvid, gltf_loaded_mvid = typeof( Sandbox.Gltf.GltfReader ).Assembly.ManifestModule.ModuleVersionId.ToString(), gltf_disk_mvid = gltfDiskMvid,
	framework_expected_mvid = frameworkExpectedMvid, framework_loaded_mvid = frameworkLoadedMvid, framework_loaded_location = frameworkLoadedLocation, framework_loaded_context = frameworkLoadedContext, framework_loaded_sha256 = frameworkLoadedHash,
	engine_mvid = typeof( Model ).Assembly.ManifestModule.ModuleVersionId.ToString(), is_editor = Application.IsEditor, starting_frame = initialFrame, final_frame = Application.FrameCount, output
};
System.IO.File.WriteAllText( receiptPath, System.Text.Json.JsonSerializer.Serialize( receipt, new System.Text.Json.JsonSerializerOptions { WriteIndented = true } ) );
return new { passed = selectedPassed, all_project_cases_passed = allProjectCasesPassed, selection, selection_source = selectionSource, request_id = requestId, phase, error = error is null ? null : error[..Math.Min( error.Length, 512 )], expected_cases = expectedCases, passed_cases = passedCases, failed_cases = failedCases, blocked_cases = blockedCases, partial_cases = partialCases, codecs = availability.OrderBy( item => item.Key, StringComparer.Ordinal ).Select( item => new { name = item.Key, passed = item.Value } ).ToArray(), receipt_path = receiptPath };
