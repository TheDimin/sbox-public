// Coordinator-only, bounded native editor-scene camera exposure audit.
// Writes only a unique Temp diagnostic directory. Does not change global convars or published captures.
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
var engine = typeof( Model ).Assembly;
var checkout = new System.IO.DirectoryInfo( System.IO.Path.GetDirectoryName( engine.Location ) );
while ( checkout is not null && !System.IO.File.Exists( System.IO.Path.Combine( checkout.FullName, "engine/Sandbox-Engine.slnx" ) ) ) checkout = checkout.Parent;
if ( checkout is null ) throw new InvalidOperationException( "Checkout unavailable." );
var output = System.IO.Path.Combine( checkout.FullName, "Temp/gltf-validation/exposure-lifecycle", Guid.NewGuid().ToString( "N" ) );
var observations = new System.Collections.Generic.List<object>();
var cleanupErrors = new System.Collections.Generic.List<string>();
Scene scene = null; CameraComponent primary = null, camera = null; Editor.SceneRenderingWidget widget = null;
SceneObject sphere = null; SceneDirectionalLight light = null; Action onFrame = null; int frames = 0;
void Require( bool ok, string reason ) { if ( !ok ) throw new InvalidOperationException( reason ); }
void Clean( string label, Action action ) { try { action(); } catch ( Exception e ) { cleanupErrors.Add( label + ": " + e ); } }
async Task Safe()
{
    await MainThread.Wait(); Require( Application.IsEditor && !Game.IsPlaying, "Requires idle editor." );
    Require( ConsoleSystem.GetValue( "r_managed_scene", "false" ).ToLowerInvariant() is "false" or "0", "Requires existing native renderer." );
    var deadline = System.Diagnostics.Stopwatch.StartNew();
    while ( Graphics.IsActive ) { Require( deadline.Elapsed.TotalSeconds < 8, "Graphics boundary unavailable." ); await Task.Delay( 20, cancellationToken ); await MainThread.Wait(); }
}
async Task WaitFrames()
{
    await Safe(); var start = frames; var deadline = System.Diagnostics.Stopwatch.StartNew();
    while ( frames < start + 3 ) { Require( deadline.Elapsed.TotalSeconds < 12, "Preview stopped receiving frames." ); await Task.Delay( 20, cancellationToken ); await Safe(); }
}
object CameraState( CameraComponent cc )
{
    var sc = typeof( CameraComponent ).GetProperty( "SceneCamera", flags )?.GetValue( cc );
    var tone = sc?.GetType().GetProperty( "Tonemap", flags )?.GetValue( sc );
    object Value( string name ) => tone?.GetType().GetProperty( name, flags )?.GetValue( tone );
    return new { isSceneCamera = ReferenceEquals( scene.Camera, cc ), cc.AutoExposure.Enabled, cc.AutoExposure.MinimumExposure, cc.AutoExposure.MaximumExposure, cc.AutoExposure.Compensation, cc.AutoExposure.Rate, nativeTonemapEnabled = Value( "Enabled" ), nativeMin = Value( "MinExposure" ), nativeMax = Value( "MaxExposure" ), nativeCompensation = Value( "ExposureCompensation" ), postprocessing = cc.EnablePostProcessing };
}
void SetExposure( CameraComponent cc, bool enabled, float exposure )
{
    cc.AutoExposure.Enabled = enabled; cc.AutoExposure.MinimumExposure = exposure; cc.AutoExposure.MaximumExposure = exposure; cc.AutoExposure.Compensation = 0; cc.AutoExposure.Rate = 20;
}
try
{
    await Safe(); System.IO.Directory.CreateDirectory( output );
    Require( Model.Sphere is { IsValid: true, IsError: false }, "Stock sphere unavailable." );
    scene = Scene.CreateEditorScene();
    using ( scene.Push() )
    {
        primary = scene.CreateObject().Components.Create<CameraComponent>(); primary.IsMainCamera = true;
        camera = scene.CreateObject().Components.Create<CameraComponent>(); camera.IsMainCamera = false;
        camera.WorldPosition = new Vector3( 200, 0, 0 ); camera.WorldRotation = Rotation.FromYaw( 180 );
        camera.Orthographic = true; camera.OrthographicHeight = 160; camera.ZNear = 1; camera.ZFar = 1000;
        camera.CustomSize = new Vector2( 192, 192 ); camera.BackgroundColor = Color.Black; camera.ClearFlags = ClearFlags.All;
        camera.DebugMode = SceneCameraDebugMode.Normal; camera.EnablePostProcessing = true;
        scene.EditorTick( 0, 0 );
    }
    Require( ReferenceEquals( scene.Camera, primary ) && !ReferenceEquals( scene.Camera, camera ), "Could not isolate secondary capture camera." );
    sphere = new SceneObject( scene.SceneWorld, Model.Sphere, Transform.Zero );
    light = new SceneDirectionalLight( scene.SceneWorld, Rotation.LookAt( new Vector3( -1, -.5f, -1 ).Normal, Vector3.Up ), Color.White );
    widget = new Editor.SceneRenderingWidget { Scene = scene, Camera = camera, WindowTitle = "glTF exposure lifecycle diagnostic", Size = new Vector2( 192, 192 ) };
    onFrame = () => frames++; widget.OnPreFrame += onFrame; widget.Show();
    foreach ( var setting in new[] { (Label: "off", Enabled: false, Exposure: 1f), (Label: "half", Enabled: true, Exposure: .5f), (Label: "one", Enabled: true, Exposure: 1f), (Label: "two", Enabled: true, Exposure: 2f) } )
    {
        await Safe(); SetExposure( primary, setting.Enabled, setting.Exposure ); SetExposure( camera, setting.Enabled, setting.Exposure );
        var requestedPrimary = CameraState( primary ); var requestedCapture = CameraState( camera );
        using ( scene.Push() ) scene.EditorTick( 0, 0 );
        var tickPrimary = CameraState( primary ); var tickCapture = CameraState( camera );
        await WaitFrames(); var framesPrimary = CameraState( primary ); var framesCapture = CameraState( camera );
        var renders = new System.Collections.Generic.List<object>();
        for ( var index = 0; index < 2; index++ )
        {
            await Safe(); var before = CameraState( camera );
            using var target = Texture.CreateRenderTarget().WithSize( 192, 192 ).WithFormat( ImageFormat.RGBA8888 ).WithInitialColor( Color.Magenta ).Create( "exposure_audit_" + Guid.NewGuid().ToString( "N" ) );
            bool submitted; using ( scene.Push() ) { target.Clear( Color.Magenta ); submitted = camera.RenderToTexture( target ); }
            Require( submitted, "Native render rejected." ); var afterSubmit = CameraState( camera ); await WaitFrames();
            using var bitmap = target.GetBitmap(); Require( bitmap is not null && bitmap.Width == 192 && bitmap.Height == 192, "Invalid target readback." );
            var pixels = bitmap.GetPixels(); var png = bitmap.ToPng();
            var path = System.IO.Path.Combine( output, setting.Label + "_" + index + ".png" ); System.IO.File.WriteAllBytes( path, png );
            renders.Add( new { before, afterSubmit, afterFrames = CameraState( camera ), path, sha256 = Convert.ToHexStringLower( System.Security.Cryptography.SHA256.HashData( png ) ), rgbMean = new[] { pixels.Average( p => p.r ), pixels.Average( p => p.g ), pixels.Average( p => p.b ) }, engineFrame = Application.FrameCount, widgetFrames = frames } );
        }
        observations.Add( new { setting.Label, setting.Enabled, requestedExposure = setting.Exposure, requestedPrimary, requestedCapture, tickPrimary, tickCapture, framesPrimary, framesCapture, renders } );
    }
}
finally
{
    await MainThread.Wait();
    Clean( "widget callback", () => { if ( widget is not null && onFrame is not null ) widget.OnPreFrame -= onFrame; } );
    Clean( "widget close", () => { if ( widget is not null && widget.IsValid ) widget.Close(); } );
    Clean( "widget destroy", () => { if ( widget is not null && widget.IsValid ) widget.Destroy(); } );
    Clean( "sphere", () => { if ( sphere.IsValid() ) sphere.Delete(); } );
    Clean( "light", () => { if ( light.IsValid() ) light.Delete(); } );
    Clean( "scene", () => { if ( scene.IsValid() ) scene.Destroy(); } );
}
if ( cleanupErrors.Count != 0 ) throw new InvalidOperationException( "Probe cleanup failed: " + string.Join( "; ", cleanupErrors ) );
return new { output, observations, scope = "Private native editor scene, primary versus secondary camera, normal postprocessing, stock sphere, off and equal-range requested exposures; compare actual settings, images and repeat stability. Managed native-tonemap fields are not an independent exposure-scalar readback. No IBL or reference match is asserted.", engineMvid = engine.ManifestModule.ModuleVersionId.ToString() };
