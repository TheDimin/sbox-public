// Run as an async method body with the existing editor's run_csharp tool.
// Uses a private preview scene and never edits the user's open scene or assets.
var engineLocation = typeof( Model ).Assembly.Location;
var root = new System.IO.DirectoryInfo( System.IO.Path.GetDirectoryName( engineLocation ) );
while ( root is not null && !System.IO.File.Exists( System.IO.Path.Combine( root.FullName, "engine", "Sandbox-Engine.slnx" ) ) ) root = root.Parent;
if ( root is null ) throw new InvalidOperationException( "Could not locate the s&box checkout from loaded engine: " + engineLocation );
var corpusRoot = Environment.GetEnvironmentVariable( "SBOX_GLTF_CORPUS" );
if ( string.IsNullOrWhiteSpace( corpusRoot ) ) corpusRoot = System.IO.Path.Combine( root.FullName, "Temp", "gltf-reference", "glTF-Sample-Assets" );
var corpus = System.IO.Path.Combine( corpusRoot, "Models", "Triangle", "glTF" );
var diagnosticMode = Environment.GetEnvironmentVariable( "SBOX_GLTF_CAMERA_CLEAR" );
var diagnosticCameraClear = diagnosticMode == "1";
var diagnosticCameraNoop = diagnosticMode == "noop";
var output = System.IO.Path.Combine( root.FullName, "Temp", "gltf-validation", diagnosticCameraClear ? "editor-render-camera-clear" : diagnosticCameraNoop ? "editor-render-camera-noop" : "editor-render" );
System.IO.Directory.CreateDirectory( output );
var receiptPath = System.IO.Path.Combine( output, "receipt.json" );
var completed = new System.Collections.Generic.List<string>();
var captures = new System.Collections.Generic.List<object>();
var cells = new System.Collections.Generic.List<object>();
var failures = new System.Collections.Generic.List<string>();
var dependencies = new System.Collections.Generic.Dictionary<string, string>();
var leases = new System.Collections.Generic.List<IDisposable>();
var ownedModels = new System.Collections.Generic.List<Model>();
var phase = "load";
var startFrame = Application.FrameCount;
string digest = null;
string compiledDigest = null;
Scene scene = null;
Editor.SceneRenderingWidget preview = null;
SceneModel modelObject = null;
Texture target = null;
Scene freshScene = null;
Editor.SceneRenderingWidget freshPreview = null;
Texture freshTarget = null;
Action onPreviewFrame = null;
Action onFreshPreviewFrame = null;
var previewFrames = 0;
var freshPreviewFrames = 0;
var commandCallbacks = 0;
var freshCommandCallbacks = 0;
CameraComponent commandCamera = null;
CameraComponent commandFreshCamera = null;
Sandbox.Rendering.CommandList cameraClearCommands = null;
Sandbox.Rendering.CommandList freshCameraClearCommands = null;
object finalControls = null;
object MakeReceipt( bool passed, string error = null ) => new
{
    passed, phase, error, completed, failures, captures, cells, final_controls = finalControls, source_sha256 = digest, compiled_sha256 = compiledDigest, dependencies,
    scope = "editor stock albedo geometry, winding and shared material lease; not full glTF, lighting, or material fidelity",
    engine_location = engineLocation, engine_mvid = typeof( Model ).Assembly.ManifestModule.ModuleVersionId.ToString(),
    gltf_mvid = typeof( Sandbox.Gltf.GltfReader ).Assembly.ManifestModule.ModuleVersionId.ToString(),
    editor = Application.IsEditor, diagnostic_camera_clear = diagnosticCameraClear, diagnostic_camera_noop = diagnosticCameraNoop, command_callbacks = commandCallbacks, fresh_command_callbacks = freshCommandCallbacks, capture_preview_callbacks_before_and_after = 3, starting_frame = startFrame, final_frame = Application.FrameCount, corpus_root = corpusRoot, output
};
void SaveReceipt( object receipt ) => System.IO.File.WriteAllText( receiptPath, System.Text.Json.JsonSerializer.Serialize( receipt, new System.Text.Json.JsonSerializerOptions { WriteIndented = true } ) );
try
{
    if ( !Application.IsEditor ) throw new InvalidOperationException( "This validation requires a running s&box editor." );
    var input = System.IO.File.ReadAllBytes( System.IO.Path.Combine( corpus, "Triangle.gltf" ) );
    digest = Convert.ToHexStringLower( System.Security.Cryptography.SHA256.HashData( input ) );
    if ( digest != "6f4a8a1fbefbac2d0adde0800864279fd98c3f119077380e6840810ed62b6cfc" ) throw new InvalidOperationException( "Unexpected Khronos Triangle source hash: " + digest );
    byte[] Resolve( string uri )
    {
        var bytes = System.IO.File.ReadAllBytes( System.IO.Path.Combine( corpus, uri ) );
        dependencies[uri] = Convert.ToHexStringLower( System.Security.Cryptography.SHA256.HashData( bytes ) );
        return bytes;
    }
    var compiled = Sandbox.Gltf.GltfCompiler.Compile( input, Resolve );
    compiledDigest = Convert.ToHexStringLower( System.Security.Cryptography.SHA256.HashData( compiled ) );
    using var sourceDoc = Sandbox.Gltf.GltfReader.Read( input, Resolve );
    using var compiledDoc = Sandbox.Gltf.GltfReader.Read( compiled );
    var sourceData = sourceDoc.CreateDrawData( 0, 0 );
    var compiledData = compiledDoc.CreateDrawData( 0, 0 );
    var engine = typeof( Model ).Assembly;
    var builderType = engine.GetType( "Sandbox.GltfModelPrimitiveBuilder", true );
    var build = builderType.GetMethod( "Build", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic ) ?? throw new InvalidOperationException( "Missing glTF primitive builder." );
    var stock = Material.Load( "materials/default/white.vmat" );
    if ( stock is null || !stock.IsValid || stock.IsError ) throw new InvalidOperationException( "Stock white material unavailable." );

    phase = "shared-material-leases";
    var keyType = engine.GetType( "Sandbox.GltfMaterialKey", true );
    var bindingType = engine.GetType( "Sandbox.GltfMaterialTextureBinding", true );
    var cacheType = engine.GetType( "Sandbox.GltfMaterialCache", true );
    var cache = cacheType.GetProperty( "Shared", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic ).GetValue( null );
    var acquire = cacheType.GetMethod( "Acquire", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic );
    var constructor = keyType.GetConstructors( System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic ).Single();
    Material AcquireMaterial( int backfaces )
    {
        object MakeKey() => constructor.Invoke( new object[] { stock, 0, new[] { new System.Collections.Generic.KeyValuePair<string, int>( "F_RENDER_BACKFACES", backfaces ) }, Array.Empty<System.Collections.Generic.KeyValuePair<string, Vector4>>(), Array.CreateInstance( bindingType, 0 ), Array.Empty<System.Collections.Generic.KeyValuePair<string, float>>() } );
        var first = acquire.Invoke( cache, new[] { MakeKey() } );
        leases.Add( (IDisposable)first );
        var second = acquire.Invoke( cache, new[] { MakeKey() } );
        leases.Add( (IDisposable)second );
        var value = first.GetType().GetProperty( "Value", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic );
        var material = (Material)value.GetValue( first );
        if ( !ReferenceEquals( material, value.GetValue( second ) ) || !material.IsValid || material.GetFeature( "F_RENDER_BACKFACES" ) != backfaces ) throw new InvalidOperationException( "Equivalent descriptors failed to share the correct material." );
        return material;
    }
    var single = AcquireMaterial( 0 );
    var doubleSided = AcquireMaterial( 1 );
    if ( ReferenceEquals( single, doubleSided ) ) throw new InvalidOperationException( "Different feature descriptors shared a material." );
    completed.Add( phase );

    phase = "create-models";
    var suffix = Guid.NewGuid().ToString( "N" );
    var sourceModel = (Model)build.Invoke( null, new object[] { sourceData, single, "gltf_editor_source_" + suffix } );
    var compiledModel = (Model)build.Invoke( null, new object[] { compiledData, single, "gltf_editor_compiled_" + suffix } );
    Model Reference( bool reverse )
    {
        const float unit = 1f / 0.0254f;
        var mesh = new Mesh( single );
        mesh.CreateVertexBuffer( 3, new Vertex[] {
            new() { Position = new Vector3( 0, 0, 0 ), Normal = Vector3.Forward, Color = Color32.White },
            new() { Position = new Vector3( 0, unit, 0 ), Normal = Vector3.Forward, Color = Color32.White },
            new() { Position = new Vector3( 0, 0, unit ), Normal = Vector3.Forward, Color = Color32.White } } );
        mesh.CreateIndexBuffer( 3, reverse ? new[] { 0, 2, 1 } : new[] { 0, 1, 2 } );
        var bounds = new BBox( Vector3.Zero, new Vector3( 0, unit, unit ) );
        mesh.Bounds = bounds;
        return Model.Builder.WithName( "gltf_editor_reference_" + reverse + suffix ).AddMesh( mesh ).WithViewBounds( bounds ).Create();
    }
    var original = Reference( false );
    var reversed = Reference( true );
    ownedModels.AddRange( new[] { original, reversed, sourceModel, compiledModel } );
    if ( new[] { original, reversed, sourceModel, compiledModel }.Any( m => m is null || !m.IsValid || m.IsError ) ) throw new InvalidOperationException( "A reference or glTF model is invalid." );
    completed.Add( phase );

    phase = "create-preview";
    scene = Scene.CreateEditorScene();
    CameraComponent camera;
    using ( scene.Push() )
    {
        camera = scene.CreateObject().Components.Create<CameraComponent>();
        camera.BackgroundColor = Color.Black;
        camera.ZNear = 1;
        camera.FieldOfView = 50;
        camera.EnablePostProcessing = false;
        camera.DebugMode = SceneCameraDebugMode.Albedo;
    }
    if ( diagnosticCameraClear || diagnosticCameraNoop )
    {
        cameraClearCommands = new Sandbox.Rendering.CommandList( "glTF diagnostic camera " + diagnosticMode );
        if ( diagnosticCameraClear ) cameraClearCommands.Clear( Color.Black, true, false, false );
        else cameraClearCommands.AddAction( () => System.Threading.Interlocked.Increment( ref commandCallbacks ) );
        camera.AddCommandList( cameraClearCommands, Sandbox.Rendering.Stage.AfterDepthPrepass );
        commandCamera = camera;
    }
    preview = new Editor.SceneRenderingWidget { Scene = scene, Camera = camera, WindowTitle = "glTF isolated validation preview", Size = new Vector2( 384, 384 ) };
    onPreviewFrame = () => previewFrames++;
    preview.OnPreFrame += onPreviewFrame;
    preview.Show();
    target = Texture.CreateRenderTarget().WithSize( 96, 96 ).WithFormat( ImageFormat.RGBA8888 ).Create( "gltf_editor_capture_" + suffix );
    modelObject = new SceneModel( scene.SceneWorld, original, Transform.Zero );
    modelObject.RenderingEnabled = false;
    completed.Add( phase );

    async Task WaitForPreviewFrames( Editor.SceneRenderingWidget capturePreview, Func<int> count )
    {
        var first = count();
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while ( count() < first + 3 )
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ( timer.Elapsed > TimeSpan.FromSeconds( 5 ) ) throw new TimeoutException( $"Preview did not receive three OnPreFrame callbacks (started {first}, now {count()}, visible {capturePreview.Visible}, size {capturePreview.Size})." );
            await Task.Delay( 50, cancellationToken );
            await MainThread.Wait();
        }
    }
    async Task<Color[]> CaptureFrom( string name, Scene captureScene, CameraComponent captureCamera, Texture captureTarget, Editor.SceneRenderingWidget capturePreview, Func<int> count )
    {
        phase = name;
        await WaitForPreviewFrames( capturePreview, count );
        var beforeSubmit = count();
        cancellationToken.ThrowIfCancellationRequested();
        using ( captureScene.Push() )
        {
            captureTarget.Clear( Color.Black );
            if ( !captureCamera.RenderToTexture( captureTarget ) ) throw new InvalidOperationException( "Camera failed to submit " + name );
        }
        await WaitForPreviewFrames( capturePreview, count );
        cancellationToken.ThrowIfCancellationRequested();
        using var bitmap = captureTarget.GetBitmap();
        if ( bitmap is null ) throw new InvalidOperationException( "Camera readback failed: " + name );
        var bytes = bitmap.ToPng();
        var path = System.IO.Path.Combine( output, name + ".png" );
        System.IO.File.WriteAllBytes( path, bytes );
        var reflection = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
        var sceneCamera = typeof( CameraComponent ).GetProperty( "SceneCamera", reflection )?.GetValue( captureCamera );
        var cameraSize = sceneCamera?.GetType().GetProperty( "Size", reflection )?.GetValue( sceneCamera )?.ToString();
        captures.Add( new { name, path, sha256 = Convert.ToHexStringLower( System.Security.Cryptography.SHA256.HashData( bytes ) ), frame = Application.FrameCount, preview_frames_at_submit = beforeSubmit, preview_frames_at_readback = count(), preview_visible = capturePreview.Visible, preview_size = capturePreview.Size.ToString(), camera_size = cameraSize } );
        completed.Add( name );
        return bitmap.GetPixels();
    }
    Task<Color[]> Capture( string name ) => CaptureFrom( name, scene, camera, target, preview, () => previewFrames );
    int Difference( Color[] a, Color[] b ) => a.Zip( b, ( x, y ) => MathF.Abs( x.r - y.r ) > 0.03f || MathF.Abs( x.g - y.g ) > 0.03f || MathF.Abs( x.b - y.b ) > 0.03f ).Count( x => x );
    Color[] lastBackground = null;
    foreach ( var side in new[] { 1, -1 } )
    {
        camera.WorldPosition = new Vector3( 80 * side, 19.68504f, 19.68504f );
        camera.WorldRotation = side > 0 ? Rotation.FromYaw( 180 ) : Rotation.Identity;
        foreach ( var backfaces in new[] { 0, 1 } )
        {
            var prefix = $"side{(side > 0 ? "plus" : "minus")}_backfaces{backfaces}";
            var chosen = backfaces == 0 ? single : doubleSided;
            modelObject.RenderingEnabled = false;
            var background = await Capture( prefix + "_background" );
            lastBackground = background;
            async Task<Color[]> Draw( Model model, string label )
            {
                modelObject.RenderingEnabled = false;
                var before = await Capture( prefix + "_" + label + "_before" );
                var beforeDifference = Difference( background, before );
                if ( beforeDifference > 5 ) failures.Add( $"{prefix} {label}: before background differs by {beforeDifference} pixels" );
                modelObject.Model = model;
                modelObject.SetMaterialOverride( chosen );
                modelObject.RenderingEnabled = true;
                var pixels = await Capture( prefix + "_" + label );
                modelObject.RenderingEnabled = false;
                var after = await Capture( prefix + "_" + label + "_after" );
                var afterDifference = Difference( background, after );
                if ( afterDifference > 5 ) failures.Add( $"{prefix} {label}: after background differs by {afterDifference} pixels" );
                return pixels;
            }
            var refPixels = await Draw( original, "reference" );
            var revPixels = await Draw( reversed, "reverse" );
            var srcPixels = await Draw( sourceModel, "source" );
            var compPixels = await Draw( compiledModel, "compiled" );
            var refCount = Difference( refPixels, background );
            var revCount = Difference( revPixels, background );
            var srcCount = Difference( srcPixels, background );
            var compCount = Difference( compPixels, background );
            var srcDiff = Difference( refPixels, srcPixels );
            var compDiff = Difference( refPixels, compPixels );
            cells.Add( new { side, backfaces, reference = refCount, reverse = revCount, source = srcCount, compiled = compCount, source_difference = srcDiff, compiled_difference = compDiff } );
            if ( Math.Max( refCount, revCount ) <= 50 ) failures.Add( prefix + ": neither native winding is visible" );
            if ( backfaces == 0 && (refCount > 50) == (revCount > 50) ) failures.Add( prefix + ": single-sided windings are not distinguished" );
            if ( backfaces == 1 && Math.Min( refCount, revCount ) <= 50 ) failures.Add( prefix + ": double-sided winding is missing" );
            if ( srcDiff > 5 || compDiff > 5 ) failures.Add( $"{prefix}: source/compiled differ from native reference ({srcDiff}/{compDiff} pixels)" );
        }
    }
    modelObject.RenderingEnabled = true;
    var visibleBeforeDelete = await Capture( "visible_before_delete" );
    var beforeDeleteCoverage = Difference( lastBackground, visibleBeforeDelete );
    if ( beforeDeleteCoverage <= 50 ) failures.Add( "Deleted-model control was not visibly rendered before deletion" );
    phase = "delete-model";
    modelObject.Delete();
    if ( modelObject.IsValid() ) throw new InvalidOperationException( "Deleted preview model remains valid." );
    modelObject = null;
    var deletedPixels = await Capture( "deleted_model" );
    var deletedDifference = Difference( lastBackground, deletedPixels );
    if ( deletedDifference > 5 ) failures.Add( $"Deleted-model background differs by {deletedDifference} pixels" );

    phase = "create-fresh-preview";
    freshScene = Scene.CreateEditorScene();
    CameraComponent freshCamera;
    using ( freshScene.Push() )
    {
        freshCamera = freshScene.CreateObject().Components.Create<CameraComponent>();
        freshCamera.WorldPosition = camera.WorldPosition;
        freshCamera.WorldRotation = camera.WorldRotation;
        freshCamera.BackgroundColor = camera.BackgroundColor;
        freshCamera.ZNear = camera.ZNear;
        freshCamera.FieldOfView = camera.FieldOfView;
        freshCamera.EnablePostProcessing = camera.EnablePostProcessing;
        freshCamera.DebugMode = camera.DebugMode;
    }
    if ( diagnosticCameraClear || diagnosticCameraNoop )
    {
        freshCameraClearCommands = new Sandbox.Rendering.CommandList( "glTF diagnostic fresh camera " + diagnosticMode );
        if ( diagnosticCameraClear ) freshCameraClearCommands.Clear( Color.Black, true, false, false );
        else freshCameraClearCommands.AddAction( () => System.Threading.Interlocked.Increment( ref freshCommandCallbacks ) );
        freshCamera.AddCommandList( freshCameraClearCommands, Sandbox.Rendering.Stage.AfterDepthPrepass );
        commandFreshCamera = freshCamera;
    }
    freshPreview = new Editor.SceneRenderingWidget { Scene = freshScene, Camera = freshCamera, WindowTitle = "glTF fresh empty validation preview", Size = new Vector2( 384, 384 ) };
    onFreshPreviewFrame = () => freshPreviewFrames++;
    freshPreview.OnPreFrame += onFreshPreviewFrame;
    freshPreview.Show();
    freshTarget = Texture.CreateRenderTarget().WithSize( 96, 96 ).WithFormat( ImageFormat.RGBA8888 ).WithInitialColor( Color.Magenta ).Create( "gltf_editor_fresh_capture_" + suffix );
    var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
    var worldField = typeof( SceneWorld ).GetField( "native", flags ) ?? throw new InvalidOperationException( "Could not inspect native scene-world identity." );
    var textureField = typeof( Texture ).GetField( "native", flags ) ?? throw new InvalidOperationException( "Could not inspect native target identity." );
    var cameraField = typeof( SceneCamera ).GetField( "_cameraId", flags ) ?? throw new InvalidOperationException( "Could not inspect camera identity." );
    var cameraProperty = typeof( CameraComponent ).GetProperty( "SceneCamera", flags ) ?? throw new InvalidOperationException( "Could not inspect component camera." );
    var worldsDistinct = !Equals( worldField.GetValue( scene.SceneWorld ), worldField.GetValue( freshScene.SceneWorld ) );
    var targetsDistinct = !Equals( textureField.GetValue( target ), textureField.GetValue( freshTarget ) );
    var camerasDistinct = !Equals( cameraField.GetValue( cameraProperty.GetValue( camera ) ), cameraField.GetValue( cameraProperty.GetValue( freshCamera ) ) );
    var widgetsDistinct = !ReferenceEquals( preview, freshPreview );
    if ( !worldsDistinct || !targetsDistinct || !camerasDistinct || !widgetsDistinct ) throw new InvalidOperationException( "Fresh preview did not receive distinct scene, camera, target and widget identities." );
    modelObject = new SceneModel( scene.SceneWorld, compiledModel, Transform.Zero );
    modelObject.SetMaterialOverride( doubleSided );
    var visibleBeforeFresh = await Capture( "visible_before_fresh" );
    var beforeFreshCoverage = Difference( lastBackground, visibleBeforeFresh );
    if ( beforeFreshCoverage <= 50 ) failures.Add( "Fresh-scene control was not preceded by visible geometry in the first preview" );
    var freshPixels = await CaptureFrom( "fresh_empty", freshScene, freshCamera, freshTarget, freshPreview, () => freshPreviewFrames );
    var freshDifference = Difference( lastBackground, freshPixels );
    if ( freshDifference > 5 ) failures.Add( $"Fresh empty background differs by {freshDifference} pixels" );
    finalControls = new { before_delete_coverage = beforeDeleteCoverage, before_fresh_coverage = beforeFreshCoverage, deleted_difference = deletedDifference, fresh_difference = freshDifference, worlds_distinct = worldsDistinct, targets_distinct = targetsDistinct, cameras_distinct = camerasDistinct, widgets_distinct = widgetsDistinct };
    if ( diagnosticCameraNoop && (commandCallbacks == 0 || freshCommandCallbacks == 0) ) failures.Add( "No-op command callbacks did not execute on both cameras" );
    completed.Add( "final-controls" );
    phase = "complete";
    var receipt = MakeReceipt( failures.Count == 0 );
    SaveReceipt( receipt );
    return receipt;
}
catch ( OperationCanceledException ) { throw; }
catch ( Exception error )
{
    var receipt = MakeReceipt( false, error.ToString() );
    SaveReceipt( receipt );
    return receipt;
}
finally
{
    await MainThread.Wait();
    if ( freshPreview is not null && freshPreview.IsValid && onFreshPreviewFrame is not null ) freshPreview.OnPreFrame -= onFreshPreviewFrame;
    if ( preview is not null && preview.IsValid && onPreviewFrame is not null ) preview.OnPreFrame -= onPreviewFrame;
    if ( commandFreshCamera.IsValid() && freshCameraClearCommands is not null ) commandFreshCamera.RemoveCommandList( freshCameraClearCommands );
    if ( commandCamera.IsValid() && cameraClearCommands is not null ) commandCamera.RemoveCommandList( cameraClearCommands );
    if ( freshPreview is not null && freshPreview.IsValid ) { freshPreview.Close(); freshPreview.Destroy(); }
    freshScene?.Destroy();
    freshTarget?.Dispose();
    if ( preview is not null && preview.IsValid ) { preview.Close(); preview.Destroy(); }
    if ( modelObject.IsValid() ) modelObject.Delete();
    scene?.Destroy();
    target?.Dispose();
    var destroyModel = typeof( Model ).GetMethod( "Destroy", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic );
    foreach ( var model in ownedModels ) if ( model is not null && model.IsValid ) destroyModel.Invoke( model, null );
    for ( var i = leases.Count - 1; i >= 0; i-- ) leases[i].Dispose();
}
