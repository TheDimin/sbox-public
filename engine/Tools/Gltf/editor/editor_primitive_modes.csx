// Actual Model.Load topology acceptance in an already-running editor.
// Private fixtures/scenes only; the direct native models are independent controls.
var engineLocation = typeof( Model ).Assembly.Location;
var root = new System.IO.DirectoryInfo( System.IO.Path.GetDirectoryName( engineLocation ) );
while ( root is not null && !System.IO.File.Exists( System.IO.Path.Combine( root.FullName, "engine", "Sandbox-Engine.slnx" ) ) ) root = root.Parent;
if ( root is null ) throw new InvalidOperationException( "Cannot locate checkout from loaded engine: " + engineLocation );
var modeSelection = Environment.GetEnvironmentVariable( "SBOX_GLTF_PRIMITIVE_MODE" );
if ( string.IsNullOrWhiteSpace( modeSelection ) ) modeSelection = "all";
var formatSelection = Environment.GetEnvironmentVariable( "SBOX_GLTF_PRIMITIVE_FORMAT" );
if ( string.IsNullOrWhiteSpace( formatSelection ) ) formatSelection = "both";
var directionSelection = Environment.GetEnvironmentVariable( "SBOX_GLTF_PRIMITIVE_DIRECTIONS" );
if ( string.IsNullOrWhiteSpace( directionSelection ) ) directionSelection = "all";
var supportedModes = new[] { "0", "1", "2", "3", "4", "5", "6", "all" };
if ( !supportedModes.Contains( modeSelection, StringComparer.Ordinal ) || !new[] { "source", "compiled", "both" }.Contains( formatSelection, StringComparer.Ordinal ) ||
	!new[] { "authored", "normal-only", "no-normal", "all" }.Contains( directionSelection, StringComparer.Ordinal ) )
	throw new ArgumentException( "Invalid primitive mode, format or direction selection." );
var managedCapture = Environment.GetEnvironmentVariable( "SBOX_GLTF_CAPTURE_RENDERER" ) == "managed";
var diagnosticClear = Environment.GetEnvironmentVariable( "SBOX_GLTF_CAMERA_CLEAR" ) == "1";
var runId = Guid.NewGuid().ToString( "N" );
var virtualBase = "gltf-primitive-modes/" + runId;
var outputGroup = "editor-primitive-modes" + (diagnosticClear ? "-camera-clear" : "") + (managedCapture ? "-managed" : "");
var output = System.IO.Path.Combine( root.FullName, "Temp", "gltf-validation", outputGroup, runId );
System.IO.Directory.CreateDirectory( output );
var receiptPath = System.IO.Path.Combine( output, "receipt.json" );
var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static;
var cases = new System.Collections.Generic.List<object>();
var captures = new System.Collections.Generic.List<object>();
var fixtureHashes = new System.Collections.Generic.Dictionary<string, string>();
var cleanupErrors = new System.Collections.Generic.List<string>();
var redirects = new System.Collections.Generic.List<string>();
var ownedModels = new System.Collections.Generic.HashSet<Model>();
var referenceModels = new System.Collections.Generic.List<Model>();
var referenceMeshes = new System.Collections.Generic.List<Mesh>();
var sceneObjects = new System.Collections.Generic.List<SceneModel>();
var selectedModes = modeSelection == "all" ? Enumerable.Range( 0, 7 ).ToArray() : new[] { int.Parse( modeSelection, System.Globalization.CultureInfo.InvariantCulture ) };
var selectedFormats = formatSelection == "both" ? new[] { "source", "compiled" } : new[] { formatSelection };
var selectedDirections = directionSelection == "all" ? new[] { "authored", "normal-only", "no-normal" } : new[] { directionSelection };
var expectedCases = selectedModes.Length * selectedFormats.Length * selectedDirections.Length;
var passedCases = 0;
var startFrame = Application.FrameCount;
var phase = "initialize";
var frameCount = 0;
var loadCalls = 0;
string error = null;
object redirectObject = null;
IDisposable redirect = null;
BaseFileSystem managedMount = null, mountedAggregate = null;
System.Reflection.MethodInfo unmountManaged = null, removeRedirect = null, managedRenderToTexture = null;
object managedRenderer = null;
bool mountedManaged = false;
bool? managedViewportEnabled = null;
Scene scene = null;
Editor.SceneRenderingWidget preview = null;
CameraComponent camera = null;
Sandbox.Rendering.CommandList clearCommand = null;
Texture target = null;
Material referenceMaterial = null;
Action onPreviewFrame = null;
string Hash( byte[] bytes ) => Convert.ToHexStringLower( System.Security.Cryptography.SHA256.HashData( bytes ) );
void Require( bool condition, string message ) { if ( !condition ) throw new InvalidOperationException( message ); }
void Cleanup( string name, Action action ) { try { action(); } catch ( Exception e ) { cleanupErrors.Add( name + ": " + e ); } }
bool Valid( Model value ) => value is { IsValid: true } && !value.IsError && !ReferenceEquals( value, Model.Error );
bool Different( Color a, Color b ) => MathF.Abs( a.r - b.r ) > .03f || MathF.Abs( a.g - b.g ) > .03f || MathF.Abs( a.b - b.b ) > .03f;
int Difference( Color[] a, Color[] b ) => a.Zip( b, ( x, y ) => Different( x, y ) ).Count( x => x );
int Nonblack( Color[] a ) => a.Count( x => Different( x, Color.Black ) );

