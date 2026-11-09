// Leave a real, isolated model preview open until the user closes its window.
// The attached CLI must remain connected while this cooperative script runs.
// This is user inspection, not an automated acceptance result.
await MainThread.Wait();
if ( !Application.IsEditor || Game.IsPlaying || Graphics.IsActive ) throw new InvalidOperationException( "Open this preview in the idle editor." );
const string caseId = "Fox/glTF";
var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static;
var engine = typeof( Model ).Assembly;
var checkout = new System.IO.DirectoryInfo( System.IO.Path.GetDirectoryName( engine.Location ) );
while ( checkout is not null && !System.IO.File.Exists( System.IO.Path.Combine( checkout.FullName, "engine/Sandbox-Engine.slnx" ) ) ) checkout = checkout.Parent;
if ( checkout is null ) throw new InvalidOperationException( "Cannot locate checkout." );
var runId = Guid.NewGuid().ToString( "N" ); var prefix = "gltf-live-preview/" + runId;
var output = System.IO.Path.Combine( checkout.FullName, "Temp/gltf-validation/editor-animation-preview", runId ); System.IO.Directory.CreateDirectory( output );
var aliases = new System.Collections.Generic.List<string>();
object redirectObject = null; IDisposable redirect = null; BaseFileSystem mount = null, aggregate = null;
System.Reflection.MethodInfo unmount = null, removeRedirect = null;
Scene scene = null; Model model = null; bool owned = false, mounted = false;
Editor.Window window = null; Editor.Widget viewer = null; Action tick = null; Action cleanup = null; Texture target = null; Editor.Pixmap pixmap = null; System.Runtime.Loader.AssemblyLoadContext uiContext = null; System.Reflection.Assembly uiAssembly = null; bool handedOff = false;
string error = null; var cleanupErrors = new System.Collections.Generic.List<string>();
var receiptPath = System.IO.Path.Combine( output, "state.json" );
void Require( bool condition, string message ) { if ( !condition ) throw new InvalidOperationException( message ); }
string Hash( byte[] value ) => Convert.ToHexStringLower( System.Security.Cryptography.SHA256.HashData( value ) );
void Clean( string label, Action action ) { try { action(); } catch ( Exception ex ) { cleanupErrors.Add( label + ": " + ex ); } }
try
{
	using var manifest = System.Text.Json.JsonDocument.Parse( System.IO.File.ReadAllBytes( System.IO.Path.Combine( checkout.FullName, "research/gltf-validation/corpus-case-manifest.json" ) ) );
	var root = manifest.RootElement; Require( root.GetProperty( "corpus" ).GetProperty( "commit" ).GetString() == "90d7ede14c7e280af263824604b427a1ca02cb66", "Unexpected corpus revision." );
	var entry = root.GetProperty( "cases" ).EnumerateArray().Single( value => value.GetProperty( "id" ).GetString() == caseId );
	var corpus = System.IO.Path.Combine( checkout.FullName, "Temp/gltf-reference/glTF-Sample-Assets" ); var sourcePath = entry.GetProperty( "path" ).GetString(); var sourceDirectory = System.IO.Path.GetDirectoryName( sourcePath );
	var redirectType = engine.GetType( "Sandbox.Internal.RedirectFileSystem", true ); redirectObject = redirectType.GetMethod( "Create", flags ).Invoke( null, new object[] { output } ); redirect = (IDisposable)redirectObject; removeRedirect = redirectType.GetMethod( "RemoveAbsFile", flags );
	var constructor = typeof( BaseFileSystem ).GetConstructors( flags ).Single( ctor => { var p = ctor.GetParameters(); return p.Length == 1 && p[0].ParameterType.IsInstanceOfType( redirectObject ); } );
	mount = (BaseFileSystem)constructor.Invoke( new[] { redirectObject } ); aggregate = FileSystem.Mounted;
	unmount = typeof( BaseFileSystem ).GetMethod( "UnMount", flags, null, new[] { typeof( BaseFileSystem ) }, null );
	typeof( BaseFileSystem ).GetMethod( "Mount", flags, null, new[] { typeof( BaseFileSystem ) }, null ).Invoke( aggregate, new object[] { mount } ); mounted = true;
	foreach ( var path in entry.GetProperty( "dependencies" ).EnumerateArray().Select( value => value.GetString() ).Prepend( sourcePath ) )
	{
		var bytes = System.IO.File.ReadAllBytes( System.IO.Path.Combine( corpus, path ) ); Require( bytes.Length <= 32 * 1024 * 1024 && Hash( bytes ) == root.GetProperty( "files" ).GetProperty( path ).GetProperty( "sha256" ).GetString(), "Changed or oversized pinned file." );
		var name = System.IO.Path.GetRelativePath( sourceDirectory, path ).Replace( '\\', '/' ); var destination = System.IO.Path.Combine( output, "input", name ); System.IO.Directory.CreateDirectory( System.IO.Path.GetDirectoryName( destination ) ); System.IO.File.WriteAllBytes( destination, bytes );
		var alias = "/" + prefix + "/" + name; redirectType.GetMethod( "AddAbsFile", flags ).Invoke( redirectObject, new object[] { alias, destination } ); aliases.Add( alias );
	}
	engine.GetType( "NativeEngine.g_pResourceSystem", true ).GetMethod( "InvalidateDatabase", flags ).Invoke( null, null );
	var logical = prefix + "/" + System.IO.Path.GetFileName( sourcePath ); model = Model.Load( logical ); owned = model is { IsValid: true, IsError: false } && string.Equals( model.ResourcePath, logical, StringComparison.OrdinalIgnoreCase );
	Require( owned && model.AnimationCount > 0, "Actual Model.Load did not provide an animated model." );
	scene = Scene.CreateEditorScene(); CameraComponent camera; SkinnedModelRenderer renderer;
	using ( scene.Push() )
	{
		camera = scene.CreateObject().Components.Create<CameraComponent>(); camera.BackgroundColor = new Color( .025f, .025f, .025f ); camera.EnablePostProcessing = false; camera.DebugMode = SceneCameraDebugMode.Albedo; camera.FieldOfView = 50;
		var center = model.RenderBounds.Center; var radius = MathF.Max( model.RenderBounds.Size.Length, .01f ); camera.WorldPosition = center + new Vector3( 1, -1, .65f ).Normal * radius * 2.5f; camera.WorldRotation = Rotation.LookAt( center - camera.WorldPosition ); camera.ZNear = MathF.Max( radius * .0001f, .0001f ); camera.ZFar = radius * 30;
		renderer = scene.CreateObject().Components.Create<SkinnedModelRenderer>(); renderer.Model = model; renderer.UseAnimGraph = false; renderer.PlayAnimationsInEditorScene = true; renderer.PlaybackRate = 1;
		renderer.Sequence.Name = model.AnimationNames.FirstOrDefault( value => value.Equals( "Run", StringComparison.OrdinalIgnoreCase ) ) ?? model.AnimationNames[0]; renderer.Sequence.Looping = true;
		scene.EditorTick( 0, 0 ); Require( renderer.SceneModel.IsValid(), "Default component has no native scene model." );
	}
	var helperSource = """
	using System;
	using Editor;
	public sealed class GltfManagedPreviewWindow : Window
	{
		public Action Tick;
		public Action Cleanup;
		[EditorEvent.Frame] public void Frame() { if ( IsValid ) Tick?.Invoke(); }
		private void Release() { var cleanup = Cleanup; Cleanup = null; Tick = null; cleanup?.Invoke(); }
		protected override void OnClosed() { Release(); base.OnClosed(); }
		public override void OnDestroyed() { Release(); base.OnDestroyed(); }
	}
	""";
	// UI-only helper: no model, animation, vertex or shader producer source is
	// compiled here. QObject/EditorEvent own normal widget callbacks after setup.
	var references = AppDomain.CurrentDomain.GetAssemblies().Where( assembly => !assembly.IsDynamic && System.Runtime.Loader.AssemblyLoadContext.GetLoadContext( assembly ) == System.Runtime.Loader.AssemblyLoadContext.Default && !string.IsNullOrWhiteSpace( assembly.Location ) && System.IO.File.Exists( assembly.Location ) ).GroupBy( assembly => assembly.GetName().Name ).Select( group => Microsoft.CodeAnalysis.MetadataReference.CreateFromFile( group.First().Location ) );
	var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create( "GltfValidationPreviewUi_" + runId,
		new[] { Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText( helperSource ) }, references,
		new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions( Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary ) );
	using var image = new System.IO.MemoryStream(); var emitted = compilation.Emit( image, cancellationToken: cancellationToken ); Require( emitted.Success, string.Join( "\n", emitted.Diagnostics ) );
	uiContext = new System.Runtime.Loader.AssemblyLoadContext( "gltf-preview-ui-" + runId, isCollectible: true );
	uiContext.Resolving += (context, requested) => AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault( assembly => !assembly.IsDynamic && System.Runtime.Loader.AssemblyLoadContext.GetLoadContext( assembly ) == System.Runtime.Loader.AssemblyLoadContext.Default && System.Reflection.AssemblyName.ReferenceMatchesDefinition( assembly.GetName(), requested ) );
	image.Position = 0; uiAssembly = uiContext.LoadFromStream( image ); typeof( Editor.EditorEvent ).GetMethod( "RegisterAssembly", flags ).Invoke( null, new object[] { uiAssembly } );
	window = (Editor.Window)Activator.CreateInstance( uiAssembly.GetType( "GltfManagedPreviewWindow", true ) ); window.WindowTitle = "glTF live managed preview — " + caseId; window.Size = new Vector2( 900, 640 );
	var canvas = new Editor.Widget( window ) { Layout = Editor.Layout.Row() }; window.Canvas = canvas;
	viewer = new Editor.Widget( canvas ) { FixedSize = new Vector2( 512, 512 ) }; canvas.Layout.Add( viewer, 1 );
	pixmap = new Editor.Pixmap( 512, 512 );
	viewer.OnPaintOverride = () => { Editor.Paint.Draw( viewer.LocalRect, pixmap ); return true; };
	var controls = new Editor.Widget( canvas ); controls.Layout = Editor.Layout.Column(); controls.FixedWidth = 280; controls.Layout.Margin = 12;
	controls.Layout.Add( new Editor.Label( "Actual Model.Load · " + caseId, controls ) );
	controls.Layout.Add( new Editor.Label( "Managed renderer inspection. Native renderer parity is unverified.", controls ) );
	var clips = new Editor.ComboBox( controls );
	foreach ( var clipName in model.AnimationNames ) { var name = clipName; clips.AddItem( name, onSelected: () => renderer.Sequence.Name = name, selected: name == renderer.Sequence.Name ); }
	controls.Layout.Add( clips );
	var sheet = new Editor.ControlSheet(); sheet.AddProperty( renderer, value => value.PlaybackRate ); sheet.AddProperty( renderer.Sequence, value => value.Looping ); controls.Layout.Add( sheet ); controls.Layout.AddStretchCell(); canvas.Layout.Add( controls );
	var managedType = engine.GetType( "Sandbox.Rendering.ManagedSceneRendering", true ); var managedViewportBefore = (bool)managedType.GetProperty( "Enabled" ).GetValue( null );
	var bridge = managedType.GetProperty( "Renderer", flags ).GetValue( null ); Require( bridge is not null, "Managed renderer unavailable." );
	var render = bridge.GetType().GetMethod( "RenderToTexture", flags, null, new[] { typeof( CameraComponent ), typeof( Texture ) }, null ); Require( render is not null, "Managed camera render unavailable." );
	var prepareScene = typeof( Scene ).GetMethod( "PreCameraRender", flags ); var prepareCamera = typeof( CameraComponent ).GetMethod( "InitializeRendering", flags );
	target = Texture.CreateRenderTarget().WithSize( 512, 512 ).WithFormat( ImageFormat.RGBA8888 ).Create( "gltf_live_managed_" + runId );
	var sourceHash = root.GetProperty( "files" ).GetProperty( sourcePath ).GetProperty( "sha256" ).GetString();
	var clock = System.Diagnostics.Stopwatch.StartNew(); var previous = clock.Elapsed.TotalSeconds; var attempt = previous; var seconds = 0f; var displayed = 0; var submitted = 0; ulong? pendingFrame = null;
	var visibleHashes = new System.Collections.Generic.HashSet<string>();
	cleanup = () =>
	{
		if ( viewer is not null && viewer.IsValid ) viewer.OnPaintOverride = null;
		if ( scene is not null ) Clean( "private scene", () => scene.Destroy() ); scene = null;
		if ( target is not null ) Clean( "preview render target", () => target.Dispose() ); target = null;
		if ( owned && model is not null ) Clean( "owned model", () => typeof( Model ).GetMethod( "Destroy", flags ).Invoke( model, null ) ); owned = false; model = null;
		if ( mounted ) Clean( "unmount", () => unmount.Invoke( aggregate, new object[] { mount } ) ); mounted = false;
		if ( redirectObject is not null ) foreach ( var alias in aliases ) Clean( "redirect", () => removeRedirect.Invoke( redirectObject, new object[] { alias } ) );
		if ( redirect is not null ) Clean( "redirect filesystem", () => redirect.Dispose() ); redirect = null;
		if ( uiAssembly is not null ) Clean( "UI event assembly", () => typeof( Editor.EditorEvent ).GetMethod( "UnregisterAssembly", flags ).Invoke( null, new object[] { uiAssembly } ) ); uiAssembly = null;
		if ( uiContext is not null ) Clean( "UI helper context", () => uiContext.Unload() ); uiContext = null;
		System.IO.File.WriteAllText( receiptPath, System.Text.Json.JsonSerializer.Serialize( new { inspection_only = true, caseId, open = false, displayed, submitted, cleanup_errors = cleanupErrors, output } ) );
	};
	tick = () =>
	{
		try
		{
			if ( !window.IsValid || Graphics.IsActive ) return;
			var now = clock.Elapsed.TotalSeconds;
			if ( Game.IsPlaying ) { previous = now; return; }
			if ( now - attempt < 1.0 / 20 ) return; attempt = now;
			if ( pendingFrame.HasValue )
			{
				if ( Application.FrameCount < pendingFrame.Value + 2 ) return;
				using var bitmap = target.GetBitmap(); Require( bitmap is not null && bitmap.Width == 512 && bitmap.Height == 512, "Live managed readback failed." );
				Require( pixmap.UpdateFromPixels( bitmap ), "Live preview pixmap update failed." ); viewer.Update(); displayed++; pendingFrame = null;
				if ( visibleHashes.Count < 3 ) visibleHashes.Add( Hash( bitmap.ToPng() ) );
			}
			var delta = (float)Math.Clamp( now - previous, 0, .2 ); previous = now; seconds += delta;
			using ( scene.Push() )
			{
				scene.EditorTick( seconds, delta );
				// The normal viewport/Camera.RenderToTexture performs this preparation.
				// This explicitly managed readback widget has no native viewport pass.
				prepareScene.Invoke( scene, null ); prepareCamera.Invoke( camera, new object[] { false } );
				target.Clear( Color.Black ); Require( (bool)render.Invoke( bridge, new object[] { camera, target } ), "Managed preview render submission failed." );
			}
			pendingFrame = Application.FrameCount; submitted++;
			if ( displayed % 30 == 0 || displayed == 6 ) System.IO.File.WriteAllText( receiptPath, System.Text.Json.JsonSerializer.Serialize( new { inspection_only = true, caseId, open = true, displayed, submitted, distinct_frame_hashes = visibleHashes.Count, selected_clip = renderer.Sequence.Name, time = renderer.Sequence.Time, renderer.PlaybackRate,
				engine_mvid = engine.ManifestModule.ModuleVersionId, renderer_mvid = bridge.GetType().Assembly.ManifestModule.ModuleVersionId, renderer_route = "Explicit managed SceneBridge.RenderToTexture; GPU readback displayed by existing Qt Pixmap", global_managed_renderer_unchanged = managedViewportBefore == (bool)managedType.GetProperty( "Enabled" ).GetValue( null ), source_path = sourcePath, source_sha256 = sourceHash, output } ) );
		}
		catch ( Exception ex ) { error = ex.ToString(); System.IO.File.WriteAllText( System.IO.Path.Combine( output, "error.txt" ), error ); window.Close(); }
	};
	var windowType = window.GetType(); windowType.GetField( "Tick" ).SetValue( window, tick ); windowType.GetField( "Cleanup" ).SetValue( window, cleanup ); window.Show();
	var setupTimer = System.Diagnostics.Stopwatch.StartNew();
	while ( displayed < 6 || visibleHashes.Count < 2 )
	{
		Require( window.IsValid && error is null && setupTimer.Elapsed < TimeSpan.FromSeconds( 12 ), "Live preview did not produce multiple actual managed frames." );
		await Task.Delay( 50, cancellationToken ); await MainThread.Wait();
	}
	handedOff = true;
	return new { inspection_only = true, caseId, open = true, selected_clip = renderer.Sequence.Name, displayed, distinct_frame_hashes = visibleHashes.Count, state_path = receiptPath, output,
		renderer_route = "Explicit managed SceneBridge.RenderToTexture displayed in Qt; no global renderer switch", user_scene_unchanged = true };
}
catch ( Exception ex ) { error = ex.ToString(); }
finally
{
	if ( !handedOff )
	{
		await MainThread.Wait();
		if ( cleanup is not null ) cleanup();
		else
		{
			if ( scene is not null ) Clean( "private scene", () => scene.Destroy() );
			if ( owned ) Clean( "owned model", () => typeof( Model ).GetMethod( "Destroy", flags ).Invoke( model, null ) );
			if ( mounted ) Clean( "unmount", () => unmount.Invoke( aggregate, new object[] { mount } ) );
			if ( redirectObject is not null ) foreach ( var alias in aliases ) Clean( "redirect", () => removeRedirect.Invoke( redirectObject, new object[] { alias } ) );
			if ( redirect is not null ) Clean( "redirect filesystem", () => redirect.Dispose() );
		}
		if ( window is not null && window.IsValid ) Clean( "window", () => { window.Close(); window.Destroy(); } );
	}
}
var closed = new { inspection_only = true, caseId, open = false, error, cleanup_errors = cleanupErrors, output }; System.IO.File.WriteAllText( receiptPath, System.Text.Json.JsonSerializer.Serialize( closed ) ); return closed;
