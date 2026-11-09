// Run in an existing s&box editor through run_csharp. Uses only private mounted fixtures.
var engineLocation = typeof( Model ).Assembly.Location;
var root = new System.IO.DirectoryInfo( System.IO.Path.GetDirectoryName( engineLocation ) );
while ( root is not null && !System.IO.File.Exists( System.IO.Path.Combine( root.FullName, "engine", "Sandbox-Engine.slnx" ) ) ) root = root.Parent;
if ( root is null ) throw new InvalidOperationException( "Cannot find checkout from loaded engine: " + engineLocation );
var corpus = Environment.GetEnvironmentVariable( "SBOX_GLTF_CORPUS" );
if ( string.IsNullOrWhiteSpace( corpus ) ) corpus = System.IO.Path.Combine( root.FullName, "Temp", "gltf-reference", "glTF-Sample-Assets" );
var inputDirectory = System.IO.Path.Combine( corpus, "Models", "Triangle", "glTF" );
var runId = Guid.NewGuid().ToString( "N" );
var virtualBase = "gltf-model-lifetime/" + runId;
var output = System.IO.Path.Combine( root.FullName, "Temp", "gltf-validation", "editor-model-lifetime", runId );
System.IO.Directory.CreateDirectory( output );
var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static;
var engine = typeof( Model ).Assembly;
var startFrame = Application.FrameCount;
var phase = "initialize";
var cases = new System.Collections.Generic.List<object>();
var cleanupErrors = new System.Collections.Generic.List<string>();
var redirects = new System.Collections.Generic.List<string>();
var owned = new System.Collections.Generic.List<Model>();
object redirectObject = null;
IDisposable redirect = null;
BaseFileSystem managedMount = null;
BaseFileSystem mountedAggregate = null;
System.Reflection.MethodInfo unmount = null;
bool mounted = false;
Model first = null;
Model second = null;
Material shared = null;
WeakReference<Model> finalizerWeak = null;
int? contextIdentity = null;
int? resourcesIdentity = null;
string error = null;
string finalizerStatus = "not_attempted";
string finalizerReason = null;
int finalizerCollections = 0;
long? finalizerElapsedMs = null;
bool explicitPassed = false;
bool? finalizerPassed = null;
int? unrelatedLeases = null;
string Hash( byte[] b ) => Convert.ToHexStringLower( System.Security.Cryptography.SHA256.HashData( b ) );
void Require( bool ok, string message ) { if ( !ok ) throw new InvalidOperationException( message ); }
void Cleanup( string name, Action action ) { try { action(); } catch ( Exception e ) { cleanupErrors.Add( name + ": " + e ); } }
bool Valid( Model m ) => m is { IsValid: true } && !m.IsError && !ReferenceEquals( m, Model.Error );

// Avoid retaining the loaded model in the async state machine during finalizer testing.
[System.Runtime.CompilerServices.MethodImpl( System.Runtime.CompilerServices.MethodImplOptions.NoInlining )]
static (WeakReference<Model> Weak, Material Material) CreateFinalizerSubject( string path )
{
    var model = Model.Load( path );
    if ( model is null || !model.IsValid || model.IsError ) throw new InvalidOperationException( "Finalizer subject failed to load." );
    var material = model.MeshInfo?.Meshes.SelectMany( m => m.DrawCalls ).Select( d => d.Material ).FirstOrDefault( m => m is { IsValid: true } && !m.IsError );
    if ( material is null ) throw new InvalidOperationException( "Finalizer subject has no valid material." );
    return (new WeakReference<Model>( model ), material);
}
[System.Runtime.CompilerServices.MethodImpl( System.Runtime.CompilerServices.MethodImplOptions.NoInlining )]
static bool ModelIsDead( WeakReference<Model> weak ) => !weak.TryGetTarget( out _ );

