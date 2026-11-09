// Existing editor only. These paired diagnostic shaders differ solely in the
// sampler Attribute annotation. No stock shader or user scene is modified.
var engine = typeof( Model ).Assembly;
var checkout = new System.IO.DirectoryInfo( System.IO.Path.GetDirectoryName( engine.Location ) );
while ( checkout is not null && !System.IO.File.Exists( System.IO.Path.Combine( checkout.FullName, "engine", "Sandbox-Engine.slnx" ) ) ) checkout = checkout.Parent;
if ( checkout is null ) throw new InvalidOperationException( "Cannot locate checkout from loaded engine." );
var runId = Guid.NewGuid().ToString( "N" );
var output = System.IO.Path.Combine( checkout.FullName, "Temp", "gltf-validation", "editor-sampler-binding", runId );
System.IO.Directory.CreateDirectory( output );
var requestPath = System.IO.Path.Combine( exchangeDirectory, "gltf-sampler-binding-request.json" );
var sourceDir = System.IO.Path.Combine( checkout.FullName, "game", "core", "shaders" );
var fixedPath = System.IO.Path.Combine( sourceDir, "gltf_sampler_fixed_diagnostic.shader" );
var attributePath = System.IO.Path.Combine( sourceDir, "gltf_sampler_attribute_diagnostic.shader" );
var runnerPath = System.IO.Path.Combine( checkout.FullName, "engine", "Tools", "Gltf", "editor", "editor_sampler_binding.csx" );
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
var results = new System.Collections.Generic.List<object>(); var routeControls = new System.Collections.Generic.List<object>(); var matrix = new System.Collections.Generic.List<object>(); var cleanupErrors = new System.Collections.Generic.List<string>();
var observations = new System.Collections.Generic.Dictionary<string, (float[] Color, int Coverage, int NonfinitePixels)>( StringComparer.Ordinal );
var materials = new System.Collections.Generic.List<Material>(); var models = new System.Collections.Generic.List<Model>(); var meshes = new System.Collections.Generic.List<Mesh>();
string phase = "initialize", error = null, requestHash = null, rendererMvid = null, rendererHash = null, fixedHash = null, attributeHash = null, runnerHash = null;
var nativeClear = false; var frames = 0; var startingFrame = Application.FrameCount;
Scene scene = null; Editor.SceneRenderingWidget preview = null; CameraComponent camera = null; ScenePointLight light = null; SceneModel active = null;
Texture fixtureTexture = null; Sandbox.Rendering.CommandList clearCommands = null; Action onFrame = null;
object renderer = null; System.Reflection.MethodInfo managedCapture = null;
string Hash( byte[] data ) => Convert.ToHexStringLower( System.Security.Cryptography.SHA256.HashData( data ) );
string HashFile( string path ) => Hash( System.IO.File.ReadAllBytes( path ) );
void Require( bool value, string message ) { if ( !value ) throw new InvalidOperationException( message ); }
void Clean( string name, Action action ) { try { action(); } catch ( Exception ex ) { cleanupErrors.Add( name + ": " + ex ); } }
string DiskMvid( string path ) { using var s = System.IO.File.OpenRead( path ); using var p = new System.Reflection.PortableExecutable.PEReader( s ); var m = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader( p ); return m.GetGuid( m.GetModuleDefinition().Mvid ).ToString(); }
bool Nonblack( float[] pixels, int offset ) => MathF.Abs( pixels[offset] ) > .03f || MathF.Abs( pixels[offset + 1] ) > .03f || MathF.Abs( pixels[offset + 2] ) > .03f;
async Task SafePoint()
{
    await Task.Delay( 1, cancellationToken ); await MainThread.Wait();
    Require( Application.IsEditor && !Game.IsPlaying, "Sampling diagnostic deferred while user is playing." );
    var timer = System.Diagnostics.Stopwatch.StartNew();
    while ( Graphics.IsActive ) { Require( timer.Elapsed < TimeSpan.FromSeconds( 5 ), "No safe render boundary." ); await Task.Delay( 20, cancellationToken ); await MainThread.Wait(); Require( !Game.IsPlaying, "User entered play mode." ); }
}
async Task WaitFrames()
{
    var before = frames; var timer = System.Diagnostics.Stopwatch.StartNew();
    while ( frames < before + 3 ) { Require( timer.Elapsed < TimeSpan.FromSeconds( 8 ), "Private preview did not advance." ); await Task.Delay( 30, cancellationToken ); await SafePoint(); }
}
async Task<(float[] Center, int Coverage, int NonfinitePixels)> Capture( string route )
{
    await WaitFrames();
    using var target = Texture.CreateRenderTarget().WithSize( 64, 64 ).WithFormat( ImageFormat.RGBA32323232F ).WithInitialColor( Color.Magenta ).Create( "sampler_binding_" + Guid.NewGuid().ToString( "N" ) );
    using ( scene.Push() )
    {
        target.Clear( Color.Magenta );
        var submitted = route == "managed" ? (bool)managedCapture.Invoke( renderer, new object[] { camera, target } ) : camera.RenderToTexture( target );
        Require( submitted, "Camera submission failed: " + route );
    }
    await WaitFrames(); await SafePoint();
    var pixels = new float[64 * 64 * 4]; target.GetPixels( (0, 0, 64, 64), 0, 0, pixels.AsSpan(), ImageFormat.RGBA32323232F );
    var coverage = 0; var nonfinite = 0;
    for ( var i = 0; i < pixels.Length; i += 4 )
    {
        if ( !float.IsFinite( pixels[i] ) || !float.IsFinite( pixels[i + 1] ) || !float.IsFinite( pixels[i + 2] ) || !float.IsFinite( pixels[i + 3] ) ) nonfinite++;
        if ( Nonblack( pixels, i ) ) coverage++;
    }
    var center = new float[4];
    for ( var y = 28; y < 36; ++y ) for ( var x = 28; x < 36; ++x ) for ( var c = 0; c < 4; ++c ) center[c] += pixels[(y * 64 + x) * 4 + c] / 64f;
    return (center, coverage, nonfinite);
}
try
{
    await SafePoint();
    var fixedBytes = System.IO.File.ReadAllBytes( fixedPath ); var attributeBytes = System.IO.File.ReadAllBytes( attributePath );
    fixedHash = Hash( fixedBytes ); attributeHash = Hash( attributeBytes ); runnerHash = HashFile( runnerPath );
    var original = System.Text.Encoding.UTF8.GetString( fixedBytes ); var annotated = System.Text.Encoding.UTF8.GetString( attributeBytes );
    Require( original.Replace( "MaxAniso( 8 ); >;", "MaxAniso( 8 ); Attribute( \"ProbeSampler\" ); >;", StringComparison.Ordinal ) == annotated, "Diagnostic shader sources differ by more than the sampler annotation." );
    Require( DiskMvid( engine.Location ) == engine.ManifestModule.ModuleVersionId.ToString(), "Loaded engine differs from disk." );
    var rendererAssembly = AppDomain.CurrentDomain.GetAssemblies().Single( a => !a.IsDynamic && a.GetName().Name == "Sandbox.SceneRenderer" && System.Runtime.Loader.AssemblyLoadContext.GetLoadContext( a ) == System.Runtime.Loader.AssemblyLoadContext.Default );
    rendererMvid = rendererAssembly.ManifestModule.ModuleVersionId.ToString(); rendererHash = HashFile( rendererAssembly.Location );
    Require( rendererMvid == DiskMvid( rendererAssembly.Location ), "Loaded renderer differs from disk." );
    var managedType = engine.GetType( "Sandbox.Rendering.ManagedSceneRendering", true ); renderer = managedType.GetProperty( "Renderer", flags ).GetValue( null );
    Require( renderer is not null && renderer.GetType().Assembly == rendererAssembly, "Managed renderer is unavailable." );
    managedCapture = renderer.GetType().GetMethod( "RenderToTexture", flags, null, new[] { typeof( CameraComponent ), typeof( Texture ) }, null );
    Require( managedCapture is not null, "Managed capture API is unavailable." );
    if ( System.IO.File.Exists( requestPath ) )
    {
        var bytes = System.IO.File.ReadAllBytes( requestPath ); Require( bytes.Length is > 0 and <= 4096, "Invalid request length." ); requestHash = Hash( bytes );
        using var request = System.Text.Json.JsonDocument.Parse( bytes ); var root = request.RootElement;
        Require( root.ValueKind == System.Text.Json.JsonValueKind.Object, "Invalid request object." );
        var fields = new System.Collections.Generic.HashSet<string>( StringComparer.Ordinal ); foreach ( var field in root.EnumerateObject() ) Require( fields.Add( field.Name ), "Duplicate request field." );
        Require( fields.SetEquals( new[] { "version", "native_color_clear" } ) && root.GetProperty( "version" ).GetInt32() == 1, "Expected version 1 sampler diagnostic request." );
        nativeClear = root.GetProperty( "native_color_clear" ).GetBoolean();
    }
    phase = "create-private-fixtures";
    // Temporary isolated 2x1 diagnostic texture: red left, blue right. This does
    // not alter the shipping glTF texture/material policy.
    fixtureTexture = Texture.Create( 2, 1, ImageFormat.RGBA8888 ).WithMips( 1 ).WithData( new byte[] { 255, 0, 0, 255, 0, 0, 255, 255 } ).WithStaticUsage().Finish();
    Require( fixtureTexture is { IsValid: true } && !fixtureTexture.IsError, "Diagnostic texture failed." );
    var shaderNames = new[] { "shaders/gltf_sampler_fixed_diagnostic.shader", "shaders/gltf_sampler_attribute_diagnostic.shader" };
    var baseMaterials = new System.Collections.Generic.Dictionary<string, Material>();
    var baseModels = new System.Collections.Generic.Dictionary<string, Model>();
    foreach ( var shader in shaderNames )
    {
        var material = Material.Create( "sampler_base_" + Guid.NewGuid().ToString( "N" ), shader ); materials.Add( material );
        Require( material is { IsValid: true } && !material.IsError && material.ShaderName.Contains( System.IO.Path.GetFileNameWithoutExtension( shader ), StringComparison.OrdinalIgnoreCase ), "Diagnostic shader missing or invalid: " + shader );
        baseMaterials.Add( shader, material );
        var mesh = new Mesh( material ); meshes.Add( mesh );
        mesh.CreateVertexBuffer( 3, new Vertex[] { new() { Position = new Vector3( 0, -40, -40 ), Color = Color32.White }, new() { Position = new Vector3( 0, 120, -40 ), Color = Color32.White }, new() { Position = new Vector3( 0, -40, 120 ), Color = Color32.White } } );
        mesh.CreateIndexBuffer( 3, new[] { 0, 1, 2 } ); var bounds = new BBox( new Vector3( -1, -40, -40 ), new Vector3( 1, 120, 120 ) ); mesh.Bounds = bounds;
        var model = Model.Builder.WithName( "sampler_probe_" + Guid.NewGuid().ToString( "N" ) ).AddMesh( mesh ).WithViewBounds( bounds ).Create(); models.Add( model );
        Require( model is { IsValid: true } && !model.IsError, "Diagnostic native model invalid." ); baseModels.Add( shader, model );
    }
    scene = Scene.CreateEditorScene();
    using ( scene.Push() )
    {
        camera = scene.CreateObject().Components.Create<CameraComponent>(); camera.WorldPosition = new Vector3( 100, 0, 0 ); camera.WorldRotation = Rotation.LookAt( new Vector3( -1, 0, 0 ) );
        camera.Orthographic = true; camera.OrthographicHeight = 160; camera.ZNear = .1f; camera.ZFar = 1000; camera.BackgroundColor = Color.Black;
        camera.EnablePostProcessing = false; camera.DebugMode = SceneCameraDebugMode.Normal;
    }
    preview = new Editor.SceneRenderingWidget { Scene = scene, Camera = camera, WindowTitle = "Sampler binding diagnostic", Size = new Vector2( 384, 384 ) };
    onFrame = () => frames++; preview.OnPreFrame += onFrame; preview.Show();
    light = new ScenePointLight( scene.SceneWorld, new Vector3( 100, 0, 0 ), 500, Color.White * 4 );
    light.ShadowsEnabled = false; light.RenderDiffuse = true; light.RenderSpecular = true; light.SetBakedLightIndex( -1 );
    var pointWrap = new Sandbox.Rendering.SamplerState { Filter = Sandbox.Rendering.FilterMode.Point, AddressModeU = Sandbox.Rendering.TextureAddressMode.Wrap, AddressModeV = Sandbox.Rendering.TextureAddressMode.Clamp, MaxAnisotropy = 1 };
    var pointClamp = pointWrap with { AddressModeU = Sandbox.Rendering.TextureAddressMode.Clamp };
    var linearWrap = pointWrap with { Filter = Sandbox.Rendering.FilterMode.Bilinear };
    var definitions = new[] { (Name: "clamp_edge", U: 1.25f, Sampler: pointClamp, Expected: new[] { 0f, 0f, 1f }),
        (Name: "point_mid", U: .4f, Sampler: pointWrap, Expected: new[] { 1f, 0f, 0f }),
        (Name: "linear_mid", U: .4f, Sampler: linearWrap, Expected: new[] { .7f, 0f, .3f }) };
    foreach ( var route in new[] { "native", "managed" } )
    {
        await SafePoint(); phase = "route-" + route;
        if ( route == "native" && nativeClear )
        {
            clearCommands = new Sandbox.Rendering.CommandList( "Sampler diagnostic native color clear" ); clearCommands.Clear( Color.Black, true, false, false );
            camera.AddCommandList( clearCommands, Sandbox.Rendering.Stage.AfterDepthPrepass );
        }
        var emptyBefore = await Capture( route );
        var sphere = Model.Sphere; Require( sphere is { IsValid: true } && !sphere.IsError, "Stock sphere control unavailable." );
        active = new SceneModel( scene.SceneWorld, sphere, Transform.Zero ); var sphereImage = await Capture( route ); active.Delete(); active = null;
        var emptyAfterSphere = await Capture( route );
        foreach ( var shader in shaderNames ) foreach ( var binding in new[] { "default", "material", "object", "material_to_object_merge", "conflict_object_wins", "conflict_material_wins" } ) foreach ( var definition in definitions )
        {
            await SafePoint();
            var material = Material.Create( "sampler_case_" + Guid.NewGuid().ToString( "N" ), shader ); materials.Add( material );
            Require( material is { IsValid: true } && !material.IsError, "Diagnostic case material invalid." );
            var opposite = definition.Name == "clamp_edge" ? pointWrap : definition.Name == "point_mid" ? linearWrap : pointWrap;
            if ( binding is "material" or "material_to_object_merge" or "conflict_material_wins" ) material.Attributes.Set( "ProbeSampler", definition.Sampler );
            if ( binding == "conflict_object_wins" ) material.Attributes.Set( "ProbeSampler", opposite );
            active = new SceneModel( scene.SceneWorld, baseModels[shader], Transform.Zero ); active.SetMaterialOverride( material );
            if ( binding == "conflict_material_wins" ) active.Attributes.Set( "ProbeSampler", opposite );
            if ( binding is "material_to_object_merge" or "conflict_object_wins" or "conflict_material_wins" )
            {
                var merge = typeof( RenderAttributes ).GetMethod( "MergeTo", flags, null, new[] { typeof( RenderAttributes ) }, null );
                Require( merge is not null, "Material-to-object attribute merge API unavailable." );
                merge.Invoke( material.Attributes, new object[] { active.Attributes } );
            }
            active.Attributes.Set( "ProbeTexture", fixtureTexture ); active.Attributes.Set( "ProbeUv", new Vector2( definition.U, .5f ) );
            if ( binding is "object" or "conflict_object_wins" ) active.Attributes.Set( "ProbeSampler", definition.Sampler );
            var measured = await Capture( route );
            var shaderLabel = shader == shaderNames[0] ? "fixed" : "annotated";
            observations.Add( route + "|" + shaderLabel + "|" + binding + "|" + definition.Name, (measured.Center, measured.Coverage, measured.NonfinitePixels) );
            var analytic = definition.Expected; var difference = Enumerable.Range( 0, 3 ).Max( c => MathF.Abs( measured.Center[c] - analytic[c] ) );
            results.Add( new { route, shader = shaderLabel, binding, name = definition.Name, u = definition.U,
                sampler = binding == "default" ? "declared shader default" : definition.Sampler.ToString(), conflict_other_sampler = binding.StartsWith( "conflict_", StringComparison.Ordinal ) ? opposite.ToString() : null,
                merge_order = binding == "conflict_object_wins" ? "material then object" : binding == "conflict_material_wins" ? "object then material" : binding == "material_to_object_merge" ? "material copied to object" : null, expected_if_override_applied = analytic,
                observed = measured.Center, observed_coverage = measured.Coverage, observed_nonfinite_pixels = measured.NonfinitePixels,
                analytic_max_error = difference, matches_requested_sampler = measured.NonfinitePixels == 0 && difference <= .06f,
                native_color_clear = route == "native" && nativeClear, frame = Application.FrameCount } );
            active.Delete(); active = null; await WaitFrames();
        }
        var emptyAfter = await Capture( route );
        var sphereFinite = sphereImage.Center.All( float.IsFinite );
        var sphereMagenta = sphereFinite && MathF.Abs( sphereImage.Center[0] - 1 ) < .06f && MathF.Abs( sphereImage.Center[1] ) < .06f && MathF.Abs( sphereImage.Center[2] - 1 ) < .06f;
        routeControls.Add( new { route, native_color_clear = route == "native" && nativeClear, empty_before_coverage = emptyBefore.Coverage, empty_before_center = emptyBefore.Center, empty_before_nonfinite = emptyBefore.NonfinitePixels,
            sphere_coverage = sphereImage.Coverage, sphere_center = sphereImage.Center, sphere_finite = sphereFinite, sphere_nonfinite = sphereImage.NonfinitePixels, sphere_is_initial_magenta = sphereMagenta,
            empty_after_sphere_coverage = emptyAfterSphere.Coverage, empty_after_sphere_center = emptyAfterSphere.Center, empty_after_sphere_nonfinite = emptyAfterSphere.NonfinitePixels,
            empty_after_coverage = emptyAfter.Coverage, empty_after_center = emptyAfter.Center, empty_after_nonfinite = emptyAfter.NonfinitePixels,
            passed = emptyBefore.Coverage == 0 && emptyBefore.NonfinitePixels == 0 && sphereImage.Coverage > 30 && sphereFinite && sphereImage.NonfinitePixels == 0 && !sphereMagenta &&
                emptyAfterSphere.Coverage == 0 && emptyAfterSphere.NonfinitePixels == 0 && emptyAfter.Coverage == 0 && emptyAfter.NonfinitePixels == 0 } );
        float Delta( float[] a, float[] b ) => Enumerable.Range( 0, 3 ).Max( i => MathF.Abs( a[i] - b[i] ) );
        foreach ( var definition in definitions )
        {
            var fixedDefault = observations[route + "|fixed|default|" + definition.Name].Color;
            var annotatedDefault = observations[route + "|annotated|default|" + definition.Name].Color;
            var defaultExpected = definition.Name == "clamp_edge" ? new[] { 1f, 0f, 0f } : new[] { .7f, 0f, .3f };
            matrix.Add( new { route, name = definition.Name, check = "declared_default_unchanged", fixed_value = fixedDefault,
                annotated_value = annotatedDefault, expected_fixed_default = defaultExpected,
                same = Delta( fixedDefault, annotatedDefault ) <= .06f, expected_match = Delta( fixedDefault, defaultExpected ) <= .06f } );
            foreach ( var binding in new[] { "material", "object", "material_to_object_merge", "conflict_object_wins", "conflict_material_wins" } )
            {
                var fixedOverride = observations[route + "|fixed|" + binding + "|" + definition.Name].Color;
                var annotatedOverride = observations[route + "|annotated|" + binding + "|" + definition.Name].Color;
                matrix.Add( new { route, name = definition.Name, binding, check = "requested_sampler_binding",
                    fixed_unchanged = Delta( fixedDefault, fixedOverride ) <= .06f,
                    annotated_matches_analytic = Delta( annotatedOverride, definition.Expected ) <= .06f,
                    annotated_differs_from_default = Delta( annotatedDefault, annotatedOverride ) > .06f,
                    expected = definition.Expected, fixed_override = fixedOverride, annotated_override = annotatedOverride } );
            }
        }
        if ( clearCommands is not null ) { camera.RemoveCommandList( clearCommands ); clearCommands = null; }
    }
    phase = "complete";
}
catch ( OperationCanceledException ex ) { error = "Cancelled: " + ex; }
catch ( Exception ex ) { error = (ex is System.Reflection.TargetInvocationException tie ? tie.InnerException ?? tie : ex).ToString(); }
finally
{
    await MainThread.Wait();
    if ( active.IsValid() ) Clean( "scene object", () => active.Delete() );
    if ( camera.IsValid() && clearCommands is not null ) Clean( "clear command", () => camera.RemoveCommandList( clearCommands ) );
    if ( preview is not null && preview.IsValid && onFrame is not null ) Clean( "preview callback", () => preview.OnPreFrame -= onFrame );
    if ( light.IsValid() ) Clean( "light", () => light.Delete() );
    if ( preview is not null && preview.IsValid ) Clean( "preview", () => { preview.Close(); preview.Destroy(); } );
    if ( scene is not null ) Clean( "scene", () => scene.Destroy() );
    var destroyModel = typeof( Model ).GetMethod( "Destroy", flags ); foreach ( var model in models ) if ( model is not null && model.IsValid ) Clean( "model", () => destroyModel.Invoke( model, null ) );
    // Model.Destroy queues native release through MainThread.Queue, which is
    // synchronous when this cleanup is already on the main thread. Release the
    // task-owned mesh handles before dropping their materials and texture.
    if ( meshes.Count > 0 ) Clean( "meshes", () => engine.GetType( "Sandbox.GltfModelLoader", true ).GetMethod( "ReleaseMeshes", flags ).Invoke( null, new object[] { meshes } ) );
    foreach ( var material in materials ) if ( material is not null && material.IsValid ) Clean( "material", () => typeof( Material ).GetMethod( "Destroy", flags ).Invoke( material, null ) );
    if ( fixtureTexture is not null ) Clean( "fixture texture", () => fixtureTexture.Dispose() );
}
var controlsPassed = routeControls.Count == 2 && routeControls.All( entry => (bool)entry.GetType().GetProperty( "passed" ).GetValue( entry ) );
var coveragePassed = observations.Count == 72 && observations.Values.All( item => item.Coverage > 30 && item.NonfinitePixels == 0 && item.Color.All( float.IsFinite ) );
var complete = phase == "complete" && error is null && cleanupErrors.Count == 0 && controlsPassed && coveragePassed && results.Count == 72;
var receipt = new { completed_with_valid_controls = complete, phase, error, scope = "Diagnostic shader sampler-attribute binding matrix; does not prove stock shader, native/managed parity, glTF sampling conformance, or fix the native empty-view clear.",
    native_color_clear_requested = nativeClear, request_path = requestPath, request_sha256 = requestHash, shader_fixed_sha256 = fixedHash, shader_attribute_sha256 = attributeHash, runner_sha256 = runnerHash,
    engine_mvid = engine.ManifestModule.ModuleVersionId.ToString(), renderer_mvid = rendererMvid, renderer_sha256 = rendererHash, controls = routeControls, coverage_passed = coveragePassed, matrix, results, cleanup_errors = cleanupErrors,
    fixture = "task-owned transient 2x1 red-blue RGBA8, sampled without sRGB", is_editor = Application.IsEditor, starting_frame = startingFrame, final_frame = Application.FrameCount, output };
var receiptPath = System.IO.Path.Combine( output, "receipt.json" ); System.IO.File.WriteAllText( receiptPath, System.Text.Json.JsonSerializer.Serialize( receipt, new System.Text.Json.JsonSerializerOptions { WriteIndented = true, NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals } ) );
return new { completed_with_valid_controls = complete, phase, error, cases = results.Count, receipt_path = receiptPath };
