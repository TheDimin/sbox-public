// Run as an async method body inside the already-running s&box editor.
// Private scene, widget and resources only. This is a position-morph capability
// probe, not glTF loader integration or proof of full deformation conformance.
var engineLocation = typeof( Model ).Assembly.Location;
var root = new System.IO.DirectoryInfo( System.IO.Path.GetDirectoryName( engineLocation ) );
while ( root is not null && !System.IO.File.Exists( System.IO.Path.Combine( root.FullName, "engine", "Sandbox-Engine.slnx" ) ) ) root = root.Parent;
if ( root is null ) throw new InvalidOperationException( "Could not locate the checkout from loaded engine: " + engineLocation );
var diagnosticCameraClear = Environment.GetEnvironmentVariable( "SBOX_GLTF_CAMERA_CLEAR" ) == "1";
var diagnosticSignedOverride = Environment.GetEnvironmentVariable( "SBOX_GLTF_SIGNED_MORPH_OVERRIDE" ) == "1";
var diagnosticPairedMorph = Environment.GetEnvironmentVariable( "SBOX_GLTF_PAIRED_MORPH" ) == "1";
var output = System.IO.Path.Combine( root.FullName, "Temp", "gltf-validation", "editor-morph" + (diagnosticCameraClear ? "-camera-clear" : "") + (diagnosticSignedOverride ? "-signed-override" : "") + (diagnosticPairedMorph ? "-paired" : "") );
System.IO.Directory.CreateDirectory( output );
var receiptPath = System.IO.Path.Combine( output, "receipt.json" );
var phase = "initialize";
var completed = new System.Collections.Generic.List<string>();
var failures = new System.Collections.Generic.List<string>();
var cleanupErrors = new System.Collections.Generic.List<string>();
var captures = new System.Collections.Generic.List<object>();
var cases = new System.Collections.Generic.List<object>();
var transitions = new System.Collections.Generic.List<object>();
var models = new System.Collections.Generic.List<Model>();
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
Scene scene = null;
Editor.SceneRenderingWidget preview = null;
SceneModel shown = null;
Texture target = null;
Material material = null;
CameraComponent commandCamera = null;
Sandbox.Rendering.CommandList cameraClearCommands = null;
Action onPreviewFrame = null;
Exception callbackError = null;
var previewFrames = 0;
var updateCalls = 0;
var activeMorph = false;
var controllerAvailable = false;
var requestedWeight = 0f;
var morphNames = Array.Empty<string>();
var morphCount = -1;
var startFrame = Application.FrameCount;
string error = null;
string sourceHash = null;
string positiveTwoHash = null;
string negativeTwoHash = null;
bool positivePassed = false;
bool negativePassed = false;
bool controlsPassed = false;
bool rangePassed = false;
bool transitionsPassed = false;
string Hash( byte[] bytes ) => Convert.ToHexStringLower( System.Security.Cryptography.SHA256.HashData( bytes ) );
void Require( bool condition, string message ) { if ( !condition ) throw new InvalidOperationException( message ); }
void Cleanup( string name, Action action ) { try { action(); } catch ( Exception e ) { cleanupErrors.Add( name + ": " + e ); } }
bool Different( Color a, Color b ) => MathF.Abs( a.r - b.r ) > .03f || MathF.Abs( a.g - b.g ) > .03f || MathF.Abs( a.b - b.b ) > .03f;
int Difference( Color[] a, Color[] b ) => a.Zip( b, ( x, y ) => Different( x, y ) ).Count( x => x );
int Nonblack( Color[] pixels ) => pixels.Count( x => Different( x, Color.Black ) );

