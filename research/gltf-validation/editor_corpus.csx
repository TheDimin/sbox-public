// Existing physical-desktop editor only. Tests untouched pinned Khronos sources
// and packaging-only derivatives one asset/form at a time. Load is NOT appearance
// acceptance; absent independent oracles stay unverified and cannot make a pass.
var engine = typeof( Model ).Assembly;
var checkout = new System.IO.DirectoryInfo( System.IO.Path.GetDirectoryName( engine.Location ) );
while ( checkout is not null && !System.IO.File.Exists( System.IO.Path.Combine( checkout.FullName, "engine", "Sandbox-Engine.slnx" ) ) ) checkout = checkout.Parent;
if ( checkout is null ) throw new InvalidOperationException( "Cannot locate checkout from loaded engine." );
var corpus = Environment.GetEnvironmentVariable( "SBOX_GLTF_CORPUS" );
if ( string.IsNullOrWhiteSpace( corpus ) ) corpus = System.IO.Path.Combine( checkout.FullName, "Temp", "gltf-reference", "glTF-Sample-Assets" );
corpus = System.IO.Path.GetFullPath( corpus );
var runId = Guid.NewGuid().ToString( "N" );
var output = System.IO.Path.Combine( checkout.FullName, "Temp", "gltf-validation", "editor-corpus", runId );
System.IO.Directory.CreateDirectory( output );
var manifestPath = System.IO.Path.Combine( checkout.FullName, "research", "gltf-validation", "corpus-case-manifest.json" );
var runnerPath = System.IO.Path.Combine( checkout.FullName, "research", "gltf-validation", "editor_corpus.csx" );
var requestPath = System.IO.Path.Combine( exchangeDirectory, "gltf-corpus-request.json" );
var jsonOptions = new System.Text.Json.JsonSerializerOptions { WriteIndented = true, NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals };
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
var summaries = new System.Collections.Generic.List<object>();
var cleanupErrors = new System.Collections.Generic.List<string>();
string phase = "initialize", error = null, manifestHash = null, runnerHash = null, requestHash = null, requestId = null;
string engineDiskMvid = null, gltfDiskMvid = null, engineBinaryHash = null, gltfBinaryHash = null;
string captureRenderer = "native", rendererLoadedMvid = null, rendererDiskMvid = null, rendererBinaryHash = null, rendererLocation = null;
object managedRenderer = null; System.Reflection.MethodInfo managedRenderToTexture = null;
string stopReason = null;
long maxEstimatedBytes = 256L * 1024 * 1024, maxFileBytes = 64L * 1024 * 1024;
bool keepInputs = false, selectionIsAll = true;
var requestedIds = new System.Collections.Generic.HashSet<string>( StringComparer.Ordinal );
int applicableCount = 0, applicableLoaded = 0, intendedPassed = 0, excludedCount = 0, pendingCount = 0, failedCount = 0;
int loadedForms = 0, capturedForms = 0, failedOrUnverifiedCaptures = 0;
int selectedCount = 0, completedCount = 0;
var startingFrame = Application.FrameCount;
System.Text.Json.JsonElement manifestRoot = default;
var selectedExtensions = new System.Collections.Generic.HashSet<string>( StringComparer.Ordinal );
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
void Save( string path, object value ) => System.IO.File.WriteAllText( path, System.Text.Json.JsonSerializer.Serialize( value, jsonOptions ) );
object Gate( string status, string reason, object observation = null ) => new { status, reason, observation };
string GateStatus( object gate ) => gate?.GetType().GetProperty( "status" )?.GetValue( gate ) as string;
bool Valid( Model value ) => value is { IsValid: true } && !value.IsError && !ReferenceEquals( value, Model.Error );
bool Hex( string value, int count ) => value is not null && value.Length == count && value.All( c => c >= '0' && c <= '9' || c >= 'a' && c <= 'f' );
float[] Point( Vector3 point ) => new[] { point.x, point.y, point.z };
byte[] ReadBounded( string path, long maximum, bool optional = false )
{
    System.IO.FileStream stream;
    try { stream = System.IO.File.OpenRead( path ); }
    catch ( System.IO.FileNotFoundException ) when ( optional ) { return null; }
    catch ( System.IO.DirectoryNotFoundException ) when ( optional ) { return null; }
    using ( stream )
    {
        var length = stream.Length;
        Require( length > 0 && length <= maximum && length <= int.MaxValue, "Missing/oversized input: " + path );
        var bytes = new byte[(int)length]; stream.ReadExactly( bytes );
        Require( stream.ReadByte() == -1, "Input grew during read: " + path );
        return bytes;
    }
}
string Contained( string root, string relative )
{
    Require( !string.IsNullOrWhiteSpace( relative ) && !System.IO.Path.IsPathRooted( relative ) && !relative.Contains( '\\' ), "Invalid relative path: " + relative );
    var prefix = System.IO.Path.GetFullPath( root ) + System.IO.Path.DirectorySeparatorChar;
    var full = System.IO.Path.GetFullPath( System.IO.Path.Combine( root, relative.Replace( '/', System.IO.Path.DirectorySeparatorChar ) ) );
    Require( full.StartsWith( prefix, StringComparison.Ordinal ), "Path escapes its private/corpus root: " + relative );
    var current = root;
    foreach ( var segment in System.IO.Path.GetRelativePath( root, full ).Split( System.IO.Path.DirectorySeparatorChar ) )
    {
        current = System.IO.Path.Combine( current, segment );
        if ( System.IO.File.Exists( current ) || System.IO.Directory.Exists( current ) )
            Require( (System.IO.File.GetAttributes( current ) & System.IO.FileAttributes.ReparsePoint) == 0, "Symbolic link in corpus/private path: " + relative );
    }
    return full;
}
string GitHead( string root )
{
    var git = System.IO.Path.Combine( root, ".git" );
    Require( System.IO.Directory.Exists( git ), "Expected the independent corpus checkout's .git directory." );
    var head = System.IO.File.ReadAllText( System.IO.Path.Combine( git, "HEAD" ) ).Trim();
    if ( !head.StartsWith( "ref: ", StringComparison.Ordinal ) ) return head;
    var reference = head[5..]; var loose = Contained( git, reference );
    if ( System.IO.File.Exists( loose ) ) return System.IO.File.ReadAllText( loose ).Trim();
    var packed = System.IO.Path.Combine( git, "packed-refs" );
    Require( System.IO.File.Exists( packed ), "Cannot resolve corpus HEAD ref." );
    var row = System.IO.File.ReadLines( packed ).SingleOrDefault( line => line.EndsWith( " " + reference, StringComparison.Ordinal ) );
    Require( row is not null, "Corpus HEAD ref is absent from packed refs." ); return row.Split( ' ' )[0];
}
async Task SafePoint()
{
    await Task.Delay( 1, cancellationToken ); await MainThread.Wait();
    Require( Application.IsEditor && !Game.IsPlaying, "Editor-only corpus test deferred while the user is playing." );
    var timer = System.Diagnostics.Stopwatch.StartNew();
    while ( Graphics.IsActive )
    {
        Require( timer.Elapsed < TimeSpan.FromSeconds( 5 ), "No safe main-thread graphics boundary." );
        await Task.Delay( 25, cancellationToken ); await MainThread.Wait();
        Require( !Game.IsPlaying, "User entered play mode; corpus test deferred." );
    }
}

