// Isolated SimpleInstancing crash reproduction: exact corpus public path plus phase checkpoints.
// Coordinator supplies the usual gltf-run-case.json for SimpleInstancing/glTF only.
// This preserves rendering and teardown order; phase trace is diagnostic, not acceptance.
// One requested corpus case, through public Model.Load and public renderer components.
// Coordinator owns editor admission and serial execution. A capture is not visual approval.
var requestPath = System.IO.Path.Combine( exchangeDirectory, "gltf-run-case.json" );
var timer = System.Diagnostics.Stopwatch.StartNew();
var jsonOptions = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
var limitations = new System.Collections.Generic.List<string>();
var cleanupErrors = new System.Collections.Generic.List<string>();
var animationFrames = new System.Collections.Generic.List<object>();
var lights = new System.Collections.Generic.List<SceneDirectionalLight>();
var producers = new System.Collections.Generic.Dictionary<string, object>();
Scene scene = null;
SceneCubemap environmentProbe = null;
CameraComponent camera = null;
ModelRenderer renderer = null;
SkinnedModelRenderer skinned = null;
Editor.SceneRenderingWidget widget = null;
Model model = null;
BaseFileSystem mount = null;
string privateMountRoot = null, privateCorpusLink = null, publicInputPath = null, resourcePrefix = null;
BaseFileSystem aggregate = null;
bool mounted = false, ownedModel = false, recipeComplete = true;
Action onFrame = null;
int widgetFrames = 0, width = 512, height = 512;
double timeoutSeconds = 90;
string id = null, inputPath = null, sourcePath = null, sourceHash = null, outputDir = null, recipeId = null;
string loadStatus = "blocked", loadError = "Not attempted", renderStatus = "blocked", renderError = "Not attempted", stopReason = null;
object captureResult = null, cameraApplied = null, presentationApplied = null, environmentApplied = null, animationApplied = null, boundsObserved = null, drawObserved = null;
System.Text.Json.JsonDocument requestDocument = null;
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
void Require( bool value, string message ) { if ( !value ) throw new InvalidOperationException( message ); }
string Hash( byte[] bytes ) => Convert.ToHexStringLower( System.Security.Cryptography.SHA256.HashData( bytes ) );
string HashFile( string path ) { using var stream = System.IO.File.OpenRead( path ); return Convert.ToHexStringLower( System.Security.Cryptography.SHA256.HashData( stream ) ); }
string DiskMvid( string path )
{
    using var stream = System.IO.File.OpenRead( path );
    using var pe = new System.Reflection.PortableExecutable.PEReader( stream );
    var metadata = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader( pe );
    return metadata.GetGuid( metadata.GetModuleDefinition().Mvid ).ToString();
}
System.Text.Json.JsonElement Field( System.Text.Json.JsonElement value, string name ) => value.ValueKind == System.Text.Json.JsonValueKind.Object && value.TryGetProperty( name, out var found ) ? found : default;
string Text( System.Text.Json.JsonElement value ) => value.ValueKind == System.Text.Json.JsonValueKind.String ? value.GetString() : null;
double Number( System.Text.Json.JsonElement value, double fallback ) => value.ValueKind == System.Text.Json.JsonValueKind.Number && value.TryGetDouble( out var number ) && double.IsFinite( number ) ? number : fallback;
float[] Point( Vector3 value ) => new[] { value.x, value.y, value.z };
bool Finite( Vector3 value ) => float.IsFinite( value.x ) && float.IsFinite( value.y ) && float.IsFinite( value.z );
bool Vector( System.Text.Json.JsonElement value, out Vector3 vector )
{
    vector = Vector3.Zero;
    if ( value.ValueKind != System.Text.Json.JsonValueKind.Array || value.GetArrayLength() != 3 ) return false;
    var numbers = value.EnumerateArray().Select( row => Number( row, double.NaN ) ).ToArray();
    if ( numbers.Any( number => !double.IsFinite( number ) || Math.Abs( number ) > float.MaxValue ) ) return false;
    vector = new Vector3( (float)numbers[0], (float)numbers[1], (float)numbers[2] ); return true;
}
Vector3 EngineDirection( Vector3 value ) => new Vector3( value.z, value.x, value.y );
Vector3 EnginePosition( Vector3 value ) => EngineDirection( value ) / .0254f;
Color ReadColor( System.Text.Json.JsonElement value, Color fallback )
{
    if ( value.ValueKind != System.Text.Json.JsonValueKind.Array || value.GetArrayLength() < 3 || value.GetArrayLength() > 4 ) return fallback;
    var numbers = value.EnumerateArray().Select( row => Number( row, double.NaN ) ).ToArray();
    if ( numbers.Any( number => !double.IsFinite( number ) || number < 0 || number > float.MaxValue ) ) return fallback;
    return new Color( (float)numbers[0], (float)numbers[1], (float)numbers[2], numbers.Length == 4 ? (float)numbers[3] : 1 );
}
void AtomicBytes( string path, byte[] bytes )
{
    var pending = path + ".pending";
    System.IO.File.WriteAllBytes( pending, bytes ); System.IO.File.Move( pending, path, true );
}
object crashInstance = null;
object crashNativeOwners = null;
var crashTraceErrors = new System.Collections.Concurrent.ConcurrentQueue<string>();
var crashTraceWriteGate = new object();
int crashCallbacksActive = 0, crashCallbacksPeak = 0, crashCallbackEntries = 0;
int crashRenderBodiesActive = 0, crashRenderBodiesPeak = 0;
object ProbeDiagnostic( string name )
{
    if ( crashInstance is null ) return null;
    try { return crashInstance.GetType().GetProperty( name, flags )?.GetValue( crashInstance ); }
    catch ( Exception error ) { return error.GetType().Name + ": " + error.Message; }
}
void Checkpoint( string phase )
{
    // Append/flush before each synchronous native boundary, so a native crash
    // cannot erase the last reached phase. No graphics calls or readback here.
    if ( outputDir is null ) return;
    try
    {
        System.IO.Directory.CreateDirectory( outputDir );
        var row = new { phase, elapsedSeconds = timer.Elapsed.TotalSeconds,
            engineFrame = Application.FrameCount, widgetFrames, mainThread = ThreadSafe.IsMainThread,
            managedThread = Environment.CurrentManagedThreadId,
            instanceIdentity = crashInstance is null ? (int?)null : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode( crashInstance ),
            callbacksActive = System.Threading.Volatile.Read( ref crashCallbacksActive ),
            callbacksPeak = System.Threading.Volatile.Read( ref crashCallbacksPeak ),
            renderBodiesActive = System.Threading.Volatile.Read( ref crashRenderBodiesActive ),
            renderBodiesPeak = System.Threading.Volatile.Read( ref crashRenderBodiesPeak ),
            nativeOwners = phase == "native_owners_observed" ? crashNativeOwners : null,
            graphicsActive = Graphics.IsActive, drawCalls = ProbeDiagnostic( "DrawCalls" ),
            requestedRevision = ProbeDiagnostic( "PoseRevision" ), submittedRevision = ProbeDiagnostic( "SubmittedRevision" ),
            callbackErrors = ProbeDiagnostic( "CallbackErrors" ), bounds = boundsObserved };
        var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes( row );
        lock ( crashTraceWriteGate )
        {
            using var stream = new System.IO.FileStream( System.IO.Path.Combine( outputDir, "instancing-crash-phases.jsonl" ),
                System.IO.FileMode.Append, System.IO.FileAccess.Write, System.IO.FileShare.Read, 4096, System.IO.FileOptions.WriteThrough );
            stream.Write( bytes ); stream.WriteByte( (byte)'\n' ); stream.Flush( true );
        }
    }
    catch ( Exception error ) { crashTraceErrors.Enqueue( phase + ": " + error ); }
}
string AttributePointer( object wrapper )
{
    if ( wrapper is null ) return null;
    var raw = wrapper.GetType().GetMethod( "Get", flags, null, Type.EmptyTypes, null ).Invoke( wrapper, null );
    return ((IntPtr)raw.GetType().GetField( "self", flags ).GetValue( raw )).ToString( "x" );
}

