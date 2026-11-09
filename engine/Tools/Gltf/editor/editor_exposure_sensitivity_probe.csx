// Coordinator-only, bounded native editor-scene camera exposure audit.
// Writes only a unique Temp diagnostic directory. Does not change global convars or published captures.
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
var engine = typeof( Model ).Assembly;
var checkout = new System.IO.DirectoryInfo( System.IO.Path.GetDirectoryName( engine.Location ) );
while ( checkout is not null && !System.IO.File.Exists( System.IO.Path.Combine( checkout.FullName, "engine/Sandbox-Engine.slnx" ) ) ) checkout = checkout.Parent;
if ( checkout is null ) throw new InvalidOperationException( "Checkout unavailable." );
var output = System.IO.Path.Combine( checkout.FullName, "Temp/gltf-validation/exposure-sensitivity", Guid.NewGuid().ToString( "N" ) );
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
async Task WaitFrames( int count = 3 )
{
    await Safe(); var start = frames; var deadline = System.Diagnostics.Stopwatch.StartNew();
    while ( frames < start + count ) { Require( deadline.Elapsed.TotalSeconds < 25, "Preview stopped receiving frames." ); await Task.Delay( 20, cancellationToken ); await Safe(); }
}
object CameraState( CameraComponent cc )
{
    var sc = typeof( CameraComponent ).GetProperty( "SceneCamera", flags )?.GetValue( cc );
    var tone = sc?.GetType().GetProperty( "Tonemap", flags )?.GetValue( sc );
    object Value( string name ) => tone?.GetType().GetProperty( name, flags )?.GetValue( tone );
    return new { isSceneCamera = ReferenceEquals( scene.Camera, cc ), cc.AutoExposure.Enabled, cc.AutoExposure.MinimumExposure, cc.AutoExposure.MaximumExposure, cc.AutoExposure.Compensation, cc.AutoExposure.Rate, nativeTonemapEnabled = Value( "Enabled" ), nativeMin = Value( "MinExposure" ), nativeMax = Value( "MaxExposure" ), nativeCompensation = Value( "ExposureCompensation" ), postprocessing = cc.EnablePostProcessing };
}
// The managed renderer uses TonemapFrameUpdate(native, false) when native has
// already advanced exposure. This diagnostic samples without requesting an update;
// this is a native tonemap value, not proof the native camera used it in a draw.
float ReadTonemapScalar( CameraComponent cc )
{
    var sc = typeof( CameraComponent ).GetProperty( "SceneCamera", flags )?.GetValue( cc ) ?? throw new InvalidOperationException( "SceneCamera absent." );
    var tone = sc.GetType().GetField( "ToneMapping", flags )?.GetValue( sc ) ?? throw new InvalidOperationException( "ToneMapping absent." );
    var native = tone.GetType().GetMethod( "GetNative", flags )?.Invoke( tone, null ) ?? throw new InvalidOperationException( "Native tonemap absent." );
    var tools = engine.GetType( "NativeEngine.RenderTools", true );
    var method = tools.GetMethod( "TonemapFrameUpdate", flags | System.Reflection.BindingFlags.Static ) ?? throw new MissingMethodException( "TonemapFrameUpdate" );
    var scalar = Convert.ToSingle( method.Invoke( null, new object[] { native, false } ) );
    Require( float.IsFinite( scalar ), "Native tonemap scalar nonfinite." ); return scalar;
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
    widget = new Editor.SceneRenderingWidget { Scene = scene, Camera = camera, WindowTitle = "glTF exposure sensitivity diagnostic", Size = new Vector2( 192, 192 ) };
    onFrame = () => frames++; widget.OnPreFrame += onFrame; widget.Show();
    foreach ( var setting in new[] { (Label: "disabled", Enabled: false, Exposure: 1f, Light: 1f), (Label: "low", Enabled: true, Exposure: .125f, Light: 1f), (Label: "high", Enabled: true, Exposure: 8f, Light: 1f), (Label: "light_positive", Enabled: false, Exposure: 1f, Light: .125f) } )
    {
        await Safe(); light.LightColor = Color.White * setting.Light; SetExposure( primary, setting.Enabled, setting.Exposure ); SetExposure( camera, setting.Enabled, setting.Exposure );
        var requestedPrimary = CameraState( primary ); var requestedCapture = CameraState( camera );
        using ( scene.Push() ) scene.EditorTick( 0, 0 );
        var tickPrimary = CameraState( primary ); var tickCapture = CameraState( camera );
        await WaitFrames(); var framesPrimary = CameraState( primary ); var framesCapture = CameraState( camera );
        var renders = new System.Collections.Generic.List<object>();
        for ( var index = 0; index < 2; index++ )
        {
            if ( index == 1 ) await WaitFrames( 30 );
            await Safe(); var before = CameraState( camera ); var scalarBefore = ReadTonemapScalar( camera );
            using var target = Texture.CreateRenderTarget().WithSize( 192, 192 ).WithFormat( ImageFormat.RGBA8888 ).WithInitialColor( Color.Magenta ).Create( "exposure_audit_" + Guid.NewGuid().ToString( "N" ) );
            bool submitted; using ( scene.Push() ) { target.Clear( Color.Magenta ); submitted = camera.RenderToTexture( target ); }
            Require( submitted, "Native render rejected." ); var afterSubmit = CameraState( camera ); var scalarAfter = ReadTonemapScalar( camera ); await WaitFrames();
            using var bitmap = target.GetBitmap(); Require( bitmap is not null && bitmap.Width == 192 && bitmap.Height == 192, "Invalid target readback." );
            var pixels = bitmap.GetPixels(); var png = bitmap.ToPng();
            var path = System.IO.Path.Combine( output, setting.Label + "_" + index + ".png" ); System.IO.File.WriteAllBytes( path, png );
            renders.Add( new { before, afterSubmit, scalarBefore, scalarAfter, afterFrames = CameraState( camera ), path, sha256 = Convert.ToHexStringLower( System.Security.Cryptography.SHA256.HashData( png ) ), rgbMean = new[] { pixels.Average( p => p.r ), pixels.Average( p => p.g ), pixels.Average( p => p.b ) }, engineFrame = Application.FrameCount, widgetFrames = frames } );
        }
        observations.Add( new { setting.Label, setting.Enabled, requestedExposure = setting.Exposure, light = setting.Light, requestedPrimary, requestedCapture, tickPrimary, tickCapture, framesPrimary, framesCapture, renders } );
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
return new { output, observations, scope = "Private native editor scene, secondary camera, normal postprocessing, stock sphere; disabled, equal-range 0.125 and 8, and independent dim-light positive control, sampled immediately and after 30 widget frames. Scalar uses native TonemapFrameUpdate(false), not direct proof of scalar bound in native capture. No IBL or reference match asserted.", engineMvid = engine.ManifestModule.ModuleVersionId.ToString() };
