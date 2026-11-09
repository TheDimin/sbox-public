// Run in an existing s&box editor. Measures native fixed-frame sequence behavior;
// it does not implement a glTF animation bridge or approve approximation tolerances.
var engineLocation = typeof( Model ).Assembly.Location;
var checkout = new System.IO.DirectoryInfo( System.IO.Path.GetDirectoryName( engineLocation ) );
while ( checkout is not null && !System.IO.File.Exists( System.IO.Path.Combine( checkout.FullName, "engine", "Sandbox-Engine.slnx" ) ) ) checkout = checkout.Parent;
if ( checkout is null ) throw new InvalidOperationException( "Could not locate checkout from " + engineLocation );
var corpusRoot = Environment.GetEnvironmentVariable( "SBOX_GLTF_CORPUS" );
if ( string.IsNullOrWhiteSpace( corpusRoot ) ) corpusRoot = System.IO.Path.Combine( checkout.FullName, "Temp", "gltf-reference", "glTF-Sample-Assets" );
var runId = Guid.NewGuid().ToString( "N" );
var output = System.IO.Path.Combine( checkout.FullName, "Temp", "gltf-validation", "editor-animation-playback", runId );
System.IO.Directory.CreateDirectory( output );
var receiptPath = System.IO.Path.Combine( output, "receipt.json" );
var suffix = runId;
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
var sourceHashes = new System.Collections.Generic.Dictionary<string, string>();
var observations = new System.Collections.Generic.List<object>();
var diagnostics = new System.Collections.Generic.List<string>();
var cleanupErrors = new System.Collections.Generic.List<string>();
var phase = "initialization";
var startingFrame = Application.FrameCount;
string Hash( byte[] value ) => Convert.ToHexStringLower( System.Security.Cryptography.SHA256.HashData( value ) );
string HashFile( string relative ) => Hash( System.IO.File.ReadAllBytes( System.IO.Path.Combine( checkout.FullName, relative ) ) );
void Save( object receipt ) => System.IO.File.WriteAllText( receiptPath, System.Text.Json.JsonSerializer.Serialize( receipt, new System.Text.Json.JsonSerializerOptions { WriteIndented = true } ) );
object loadAcceptance = new { status = "not-run" };
object sequenceSummary = null;
SceneWorld world = null;
SceneModel sceneModel = null;
Mesh mesh = null;
Model model = null;
Model loaded = null;
IDisposable redirect = null;
object redirectObject = null;
BaseFileSystem mountedAggregate = null;
BaseFileSystem managedMount = null;
System.Reflection.MethodInfo unmount = null;
bool mounted = false;
string redirectPath = null;
string virtualPath = null;
string physical = null;
async Task WaitForSafeMainThread()
{
	await MainThread.Wait();
	var timer = System.Diagnostics.Stopwatch.StartNew();
	while ( Graphics.IsActive )
	{
		if ( timer.Elapsed > TimeSpan.FromSeconds( 5 ) ) throw new TimeoutException( "Graphics remained active while waiting for a safe main-thread boundary." );
		await Task.Delay( 25 );
		await MainThread.Wait();
	}
}
void Cleanup( string step, Action action )
{
	try { action(); }
	catch ( Exception error ) { cleanupErrors.Add( step + ": " + error ); }
}
try
{
	if ( !Application.IsEditor ) throw new InvalidOperationException( "This measurement requires a running s&box editor." );
	await WaitForSafeMainThread();
	if ( Graphics.IsActive ) throw new InvalidOperationException( "Native animation probe cannot run during rendering." );
	foreach ( var file in new[] { "engine/Sandbox.Engine/Resources/Model/Builder/AnimationBuilder.cs", "engine/Sandbox.Engine/Resources/Model/Builder/ModelBuilder.Animation.cs", "engine/Sandbox.Engine/Resources/Model/Builder/ModelBuilder.Skeleton.cs", "engine/Sandbox.Engine/Systems/SceneSystem/Accessors/SceneObjectAnimationSequence.cs", "engine/Tools/Gltf/editor/editor_animation_playback.csx" } ) sourceHashes[file] = HashFile( file );
	try
	{
	phase = "create-two-bone-sequences";
	var stock = Material.Load( "materials/default/white.vmat" );
	if ( stock is null || !stock.IsValid || stock.IsError ) throw new InvalidOperationException( "Stock material unavailable." );
	mesh = new Mesh( stock );
	mesh.CreateVertexBuffer( 3, new Vertex[] { new() { Position = Vector3.Zero }, new() { Position = new Vector3( 0, 10, 0 ) }, new() { Position = new Vector3( 0, 0, 10 ) } } );
	mesh.CreateIndexBuffer( 3, new[] { 0, 1, 2 } );
	mesh.Bounds = new BBox( Vector3.Zero, new Vector3( 0, 10, 10 ) );
	var builder = Model.Builder.WithName( "gltf_animation_probe_" + suffix ).AddMesh( mesh ).WithViewBounds( new BBox( new Vector3( -32, -32, -32 ), new Vector3( 32, 32, 32 ) ) );
	builder.AddBone( "root", Vector3.Zero, Rotation.Identity );
	builder.AddBone( "child", new Vector3( 0, 0, 10 ), Rotation.Identity, "root" );
	foreach ( var disabled in new[] { false, true } )
	{
		var animation = builder.AddAnimation( disabled ? "fixed_step" : "fixed_linear", 2f );
		animation.DisableInterpolation = disabled;
		for ( var frame = 0; frame < 3; frame++ )
		{
			var root = new Transform { Position = new Vector3( frame * 10, 0, 0 ), Rotation = Rotation.FromYaw( frame * 45 ), Scale = Vector3.One * (1 + frame * 0.5f) };
			var child = new Transform { Position = new Vector3( 0, 0, 10 ), Rotation = Rotation.Identity, Scale = Vector3.One };
			animation.AddFrame( new System.Collections.Generic.List<Transform> { root, child } );
		}
	}
	model = builder.Create();
	if ( model is null || !model.IsValid || model.IsError ) throw new InvalidOperationException( "Native animation probe model is invalid." );
	if ( model.BoneCount != 2 || model.GetBoneName( 0 ) != "root" || model.GetBoneName( 1 ) != "child" || model.GetBoneParent( 1 ) != 0 )
		throw new InvalidOperationException( "Native skeleton order/parent does not match frame layout." );
	world = new SceneWorld();
	sceneModel = new SceneModel( world, model, Transform.Zero ) { UseAnimGraph = false, PlaybackRate = 0 };
	var sequence = sceneModel.CurrentSequence;
	sequenceSummary = new { names = sequence.SequenceNames, bone_count = model.BoneCount, bone0 = model.GetBoneName( 0 ), bone1 = model.GetBoneName( 1 ), bone1_parent = model.GetBoneParent( 1 ), frame_rate = 2, authored_frames = 3 };
	var requestedTimes = new[] { 0f, 0.125f, 0.25f, MathF.BitDecrement( 0.5f ), 0.5f, MathF.BitIncrement( 0.5f ), 0.75f, 1f };
	foreach ( var name in new[] { "fixed_linear", "fixed_step" } )
	{
		phase = "native-sequence-" + name;
		sequence.Name = name;
		var duration = sequence.Duration;
		if ( !float.IsFinite( duration ) || duration <= 0 ) throw new InvalidOperationException( "Sequence duration is not positive: " + name );
		foreach ( var time in requestedTimes )
		{
			cancellationToken.ThrowIfCancellationRequested();
			if ( time > duration ) { diagnostics.Add( name + ": requested time " + time + " exceeds measured duration " + duration ); continue; }
			sequence.Time = time;
			var before = sequence.Time;
			sceneModel.Update( 0 );
			var after = sequence.Time;
			var root = sceneModel.GetParentSpaceBone( 0 );
			var child = sceneModel.GetParentSpaceBone( 1 );
			var childWorld = sceneModel.GetBoneWorldTransform( 1 );
			if ( !new[] { before, after, root.Position.x, root.Position.y, root.Position.z, root.Scale.x, root.Scale.y, root.Scale.z,
				child.Position.x, child.Position.y, child.Position.z, childWorld.Position.x, childWorld.Position.y, childWorld.Position.z }.All( float.IsFinite ) )
				throw new InvalidOperationException( "Native sequence returned non-finite time or transform values." );
			System.Numerics.Quaternion actual = root.Rotation;
			if ( !float.IsFinite( actual.X ) || !float.IsFinite( actual.Y ) || !float.IsFinite( actual.Z ) || !float.IsFinite( actual.W ) || actual.LengthSquared() < 1e-20f )
				throw new InvalidOperationException( "Native sequence returned an invalid root quaternion." );
			System.Numerics.Quaternion reference = Rotation.FromYaw( Math.Clamp( time, 0, 1 ) * 90 );
			var qDot = Math.Clamp( Math.Abs( System.Numerics.Quaternion.Dot( System.Numerics.Quaternion.Normalize( actual ), reference ) ), 0f, 1f );
			var referencePosition = time < 0.5f ? 0f : time < 1f ? 10f : 20f;
			observations.Add( new { sequence = name, duration, requested_time = time, reported_time_before_update = before, reported_time_after_update = after,
				root_position = new[] { root.Position.x, root.Position.y, root.Position.z }, root_scale = new[] { root.Scale.x, root.Scale.y, root.Scale.z },
				root_rotation_xyzw = new[] { actual.X, actual.Y, actual.Z, actual.W }, child_local_position = new[] { child.Position.x, child.Position.y, child.Position.z },
				child_world_position = new[] { childWorld.Position.x, childWorld.Position.y, childWorld.Position.z },
				mixed_reference_step_x = referencePosition, mixed_reference_linear_yaw_degrees = Math.Clamp( time, 0, 1 ) * 90,
				mixed_translation_error_inches = Math.Abs( root.Position.x - referencePosition ), mixed_rotation_error_degrees = 2 * Math.Acos( qDot ) * 180 / Math.PI } );
		}
	}
	}
	catch ( OperationCanceledException ) { throw; }
	catch ( Exception error ) { diagnostics.Add( phase + ": " + error ); }
	phase = "actual-animated-gltf-model-load";
	var sourcePath = System.IO.Path.Combine( corpusRoot, "Models", "AnimatedTriangle", "glTF-Embedded", "AnimatedTriangle.gltf" );
	var source = System.IO.File.ReadAllBytes( sourcePath );
	const string expectedSourceSha = "7add176d00a1275208a383fbaed6fbdd6e486f6ad8f6580ec7f0bc5b20c48a17";
	if ( Hash( source ) != expectedSourceSha ) throw new InvalidOperationException( "Pinned Khronos AnimatedTriangle fixture differs." );
	using ( var doc = Sandbox.Gltf.GltfReader.Read( source ) )
		if ( !doc.Root.TryGetProperty( "animations", out var animationArray ) || animationArray.GetArrayLength() == 0 ) throw new InvalidOperationException( "Pinned fixture has no animation." );
	physical = System.IO.Path.Combine( output, "animated-" + suffix + ".gltf" );
	System.IO.File.WriteAllBytes( physical, source );
	virtualPath = "gltf-animation-probe/" + suffix + "/AnimatedTriangle.gltf";
	redirectPath = "/" + virtualPath;
	var redirectType = typeof( Model ).Assembly.GetType( "Sandbox.Internal.RedirectFileSystem", true );
	redirectObject = redirectType.GetMethod( "Create", flags ).Invoke( null, new object[] { output } );
	redirect = (IDisposable)redirectObject;
	redirectType.GetMethod( "AddAbsFile", flags ).Invoke( redirectObject, new object[] { redirectPath, physical } );
	var wrapper = typeof( BaseFileSystem ).GetConstructors( flags ).Single( ctor => { var p = ctor.GetParameters(); return p.Length == 1 && p[0].ParameterType.IsInstanceOfType( redirectObject ); } );
	managedMount = (BaseFileSystem)wrapper.Invoke( new[] { redirectObject } );
	mountedAggregate = Sandbox.FileSystem.Mounted ?? throw new InvalidOperationException( "Mounted filesystem unavailable." );
	var mount = typeof( BaseFileSystem ).GetMethod( "Mount", flags, null, new[] { typeof( BaseFileSystem ) }, null );
	unmount = typeof( BaseFileSystem ).GetMethod( "UnMount", flags, null, new[] { typeof( BaseFileSystem ) }, null );
	if ( mount is null || unmount is null ) throw new InvalidOperationException( "Managed mount methods unavailable." );
	mount.Invoke( mountedAggregate, new object[] { managedMount } );
	mounted = true;
	if ( !mountedAggregate.FileExists( virtualPath ) || Hash( mountedAggregate.ReadAllBytes( virtualPath ).ToArray() ) != expectedSourceSha ) throw new InvalidOperationException( "Animated glTF fixture failed mounted readback." );
	try
	{
		loaded = Model.Load( virtualPath );
		var valid = loaded is not null && loaded.IsValid && !loaded.IsError && !ReferenceEquals( loaded, Model.Error );
		var names = valid ? loaded.AnimationNames.ToArray() : Array.Empty<string>();
		loadAcceptance = new { status = valid && names.Length > 0 ? "loaded-unverified-playback" : "fail", source_sha256 = expectedSourceSha, valid_model = valid, animation_names = names, path = virtualPath };
	}
	catch ( Exception error ) { loadAcceptance = new { status = "fail", source_sha256 = expectedSourceSha, valid_model = false, error = error.ToString(), path = virtualPath }; }
	phase = "complete";
}
catch ( OperationCanceledException ) { throw; }
catch ( Exception error ) { diagnostics.Add( phase + ": " + error ); }
finally
{
	try { await WaitForSafeMainThread(); }
	catch ( Exception error ) { cleanupErrors.Add( "main-thread boundary: " + error ); }
	if ( !Graphics.IsActive )
	{
		Cleanup( "scene model", () => { if ( sceneModel.IsValid() ) sceneModel.Delete(); } );
		Cleanup( "scene world", () => world?.Delete() );
		var destroy = typeof( Model ).GetMethod( "Destroy", flags );
		Cleanup( "loaded model", () => { if ( loaded is not null && !ReferenceEquals( loaded, Model.Error ) ) (destroy ?? throw new InvalidOperationException( "Model.Destroy unavailable." )).Invoke( loaded, null ); } );
		Cleanup( "probe model", () => { if ( model is not null && !ReferenceEquals( model, Model.Error ) ) (destroy ?? throw new InvalidOperationException( "Model.Destroy unavailable." )).Invoke( model, null ); } );
		// Give queued native releases an ordinary editor turn before disposing the owned mesh.
		try { await Task.Delay( 50 ); await WaitForSafeMainThread(); }
		catch ( Exception error ) { cleanupErrors.Add( "mesh release boundary: " + error ); }
		Cleanup( "owned mesh", () =>
		{
			if ( mesh is null ) return;
			if ( Graphics.IsActive ) throw new InvalidOperationException( "Cannot release mesh during rendering." );
			var method = typeof( Model ).Assembly.GetType( "Sandbox.GltfModelLoader" )?.GetMethod( "ReleaseMeshes", flags ) ?? throw new InvalidOperationException( "Owned mesh release helper unavailable." );
			method.Invoke( null, new object[] { new System.Collections.Generic.List<Mesh> { mesh } } );
		} );
	}
	else cleanupErrors.Add( "native cleanup deferred: graphics remained active" );
	Cleanup( "managed unmount", () => { if ( mounted ) unmount.Invoke( mountedAggregate, new object[] { managedMount } ); } );
	Cleanup( "native redirect removal", () => { if ( redirectObject is not null && redirectPath is not null ) (redirectObject.GetType().GetMethod( "RemoveAbsFile", flags ) ?? throw new InvalidOperationException( "RemoveAbsFile unavailable." )).Invoke( redirectObject, new object[] { redirectPath } ); } );
	Cleanup( "redirect disposal", () => redirect?.Dispose() );
	Cleanup( "fixture removal", () => { if ( physical is not null && System.IO.File.Exists( physical ) ) System.IO.File.Delete( physical ); } );
}
var receipt = new { measurements_complete = phase == "complete" && observations.Count == 16 && diagnostics.Count == 0 && cleanupErrors.Count == 0, full_animation_passed = false,
	scope = "two-bone native fixed-frame seek and parent/world bone readback; no skinned render or glTF-to-native playback bridge",
	mixed_channel_reference = "translation STEP and rotation LINEAR over the same 2 Hz keys; both native sequences share one interpolation mode",
	native_tolerances = "not-approved", gltf_animation_model_load = loadAcceptance, sequence = sequenceSummary, observations, diagnostics, cleanup_errors = cleanupErrors,
	source_hashes = sourceHashes, engine_mvid = typeof( Model ).Assembly.ManifestModule.ModuleVersionId.ToString(),
	source_hash_qualification = "worktree source inventory at execution time; not proof loaded modules were built from those bytes",
	gltf_mvid = typeof( Sandbox.Gltf.GltfReader ).Assembly.ManifestModule.ModuleVersionId.ToString(), is_editor = Application.IsEditor,
	starting_frame = startingFrame, final_frame = Application.FrameCount, run_id = runId, output };
Save( receipt );
return receipt;
