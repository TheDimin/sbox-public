// Existing editor only: compile the exact native component regression source
// against the loaded producers. No producer substitution or unit-test flags.
var engine = typeof( Model ).Assembly;
var gltf = typeof( Sandbox.Gltf.GltfReader ).Assembly;
var checkout = new System.IO.DirectoryInfo( System.IO.Path.GetDirectoryName( engine.Location ) );
while ( checkout is not null && !System.IO.File.Exists( System.IO.Path.Combine( checkout.FullName, "engine", "Sandbox-Engine.slnx" ) ) ) checkout = checkout.Parent;
if ( checkout is null ) throw new InvalidOperationException( "Cannot locate checkout from loaded engine." );
var snapshotRoot = System.IO.Path.Combine( checkout.FullName, "Temp/gltf-validation/staged-build/f9fd58602bbf47a6ac1bfa51f0d07845" );
var testSourceRoot = System.IO.Path.Combine( snapshotRoot, "test-source-snapshot" );
var snapshotManifestPath = System.IO.Path.Combine( snapshotRoot, "source-snapshot-manifest.json" );
using var snapshotManifest = System.Text.Json.JsonDocument.Parse( System.IO.File.ReadAllBytes( snapshotManifestPath ) );
var runId = Guid.NewGuid().ToString( "N" );
var output = System.IO.Path.Combine( checkout.FullName, "Temp", "gltf-validation", "editor-model-rest-tests", runId );
System.IO.Directory.CreateDirectory( output );
var receiptPath = System.IO.Path.Combine( output, "receipt.json" );
var runnerPath = System.IO.Path.Combine( checkout.FullName, "engine", "Tools", "Gltf", "editor", "editor_model_rest_tests.csx" );
var frameworkPath = System.IO.Path.Combine( checkout.FullName, "engine", "Tests", "Sandbox.Test.Gltf", "bin", "Release", "net10.0", "Microsoft.VisualStudio.TestPlatform.TestFramework.dll" );
var selected = new[] { (File: "engine/Tests/Sandbox.Test.Integration/Resources/GltfModelRestPoseTests.cs", Type: "ResourceTests.GltfModelRestPoseTests") };
var sourceHashes = new System.Collections.Generic.Dictionary<string, string>( StringComparer.Ordinal );
var diagnostics = new System.Collections.Generic.List<string>(); var cleanupErrors = new System.Collections.Generic.List<string>();
var results = new System.Collections.Generic.List<object>(); var exclusions = new System.Collections.Generic.List<object>();
var referencesUsed = Array.Empty<string>();
string phase = "initialize", error = null, runnerHash = null, helperMvid = null, engineDiskMvid = null, gltfDiskMvid = null;
string engineHash = null, gltfHash = null, frameworkHash = null, frameworkExpectedMvid = null, frameworkLoadedMvid = null, frameworkLoadedHash = null, frameworkLoadedLocation = null, frameworkLoadedContext = null;
int expected = 0, passed = 0, failed = 0; var initialFrame = Application.FrameCount;
bool? unitTestMode = null; System.Runtime.Loader.AssemblyLoadContext context = null;
string Hash( byte[] bytes ) => Convert.ToHexStringLower( System.Security.Cryptography.SHA256.HashData( bytes ) );
void Require( bool condition, string message ) { if ( !condition ) throw new InvalidOperationException( message ); }
byte[] ReadBounded( string path, long limit = 1024 * 1024 )
{
    var info = new System.IO.FileInfo( path );
    Require( info.Exists && info.Length > 0 && info.Length <= limit, "Missing or oversized input: " + path );
    return System.IO.File.ReadAllBytes( path );
}
string DiskMvid( string path )
{
    using var stream = System.IO.File.OpenRead( path ); using var pe = new System.Reflection.PortableExecutable.PEReader( stream );
    var metadata = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader( pe );
    return metadata.GetGuid( metadata.GetModuleDefinition().Mvid ).ToString();
}
object Unwrap( System.Reflection.CustomAttributeTypedArgument value )
{
    if ( value.Value is System.Collections.Generic.IReadOnlyCollection<System.Reflection.CustomAttributeTypedArgument> children ) return children.Select( Unwrap ).ToArray();
    return value.Value;
}
bool IsTest( System.Reflection.MethodInfo method ) => method.GetCustomAttributesData().Any( a => a.AttributeType.FullName is "Microsoft.VisualStudio.TestTools.UnitTesting.TestMethodAttribute" or "Microsoft.VisualStudio.TestTools.UnitTesting.DataTestMethodAttribute" );
System.Collections.Generic.List<object[]> ArgumentsFor( System.Reflection.MethodInfo method )
{
    Require( method.IsPublic && !method.IsStatic && (method.ReturnType == typeof( void ) || method.ReturnType == typeof( Task )), "Unsupported test shape: " + method.Name );
    var attributes = method.GetCustomAttributesData(); var parameters = method.GetParameters();
    Require( attributes.Count( a => a.AttributeType.FullName is "Microsoft.VisualStudio.TestTools.UnitTesting.TestMethodAttribute" or "Microsoft.VisualStudio.TestTools.UnitTesting.DataTestMethodAttribute" ) == 1, "Ambiguous test method attribute: " + method.Name );
    var rows = attributes.Where( a => a.AttributeType.Name == "DataRowAttribute" ).ToArray(); var values = new System.Collections.Generic.List<object[]>();
    if ( rows.Length == 0 )
    {
        Require( parameters.Length == 0 && !attributes.Any( a => a.AttributeType.Name == "DataTestMethodAttribute" ), "Missing explicit data rows: " + method.Name );
        values.Add( Array.Empty<object>() );
    }
    foreach ( var row in rows )
    {
        Require( row.NamedArguments.Count == 0, "Named DataRow values are unsupported: " + method.Name );
        var args = row.ConstructorArguments.Select( Unwrap ).ToArray();
        if ( args.Length == 1 && args[0] is object[] array ) args = array;
        Require( args.Length == parameters.Length, "DataRow arity differs: " + method.Name );
        for ( var i = 0; i < args.Length; ++i )
        {
            Require( args[i] is bool or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or char or string,
                "Unsupported DataRow value: " + method.Name );
            Require( args[i].GetType() == parameters[i].ParameterType, "DataRow primitive type differs: " + method.Name );
        }
        values.Add( args );
    }
    return values;
}
try
{
    await MainThread.Wait();
    Require( Application.IsEditor && !Game.IsPlaying && !Graphics.IsActive, "Run on the idle editor main thread outside a graphics scope." );
    unitTestMode = Application.IsUnitTest; runnerHash = Hash( ReadBounded( runnerPath ) );
    phase = "verify-loaded-producers";
    Require( System.Runtime.Loader.AssemblyLoadContext.GetLoadContext( engine ) == System.Runtime.Loader.AssemblyLoadContext.Default && System.Runtime.Loader.AssemblyLoadContext.GetLoadContext( gltf ) == System.Runtime.Loader.AssemblyLoadContext.Default, "Engine/glTF must be loaded in the default context." );
    engineDiskMvid = DiskMvid( engine.Location ); gltfDiskMvid = DiskMvid( gltf.Location );
    Require( engineDiskMvid == engine.ManifestModule.ModuleVersionId.ToString() && gltfDiskMvid == gltf.ManifestModule.ModuleVersionId.ToString(), "A loaded producer differs from disk." );
    engineHash = Hash( ReadBounded( engine.Location, 128L * 1024 * 1024 ) ); gltfHash = Hash( ReadBounded( gltf.Location, 128L * 1024 * 1024 ) );
    Require( engineHash == snapshotManifest.RootElement.GetProperty( "engine_sha256" ).GetString() && gltfHash == snapshotManifest.RootElement.GetProperty( "gltf_sha256" ).GetString(), "Frozen test snapshot belongs to a different producer." );
    frameworkHash = Hash( ReadBounded( frameworkPath ) ); frameworkExpectedMvid = DiskMvid( frameworkPath );
    phase = "compile-tests-against-loaded-producers";
    var references = new System.Collections.Generic.Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
    foreach ( var assembly in AppDomain.CurrentDomain.GetAssemblies() )
    {
        try
        {
            if ( assembly.IsDynamic || System.Runtime.Loader.AssemblyLoadContext.GetLoadContext( assembly ) != System.Runtime.Loader.AssemblyLoadContext.Default ) continue;
            var name = assembly.GetName().Name;
            if ( name == "Sandbox.Test.Unit" || name == "Microsoft.VisualStudio.TestPlatform.TestFramework" || string.IsNullOrWhiteSpace( assembly.Location ) || !System.IO.File.Exists( assembly.Location ) ) continue;
            if ( !references.ContainsKey( name ) ) references.Add( name, assembly.Location );
        }
        catch ( Exception ex ) when ( ex is System.NotSupportedException || ex is System.IO.IOException || ex is BadImageFormatException || ex is UnauthorizedAccessException ) { }
    }
    references[engine.GetName().Name] = engine.Location; references[gltf.GetName().Name] = gltf.Location;
    references["Microsoft.VisualStudio.TestPlatform.TestFramework"] = frameworkPath;
    referencesUsed = references.Keys.OrderBy( x => x, StringComparer.Ordinal ).ToArray();
    var parse = new Microsoft.CodeAnalysis.CSharp.CSharpParseOptions( Microsoft.CodeAnalysis.CSharp.LanguageVersion.Preview );
    var imports = "global using System; global using System.Linq; global using System.Threading.Tasks; global using Sandbox; global using Microsoft.VisualStudio.TestTools.UnitTesting; global using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;";
    var trees = new System.Collections.Generic.List<Microsoft.CodeAnalysis.SyntaxTree> { Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText( imports, parse, path: "AdapterTestGlobalUsings.cs" ) };
    foreach ( var source in selected )
    {
        var path = System.IO.Path.Combine( testSourceRoot, source.File ); var bytes = ReadBounded( path ); sourceHashes[source.File] = Hash( bytes ); Require( sourceHashes[source.File] == snapshotManifest.RootElement.GetProperty( "test_sources_sha256" ).GetProperty( source.File ).GetString(), "Frozen test source hash differs." );
        trees.Add( Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText( System.Text.Encoding.UTF8.GetString( bytes ), parse, path: path ) );
    }
    var options = new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions( Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary, optimizationLevel: Microsoft.CodeAnalysis.OptimizationLevel.Release );
    var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create( "Sandbox.Test.Unit", trees, references.Values.Select( file => Microsoft.CodeAnalysis.MetadataReference.CreateFromFile( file ) ), options );
    using var image = new System.IO.MemoryStream(); var emitted = compilation.Emit( image, cancellationToken: cancellationToken );
    diagnostics.AddRange( emitted.Diagnostics.Select( item => item.ToString() ) ); Require( emitted.Success, "Adapter test sources did not compile against loaded producers." );
    context = new System.Runtime.Loader.AssemblyLoadContext( "gltf-animation-component-tests-" + runId, isCollectible: true );
    context.Resolving += ( loader, requested ) => requested.Name == "Microsoft.VisualStudio.TestPlatform.TestFramework" ? loader.LoadFromAssemblyPath( frameworkPath ) :
        AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault( a => !a.IsDynamic && System.Runtime.Loader.AssemblyLoadContext.GetLoadContext( a ) == System.Runtime.Loader.AssemblyLoadContext.Default && System.Reflection.AssemblyName.ReferenceMatchesDefinition( a.GetName(), requested ) );
    image.Position = 0; var helper = context.LoadFromStream( image ); helperMvid = helper.ManifestModule.ModuleVersionId.ToString();
    Require( helper.GetName().Name == "Sandbox.Test.Unit", "Wrong test helper assembly identity." );
    phase = "discover-tests-and-framework";
    var planned = new System.Collections.Generic.List<(string Suite, System.Reflection.MethodInfo Method, object[] Arguments)>();
    var frameworkAssemblies = new System.Collections.Generic.HashSet<System.Reflection.Assembly>();
    string[] unsupported = ["DynamicDataAttribute", "TestInitializeAttribute", "TestCleanupAttribute", "ClassInitializeAttribute", "ClassCleanupAttribute", "AssemblyInitializeAttribute", "AssemblyCleanupAttribute", "ExpectedExceptionAttribute", "IgnoreAttribute"];
    Require( !helper.GetCustomAttributesData().Any( a => unsupported.Contains( a.AttributeType.Name ) ), "Unsupported assembly lifecycle/data attribute." );
    foreach ( var source in selected )
    {
        var type = helper.GetType( source.Type, true );
        var methods = type.GetMethods( System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic );
        Require( !type.GetCustomAttributesData().Any( a => unsupported.Contains( a.AttributeType.Name ) ) && !methods.Any( m => m.GetCustomAttributesData().Any( a => unsupported.Contains( a.AttributeType.Name ) ) ), "Unsupported lifecycle/data attribute: " + source.Type );
        var classAttributes = type.GetCustomAttributesData().Where( a => a.AttributeType.FullName == "Microsoft.VisualStudio.TestTools.UnitTesting.TestClassAttribute" ).ToArray();
        Require( classAttributes.Length == 1, "Expected one TestClass attribute: " + source.Type ); frameworkAssemblies.Add( classAttributes[0].AttributeType.Assembly );
        var tests = methods.Where( IsTest ).OrderBy( m => m.Name, StringComparer.Ordinal ).ToArray(); Require( tests.Length > 0, "No tests discovered: " + source.Type );
        foreach ( var method in tests )
        {
            foreach ( var attr in method.GetCustomAttributesData().Where( a => a.AttributeType.FullName is "Microsoft.VisualStudio.TestTools.UnitTesting.TestMethodAttribute" or "Microsoft.VisualStudio.TestTools.UnitTesting.DataTestMethodAttribute" ) ) frameworkAssemblies.Add( attr.AttributeType.Assembly );
            var arguments = ArgumentsFor( method );
            foreach ( var args in arguments ) planned.Add( (source.Type, method, args) );
        }
    }
    Require( exclusions.Count == 0, "No native component regression may be excluded." );
    Require( frameworkAssemblies.Count == 1, "Test attributes resolved from multiple framework assemblies." );
    var framework = frameworkAssemblies.Single(); frameworkLoadedMvid = framework.ManifestModule.ModuleVersionId.ToString(); frameworkLoadedLocation = framework.Location;
    frameworkLoadedContext = System.Runtime.Loader.AssemblyLoadContext.GetLoadContext( framework )?.Name ?? "<unnamed>";
    Require( !string.IsNullOrWhiteSpace( frameworkLoadedLocation ), "Resolved test framework has no disk location." );
    frameworkLoadedHash = Hash( ReadBounded( frameworkLoadedLocation ) );
    Require( frameworkLoadedHash == frameworkHash && frameworkHash == Hash( ReadBounded( frameworkPath ) ) && frameworkLoadedMvid == frameworkExpectedMvid, "Resolved test framework differs from the expected binary." );
    expected = planned.Count; Require( expected > 0, "No CPU tests selected." );
    phase = "execute-native-component-tests";
    foreach ( var test in planned )
    {
        cancellationToken.ThrowIfCancellationRequested(); await Task.Delay( 1, cancellationToken ); await MainThread.Wait();
        Require( Application.IsEditor && !Game.IsPlaying && !Graphics.IsActive, "Editor left the safe idle state." );
        var before = Application.FrameCount; string failure = null;
        try { var invoked = test.Method.Invoke( Activator.CreateInstance( test.Method.DeclaringType ), test.Arguments ); if ( invoked is Task task ) { await task; await MainThread.Wait(); } }
        catch ( System.Reflection.TargetInvocationException ex ) { failure = (ex.InnerException ?? ex).ToString(); }
        catch ( Exception ex ) { failure = ex.ToString(); }
        var after = Application.FrameCount;
        if ( test.Method.ReturnType == typeof( void ) && before != after ) failure = (failure ?? "") + $" Synchronous test changed frame count: {before}->{after}.";
        if ( failure is null ) passed++; else failed++;
        results.Add( new { suite = test.Suite, name = test.Method.Name, arguments = test.Arguments.Select( value => value is float f && !float.IsFinite( f ) ? (object)f.ToString( System.Globalization.CultureInfo.InvariantCulture ) : value ).ToArray(), passed = failure is null, error = failure, before_frame = before, after_frame = after } );
    }
    foreach ( var source in selected ) Require( Hash( ReadBounded( System.IO.Path.Combine( testSourceRoot, source.File ) ) ) == sourceHashes[source.File], "A test source changed during the run: " + source.File );
    phase = "complete";
}
catch ( Exception ex ) { error = ex.ToString(); }
finally { if ( context is not null ) try { context.Unload(); } catch ( Exception ex ) { cleanupErrors.Add( "Unload test context: " + ex ); } }
var success = error is null && cleanupErrors.Count == 0 && phase == "complete" && expected > 0 && passed == expected && failed == 0;
var receipt = new { passed = success, phase, error, scope = "Actual Model.Load generic/SkinnedModelRenderer authored rest versus independent native reference, async real-frame managed GPU readback with fresh empty controls. Native/default pipeline and full conformance remain separate.",
    expected_cases = expected, passed_cases = passed, failed_cases = failed, incomplete_cases = Math.Max( 0, expected - passed - failed ), tests = results, excluded = exclusions, diagnostics, cleanup_errors = cleanupErrors,
    runner_sha256 = runnerHash, test_source_snapshot = testSourceRoot, snapshot_manifest_sha256 = Hash( ReadBounded( snapshotManifestPath, 4L * 1024 * 1024 ) ), source_sha256 = sourceHashes, metadata_reference_names = referencesUsed, helper_mvid = helperMvid,
    engine_location = engine.Location, engine_loaded_mvid = engine.ManifestModule.ModuleVersionId.ToString(), engine_disk_mvid = engineDiskMvid, engine_sha256 = engineHash,
    gltf_location = gltf.Location, gltf_loaded_mvid = gltf.ManifestModule.ModuleVersionId.ToString(), gltf_disk_mvid = gltfDiskMvid, gltf_sha256 = gltfHash,
    framework_sha256 = frameworkHash, framework_expected_mvid = frameworkExpectedMvid, framework_loaded_mvid = frameworkLoadedMvid, framework_loaded_sha256 = frameworkLoadedHash, framework_loaded_location = frameworkLoadedLocation, framework_loaded_context = frameworkLoadedContext,
    is_editor = Application.IsEditor, application_is_unit_test = unitTestMode, initial_frame = initialFrame, final_frame = Application.FrameCount, output };
System.IO.File.WriteAllText( receiptPath, System.Text.Json.JsonSerializer.Serialize( receipt, new System.Text.Json.JsonSerializerOptions { WriteIndented = true } ) );
return new { passed = success, phase, error, expected_cases = expected, passed_cases = passed, failed_cases = failed, receipt_path = receiptPath };
