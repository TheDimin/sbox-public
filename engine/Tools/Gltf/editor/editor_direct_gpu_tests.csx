// Frozen model-local assertions against the actual loaded producers. This runner
// makes no GPU/rendering parity claim and never compiles producer substitutes.
var engine = typeof( Model ).Assembly;
var gltf = typeof( Sandbox.Gltf.GltfReader ).Assembly;
var checkout = new System.IO.DirectoryInfo( System.IO.Path.GetDirectoryName( engine.Location ) );
while ( checkout is not null && !System.IO.File.Exists( System.IO.Path.Combine( checkout.FullName, "engine/Sandbox-Engine.slnx" ) ) ) checkout = checkout.Parent;
if ( checkout is null ) throw new InvalidOperationException( "Cannot locate checkout from loaded engine." );
var root = checkout.FullName;
var requestPath = System.IO.Path.Combine( root, "Temp/gltf-validation/direct-gpu/current-tests.json" );
var runnerPath = System.IO.Path.Combine( root, "engine/Tools/Gltf/editor/editor_direct_gpu_tests.csx" );
var output = System.IO.Path.Combine( root, "Temp/gltf-validation/direct-gpu/model-tests", Guid.NewGuid().ToString( "N" ) );
System.IO.Directory.CreateDirectory( output );
var receiptPath = System.IO.Path.Combine( output, "receipt.json" );
var sourceHashes = new System.Collections.Generic.Dictionary<string, string>( StringComparer.Ordinal );
var sourceReadPaths = new System.Collections.Generic.Dictionary<string, string>( StringComparer.Ordinal );
var snapshotManifestHashes = new System.Collections.Generic.Dictionary<string, string>( StringComparer.Ordinal );
var diagnostics = new System.Collections.Generic.List<string>();
var rows = new System.Collections.Generic.List<object>();
var measurements = new System.Collections.Generic.List<object>();
var cleanupErrors = new System.Collections.Generic.List<string>();
System.Runtime.Loader.AssemblyLoadContext context = null;
string phase = "initialize", error = null, helperMvid = null, requestHash = null, runnerHash = null;
string frameworkHash = null, frameworkMvid = null;
int expected = 0, passed = 0, failed = 0;
string Hash( byte[] bytes ) => Convert.ToHexStringLower( System.Security.Cryptography.SHA256.HashData( bytes ) );
void Require( bool condition, string message ) { if ( !condition ) throw new InvalidOperationException( message ); }
byte[] Read( string path, long limit = 4L * 1024 * 1024 )
{
    var info = new System.IO.FileInfo( path );
    Require( info.Exists && info.Length > 0 && info.Length <= limit, "Missing or oversized input: " + path );
    return System.IO.File.ReadAllBytes( path );
}
string DiskMvid( string path )
{
    using var stream = System.IO.File.OpenRead( path );
    using var pe = new System.Reflection.PortableExecutable.PEReader( stream );
    var md = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader( pe );
    return md.GetGuid( md.GetModuleDefinition().Mvid ).ToString();
}
object Unwrap( System.Reflection.CustomAttributeTypedArgument value ) =>
    value.Value is System.Collections.Generic.IReadOnlyCollection<System.Reflection.CustomAttributeTypedArgument> children ? children.Select( Unwrap ).ToArray() : value.Value;
