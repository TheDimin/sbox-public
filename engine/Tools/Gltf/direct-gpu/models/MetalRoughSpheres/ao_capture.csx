// One requested corpus case, through public Model.Load and public renderer components.
// Coordinator owns editor admission and serial execution. A capture is not visual approval.
var requestPath = System.IO.Path.Combine( exchangeDirectory, "gltf-run-case.json" );
var timer = System.Diagnostics.Stopwatch.StartNew();
var jsonOptions = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
var limitations = new System.Collections.Generic.List<string>();
var cleanupErrors = new System.Collections.Generic.List<string>();
var animationFrames = new System.Collections.Generic.List<object>();
var gpuReadbacks = new System.Collections.Generic.List<object>();
var exposureReceipt = new System.Collections.Generic.Dictionary<string, object>();
var explicitPresentationReceipt = new System.Collections.Generic.Dictionary<string, object>();
var lights = new System.Collections.Generic.List<SceneDirectionalLight>();
var producers = new System.Collections.Generic.Dictionary<string, object>();
Scene scene = null;
SceneCubemap environmentProbe = null;
SceneSkyBox environmentSky = null;
Material environmentSkyMaterial = null;
CameraComponent camera = null;
Tonemapping explicitTonemapping = null;
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
object materialProof = null, aoProof = null;
Material materialObserved = null;
var aoMaterials = new System.Collections.Generic.List<Material>();
var aoTextures = new System.Collections.Generic.List<Texture>();
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
    outputDir = Text( Field( request, "outputDir" ) );
    Require( !string.IsNullOrWhiteSpace( outputDir ) && System.IO.Path.IsPathFullyQualified( outputDir ), "outputDir must be absolute." );
    outputDir = System.IO.Path.GetFullPath( outputDir ); System.IO.Directory.CreateDirectory( outputDir );
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
    var recipe = Field( request, "recipe" ); recipeId = Text( Field( recipe, "id" ) );
    var requestedCamera = Field( recipe, "camera" ); var presentation = Field( recipe, "presentation" );
    var debugName = Text( Field( request, "debugMode" ) ) ?? "Normal";
    Require( debugName is "Normal" or "NormalMap" or "Roughness", "Unsupported diagnostic debug mode." );
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
    await SafePoint(); model = Model.Load( publicInputPath );
    ownedModel = model is not null && model.ResourcePath?.StartsWith( resourcePrefix + "/", StringComparison.Ordinal ) == true && !ReferenceEquals( model, Model.Error );
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
    scene = Scene.CreateEditorScene();
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
    camera.ClearFlags = ClearFlags.All; camera.DebugMode = Enum.Parse<SceneCameraDebugMode>( debugName ); camera.EnablePostProcessing = true;
    var exposure = Number( Field( presentation, "exposure" ), 1 ); Require( exposure > 0 && exposure <= 100, "Exposure must be finite in (0,100]." );
    camera.AutoExposure.Enabled = true; camera.AutoExposure.MinimumExposure = (float)exposure; camera.AutoExposure.MaximumExposure = (float)exposure; camera.AutoExposure.Compensation = 0;
    var tonemapping = Field( presentation, "tonemapping" );
    var exposureCompensationEv = 0.0;
    if ( tonemapping.ValueKind != System.Text.Json.JsonValueKind.Undefined )
    {
        Require( tonemapping.ValueKind == System.Text.Json.JsonValueKind.Object, "tonemapping must be an explicit object." );
        var mode = Text( Field( tonemapping, "mode" ) );
        Require( mode is "ACES" or "HableFilmic" or "ReinhardJodie" or "Linear" or "AgX", "Unsupported native tonemapping mode." );
        exposureCompensationEv = Number( Field( tonemapping, "exposureCompensationEv" ), double.NaN );
        Require( double.IsFinite( exposureCompensationEv ) && Math.Abs( exposureCompensationEv ) <= 5, "Explicit exposureCompensationEv must be in [-5,5]." );
        // Editor scene ticks reset camera-only exposure to [1,2]. A colocated
        // stock effect restores the requested settings during effect collection.
        using ( scene.Push() ) explicitTonemapping = camera.GameObject.Components.Create<Tonemapping>();
        explicitTonemapping.Mode = Enum.Parse<Tonemapping.TonemappingMode>( mode );
        explicitTonemapping.AutoExposureEnabled = true;
        explicitTonemapping.MinimumExposure = (float)exposure;
        explicitTonemapping.MaximumExposure = (float)exposure;
        explicitTonemapping.ExposureCompensation = (float)exposureCompensationEv;
        explicitTonemapping.Rate = 20;
    }
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
        var visibleSky = Field( environment, "visibleSky" );
        Require( visibleSky.ValueKind is System.Text.Json.JsonValueKind.Undefined or System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False, "visibleSky must be an explicit boolean when supplied." );
        object skyApplied = null;
        if ( visibleSky.ValueKind == System.Text.Json.JsonValueKind.True )
        {
            const string stockSkyPath = "materials/skybox/skybox_day_01.vmat";
            var stockSky = Material.Load( stockSkyPath );
            Require( stockSky is { IsValid: true } && stockSky.ShaderName.Contains( "sky", StringComparison.OrdinalIgnoreCase ), "Stock native sky material is unavailable." );
            environmentSkyMaterial = stockSky.CreateCopy( "gltf_reference_sky_" + Guid.NewGuid().ToString( "N" ) );
            Require( environmentSkyMaterial is { IsValid: true } && environmentSkyMaterial.Set( "g_tSkyTexture", texture ), "Stock sky material did not accept the requested cubemap." );
            Require( environmentSkyMaterial.GetTexture( "g_tSkyTexture" ) == texture, "Stock sky material retained another texture." );
            environmentSky = new SceneSkyBox( scene.SceneWorld, environmentSkyMaterial );
            environmentSky.Transform = Transform.Zero.WithRotation( Rotation.FromYaw( (float)yaw ) );
            environmentSky.SkyTint = tint;
            Require( environmentSky.IsValid(), "Native sky object is invalid." );
            skyApplied = new { visible = true, materialTemplate = stockSkyPath, shader = environmentSkyMaterial.ShaderName, textureResourcePath = texture.ResourcePath, yawDegrees = yaw, tint = new[] { tint.r, tint.g, tint.b, tint.a }, scope = "Same native cubemap as the probe; sky visibility does not establish IBL correctness." };
        }
        environmentApplied = new { type = "cubemap", texturePath, textureResourcePath = texture.ResourcePath, sourcePath = environmentSourcePath, sourceSha256 = sourceActual, compiledPath, compiledSha256 = compiledActual, texture.Width, texture.Height, texture.Depth, texture.Mips, format = texture.ImageFormat.ToString(), centerInches = Point( center ), sizeInches = size, yawDegrees = yaw, tint = new[] { tint.r, tint.g, tint.b, tint.a }, projection = environmentProbe.Projection.ToString(), sky = skyApplied, limitations = "Loaded resource-to-compiled-file identity, orientation and actual IBL pixels require native validation." };
    }
    if ( appliedLights.Count == 0 && environmentProbe is null ) limitations.Add( "No valid recipe lights; no implicit light/sky/environment substituted." );
    exposureReceipt["requested"] = new { enabled = true, minimum = exposure, maximum = exposure, compensation = exposureCompensationEv };
    exposureReceipt["lockStatus"] = "unverified";
    exposureReceipt["locked"] = false;
    presentationApplied = new { background = new[] { camera.BackgroundColor.r, camera.BackgroundColor.g, camera.BackgroundColor.b, camera.BackgroundColor.a }, lights = appliedLights, environment = environmentApplied, exposure, exposureLocked = false, exposureLockStatus = "unverified", exposureDetails = exposureReceipt, nativeDebugMode = debugName, postProcessing = true };
    if ( explicitTonemapping is null ) limitations.Add( "Requested exposure is not a verified lock: editor postprocessing can change camera exposure settings." );
    else
    {
        explicitPresentationReceipt["background"] = new[] { camera.BackgroundColor.r, camera.BackgroundColor.g, camera.BackgroundColor.b, camera.BackgroundColor.a };
        explicitPresentationReceipt["lights"] = appliedLights; explicitPresentationReceipt["environment"] = environmentApplied;
        explicitPresentationReceipt["exposure"] = exposure; explicitPresentationReceipt["exposureLocked"] = false;
        explicitPresentationReceipt["exposureLockStatus"] = "awaiting-render"; explicitPresentationReceipt["exposureDetails"] = exposureReceipt;
        explicitPresentationReceipt["nativeDebugMode"] = debugName; explicitPresentationReceipt["postProcessing"] = true;
        explicitPresentationReceipt["tonemapping"] = new { mode = explicitTonemapping.Mode.ToString(), exposureCompensationEv, source = "camera-colocated stock Tonemapping component", scope = "Component selection and post-render camera settings; no independent shader pixel oracle." };
        presentationApplied = explicitPresentationReceipt;
    }
    using ( scene.Push() )
    {
        if ( model.BoneCount > 0 || model.AnimationCount > 0 )
        {
            skinned = scene.CreateObject().Components.Create<SkinnedModelRenderer>( false ); renderer = skinned;
            skinned.UseAnimGraph = false; skinned.PlayAnimationsInEditorScene = true; skinned.PlaybackRate = 0;
        }
        else renderer = scene.CreateObject().Components.Create<ModelRenderer>( false );
        renderer.WorldTransform = Transform.Zero; renderer.Model = model; renderer.Enabled = true; scene.EditorTick( 0, 0 );
    }
    Require( renderer.SceneObject is SceneCustomObject && renderer.SceneObject.Model is null, "Public renderer must own a custom object without a native model." );
    var instance = (typeof( ModelRenderer ).GetProperty( "GltfInstance", flags ) ?? throw new MissingMemberException( "ModelRenderer.GltfInstance" )).GetValue( renderer );
    Require( instance is not null, "Public renderer has no managed draw instance." );
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
    onFrame = () => widgetFrames++; widget.OnPreFrame += onFrame; widget.Show();
    renderStatus = "capturing"; renderError = null;
    var frameDir = System.IO.Path.Combine( outputDir, "frames" ); if ( times.Count > 1 ) System.IO.Directory.CreateDirectory( frameDir );
    for ( var index = 0; index < times.Count; index++ )
    {
        await SafePoint();
        if ( skinned is not null && clipName is not null ) skinned.Sequence.Time = (float)times[index];
        using ( scene.Push() ) scene.EditorTick( 0, 0 );
        await WaitFrames();
        // Public RenderToTexture initializes the native camera outside Graphics.IsActive.
        using var targetTexture = Texture.CreateRenderTarget().WithSize( width, height ).WithFormat( ImageFormat.RGBA8888 ).WithInitialColor( Color.Magenta ).Create( "gltf_run_" + Guid.NewGuid().ToString( "N" ) );
        bool submitted; using ( scene.Push() ) { targetTexture.Clear( Color.Magenta ); submitted = camera.RenderToTexture( targetTexture ); }
        Require( submitted, "Native camera rejected capture submission." );
        exposureReceipt["observedAfterRender"] = new { frameIndex = index, enabled = camera.AutoExposure.Enabled, minimum = camera.AutoExposure.MinimumExposure, maximum = camera.AutoExposure.MaximumExposure, compensation = camera.AutoExposure.Compensation, rate = camera.AutoExposure.Rate, sceneMainCamera = ReferenceEquals( scene.Camera, camera ), engineFrame = Application.FrameCount };
        if ( explicitTonemapping is not null )
        {
            var retained = camera.AutoExposure.Enabled
                && Math.Abs( camera.AutoExposure.MinimumExposure - exposure ) < 1e-5
                && Math.Abs( camera.AutoExposure.MaximumExposure - exposure ) < 1e-5
                && Math.Abs( camera.AutoExposure.Compensation - exposureCompensationEv ) < 1e-5;
            exposureReceipt["locked"] = retained; exposureReceipt["lockStatus"] = retained ? "component-settings-retained-after-render" : "failed";
            explicitPresentationReceipt["exposureLocked"] = retained; explicitPresentationReceipt["exposureLockStatus"] = exposureReceipt["lockStatus"];
            Require( retained, "Native render did not retain the explicitly requested exposure settings." );
        }
        await WaitFrames();
        using var bitmap = targetTexture.GetBitmap(); Require( bitmap is not null && bitmap.Width == width && bitmap.Height == height, "Native target returned no correctly sized bitmap." );
        var pixels = bitmap.GetPixels(); Require( pixels.Length == width * height, "Native capture pixel count differs." );
        var callbackErrors = (string[])Diagnostic( "CallbackErrors" );
        var drawCalls = Convert.ToInt32( Diagnostic( "DrawCalls" ) );
        var requestedRevision = Convert.ToInt64( Diagnostic( "PoseRevision" ) ); var submittedRevision = Convert.ToInt64( Diagnostic( "SubmittedRevision" ) );
        drawObserved = new { renderer = renderer.GetType().Name, resourcePath = model.ResourcePath, callbackErrors, drawCalls, requestedRevision, submittedRevision, nativeCaptureSubmitted = submitted, widgetFrames, bounds = boundsObserved };
        Require( callbackErrors.Length == 0, "Managed draw callback failed: " + string.Join( "\n", callbackErrors ) );
        Require( drawCalls > 0 && submittedRevision == requestedRevision, "Native capture completed without a successful current managed draw submission." );
        // Independent literal counts from the pinned original glTF accessors;
        // enumerate actual GPU output and index buffers after a real submission.
        var readbackMethod = instance.GetType().GetMethod( "Readback", flags ) ?? throw new MissingMethodException( "GltfInstance.Readback" );
        var readbacks = readbackMethod.Invoke( instance, null ) as System.Collections.IEnumerable ?? throw new InvalidOperationException( "GPU readback unavailable." );
        var vertexCounts = new[] { 31332, 62664, 62664, 62664, 36590 };
        var indexCounts = new[] { 184320, 368640, 368640, 368640, 215088 };
        var seen = new System.Collections.Generic.HashSet<int>();
        var sourceBin = "/home/damian/projects/sbox-public/Temp/gltf-reference/glTF-Sample-Assets/Models/MetalRoughSpheres/glTF/MetalRoughSpheres0.bin";
        Require( System.IO.File.Exists( sourceBin ) && HashFile( sourceBin ) == "f35a1caf37837b2987e24859d812a23e40ca37e1f741782e2a754961250e8c36", "Pinned normal oracle binary differs." );
        var sourceBytes = System.IO.File.ReadAllBytes( sourceBin );
        var normalOffsets = new[] { 5389968, 0, 3886032, 2382096, 1503936 };
        foreach ( var item in readbacks )
        {
            object Get( string key ) => (item.GetType().GetProperty( key, flags ) ?? throw new MissingMemberException( key )).GetValue( item );
            var mesh = Convert.ToInt32( Get( "Mesh" ) ); var vertexCount = Convert.ToInt32( Get( "VertexCount" ) );
            var bytes = (byte[])Get( "Bytes" ); var indices = (uint[])Get( "Indices" ); var stride = Convert.ToInt32( Get( "VertexStride" ) );
            Require( mesh >= 0 && mesh < 5 && seen.Add( mesh ), "Unexpected or duplicated MetalRoughSpheres mesh." );
            Require( vertexCount == vertexCounts[mesh] && indices.Length == indexCounts[mesh], "GPU counts differ from pinned source accessors for mesh " + mesh );
            Require( bytes.Length == checked(vertexCount * stride) && indices.All( n => n < vertexCount ) && bytes.Any( n => n != 0 ), "Invalid or empty GPU output." );
            Require( Convert.ToInt64( Get( "SubmittedRevision" ) ) == requestedRevision && Convert.ToInt32( Get( "DrawCalls" ) ) > 0, "GPU row did not submit the current pose." );
            if ( materialProof is null )
            {
                var actualMaterial = (Material)Get( "SubmittedMaterial" ); materialObserved = actualMaterial;
                Require( actualMaterial is { IsValid: true }, "Submitted material unavailable." );
                var bindings = new System.Collections.Generic.List<object>();
                foreach ( var parameter in new[] { "g_tNormal", "g_tMetalness", "g_tColor2" } )
                {
                    var present = actualMaterial.HasParameter( parameter ); var texture = actualMaterial.GetTexture( parameter );
                    object pixelsProof = null; string pixelError = null;
                    try
                    {
                        if ( texture is { IsValid: true, IsError: false } && texture.Width == 1024 && texture.Height == 1024 )
                        {
                            var samples = texture.GetPixels();
                            var coordinates = new[] { (X: 47, Y: 35), (X: 471, Y: 909), (X: 904, Y: 779) };
                            pixelsProof = coordinates.Select( point => { var pixel = samples[point.Y * texture.Width + point.X]; return new { point.X, point.Y, pixel.r, pixel.g, pixel.b, pixel.a }; } ).ToArray();
                        }
                    }
                    catch ( Exception error ) { pixelError = error.ToString(); }
                    bindings.Add( new { parameter, present, valid = texture?.IsValid ?? false, width = texture?.Width, height = texture?.Height, format = texture?.ImageFormat.ToString(), pixels = pixelsProof, pixelError } );
                }
                materialProof = new { actualMaterial.ShaderName, linearVertexColorFeature = actualMaterial.GetFeature( "F_GLTF_LINEAR_VERTEX_COLOR" ), metalTextureFeature = actualMaterial.GetFeature( "F_GLTF_METALLIC_TEXTURE" ), roughnessScale = actualMaterial.GetVector4( "g_flRoughnessScaleFactor" ).x, metalness = actualMaterial.GetVector4( "g_flMetalness" ).x, bindings, scope = "Submitted material parameter and texture readback; does not alone prove shader samples these bindings." };
            }
            var fields = ((System.Collections.IEnumerable)Get( "Fields" )).Cast<object>().ToArray();
            object FieldValue( object field, string key ) => (field.GetType().GetProperty( key, flags ) ?? throw new MissingMemberException( key )).GetValue( field );
            var normalField = fields.Single( f => (string)FieldValue( f, "Semantic" ) == "NORMAL" && Convert.ToInt32( FieldValue( f, "SemanticIndex" ) ) == 0 );
            var tangentField = fields.Single( f => (string)FieldValue( f, "Semantic" ) == "TANGENT" && Convert.ToInt32( FieldValue( f, "SemanticIndex" ) ) == 0 );
            var normalOffset = Convert.ToInt32( FieldValue( normalField, "Offset" ) ); var tangentOffset = Convert.ToInt32( FieldValue( tangentField, "Offset" ) );
            Require( Convert.ToInt32( FieldValue( normalField, "ComponentType" ) ) == 5126 && Convert.ToInt32( FieldValue( tangentField, "ComponentType" ) ) == 5126, "Unexpected GPU normal/tangent representation." );
            var sourceVertices = (uint[])Get( "SourceVertices" ); Require( sourceVertices.Length == vertexCount, "Missing source vertex map." );
            double maxNormalError = 0, maxTangentLengthError = 0, maxOrthogonalityError = 0; int checkedCount = 0;
            for ( int vertex = 0; vertex < vertexCount; vertex++ )
            {
                var origin = checked(normalOffsets[mesh] + (int)sourceVertices[vertex] * 12); var output = vertex * stride;
                // Authored root maps (x,y,z) to (x,z,-y); glTF-to-engine maps (x,y,z) to (z,x,y).
                var expected = new System.Numerics.Vector3( -BitConverter.ToSingle( sourceBytes, origin + 4 ), BitConverter.ToSingle( sourceBytes, origin ), BitConverter.ToSingle( sourceBytes, origin + 8 ) );
                expected = System.Numerics.Vector3.Normalize( expected );
                var actual = new System.Numerics.Vector3( BitConverter.ToSingle( bytes, output + normalOffset ), BitConverter.ToSingle( bytes, output + normalOffset + 4 ), BitConverter.ToSingle( bytes, output + normalOffset + 8 ) );
                var tangent = new System.Numerics.Vector3( BitConverter.ToSingle( bytes, output + tangentOffset ), BitConverter.ToSingle( bytes, output + tangentOffset + 4 ), BitConverter.ToSingle( bytes, output + tangentOffset + 8 ) );
                var handedness = BitConverter.ToSingle( bytes, output + tangentOffset + 12 );
                var ne = System.Numerics.Vector3.Distance( expected, actual ); var te = Math.Abs( tangent.Length() - 1 ); var oe = Math.Abs( System.Numerics.Vector3.Dot( actual, tangent ) );
                Require( float.IsFinite( ne ) && float.IsFinite( te ) && float.IsFinite( oe ) && float.IsFinite( handedness ), "Non-finite GPU normal/tangent." );
                maxNormalError = Math.Max( maxNormalError, ne ); maxTangentLengthError = Math.Max( maxTangentLengthError, te ); maxOrthogonalityError = Math.Max( maxOrthogonalityError, oe ); checkedCount++;
            }
            var basisPassed = maxNormalError < .0002 && maxTangentLengthError < .0002 && maxOrthogonalityError < .0002;
            var indexBytes = new byte[indices.Length * 4]; Buffer.BlockCopy( indices, 0, indexBytes, 0, indexBytes.Length );
            gpuReadbacks.Add( new { mesh, vertexCount, stride, indexCount = indices.Length, normalOracleCount = checkedCount, basisPassed, maxNormalError, maxTangentLengthError, maxOrthogonalityError, vertexSha256 = Hash( bytes ), indexSha256 = Hash( indexBytes ), revision = requestedRevision } );
        }
        Require( seen.Count == 5, "Expected all five GPU primitive rows." );
        Require( !pixels.All( pixel => pixel.r > .99f && pixel.g < .01f && pixel.b > .99f ), "Native target retained its magenta initialization." );
        var png = bitmap.ToPng(); var path = times.Count == 1 ? System.IO.Path.Combine( outputDir, "capture.png" ) : System.IO.Path.Combine( frameDir, "frame" + index.ToString( "D4" ) + ".png" );
        AtomicBytes( path, png ); if ( index == 0 && times.Count > 1 ) AtomicBytes( System.IO.Path.Combine( outputDir, "capture.png" ), png );
        var actualTime = skinned is not null && clipName is not null ? skinned.Sequence.Time : (float?)null;
        animationFrames.Add( new { index, requestedTime = times[index], publicSeekTime = (float)times[index], actualTime, path, sha256 = Hash( png ), engineFrame = Application.FrameCount, wallSeconds = timer.Elapsed.TotalSeconds, width, height } );

        if ( index == 0 ) captureResult = new { path = System.IO.Path.Combine( outputDir, "capture.png" ), sha256 = Hash( png ), kind = "image" };
        // A private material copy per control avoids mutating the cached source material.
        var currentAo = materialObserved.GetTexture( "g_tAmbientOcclusion" ); object aoReadback = null; string aoReadbackError = null;
        try
        {
            if ( currentAo is { IsValid: true, IsError: false } )
            {
                var values = currentAo.GetPixels();
                aoReadback = new { count = values.Length, first = values.Take( 8 ).Select( c => new[] { c.r, c.g, c.b, c.a } ).ToArray(), minRed = values.Min( c => c.r ), maxRed = values.Max( c => c.r ) };
            }
        }
        catch ( Exception error ) { aoReadbackError = error.ToString(); }
        var aoBaseline = new { present = materialObserved.HasParameter( "g_tAmbientOcclusion" ), valid = currentAo?.IsValid ?? false, width = currentAo?.Width, height = currentAo?.Height, format = currentAo?.ImageFormat.ToString(), readback = aoReadback, error = aoReadbackError };
        var controls = new System.Collections.Generic.List<object>();
        foreach ( var entry in new[] { (Label: "authored", Value: -1), (Label: "white", Value: 255), (Label: "black", Value: 0) } )
        {
            Material selected = null;
            if ( entry.Value >= 0 )
            {
                var channel = (byte)entry.Value;
                var texture = Texture.Create( 1, 1, ImageFormat.RGBA8888 ).WithMips( 1 ).WithData( new byte[] { channel, channel, channel, 255 } ).WithStaticUsage().Finish(); aoTextures.Add( texture );
                selected = materialObserved.CreateCopy( "gltf_ao_control_" + Guid.NewGuid().ToString( "N" ) ); aoMaterials.Add( selected );
                Require( selected is { IsValid: true } && selected.Set( "g_tAmbientOcclusion", texture ) && selected.GetTexture( "g_tAmbientOcclusion" ) == texture, "AO diagnostic binding not retained." );
            }
            using ( scene.Push() ) { renderer.MaterialOverride = selected; scene.EditorTick( 0, 0 ); }
            await WaitFrames();
            foreach ( var mode in new[] { SceneCameraDebugMode.Normal, SceneCameraDebugMode.AmbientOcclusion } )
            {
                camera.DebugMode = mode;
                bool accepted; using ( scene.Push() ) { targetTexture.Clear( Color.Magenta ); accepted = camera.RenderToTexture( targetTexture ); }
                Require( accepted, "AO diagnostic capture was not submitted." ); await WaitFrames();
                var observedRows = (System.Collections.IEnumerable)readbackMethod.Invoke( instance, null );
                foreach ( var observedRow in observedRows )
                {
                    var actual = (Material)(observedRow.GetType().GetProperty( "SubmittedMaterial", flags ) ?? throw new MissingMemberException( "SubmittedMaterial" )).GetValue( observedRow );
                    Require( ReferenceEquals( actual, selected ?? materialObserved ), "AO control did not submit the selected material." );
                }
                using var controlBitmap = targetTexture.GetBitmap(); Require( controlBitmap is not null, "AO diagnostic bitmap missing." );
                var controlPng = controlBitmap.ToPng(); var controlPath = System.IO.Path.Combine( outputDir, "ao-" + entry.Label + "-" + mode + ".png" ); AtomicBytes( controlPath, controlPng );
                controls.Add( new { entry.Label, mode = mode.ToString(), path = controlPath, sha256 = Hash( controlPng ), material = selected is null ? "original-source" : "owned-copy-with-override", overrideValue = entry.Value } );
            }
        }
        aoProof = new { sourceBinding = aoBaseline, controls, scope = "Source material plus private white/black AO texture overrides; individual captures are diagnostics, not source appearance approvals." };
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
    await MainThread.Wait();
    Clean( "renderer", () => { if ( renderer.IsValid() ) { using var scope = scene.Push(); renderer.GameObject.Destroy(); scene.ProcessDeletes(); } } );
    foreach ( var material in aoMaterials ) Clean( "AO material", () => { if ( material is { IsValid: true } ) (typeof( Material ).GetMethod( "Destroy", flags, null, Type.EmptyTypes, null ) ?? throw new MissingMethodException( "Material.Destroy" )).Invoke( material, null ); } );
    foreach ( var texture in aoTextures ) Clean( "AO texture", () => texture.Dispose() );
    Clean( "widget callback", () => { if ( widget is not null && onFrame is not null ) widget.OnPreFrame -= onFrame; } );
    Clean( "widget close", () => { if ( widget is not null && widget.IsValid ) widget.Close(); } );
    Clean( "widget destroy", () => { if ( widget is not null && widget.IsValid ) widget.Destroy(); } );
    foreach ( var light in lights ) Clean( "light", () => { if ( light.IsValid() ) light.Delete(); } );
    Clean( "environment sky", () => { if ( environmentSky.IsValid() ) environmentSky.Delete(); } );
    Clean( "owned sky material", () => { if ( environmentSkyMaterial is { IsValid: true } ) (typeof( Material ).GetMethod( "Destroy", flags, null, Type.EmptyTypes, null ) ?? throw new MissingMethodException( "Material.Destroy" )).Invoke( environmentSkyMaterial, null ); } );
    Clean( "environment probe", () => { if ( environmentProbe.IsValid() ) environmentProbe.Delete(); } );
    Clean( "scene", () => { if ( scene.IsValid() ) scene.Destroy(); } );
    Clean( "model", () => { if ( ownedModel ) (typeof( Model ).GetMethod( "Destroy", flags, null, Type.EmptyTypes, null ) ?? throw new MissingMethodException( "Model.Destroy" )).Invoke( model, null ); } );
    Clean( "unmount", () => { if ( mounted ) (typeof( BaseFileSystem ).GetMethod( "UnMount", flags, null, new[] { typeof( BaseFileSystem ) }, null ) ?? throw new MissingMethodException( "BaseFileSystem.UnMount" )).Invoke( aggregate, new object[] { mount } ); } );
    Clean( "filesystem", () => { if ( mount is not null ) (mount.GetType().GetMethod( "Dispose", flags, null, Type.EmptyTypes, null ) ?? throw new MissingMethodException( "LocalFileSystem.Dispose" )).Invoke( mount, null ); } );
    Clean( "private corpus link", () => { if ( privateCorpusLink is not null && System.IO.Directory.Exists( privateCorpusLink ) ) System.IO.Directory.Delete( privateCorpusLink, false ); } );
    Clean( "private mount directory", () => { if ( privateMountRoot is not null && System.IO.Directory.Exists( privateMountRoot ) ) System.IO.Directory.Delete( privateMountRoot, false ); } );
    Clean( "request", () => requestDocument?.Dispose() );
    if ( cleanupErrors.Count > 0 ) { renderStatus = "failed"; renderError = (renderError ?? "") + "\nOwned cleanup failed; stop Run."; stopReason = "cleanup_failure"; }
    if ( renderStatus != "passed" ) Clean( "failed capture invalidation", ClearCaptureFiles );
    if ( cleanupErrors.Count > 0 ) { captureResult = null; renderStatus = "failed"; stopReason = "cleanup_failure"; }
}
var result = new System.Collections.Generic.Dictionary<string, object>
{
    ["id"] = id, ["input"] = new { path = inputPath, sourcePath, sha256 = sourceHash },
    ["load"] = new { status = loadStatus, error = loadError }, ["render"] = new { status = renderStatus, error = renderError, observation = drawObserved, visualApproval = "pending" },
    ["capture"] = captureResult, ["animationFrames"] = animationFrames, ["gpuReadbacks"] = gpuReadbacks, ["materialProof"] = materialProof, ["aoProof"] = aoProof, ["recipeId"] = recipeId,
    ["recipeApplied"] = new { complete = recipeComplete && renderStatus == "passed", camera = cameraApplied, presentation = presentationApplied, animation = animationApplied, limitations },
    ["producer"] = producers, ["cleanup_errors"] = cleanupErrors, ["cleanupConfirmed"] = cleanupErrors.Count == 0, ["stopReason"] = stopReason, ["elapsedSeconds"] = timer.Elapsed.TotalSeconds
};
if ( outputDir is not null ) AtomicBytes( System.IO.Path.Combine( outputDir, "result.json" ), System.Text.Json.JsonSerializer.SerializeToUtf8Bytes( result, jsonOptions ) );
return result;
