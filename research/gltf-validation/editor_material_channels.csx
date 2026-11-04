// Exploratory stock-complex material probe for the existing editor's run_csharp tool.
// Only private preview objects, uniquely named temporary resources and Temp output are touched.
var engineLocation = typeof( Model ).Assembly.Location;
var root = new System.IO.DirectoryInfo( System.IO.Path.GetDirectoryName( engineLocation ) );
while ( root is not null && !System.IO.File.Exists( System.IO.Path.Combine( root.FullName, "engine", "Sandbox-Engine.slnx" ) ) ) root = root.Parent;
if ( root is null ) throw new InvalidOperationException( "Cannot find checkout from " + engineLocation );
var corpus = Environment.GetEnvironmentVariable( "SBOX_GLTF_CORPUS" );
if ( string.IsNullOrWhiteSpace( corpus ) ) corpus = System.IO.Path.Combine( root.FullName, "Temp", "gltf-reference", "glTF-Sample-Assets" );
var sourceDirectory = System.IO.Path.Combine( corpus, "Models", "Triangle", "glTF" );
var diagnosticClear = Environment.GetEnvironmentVariable( "SBOX_GLTF_CAMERA_CLEAR" ) == "1";
var output = System.IO.Path.Combine( root.FullName, "Temp", "gltf-validation", diagnosticClear ? "editor-material-channels-camera-clear" : "editor-material-channels" );
System.IO.Directory.CreateDirectory( output );
var receiptPath = System.IO.Path.Combine( output, "receipt.json" );
var suffix = Guid.NewGuid().ToString( "N" );
var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static;
var engine = typeof( Model ).Assembly;
var completed = new System.Collections.Generic.List<string>();
var failures = new System.Collections.Generic.List<string>();
var captures = new System.Collections.Generic.List<object>();
var channelResults = new System.Collections.Generic.List<object>();
var textureEvidence = new System.Collections.Generic.List<object>();
var textures = new System.Collections.Generic.Dictionary<string, Texture>();
var generatedPaths = new System.Collections.Generic.List<string>();
var redirectedNames = new System.Collections.Generic.List<string>();
var ownedMaterials = new System.Collections.Generic.List<Material>();
var phase = "load";
var startFrame = Application.FrameCount;
var frameCount = 0;
Action onPreviewFrame = null;
Scene scene = null;
Editor.SceneRenderingWidget preview = null;
SceneModel sceneModel = null;
CameraComponent camera = null;
Sandbox.Rendering.CommandList clearCommand = null;
Texture target = null;
Model model = null;
IDisposable redirect = null;
object redirectObject = null;
string sourceHash = null;
string Hash( byte[] bytes ) => Convert.ToHexStringLower( System.Security.Cryptography.SHA256.HashData( bytes ) );
object Receipt( bool passed, string error = null ) => new
{
    passed, phase, error, completed, failures, captures, channel_results = channelResults, texture_evidence = textureEvidence,
    scope = "exploratory stock complex debug-output channel/scalar sensitivity, not glTF material fidelity or PBR equivalence",
    diagnostic_camera_clear = diagnosticClear, editor = Application.IsEditor, engine_location = engineLocation,
    engine_mvid = engine.ManifestModule.ModuleVersionId.ToString(), gltf_mvid = typeof( Sandbox.Gltf.GltfReader ).Assembly.ManifestModule.ModuleVersionId.ToString(),
    source_sha256 = sourceHash, starting_frame = startFrame, final_frame = Application.FrameCount, output
};
void Save( object result ) => System.IO.File.WriteAllText( receiptPath, System.Text.Json.JsonSerializer.Serialize( result, new System.Text.Json.JsonSerializerOptions { WriteIndented = true } ) );
try
{
    if ( !Application.IsEditor ) throw new InvalidOperationException( "Run only in an existing s&box editor." );
    var source = System.IO.File.ReadAllBytes( System.IO.Path.Combine( sourceDirectory, "Triangle.gltf" ) );
    sourceHash = Hash( source );
    if ( sourceHash != "6f4a8a1fbefbac2d0adde0800864279fd98c3f119077380e6840810ed62b6cfc" ) throw new InvalidOperationException( "Unexpected Triangle hash." );
    byte[] Resolve( string uri ) => System.IO.File.ReadAllBytes( System.IO.Path.Combine( sourceDirectory, uri ) );
    using var doc = Sandbox.Gltf.GltfReader.Read( source, Resolve );
    var data = doc.CreateDrawData( 0, 0 );
    var stock = Material.Load( "materials/default/white.vmat" );
    if ( stock is null || !stock.IsValid || stock.IsError || !stock.ShaderName.Contains( "complex", StringComparison.OrdinalIgnoreCase ) ) throw new InvalidOperationException( "Stock complex material unavailable." );
    var build = engine.GetType( "Sandbox.GltfModelPrimitiveBuilder", true ).GetMethod( "Build", flags );
    model = (Model)build.Invoke( null, new object[] { data, stock, "gltf_channel_model_" + suffix } );
    if ( model is null || !model.IsValid || model.IsError ) throw new InvalidOperationException( "Triangle model failed to build." );
    completed.Add( "load-triangle-and-stock" );

    phase = "compile-texture-fixtures";
    var compiler = engine.GetType( "Sandbox.GltfTextureCompiler", true ).GetMethod( "Compile", flags );
    var redirectType = engine.GetType( "Sandbox.Internal.RedirectFileSystem", true );
    redirectObject = redirectType.GetMethod( "Create", flags ).Invoke( null, new object[] { output } );
    redirect = (IDisposable)redirectObject;
    var pixelValues = new[] { 32, 224 };
    for ( int channel = 0; channel < 4; channel++ )
    foreach ( var value in pixelValues )
    {
        var bytes = new byte[] { 128, 128, 128, 255 };
        bytes[channel] = (byte)value;
        using var sourceBitmap = new Bitmap( 2, 2 );
        var sample = new Color( bytes[0] / 255f, bytes[1] / 255f, bytes[2] / 255f, bytes[3] / 255f );
        sourceBitmap.SetPixels( Enumerable.Repeat( sample, 4 ).ToArray() );
        var png = sourceBitmap.ToPng();
        var image = new Sandbox.Gltf.GltfImage { Bytes = png, MimeType = "image/png" };
        var compiled = (byte[])compiler.Invoke( null, new object[] { image, false, 1024L * 1024 } );
        var label = $"ch{channel}_{value}";
        var virtualBase = "gltf-channels/" + suffix + "/" + label;
        var virtualName = "/" + virtualBase + ".vtex_c";
        var path = System.IO.Path.Combine( output, "temporary-" + suffix + "-" + label + ".vtex_c" );
        System.IO.File.WriteAllBytes( path, compiled );
        generatedPaths.Add( path );
        redirectType.GetMethod( "AddAbsFile", flags ).Invoke( redirectObject, new object[] { virtualName, path } );
        redirectedNames.Add( virtualName );
        textureEvidence.Add( new { label, rgba = bytes, png_sha256 = Hash( png ), vtex_sha256 = Hash( compiled ) } );
    }
    engine.GetType( "NativeEngine.g_pResourceSystem", true ).GetMethod( "InvalidateDatabase", flags ).Invoke( null, null );
    foreach ( var name in redirectedNames )
    {
        var path = name.Substring( 1, name.Length - 1 - ".vtex_c".Length );
        var texture = Texture.Load( path + ".vtex", false );
        if ( texture is null || !texture.IsValid || texture.IsError || texture.Width != 2 || texture.Height != 2 ) throw new InvalidOperationException( "Compiled texture failed to load: " + path );
        var label = path.Substring( path.LastIndexOf( '/' ) + 1 );
        textures.Add( label, texture );
        var split = label.Split( '_' );
        var channel = int.Parse( split[0].Substring( 2 ) );
        var value = byte.Parse( split[1] );
        var expected = new byte[] { 128, 128, 128, 255 };
        expected[channel] = value;
        var pixels = texture.GetPixels();
        if ( pixels.Length != 4 || pixels.Any( p => p.r != expected[0] || p.g != expected[1] || p.b != expected[2] || p.a != expected[3] ) )
            throw new InvalidOperationException( "Compiled texture readback is not the intended RGBA pattern: " + label );
    }
    completed.Add( "compile-load-and-verify-eight-linear-textures" );

    phase = "create-preview";
    scene = Scene.CreateEditorScene();
    using ( scene.Push() )
    {
        camera = scene.CreateObject().Components.Create<CameraComponent>();
        camera.WorldPosition = new Vector3( 80, 19.68504f, 19.68504f );
        camera.WorldRotation = Rotation.FromYaw( 180 );
        camera.BackgroundColor = Color.Black;
        camera.ZNear = 1;
        camera.FieldOfView = 50;
        camera.EnablePostProcessing = false;
    }
    if ( diagnosticClear )
    {
        clearCommand = new Sandbox.Rendering.CommandList( "glTF channel probe diagnostic clear" );
        clearCommand.Clear( Color.Black, true, false, false );
        camera.AddCommandList( clearCommand, Sandbox.Rendering.Stage.AfterDepthPrepass );
    }
    preview = new Editor.SceneRenderingWidget { Scene = scene, Camera = camera, WindowTitle = "glTF stock material channels", Size = new Vector2( 384, 384 ) };
    onPreviewFrame = () => frameCount++;
    preview.OnPreFrame += onPreviewFrame;
    preview.Show();
    target = Texture.CreateRenderTarget().WithSize( 96, 96 ).WithFormat( ImageFormat.RGBA8888 ).Create( "gltf_channel_capture_" + suffix );
    async Task WaitFrames()
    {
        var first = frameCount;
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while ( frameCount < first + 3 )
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ( timer.Elapsed > TimeSpan.FromSeconds( 5 ) ) throw new TimeoutException( $"Preview callbacks stalled: {first}->{frameCount}, visible={preview.Visible}, size={preview.Size}" );
            await Task.Delay( 50, cancellationToken );
            await MainThread.Wait();
        }
    }
    async Task<Color[]> Capture( string name, SceneCameraDebugMode mode )
    {
        phase = name;
        camera.DebugMode = mode;
        await WaitFrames();
        var submitted = frameCount;
        using ( scene.Push() )
        {
            target.Clear( Color.Black );
            if ( !camera.RenderToTexture( target ) ) throw new InvalidOperationException( "Camera submission failed: " + name );
        }
        await WaitFrames();
        cancellationToken.ThrowIfCancellationRequested();
        using var bitmap = target.GetBitmap();
        if ( bitmap is null ) throw new InvalidOperationException( "Readback failed: " + name );
        var png = bitmap.ToPng();
        var path = System.IO.Path.Combine( output, name + ".png" );
        System.IO.File.WriteAllBytes( path, png );
        captures.Add( new { name, mode = (int)mode, path, sha256 = Hash( png ), frame = Application.FrameCount, preview_frames_at_submit = submitted, preview_frames_at_readback = frameCount, preview_visible = preview.Visible, preview_size = preview.Size.ToString() } );
        return bitmap.GetPixels();
    }
    bool Different( Color a, Color b ) => MathF.Abs( a.r - b.r ) > 0.03f || MathF.Abs( a.g - b.g ) > 0.03f || MathF.Abs( a.b - b.b ) > 0.03f;
    Material MakeMaterial( string name )
    {
        var copy = stock.CreateCopy();
        if ( copy is null || !copy.IsValid || copy.IsError ) throw new InvalidOperationException( "Could not create material " + name );
        ownedMaterials.Add( copy );
        copy.SetFeature( "F_RENDER_BACKFACES", 1 );
        copy.SetFeature( "F_ALPHA_TEST", 0 );
        copy.SetFeature( "F_TRANSLUCENT", 0 );
        copy.Set( "g_vColorTint", new Vector4( .2f, .7f, .3f, 1 ) );
        return copy;
    }
    var background = await Capture( "background", SceneCameraDebugMode.Albedo );
    if ( background.Any( p => Different( p, Color.Black ) ) ) failures.Add( "Initial background is not black; no channel result is trustworthy." );
    sceneModel = new SceneModel( scene.SceneWorld, model, Transform.Zero );
    var visible = MakeMaterial( "visibility" );
    sceneModel.SetMaterialOverride( visible );
    var visiblePixels = await Capture( "visibility", SceneCameraDebugMode.Albedo );
    var mask = Enumerable.Range( 0, background.Length ).Where( i => Different( visiblePixels[i], background[i] ) ).ToArray();
    if ( mask.Length <= 50 ) throw new InvalidOperationException( "The stock native geometry control has insufficient coverage: " + mask.Length );
    object Summary( Color[] pixels ) => new { mean_r = mask.Average( i => pixels[i].r ), mean_g = mask.Average( i => pixels[i].g ), mean_b = mask.Average( i => pixels[i].b ), center = pixels[48 * 96 + 48].ToString() };
    int Changed( Color[] a, Color[] b ) => mask.Count( i => Different( a[i], b[i] ) );
    completed.Add( "visible-control" );

    phase = "scalar-controls";
    async Task<Color[]> Scalar( string label, string parameter, float value, int metalTexture, SceneCameraDebugMode mode )
    {
        var material = MakeMaterial( label );
        material.SetFeature( "F_METALNESS_TEXTURE", metalTexture );
        material.Set( parameter, value );
        sceneModel.SetMaterialOverride( material );
        return await Capture( label, mode );
    }
    var roughLow = await Scalar( "roughness_scalar_0", "g_flRoughnessScaleFactor", 0, 0, SceneCameraDebugMode.Roughness );
    var roughHigh = await Scalar( "roughness_scalar_1", "g_flRoughnessScaleFactor", 1, 0, SceneCameraDebugMode.Roughness );
    var metalLow = await Scalar( "metal_scalar_0", "g_flMetalness", 0, 0, (SceneCameraDebugMode)13 );
    var metalHigh = await Scalar( "metal_scalar_1", "g_flMetalness", 1, 0, (SceneCameraDebugMode)13 );
    var roughScalarChanged = Changed( roughLow, roughHigh );
    var metalScalarChanged = Changed( metalLow, metalHigh );
    channelResults.Add( new { kind = "scalar_control", parameter = "g_flRoughnessScaleFactor", mode = 12, low = Summary( roughLow ), high = Summary( roughHigh ), changed_pixels = roughScalarChanged } );
    channelResults.Add( new { kind = "scalar_control", parameter = "g_flMetalness", feature_F_METALNESS_TEXTURE = 0, mode = 13, low = Summary( metalLow ), high = Summary( metalHigh ), changed_pixels = metalScalarChanged } );
    if ( roughScalarChanged <= 50 ) failures.Add( "Roughness scalar control did not change enough visible pixels." );
    if ( metalScalarChanged <= 50 ) failures.Add( "Metalness scalar control did not change enough visible pixels." );
    completed.Add( "scalar-controls" );

    foreach ( var slot in new[] { "g_tColor", "g_tNormal", "g_tMetalness" } )
    for ( var channel = 0; channel < 4; channel++ )
    {
        var pair = new System.Collections.Generic.Dictionary<string, Color[]>();
        foreach ( var value in pixelValues )
        {
            var label = slot + "_ch" + channel + "_" + value;
            var material = MakeMaterial( label );
            material.SetFeature( "F_METALNESS_TEXTURE", 1 );
            material.Set( "g_flMetalness", 1f );
            material.Set( "g_flRoughnessScaleFactor", 1f );
            if ( !material.Set( slot, textures[$"ch{channel}_{value}"] ) ) throw new InvalidOperationException( "Failed texture binding: " + label );
            sceneModel.SetMaterialOverride( material );
            pair[$"{value}_rough"] = await Capture( label + "_roughness", SceneCameraDebugMode.Roughness );
            // ToolsVisMode 11 is Reflectivity; SceneCameraDebugMode.Reflect is 3 (a different lighting visualization).
            pair[$"{value}_reflectivity"] = await Capture( label + "_reflectivity", (SceneCameraDebugMode)11 );
            pair[$"{value}_reflectance"] = await Capture( label + "_reflectance", (SceneCameraDebugMode)13 );
        }
        channelResults.Add( new { kind = "channel_pair", slot, channel = "RGBA"[channel].ToString(), low = 32, high = 224,
            roughness = new { low = Summary( pair["32_rough"] ), high = Summary( pair["224_rough"] ), changed_pixels = Changed( pair["32_rough"], pair["224_rough"] ) },
            reflectivity = new { low = Summary( pair["32_reflectivity"] ), high = Summary( pair["224_reflectivity"] ), changed_pixels = Changed( pair["32_reflectivity"], pair["224_reflectivity"] ) },
            reflectance = new { low = Summary( pair["32_reflectance"] ), high = Summary( pair["224_reflectance"] ), changed_pixels = Changed( pair["32_reflectance"], pair["224_reflectance"] ) } } );
    }
    completed.Add( "channel-pairs" );
    sceneModel.RenderingEnabled = false;
    var after = await Capture( "background_after", SceneCameraDebugMode.Albedo );
    var afterDifferent = Enumerable.Range( 0, background.Length ).Count( i => Different( after[i], background[i] ) );
    if ( afterDifferent > 5 ) failures.Add( $"Disabled background differs from original by {afterDifferent} pixels." );
    completed.Add( "disabled-control" );
    phase = "complete";
    var result = Receipt( failures.Count == 0 );
    Save( result );
    return result;
}
catch ( OperationCanceledException ) { throw; }
catch ( Exception error )
{
    var result = Receipt( false, error.ToString() );
    Save( result );
    return result;
}
finally
{
    await MainThread.Wait();
    if ( camera.IsValid() && clearCommand is not null ) camera.RemoveCommandList( clearCommand );
    if ( preview is not null && preview.IsValid && onPreviewFrame is not null ) preview.OnPreFrame -= onPreviewFrame;
    if ( preview is not null && preview.IsValid ) { preview.Close(); preview.Destroy(); }
    if ( sceneModel.IsValid() ) sceneModel.Delete();
    scene?.Destroy();
    target?.Dispose();
    var destroyModel = typeof( Model ).GetMethod( "Destroy", flags );
    if ( model is not null && model.IsValid ) destroyModel.Invoke( model, null );
    var destroyMaterial = typeof( Material ).GetMethod( "Destroy", flags );
    foreach ( var material in ownedMaterials ) if ( material is not null && material.IsValid ) destroyMaterial.Invoke( material, null );
    foreach ( var texture in textures.Values ) texture?.Dispose();
    if ( redirectObject is not null ) foreach ( var name in redirectedNames ) redirectObject.GetType().GetMethod( "RemoveAbsFile", flags ).Invoke( redirectObject, new object[] { name } );
    redirect?.Dispose();
    foreach ( var path in generatedPaths ) if ( System.IO.File.Exists( path ) ) System.IO.File.Delete( path );
}