void ClearCaptureFiles()
{
    captureResult = null; animationFrames.Clear();
    if ( outputDir is null ) return;
    foreach ( var name in new[] { "capture.png", "capture.png.pending", "capture.apng", "capture.apng.pending" } )
    {
        var path = System.IO.Path.Combine( outputDir, name ); if ( System.IO.File.Exists( path ) ) System.IO.File.Delete( path );
    }
    var frames = System.IO.Path.Combine( outputDir, "frames" );
    if ( System.IO.Directory.Exists( frames ) )
        foreach ( var path in System.IO.Directory.EnumerateFiles( frames, "frame*.png*", System.IO.SearchOption.TopDirectoryOnly ) ) System.IO.File.Delete( path );
}
async Task SafePoint()
{
    cancellationToken.ThrowIfCancellationRequested();
    if ( timer.Elapsed.TotalSeconds > timeoutSeconds ) throw new TimeoutException( "Per-case capture deadline exceeded; synchronous Model.Load cannot be interrupted." );
    await MainThread.Wait();
    Require( Application.IsEditor && !Game.IsPlaying, "Public corpus capture requires an idle editor; human Play blocks the case." );
    Require( ConsoleSystem.GetValue( "r_managed_scene", "false" ).ToLowerInvariant() is "false" or "0", "Native capture requires the existing native renderer; no renderer toggle is permitted." );
    while ( Graphics.IsActive )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if ( timer.Elapsed.TotalSeconds > timeoutSeconds ) throw new TimeoutException( "Deadline waiting for a safe graphics boundary." );
        await Task.Delay( 20, cancellationToken ); await MainThread.Wait();
    }
}
async Task WaitFrames()
{
    await SafePoint(); var start = widgetFrames; var wait = System.Diagnostics.Stopwatch.StartNew();
    while ( widgetFrames < start + 3 )
    {
        if ( wait.Elapsed.TotalSeconds > 12 ) throw new TimeoutException( "Visible preview stopped receiving real editor frames." );
        await Task.Delay( 20, cancellationToken ); await SafePoint();
    }
}
void Clean( string label, Action action )
{
    try { action(); } catch ( Exception error ) { cleanupErrors.Add( label + ": " + error ); }
}
try
{
    requestDocument = System.Text.Json.JsonDocument.Parse( System.IO.File.ReadAllBytes( requestPath ) );
    var request = requestDocument.RootElement;
    id = Text( Field( request, "id" ) ); inputPath = Text( Field( request, "inputPath" ) ); sourcePath = Text( Field( request, "sourcePath" ) );
    Require( id == "SimpleInstancing/glTF" && inputPath == "Models/SimpleInstancing/glTF/SimpleInstancing.gltf", "This isolated crash fixture accepts only the actual SimpleInstancing/glTF case." );
    outputDir = Text( Field( request, "outputDir" ) );
    Require( !string.IsNullOrWhiteSpace( outputDir ) && System.IO.Path.IsPathFullyQualified( outputDir ), "outputDir must be absolute." );
    outputDir = System.IO.Path.GetFullPath( outputDir ); System.IO.Directory.CreateDirectory( outputDir );
    Require( !System.IO.File.Exists( System.IO.Path.Combine( outputDir, "instancing-crash-phases.jsonl" ) ), "Crash reproduction requires a fresh trace destination." );
    ClearCaptureFiles();
    var resultPath = System.IO.Path.Combine( outputDir, "result.json" );
    if ( System.IO.File.Exists( resultPath ) ) System.IO.File.Delete( resultPath );
    Require( Number( Field( request, "schemaVersion" ), Number( Field( request, "schema" ), Number( Field( request, "version" ), -1 ) ) ) == 1, "Expected request schema 1." );
    timeoutSeconds = Number( Field( request, "timeoutSeconds" ), 90 );
    Require( timeoutSeconds > 0 && timeoutSeconds <= 600, "timeoutSeconds must be in (0,600]." );
    var corpusRoot = Text( Field( request, "corpusRoot" ) );
    Require( System.IO.Path.IsPathFullyQualified( corpusRoot ?? "" ) && System.IO.Path.IsPathFullyQualified( sourcePath ?? "" ), "Corpus and source paths must be absolute." );
    corpusRoot = System.IO.Path.GetFullPath( corpusRoot ); sourcePath = System.IO.Path.GetFullPath( sourcePath );
    Require( !string.IsNullOrWhiteSpace( inputPath ) && !System.IO.Path.IsPathRooted( inputPath ) && !inputPath.Contains( '\\' ), "inputPath must be corpus-relative." );
    var expectedPath = System.IO.Path.GetFullPath( System.IO.Path.Combine( corpusRoot, inputPath ) );
    Require( expectedPath.StartsWith( corpusRoot.TrimEnd( System.IO.Path.DirectorySeparatorChar ) + System.IO.Path.DirectorySeparatorChar, StringComparison.Ordinal ) && expectedPath == sourcePath, "sourcePath must identify the mounted inputPath within corpusRoot." );
    sourceHash = HashFile( sourcePath );
    Require( sourceHash == "a01695630e6459c945826c7ff8dde74784de8279f60b9b62604980dc7486340e", "Crash reproduction requires the pinned upstream source bytes." );
    var recipe = Field( request, "recipe" ); recipeId = Text( Field( recipe, "id" ) );
    var requestedCamera = Field( recipe, "camera" ); var presentation = Field( recipe, "presentation" );
    var animation = Field( request, "animation" ); if ( animation.ValueKind != System.Text.Json.JsonValueKind.Object ) animation = Field( recipe, "animation" );
    var earlyTimes = Field( animation, "captureTimes" );
    if ( earlyTimes.ValueKind == System.Text.Json.JsonValueKind.Array )
    {
        Require( earlyTimes.GetArrayLength() > 0 && earlyTimes.GetArrayLength() <= 2048, "captureTimes requires 1..2048 frames." );
        timeoutSeconds = Math.Min( 600, Math.Max( timeoutSeconds, earlyTimes.GetArrayLength() * .3 + 30 ) );
    }
    var resolution = Field( presentation, "resolution" );
    width = (int)Number( Field( resolution, "width" ), 512 ); height = (int)Number( Field( resolution, "height" ), 512 );
    Require( width >= 64 && width <= 512 && height >= 64 && height <= 512, "Capture dimensions must be in [64,512]." );
    var engine = typeof( Model ).Assembly; var gltf = typeof( Sandbox.Gltf.GltfReader ).Assembly;
    var rendering = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault( assembly => assembly.GetName().Name == "Sandbox.SceneRenderer" );
    foreach ( var pair in new[] { (Name: "engine", Assembly: engine), (Name: "gltf", Assembly: gltf), (Name: "renderer", Assembly: rendering) } )
    {
        Require( pair.Assembly is not null, "Required loaded producer is absent: " + pair.Name );
        var loadedMvid = pair.Assembly.ManifestModule.ModuleVersionId.ToString(); var diskMvid = DiskMvid( pair.Assembly.Location );
        producers[pair.Name] = new { mvid = loadedMvid, diskMvid, path = pair.Assembly.Location, diskSha256 = HashFile( pair.Assembly.Location ) };
        Require( loadedMvid == diskMvid, "Loaded/disk producer MVID mismatch: " + pair.Name );
    }
    await SafePoint();
    aggregate = FileSystem.Mounted ?? throw new InvalidOperationException( "Mounted filesystem unavailable." );
    // A private resource prefix prevents collisions with any pre-existing cached model.
    resourcePrefix = "gltf_run_" + Guid.NewGuid().ToString( "N" );
    privateMountRoot = System.IO.Path.Combine( System.IO.Path.GetTempPath(), resourcePrefix );
    System.IO.Directory.CreateDirectory( privateMountRoot );
    privateCorpusLink = System.IO.Path.Combine( privateMountRoot, resourcePrefix );
    var corpusLink = System.IO.Directory.CreateSymbolicLink( privateCorpusLink, corpusRoot );
    Require( corpusLink.ResolveLinkTarget( true )?.FullName == corpusRoot, "Private corpus link did not resolve to the verified corpusRoot." );
    publicInputPath = resourcePrefix + "/" + inputPath;
    var localType = typeof( BaseFileSystem ).Assembly.GetType( "Sandbox.LocalFileSystem", true );
    var constructor = localType.GetConstructor( flags, null, new[] { typeof( string ), typeof( bool ) }, null ) ?? throw new MissingMethodException( "LocalFileSystem constructor" );
    mount = (BaseFileSystem)constructor.Invoke( new object[] { privateMountRoot, true } );
    mounted = true;
    (typeof( BaseFileSystem ).GetMethod( "Mount", flags, null, new[] { typeof( BaseFileSystem ) }, null ) ?? throw new MissingMethodException( "BaseFileSystem.Mount" )).Invoke( aggregate, new object[] { mount } );
    loadError = null; loadStatus = "loading";
    await SafePoint(); Checkpoint( "before_Model_Load" ); model = Model.Load( publicInputPath );
    ownedModel = model is not null && model.ResourcePath?.StartsWith( resourcePrefix + "/", StringComparison.Ordinal ) == true && !ReferenceEquals( model, Model.Error );
    Checkpoint( "after_Model_Load" );
    await SafePoint();
    Require( model is { IsValid: true, IsError: false } && ownedModel, "Model.Load returned null/invalid/error or non-owned resource." );
    // The script cannot directly access internal IModel. No native handle is constructed.
    var nativeField = typeof( Model ).GetField( "native", flags ) ?? throw new MissingFieldException( "Model.native" );
    var native = nativeField.GetValue( model );
    var nullProperty = native.GetType().GetProperty( "IsNull", flags ) ?? throw new MissingMemberException( "IModel.IsNull" );
    Require( nullProperty.GetValue( native ) is true, "Public glTF loading returned a native model placeholder." );
    loadStatus = "passed"; loadError = null;
    Require( model.MeshCount > 0, "Source contains zero drawable meshes; no substitute geometry is allowed." );
    var bounds = model.Bounds;
    Require( Finite( bounds.Mins ) && Finite( bounds.Maxs ), "Model bounds are nonfinite." );
    boundsObserved = new { minimumInches = Point( bounds.Mins ), maximumInches = Point( bounds.Maxs ), model.MeshCount, model.BoneCount, model.AnimationCount };
    Require( model.MeshCount == 125, "Actual instancing selection must expose exactly125 rows before native realization." );
    Checkpoint( "before_scene_create" ); scene = Scene.CreateEditorScene(); Checkpoint( "after_scene_create" );
    using ( scene.Push() ) camera = scene.CreateObject().Components.Create<CameraComponent>();
    var position = Vector3.Zero; var target = Vector3.Zero; var up = Vector3.Up;
    var projection = Text( Field( requestedCamera, "projection" ) );
    var recipeCameraValid = Text( Field( requestedCamera, "space" ) ) == "gltf-metres"
        && Vector( Field( requestedCamera, "position" ), out position ) && Vector( Field( requestedCamera, "target" ), out target )
        && Vector( Field( requestedCamera, "up" ), out up ) && (target - position).Length > .000001f && up.Length > .000001f
        && Vector3.Cross( target - position, up ).Length > .000001f && projection is "perspective" or "orthographic";
    var verticalFov = Number( Field( requestedCamera, "verticalFov" ), double.NaN );
    var orthoHeight = Number( Field( requestedCamera, "orthographicHeight" ), double.NaN );
    recipeCameraValid &= projection == "perspective" ? verticalFov > 0 && verticalFov < 179 : orthoHeight > 0 && double.IsFinite( orthoHeight );
    if ( recipeCameraValid )
    {
        camera.WorldPosition = EnginePosition( position ); target = EnginePosition( target ); up = EngineDirection( up ).Normal;
        camera.Orthographic = projection == "orthographic";
        if ( camera.Orthographic ) camera.OrthographicHeight = (float)(orthoHeight / .0254);
        else { camera.FovAxis = CameraComponent.Axis.Vertical; camera.FieldOfView = (float)verticalFov; }
    }
    else
    {
        target = (bounds.Mins + bounds.Maxs) * .5f; var diagonal = Math.Max( (bounds.Maxs - bounds.Mins).Length, 1 );
        camera.WorldPosition = target + new Vector3( 1, 1, .65f ).Normal * diagonal * 3; up = Vector3.Up;
        camera.Orthographic = true; camera.OrthographicHeight = diagonal * 1.25f;
        recipeComplete = false; limitations.Add( "Missing/invalid recipe camera: fitted public model.Bounds; not a Khronos matched view." );
    }
    camera.WorldRotation = Rotation.LookAt( target - camera.WorldPosition, up );
    camera.ZNear = .01f; camera.ZFar = Math.Max( 1000, (camera.WorldPosition - target).Length + (bounds.Maxs - bounds.Mins).Length * 4 );
    camera.CustomSize = new Vector2( width, height );
    camera.BackgroundColor = ReadColor( Field( Field( presentation, "background" ), "color" ), Color.Black );
    camera.ClearFlags = ClearFlags.All; camera.DebugMode = SceneCameraDebugMode.Normal; camera.EnablePostProcessing = true;
    var exposure = Number( Field( presentation, "exposure" ), 1 ); Require( exposure > 0 && exposure <= 100, "Exposure must be finite in (0,100]." );
    camera.AutoExposure.Enabled = true; camera.AutoExposure.MinimumExposure = (float)exposure; camera.AutoExposure.MaximumExposure = (float)exposure; camera.AutoExposure.Compensation = 0;
    cameraApplied = new { source = recipeCameraValid ? "recipe" : "bounds-fit", positionInches = Point( camera.WorldPosition ), targetInches = Point( target ), up = Point( up ), camera.Orthographic, camera.OrthographicHeight, camera.FieldOfView, fovAxis = camera.FovAxis.ToString(), camera.ZNear, camera.ZFar, width, height };
    var appliedLights = new System.Collections.Generic.List<object>(); var requestedLights = Field( presentation, "lights" );
    if ( requestedLights.ValueKind == System.Text.Json.JsonValueKind.Array )
    {
        Require( requestedLights.GetArrayLength() <= 8, "At most eight recipe lights per bounded case." );
        foreach ( var light in requestedLights.EnumerateArray() )
        {
            if ( Text( Field( light, "type" ) ) != "directional" || !Vector( Field( light, "direction" ), out var direction ) || direction.Length < .000001f ) { recipeComplete = false; limitations.Add( "Unsupported/invalid recipe light omitted; no default light substituted." ); continue; }
            var lightDirection = EngineDirection( direction ).Normal; var color = ReadColor( Field( light, "color" ), Color.White );
            var intensity = Number( Field( light, "intensity" ), 1 ); Require( intensity >= 0 && intensity <= 100000, "Invalid directional light intensity." );
            var rotation = Rotation.LookAt( lightDirection, Math.Abs( lightDirection.z ) > .99f ? Vector3.Left : Vector3.Up );
            var created = new SceneDirectionalLight( scene.SceneWorld, rotation, color * (float)intensity ); lights.Add( created );
            appliedLights.Add( new { type = "directional", direction = Point( lightDirection ), color = new[] { color.r, color.g, color.b, color.a }, intensity } );
        }
    }
    // Opt-in only: legacy environment descriptions do not create an implicit probe.
    var environment = Field( presentation, "environment" );
    if ( Text( Field( environment, "type" ) ) == "cubemap" )
    {
        var texturePath = Text( Field( environment, "texturePath" ) );
        var environmentSourcePath = Text( Field( environment, "sourcePath" ) );
        var compiledPath = Text( Field( environment, "compiledPath" ) );
        var sourceExpected = Text( Field( environment, "sourceSha256" ) );
        var compiledExpected = Text( Field( environment, "compiledSha256" ) );
        Require( !string.IsNullOrWhiteSpace( texturePath ) && !System.IO.Path.IsPathRooted( texturePath ) && !texturePath.Contains( ".." ) && !texturePath.Contains( '\\' ), "Environment texturePath must be a mounted resource-relative path." );
        Require( System.IO.Path.IsPathFullyQualified( environmentSourcePath ?? "" ) && System.IO.File.Exists( environmentSourcePath ), "Environment source file is unavailable." );
        Require( System.IO.Path.IsPathFullyQualified( compiledPath ?? "" ) && System.IO.File.Exists( compiledPath ), "Compiled environment file is unavailable." );
        var sourceActual = HashFile( environmentSourcePath ); var compiledActual = HashFile( compiledPath );
        Require( sourceActual == sourceExpected && compiledActual == compiledExpected, "Environment source or compiled hash differs from the request." );
        var texture = Texture.Load( texturePath );
        Require( texture is { IsValid: true, IsError: false } && texture.Depth == 6 && texture.Width == texture.Height && texture.Mips >= 2, "Environment resource is not a valid square cubemap with mips." );
        var size = Number( Field( environment, "sizeInches" ), double.NaN );
        var yaw = Number( Field( environment, "yawDegrees" ), double.NaN );
        var tint = ReadColor( Field( environment, "tint" ), Color.White );
        Require( double.IsFinite( size ) && size > 0 && size <= 100000000 && double.IsFinite( yaw ) && Math.Abs( yaw ) <= 3600, "Environment size or yaw is invalid." );
        var center = (bounds.Mins + bounds.Maxs) * .5f;
        var extent = bounds.Maxs - bounds.Mins;
        Require( size > Math.Max( extent.x, Math.Max( extent.y, extent.z ) ) * 2, "Environment probe bounds must cover the model with margin." );
        environmentProbe = new SceneCubemap( scene.SceneWorld, texture, BBox.FromPositionAndSize( Vector3.Zero, (float)size ) );
        environmentProbe.Projection = SceneCubemap.ProjectionMode.Sphere;
        environmentProbe.Transform = Transform.Zero.WithPosition( center ).WithRotation( Rotation.FromYaw( (float)yaw ) );
        environmentProbe.TintColor = tint; environmentProbe.Feathering = .01f; environmentProbe.RenderDirty();
        Require( environmentProbe.IsValid() && environmentProbe.Texture is { IsValid: true, IsError: false }, "Environment probe did not retain the cubemap." );
        environmentApplied = new { type = "cubemap", texturePath, textureResourcePath = texture.ResourcePath, sourcePath = environmentSourcePath, sourceSha256 = sourceActual, compiledPath, compiledSha256 = compiledActual, texture.Width, texture.Height, texture.Depth, texture.Mips, format = texture.ImageFormat.ToString(), centerInches = Point( center ), sizeInches = size, yawDegrees = yaw, tint = new[] { tint.r, tint.g, tint.b, tint.a }, projection = environmentProbe.Projection.ToString(), limitations = "Loaded resource-to-compiled-file identity, orientation and actual IBL pixels require native validation." };
    }
    if ( appliedLights.Count == 0 && environmentProbe is null ) limitations.Add( "No valid recipe lights; no implicit light/sky/environment substituted." );
    presentationApplied = new { background = new[] { camera.BackgroundColor.r, camera.BackgroundColor.g, camera.BackgroundColor.b, camera.BackgroundColor.a }, lights = appliedLights, environment = environmentApplied, exposure, exposureLocked = true, nativeDebugMode = "Normal", postProcessing = true };
    using ( scene.Push() )
    {
        if ( model.BoneCount > 0 || model.AnimationCount > 0 )
        {
            skinned = scene.CreateObject().Components.Create<SkinnedModelRenderer>( false ); renderer = skinned;
            skinned.UseAnimGraph = false; skinned.PlayAnimationsInEditorScene = true; skinned.PlaybackRate = 0;
        }
        else renderer = scene.CreateObject().Components.Create<ModelRenderer>( false );
        renderer.WorldTransform = Transform.Zero; renderer.Model = model;
        Checkpoint( "before_renderer_enable" ); renderer.Enabled = true; Checkpoint( "after_renderer_enable" ); scene.EditorTick( 0, 0 ); Checkpoint( "after_editor_tick" );
    }
    Require( renderer.SceneObject is SceneCustomObject && renderer.SceneObject.Model is null, "Public renderer must own a custom object without a native model." );
    var instance = (typeof( ModelRenderer ).GetProperty( "GltfInstance", flags ) ?? throw new MissingMemberException( "ModelRenderer.GltfInstance" )).GetValue( renderer );
    Require( instance is not null, "Public renderer has no managed draw instance." );
    crashInstance = instance; Checkpoint( "managed_instance_observed" );
    // Record wrapper identities and raw interop self fields without invoking a
    // native getter or GPU readback. All pointers belong to this owned fixture.
    var drawsField = instance.GetType().GetField( "draws", flags );
    var drawOwners = new System.Collections.Generic.List<object>(); var drawOwnerIndex = 0;
    foreach ( var draw in (System.Collections.IEnumerable)drawsField.GetValue( instance ) )
    {
        var type = draw.GetType();
        drawOwners.Add( new { row = drawOwnerIndex++,
            computeAttributes = AttributePointer( type.GetField( "Attributes", flags ).GetValue( draw ) ),
            renderAttributes = AttributePointer( type.GetField( "RenderAttributes", flags ).GetValue( draw ) ) } );
    }
    Require( drawOwners.Count == 125, "Actual managed instance must retain125 draw rows." );
    var crashAsset = instance.GetType().GetField( "asset", flags ).GetValue( instance );
    var crashCompute = (Material)crashAsset.GetType().GetProperty( "ComputeMaterial", flags ).GetValue( crashAsset );
    var materialOwners = new System.Collections.Generic.List<object>();
    foreach ( var primitive in (System.Collections.IEnumerable)crashAsset.GetType().GetField( "Primitives", flags ).GetValue( crashAsset ) )
    {
        var material = (Material)primitive.GetType().GetField( "Material", flags ).GetValue( primitive );
        materialOwners.Add( new { identity = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode( material ), attributes = AttributePointer( material.Attributes ) } );
    }
    crashNativeOwners = new { instanceIdentity = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode( instance ),
        customObjectIdentity = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode( renderer.SceneObject ),
        assetIdentity = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode( crashAsset ),
        computeMaterialIdentity = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode( crashCompute ),
        computeMaterialAttributes = AttributePointer( crashCompute.Attributes ), materialOwners, draws = drawOwners };
    Checkpoint( "native_owners_observed" );
    var customObject = (SceneCustomObject)renderer.SceneObject;
    var originalRenderCallback = customObject.RenderOverride;
    Require( originalRenderCallback is not null, "Owned public renderer has no actual native callback." );
    // Native still invokes this callback using its real Graphics.Scope. The
    // observer does not supply a context, call Render manually, or serialize it.
    customObject.RenderOverride = obj =>
    {
        var active = System.Threading.Interlocked.Increment( ref crashCallbacksActive );
        int peak;
        do { peak = System.Threading.Volatile.Read( ref crashCallbacksPeak ); }
        while ( active > peak && System.Threading.Interlocked.CompareExchange( ref crashCallbacksPeak, active, peak ) != peak );
        var ordinal = System.Threading.Interlocked.Increment( ref crashCallbackEntries );
        if ( ordinal <= 32 ) Checkpoint( "native_callback_enter_" + ordinal );
        // This second counter covers only the original callback body. Entry
        // trace IO has completed, and exit trace IO starts after decrement.
        var bodiesActive = System.Threading.Interlocked.Increment( ref crashRenderBodiesActive );
        int bodiesPeak;
        do { bodiesPeak = System.Threading.Volatile.Read( ref crashRenderBodiesPeak ); }
        while ( bodiesActive > bodiesPeak && System.Threading.Interlocked.CompareExchange( ref crashRenderBodiesPeak, bodiesActive, bodiesPeak ) != bodiesPeak );
        try { originalRenderCallback( obj ); }
        finally
        {
            System.Threading.Interlocked.Decrement( ref crashRenderBodiesActive );
            System.Threading.Interlocked.Decrement( ref crashCallbacksActive );
            if ( ordinal <= 32 ) Checkpoint( "native_callback_exit_" + ordinal );
        }
    };
    object Diagnostic( string name ) => (instance.GetType().GetProperty( name, flags ) ?? throw new MissingMemberException( "GltfInstance." + name )).GetValue( instance );
    var clipName = Text( Field( animation, "name" ) ); var clip = Field( animation, "index" ); if ( clip.ValueKind == System.Text.Json.JsonValueKind.Undefined ) clip = Field( animation, "clip" );
    var requestedIndex = Number( clip, double.NaN );
    if ( string.IsNullOrEmpty( clipName ) && double.IsFinite( requestedIndex ) )
    {
        Require( requestedIndex == Math.Truncate( requestedIndex ) && requestedIndex >= 0 && requestedIndex < model.AnimationCount, "Requested animation index is out of range." ); clipName = model.AnimationNames[(int)requestedIndex];
    }
    if ( clipName is not null ) { Require( skinned is not null && model.AnimationNames.Contains( clipName ), "Requested animation is unavailable." ); skinned.Sequence.Name = clipName; }
    else if ( model.AnimationCount > 0 ) { clipName = model.AnimationNames[0]; skinned.Sequence.Name = clipName; limitations.Add( "No requested clip: captured first public animation sequence." ); }
    var loopedSeeks = Field( animation, "loop" ).ValueKind == System.Text.Json.JsonValueKind.True;
    if ( skinned is not null && clipName is not null ) skinned.Sequence.Looping = loopedSeeks;
    var simultaneous = Field( animation, "simultaneousClips" );
    var requestedClips = simultaneous.ValueKind == System.Text.Json.JsonValueKind.Array ? simultaneous.EnumerateArray().Select( value => value.Clone() ).ToArray() : Array.Empty<System.Text.Json.JsonElement>();
    var requirements = Field( animation, "requires" );
    var requestedRequirements = requirements.ValueKind == System.Text.Json.JsonValueKind.Array ? requirements.EnumerateArray().Select( value => value.ToString() ).ToArray() : Array.Empty<string>();
    var appliedClipCount = clipName is null ? 0 : 1;
    if ( requestedClips.Length > 1 || requestedRequirements.Length > 0 )
    {
        recipeComplete = false;
        limitations.Add( $"Recipe requests {requestedClips.Length} simultaneous clips; this capture applies {appliedClipCount} public Sequence only. Requirements not fulfilled: " + string.Join( "; ", requestedRequirements ) );
    }
    var times = new System.Collections.Generic.List<double>(); var planned = Field( animation, "captureTimes" );
    if ( planned.ValueKind == System.Text.Json.JsonValueKind.Array )
    {
        Require( planned.GetArrayLength() > 0 && planned.GetArrayLength() <= 2048, "captureTimes requires 1..2048 frames." );
        foreach ( var time in planned.EnumerateArray() ) { var value = Number( time, double.NaN ); Require( double.IsFinite( value ) && value >= 0 && value <= float.MaxValue, "Invalid capture time." ); times.Add( value ); }
        // Source time may wrap at a looping clip boundary. Presentation time is
        // separately described by positive recording delays, never source deltas.
        Require( loopedSeeks || times.Zip( times.Skip( 1 ), (a, b) => b > a ).All( value => value ), "Non-looping capture times must increase strictly." );
    }
    else times.Add( Number( Field( animation, "time" ), 0 ) );
    timeoutSeconds = Math.Min( 600, Math.Max( timeoutSeconds, times.Count * .3 + 30 ) );
    Require( times.All( time => double.IsFinite( time ) && time >= 0 && time <= float.MaxValue ), "Invalid requested time." );
    Require( times.Count == 1 || clipName is not null, "Multiple capture times require an authored animation clip." );
    if ( loopedSeeks && clipName is not null ) Require( times.All( time => time <= skinned.Sequence.Duration ), "Looped seek time exceeds the public clip duration." );
    var recording = Field( animation, "recording" ); var presentationDurations = new System.Collections.Generic.List<double>();
    if ( Text( Field( recording, "kind" ) ) == "apng" )
    {
        var durations = Field( recording, "frameDurationsSeconds" );
        Require( durations.ValueKind == System.Text.Json.JsonValueKind.Array && durations.GetArrayLength() == times.Count && Number( Field( recording, "frameCount" ), -1 ) == times.Count, "Recording seek/duration inventory differs." );
        foreach ( var delay in durations.EnumerateArray() ) { var value = Number( delay, double.NaN ); Require( double.IsFinite( value ) && value > 0, "Presentation frame durations must be strictly positive." ); presentationDurations.Add( value ); }
        var declaredDuration = Number( Field( recording, "duration" ), double.NaN );
        Require( double.IsFinite( declaredDuration ) && declaredDuration > 0 && Math.Abs( presentationDurations.Sum() - declaredDuration ) <= Math.Max( 1e-7, declaredDuration * 1e-7 ), "Recording presentation duration differs from positive frame delays." );
    }
    else Require( times.Count <= 1 || times.Zip( times.Skip( 1 ), (a, b) => b > a ).All( value => value ), "Wrapped multi-frame seeks require explicit positive recording delays." );
    var rate = Number( Field( animation, "rate" ), 1 ); var duration = Number( Field( animation, "duration" ), skinned is not null && clipName is not null ? skinned.Sequence.Duration : 0 );
    animationApplied = new { clip = clipName, captureTimes = times, loop = loopedSeeks, frameDurationsSeconds = presentationDurations, requestedClips, requestedClipCount = requestedClips.Length > 0 ? requestedClips.Length : appliedClipCount, appliedClipCount, requires = requestedRequirements, requestedRate = rate, duration, playbackRate = 0, mode = "public Sequence.Time deterministic seeks; not real-time playback" };
    // Allocate then configure: references exist before any subsequent operation can throw.
    widget = new Editor.SceneRenderingWidget(); widget.Scene = scene; widget.Camera = camera;
    widget.WindowTitle = "glTF Run case: " + id; widget.Size = new Vector2( width, height );
    onFrame = () => widgetFrames++; widget.OnPreFrame += onFrame; Checkpoint( "before_widget_show" ); widget.Show(); Checkpoint( "after_widget_show" );
    renderStatus = "capturing"; renderError = null;
    var frameDir = System.IO.Path.Combine( outputDir, "frames" ); if ( times.Count > 1 ) System.IO.Directory.CreateDirectory( frameDir );
    for ( var index = 0; index < times.Count; index++ )
    {
        await SafePoint();
        if ( skinned is not null && clipName is not null ) skinned.Sequence.Time = (float)times[index];
        using ( scene.Push() ) scene.EditorTick( 0, 0 );
        Checkpoint( "before_initial_frames" ); await WaitFrames(); Checkpoint( "after_initial_frames" );
        // Public RenderToTexture initializes the native camera outside Graphics.IsActive.
        using var targetTexture = Texture.CreateRenderTarget().WithSize( width, height ).WithFormat( ImageFormat.RGBA8888 ).WithInitialColor( Color.Magenta ).Create( "gltf_run_" + Guid.NewGuid().ToString( "N" ) );
        bool submitted; using ( scene.Push() ) { targetTexture.Clear( Color.Magenta ); Checkpoint( "before_RenderToTexture" ); submitted = camera.RenderToTexture( targetTexture ); Checkpoint( "after_RenderToTexture" ); }
        Require( submitted, "Native camera rejected capture submission." ); Checkpoint( "before_postcapture_frames" ); await WaitFrames(); Checkpoint( "after_postcapture_frames" );
        Checkpoint( "before_GetBitmap" ); using var bitmap = targetTexture.GetBitmap(); Checkpoint( "after_GetBitmap" ); Require( bitmap is not null && bitmap.Width == width && bitmap.Height == height, "Native target returned no correctly sized bitmap." );
        var pixels = bitmap.GetPixels(); Require( pixels.Length == width * height, "Native capture pixel count differs." );
        var callbackErrors = (string[])Diagnostic( "CallbackErrors" );
        var drawCalls = Convert.ToInt32( Diagnostic( "DrawCalls" ) );
        var requestedRevision = Convert.ToInt64( Diagnostic( "PoseRevision" ) ); var submittedRevision = Convert.ToInt64( Diagnostic( "SubmittedRevision" ) );
        drawObserved = new { renderer = renderer.GetType().Name, resourcePath = model.ResourcePath, callbackErrors, drawCalls, requestedRevision, submittedRevision, nativeCaptureSubmitted = submitted, widgetFrames, bounds = boundsObserved };
        Require( callbackErrors.Length == 0, "Managed draw callback failed: " + string.Join( "\n", callbackErrors ) );
        Require( drawCalls > 0 && submittedRevision == requestedRevision, "Native capture completed without a successful current managed draw submission." );
        Require( !pixels.All( pixel => pixel.r > .99f && pixel.g < .01f && pixel.b > .99f ), "Native target retained its magenta initialization." );
        Checkpoint( "before_ToPng" ); var png = bitmap.ToPng(); Checkpoint( "after_ToPng" ); var path = times.Count == 1 ? System.IO.Path.Combine( outputDir, "capture.png" ) : System.IO.Path.Combine( frameDir, "frame" + index.ToString( "D4" ) + ".png" );
        AtomicBytes( path, png ); if ( index == 0 && times.Count > 1 ) AtomicBytes( System.IO.Path.Combine( outputDir, "capture.png" ), png );
        var actualTime = skinned is not null && clipName is not null ? skinned.Sequence.Time : (float?)null;
        animationFrames.Add( new { index, requestedTime = times[index], publicSeekTime = (float)times[index], actualTime, path, sha256 = Hash( png ), engineFrame = Application.FrameCount, wallSeconds = timer.Elapsed.TotalSeconds, width, height } );

        if ( index == 0 ) captureResult = new { path = System.IO.Path.Combine( outputDir, "capture.png" ), sha256 = Hash( png ), kind = "image" };
        await SafePoint();
    }
    limitations.Add( "Captured default source materials through public components; visual approval, independent geometry/material parity and performance remain pending." );
    renderStatus = "passed"; renderError = null;
}
catch ( Exception error )
{
    stopReason = error is OperationCanceledException ? "cancelled" : error is TimeoutException ? "timeout" : Game.IsPlaying ? "human_play_active" : null;
    if ( loadStatus != "passed" ) { loadStatus = stopReason is null ? "failed" : "blocked"; loadError = error.ToString(); }
    renderStatus = stopReason is null ? "failed" : "blocked"; renderError = error.ToString();
}
finally
{
    // Cancellation never skips owned cleanup, and each cleanup is independent.
    await MainThread.Wait(); Checkpoint( "before_cleanup" );
    Clean( "renderer", () => { if ( renderer.IsValid() ) { using var scope = scene.Push(); renderer.GameObject.Destroy(); scene.ProcessDeletes(); } } );
    Clean( "widget callback", () => { if ( widget is not null && onFrame is not null ) widget.OnPreFrame -= onFrame; } );
    Clean( "widget close", () => { if ( widget is not null && widget.IsValid ) widget.Close(); } );
    Clean( "widget destroy", () => { if ( widget is not null && widget.IsValid ) widget.Destroy(); } );
    foreach ( var light in lights ) Clean( "light", () => { if ( light.IsValid() ) light.Delete(); } );
    Clean( "environment probe", () => { if ( environmentProbe.IsValid() ) environmentProbe.Delete(); } );
    Checkpoint( "after_renderer_and_widget_cleanup" );
    Clean( "scene", () => { if ( scene.IsValid() ) scene.Destroy(); } ); Checkpoint( "after_scene_cleanup" );
    Clean( "model", () => { if ( ownedModel ) (typeof( Model ).GetMethod( "Destroy", flags, null, Type.EmptyTypes, null ) ?? throw new MissingMethodException( "Model.Destroy" )).Invoke( model, null ); } );
    Checkpoint( "after_model_cleanup" );
    Clean( "unmount", () => { if ( mounted ) (typeof( BaseFileSystem ).GetMethod( "UnMount", flags, null, new[] { typeof( BaseFileSystem ) }, null ) ?? throw new MissingMethodException( "BaseFileSystem.UnMount" )).Invoke( aggregate, new object[] { mount } ); } );
    Clean( "filesystem", () => { if ( mount is not null ) (mount.GetType().GetMethod( "Dispose", flags, null, Type.EmptyTypes, null ) ?? throw new MissingMethodException( "LocalFileSystem.Dispose" )).Invoke( mount, null ); } );
    Clean( "private corpus link", () => { if ( privateCorpusLink is not null && System.IO.Directory.Exists( privateCorpusLink ) ) System.IO.Directory.Delete( privateCorpusLink, false ); } );
    Clean( "private mount directory", () => { if ( privateMountRoot is not null && System.IO.Directory.Exists( privateMountRoot ) ) System.IO.Directory.Delete( privateMountRoot, false ); } );
    Clean( "request", () => requestDocument?.Dispose() );
    if ( cleanupErrors.Count > 0 ) { renderStatus = "failed"; renderError = (renderError ?? "") + "\nOwned cleanup failed; stop Run."; stopReason = "cleanup_failure"; }
    Checkpoint( "after_cleanup" );
    if ( renderStatus != "passed" ) Clean( "failed capture invalidation", ClearCaptureFiles );
    if ( cleanupErrors.Count > 0 ) { captureResult = null; renderStatus = "failed"; stopReason = "cleanup_failure"; }
}
var result = new System.Collections.Generic.Dictionary<string, object>
{
    ["crashTraceErrors"] = crashTraceErrors.ToArray(),
    ["crashCallbackObservation"] = new { entries = crashCallbackEntries,
        callbackIncludingObserverPeak = crashCallbacksPeak, callbackIncludingObserverActive = crashCallbacksActive,
        originalRenderBodyPeak = crashRenderBodiesPeak, originalRenderBodyActive = crashRenderBodiesActive,
        scope = "Outer count includes observer logging; original body count excludes checkpoint IO and surrounds only the original actual native callback.", nativeOwners = crashNativeOwners },
    ["id"] = id, ["input"] = new { path = inputPath, sourcePath, sha256 = sourceHash },
    ["load"] = new { status = loadStatus, error = loadError }, ["render"] = new { status = renderStatus, error = renderError, observation = drawObserved, visualApproval = "pending" },
    ["capture"] = captureResult, ["animationFrames"] = animationFrames, ["recipeId"] = recipeId,
    ["recipeApplied"] = new { complete = recipeComplete && renderStatus == "passed", camera = cameraApplied, presentation = presentationApplied, animation = animationApplied, limitations },
    ["producer"] = producers, ["cleanup_errors"] = cleanupErrors, ["cleanupConfirmed"] = cleanupErrors.Count == 0, ["stopReason"] = stopReason, ["elapsedSeconds"] = timer.Elapsed.TotalSeconds
};
if ( outputDir is not null ) AtomicBytes( System.IO.Path.Combine( outputDir, "result.json" ), System.Text.Json.JsonSerializer.SerializeToUtf8Bytes( result, jsonOptions ) );
return result;
