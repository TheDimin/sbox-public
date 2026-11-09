// Async method body for the existing editor. A collectible IVT bridge compiles
// against the loaded engine; no producer source, scene or project is changed.
var engine = typeof( Model ).Assembly;
var root = new System.IO.DirectoryInfo( System.IO.Path.GetDirectoryName( engine.Location ) );
while ( root is not null && !System.IO.File.Exists( System.IO.Path.Combine( root.FullName, "engine", "Sandbox-Engine.slnx" ) ) ) root = root.Parent;
if ( root is null ) throw new InvalidOperationException( "Cannot locate checkout from the loaded engine." );
var runId = Guid.NewGuid().ToString( "N" );
var output = System.IO.Path.Combine( root.FullName, "Temp", "gltf-validation", "editor-raw-vertex-buffer", runId );
System.IO.Directory.CreateDirectory( output );
var receiptPath = System.IO.Path.Combine( output, "receipt.json" );
var runnerPath = System.IO.Path.Combine( root.FullName, "engine", "Tools", "Gltf", "editor", "editor_raw_vertex_buffer.csx" );
var sourcePath = System.IO.Path.Combine( root.FullName, "engine", "Sandbox.Engine", "Resources", "Mesh", "Mesh.RawVertexBuffer.cs" );
var captures = new System.Collections.Generic.List<object>();
var checks = new System.Collections.Generic.List<object>();
var diagnostics = new System.Collections.Generic.List<string>();
var cleanupErrors = new System.Collections.Generic.List<string>();
var models = new System.Collections.Generic.List<Model>();
var meshes = new System.Collections.Generic.List<Mesh>();
var sceneObjects = new System.Collections.Generic.List<SceneModel>();
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
var phase = "initialize"; string error = null, diskMvid = null, helperMvid = null, sourceHash = null, runnerHash = null, shaderName = null;
var frameStart = Application.FrameCount; var previewFrames = 0; var clearDiagnostic = Environment.GetEnvironmentVariable( "SBOX_GLTF_CAMERA_CLEAR" ) == "1";
Scene scene = null; Editor.SceneRenderingWidget preview = null; Texture target = null; Material material = null;
CameraComponent camera = null; Sandbox.Rendering.CommandList clearCommands = null; Action onPreviewFrame = null;
System.Runtime.Loader.AssemblyLoadContext helperContext = null;
string Hash( byte[] bytes ) => Convert.ToHexStringLower( System.Security.Cryptography.SHA256.HashData( bytes ) );
void Require( bool value, string message ) { if ( !value ) throw new InvalidOperationException( message ); }
void Cleanup( string label, Action action ) { try { action(); } catch ( Exception ex ) { cleanupErrors.Add( label + ": " + ex ); } }
bool Different( Color a, Color b ) => MathF.Abs( a.r - b.r ) > .03f || MathF.Abs( a.g - b.g ) > .03f || MathF.Abs( a.b - b.b ) > .03f;
int Difference( Color[] a, Color[] b ) => a.Zip( b, ( x, y ) => Different( x, y ) ).Count( x => x );
var bridgeSource = """
using System;
using System.Collections.Generic;
using System.Buffers.Binary;
using NativeEngine;
using Sandbox;
public static class RawVertexEditorBridge
{
    private static readonly RawVertexField[] Fields = {
        new("texcoord", 1, 0, ColorFormat.COLOR_FORMAT_R32_UINT),
        new("position", 0, 4, ColorFormat.COLOR_FORMAT_R32G32B32_FLOAT),
        new("texcoord", 2, 16, ColorFormat.COLOR_FORMAT_R32_SINT),
        new("normal", 0, 24, ColorFormat.COLOR_FORMAT_R32G32B32_FLOAT),
        new("tangent", 0, 40, ColorFormat.COLOR_FORMAT_R32G32B32A32_FLOAT),
        new("color", 0, 56, ColorFormat.COLOR_FORMAT_R8G8B8A8_UNORM)
    };
    static void Float(byte[] a, int o, float f) => BinaryPrimitives.WriteInt32LittleEndian(a.AsSpan(o,4), BitConverter.SingleToInt32Bits(f));
    public static Model Build(Material material, List<Mesh> meshes, bool raw, bool trimmed, bool colored, string name)
    {
        const int stride = 64; const float unit = 1f / .0254f;
        var positions = new[] { Vector3.Zero, new Vector3(0,unit,0), new Vector3(0,0,unit) };
        var color = colored ? new Color32(64,192,128,255) : Color32.White;
        var mesh = new Mesh(material); meshes.Add(mesh);
        if (raw)
        {
            var bytes = new byte[trimmed ? 188 : 192];
            for (int i = 0; i < 3; ++i)
            {
                int o = i * stride;
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(o,4), 0xA0B0C001u + (uint)i);
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(o+16,4), -100 - i);
                Float(bytes,o+4,positions[i].x); Float(bytes,o+8,positions[i].y); Float(bytes,o+12,positions[i].z);
                Float(bytes,o+24,1); Float(bytes,o+28,0); Float(bytes,o+32,0);
                Float(bytes,o+40,0); Float(bytes,o+44,1); Float(bytes,o+48,0); Float(bytes,o+52,1);
                bytes[o+56] = color.r; bytes[o+57] = color.g; bytes[o+58] = color.b; bytes[o+59] = color.a;
            }
            bool rejected = false;
            try { mesh.CreateRawVertexBuffer(stride,3,bytes.AsSpan(0,187),Fields); }
            catch (ArgumentException) { rejected = true; }
            if (!rejected || mesh.HasVertexBuffer) throw new InvalidOperationException("Short input was not rejected before publication.");
            mesh.CreateRawVertexBuffer(stride,3,bytes,Fields);
        }
        else
        {
            var vertices = new Vertex[3];
            for (int i = 0; i < 3; ++i) vertices[i] = new Vertex { Position=positions[i], Normal=Vector3.Forward, Tangent=new Vector4(0,1,0,1), Color=color };
            mesh.CreateVertexBuffer(3,vertices);
        }
        if (!mesh.HasVertexBuffer || mesh.VertexCount != 3) throw new InvalidOperationException("Native vertex buffer was not attached.");
        mesh.CreateIndexBuffer(3,new[] {0,1,2});
        var bounds = new BBox(Vector3.Zero,new Vector3(0,unit,unit)); mesh.Bounds = bounds;
        return Model.Builder.WithName(name).AddMesh(mesh).WithViewBounds(bounds).Create();
    }
}
""";
try
{
    await MainThread.Wait();
    Require( Application.IsEditor && !Graphics.IsActive, "Run in the existing editor outside a graphics scope." );
    sourceHash = Hash( System.IO.File.ReadAllBytes( sourcePath ) ); runnerHash = Hash( System.IO.File.ReadAllBytes( runnerPath ) );
    using ( var stream = System.IO.File.OpenRead( engine.Location ) )
    using ( var pe = new System.Reflection.PortableExecutable.PEReader( stream ) )
    {
        var md = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader( pe );
        diskMvid = md.GetGuid( md.GetModuleDefinition().Mvid ).ToString();
    }
    Require( diskMvid == engine.ManifestModule.ModuleVersionId.ToString(), "Loaded engine differs from the engine binary on disk." );
    phase = "compile-ivt-bridge";
    var references = new System.Collections.Generic.Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
    foreach ( var assembly in AppDomain.CurrentDomain.GetAssemblies() )
    {
        try
        {
            if ( assembly.IsDynamic || System.Runtime.Loader.AssemblyLoadContext.GetLoadContext( assembly ) != System.Runtime.Loader.AssemblyLoadContext.Default ) continue;
            var name = assembly.GetName().Name;
            if ( name == "Sandbox.Test.Unit" || string.IsNullOrEmpty( assembly.Location ) || !System.IO.File.Exists( assembly.Location ) ) continue;
            if ( !references.ContainsKey( name ) ) references.Add( name, assembly.Location );
        }
        catch ( Exception ex ) when ( ex is System.NotSupportedException || ex is System.IO.IOException || ex is BadImageFormatException || ex is UnauthorizedAccessException ) { }
    }
    references[engine.GetName().Name] = engine.Location;
    var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText( bridgeSource, new Microsoft.CodeAnalysis.CSharp.CSharpParseOptions( Microsoft.CodeAnalysis.CSharp.LanguageVersion.Preview ), path: "RawVertexEditorBridge.cs" );
    var options = new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions( Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary, optimizationLevel: Microsoft.CodeAnalysis.OptimizationLevel.Release );
    var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create( "Sandbox.Test.Unit", new[] { tree }, references.Values.Select( x => Microsoft.CodeAnalysis.MetadataReference.CreateFromFile( x ) ), options );
    using var streamImage = new System.IO.MemoryStream();
    var result = compilation.Emit( streamImage, cancellationToken: cancellationToken ); diagnostics.AddRange( result.Diagnostics.Select( x => x.ToString() ) );
    Require( result.Success, "Bridge could not compile against the loaded producer; inspect diagnostics." );
    helperContext = new System.Runtime.Loader.AssemblyLoadContext( "raw-vertex-editor-" + runId, true );
    helperContext.Resolving += ( _, requested ) => AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault( a => !a.IsDynamic && System.Runtime.Loader.AssemblyLoadContext.GetLoadContext( a ) == System.Runtime.Loader.AssemblyLoadContext.Default && System.Reflection.AssemblyName.ReferenceMatchesDefinition( a.GetName(), requested ) );
    streamImage.Position = 0; var helper = helperContext.LoadFromStream( streamImage ); helperMvid = helper.ManifestModule.ModuleVersionId.ToString();
    var build = helper.GetType( "RawVertexEditorBridge", true ).GetMethod( "Build" );
    // The stock white material uses complex.shader, and its actual editor
    // control showed no vertex-color response. This shipped material uses
    // vertex_color.shader, whose VS explicitly reads COLOR0.
    var stock = Material.Load( "materials/default/vertex_color.vmat" );
    Require( stock is { IsValid: true } && !stock.IsError, "Stock vertex-color material is unavailable." );
    shaderName = stock.ShaderName;
    Require( !string.IsNullOrEmpty( shaderName ) && shaderName.Contains( "vertex_color", StringComparison.OrdinalIgnoreCase ), "The stock material did not resolve the vertex-color shader." );
    material = stock.CreateCopy( "raw_vertex_editor_" + runId );
    Require( material is { IsValid: true } && !material.IsError, "Stock material copy failed." );
    phase = "create-native-models";
    Model Build( bool raw, bool trimmed, bool colored, string name )
    {
        var model = (Model)build.Invoke( null, new object[] { material, meshes, raw, trimmed, colored, name + runId } );
        Require( model is { IsValid: true } && !model.IsError, "Could not construct native model " + name );
        models.Add( model ); return model;
    }
    var typedColor = Build( false, false, true, "raw_vertex_typed_color_" );
    var typedWhite = Build( false, false, false, "raw_vertex_typed_white_" );
    var rawComplete = Build( true, false, true, "raw_vertex_complete_" );
    var rawTrimmed = Build( true, true, true, "raw_vertex_trimmed_" );
    phase = "private-preview";
    scene = Scene.CreateEditorScene();
    using ( scene.Push() )
    {
        camera = scene.CreateObject().Components.Create<CameraComponent>();
        camera.WorldPosition = new Vector3( 80, 19.68504f, 19.68504f ); camera.WorldRotation = Rotation.FromYaw( 180 );
        camera.BackgroundColor = Color.Black; camera.ZNear = 1; camera.FieldOfView = 50; camera.EnablePostProcessing = false; camera.DebugMode = SceneCameraDebugMode.Albedo;
    }
    if ( clearDiagnostic )
    {
        clearCommands = new Sandbox.Rendering.CommandList( "Raw vertex probe diagnostic clear" );
        clearCommands.Clear( Color.Black, true, false, false ); camera.AddCommandList( clearCommands, Sandbox.Rendering.Stage.AfterDepthPrepass );
    }
    preview = new Editor.SceneRenderingWidget { Scene = scene, Camera = camera, WindowTitle = "Raw vertex buffer validation", Size = new Vector2( 384, 384 ) };
    onPreviewFrame = () => previewFrames++; preview.OnPreFrame += onPreviewFrame; preview.Show();
    target = Texture.CreateRenderTarget().WithSize( 96, 96 ).WithFormat( ImageFormat.RGBA8888 ).Create( "raw_vertex_target_" + runId );
    async Task WaitFrames()
    {
        int start = previewFrames; var watch = System.Diagnostics.Stopwatch.StartNew();
        while ( previewFrames < start + 3 )
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ( watch.Elapsed > TimeSpan.FromSeconds( 5 ) ) throw new TimeoutException( "Preview did not advance." );
            await Task.Delay( 50, cancellationToken ); await MainThread.Wait();
        }
    }
    async Task<Color[]> Capture( string name )
    {
        phase = "capture-" + name; await WaitFrames(); int before = previewFrames;
        using ( scene.Push() ) { target.Clear( Color.Black ); Require( camera.RenderToTexture( target ), "Camera submission failed: " + name ); }
        await WaitFrames();
        using var bitmap = target.GetBitmap(); Require( bitmap is not null, "Readback failed: " + name );
        var png = bitmap.ToPng(); var path = System.IO.Path.Combine( output, name + ".png" ); System.IO.File.WriteAllBytes( path, png );
        captures.Add( new { name, path, sha256 = Hash( png ), submitted_preview_frame = before, readback_preview_frame = previewFrames, frame = Application.FrameCount } );
        return bitmap.GetPixels();
    }
    var background = await Capture( "empty" );
    Require( background.Count( x => Different( x, Color.Black ) ) == 0, "Empty control did not clear to black." );
    var images = new System.Collections.Generic.Dictionary<string, Color[]>();
    foreach ( var item in new[] { ("typed_color", typedColor), ("typed_white", typedWhite), ("raw_complete", rawComplete), ("raw_trimmed", rawTrimmed) } )
    {
        var obj = new SceneModel( scene.SceneWorld, item.Item2, Transform.Zero ); sceneObjects.Add( obj );
        images[item.Item1] = await Capture( item.Item1 );
        obj.Delete(); sceneObjects.Remove( obj );
        var empty = await Capture( item.Item1 + "_after" );
        Require( Difference( empty, background ) == 0, "Deleted model remains visible: " + item.Item1 );
    }
    var coverage = Difference( images["typed_color"], background );
    var whiteDifference = Difference( images["typed_color"], images["typed_white"] );
    var fullDifference = Difference( images["typed_color"], images["raw_complete"] );
    var trimmedDifference = Difference( images["typed_color"], images["raw_trimmed"] );
    checks.Add( new { coverage, typed_color_control_difference = whiteDifference, full_difference = fullDifference, trimmed_difference = trimmedDifference } );
    Require( coverage > 100, "Typed reference produced insufficient visible geometry." );
    Require( whiteDifference > 100, "Typed vertex-color sensitivity control failed; normalized color is not observable." );
    Require( fullDifference <= 5 && trimmedDifference <= 5, "Raw layout output differs from the typed reference." );
    phase = "complete";
}
catch ( OperationCanceledException ex ) { error = "Cancelled: " + ex; }
catch ( Exception ex ) { error = (ex is System.Reflection.TargetInvocationException tie ? tie.InnerException ?? tie : ex).ToString(); }
finally
{
    await MainThread.Wait();
    if ( preview is not null && preview.IsValid && onPreviewFrame is not null ) Cleanup( "preview callback", () => preview.OnPreFrame -= onPreviewFrame );
    if ( camera.IsValid() && clearCommands is not null ) Cleanup( "camera command", () => camera.RemoveCommandList( clearCommands ) );
    if ( preview is not null && preview.IsValid ) Cleanup( "preview", () => { preview.Close(); preview.Destroy(); } );
    foreach ( var obj in sceneObjects ) if ( obj.IsValid() ) Cleanup( "scene object", () => obj.Delete() );
    if ( scene is not null ) Cleanup( "scene", () => scene.Destroy() );
    if ( target is not null ) Cleanup( "target", () => target.Dispose() );
    var destroyModel = typeof( Model ).GetMethod( "Destroy", flags );
    foreach ( var model in models ) if ( model is not null && model.IsValid ) Cleanup( "model", () => destroyModel.Invoke( model, null ) );
    if ( material is not null && material.IsValid ) Cleanup( "material", () => typeof( Material ).GetMethod( "Destroy", flags ).Invoke( material, null ) );
    if ( helperContext is not null ) Cleanup( "bridge context", () => helperContext.Unload() );
}
var passed = error is null && cleanupErrors.Count == 0 && phase == "complete";
var receipt = new { passed, phase, error, scope = "Single-stream native Mesh draw: runtime stride/offsets and normalized color against typed geometry. UINT/SINT descriptors are included but their shader values are not read; native failure rollback and lifetime are not proven.",
    integer_attribute_values_observed = false, native_failure_rollback_proven = false, native_lifetime_proven = false, multistream_proven = false,
    engine_location = engine.Location, engine_loaded_mvid = engine.ManifestModule.ModuleVersionId.ToString(), engine_disk_mvid = diskMvid, engine_source_sha256 = sourceHash, runner_sha256 = runnerHash, bridge_sha256 = Hash( System.Text.Encoding.UTF8.GetBytes( bridgeSource ) ), helper_mvid = helperMvid,
    material_path = "materials/default/vertex_color.vmat", material_shader = shaderName,
    diagnostic_camera_clear = clearDiagnostic, preview_frames = previewFrames, initial_frame = frameStart, final_frame = Application.FrameCount, captures, checks, diagnostics, cleanup_errors = cleanupErrors, output };
System.IO.File.WriteAllText( receiptPath, System.Text.Json.JsonSerializer.Serialize( receipt, new System.Text.Json.JsonSerializerOptions { WriteIndented = true } ) );
return new { passed, phase, error, receipt_path = receiptPath, integer_attribute_values_observed = false, native_lifetime_proven = false };