try
{
	await MainThread.Wait();
	Require( Application.IsEditor, "This proof must run inside an s&box editor." );
	Require( !Graphics.IsActive, "Preview setup must be outside a Graphics scope." );
	Require( !(diagnosticSignedOverride && diagnosticPairedMorph), "Choose either the signed-override or paired-target diagnostic." );
	// Self-contained deterministic glTF: triangle, one +X position delta and
	// STEP-free LINEAR weight samples at times 0/1/2 with weights 0/+1/-1.
	const string json = """{"asset":{"version":"2.0"},"buffers":[{"uri":"data:application/octet-stream;base64,mpkZvwAAAL8AAAAAmpkZPwAAAL8AAAAAAAAAAAAAAD8AAAAAAAAAAAAAAAAAAIA/AAAAAAAAAAAAAIA/AAAAAAAAAAAAAIA/AACAPgAAAAAAAAAAAACAPgAAAAAAAAAAAACAPgAAAAAAAAAAAAAAAAAAgD8AAABAAAAAAAAAgD8AAIC/","byteLength":132}],"bufferViews":[{"buffer":0,"byteOffset":0,"byteLength":36},{"buffer":0,"byteOffset":36,"byteLength":36},{"buffer":0,"byteOffset":72,"byteLength":36},{"buffer":0,"byteOffset":108,"byteLength":12},{"buffer":0,"byteOffset":120,"byteLength":12}],"accessors":[{"bufferView":0,"componentType":5126,"count":3,"type":"VEC3","min":[-0.6,-0.5,0],"max":[0.6,0.5,0]},{"bufferView":1,"componentType":5126,"count":3,"type":"VEC3"},{"bufferView":2,"componentType":5126,"count":3,"type":"VEC3","min":[0.25,0,0],"max":[0.25,0,0]},{"bufferView":3,"componentType":5126,"count":3,"type":"SCALAR","min":[0],"max":[2]},{"bufferView":4,"componentType":5126,"count":3,"type":"SCALAR"}],"meshes":[{"weights":[0],"primitives":[{"attributes":{"POSITION":0,"NORMAL":1},"targets":[{"POSITION":2}]}]}],"nodes":[{"mesh":0}],"scenes":[{"nodes":[0]}],"scene":0,"animations":[{"samplers":[{"input":3,"output":4}],"channels":[{"sampler":0,"target":{"node":0,"path":"weights"}}]}]}""";
	var source = System.Text.Encoding.UTF8.GetBytes( json );
	sourceHash = Hash( source );
	Require( sourceHash == "cc858730830536cf0973b2db5f6cfb76f6134c4d994b7867dfe8609d8d5f77d2", "Synthetic fixture does not match its pinned hash." );
	using var document = Sandbox.Gltf.GltfReader.Read( source );
	var data = document.CreateDrawData( 0, 0 );
	Require( data.HasMorphTargets && !data.HasSkinning && data.Positions.Length == 3, "Unexpected fixture draw data." );
	var decodedTargets = document.ReadMorphTargets( 0, 0 );
	Require( decodedTargets.Length == 1 && decodedTargets[0].PositionDeltas.Length == 9, "Fixture target was not decoded." );
	var decodedDeltas = decodedTargets[0].PositionDeltas;
	var indices = data.Indices.ToArray().Select( x => checked((int)x) ).ToArray();
	var initialPositions = data.Positions.ToArray();
	var normals = data.Normals.ToArray();
	var stock = Material.Load( "materials/default/white.vmat" );
	Require( stock is { IsValid: true } && !stock.IsError, "Stock white material is unavailable." );
	material = stock.CreateCopy( "gltf_morph_probe_material_" + Guid.NewGuid().ToString( "N" ) );
	Require( material is { IsValid: true }, "Could not copy the stock material." );
	material.SetFeature( "F_RENDER_BACKFACES", 1 );
	var bounds = new BBox( new Vector3( -100, -100, -100 ), new Vector3( 100, 100, 100 ) );
	var suffix = Guid.NewGuid().ToString( "N" );
	Vertex[] Vertices( System.Numerics.Vector3[] positions )
	{
		var vertices = new Vertex[positions.Length];
		for ( var i = 0; i < vertices.Length; i++ ) vertices[i] = new Vertex { Position = positions[i], Normal = normals[i], Color = Color32.White };
		return vertices;
	}
	Mesh MeshFor( System.Numerics.Vector3[] positions )
	{
		var mesh = new Mesh( material );
		var vertices = Vertices( positions );
		mesh.CreateVertexBuffer( vertices.Length, vertices );
		mesh.CreateIndexBuffer( indices.Length, indices );
		mesh.Bounds = bounds;
		return mesh;
	}
	Model BuildModel( Mesh mesh, string label )
	{
		var model = Model.Builder.WithName( "gltf_morph_" + label + "_" + suffix ).AddMesh( mesh ).WithViewBounds( bounds ).Create();
		Require( model is { IsValid: true } && !model.IsError, "Model creation failed: " + label );
		models.Add( model );
		return model;
	}
	phase = "build-procedural-morph";
	var morphMesh = MeshFor( initialPositions );
	var deltas = new MorphDelta[3];
	for ( var i = 0; i < deltas.Length; i++ )
	{
		var o = i * 3;
		var converted = Sandbox.Gltf.GltfGeometry.ConvertPosition( new System.Numerics.Vector3( decodedDeltas[o], decodedDeltas[o + 1], decodedDeltas[o + 2] ) );
		deltas[i] = new MorphDelta( i, converted, Vector3.Zero );
	}
	morphMesh.AddMorph( "gltf_target_0", deltas );
	if ( diagnosticPairedMorph )
	{
		var reversedDeltas = new MorphDelta[deltas.Length];
		for ( var i = 0; i < reversedDeltas.Length; i++ ) reversedDeltas[i] = new MorphDelta( deltas[i].VertexIndex, -deltas[i].PositionDelta, -deltas[i].NormalDelta );
		morphMesh.AddMorph( "gltf_target_0_negative", reversedDeltas );
	}
	var morphModel = BuildModel( morphMesh, "animated" );
	morphCount = morphModel.MorphCount;
	morphNames = morphModel.Morphs.Names.ToArray();
	controllerAvailable = morphNames.Any( x => string.Equals( x, "gltf_target_0", StringComparison.OrdinalIgnoreCase ) ) &&
		(!diagnosticPairedMorph || morphNames.Any( x => string.Equals( x, "gltf_target_0_negative", StringComparison.OrdinalIgnoreCase ) ));
	if ( !controllerAvailable ) failures.Add( "Runtime-built model did not expose the requested morph controller." );
	completed.Add( phase );

	phase = "build-reference-models";
	var references = new System.Collections.Generic.Dictionary<float, Model>();
	foreach ( var entry in new[] { (Time: 0f, Weight: 0f, Name: "zero"), (Time: 1f, Weight: 1f, Name: "positive"), (Time: 2f, Weight: -1f, Name: "negative") } )
	{
		var pose = document.EvaluatePose( 0, entry.Time );
		Require( MathF.Abs( pose.GetMorphWeights( 0 )[0] - entry.Weight ) < .00001f, "CPU animation did not produce expected weight: " + entry.Name );
		var expectedPositions = new System.Numerics.Vector3[3];
		for ( var i = 0; i < 3; i++ ) expectedPositions[i] = Sandbox.Gltf.GltfGeometry.ConvertPosition( document.EvaluateVertexPosition( 0, 0, i, pose ) );
		references.Add( entry.Weight, BuildModel( MeshFor( expectedPositions ), "reference_" + entry.Name ) );
	}
	if ( diagnosticPairedMorph )
	{
		foreach ( var entry in new[]
		{
			(Weight: 2f, Literal: "2", Hash: "a7f80f0f60bc0c0c44a6ed65c416e43dff7f7bfd3390a5901a3cd0abb0cf6651", Name: "positive_two"),
			(Weight: -2f, Literal: "-2", Hash: "9f5593eeb30f2d7b87c982d74e0fcf3aa1ec374adb723bb5d5484188c4e6fa1e", Name: "negative_two")
		} )
		{
			var variant = json.Replace( "\"weights\":[0]", "\"weights\":[" + entry.Literal + "]", StringComparison.Ordinal );
			var variantBytes = System.Text.Encoding.UTF8.GetBytes( variant );
			var variantHash = Hash( variantBytes );
			Require( variantHash == entry.Hash, "Static mesh-weight reference differs from its pinned digest: " + entry.Name );
			if ( entry.Weight > 0 ) positiveTwoHash = variantHash; else negativeTwoHash = variantHash;
			using var staticDocument = Sandbox.Gltf.GltfReader.Read( variantBytes );
			Require( MathF.Abs( staticDocument.GetMorphWeights( 0 )[0] - entry.Weight ) < .00001f, "Unexpected static mesh weight: " + entry.Name );
			var expectedPositions = new System.Numerics.Vector3[3];
			for ( var i = 0; i < 3; i++ ) expectedPositions[i] = Sandbox.Gltf.GltfGeometry.ConvertPosition( staticDocument.EvaluateVertexPosition( 0, 0, i ) );
			references.Add( entry.Weight, BuildModel( MeshFor( expectedPositions ), "reference_" + entry.Name ) );
		}
	}
	completed.Add( phase );

	phase = "create-private-preview";
	scene = Scene.CreateEditorScene();
	CameraComponent camera;
	using ( scene.Push() )
	{
		camera = scene.CreateObject().Components.Create<CameraComponent>();
		camera.WorldPosition = new Vector3( diagnosticPairedMorph ? 100 : 80, 0, 0 );
		camera.WorldRotation = Rotation.FromYaw( 180 );
		camera.BackgroundColor = Color.Black;
		camera.ZNear = 1;
		camera.FieldOfView = 50;
		camera.EnablePostProcessing = false;
		camera.DebugMode = SceneCameraDebugMode.Albedo;
	}
	if ( diagnosticCameraClear )
	{
		cameraClearCommands = new Sandbox.Rendering.CommandList( "glTF morph diagnostic camera color clear" );
		cameraClearCommands.Clear( Color.Black, true, false, false );
		camera.AddCommandList( cameraClearCommands, Sandbox.Rendering.Stage.AfterDepthPrepass );
		commandCamera = camera;
	}
	preview = new Editor.SceneRenderingWidget { Scene = scene, Camera = camera, WindowTitle = "glTF position morph capability", Size = new Vector2( 384, 384 ) };
	// Capability diagnostic only: call the existing generated native entry point
	// without the managed API's nonnegative clamp. Never alter the legacy API.
	var nativeField = typeof( SceneModel ).GetField( "animNative", flags );
	var signedSetter = diagnosticSignedOverride ? nativeField?.FieldType.GetMethod( "SBox_SetFlexOverride", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic, null, new[] { typeof( int ), typeof( float ), typeof( float ) }, null ) : null;
	if ( diagnosticSignedOverride ) Require( signedSetter is not null, "Signed override diagnostic entry point unavailable." );
	onPreviewFrame = () =>
	{
		previewFrames++;
		if ( !activeMorph || !shown.IsValid() || callbackError is not null ) return;
		try
		{
			if ( controllerAvailable )
			{
				if ( diagnosticPairedMorph )
				{
					shown.Morphs.Set( "gltf_target_0", MathF.Max( requestedWeight, 0 ), 0 );
					shown.Morphs.Set( "gltf_target_0_negative", MathF.Max( -requestedWeight, 0 ), 0 );
				}
				else if ( diagnosticSignedOverride ) signedSetter.Invoke( nativeField.GetValue( shown ), new object[] { 0, requestedWeight, 0f } );
				else shown.Morphs.Set( "gltf_target_0", requestedWeight, 0 );
			}
			shown.Update( RealTime.Delta );
			updateCalls++;
		}
		catch ( Exception e ) { callbackError = e; }
	};
	preview.OnPreFrame += onPreviewFrame;
	preview.Show();
	target = Texture.CreateRenderTarget().WithSize( 96, 96 ).WithFormat( ImageFormat.RGBA8888 ).Create( "gltf_morph_capture_" + suffix );
	completed.Add( phase );

	async Task WaitFrames()
	{
		var start = previewFrames;
		var timer = System.Diagnostics.Stopwatch.StartNew();
		while ( previewFrames < start + 3 )
		{
			cancellationToken.ThrowIfCancellationRequested();
			if ( callbackError is not null ) throw new InvalidOperationException( "Morph preview callback failed.", callbackError );
			if ( timer.Elapsed > TimeSpan.FromSeconds( 5 ) ) throw new TimeoutException( $"Preview did not receive three frame callbacks (started {start}, now {previewFrames})." );
			await Task.Delay( 50, cancellationToken );
			await MainThread.Wait();
		}
	}
	async Task<Color[]> Capture( string name )
	{
		phase = name;
		await WaitFrames();
		var before = previewFrames;
		using ( scene.Push() )
		{
			target.Clear( Color.Black );
			if ( !camera.RenderToTexture( target ) ) throw new InvalidOperationException( "Camera submission failed: " + name );
		}
		await WaitFrames();
		cancellationToken.ThrowIfCancellationRequested();
		using var bitmap = target.GetBitmap();
		Require( bitmap is not null, "Camera readback failed: " + name );
		var png = bitmap.ToPng();
		var path = System.IO.Path.Combine( output, name + ".png" );
		System.IO.File.WriteAllBytes( path, png );
		captures.Add( new { name, path, sha256 = Hash( png ), frame = Application.FrameCount, preview_frames_at_submit = before, preview_frames_at_readback = previewFrames } );
		return bitmap.GetPixels();
	}
	(float? Effective, float? Positive, float? Negative) ReadWeights()
	{
		if ( !controllerAvailable ) return (null, null, null);
		var positive = shown.Morphs.Get( "gltf_target_0" );
		if ( !diagnosticPairedMorph ) return (positive, positive, null);
		var negative = shown.Morphs.Get( "gltf_target_0_negative" );
		return (positive - negative, positive, negative);
	}
	async Task<(Color[] Pixels, float? Reported, float? Positive, float? Negative)> RenderModel( Model model, string name, bool animate, float weight )
	{
		requestedWeight = weight;
		activeMorph = false;
		shown = new SceneModel( scene.SceneWorld, model, Transform.Zero ) { UseAnimGraph = false };
		activeMorph = animate;
		var pixels = await Capture( name );
		var reported = animate ? ReadWeights() : (Effective: (float?)null, Positive: (float?)null, Negative: (float?)null);
		activeMorph = false;
		shown.Delete(); shown = null;
		return (pixels, reported.Effective, reported.Positive, reported.Negative);
	}
	var backgroundInitial = await Capture( "background_initial" );
	var background = await Capture( "background" );
	var backgroundNonblack = Nonblack( background );
	var baselineDifference = Difference( backgroundInitial, background );
	var referenceImages = new System.Collections.Generic.Dictionary<float, Color[]>();
	var nativeImages = new System.Collections.Generic.Dictionary<float, Color[]>();
	var perCasePassed = new System.Collections.Generic.Dictionary<float, bool>();
	var caseEntries = new System.Collections.Generic.List<(float Weight, string Name)> { (0f, "zero"), (1f, "positive"), (-1f, "negative") };
	if ( diagnosticPairedMorph ) { caseEntries.Add( (2f, "positive_two") ); caseEntries.Add( (-2f, "negative_two") ); }
	foreach ( var entry in caseEntries )
	{
		var reference = await RenderModel( references[entry.Weight], "reference_" + entry.Name, false, entry.Weight );
		var actual = await RenderModel( morphModel, "native_" + entry.Name, true, entry.Weight );
		var empty = await Capture( "empty_after_" + entry.Name );
		var expectedCoverage = Difference( reference.Pixels, background );
		var actualCoverage = Difference( actual.Pixels, background );
		var difference = Difference( reference.Pixels, actual.Pixels );
		var emptyDifference = Difference( empty, background );
		var reportedMatches = actual.Reported.HasValue && MathF.Abs( actual.Reported.Value - entry.Weight ) < .0001f;
		if ( diagnosticPairedMorph ) reportedMatches = reportedMatches && actual.Positive.HasValue && actual.Negative.HasValue &&
			MathF.Abs( actual.Positive.Value - MathF.Max( entry.Weight, 0 ) ) < .0001f && MathF.Abs( actual.Negative.Value - MathF.Max( -entry.Weight, 0 ) ) < .0001f;
		var passed = controllerAvailable && reportedMatches && expectedCoverage > 50 && actualCoverage > 50 && difference <= 5 && emptyDifference <= 5;
		if ( !passed ) failures.Add( $"{entry.Name}: expected coverage {expectedCoverage}, actual coverage {actualCoverage}, reference difference {difference}, empty difference {emptyDifference}, reported weight {actual.Reported?.ToString() ?? "unavailable"}." );
		referenceImages.Add( entry.Weight, reference.Pixels ); nativeImages.Add( entry.Weight, actual.Pixels ); perCasePassed.Add( entry.Weight, passed );
		cases.Add( new { name = entry.Name, requested_weight = entry.Weight, reported_weight = actual.Reported, positive_weight = actual.Positive, negative_weight = actual.Negative, expected_coverage = expectedCoverage, actual_coverage = actualCoverage, reference_difference = difference, empty_difference = emptyDifference, passed } );
	}
	if ( diagnosticPairedMorph )
	{
		phase = "same-instance-transitions";
		activeMorph = false;
		shown = new SceneModel( scene.SceneWorld, morphModel, Transform.Zero ) { UseAnimGraph = false };
		activeMorph = true;
		var transitionPass = true;
		foreach ( var entry in new[] { (Weight: 1f, Name: "positive"), (Weight: -1f, Name: "negative"), (Weight: 0f, Name: "zero") } )
		{
			requestedWeight = entry.Weight;
			var pixels = await Capture( "transition_" + entry.Name );
			var reported = ReadWeights();
			var difference = Difference( pixels, referenceImages[entry.Weight] );
			var coverage = Difference( pixels, background );
			var matches = reported.Effective.HasValue && reported.Positive.HasValue && reported.Negative.HasValue &&
				MathF.Abs( reported.Effective.Value - entry.Weight ) < .0001f &&
				MathF.Abs( reported.Positive.Value - MathF.Max( entry.Weight, 0 ) ) < .0001f &&
				MathF.Abs( reported.Negative.Value - MathF.Max( -entry.Weight, 0 ) ) < .0001f;
			var passed = matches && coverage > 50 && difference <= 5;
			transitionPass &= passed;
			if ( !passed ) failures.Add( $"Transition {entry.Name}: coverage {coverage}, reference difference {difference}, positive {reported.Positive?.ToString() ?? "unavailable"}, negative {reported.Negative?.ToString() ?? "unavailable"}." );
			transitions.Add( new { name = entry.Name, requested_weight = entry.Weight, effective_weight = reported.Effective, positive_weight = reported.Positive, negative_weight = reported.Negative, coverage, reference_difference = difference, passed } );
		}
		activeMorph = false;
		shown.Delete(); shown = null;
		var empty = await Capture( "empty_after_transitions" );
		var emptyDifference = Difference( empty, background );
		if ( emptyDifference > 5 ) { transitionPass = false; failures.Add( $"Transition empty control differs by {emptyDifference} pixels." ); }
		transitionsPassed = transitionPass;
	}
	var positiveReferenceDifference = Difference( referenceImages[0], referenceImages[1] );
	var negativeReferenceDifference = Difference( referenceImages[0], referenceImages[-1] );
	controlsPassed = Nonblack( backgroundInitial ) == 0 && backgroundNonblack == 0 && baselineDifference <= 5 && positiveReferenceDifference > 50 && negativeReferenceDifference > 50 && updateCalls > 0;
	if ( !controlsPassed ) failures.Add( $"Control failure: baseline nonblack {Nonblack( backgroundInitial )}/{backgroundNonblack}, baseline difference {baselineDifference}, positive/negative reference changes {positiveReferenceDifference}/{negativeReferenceDifference}, update calls {updateCalls}." );
	rangePassed = !diagnosticPairedMorph || perCasePassed[2] && perCasePassed[-2];
	positivePassed = controlsPassed && perCasePassed[0] && perCasePassed[1] && (!diagnosticPairedMorph || perCasePassed[2]);
	negativePassed = controlsPassed && perCasePassed[-1] && (!diagnosticPairedMorph || perCasePassed[-2]);
	completed.Add( "all-captures" );
}
catch ( OperationCanceledException e ) { error = "Cancelled: " + e; }
catch ( Exception e ) { error = e.ToString(); }
finally
{
	await MainThread.Wait();
	activeMorph = false;
	if ( preview is not null && preview.IsValid && onPreviewFrame is not null ) Cleanup( "detach callback", () => preview.OnPreFrame -= onPreviewFrame );
	if ( commandCamera.IsValid() && cameraClearCommands is not null ) Cleanup( "camera command", () => commandCamera.RemoveCommandList( cameraClearCommands ) );
	if ( preview is not null && preview.IsValid ) Cleanup( "preview", () => { preview.Close(); preview.Destroy(); } );
	if ( shown.IsValid() ) Cleanup( "scene model", () => shown.Delete() );
	if ( scene is not null ) Cleanup( "scene", () => scene.Destroy() );
	if ( target is not null ) Cleanup( "render target", () => target.Dispose() );
	var destroyModel = typeof( Model ).GetMethod( "Destroy", flags );
	foreach ( var model in models ) if ( model is not null && model.IsValid ) Cleanup( "model", () => destroyModel.Invoke( model, null ) );
	if ( material is not null && material.IsValid ) Cleanup( "material", () => typeof( Material ).GetMethod( "Destroy", flags ).Invoke( material, null ) );
}
var receipt = new
{
	passed = error is null && failures.Count == 0 && cleanupErrors.Count == 0 && positivePassed && negativePassed && rangePassed && (!diagnosticPairedMorph || transitionsPassed),
	positive_passed = positivePassed, negative_passed = negativePassed, controls_passed = controlsPassed, range_passed = rangePassed, transitions_passed = diagnosticPairedMorph ? transitionsPassed : (bool?)null,
	scope = "private editor stock-material runtime-built position-morph capability; negative weight remains a strict case; not loader integration, skinning, tangent morph or full glTF conformance",
	phase, error, failures, cleanup_errors = cleanupErrors, completed, cases, transitions, captures,
	controller_count = morphCount, controller_names = morphNames, requested_controller_found = controllerAvailable,
	preview_frames = previewFrames, scene_model_updates = updateCalls, source_sha256 = sourceHash, positive_two_sha256 = positiveTwoHash, negative_two_sha256 = negativeTwoHash,
	engine_location = engineLocation, engine_mvid = typeof( Model ).Assembly.ManifestModule.ModuleVersionId.ToString(),
	gltf_mvid = typeof( Sandbox.Gltf.GltfReader ).Assembly.ManifestModule.ModuleVersionId.ToString(),
	is_editor = Application.IsEditor, diagnostic_camera_clear = diagnosticCameraClear, diagnostic_signed_override = diagnosticSignedOverride, diagnostic_paired_morph = diagnosticPairedMorph, starting_frame = startFrame, final_frame = Application.FrameCount, output
};
System.IO.File.WriteAllText( receiptPath, System.Text.Json.JsonSerializer.Serialize( receipt, new System.Text.Json.JsonSerializerOptions { WriteIndented = true } ) );
return receipt;