void VerifyFile( string relative )
{
    var expected = manifestRoot.GetProperty( "files" ).GetProperty( relative );
    var path = Contained( corpus, relative );
    Require( new System.IO.FileInfo( path ).Length == expected.GetProperty( "bytes" ).GetInt64(), "Pinned file size differs: " + relative );
    Require( HashFile( path ) == expected.GetProperty( "sha256" ).GetString(), "Pinned file hash differs: " + relative );
}
string[] CaseFiles( System.Text.Json.JsonElement entry ) => new[] { entry.GetProperty( "path" ).GetString() }
    .Concat( entry.GetProperty( "dependencies" ).EnumerateArray().Select( item => item.GetString() ) ).Distinct( StringComparer.Ordinal ).ToArray();
void CopySource( System.Text.Json.JsonElement entry, string input )
{
    foreach ( var relative in CaseFiles( entry ) )
    {
        VerifyFile( relative );
        var original = Contained( corpus, relative );
        Require( new System.IO.FileInfo( original ).Length <= maxFileBytes, "Private input exceeds admitted file bound: " + relative );
        var target = Contained( input, relative ); System.IO.Directory.CreateDirectory( System.IO.Path.GetDirectoryName( target ) );
        using ( var source = System.IO.File.OpenRead( original ) )
        using ( var destination = new System.IO.FileStream( target, System.IO.FileMode.CreateNew, System.IO.FileAccess.Write ) ) source.CopyTo( destination, 65536 );
        Require( HashFile( target ) == manifestRoot.GetProperty( "files" ).GetProperty( relative ).GetProperty( "sha256" ).GetString(), "Private source copy differs: " + relative );
    }
}
object GeometryChecks( Model model, System.Text.Json.JsonElement entry )
{
    var oracle = entry.GetProperty( "geometry_oracle" );
    var info = model.MeshInfo ?? throw new InvalidOperationException( "Loaded model has no mesh metadata." );
    var checks = new System.Collections.Generic.List<object>();
    var numericPassed = true;
    if ( oracle.TryGetProperty( "triangle_count", out var triangles ) && triangles.ValueKind == System.Text.Json.JsonValueKind.Number )
    {
        var expected = triangles.GetInt32();
        checks.Add( new { name = "declared topology count diagnostic", equals_native = info.TotalTriangles == expected, declared = expected, actual = info.TotalTriangles,
            qualification = "Declared count can include degenerate/strip source triangles; native pruning is not universally a defect." } );
    }
    if ( oracle.TryGetProperty( "bounds_status", out var status ) && status.GetString() == "available" )
    {
        var expectedMin = oracle.GetProperty( "bounds_min" ).EnumerateArray().Select( value => value.GetDouble() ).ToArray();
        var expectedMax = oracle.GetProperty( "bounds_max" ).EnumerateArray().Select( value => value.GetDouble() ).ToArray();
        var actualMin = Point( model.RenderBounds.Mins ); var actualMax = Point( model.RenderBounds.Maxs );
        var absolute = oracle.GetProperty( "bounds_absolute_tolerance_inches" ).GetDouble();
        var relative = oracle.GetProperty( "bounds_relative_tolerance" ).GetDouble();
        bool Matches( double expected, double actual ) => double.IsFinite( actual ) && Math.Abs( expected - actual ) <= absolute + relative * Math.Abs( expected );
        var passed = Enumerable.Range( 0, 3 ).All( i => Matches( expectedMin[i], actualMin[i] ) && Matches( expectedMax[i], actualMax[i] ) );
        numericPassed &= passed;
        checks.Add( new { name = "independent decoded world-space bounds", passed, expected_min = expectedMin, expected_max = expectedMax, actual_min = actualMin, actual_max = actualMax, absolute_inches = absolute, relative } );
    }
    // Bounds are partial assertions and declared counts are diagnostic. Exact vertex/attribute/topology
    // readback and posed geometry are additional, independent required gates.
    return Gate( numericPassed ? "unverified" : "failed", numericPassed ? "Independent counts/bounds asserted where available; complete vertex/attribute/posed readback oracle not attached" : "Independent source count/bounds oracle disagrees with native output", new
    {
        checks, oracle_status = oracle.GetProperty( "status" ).GetString(),
        actual_vertices = info.TotalVertices, actual_triangles = info.TotalTriangles,
        actual_draws = info.TotalDrawCalls, actual_bounds_min = Point( model.RenderBounds.Mins ), actual_bounds_max = Point( model.RenderBounds.Maxs )
    } );
}
object MaterialChecks( Model model, System.Text.Json.JsonElement entry )
{
    var native = model.MeshInfo.Meshes.SelectMany( mesh => mesh.DrawCalls ).Select( draw => draw.Material )
        .Where( material => material is { IsValid: true } && !material.IsError ).ToArray();
    var checks = new System.Collections.Generic.List<object>();
    var parametersPassed = true;
    var oracle = entry.GetProperty( "geometry_oracle" );
    var table = manifestRoot.GetProperty( "material_contract_tables" ).GetProperty( entry.GetProperty( "material_contract_ref" ).GetString() );
    if ( oracle.TryGetProperty( "material_indices", out var indices ) )
    {
        foreach ( var indexValue in indices.EnumerateArray() )
        {
            var index = indexValue.GetInt32(); var color = new[] { 1f, 1f, 1f, 1f }; bool doubleSided = false;
            if ( index >= 0 )
            {
                var material = table[index];
                if ( material.TryGetProperty( "pbrMetallicRoughness", out var pbr ) && pbr.TryGetProperty( "baseColorFactor", out var factor ) ) color = factor.EnumerateArray().Select( item => item.GetSingle() ).ToArray();
                if ( !material.TryGetProperty( "alphaMode", out var alpha ) || alpha.GetString() == "OPAQUE" ) color[3] = 1;
                doubleSided = material.TryGetProperty( "doubleSided", out var both ) && both.GetBoolean();
            }
            // glTF factors are linear; the shipped complex shader decodes its
            // native tint from sRGB. This oracle is independent of the adapter.
            for ( var channel = 0; channel < 3; channel++ )
            {
                var linear = (double)color[channel];
                color[channel] = linear == 0 || linear == 1 ? (float)linear :
                    (float)(linear <= .0031308 ? linear * 12.92 : 1.055 * Math.Pow( linear, 1.0 / 2.4 ) - .055);
            }
            var found = native.Any( material =>
            {
                var actual = material.GetVector4( "g_vColorTint" );
                return MathF.Abs( actual.x - color[0] ) < .0001f && MathF.Abs( actual.y - color[1] ) < .0001f && MathF.Abs( actual.z - color[2] ) < .0001f && MathF.Abs( actual.w - color[3] ) < .0001f && material.GetFeature( "F_RENDER_BACKFACES" ) == (doubleSided ? 1 : 0);
            } );
            parametersPassed &= found;
            checks.Add( new { name = "base-factor/backface native parameter candidate", source_material = index, passed = found, expected_color = color, expected_double_sided = doubleSided } );
        }
    }
    return Gate( parametersPassed ? "unverified" : "failed", parametersPassed ? "Parameter candidates do not prove PBR, all texture roles/UVs/samplers/color spaces, alpha or per-primitive material binding" : "Independent source base-factor/backface contract disagrees with native parameters", new
    {
        checks, native_material_count = native.Length,
        source_contract_ref = entry.GetProperty( "material_contract_ref" ).GetString(),
        node_material_binding_ref = entry.GetProperty( "node_material_binding_ref" ).GetString()
    } );
}
// Each image uses a new render target. Empty-after-delete is an essential
// control: an older native path has reproduced stale images on empty views.
async Task<object> CaptureModel( Model model, string directory, System.Text.Json.JsonElement entry, string form )
{
    Scene scene = null; Editor.SceneRenderingWidget preview = null; CameraComponent camera = null;
    SceneModel obj = null; ScenePointLight light = null; Action onFrame = null;
    var frames = 0; var paths = new System.Collections.Generic.List<object>(); var localErrors = new System.Collections.Generic.List<string>();
    string failure = null; int? referenceCoverage = null, assetCoverage = null, emptyBeforePixels = null, emptyAfterSpherePixels = null, emptyAfterPixels = null;
    object framing = null;
    void Clean( string name, Action action ) { try { action(); } catch ( Exception ex ) { localErrors.Add( name + ": " + ex ); } }
    bool Nonblack( Color pixel ) => MathF.Abs( pixel.r ) > .03f || MathF.Abs( pixel.g ) > .03f || MathF.Abs( pixel.b ) > .03f;
    bool IsFinite( Vector3 v ) => float.IsFinite( v.x ) && float.IsFinite( v.y ) && float.IsFinite( v.z );
    object Frame( BBox bounds )
    {
        var min = bounds.Mins; var max = bounds.Maxs;
        Require( IsFinite( min ) && IsFinite( max ) && max.x >= min.x && max.y >= min.y && max.z >= min.z, "Model bounds cannot be framed safely." );
        var center = (min + max) * .5f; var diagonal = (max - min).Length;
        Require( IsFinite( center ) && float.IsFinite( diagonal ), "Model framing overflowed." );
        var radius = MathF.Max( diagonal * .5f, .01f ); var distance = radius * 3.5f;
        Require( float.IsFinite( distance ) && distance < 1e12f, "Model bounds exceed the preview framing limit." );
        var sourceDirection = new Vector3( 1, .65f, 1 ); var sourceUp = new Vector3( 0, 1, 0 );
        var nativeDirection = new Vector3( sourceDirection.z, sourceDirection.x, sourceDirection.y );
        var nativeUp = new Vector3( sourceUp.z, sourceUp.x, sourceUp.y );
        camera.WorldPosition = center + nativeDirection.Normal * distance; camera.WorldRotation = Rotation.LookAt( center - camera.WorldPosition, nativeUp );
        camera.ZNear = MathF.Max( radius * .005f, .0001f ); camera.ZFar = MathF.Max( distance * 10, camera.ZNear + 1 );
        return new { camera_preset = "source-positive", source_direction = Point( sourceDirection ), native_direction = Point( nativeDirection ), source_up = Point( sourceUp ), native_up = Point( nativeUp ), exact_upstream_extrinsics_known = false, bounds_min = Point( min ), bounds_max = Point( max ), center = Point( center ), radius, distance,
            camera_position = Point( camera.WorldPosition ), camera_rotation = camera.WorldRotation.ToString(), camera.FieldOfView, camera.ZNear, camera.ZFar };
    }
    try
    {
        await SafePoint(); System.IO.Directory.CreateDirectory( directory );
        scene = Scene.CreateEditorScene();
        using ( scene.Push() )
        {
            camera = scene.CreateObject().Components.Create<CameraComponent>(); camera.BackgroundColor = Color.Black;
            camera.FieldOfView = 50; camera.EnablePostProcessing = false;
        }
        preview = new Editor.SceneRenderingWidget { Scene = scene, Camera = camera, WindowTitle = "glTF corpus capture", Size = new Vector2( 384, 384 ) };
        onFrame = () => frames++; preview.OnPreFrame += onFrame; preview.Show();
        async Task WaitFrames()
        {
            var start = frames; var timer = System.Diagnostics.Stopwatch.StartNew();
            while ( frames < start + 3 )
            {
                cancellationToken.ThrowIfCancellationRequested();
                Require( timer.Elapsed < TimeSpan.FromSeconds( 8 ), "Private corpus preview stopped receiving frames." );
                await Task.Delay( 30, cancellationToken ); await SafePoint();
            }
        }
        async Task<Color[]> Capture( string label, SceneCameraDebugMode mode )
        {
            camera.DebugMode = mode; await WaitFrames(); var before = frames;
            // A distinct, initially magenta target makes accidental reuse or an
            // empty render that never clears observable in its pixel control.
            using var target = Texture.CreateRenderTarget().WithSize( 192, 192 ).WithFormat( ImageFormat.RGBA8888 ).WithInitialColor( Color.Magenta ).Create( "gltf_corpus_" + Guid.NewGuid().ToString( "N" ) );
            using ( scene.Push() )
            {
                target.Clear( new Color( 1, 0, 1, 1 ) );
                var submitted = captureRenderer == "managed"
                    ? (bool)managedRenderToTexture.Invoke( managedRenderer, new object[] { camera, target } )
                    : camera.RenderToTexture( target );
                Require( submitted, "Camera submission failed: " + label );
            }
            await WaitFrames(); await SafePoint();
            using var bitmap = target.GetBitmap(); Require( bitmap is not null, "Readback failed: " + label );
            var png = bitmap.ToPng(); var path = System.IO.Path.Combine( directory, label + ".png" ); System.IO.File.WriteAllBytes( path, png );
            paths.Add( new { label, path, sha256 = Hash( png ), width = 192, height = 192, debug_mode = mode.ToString(),
                fresh_render_target = true, capture_renderer = captureRenderer, renderer_mvid = rendererLoadedMvid, renderer_sha256 = rendererBinaryHash,
                preview_frames_submit = before, preview_frames_readback = frames, frame = Application.FrameCount } );
            return bitmap.GetPixels();
        }
        var sphere = Model.Sphere;
        var validSphere = Valid( sphere );
        _ = Frame( validSphere ? sphere.RenderBounds : model.RenderBounds );
        var before = await Capture( "empty_before", SceneCameraDebugMode.Albedo ); emptyBeforePixels = before.Count( Nonblack );
        if ( validSphere )
        {
            obj = new SceneModel( scene.SceneWorld, sphere, Transform.Zero );
            var reference = await Capture( "native_sphere_control", SceneCameraDebugMode.Albedo ); referenceCoverage = reference.Count( Nonblack );
            obj.Delete(); obj = null;
        }
        var afterReference = await Capture( "empty_after_sphere", SceneCameraDebugMode.Albedo ); emptyAfterSpherePixels = afterReference.Count( Nonblack );
        framing = Frame( model.RenderBounds );
        obj = new SceneModel( scene.SceneWorld, model, Transform.Zero );
        var albedo = await Capture( "albedo", SceneCameraDebugMode.Albedo ); assetCoverage = albedo.Count( Nonblack );
        var radiusForLight = MathF.Max( (model.RenderBounds.Maxs - model.RenderBounds.Mins).Length * .5f, .01f );
        light = new ScenePointLight( scene.SceneWorld, camera.WorldPosition, radiusForLight * 12, Color.White * 4 );
        light.ShadowsEnabled = false; light.RenderDiffuse = true; light.RenderSpecular = true; light.SetBakedLightIndex( -1 );
        _ = await Capture( "lit", SceneCameraDebugMode.Normal );
        light.Delete(); light = null; obj.Delete(); obj = null;
        var after = await Capture( "empty_after_model", SceneCameraDebugMode.Albedo ); emptyAfterPixels = after.Count( Nonblack );
        Require( emptyBeforePixels == 0 && emptyAfterSpherePixels == 0 && emptyAfterPixels == 0, "Fresh-target empty-scene controls failed." );
        Require( validSphere && referenceCoverage > 50, "Native sphere visibility control failed." );
    }
    catch ( Exception ex ) when ( ex is not OutOfMemoryException && ex is not OperationCanceledException ) { failure = ex.ToString(); }
    finally
    {
        await MainThread.Wait();
        if ( preview is not null && preview.IsValid && onFrame is not null ) Clean( "preview callback", () => preview.OnPreFrame -= onFrame );
        if ( obj.IsValid() ) Clean( "scene object", () => obj.Delete() );
        if ( light.IsValid() ) Clean( "light", () => light.Delete() );
        if ( preview is not null && preview.IsValid ) Clean( "preview", () => { preview.Close(); preview.Destroy(); } );
        if ( scene is not null ) Clean( "scene", () => scene.Destroy() );
    }
    if ( localErrors.Count != 0 ) { failure = (failure ?? "") + " Preview cleanup failed: " + string.Join( "; ", localErrors ); cleanupErrors.AddRange( localErrors ); }
    var controlsPassed = failure is null && emptyBeforePixels == 0 && emptyAfterPixels == 0 && referenceCoverage > 50;
    return Gate( failure is not null ? "failed" : assetCoverage > 0 && controlsPassed ? "captured" : "unverified",
        failure ?? (assetCoverage > 0 ? "Actual loaded model captured; appearance has no reference oracle." : "Images captured but nonblack coverage does not establish visibility for this asset."),
        new { failure, paths, framing, form, source_id = entry.GetProperty( "id" ).GetString(), source_path = entry.GetProperty( "path" ).GetString(),
            source_sha256 = manifestRoot.GetProperty( "files" ).GetProperty( entry.GetProperty( "path" ).GetString() ).GetProperty( "sha256" ).GetString(),
            reference_coverage = referenceCoverage, asset_nonblack_pixels = assetCoverage, empty_before_nonblack = emptyBeforePixels, empty_after_sphere_nonblack = emptyAfterSpherePixels,
            empty_after_nonblack = emptyAfterPixels, controls_passed = controlsPassed, engine_mvid = engine.ManifestModule.ModuleVersionId.ToString(),
            gltf_mvid = typeof( Sandbox.Gltf.GltfReader ).Assembly.ManifestModule.ModuleVersionId.ToString(), capture_renderer = captureRenderer,
            renderer_mvid = rendererLoadedMvid, renderer_sha256 = rendererBinaryHash, cleanup_errors = localErrors } );
}
async Task<System.Collections.Generic.Dictionary<string, object>> RunForm( System.Text.Json.JsonElement entry, string form, string caseDirectory, string virtualBase )
{
    Require( form is "original-source" or "derived-packaging-glb_c", "Unknown corpus source form." );
    var result = new System.Collections.Generic.Dictionary<string, object>( StringComparer.Ordinal )
    {
        ["form"] = form, ["passed"] = false, ["capture_renderer"] = captureRenderer,
        ["renderer_mvid"] = rendererLoadedMvid, ["renderer_sha256"] = rendererBinaryHash, ["load"] = Gate( "not_run", "Model.Load not reached" ),
        ["geometry"] = Gate( "unverified", "No loaded model numeric evidence" ),
        ["materials"] = Gate( "unverified", "No loaded model material evidence" ),
        ["render_capture"] = Gate( "not_run", "No loaded model captured" ),
        ["animation"] = Gate( entry.GetProperty( "animation_oracle" ).GetProperty( "status" ).GetString(), entry.GetProperty( "animation_oracle" ).GetProperty( "reason" ).GetString() ),
        ["render_appearance"] = Gate( "unverified", entry.GetProperty( "visual_oracle" ).GetProperty( "reason" ).GetString(), entry.GetProperty( "visual_oracle" ).Clone() )
    };
    var input = System.IO.Path.Combine( caseDirectory, form, "input" ); System.IO.Directory.CreateDirectory( input );
    var aliases = new System.Collections.Generic.List<string>();
    var messages = new System.Collections.Generic.List<string>(); var localCleanup = new System.Collections.Generic.List<string>();
    Model model = null; bool ownedModel = false, mounted = false;
    object redirectObject = null; IDisposable redirect = null; BaseFileSystem mount = null, aggregate = null;
    System.Reflection.MethodInfo unmount = null, removeRedirect = null, removeLog = null; Action<LogEvent> logHandler = null;
    string localPhase = "source_copy", logical = null;
    void Clean( string label, Action action ) { try { action(); } catch ( Exception ex ) { localCleanup.Add( label + ": " + ex ); } }
    try
    {
        await SafePoint(); CopySource( entry, input );
        var sourceRelative = entry.GetProperty( "path" ).GetString();
        var files = CaseFiles( entry ).ToDictionary( relative => relative, relative => Contained( input, relative ), StringComparer.Ordinal );
        if ( form == "derived-packaging-glb_c" )
        {
            localPhase = "package_fixture";
            byte[] Resolve( string uri )
            {
                Require( !Uri.TryCreate( uri, UriKind.Absolute, out _ ), "Compiler requested an absolute/nonlocal URI." );
                var physical = System.IO.Path.GetFullPath( System.IO.Path.Combine( System.IO.Path.GetDirectoryName( Contained( input, sourceRelative ) ), Uri.UnescapeDataString( uri ).Replace( '/', System.IO.Path.DirectorySeparatorChar ) ) );
                var relative = System.IO.Path.GetRelativePath( input, physical ).Replace( System.IO.Path.DirectorySeparatorChar, '/' );
                Require( files.ContainsKey( relative ), "Compiler requested an uninventoried dependency: " + uri );
                return ReadBounded( physical, maxFileBytes );
            }
            var source = ReadBounded( Contained( input, sourceRelative ), maxFileBytes );
            var compiled = Sandbox.Gltf.GltfCompiler.Compile( source, Resolve, selectedExtensions, maxFileBytes );
            var bundle = Sandbox.Gltf.GltfArtifactDependencies.Prepare( compiled, Resolve, selectedExtensions );
            var packaged = Contained( input, "package/model.glb_c" ); System.IO.Directory.CreateDirectory( System.IO.Path.GetDirectoryName( packaged ) );
            System.IO.File.WriteAllBytes( packaged, bundle.Artifact );
            files = new System.Collections.Generic.Dictionary<string, string>( StringComparer.Ordinal ) { ["package/model.glb_c"] = packaged };
            foreach ( var dependency in bundle.Dependencies )
            {
                var relative = "package/" + dependency.Key; var path = Contained( input, relative );
                System.IO.Directory.CreateDirectory( System.IO.Path.GetDirectoryName( path ) ); System.IO.File.WriteAllBytes( path, dependency.Value ); files.Add( relative, path );
            }
            result["compiled_fixture_sha256"] = Hash( bundle.Artifact );
            result["compiler_evidence_scope"] = "Packaging-only derivative produced by the implementation under test; source/compiled agreement is supplementary, not an independent oracle or GPU optimization proof.";
            logical = virtualBase + "/package/model.glb";
        }
        else logical = virtualBase + "/" + sourceRelative;
        await SafePoint(); localPhase = "mount_private_fixture";
        var redirectType = engine.GetType( "Sandbox.Internal.RedirectFileSystem", true );
        redirectObject = redirectType.GetMethod( "Create", flags ).Invoke( null, new object[] { input } ); redirect = (IDisposable)redirectObject;
        removeRedirect = redirectType.GetMethod( "RemoveAbsFile", flags );
        foreach ( var file in files )
        {
            var alias = "/" + virtualBase + "/" + file.Key;
            redirectType.GetMethod( "AddAbsFile", flags ).Invoke( redirectObject, new object[] { alias, file.Value } ); aliases.Add( alias );
        }
        var constructor = typeof( BaseFileSystem ).GetConstructors( flags ).Single( value => { var p = value.GetParameters(); return p.Length == 1 && p[0].ParameterType.IsInstanceOfType( redirectObject ); } );
        mount = (BaseFileSystem)constructor.Invoke( new[] { redirectObject } ); aggregate = Sandbox.FileSystem.Mounted;
        Require( aggregate is not null, "Managed mounted filesystem unavailable." );
        unmount = typeof( BaseFileSystem ).GetMethod( "UnMount", flags, null, new[] { typeof( BaseFileSystem ) }, null );
        typeof( BaseFileSystem ).GetMethod( "Mount", flags, null, new[] { typeof( BaseFileSystem ) }, null ).Invoke( aggregate, new object[] { mount } ); mounted = true;
        foreach ( var file in files ) Require( aggregate.FileExists( virtualBase + "/" + file.Key ), "Private mounted input is absent: " + file.Key );
        var logging = typeof( LogEvent ).Assembly.GetType( "Sandbox.Diagnostics.Logging", true );
        var logEvent = logging.GetEvent( "OnMessage", flags );
        logHandler = entryLog => { var text = entryLog.Message ?? ""; if ( !text.Contains( virtualBase, StringComparison.OrdinalIgnoreCase ) ) return; lock ( messages ) if ( messages.Count < 64 ) messages.Add( entryLog.Level + ": " + text[..Math.Min( text.Length, 2048 )] ); };
        logEvent.GetAddMethod( true ).Invoke( null, new object[] { logHandler } ); removeLog = logEvent.GetRemoveMethod( true );
        await SafePoint(); localPhase = "actual_Model.Load";
        result["asset_path"] = logical; model = Model.Load( logical );
        var loaded = Valid( model );
        ownedModel = loaded && string.Equals( model.ResourcePath, logical, StringComparison.OrdinalIgnoreCase );
        var selectionNeeded = entry.GetProperty( "default_scene" ).ValueKind == System.Text.Json.JsonValueKind.Null;
        result["load"] = Gate( loaded ? "passed" : "failed",
            loaded ? "Actual public Model.Load returned a valid model" : selectionNeeded ? "Actual Model.Load failed for valid glTF without a default scene; explicit scene selection/import remains unverified." : "Actual Model.Load failed; inspect messages/error. Unsupported core/selected features remain in scope." );
        result["model_loaded"] = loaded; result["owned_model_identity"] = ownedModel;
        Require( loaded, "Actual Model.Load returned null/error/invalid model." );
        localPhase = "capture_actual_loaded_model";
        result["render_capture"] = await CaptureModel( model, System.IO.Path.Combine( caseDirectory, form, "captures" ), entry, form );
        Require( cleanupErrors.Count == 0, "Corpus preview cleanup failed; stop before allocating another asset." );
        Require( ownedModel, "Returned model does not have the unique private asset identity; it will not be destroyed as task-owned." );
        await SafePoint();
        localPhase = "independent_partial_oracles";
        result["geometry"] = GeometryChecks( model, entry ); result["materials"] = MaterialChecks( model, entry );
        result["native_metadata"] = new { model.BoneCount, model.AnimationCount, animation_names = model.AnimationNames.ToArray() };
        // Full material, posed animation and matched appearance oracles are
        // deliberately absent. A successful load/count/bounds cannot fill them.
        result["passed"] = GateStatus( result["load"] ) == "passed" && GateStatus( result["render_capture"] ) == "captured" &&
            new[] { "geometry", "materials", "animation", "render_appearance" }.All( name => GateStatus( result[name] ) == "passed" );
        localPhase = "complete_partial_observations";
    }
    catch ( Exception ex ) when ( ex is not OutOfMemoryException && ex is not OperationCanceledException )
    {
        result["error"] = ex.ToString();
        if ( localPhase == "actual_Model.Load" && !result.ContainsKey( "model_loaded" ) )
            result["load"] = Gate( "failed", "Actual Model.Load threw; inspect the preserved error." );
    }
    finally
    {
        await MainThread.Wait();
        if ( model is not null && ownedModel ) Clean( "explicit owned Model.Destroy", () => typeof( Model ).GetMethod( "Destroy", flags ).Invoke( model, null ) );
        model = null;
        if ( mounted ) Clean( "private unmount", () => unmount.Invoke( aggregate, new object[] { mount } ) );
        if ( redirectObject is not null ) foreach ( var alias in aliases ) Clean( "private redirect", () => removeRedirect.Invoke( redirectObject, new object[] { alias } ) );
        if ( redirect is not null ) Clean( "private redirect dispose", () => redirect.Dispose() );
        if ( removeLog is not null && logHandler is not null ) Clean( "log handler", () => removeLog.Invoke( null, new object[] { logHandler } ) );
        if ( !keepInputs && localCleanup.Count == 0 ) Clean( "owned input cleanup", () => System.IO.Directory.Delete( input, recursive: true ) );
        result["phase"] = localPhase; result["relevant_messages"] = messages.ToArray(); result["cleanup_errors"] = localCleanup;
        result["explicit_model_destroy_invoked"] = ownedModel; result["input_copies_retained"] = System.IO.Directory.Exists( input );
        Save( System.IO.Path.Combine( caseDirectory, form + ".json" ), result );
        Require( localCleanup.Count == 0, "Corpus cleanup failed; stop before allocating another asset." );
        Require( cleanupErrors.Count == 0, "Corpus preview cleanup failed; stop before allocating another asset." );
    }
    return result;
}