try
{
    await MainThread.Wait();
    Require( Application.IsEditor && !Graphics.IsActive, "Run inside the existing editor outside a render scope." );
    Require( Valid( Model.Sphere ), "Stock vmdl sphere is unavailable before the test." );
    var contextType = engine.GetType( "Sandbox.Engine.GlobalContext", true );
    var currentContext = contextType.GetProperty( "Current", flags ).GetValue( null );
    var contextResources = contextType.GetProperty( "ResourceSystem", flags ).GetValue( currentContext );
    Require( currentContext is not null && contextResources is not null, "Current context/resource system unavailable." );
    contextIdentity = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode( currentContext );
    resourcesIdentity = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode( contextResources );
    phase = "prepare-fixtures";
    var gltf = System.IO.File.ReadAllBytes( System.IO.Path.Combine( inputDirectory, "Triangle.gltf" ) );
    var bin = System.IO.File.ReadAllBytes( System.IO.Path.Combine( inputDirectory, "Triangle.bin" ) );
    var sourceHash = Hash( gltf ); var bufferHash = Hash( bin );
    Require( sourceHash == "6f4a8a1fbefbac2d0adde0800864279fd98c3f119077380e6840810ed62b6cfc" && bufferHash == "898ed7f560dca9907a80c98be57fbbc0f3a092d2e3683b964ffcfa75c549ac62", "Pinned Triangle differs." );
    var files = new System.Collections.Generic.Dictionary<string, byte[]> { ["first.gltf"] = gltf, ["second.gltf"] = gltf, ["finalizer.gltf"] = gltf, ["Triangle.bin"] = bin };
    var redirectType = engine.GetType( "Sandbox.Internal.RedirectFileSystem", true );
    redirectObject = redirectType.GetMethod( "Create", flags ).Invoke( null, new object[] { output } );
    redirect = (IDisposable)redirectObject;
    var addRedirect = redirectType.GetMethod( "AddAbsFile", flags );
    var removeRedirect = redirectType.GetMethod( "RemoveAbsFile", flags );
    foreach ( var file in files )
    {
        var physical = System.IO.Path.Combine( output, file.Key );
        System.IO.File.WriteAllBytes( physical, file.Value );
        var path = "/" + virtualBase + "/" + file.Key;
        addRedirect.Invoke( redirectObject, new object[] { path, physical } );
        redirects.Add( path );
    }
    var ctor = typeof( BaseFileSystem ).GetConstructors( flags ).Single( c => { var p = c.GetParameters(); return p.Length == 1 && p[0].ParameterType.IsInstanceOfType( redirectObject ); } );
    managedMount = (BaseFileSystem)ctor.Invoke( new[] { redirectObject } );
    mountedAggregate = Sandbox.FileSystem.Mounted ?? throw new InvalidOperationException( "Managed mounted filesystem unavailable." );
    var mount = typeof( BaseFileSystem ).GetMethod( "Mount", flags, null, new[] { typeof( BaseFileSystem ) }, null );
    unmount = typeof( BaseFileSystem ).GetMethod( "UnMount", flags, null, new[] { typeof( BaseFileSystem ) }, null );
    Require( mount is not null && unmount is not null, "Managed mount APIs unavailable." );
    mount.Invoke( mountedAggregate, new object[] { managedMount } ); mounted = true;
    Require( mountedAggregate.FileExists( virtualBase + "/first.gltf" ), "Private fixture is not mounted." );
    var destroy = typeof( Model ).GetMethod( "Destroy", flags );
    Require( destroy is not null, "Model destruction hook unavailable." );

    // Inspect only the matching material entry; never clear or change the shared cache.
    var cacheType = engine.GetType( "Sandbox.GltfMaterialCache", true );
    var cache = cacheType.GetProperty( "Shared", flags ).GetValue( null );
    var core = cacheType.GetField( "core", flags ).GetValue( cache );
    var entries = (System.Collections.IDictionary)core.GetType().GetField( "entries", flags ).GetValue( core );
    int Users( Material material )
    {
        foreach ( System.Collections.DictionaryEntry pair in entries )
        {
            var entry = pair.Value;
            var value = entry.GetType().GetField( "Value", flags ).GetValue( entry );
            if ( ReferenceEquals( value, material ) ) return (int)entry.GetType().GetField( "Users", flags ).GetValue( entry );
        }
        return 0;
    }
    Material MaterialOf( Model model ) => model.MeshInfo?.Meshes.SelectMany( m => m.DrawCalls ).Select( d => d.Material ).FirstOrDefault( m => m is { IsValid: true } && !m.IsError );
    object Owner( Model model )
    {
        var lifetime = typeof( Model ).GetField( "gltfLifetime", flags ).GetValue( model ) ?? throw new InvalidOperationException( "Loaded model has no glTF lifetime." );
        return lifetime.GetType().GetProperty( "Context", flags ).GetValue( lifetime );
    }
    async Task Settle()
    {
        var firstFrame = Application.FrameCount;
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while ( Application.FrameCount < firstFrame + 3 )
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ( timer.Elapsed > TimeSpan.FromSeconds( 5 ) ) throw new TimeoutException( "Editor frames did not advance." );
            await Task.Delay( 50, cancellationToken ); await MainThread.Wait();
        }
    }

    phase = "shared-model-ownership";
    first = Model.Load( virtualBase + "/first.gltf" );
    if ( Valid( first ) ) owned.Add( first );
    second = Model.Load( virtualBase + "/second.gltf" );
    if ( Valid( second ) ) owned.Add( second );
    Require( Valid( first ) && Valid( second ) && !ReferenceEquals( first, second ), "Distinct model paths did not produce two valid independent models." );
    Require( ReferenceEquals( Owner( first ), currentContext ) && ReferenceEquals( Owner( second ), currentContext ), "Model ownership context differs from its loading context." );
    shared = MaterialOf( first );
    var otherMaterial = MaterialOf( second );
    Require( shared is { IsValid: true } && !shared.IsError && ReferenceEquals( shared, otherMaterial ), "Distinct models do not share the same native material wrapper." );
    var usersBefore = Users( shared );
    Require( usersBefore >= 2, "Shared cache does not show both model leases." );
    unrelatedLeases = usersBefore - 2;
    var firstPath = first.ResourcePath;
    var secondPath = second.ResourcePath;
    destroy.Invoke( first, null );
    await Settle();
    Require( !first.IsValid && Valid( second ) && shared.IsValid && !shared.IsError, "Destroying one model damaged the other model or shared material." );
    var usersAfterFirst = Users( shared );
    Require( usersAfterFirst == usersBefore - 1, "First model did not release exactly one cache lease." );
    destroy.Invoke( first, null );
    Require( Users( shared ) == usersAfterFirst && Valid( second ) && shared.IsValid, "Repeated destruction changed shared ownership." );
    destroy.Invoke( second, null );
    await Settle();
    var usersAfterSecond = Users( shared );
    Require( !second.IsValid && usersAfterSecond == unrelatedLeases, "Second model did not release exactly one cache lease." );
    var finalMaterialInvalid = !shared.IsValid;
    if ( unrelatedLeases == 0 ) Require( finalMaterialInvalid, "Last exclusive material lease did not invalidate its wrapper." );
    else Require( shared.IsValid, "Material with unrelated live leases was invalidated." );
    destroy.Invoke( second, null );
    Require( Users( shared ) == usersAfterSecond, "Repeated second destruction changed the cache." );
    explicitPassed = true;
    cases.Add( new { name = "explicit_destroy", passed = true, first_path = firstPath, second_path = secondPath, same_material = ReferenceEquals( shared, otherMaterial ), users_before = usersBefore, users_after_first = usersAfterFirst, users_after_second = usersAfterSecond, unrelated_leases = unrelatedLeases, last_material_invalid = finalMaterialInvalid, last_material_invalidation_proven = unrelatedLeases == 0, repeated_destroy_harmless = true, context_identity = contextIdentity, resource_system_identity = resourcesIdentity } );

    phase = "finalizer";
    // The native cache intentionally retains recent models and freezes expiry when
    // Game.IsPaused; never clear it or mutate the user's pause state for this probe.
    if ( Game.IsPaused )
    {
        finalizerStatus = "unproven";
        finalizerReason = "Game.IsPaused prevents normal NativeResourceCache retention expiry; cache and pause state were preserved.";
    }
    else
    {
        var subject = CreateFinalizerSubject( virtualBase + "/finalizer.gltf" );
        finalizerWeak = subject.Weak;
        var finalizerMaterial = subject.Material;
        var initialUsers = Users( finalizerMaterial );
        Require( initialUsers >= 1, "Finalizer subject has no cache lease." );
        var otherUsers = initialUsers - 1;
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while ( !ModelIsDead( subject.Weak ) && !Game.IsPaused && timer.Elapsed < TimeSpan.FromSeconds( 15 ) )
        {
            cancellationToken.ThrowIfCancellationRequested();
            Require( !Graphics.IsActive, "Finalizer control must collect outside a graphics scope." );
            GC.Collect( 2, GCCollectionMode.Forced, blocking: true, compacting: false );
            finalizerCollections++;
            GC.WaitForPendingFinalizers();
            await Task.Delay( 250, cancellationToken ); await MainThread.Wait();
        }
        finalizerElapsedMs = timer.ElapsedMilliseconds;
        if ( !ModelIsDead( subject.Weak ) )
        {
            finalizerStatus = "unproven";
            finalizerReason = Game.IsPaused ? "Game became paused during the wait, preventing normal cache expiry." : "Model remained reachable within the 15-second wait, possibly due to normal resource-cache retention.";
        }
        else
        {
            await Settle();
            var usersAfterGc = Users( finalizerMaterial );
            var materialInvalid = !finalizerMaterial.IsValid;
            finalizerPassed = usersAfterGc == otherUsers && (otherUsers == 0 ? materialInvalid : finalizerMaterial.IsValid);
            finalizerStatus = finalizerPassed == true ? "passed" : "failed";
            cases.Add( new { name = "finalizer", passed = finalizerPassed, weak_model_dead = true, users_before = initialUsers, users_after = usersAfterGc, unrelated_leases = otherUsers, material_invalid = materialInvalid, last_material_invalidation_proven = otherUsers == 0, elapsed_ms = timer.ElapsedMilliseconds } );
            Require( finalizerPassed == true, "Finalization did not release exactly its own material lease." );
        }
    }
    Require( Valid( Model.Sphere ), "Stock vmdl sphere became invalid." );
    phase = "complete";
}
catch ( OperationCanceledException e ) { error = "Cancelled: " + e; }
catch ( Exception e ) { error = e.ToString(); }
finally
{
    await MainThread.Wait();
    var destroy = typeof( Model ).GetMethod( "Destroy", flags );
    foreach ( var model in owned ) if ( Valid( model ) ) Cleanup( "owned model", () => destroy.Invoke( model, null ) );
    if ( finalizerWeak is not null && finalizerWeak.TryGetTarget( out var remainingFinalizerModel ) && Valid( remainingFinalizerModel ) )
        Cleanup( "remaining finalizer model", () => destroy.Invoke( remainingFinalizerModel, null ) );
    if ( mounted ) Cleanup( "managed mount", () => unmount.Invoke( mountedAggregate, new object[] { managedMount } ) );
    if ( redirectObject is not null ) foreach ( var name in redirects ) Cleanup( "redirect", () => redirectObject.GetType().GetMethod( "RemoveAbsFile", flags ).Invoke( redirectObject, new object[] { name } ) );
    if ( redirect is not null ) Cleanup( "redirect filesystem", () => redirect.Dispose() );
}
var result = new
{
    passed = error is null && cleanupErrors.Count == 0 && explicitPassed && unrelatedLeases == 0 && finalizerPassed != false && phase == "complete",
    scope = "editor model-owned shared material lifetime, explicit destruction and bounded finalization; no cache clear or pause mutation",
    phase, error, cases, explicit_destroy_passed = explicitPassed, finalizer_status = finalizerStatus, finalizer_passed = finalizerPassed, finalizer_reason = finalizerReason, finalizer_collections = finalizerCollections, finalizer_elapsed_ms = finalizerElapsedMs,
    unrelated_material_leases = unrelatedLeases, last_material_invalidation_proven = explicitPassed && unrelatedLeases == 0, context_identity = contextIdentity, resource_system_identity = resourcesIdentity, cleanup_errors = cleanupErrors, sphere_valid_after = Valid( Model.Sphere ),
    engine_mvid = engine.ManifestModule.ModuleVersionId.ToString(), gltf_mvid = typeof( Sandbox.Gltf.GltfReader ).Assembly.ManifestModule.ModuleVersionId.ToString(),
    engine_location = engineLocation, starting_frame = startFrame, final_frame = Application.FrameCount, virtual_base = virtualBase, output
};
System.IO.File.WriteAllText( System.IO.Path.Combine( output, "receipt.json" ), System.Text.Json.JsonSerializer.Serialize( result, new System.Text.Json.JsonSerializerOptions { WriteIndented = true } ) );
return result;
