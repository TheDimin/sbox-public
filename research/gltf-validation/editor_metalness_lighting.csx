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
var controlsOnly = Environment.GetEnvironmentVariable( "SBOX_GLTF_LIGHTING_CONTROLS_ONLY" ) == "1";
var managedCapture = Environment.GetEnvironmentVariable( "SBOX_GLTF_CAPTURE_RENDERER" ) == "managed";
var output = System.IO.Path.Combine( root.FullName, "Temp", "gltf-validation", (controlsOnly ? (diagnosticClear ? "editor-lighting-controls-camera-clear-tangents" : "editor-lighting-controls-tangents") : (diagnosticClear ? "editor-metalness-lighting-camera-clear" : "editor-metalness-lighting")) + (managedCapture ? "-managed" : "") );
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
ScenePointLight pointLight = null;
CameraComponent camera = null;
Sandbox.Rendering.CommandList clearCommand = null;
Texture target = null;
Model model = null;
Model referenceModel = null;
var tangentModels = new System.Collections.Generic.List<Model>();
IDisposable redirect = null;
object redirectObject = null;
string sourceHash = null;
object managedRenderer = null;
System.Reflection.MethodInfo managedRenderToTexture = null;
bool managedViewportEnabled = false;
string Hash( byte[] bytes ) => Convert.ToHexStringLower( System.Security.Cryptography.SHA256.HashData( bytes ) );
object Receipt( bool passed, string error = null ) => new
{
    passed, phase, error, completed, failures, captures, channel_results = channelResults, texture_evidence = textureEvidence,
    scope = "exploratory stock complex lit metalness sensitivity, not glTF material fidelity or PBR equivalence",
    diagnostic_camera_clear = diagnosticClear, lighting_controls_only = controlsOnly, capture_renderer = managedCapture ? "managed SceneBridge.RenderToTexture" : "native CameraComponent.RenderToTexture", managed_viewport_enabled = managedViewportEnabled, editor = Application.IsEditor, engine_location = engineLocation,
    engine_mvid = engine.ManifestModule.ModuleVersionId.ToString(), gltf_mvid = typeof( Sandbox.Gltf.GltfReader ).Assembly.ManifestModule.ModuleVersionId.ToString(),
    source_sha256 = sourceHash, starting_frame = startFrame, final_frame = Application.FrameCount, output
};
void Save( object result ) => System.IO.File.WriteAllText( receiptPath, System.Text.Json.JsonSerializer.Serialize( result, new System.Text.Json.JsonSerializerOptions { WriteIndented = true } ) );
try
{
    if ( !Application.IsEditor ) throw new InvalidOperationException( "Run only in an existing s&box editor." );
    var managedRenderingType = engine.GetType( "Sandbox.Rendering.ManagedSceneRendering", true );
    managedViewportEnabled = (bool)managedRenderingType.GetProperty( "Enabled" ).GetValue( null );
    if ( managedCapture )
    {
        managedRenderer = managedRenderingType.GetProperty( "Renderer" ).GetValue( null ) ?? throw new InvalidOperationException( "Managed renderer unavailable." );
        managedRenderToTexture = managedRenderer.GetType().GetMethod( "RenderToTexture", new[] { typeof( CameraComponent ), typeof( Texture ) } ) ?? throw new InvalidOperationException( "Managed texture rendering API unavailable." );
    }
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

    var pixelValues = new[] { 32, 224 };
    if ( !controlsOnly )
    {
    phase = "compile-texture-fixtures";
    var compiler = engine.GetType( "Sandbox.GltfTextureCompiler", true ).GetMethod( "Compile", flags );
    var redirectType = engine.GetType( "Sandbox.Internal.RedirectFileSystem", true );
    redirectObject = redirectType.GetMethod( "Create", flags ).Invoke( null, new object[] { output } );
    redirect = (IDisposable)redirectObject;
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
    }

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
            var didSubmit = managedCapture ? (bool)managedRenderToTexture.Invoke( managedRenderer, new object[] { camera, target } ) : camera.RenderToTexture( target );
            if ( !didSubmit ) throw new InvalidOperationException( "Camera submission failed: " + name );
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

    phase = "lighting-control";
    // Mode 2 reports direct plus indirect diffuse lighting. Verify a real light
    // changes it before using either lit or specular output as a metalness probe.
    var unlitControl = await Capture( "no_point_light_diffuse", (SceneCameraDebugMode)2 );
    pointLight = new ScenePointLight( scene.SceneWorld, new Vector3( 80, 19.68504f, 19.68504f ), 500, Color.White * 4 );
    pointLight.ShadowsEnabled = false;
    pointLight.RenderDiffuse = true;
    pointLight.RenderSpecular = true;
    pointLight.SetBakedLightIndex( -1 );
    if ( !pointLight.RenderDiffuse || !pointLight.RenderSpecular || pointLight.ShadowsEnabled || pointLight.Radius != 500 )
        throw new InvalidOperationException( "Point-light state did not retain explicitly requested diffuse/specular, shadows or radius." );
    var litControl = await Capture( "point_light_diffuse", (SceneCameraDebugMode)2 );
    var lightingChanged = Changed( unlitControl, litControl );
    channelResults.Add( new { kind = "lighting_control", mode = 2, no_point_light = Summary( unlitControl ), point_light = Summary( litControl ), changed_pixels = lightingChanged } );
    if ( lightingChanged <= 50 ) failures.Add( "Point-light positive control did not change enough visible diffuse-lighting pixels." );
    completed.Add( "lighting-control" );

    if ( controlsOnly )
    {
        phase = "native-reference-model";
        const float unit = 1f / 0.0254f;
        var nativeMesh = new Mesh( visible );
        nativeMesh.CreateVertexBuffer( 3, new Vertex[] {
            new() { Position = new Vector3( 0, 0, 0 ), Normal = Vector3.Forward, Color = Color32.White },
            new() { Position = new Vector3( 0, unit, 0 ), Normal = Vector3.Forward, Color = Color32.White },
            new() { Position = new Vector3( 0, 0, unit ), Normal = Vector3.Forward, Color = Color32.White } } );
        nativeMesh.CreateIndexBuffer( 3, new[] { 0, 1, 2 } );
        var bounds = new BBox( Vector3.Zero, new Vector3( 0, unit, unit ) );
        nativeMesh.Bounds = bounds;
        referenceModel = Model.Builder.WithName( "gltf_light_reference_" + suffix ).AddMesh( nativeMesh ).WithViewBounds( bounds ).Create();
        if ( referenceModel is null || !referenceModel.IsValid || referenceModel.IsError ) throw new InvalidOperationException( "Native reference model could not be built." );
        Model NativeTangentReference( string name, Vector4 tangent )
        {
            var mesh = new Mesh( visible );
            mesh.CreateVertexBuffer( 3, new Vertex[] {
                new() { Position = new Vector3( 0, 0, 0 ), Normal = Vector3.Forward, Tangent = tangent, Color = Color32.White },
                new() { Position = new Vector3( 0, unit, 0 ), Normal = Vector3.Forward, Tangent = tangent, Color = Color32.White },
                new() { Position = new Vector3( 0, 0, unit ), Normal = Vector3.Forward, Tangent = tangent, Color = Color32.White } } );
            mesh.CreateIndexBuffer( 3, new[] { 0, 1, 2 } );
            mesh.Bounds = bounds;
            var result = Model.Builder.WithName( name + suffix ).AddMesh( mesh ).WithViewBounds( bounds ).Create();
            if ( result is null || !result.IsValid || result.IsError ) throw new InvalidOperationException( "Tangent reference model failed: " + name );
            tangentModels.Add( result );
            return result;
        }
        var nativeTangentY = NativeTangentReference( "gltf_light_native_tangent_y_", new Vector4( 0, 1, 0, 1 ) );
        var nativeTangentZ = NativeTangentReference( "gltf_light_native_tangent_z_", new Vector4( 0, 0, 1, 1 ) );
        // Preserve the actual source's decoded positions/indices, then supply an
        // explicit, orthogonal basis to the same glTF primitive builder.
        var basisData = new Sandbox.Gltf.GltfDrawData {
            Mode = data.Mode, Material = data.Material, HasSkinning = data.HasSkinning, HasMorphTargets = data.HasMorphTargets,
            Positions = data.Positions, Indices = data.Indices, Colors = data.Colors, TexCoords = data.TexCoords,
            Normals = Enumerable.Repeat( System.Numerics.Vector3.UnitX, data.Positions.Length ).ToArray(),
            Tangents = Enumerable.Repeat( new System.Numerics.Vector4( 0, 1, 0, 1 ), data.Positions.Length ).ToArray() };
        var gltfTangentY = (Model)build.Invoke( null, new object[] { basisData, visible, "gltf_light_source_tangent_y_" + suffix } );
        if ( gltfTangentY is null || !gltfTangentY.IsValid || gltfTangentY.IsError ) throw new InvalidOperationException( "glTF source with explicit tangent failed to build." );
        tangentModels.Add( gltfTangentY );
        var sphere = Model.Sphere;
        if ( sphere is null || !sphere.IsValid || sphere.IsError ) throw new InvalidOperationException( "Stock vmdl sphere is unavailable." );
        var size = sphere.Bounds.Size;
        var maximum = MathF.Max( size.x, MathF.Max( size.y, size.z ) );
        if ( !float.IsFinite( maximum ) || maximum <= 0 ) throw new InvalidOperationException( "Stock sphere bounds are invalid." );
        var sphereScale = 30f / maximum;
        var sphereTransform = Transform.Zero.WithScale( sphereScale ).WithPosition( new Vector3( 0, 19.68504f, 19.68504f ) - sphere.Bounds.Center * sphereScale );
        var lightField = typeof( SceneLight ).GetField( "lightNative", flags ) ?? throw new InvalidOperationException( "Native point-light inspection unavailable." );
        var nativeLight = lightField.GetValue( pointLight );
        var lightFlags = (uint)nativeLight.GetType().GetMethod( "GetLightFlags", flags ).Invoke( nativeLight, null );
        if ( (lightFlags & 32u) != 0 ) throw new InvalidOperationException( "Point-light native flags still mark it baked." );
        channelResults.Add( new { kind = "point_light_state", position = pointLight.Position.ToString(), radius = pointLight.Radius, color = pointLight.LightColor.ToString(), pointLight.RenderDiffuse, pointLight.RenderSpecular, pointLight.ShadowsEnabled, requested_baked_index = -1, native_light_flags = lightFlags, native_bake_index_readback = "not exposed" } );
        var observedLighting = false;
        foreach ( var control in new[] {
            (Name: "gltf_triangle", Model: model, Transform: Transform.Zero),
            (Name: "native_triangle_explicit_plus_x", Model: referenceModel, Transform: Transform.Zero),
            (Name: "native_triangle_normal_x_tangent_y_w1", Model: nativeTangentY, Transform: Transform.Zero),
            (Name: "native_triangle_normal_x_tangent_z_w1", Model: nativeTangentZ, Transform: Transform.Zero),
            (Name: "gltf_source_builder_normal_x_tangent_y_w1", Model: gltfTangentY, Transform: Transform.Zero),
            (Name: "stock_vmdl_sphere", Model: sphere, Transform: sphereTransform) } )
        {
            sceneModel.Model = control.Model;
            sceneModel.Transform = control.Transform;
            sceneModel.SetMaterialOverride( visible );
            pointLight.RenderingEnabled = false;
            var albedo = await Capture( control.Name + "_albedo", SceneCameraDebugMode.Albedo );
            var controlMask = Enumerable.Range( 0, background.Length ).Where( i => Different( albedo[i], background[i] ) ).ToArray();
            if ( controlMask.Length <= 50 ) { failures.Add( control.Name + ": insufficient visible geometry" ); continue; }
            object Stats( Color[] pixels ) => new { r = controlMask.Average( i => pixels[i].r ), g = controlMask.Average( i => pixels[i].g ), b = controlMask.Average( i => pixels[i].b ) };
            int ControlChanged( Color[] a, Color[] b ) => controlMask.Count( i => Different( a[i], b[i] ) );
            var normal = await Capture( control.Name + "_world_normal", SceneCameraDebugMode.NormalMap );
            var noLightDiffuse = await Capture( control.Name + "_no_light_diffuse", (SceneCameraDebugMode)2 );
            var noLightSpecular = await Capture( control.Name + "_no_light_specular", SceneCameraDebugMode.Reflect );
            foreach ( var side in new[] { 1, -1 } )
            {
                pointLight.Position = new Vector3( side * 80, 19.68504f, 19.68504f );
                pointLight.RenderingEnabled = true;
                var diffuse = await Capture( control.Name + "_light_" + side + "_diffuse", (SceneCameraDebugMode)2 );
                var specular = await Capture( control.Name + "_light_" + side + "_specular", SceneCameraDebugMode.Reflect );
                var diffuseChanged = ControlChanged( noLightDiffuse, diffuse );
                var specularChanged = ControlChanged( noLightSpecular, specular );
                observedLighting |= Math.Max( diffuseChanged, specularChanged ) > 50;
                channelResults.Add( new { kind = "geometry_lighting_control", geometry = control.Name, visible_pixels = controlMask.Length, world_normal = Stats( normal ), no_light_diffuse = Stats( noLightDiffuse ), no_light_specular = Stats( noLightSpecular ), light_side = side, light_position = pointLight.Position.ToString(), light_diffuse = Stats( diffuse ), light_specular = Stats( specular ), diffuse_changed_pixels = diffuseChanged, specular_changed_pixels = specularChanged } );
            }
        }
        if ( !observedLighting ) failures.Add( "None of the glTF, native triangle or stock vmdl sphere controls showed point lighting." );
        sceneModel.RenderingEnabled = false;
        pointLight.RenderingEnabled = false;
        var finalBackground = await Capture( "background_after_controls", SceneCameraDebugMode.Albedo );
        var finalDifference = Enumerable.Range( 0, background.Length ).Count( i => Different( background[i], finalBackground[i] ) );
        if ( finalDifference > 5 ) failures.Add( $"Disabled background differs by {finalDifference} pixels." );
        completed.Add( "front-back-lighting-controls" );
        phase = "complete";
        var controlsResult = Receipt( failures.Count == 0 && observedLighting );
        Save( controlsResult );
        return controlsResult;
    }

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
    var metalLow = await Scalar( "metal_scalar_0_specular", "g_flMetalness", 0, 0, SceneCameraDebugMode.Reflect );
    var metalHigh = await Scalar( "metal_scalar_1_specular", "g_flMetalness", 1, 0, SceneCameraDebugMode.Reflect );
    var metalLowLit = await Scalar( "metal_scalar_0_lit", "g_flMetalness", 0, 0, SceneCameraDebugMode.Normal );
    var metalHighLit = await Scalar( "metal_scalar_1_lit", "g_flMetalness", 1, 0, SceneCameraDebugMode.Normal );
    var roughScalarChanged = Changed( roughLow, roughHigh );
    var metalScalarChanged = Changed( metalLow, metalHigh );
    var metalScalarLitChanged = Changed( metalLowLit, metalHighLit );
    channelResults.Add( new { kind = "scalar_control", parameter = "g_flRoughnessScaleFactor", mode = 12, low = Summary( roughLow ), high = Summary( roughHigh ), changed_pixels = roughScalarChanged } );
    channelResults.Add( new { kind = "scalar_control", parameter = "g_flMetalness", feature_F_METALNESS_TEXTURE = 0, specular_mode = 3, low = Summary( metalLow ), high = Summary( metalHigh ), changed_pixels = metalScalarChanged, lit_mode = 0, lit_low = Summary( metalLowLit ), lit_high = Summary( metalHighLit ), lit_changed_pixels = metalScalarLitChanged } );
    if ( roughScalarChanged <= 50 ) failures.Add( "Roughness scalar control did not change enough visible pixels." );
    if ( Math.Max( metalScalarChanged, metalScalarLitChanged ) <= 50 ) failures.Add( "Metalness scalar control did not change enough visible lit or specular pixels." );
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
            pair[$"{value}_specular"] = await Capture( label + "_specular", SceneCameraDebugMode.Reflect );
            pair[$"{value}_lit"] = await Capture( label + "_lit", SceneCameraDebugMode.Normal );
        }
        channelResults.Add( new { kind = "channel_pair", slot, channel = "RGBA"[channel].ToString(), low = 32, high = 224,
            specular = new { low = Summary( pair["32_specular"] ), high = Summary( pair["224_specular"] ), changed_pixels = Changed( pair["32_specular"], pair["224_specular"] ) },
            lit = new { low = Summary( pair["32_lit"] ), high = Summary( pair["224_lit"] ), changed_pixels = Changed( pair["32_lit"], pair["224_lit"] ) } } );
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
    if ( pointLight.IsValid() ) pointLight.Delete();
    scene?.Destroy();
    target?.Dispose();
    var destroyModel = typeof( Model ).GetMethod( "Destroy", flags );
    if ( model is not null && model.IsValid ) destroyModel.Invoke( model, null );
    if ( referenceModel is not null && referenceModel.IsValid ) destroyModel.Invoke( referenceModel, null );
    foreach ( var tangentModel in tangentModels ) if ( tangentModel is not null && tangentModel.IsValid ) destroyModel.Invoke( tangentModel, null );
    var destroyMaterial = typeof( Material ).GetMethod( "Destroy", flags );
    foreach ( var material in ownedMaterials ) if ( material is not null && material.IsValid ) destroyMaterial.Invoke( material, null );
    foreach ( var texture in textures.Values ) texture?.Dispose();
    if ( redirectObject is not null ) foreach ( var name in redirectedNames ) redirectObject.GetType().GetMethod( "RemoveAbsFile", flags ).Invoke( redirectObject, new object[] { name } );
    redirect?.Dispose();
    foreach ( var path in generatedPaths ) if ( System.IO.File.Exists( path ) ) System.IO.File.Delete( path );
}