try
{
	await MainThread.Wait();
	Require( Application.IsEditor && !Graphics.IsActive, "Run only inside the editor on its main thread outside a Graphics scope." );
	var renderingType = typeof( Model ).Assembly.GetType( "Sandbox.Rendering.ManagedSceneRendering", true );
	managedViewportEnabled = (bool)renderingType.GetProperty( "Enabled" ).GetValue( null );
	if ( managedCapture )
	{
		managedRenderer = renderingType.GetProperty( "Renderer" ).GetValue( null ) ?? throw new InvalidOperationException( "Managed renderer unavailable." );
		managedRenderToTexture = managedRenderer.GetType().GetMethod( "RenderToTexture", new[] { typeof( CameraComponent ), typeof( Texture ) } ) ?? throw new InvalidOperationException( "Managed texture capture unavailable." );
	}
	phase = "create-fixtures";
	// glTF source geometry is expressed in meters; six points allow distinct
	// closure, strip winding and fan triangles. Direction variants explicitly
	// test supplied frames, missing tangents, and ignored tangents without normals.
	float[] sourcePositions = [ -.7f,-.6f,0, .7f,-.6f,0, -.7f,.6f,0, .7f,.6f,0, 0,.85f,0, 0,-.85f,0 ];
	ushort[][] sourceIndices = [ [0,1,4], [0,1,2,3,4,5], [0,1,3,2], [0,1,3,2], [0,1,2,3,4,5], [0,1,2,3], [0,1,3,4,2] ];
	// Hand-authored native draw-list expectations, not derived from the glTF
	// topology-expansion helper. Removing the final primitive is a visibility control.
	int[][] expectedIndices = [ [0,1,4], [0,1,2,3,4,5], [0,1,1,3,3,2,2,0], [0,1,1,3,3,2], [0,1,2,3,4,5], [0,1,2,2,1,3], [0,1,3,0,3,4,0,4,2] ];
	var labels = new[] { "points", "lines", "line_loop", "line_strip", "triangles", "triangle_strip", "triangle_fan" };
	var files = new System.Collections.Generic.Dictionary<string, byte[]>( StringComparer.Ordinal );
	foreach ( var mode in selectedModes )
	foreach ( var direction in selectedDirections )
	{
		using var bytes = new System.IO.MemoryStream();
		using ( var writer = new System.IO.BinaryWriter( bytes, System.Text.Encoding.UTF8, leaveOpen: true ) )
		{
			foreach ( var value in sourcePositions ) writer.Write( value );
			for ( var i = 0; i < 6; i++ ) { writer.Write( 0f ); writer.Write( 0f ); writer.Write( 1f ); }
			for ( var i = 0; i < 6; i++ ) { writer.Write( 1f ); writer.Write( 0f ); writer.Write( 0f ); writer.Write( 1f ); }
			foreach ( var index in sourceIndices[mode] ) writer.Write( index );
		}
		var data = bytes.ToArray();
		var encoded = Convert.ToBase64String( data );
		var directionAttributes = direction switch { "authored" => ",\"NORMAL\":1,\"TANGENT\":2", "normal-only" => ",\"NORMAL\":1", _ => ",\"TANGENT\":2" };
		var json = $$$"""
			{"asset":{"version":"2.0"},"buffers":[{"uri":"data:application/octet-stream;base64,{{{encoded}}}","byteLength":{{{data.Length}}} }],
			 "bufferViews":[{"buffer":0,"byteOffset":0,"byteLength":72},{"buffer":0,"byteOffset":72,"byteLength":72},{"buffer":0,"byteOffset":144,"byteLength":96},{"buffer":0,"byteOffset":240,"byteLength":{{{sourceIndices[mode].Length * 2}}} }],
			 "accessors":[{"bufferView":0,"componentType":5126,"count":6,"type":"VEC3","min":[-0.7,-0.85,0],"max":[0.7,0.85,0]},
			  {"bufferView":1,"componentType":5126,"count":6,"type":"VEC3"},{"bufferView":2,"componentType":5126,"count":6,"type":"VEC4"},
			  {"bufferView":3,"componentType":5123,"count":{{{sourceIndices[mode].Length}}},"type":"SCALAR"}],
			 "meshes":[{"primitives":[{"attributes":{"POSITION":0{{{directionAttributes}}} },"indices":3,"mode":{{{mode}}} }]}],
			 "nodes":[{"mesh":0}],"scenes":[{"nodes":[0]}],"scene":0}
			""";
		var source = System.Text.Encoding.UTF8.GetBytes( json );
		files.Add( "mode-" + mode + "-" + direction + ".gltf", source );
		files.Add( "compiled-only-" + mode + "-" + direction + ".glb_c", Sandbox.Gltf.GltfCompiler.Compile( source ) );
	}
	var redirectType = typeof( Model ).Assembly.GetType( "Sandbox.Internal.RedirectFileSystem", true );
	redirectObject = redirectType.GetMethod( "Create", flags ).Invoke( null, new object[] { output } );
	redirect = (IDisposable)redirectObject;
	var addRedirect = redirectType.GetMethod( "AddAbsFile", flags ); removeRedirect = redirectType.GetMethod( "RemoveAbsFile", flags );
	foreach ( var file in files )
	{
		var path = System.IO.Path.Combine( output, file.Key ); System.IO.File.WriteAllBytes( path, file.Value );
		fixtureHashes.Add( file.Key, Hash( file.Value ) );
		var alias = "/" + virtualBase + "/" + file.Key;
		addRedirect.Invoke( redirectObject, new object[] { alias, path } ); redirects.Add( alias );
	}
	var wrapper = typeof( BaseFileSystem ).GetConstructors( flags ).Single( ctor =>
	{
		var parameters = ctor.GetParameters(); return parameters.Length == 1 && parameters[0].ParameterType.IsInstanceOfType( redirectObject );
	} );
	managedMount = (BaseFileSystem)wrapper.Invoke( new[] { redirectObject } );
	mountedAggregate = Sandbox.FileSystem.Mounted ?? throw new InvalidOperationException( "Managed mounted filesystem unavailable." );
	var mount = typeof( BaseFileSystem ).GetMethod( "Mount", flags, null, new[] { typeof( BaseFileSystem ) }, null );
	unmountManaged = typeof( BaseFileSystem ).GetMethod( "UnMount", flags, null, new[] { typeof( BaseFileSystem ) }, null );
	Require( mount is not null && unmountManaged is not null, "Managed mount APIs unavailable." );
	mount.Invoke( mountedAggregate, new object[] { managedMount } ); mountedManaged = true;
	foreach ( var file in files )
		Require( mountedAggregate.FileExists( virtualBase + "/" + file.Key ) && Hash( mountedAggregate.ReadAllBytes( virtualBase + "/" + file.Key ).ToArray() ) == fixtureHashes[file.Key], "Mounted source mismatch: " + file.Key );
	foreach ( var mode in selectedModes )
	foreach ( var direction in selectedDirections )
		Require( !mountedAggregate.FileExists( virtualBase + "/compiled-only-" + mode + "-" + direction + ".glb" ) && !mountedAggregate.FileExists( virtualBase + "/compiled-only-" + mode + "-" + direction + ".gltf" ), "Compiled fixture has a source sibling." );
	typeof( Model ).Assembly.GetType( "NativeEngine.g_pResourceSystem", true ).GetMethod( "InvalidateDatabase", flags ).Invoke( null, null );

	phase = "create-reference-preview";
	var stock = Material.Load( "materials/default/white.vmat" );
	Require( stock is { IsValid: true } && !stock.IsError && stock.ShaderName.Contains( "complex", StringComparison.OrdinalIgnoreCase ), "Stock complex material unavailable." );
	referenceMaterial = stock.CreateCopy( "gltf_primitive_reference_" + runId );
	Require( referenceMaterial is { IsValid: true } && !referenceMaterial.IsError, "Could not copy stock material." );
	referenceMaterial.SetFeature( "F_RENDER_BACKFACES", 0 ); referenceMaterial.SetFeature( "F_METALNESS_TEXTURE", 0 );
	referenceMaterial.SetFeature( "F_ALPHA_TEST", 0 ); referenceMaterial.SetFeature( "F_TRANSLUCENT", 0 );
	referenceMaterial.Set( "g_vColorTint", new Vector4( 1, 1, 1, 1 ) );
	referenceMaterial.Set( "g_flMetalness", 1f ); referenceMaterial.Set( "g_flRoughnessScaleFactor", 0f );
	const float unit = 1f / .0254f;
	// Explicit independent reference positions in s&box axes; no GltfGeometry conversion.
	var referencePositions = new[] { new Vector3( 0,-.7f*unit,-.6f*unit ), new Vector3( 0,.7f*unit,-.6f*unit ), new Vector3( 0,-.7f*unit,.6f*unit ), new Vector3( 0,.7f*unit,.6f*unit ), new Vector3( 0,0,.85f*unit ), new Vector3( 0,0,-.85f*unit ) };
	Model Reference( int mode, bool omitLast )
	{
		var kind = mode == 0 ? MeshPrimitiveType.Points : mode <= 3 ? MeshPrimitiveType.Lines : MeshPrimitiveType.Triangles;
		var remove = mode == 0 ? 1 : mode <= 3 ? 2 : 3;
		var indices = omitLast ? expectedIndices[mode].Take( expectedIndices[mode].Length - remove ).ToArray() : expectedIndices[mode];
		var vertices = referencePositions.Select( p => new Vertex { Position = p, Normal = Vector3.Forward, Tangent = new Vector4( 0, 1, 0, 1 ), Color = Color32.White } ).ToArray();
		var mesh = new Mesh( "gltf_primitive_reference", referenceMaterial, kind );
		referenceMeshes.Add( mesh );
		mesh.CreateVertexBuffer( vertices.Length, vertices ); mesh.CreateIndexBuffer( indices.Length, indices );
		var bounds = new BBox( new Vector3( -1, -50, -50 ), new Vector3( 1, 50, 50 ) ); mesh.Bounds = bounds;
		var model = Model.Builder.WithName( "gltf_primitive_reference_" + mode + "_" + omitLast + "_" + runId ).AddMesh( mesh ).WithViewBounds( bounds ).Create();
		Require( Valid( model ), "Native reference model is invalid." ); referenceModels.Add( model ); return model;
	}
	scene = Scene.CreateEditorScene();
	using ( scene.Push() )
	{
		camera = scene.CreateObject().Components.Create<CameraComponent>(); camera.WorldPosition = new Vector3( 130, 0, 0 );
		camera.WorldRotation = Rotation.FromYaw( 180 ); camera.BackgroundColor = Color.Black; camera.ZNear = 1; camera.FieldOfView = 50;
		camera.EnablePostProcessing = false; camera.DebugMode = SceneCameraDebugMode.Albedo;
	}
	if ( diagnosticClear )
	{
		clearCommand = new Sandbox.Rendering.CommandList( "glTF primitive diagnostic camera color clear" );
		clearCommand.Clear( Color.Black, true, false, false ); camera.AddCommandList( clearCommand, Sandbox.Rendering.Stage.AfterDepthPrepass );
	}
	preview = new Editor.SceneRenderingWidget { Scene = scene, Camera = camera, WindowTitle = "glTF primitive mode capability", Size = new Vector2( 384, 384 ) };
	onPreviewFrame = () => frameCount++; preview.OnPreFrame += onPreviewFrame; preview.Show();
	target = Texture.CreateRenderTarget().WithSize( 128, 128 ).WithFormat( ImageFormat.RGBA8888 ).Create( "gltf_primitive_capture_" + runId );
	async Task WaitFrames()
	{
		var start = frameCount; var timer = System.Diagnostics.Stopwatch.StartNew();
		while ( frameCount < start + 3 )
		{
			cancellationToken.ThrowIfCancellationRequested();
			if ( timer.Elapsed > TimeSpan.FromSeconds( 5 ) ) throw new TimeoutException( $"Preview stalled at {frameCount} callbacks after {start}." );
			await Task.Delay( 50, cancellationToken ); await MainThread.Wait();
		}
	}
	async Task<Color[]> Capture( string label, SceneCameraDebugMode debugMode = SceneCameraDebugMode.Albedo )
	{
		phase = label; camera.DebugMode = debugMode; await WaitFrames(); var submittedAt = frameCount;
		using ( scene.Push() )
		{
			target.Clear( Color.Black );
			var submitted = managedCapture ? (bool)managedRenderToTexture.Invoke( managedRenderer, new object[] { camera, target } ) : camera.RenderToTexture( target );
			if ( !submitted ) throw new InvalidOperationException( "Capture submit failed: " + label );
		}
		await WaitFrames(); cancellationToken.ThrowIfCancellationRequested();
		using var bitmap = target.GetBitmap(); Require( bitmap is not null, "Capture readback failed: " + label );
		var png = bitmap.ToPng(); var path = System.IO.Path.Combine( output, label + ".png" ); System.IO.File.WriteAllBytes( path, png );
		captures.Add( new { label, mode = (int)debugMode, path, sha256 = Hash( png ), frame = Application.FrameCount, preview_frames_at_submit = submittedAt, preview_frames_at_readback = frameCount } );
		return bitmap.GetPixels();
	}
	async Task<(Color[] Pixels, Color[] Empty)> Render( Model model, string label, SceneCameraDebugMode debugMode = SceneCameraDebugMode.Albedo )
	{
		var obj = new SceneModel( scene.SceneWorld, model, Transform.Zero ); sceneObjects.Add( obj );
		Color[] pixels;
		try { pixels = await Capture( label, debugMode ); }
		finally { if ( obj.IsValid() ) obj.Delete(); sceneObjects.Remove( obj ); }
		var empty = await Capture( label + "-empty", debugMode );
		return (pixels, empty);
	}
	var backgroundInitial = await Capture( "background-initial" );
	var background = await Capture( "background" );
	var initialControlsPass = Nonblack( backgroundInitial ) == 0 && Nonblack( background ) == 0 && Difference( backgroundInitial, background ) <= 5;
	foreach ( var mode in selectedModes )
	{
		string referenceError = null, normalReferenceError = null;
		Model reference = null;
		var refDraws = Array.Empty<Model.ModelMeshInfo.DrawCallData>();
		(Color[] Pixels, Color[] Empty) referenceFrame = (background, background), omissionFrame = (background, background), referenceNormals = (background, background);
		try
		{
			reference = Reference( mode, false ); var omitted = Reference( mode, true );
			refDraws = reference.MeshInfo.Meshes.SelectMany( x => x.DrawCalls ).ToArray();
			referenceFrame = await Render( reference, labels[mode] + "-reference" );
			omissionFrame = await Render( omitted, labels[mode] + "-omission" );
		}
		catch ( OperationCanceledException ) { throw; }
		catch ( Exception e ) { referenceError = e.ToString(); }
		if ( reference is not null && (mode >= 4 || selectedDirections.Any( x => x != "no-normal" )) )
		{
			try { referenceNormals = await Render( reference, labels[mode] + "-reference-normal", SceneCameraDebugMode.NormalMap ); }
			catch ( OperationCanceledException ) { throw; }
			catch ( Exception e ) { normalReferenceError = e.ToString(); }
		}
		var referenceCoverage = Difference( referenceFrame.Pixels, background );
		var omittedCoverage = Difference( referenceFrame.Pixels, omissionFrame.Pixels );
		var referenceEmptyDiff = Difference( referenceFrame.Empty, background );
		var omissionEmptyDiff = Difference( omissionFrame.Empty, background );
		var referenceNormalCoverage = Nonblack( referenceNormals.Pixels );
		var minimumCoverage = mode == 0 ? 3 : mode <= 3 ? 6 : 30;
		var minimumOmission = mode == 0 ? 1 : mode <= 3 ? 2 : 10;
		var referenceVisible = referenceCoverage >= minimumCoverage && omittedCoverage >= minimumOmission && refDraws.Length > 0;
		foreach ( var direction in selectedDirections )
		foreach ( var format in selectedFormats )
		{
			cancellationToken.ThrowIfCancellationRequested();
			var filename = format == "source" ? "mode-" + mode + "-" + direction + ".gltf" : "compiled-only-" + mode + "-" + direction + ".glb";
			var path = virtualBase + "/" + filename;
			var result = new System.Collections.Generic.Dictionary<string, object>
			{
				["mode"] = mode, ["topology"] = labels[mode], ["format"] = format, ["directions"] = direction, ["asset_path"] = path, ["passed"] = false,
				["reference_coverage"] = referenceCoverage, ["omission_difference"] = omittedCoverage, ["native_reference_visible"] = referenceVisible,
				["reference_error"] = referenceError, ["normal_reference_error"] = normalReferenceError, ["reference_normal_coverage"] = referenceNormalCoverage, ["reference_empty_difference"] = referenceEmptyDiff, ["omission_empty_difference"] = omissionEmptyDiff,
				["normal_direction_policy"] = direction == "no-normal" && mode <= 3 ? "unlit recommendation not yet tested" : "match independent +X reference"
			};
			try
			{
				loadCalls++;
				var loaded = Model.Load( path );
				result["model_null"] = loaded is null; result["model_valid"] = loaded?.IsValid; result["model_error"] = loaded?.IsError;
				Require( Valid( loaded ), "Actual Model.Load returned no valid model." ); ownedModels.Add( loaded );
				Require( referenceError is null, "Native reference setup failed; loaded topology cannot be visually verified." );
				var loadedDraws = loaded.MeshInfo.Meshes.SelectMany( x => x.DrawCalls ).ToArray();
				var metadataMatches = loadedDraws.Length == refDraws.Length && loadedDraws.Length == 1 && loadedDraws[0].PrimitiveType == refDraws[0].PrimitiveType && loadedDraws[0].Indices == expectedIndices[mode].Length;
				result["draw_types"] = loadedDraws.Select( x => x.PrimitiveType ).ToArray(); result["draw_indices"] = loadedDraws.Select( x => x.Indices ).ToArray();
				result["draw_metadata_matches"] = metadataMatches;
				var prefix = labels[mode] + "-" + direction + "-" + format;
				var actual = await Render( loaded, prefix );
				var actualCoverage = Difference( actual.Pixels, background );
				var pixelsDiffer = Difference( referenceFrame.Pixels, actual.Pixels );
				var emptyDifference = Difference( actual.Empty, background );
				var allowedDifference = mode == 0 ? 0 : mode <= 3 ? 2 : 5;
				result["actual_coverage"] = actualCoverage; result["reference_difference"] = pixelsDiffer; result["allowed_difference"] = allowedDifference;
				result["actual_empty_difference"] = emptyDifference;
				Require( initialControlsPass && referenceVisible && referenceEmptyDiff <= 5 && omissionEmptyDiff <= 5 && Nonblack( referenceFrame.Empty ) == 0 && Nonblack( omissionFrame.Empty ) == 0,
					"Native reference or empty controls did not establish observable topology; no support conclusion is possible." );
				Require( actualCoverage >= minimumCoverage && pixelsDiffer <= allowedDifference && emptyDifference <= 5 && Nonblack( actual.Empty ) == 0,
					"Loaded topology differs from the independently authored native reference or empty control." );
				Require( metadataMatches, "Loaded draw topology or expanded index count differs from the independent native draw." );
				if ( direction != "no-normal" || mode >= 4 )
				{
					Require( normalReferenceError is null && referenceNormalCoverage >= minimumCoverage && Nonblack( referenceNormals.Empty ) == 0, "The independent normal reference or its empty control failed." );
					var actualNormal = await Render( loaded, prefix + "-normal", SceneCameraDebugMode.NormalMap );
					var actualNormalCoverage = Nonblack( actualNormal.Pixels );
					var normalDifference = Difference( referenceNormals.Pixels, actualNormal.Pixels );
					result["actual_normal_coverage"] = actualNormalCoverage; result["normal_reference_difference"] = normalDifference;
					Require( actualNormalCoverage >= minimumCoverage && Nonblack( actualNormal.Empty ) == 0, "The loaded stock-shader normal frame is not observable or its empty control failed." );
					Require( normalDifference <= allowedDifference, "The loaded world-normal frame differs from the independently authored +X reference." );
				}
				result["passed"] = true; passedCases++;
			}
			catch ( OperationCanceledException ) { throw; }
			catch ( Exception e ) { result["error"] = e.ToString(); }
			cases.Add( result );
		}
	}
	phase = "complete";
}
catch ( OperationCanceledException e ) { error = "Cancelled: " + e; }
catch ( Exception e ) { error = e.ToString(); }
finally
{
	await MainThread.Wait();
	if ( preview is not null && preview.IsValid && onPreviewFrame is not null ) Cleanup( "preview callback", () => preview.OnPreFrame -= onPreviewFrame );
	if ( camera.IsValid() && clearCommand is not null ) Cleanup( "camera command", () => camera.RemoveCommandList( clearCommand ) );
	if ( preview is not null && preview.IsValid ) Cleanup( "preview", () => { preview.Close(); preview.Destroy(); } );
	foreach ( var obj in sceneObjects ) if ( obj.IsValid() ) Cleanup( "scene model", () => obj.Delete() );
	if ( scene is not null ) Cleanup( "scene", () => scene.Destroy() );
	if ( target is not null ) Cleanup( "target", () => target.Dispose() );
	var destroyModel = typeof( Model ).GetMethod( "Destroy", flags );
	foreach ( var model in referenceModels ) if ( Valid( model ) ) Cleanup( "reference model", () => destroyModel.Invoke( model, null ) );
	foreach ( var model in ownedModels ) if ( Valid( model ) && !ReferenceEquals( model, Model.Sphere ) ) Cleanup( "loaded model", () => destroyModel.Invoke( model, null ) );
	// Model.Destroy queues native-handle release; drain the main-thread queue first.
	await Task.Delay( 100 ); await MainThread.Wait();
	if ( referenceMeshes.Count > 0 ) Cleanup( "reference meshes", () => typeof( Model ).Assembly.GetType( "Sandbox.GltfModelLoader", true ).GetMethod( "ReleaseMeshes", flags ).Invoke( null, new object[] { referenceMeshes } ) );
	if ( referenceMaterial is not null && referenceMaterial.IsValid ) Cleanup( "reference material", () => typeof( Material ).GetMethod( "Destroy", flags ).Invoke( referenceMaterial, null ) );
	if ( mountedManaged ) Cleanup( "managed mount", () => unmountManaged.Invoke( mountedAggregate, new object[] { managedMount } ) );
	if ( removeRedirect is not null ) foreach ( var alias in redirects ) Cleanup( "redirect " + alias, () => removeRedirect.Invoke( redirectObject, new object[] { alias } ) );
	if ( redirect is not null ) Cleanup( "redirect filesystem", () => redirect.Dispose() );
}
var receipt = new
{
	passed = error is null && phase == "complete" && cleanupErrors.Count == 0 && cases.Count == expectedCases && passedCases == expectedCases,
	scope = "actual editor Model.Load primitive topology and stock albedo comparisons; native reference visibility is required; compiled fixtures are packaging-only, not GPU-optimized",
	mode_selection = modeSelection, format_selection = formatSelection, direction_selection = directionSelection, expected_cases = expectedCases, passed_cases = passedCases, load_calls = loadCalls,
	phase, error, cases, captures, fixture_sha256 = fixtureHashes, cleanup_errors = cleanupErrors,
	capture_renderer = managedCapture ? "managed SceneBridge.RenderToTexture" : "native CameraComponent.RenderToTexture", managed_viewport_enabled = managedViewportEnabled, diagnostic_camera_clear = diagnosticClear,
	preview_frames = frameCount, managed_redirect_mounted = mountedManaged, virtual_base = virtualBase,
	engine_location = engineLocation, engine_mvid = typeof( Model ).Assembly.ManifestModule.ModuleVersionId.ToString(), gltf_mvid = typeof( Sandbox.Gltf.GltfReader ).Assembly.ManifestModule.ModuleVersionId.ToString(),
	is_editor = Application.IsEditor, starting_frame = startFrame, final_frame = Application.FrameCount, output
};
System.IO.File.WriteAllText( receiptPath, System.Text.Json.JsonSerializer.Serialize( receipt, new System.Text.Json.JsonSerializerOptions { WriteIndented = true } ) );
return receipt;