try
{
    await SafePoint(); phase = "verify_loaded_modules";
    var gltfAssembly = typeof( Sandbox.Gltf.GltfReader ).Assembly;
    engineDiskMvid = DiskMvid( engine.Location ); gltfDiskMvid = DiskMvid( gltfAssembly.Location );
    Require( engineDiskMvid == engine.ManifestModule.ModuleVersionId.ToString() && gltfDiskMvid == gltfAssembly.ManifestModule.ModuleVersionId.ToString(), "Loaded engine or glTF module differs from its disk binary." );
    engineBinaryHash = HashFile( engine.Location ); gltfBinaryHash = HashFile( gltfAssembly.Location );
    var rendererAssembly = AppDomain.CurrentDomain.GetAssemblies().SingleOrDefault( assembly => !assembly.IsDynamic && assembly.GetName().Name == "Sandbox.SceneRenderer" && System.Runtime.Loader.AssemblyLoadContext.GetLoadContext( assembly ) == System.Runtime.Loader.AssemblyLoadContext.Default );
    Require( rendererAssembly is not null, "The default-context Sandbox.SceneRenderer is not loaded." );
    rendererLocation = rendererAssembly.Location; rendererLoadedMvid = rendererAssembly.ManifestModule.ModuleVersionId.ToString();
    rendererDiskMvid = DiskMvid( rendererLocation ); rendererBinaryHash = HashFile( rendererLocation );
    Require( rendererDiskMvid == rendererLoadedMvid, "Loaded scene renderer differs from its disk binary." );
    phase = "verify_pinned_inventory";
    runnerHash = Hash( ReadBounded( runnerPath, 1024 * 1024 ) );
    var manifestBytes = ReadBounded( manifestPath, 32L * 1024 * 1024 ); manifestHash = Hash( manifestBytes );
    using var document = System.Text.Json.JsonDocument.Parse( manifestBytes ); manifestRoot = document.RootElement.Clone();
    Require( manifestRoot.GetProperty( "version" ).GetInt32() == 1, "Expected corpus manifest version 1." );
    var corpusSpec = manifestRoot.GetProperty( "corpus" ); var pin = corpusSpec.GetProperty( "commit" ).GetString();
    Require( pin == "90d7ede14c7e280af263824604b427a1ca02cb66" && GitHead( corpus ) == pin, "Corpus Git HEAD differs from the audited pin." );
    foreach ( var value in manifestRoot.GetProperty( "selected_extensions" ).EnumerateArray() ) selectedExtensions.Add( value.GetString() );
    var entries = manifestRoot.GetProperty( "cases" ).EnumerateArray().ToArray();
    Require( entries.Length == 337 && corpusSpec.GetProperty( "case_count" ).GetInt32() == 337 && corpusSpec.GetProperty( "model_count" ).GetInt32() == 150, "Expected 337 corpus variants across 150 models." );
    var ids = entries.Select( entry => entry.GetProperty( "id" ).GetString() ).ToHashSet( StringComparer.Ordinal );
    var listedPaths = entries.Select( entry => entry.GetProperty( "path" ).GetString() ).ToHashSet( StringComparer.Ordinal );
    Require( ids.Count == 337 && listedPaths.Count == 337, "Duplicate corpus case IDs/paths." );
    VerifyFile( "Models/model-index.json" );
    using var indexDocument = System.Text.Json.JsonDocument.Parse( ReadBounded( Contained( corpus, "Models/model-index.json" ), 4 * 1024 * 1024 ) );
    var indexedPaths = indexDocument.RootElement.EnumerateArray().SelectMany( model => model.GetProperty( "variants" ).EnumerateObject()
        .Select( variant => "Models/" + model.GetProperty( "name" ).GetString() + "/" + variant.Name + "/" + variant.Value.GetString() ) ).ToHashSet( StringComparer.Ordinal );
    var diskPaths = new System.Collections.Generic.HashSet<string>( StringComparer.Ordinal );
    foreach ( var model in System.IO.Directory.EnumerateDirectories( Contained( corpus, "Models" ) ) )
        foreach ( var variant in System.IO.Directory.EnumerateDirectories( model ).Where( path => System.IO.Path.GetFileName( path ).StartsWith( "glTF", StringComparison.Ordinal ) ) )
            foreach ( var path in System.IO.Directory.EnumerateFiles( variant ) )
                if ( System.IO.Path.GetExtension( path ).Equals( ".gltf", StringComparison.OrdinalIgnoreCase ) || System.IO.Path.GetExtension( path ).Equals( ".glb", StringComparison.OrdinalIgnoreCase ) )
                    diskPaths.Add( System.IO.Path.GetRelativePath( corpus, path ).Replace( System.IO.Path.DirectorySeparatorChar, '/' ) );
    Require( listedPaths.SetEquals( indexedPaths ) && listedPaths.SetEquals( diskPaths ), "337 indexed/manifest/disk variants differ; omissions are not permitted." );
    foreach ( var tableName in new[] { "material_contract_tables", "node_material_binding_tables" } )
        foreach ( var table in manifestRoot.GetProperty( tableName ).EnumerateObject() )
            Require( Hex( table.Name, 64 ) && Hash( System.Text.Encoding.UTF8.GetBytes( table.Value.GetRawText() ) ) == table.Name, "Shared immutable corpus contract hash differs: " + tableName + "/" + table.Name );
    foreach ( var entry in entries.Where( entry => entry.GetProperty( "classification" ).GetString() != "inventory_error" ) )
    {
        Require( manifestRoot.GetProperty( "material_contract_tables" ).TryGetProperty( entry.GetProperty( "material_contract_ref" ).GetString(), out _ ), "Missing shared material contract." );
        Require( manifestRoot.GetProperty( "node_material_binding_tables" ).TryGetProperty( entry.GetProperty( "node_material_binding_ref" ).GetString(), out _ ), "Missing shared node/material binding map." );
    }
    foreach ( var license in manifestRoot.GetProperty( "license_files" ).EnumerateArray() ) VerifyFile( license.GetString() );
    phase = "read_optional_request";
    var requestBytes = ReadBounded( requestPath, 4096, optional: true );
    if ( requestBytes is not null )
    {
        requestHash = Hash( requestBytes );
        using var requestDocument = System.Text.Json.JsonDocument.Parse( new System.Text.UTF8Encoding( false, true ).GetString( requestBytes ), new System.Text.Json.JsonDocumentOptions { MaxDepth = 4 } );
        var request = requestDocument.RootElement;
        Require( request.ValueKind == System.Text.Json.JsonValueKind.Object, "Corpus request must be an object." );
        var fields = new System.Collections.Generic.HashSet<string>( StringComparer.Ordinal );
        foreach ( var field in request.EnumerateObject() ) Require( fields.Add( field.Name ), "Duplicate corpus request field." );
        var requestVersion = request.GetProperty( "version" ).GetInt32();
        var expectedFields = new[] { "version", "request_id", "manifest_sha256", "case_ids", "max_estimated_bytes", "max_file_bytes", "keep_inputs" };
        Require( requestVersion is 1 or 2 && fields.SetEquals( requestVersion == 1 ? expectedFields : expectedFields.Append( "capture_renderer" ) ), "Unexpected corpus request schema or version." );
        if ( requestVersion == 2 )
        {
            captureRenderer = request.GetProperty( "capture_renderer" ).GetString();
            Require( captureRenderer is "native" or "managed", "Invalid capture renderer." );
        }
        requestId = request.GetProperty( "request_id" ).GetString(); Require( Hex( requestId, 32 ), "Invalid corpus request ID." );
        Require( request.GetProperty( "manifest_sha256" ).GetString() == manifestHash, "Corpus request targets another manifest." );
        foreach ( var id in request.GetProperty( "case_ids" ).EnumerateArray().Select( item => item.GetString() ) ) Require( ids.Contains( id ) && requestedIds.Add( id ), "Unknown/duplicate requested corpus case: " + id );
        selectionIsAll = requestedIds.Count == 0;
        maxEstimatedBytes = request.GetProperty( "max_estimated_bytes" ).GetInt64(); maxFileBytes = request.GetProperty( "max_file_bytes" ).GetInt64(); keepInputs = request.GetProperty( "keep_inputs" ).GetBoolean();
        Require( maxEstimatedBytes >= 1024 * 1024 && maxEstimatedBytes <= 2L * 1024 * 1024 * 1024 && maxFileBytes >= 1024 * 1024 && maxFileBytes <= 256L * 1024 * 1024, "Corpus request resource bounds are outside supported limits." );
    }
    if ( captureRenderer == "managed" )
    {
        var managedType = engine.GetType( "Sandbox.Rendering.ManagedSceneRendering", true );
        managedRenderer = managedType.GetProperty( "Renderer", flags )?.GetValue( null );
        Require( managedRenderer is not null && managedRenderer.GetType().Assembly == rendererAssembly, "Managed scene renderer is unavailable or does not match the verified module." );
        managedRenderToTexture = managedRenderer.GetType().GetMethod( "RenderToTexture", flags, null, new[] { typeof( CameraComponent ), typeof( Texture ) }, null );
        Require( managedRenderToTexture is not null && managedRenderToTexture.ReturnType == typeof( bool ), "Managed scene capture API is unavailable." );
    }
    Save( System.IO.Path.Combine( output, "inputs.json" ), new { manifest_sha256 = manifestHash, runner_source_sha256 = runnerHash, request_sha256 = requestHash, request_id = requestId, pin, corpus, selected_ids = requestedIds.ToArray(), max_estimated_bytes = maxEstimatedBytes, max_file_bytes = maxFileBytes, keep_inputs = keepInputs, capture_renderer = captureRenderer, renderer_mvid = rendererLoadedMvid, renderer_sha256 = rendererBinaryHash, estimates_are_not_hard_caps = true } );
    var chosen = entries.Where( entry => selectionIsAll || requestedIds.Contains( entry.GetProperty( "id" ).GetString() ) ).ToArray(); selectedCount = chosen.Length;
    phase = "run_cases";
    foreach ( var entry in chosen )
    {
        var id = entry.GetProperty( "id" ).GetString(); var classification = entry.GetProperty( "classification" ).GetString();
        var key = Hash( System.Text.Encoding.UTF8.GetBytes( id ) )[..20];
        var caseDirectory = System.IO.Path.Combine( output, "cases", key ); System.IO.Directory.CreateDirectory( caseDirectory );
        var receiptPath = System.IO.Path.Combine( caseDirectory, "receipt.json" );
        var forms = new System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, object>>();
        var record = new System.Collections.Generic.Dictionary<string, object>( StringComparer.Ordinal )
        {
            ["id"] = id, ["classification"] = classification, ["passed"] = false, ["status"] = "not_dispatched",
            ["corpus_commit"] = pin, ["manifest_sha256"] = manifestHash, ["runner_source_sha256"] = runnerHash,
            ["source_path"] = entry.GetProperty( "path" ).GetString(), ["provenance"] = entry.GetProperty( "provenance" ).Clone(),
            ["source_file_manifest"] = CaseFiles( entry ).ToDictionary( path => path, path => manifestRoot.GetProperty( "files" ).GetProperty( path ).Clone(), StringComparer.Ordinal ),
            ["feature_ids"] = entry.GetProperty( "feature_ids" ).Clone(), ["forms"] = forms,
            ["upstream_defect"] = entry.GetProperty( "upstream_defect" ).Clone()
        };
        Save( receiptPath, record );
        try
        {
            if ( classification == "out_of_scope_required_extension" )
            {
                excludedCount++; record["status"] = "excluded_required_extension";
                record["reason"] = "Required extension outside the selected scope; row retained, not counted as supported.";
                record["unsupported_required_extensions"] = entry.GetProperty( "unsupported_required_extensions" ).Clone();
                continue;
            }
            applicableCount++;
            if ( classification != "applicable" ) { failedCount++; record["status"] = "inventory_error"; record["error"] = entry.GetProperty( "error" ).GetString(); continue; }
            if ( stopReason is not null || cancellationToken.IsCancellationRequested || Game.IsPlaying )
            {
                stopReason ??= Game.IsPlaying ? "human_play_active" : "cancelled"; pendingCount++; record["status"] = "not_run"; record["reason"] = stopReason; continue;
            }
            var estimated = entry.GetProperty( "estimated_peak_bytes" ).GetInt64();
            if ( estimated > maxEstimatedBytes || CaseFiles( entry ).Any( path => manifestRoot.GetProperty( "files" ).GetProperty( path ).GetProperty( "bytes" ).GetInt64() > maxFileBytes ) )
            {
                pendingCount++; record["status"] = "resource_admission_pending"; record["estimated_peak_bytes"] = estimated;
                record["reason"] = "Valid feature/asset remains applicable; root must admit an appropriately budgeted isolated run. This is not an exclusion."; continue;
            }
            await SafePoint();
            VerifyFile( entry.GetProperty( "provenance" ).GetProperty( "readme" ).GetString() );
            VerifyFile( entry.GetProperty( "provenance" ).GetProperty( "metadata" ).GetString() );
            foreach ( var form in entry.GetProperty( "required_forms" ).EnumerateArray().Select( item => item.GetString() ) )
            {
                await SafePoint();
                forms.Add( await RunForm( entry, form, caseDirectory, "gltf-corpus/" + runId + "/" + key + "/" + form ) );
            }
            var allLoaded = forms.Count == 2 && forms.All( form => form.TryGetValue( "model_loaded", out var loaded ) && loaded is true );
            foreach ( var form in forms.Where( form => form.TryGetValue( "model_loaded", out var loaded ) && loaded is true ) )
            {
                loadedForms++;
                if ( GateStatus( form["render_capture"] ) == "captured" ) capturedForms++; else failedOrUnverifiedCaptures++;
            }
            if ( allLoaded ) applicableLoaded++;
            record["all_forms_loaded"] = allLoaded;
            // Each gate remains distinct. No full geometry/material/animation/
            // appearance oracle is replaced with load or roundtrip agreement.
            var intended = forms.Count == 2 && forms.All( form => form.TryGetValue( "passed", out var passed ) && passed is true );
            if ( intended ) intendedPassed++; else failedCount++;
            record["passed"] = intended; record["status"] = intended ? "passed" : "failed_or_unverified";
        }
        catch ( OutOfMemoryException ex ) { stopReason = "out_of_memory_stop"; pendingCount++; record["status"] = "resource_failure"; record["error"] = ex.ToString(); }
        catch ( OperationCanceledException ex ) { stopReason = "cancelled"; pendingCount++; record["status"] = "not_completed"; record["error"] = ex.ToString(); }
        catch ( Exception ex )
        {
            failedCount++; record["status"] = "failed"; record["error"] = ex.ToString();
            if ( Game.IsPlaying ) stopReason = "human_play_active";
            if ( ex.Message.Contains( "cleanup failed", StringComparison.OrdinalIgnoreCase ) ) stopReason = "cleanup_failure_stop";
        }
        finally
        {
            completedCount++; Save( receiptPath, record );
            var summary = new { id, classification, status = record["status"], passed = record["passed"], receipt_path = receiptPath };
            summaries.Add( summary );
            System.IO.File.AppendAllText( System.IO.Path.Combine( output, "progress.jsonl" ), System.Text.Json.JsonSerializer.Serialize( summary ) + "\n" );
        }
    }
    Require( GitHead( corpus ) == pin, "Corpus HEAD changed during run." );
    phase = "complete";
}
catch ( Exception ex ) { error = ex.ToString(); phase = Game.IsPlaying ? "deferred_human_play" : phase; }
var applicableFullPass = error is null && stopReason is null && selectionIsAll && completedCount == 337 && applicableCount > 0 && intendedPassed == applicableCount && pendingCount == 0 && failedCount == 0 && cleanupErrors.Count == 0;
var finalReceipt = new
{
    passed = applicableFullPass, applicable_full_pass = applicableFullPass, corpus_full_pass = applicableFullPass && excludedCount == 0,
    all_applicable_forms_loaded = selectionIsAll && completedCount == 337 && applicableCount > 0 && applicableLoaded == applicableCount,
    phase, error, stop_reason = stopReason, scope = "Pinned 337-row original/packaging-only Model.Load corpus test. Independent numeric checks are partial; missing material/animation/full-appearance oracles remain unverified. No load-only or implementation roundtrip claim of conformance.",
    expected_inventory_cases = 337, selected_cases = selectedCount, completed_case_receipts = completedCount,
    applicable_cases = applicableCount, applicable_both_forms_loaded = applicableLoaded, intended_passed_cases = intendedPassed,
    loaded_forms = loadedForms, captured_forms = capturedForms, failed_or_unverified_captures = failedOrUnverifiedCaptures,
    failed_or_unverified_cases = failedCount, pending_cases = pendingCount, excluded_required_extension_cases = excludedCount,
    cases = summaries, cleanup_errors = cleanupErrors, manifest_sha256 = manifestHash, runner_source_sha256 = runnerHash,
    request_path = requestPath, request_sha256 = requestHash, request_id = requestId, corpus_root = corpus,
    engine_mvid = engine.ManifestModule.ModuleVersionId.ToString(), gltf_mvid = typeof( Sandbox.Gltf.GltfReader ).Assembly.ManifestModule.ModuleVersionId.ToString(),
    engine_disk_mvid = engineDiskMvid, gltf_disk_mvid = gltfDiskMvid, engine_binary_sha256 = engineBinaryHash, gltf_binary_sha256 = gltfBinaryHash,
    capture_renderer = captureRenderer, renderer_location = rendererLocation, renderer_mvid = rendererLoadedMvid, renderer_disk_mvid = rendererDiskMvid, renderer_binary_sha256 = rendererBinaryHash,
    is_editor = Application.IsEditor, starting_frame = startingFrame, final_frame = Application.FrameCount, output
};
var finalPath = System.IO.Path.Combine( output, "receipt.json" ); Save( finalPath, finalReceipt );
return new { passed = applicableFullPass, phase, error = error is null ? null : error[..Math.Min( error.Length, 500 )], selected_cases = selectedCount, applicable_cases = applicableCount, intended_passed_cases = intendedPassed, pending_cases = pendingCount, excluded_required_extension_cases = excludedCount, receipt_path = finalPath };
