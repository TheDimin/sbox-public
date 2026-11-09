// Native capture gate for the direct GPU backend. No ModelBuilder, Mesh, glTF
// native-model substitute, shader compilation or global renderer mutation.
var engine = typeof( Model ).Assembly;
var checkout = new System.IO.DirectoryInfo( System.IO.Path.GetDirectoryName( engine.Location ) );
while ( checkout is not null && !System.IO.File.Exists( System.IO.Path.Combine( checkout.FullName, "engine", "Sandbox-Engine.slnx" ) ) ) checkout = checkout.Parent;
if ( checkout is null ) throw new InvalidOperationException( "Cannot locate checkout." );
var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static;
var bridge = engine.GetType( "Sandbox.Rendering.ManagedSceneRendering", true );
var enabled = bridge.GetProperty( "Enabled", flags );
var id = Guid.NewGuid().ToString( "N" );
var output = System.IO.Path.Combine( checkout.FullName, "Temp/gltf-validation/native-capture-controls", id );
var captures = new System.Collections.Generic.List<object>(); var rows = new System.Collections.Generic.List<object>();
var errors = new System.Collections.Generic.List<string>(); var callbackErrors = new System.Collections.Concurrent.ConcurrentQueue<string>();
Scene scene = null; CameraComponent camera = null; Editor.SceneRenderingWidget preview = null;
SceneObject sphere = null; SceneCustomObject custom = null; ScenePointLight light = null; Material material = null;
Action onFrame = null; int frames = 0, calls = 0, activeCalls = 0; string phase = "initialize", failure = null;
const int size = 192;
string Hash( byte[] data ) => Convert.ToHexStringLower( System.Security.Cryptography.SHA256.HashData( data ) );
void Require( bool condition, string message ) { if ( !condition ) throw new InvalidOperationException( message ); }
void Clean( string label, Action action ) { try { action(); } catch ( Exception e ) { errors.Add( label + ": " + e ); } }
bool NativeEnabled() => !(bool)enabled.GetValue( null );
async Task Safe()
{
	await MainThread.Wait(); Require( Application.IsEditor && !Game.IsPlaying, "Requires the idle physical editor." );
	Require( NativeEnabled(), "Native controls require the existing r_managed_scene=false; the probe will not change it." );
	var timer = System.Diagnostics.Stopwatch.StartNew();
	while ( Graphics.IsActive ) { Require( timer.Elapsed < TimeSpan.FromSeconds( 5 ), "No safe main-thread graphics boundary." ); await Task.Delay( 20, cancellationToken ); await MainThread.Wait(); }
}
async Task Frames()
{
	var before = frames; var timer = System.Diagnostics.Stopwatch.StartNew();
	while ( frames < before + 3 ) { Require( timer.Elapsed < TimeSpan.FromSeconds( 8 ), "Visible native widget stopped receiving frames." ); await Task.Delay( 30, cancellationToken ); await Safe(); }
}
object CameraState()
{
	var sceneCamera = typeof( CameraComponent ).GetProperty( "SceneCamera", flags ).GetValue( camera );
	return new { customSize = camera.CustomSize?.ToString(), screenRect = camera.ScreenRect.ToString(), sceneSize = sceneCamera.GetType().GetProperty( "Size", flags ).GetValue( sceneCamera )?.ToString(), viewport = camera.Viewport.ToString(), clearFlags = camera.ClearFlags.ToString(), debugMode = camera.DebugMode.ToString(), postprocessing_enabled = camera.EnablePostProcessing };
}
async Task<(Color[] Pixels, int Coverage, int Magenta)> Capture( string route, string label, bool bitmapRoute )
{
	phase = route + "_" + label; await Safe(); await Frames();
	using ( scene.Push() )
	{
		typeof( Scene ).GetMethod( "PreCameraRender", flags ).Invoke( scene, null );
		typeof( CameraComponent ).GetMethod( "InitializeRendering", flags ).Invoke( camera, new object[] { false } );
	}
	var before = CameraState(); Color[] pixels; byte[] png; bool? submitted = null;
	if ( bitmapRoute )
	{
		using var bitmap = new Bitmap( size, size ); bitmap.Clear( Color.Magenta );
		using ( scene.Push() ) camera.RenderToBitmap( bitmap, renderUI: false );
		pixels = bitmap.GetPixels(); png = bitmap.ToPng();
	}
	else
	{
		using var target = Texture.CreateRenderTarget().WithSize( size, size ).WithFormat( ImageFormat.RGBA8888 ).WithInitialColor( Color.Magenta ).Create( "native_capture_" + Guid.NewGuid().ToString( "N" ) );
		using ( scene.Push() ) { target.Clear( Color.Magenta ); submitted = camera.RenderToTexture( target ); }
		await Frames(); using var bitmap = target.GetBitmap(); Require( bitmap is not null, "Native target readback failed." );
		pixels = bitmap.GetPixels(); png = bitmap.ToPng();
	}
	Require( NativeEnabled(), "Global renderer changed during the native control run." );
	var coverage = pixels.Count( p => p.r > .03f || p.g > .03f || p.b > .03f );
	var magenta = pixels.Count( p => p.r > .8f && p.g < .2f && p.b > .8f );
	var path = System.IO.Path.Combine( output, phase + ".png" ); System.IO.File.WriteAllBytes( path, png );
	captures.Add( new { route, label, path, sha256 = Hash( png ), size, submitted, coverage, magenta, camera_before = before, camera_after = CameraState(), widget_frames = frames, engine_frame = Application.FrameCount, managed_scene = !NativeEnabled() } );
	return (pixels, coverage, magenta);
}
void RemoveObjects()
{
	if ( sphere.IsValid() ) sphere.Delete(); sphere = null;
	if ( custom.IsValid() ) custom.Delete(); custom = null;
	if ( scene is not null ) { using var scope = scene.Push(); scene.ProcessDeletes(); }
}
try
{
	await Safe(); System.IO.Directory.CreateDirectory( output );
	Require( Model.Sphere is { IsValid: true, IsError: false }, "The independent stock sphere resource is unavailable." );
	material = Material.Load( "materials/default/white.vmat" ).CreateCopy( "native_capture_control_" + id );
	Require( material is { IsValid: true, IsError: false }, "Stock material copy unavailable." ); material.SetFeature( "F_RENDER_BACKFACES", 0 );
	scene = Scene.CreateEditorScene();
	using ( scene.Push() )
	{
		camera = scene.CreateObject().Components.Create<CameraComponent>(); camera.WorldPosition = new Vector3( 200, 0, 0 ); camera.WorldRotation = Rotation.FromYaw( 180 );
		camera.Orthographic = true; camera.OrthographicHeight = 160; camera.ZNear = 1; camera.ZFar = 1000; camera.BackgroundColor = Color.Black; camera.ClearFlags = ClearFlags.All;
		camera.EnablePostProcessing = false; camera.DebugMode = SceneCameraDebugMode.Albedo;
	}
	preview = new Editor.SceneRenderingWidget { Scene = scene, Camera = camera, WindowTitle = "Native empty / sphere / deletion / custom-draw controls", Size = new Vector2( 384, 384 ) };
	onFrame = () => frames++; preview.OnPreFrame += onFrame; preview.Show();
	var vertices = new[]
	{
		new Vertex( new Vector3( 0, -25, -20 ) ) { Normal = Vector3.Forward, Tangent = new Vector4( 0, 1, 0, 1 ), Color = Color32.White },
		new Vertex( new Vector3( 0, 25, -20 ) ) { Normal = Vector3.Forward, Tangent = new Vector4( 0, 1, 0, 1 ), Color = Color32.White },
		new Vertex( new Vector3( 0, -5, 30 ) ) { Normal = Vector3.Forward, Tangent = new Vector4( 0, 1, 0, 1 ), Color = Color32.White }
	};
	foreach ( var definition in new[] { (Name: "baseline_texture_screen_extent", Bitmap: false, ExactSize: false, PostProcess: false, Lit: false), (Name: "texture_exact_camera_extent", Bitmap: false, ExactSize: true, PostProcess: false, Lit: false), (Name: "bitmap_standard_api", Bitmap: true, ExactSize: false, PostProcess: false, Lit: false), (Name: "texture_normal_camera_postprocessing", Bitmap: false, ExactSize: true, PostProcess: true, Lit: false), (Name: "texture_default_visualization_lit", Bitmap: false, ExactSize: true, PostProcess: true, Lit: true) } )
	{
		RemoveObjects(); camera.CustomSize = definition.ExactSize ? new Vector2( size, size ) : null; camera.EnablePostProcessing = definition.PostProcess; camera.DebugMode = definition.Lit ? SceneCameraDebugMode.Normal : SceneCameraDebugMode.Albedo;
		if ( definition.Lit ) light = new ScenePointLight( scene.SceneWorld, new Vector3( 200, -30, 60 ), 1000, Color.White * 4 );
		var emptyBefore = await Capture( definition.Name, "empty_before", definition.Bitmap );
		sphere = new SceneObject( scene.SceneWorld, Model.Sphere, Transform.Zero );
		var visible = await Capture( definition.Name, "stock_sphere", definition.Bitmap );
		RemoveObjects(); var emptyAfterSphere = await Capture( definition.Name, "empty_after_sphere", definition.Bitmap );
		var beforeCalls = System.Threading.Volatile.Read( ref calls ); var beforeActive = System.Threading.Volatile.Read( ref activeCalls );
		custom = new SceneCustomObject( scene.SceneWorld ); custom.Flags.IsOpaque = true; custom.Flags.IsTranslucent = false;
		custom.Bounds = new BBox( new Vector3( -1, -30, -25 ), new Vector3( 1, 30, 35 ) );
		custom.RenderOverride = _ =>
		{
			try { System.Threading.Interlocked.Increment( ref calls ); if ( Graphics.IsActive ) System.Threading.Interlocked.Increment( ref activeCalls ); Graphics.Draw( vertices.AsSpan(), 3, material ); }
			catch ( Exception e ) { callbackErrors.Enqueue( e.ToString() ); }
		};
		var triangle = await Capture( definition.Name, "custom_triangle", definition.Bitmap );
		RemoveObjects(); await Frames(); var afterDeleteCalls = System.Threading.Volatile.Read( ref calls );
		var emptyAfterCustom = await Capture( definition.Name, "empty_after_custom", definition.Bitmap );
		var callsAfterEmpty = System.Threading.Volatile.Read( ref calls );
		var passed = emptyBefore.Coverage == 0 && emptyAfterSphere.Coverage == 0 && emptyAfterCustom.Coverage == 0 && visible.Coverage > 64 && visible.Magenta == 0 && triangle.Coverage > 64 && triangle.Magenta == 0 && afterDeleteCalls > beforeCalls && System.Threading.Volatile.Read( ref activeCalls ) > beforeActive && afterDeleteCalls == callsAfterEmpty && callbackErrors.IsEmpty;
		rows.Add( new { route = definition.Name, passed, emptyBefore = emptyBefore.Coverage, sphereCoverage = visible.Coverage, emptyAfterSphere = emptyAfterSphere.Coverage, customCoverage = triangle.Coverage, emptyAfterCustom = emptyAfterCustom.Coverage, callbacks = afterDeleteCalls - beforeCalls, active_callbacks = System.Threading.Volatile.Read( ref activeCalls ) - beforeActive, deleted_callback_count_stable = afterDeleteCalls == callsAfterEmpty } );
		if ( light.IsValid() ) light.Delete(); light = null;
	}
	phase = "complete";
}
catch ( Exception e ) { failure = e.ToString(); }
finally
{
	await MainThread.Wait(); RemoveObjects(); if ( light.IsValid() ) light.Delete(); light = null;
	if ( preview is not null && preview.IsValid && onFrame is not null ) preview.OnPreFrame -= onFrame;
	if ( preview is not null && preview.IsValid ) { preview.Close(); preview.Destroy(); }
	scene?.Destroy();
	if ( material is not null ) Clean( "owned material", () => typeof( Material ).GetMethod( "Destroy", flags ).Invoke( material, null ) );
}
var accepted = rows.Where( row => (bool)row.GetType().GetProperty( "passed" ).GetValue( row ) ).Select( row => (string)row.GetType().GetProperty( "route" ).GetValue( row ) ).ToArray();
var receipt = new { passed = failure is null && errors.Count == 0 && callbackErrors.IsEmpty && accepted.Any( name => name != "baseline_texture_screen_extent" ), phase, failure, errors, callback_errors = callbackErrors.ToArray(), rows, accepted_native_routes = accepted, captures, output, engine_mvid = engine.ManifestModule.ModuleVersionId, renderer = "standard native camera APIs with existing r_managed_scene=false", source_sha256 = Hash( System.IO.File.ReadAllBytes( System.IO.Path.Combine( checkout.FullName, "engine/Tools/Gltf/editor/editor_native_capture_controls.csx" ) ) ), scope = "Native capture+callback controls only. Existing stock sphere control; no native Model/Mesh construction, shader compile, glTF loading, compute or Model/component parity claim." };
System.IO.File.WriteAllText( System.IO.Path.Combine( output, "receipt.json" ), System.Text.Json.JsonSerializer.Serialize( receipt, new System.Text.Json.JsonSerializerOptions { WriteIndented = true } ) );
return receipt;