bool IsTest( System.Reflection.MethodInfo method ) => method.GetCustomAttributesData().Any( a => a.AttributeType.FullName is "Microsoft.VisualStudio.TestTools.UnitTesting.TestMethodAttribute" or "Microsoft.VisualStudio.TestTools.UnitTesting.DataTestMethodAttribute" );
string Expand( string value ) => value == "$checkout" ? root : value.Replace( "$checkout/", root + "/", StringComparison.Ordinal );
object EntryArgument( string value ) => value == "$cancellationToken" ? cancellationToken : Expand( value );
try
{
    await MainThread.Wait();
    Require( Application.IsEditor && !Game.IsPlaying && !Graphics.IsActive, "Requires idle editor main thread outside rendering." );
    var requestBytes = Read( requestPath ); requestHash = Hash( requestBytes ); runnerHash = Hash( Read( runnerPath ) );
    System.IO.File.WriteAllBytes( System.IO.Path.Combine( output, "request.json" ), requestBytes );
    using var request = System.Text.Json.JsonDocument.Parse( requestBytes ); var spec = request.RootElement;
    Require( spec.GetProperty( "version" ).GetInt32() == 1, "Unsupported request version." );
    var declaredSuites = spec.GetProperty( "suites" ).EnumerateArray().ToArray();
    Require( declaredSuites.Length > 0 && declaredSuites.Length <= 32, "Expected a bounded nonempty suite list." );
    foreach ( var suite in declaredSuites )
    {
        var count = suite.GetProperty( "expected_cases" ).GetInt32();
        Require( count > 0 && count <= 16384, "Every suite must declare a bounded positive expected case count." );
        expected = checked( expected + count );
    }
    phase = "verify-loaded-producers";
    foreach ( var pair in new[] { (Name: "engine", Assembly: engine), (Name: "gltf", Assembly: gltf) } )
    {
        Require( System.Runtime.Loader.AssemblyLoadContext.GetLoadContext( pair.Assembly ) == System.Runtime.Loader.AssemblyLoadContext.Default, "Producer must use default load context." );
        Require( DiskMvid( pair.Assembly.Location ) == pair.Assembly.ManifestModule.ModuleVersionId.ToString(), "Loaded producer differs from disk: " + pair.Name );
        Require( Hash( Read( pair.Assembly.Location, 128L * 1024 * 1024 ) ) == spec.GetProperty( pair.Name + "_sha256" ).GetString(), "Producer hash differs from request: " + pair.Name );
    }
    var frameworkPath = System.IO.Path.Combine( root, "engine/Tests/Sandbox.Test.Gltf/bin/Release/net10.0/Microsoft.VisualStudio.TestPlatform.TestFramework.dll" );
    frameworkHash = Hash( Read( frameworkPath ) ); frameworkMvid = DiskMvid( frameworkPath );
    Require( frameworkHash == spec.GetProperty( "framework_sha256" ).GetString(), "Test framework differs from request." );
    phase = "compile-model-tests";
    var references = new System.Collections.Generic.Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
    foreach ( var assembly in AppDomain.CurrentDomain.GetAssemblies() )
    {
        try
        {
            if ( assembly.IsDynamic || System.Runtime.Loader.AssemblyLoadContext.GetLoadContext( assembly ) != System.Runtime.Loader.AssemblyLoadContext.Default || string.IsNullOrWhiteSpace( assembly.Location ) || !System.IO.File.Exists( assembly.Location ) ) continue;
            var name = assembly.GetName().Name;
            if ( name is "Sandbox.Test.Unit" or "Microsoft.VisualStudio.TestPlatform.TestFramework" ) continue;
            references.TryAdd( name, assembly.Location );
        }
        catch ( Exception e ) when ( e is System.NotSupportedException or System.IO.IOException or BadImageFormatException or UnauthorizedAccessException ) { }
    }
    references[engine.GetName().Name] = engine.Location; references[gltf.GetName().Name] = gltf.Location;
    references["Microsoft.VisualStudio.TestPlatform.TestFramework"] = frameworkPath;
    var parse = new Microsoft.CodeAnalysis.CSharp.CSharpParseOptions( Microsoft.CodeAnalysis.CSharp.LanguageVersion.Preview );
    var trees = new System.Collections.Generic.List<Microsoft.CodeAnalysis.SyntaxTree>
    {
        Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText( "global using System; global using System.Linq; global using System.Threading.Tasks; global using Microsoft.VisualStudio.TestTools.UnitTesting;", parse, path: "ModelTestGlobalUsings.cs" )
    };
    var sourceRoot = System.IO.Path.GetFullPath( System.IO.Path.Combine( root, "engine/Tools/Gltf/direct-gpu" ) ) + System.IO.Path.DirectorySeparatorChar;
    var readerTestPaths = new System.Collections.Generic.HashSet<string>( StringComparer.Ordinal )
    {
        System.IO.Path.GetFullPath( System.IO.Path.Combine( root, "engine/Tests/Sandbox.Test.Gltf/GltfMorphUploadDataTests.cs" ) ),
        System.IO.Path.GetFullPath( System.IO.Path.Combine( root, "engine/Tests/Sandbox.Test.Gltf/GltfPoseMorphOverrideTests.cs" ) )
    };
    var unitTestRoot = System.IO.Path.GetFullPath( System.IO.Path.Combine( root, "engine/Tests/Sandbox.Test.Unit/Resources" ) ) + System.IO.Path.DirectorySeparatorChar;
    foreach ( var source in spec.GetProperty( "sources" ).EnumerateArray() )
    {
        var relative = source.GetProperty( "path" ).GetString(); var path = System.IO.Path.GetFullPath( System.IO.Path.Combine( root, relative ) );
        Require( (path.StartsWith( sourceRoot, StringComparison.Ordinal ) || readerTestPaths.Contains( path ) || (path.StartsWith( unitTestRoot, StringComparison.Ordinal ) && System.IO.Path.GetFileName( path ).StartsWith( "Gltf", StringComparison.Ordinal ))) && path.EndsWith( ".cs", StringComparison.Ordinal ) && !sourceHashes.ContainsKey( relative ), "Sources must be distinct research helpers or explicitly scoped glTF test sources; no producer source is allowed." );
        if ( source.TryGetProperty( "snapshot_path", out var snapshot ) )
        {
            var stagedRoot = System.IO.Path.GetFullPath( System.IO.Path.Combine( root, "Temp/gltf-validation/staged-build" ) ) + System.IO.Path.DirectorySeparatorChar;
            var archivedPath = System.IO.Path.GetFullPath( System.IO.Path.Combine( root, snapshot.GetString() ) );
            Require( archivedPath.StartsWith( stagedRoot, StringComparison.Ordinal ), "Test snapshot must be inside the managed stage archive." );
            var stagedRelative = System.IO.Path.GetRelativePath( stagedRoot, archivedPath );
            var parts = stagedRelative.Split( System.IO.Path.DirectorySeparatorChar );
            Require( parts.Length >= 4 && Guid.TryParseExact( parts[0], "N", out _ ) && parts[1] == "validation-test-snapshot" &&
                string.Join( "/", parts.Skip( 2 ) ) == relative.Replace( '\\', '/' ), "Test snapshot must retain its original scoped test/helper path." );
            var snapshotManifest = System.IO.Path.Combine( stagedRoot, parts[0], "validation-test-snapshot.json" );
            var manifestBytes = Read( snapshotManifest );
            snapshotManifestHashes[snapshotManifest] = Hash( manifestBytes );
            using var archived = System.Text.Json.JsonDocument.Parse( manifestBytes );
            var archivedRow = archived.RootElement.GetProperty( "tests" ).EnumerateArray().Single( row => row.GetProperty( "source" ).GetString() == relative );
            Require( archivedRow.GetProperty( "snapshot" ).GetString() == snapshot.GetString() && archivedRow.GetProperty( "sha256" ).GetString() == source.GetProperty( "sha256" ).GetString(), "Test snapshot is not the frozen manifest entry." );
            path = archivedPath;
        }
        var bytes = Read( path ); var hash = Hash( bytes );
        Require( hash == source.GetProperty( "sha256" ).GetString(), "Frozen source hash differs: " + relative ); sourceHashes.Add( relative, hash );
        sourceReadPaths.Add( relative, path );
        var destination = System.IO.Path.Combine( output, "sources", relative ); System.IO.Directory.CreateDirectory( System.IO.Path.GetDirectoryName( destination ) ); System.IO.File.WriteAllBytes( destination, bytes );
        trees.Add( Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText( System.Text.Encoding.UTF8.GetString( bytes ), parse, path: path ) );
    }
    Require( sourceHashes.Count > 0 && sourceHashes.Count <= 32, "Expected a bounded nonempty source freeze." );
    var options = new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions( Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary, optimizationLevel: Microsoft.CodeAnalysis.OptimizationLevel.Release, allowUnsafe: true );
    var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create( "Sandbox.Test.Unit", trees, references.Values.Select( path => Microsoft.CodeAnalysis.MetadataReference.CreateFromFile( path ) ), options );
    using var image = new System.IO.MemoryStream(); var emitted = compilation.Emit( image, cancellationToken: cancellationToken );
    diagnostics.AddRange( emitted.Diagnostics.Select( d => d.ToString() ) ); Require( emitted.Success, "Frozen helper sources failed compilation against loaded producers." );
    context = new System.Runtime.Loader.AssemblyLoadContext( "gltf-direct-gpu-tests-" + Guid.NewGuid().ToString( "N" ), isCollectible: true );
    context.Resolving += ( loader, requested ) => requested.Name == "Microsoft.VisualStudio.TestPlatform.TestFramework" ? loader.LoadFromAssemblyPath( frameworkPath ) :
        AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault( a => !a.IsDynamic && System.Runtime.Loader.AssemblyLoadContext.GetLoadContext( a ) == System.Runtime.Loader.AssemblyLoadContext.Default && System.Reflection.AssemblyName.ReferenceMatchesDefinition( a.GetName(), requested ) );
    image.Position = 0; var helper = context.LoadFromStream( image ); helperMvid = helper.ManifestModule.ModuleVersionId.ToString();
    Require( helper.GetTypes().All( t => t.Namespace is null || !t.Namespace.StartsWith( "Sandbox.Gltf", StringComparison.Ordinal ) || t.Namespace.StartsWith( "Sandbox.Gltf.DirectGpu", StringComparison.Ordinal ) ), "Helper must not define replacement glTF producer types." );
    Require( helper.GetTypes().Where( t => t.Namespace?.StartsWith( "Sandbox", StringComparison.Ordinal ) == true ).All( t => engine.GetType( t.FullName ) is null && gltf.GetType( t.FullName ) is null ), "Helper must not shadow a type from the actual loaded Engine or Gltf producer." );
    string[] unsupported = ["DynamicDataAttribute", "TestInitializeAttribute", "TestCleanupAttribute", "ClassInitializeAttribute", "ClassCleanupAttribute", "AssemblyInitializeAttribute", "AssemblyCleanupAttribute", "ExpectedExceptionAttribute", "IgnoreAttribute"];
    phase = "execute-model-tests";
    foreach ( var suite in declaredSuites )
    {
        var type = helper.GetType( suite.GetProperty( "type" ).GetString(), throwOnError: true );
        var suiteExpected = suite.GetProperty( "expected_cases" ).GetInt32();
        if ( suite.TryGetProperty( "setup", out var setup ) ) foreach ( var instruction in setup.EnumerateArray() )
        {
            var name = instruction.GetProperty( "name" ).GetString(); var arguments = instruction.GetProperty( "arguments" ).EnumerateArray().Select( a => (object)Expand( a.GetString() ) ).ToArray();
            if ( instruction.GetProperty( "kind" ).GetString() == "property" ) { Require( arguments.Length == 1, "Property setup needs one argument." ); type.GetProperty( name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static ).SetValue( null, arguments[0] ); }
            else type.GetMethod( name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static ).Invoke( null, arguments );
        }
        var kind = suite.GetProperty( "kind" ).GetString();
        if ( kind is "static_results" or "static_labels" )
        {
            var method = type.GetMethod( suite.GetProperty( "method" ).GetString(), System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static );
            var arguments = suite.TryGetProperty( "arguments", out var entryArguments ) ? entryArguments.EnumerateArray().Select( a => EntryArgument( a.GetString() ) ).ToArray() : new object[] { root };
            var returned = method.Invoke( null, arguments );
            if ( returned is Task staticTask )
            {
                await staticTask; await MainThread.Wait();
                var resultProperty = staticTask.GetType().GetProperty( "Result", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance );
                Require( resultProperty is not null, "Static entry must return a result sequence, including Task<T>." );
                returned = resultProperty.GetValue( staticTask );
            }
            var items = ((System.Collections.IEnumerable)returned).Cast<object>().ToArray(); Require( items.Length == suiteExpected, "Static result count differs from request." );
            foreach ( var item in items )
            {
                if ( kind == "static_labels" )
                {
                    Require( item is string label && !string.IsNullOrWhiteSpace( label ), "A completed assertion label must be a nonempty string." );
                    passed++; rows.Add( new { suite = type.FullName, passed = true, completed_assertion_group = (string)item } ); continue;
                }
                var json = System.Text.Json.JsonSerializer.SerializeToElement( item, item.GetType() );
                var ok = json.GetProperty( "Passed" ).GetBoolean(); if ( ok ) passed++; else failed++;
                rows.Add( new { suite = type.FullName, passed = ok, result = json } );
            }
            continue;
        }
        Require( suite.GetProperty( "kind" ).GetString() == "mstest", "Unknown suite kind." );
        var methods = type.GetMethods( System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static );
        Require( !type.GetCustomAttributesData().Any( a => unsupported.Contains( a.AttributeType.Name ) ) && !methods.Any( m => m.GetCustomAttributesData().Any( a => unsupported.Contains( a.AttributeType.Name ) ) ), "Unsupported lifecycle/data/skip attribute." );
        var classAttributes = type.GetCustomAttributesData().Where( a => a.AttributeType.FullName == "Microsoft.VisualStudio.TestTools.UnitTesting.TestClassAttribute" ).ToArray();
        Require( classAttributes.Length == 1, "Expected TestClass attribute." );
        Require( classAttributes[0].AttributeType.Assembly.ManifestModule.ModuleVersionId.ToString() == frameworkMvid, "Resolved test framework differs." );
        var planned = new System.Collections.Generic.List<(System.Reflection.MethodInfo Method, object[] Args)>();
        foreach ( var method in methods.Where( IsTest ).OrderBy( m => m.Name, StringComparer.Ordinal ) )
        {
            Require( method.IsPublic && !method.IsStatic && (method.ReturnType == typeof( void ) || method.ReturnType == typeof( Task )), "Unsupported method shape: " + method.Name );
            var attributes = method.GetCustomAttributesData(); var data = attributes.Where( a => a.AttributeType.Name == "DataRowAttribute" ).ToArray();
            if ( data.Length == 0 ) { Require( method.GetParameters().Length == 0 && !attributes.Any( a => a.AttributeType.Name == "DataTestMethodAttribute" ), "Missing explicit data rows." ); planned.Add( (method, Array.Empty<object>()) ); }
            foreach ( var row in data )
            {
                Require( row.NamedArguments.Count == 0, "Named DataRow values unsupported." );
                var args = row.ConstructorArguments.Select( Unwrap ).ToArray(); if ( args.Length == 1 && args[0] is object[] array ) args = array;
                var parameters = method.GetParameters(); Require( args.Length == parameters.Length, "DataRow arity differs." );
                for ( int i = 0; i < args.Length; ++i ) Require( args[i] is not null && args[i].GetType() == parameters[i].ParameterType && (args[i] is bool or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or char or string), "Unsupported DataRow primitive." );
                planned.Add( (method, args) );
            }
        }
        Require( planned.Count == suiteExpected, "Discovered case count differs from request: " + type.FullName );
        foreach ( var test in planned )
        {
            cancellationToken.ThrowIfCancellationRequested(); await Task.Delay( 1, cancellationToken ); await MainThread.Wait();
            Require( Application.IsEditor && !Game.IsPlaying && !Graphics.IsActive, "Editor left idle state." );
            string failure = null;
            try { var value = test.Method.Invoke( Activator.CreateInstance( type, nonPublic: true ), test.Args ); if ( value is Task task ) { await task; await MainThread.Wait(); } }
            catch ( System.Reflection.TargetInvocationException e ) { failure = (e.InnerException ?? e).ToString(); }
            catch ( Exception e ) { failure = e.ToString(); }
            if ( failure is null ) passed++; else failed++;
            var recordedArguments = test.Args.Select( value => value is float f && !float.IsFinite( f ) ? (object)f.ToString( System.Globalization.CultureInfo.InvariantCulture ) :
                value is double d && !double.IsFinite( d ) ? (object)d.ToString( System.Globalization.CultureInfo.InvariantCulture ) : value ).ToArray();
            rows.Add( new { suite = type.FullName, method = test.Method.Name, arguments = recordedArguments, passed = failure is null, error = failure } );
        }
        var measurementProperties = new System.Collections.Generic.List<string>();
        if ( suite.TryGetProperty( "measurements_property", out var property ) ) measurementProperties.Add( property.GetString() );
        if ( suite.TryGetProperty( "measurements_properties", out var properties ) ) measurementProperties.AddRange( properties.EnumerateArray().Select( p => p.GetString() ) );
        Require( measurementProperties.Count <= 8 && measurementProperties.Distinct( StringComparer.Ordinal ).Count() == measurementProperties.Count, "Measurements must name at most eight distinct properties." );
        foreach ( var name in measurementProperties )
        {
            var value = type.GetProperty( name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static ).GetValue( null );
            measurements.Add( new { suite = type.FullName, property = name, value = System.Text.Json.JsonSerializer.SerializeToElement( value, value.GetType() ) } );
        }
    }
    Require( Hash( Read( requestPath ) ) == requestHash, "Request changed during run." );
    Require( Hash( Read( runnerPath ) ) == runnerHash, "Runner changed during run." );
    foreach ( var source in sourceHashes ) Require( Hash( Read( sourceReadPaths[source.Key] ) ) == source.Value, "Source changed during run: " + source.Key );
    foreach ( var manifest in snapshotManifestHashes ) Require( Hash( Read( manifest.Key ) ) == manifest.Value, "Test snapshot manifest changed during run." );
    phase = "complete";
}
catch ( Exception e ) { error = e.ToString(); }
finally { if ( context is not null ) try { context.Unload(); } catch ( Exception e ) { cleanupErrors.Add( e.ToString() ); } }
var success = phase == "complete" && error is null && cleanupErrors.Count == 0 && expected > 0 && passed == expected && failed == 0;
var receipt = new { passed = success, phase, error, expected_cases = expected, passed_cases = passed, failed_cases = failed, incomplete_cases = Math.Max( 0, expected - passed - failed ), tests = rows, measurements, diagnostics, cleanup_errors = cleanupErrors,
    scope = "Model-local assertions against the actual loaded producer; CPU/data rows alone do not establish directGPU rendered parity.", source_sha256 = sourceHashes, source_read_paths = sourceReadPaths, test_snapshot_manifests = snapshotManifestHashes, request_sha256 = requestHash, runner_sha256 = runnerHash, helper_mvid = helperMvid,
    engine_location = engine.Location, engine_mvid = engine.ManifestModule.ModuleVersionId.ToString(), gltf_location = gltf.Location, gltf_mvid = gltf.ManifestModule.ModuleVersionId.ToString(), framework_sha256 = frameworkHash, framework_mvid = frameworkMvid,
    is_editor = Application.IsEditor, is_unit_test = Application.IsUnitTest, output };
System.IO.File.WriteAllText( receiptPath, System.Text.Json.JsonSerializer.Serialize( receipt, new System.Text.Json.JsonSerializerOptions { WriteIndented = true } ) );
return new { passed = success, phase, error, expected_cases = expected, passed_cases = passed, failed_cases = failed, receipt_path = receiptPath };
