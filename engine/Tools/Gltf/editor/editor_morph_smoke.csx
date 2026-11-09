// Actual Model.Load + existing SkinnedModelRenderer/Sequence in a private editor
// scene. Motion evidence is separate from reference-animation conformance.
var engine = typeof( Model ).Assembly;
var checkout = new System.IO.DirectoryInfo( System.IO.Path.GetDirectoryName( engine.Location ) );
while ( checkout is not null && !System.IO.File.Exists( System.IO.Path.Combine( checkout.FullName, "engine", "Sandbox-Engine.slnx" ) ) ) checkout = checkout.Parent;
if ( checkout is null ) throw new InvalidOperationException( "Cannot locate checkout." );
var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static;
var runId = Guid.NewGuid().ToString( "N" ); var prefix = "gltf-animation-load/" + runId;
var output = System.IO.Path.Combine( checkout.FullName, "Temp", "gltf-validation", "editor-animation-load", runId ); System.IO.Directory.CreateDirectory( output );
var rows = new System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, object>>();
var cleanupErrors = new System.Collections.Generic.List<string>(); var aliases = new System.Collections.Generic.List<string>();
var hashes = new System.Collections.Generic.Dictionary<string, string>(); var messages = new System.Collections.Generic.List<string>();
var selectedIds = new[] { "AnimatedMorphCube/glTF" };
object redirectObject = null; IDisposable redirect = null; BaseFileSystem mount = null, aggregate = null;
System.Reflection.MethodInfo unmount = null, removeRedirect = null, removeLog = null; Action<LogEvent> logHandler = null;
bool mounted = false; string phase = "initialize", error = null;
string Hash( byte[] bytes ) => Convert.ToHexStringLower( System.Security.Cryptography.SHA256.HashData( bytes ) );
void Require( bool condition, string message ) { if ( !condition ) throw new InvalidOperationException( message ); }
void Clean( string name, Action action ) { try { action(); } catch ( Exception ex ) { cleanupErrors.Add( name + ": " + ex ); } }
bool Valid( Model model ) => model is { IsValid: true } && !model.IsError && !ReferenceEquals( model, Model.Error );
async Task SafePoint()
{
	await Task.Delay( 1, cancellationToken ); await MainThread.Wait();
	Require( Application.IsEditor && !Game.IsPlaying && !Graphics.IsActive, "Run in the idle editor outside rendering; preserve user play." );
}
void Put( string relative, byte[] bytes )
{
	var path = System.IO.Path.Combine( output, "input", relative.Replace( '/', System.IO.Path.DirectorySeparatorChar ) );
	System.IO.Directory.CreateDirectory( System.IO.Path.GetDirectoryName( path ) ); System.IO.File.WriteAllBytes( path, bytes );
	var alias = "/" + prefix + "/" + relative; redirectObject.GetType().GetMethod( "AddAbsFile", flags ).Invoke( redirectObject, new object[] { alias, path } ); aliases.Add( alias ); hashes[relative] = Hash( bytes );
}
try
{
	await SafePoint();
	var manifestBytes = System.IO.File.ReadAllBytes( System.IO.Path.Combine( checkout.FullName, "research/gltf-validation/corpus-case-manifest.json" ) ); hashes["corpus_manifest"] = Hash( manifestBytes );
	using var manifest = System.Text.Json.JsonDocument.Parse( manifestBytes ); var root = manifest.RootElement;
	Require( root.GetProperty( "corpus" ).GetProperty( "commit" ).GetString() == "90d7ede14c7e280af263824604b427a1ca02cb66", "Unexpected corpus pin." );
	var corpus = System.IO.Path.Combine( checkout.FullName, "Temp/gltf-reference/glTF-Sample-Assets" );
	var rendererAssembly = System.Reflection.Assembly.Load( "Sandbox.SceneRenderer" );
	var renderer = engine.GetType( "Sandbox.Rendering.ManagedSceneRendering", true ).GetProperty( "Renderer", flags ).GetValue( null );
	Require( renderer is not null && renderer.GetType().Assembly == rendererAssembly, "Verified managed scene renderer is unavailable." );
	var render = renderer.GetType().GetMethod( "RenderToTexture", flags, null, new[] { typeof( CameraComponent ), typeof( Texture ) }, null ); Require( render is not null, "Camera capture API unavailable." );
	var redirectType = engine.GetType( "Sandbox.Internal.RedirectFileSystem", true ); redirectObject = redirectType.GetMethod( "Create", flags ).Invoke( null, new object[] { output } ); redirect = (IDisposable)redirectObject;
	removeRedirect = redirectType.GetMethod( "RemoveAbsFile", flags );
	var constructor = typeof( BaseFileSystem ).GetConstructors( flags ).Single( ctor => { var p = ctor.GetParameters(); return p.Length == 1 && p[0].ParameterType.IsInstanceOfType( redirectObject ); } );
	mount = (BaseFileSystem)constructor.Invoke( new[] { redirectObject } ); aggregate = FileSystem.Mounted;
	unmount = typeof( BaseFileSystem ).GetMethod( "UnMount", flags, null, new[] { typeof( BaseFileSystem ) }, null );
	typeof( BaseFileSystem ).GetMethod( "Mount", flags, null, new[] { typeof( BaseFileSystem ) }, null ).Invoke( aggregate, new object[] { mount } ); mounted = true;
	var logging = typeof( LogEvent ).Assembly.GetType( "Sandbox.Diagnostics.Logging", true ); var logEvent = logging.GetEvent( "OnMessage", flags );
	logHandler = entry =>
	{
		var text = (entry.Message ?? "") + (entry.Exception is null ? "" : " " + entry.Exception.Message); var scoped = text.Contains( prefix, StringComparison.OrdinalIgnoreCase ); var level = entry.Level.ToString();
		if ( !scoped && !level.Contains( "warn", StringComparison.OrdinalIgnoreCase ) && !level.Contains( "error", StringComparison.OrdinalIgnoreCase ) ) return;
		lock ( messages ) if ( messages.Count < 128 ) messages.Add( phase + " | " + (scoped ? "fixture" : "unscoped diagnostic; not attributed") + " | " + level + ": " + text[..Math.Min( text.Length, 2048 )] );
	};
	logEvent.GetAddMethod( true ).Invoke( null, new object[] { logHandler } ); removeLog = logEvent.GetRemoveMethod( true );
	foreach ( var id in selectedIds ) foreach ( var form in new[] { "original-source", "derived-packaging-glb_c" } )
	{
		await SafePoint(); phase = id + " " + form;
		var row = new System.Collections.Generic.Dictionary<string, object> { ["id"] = id, ["form"] = form, ["load"] = "not_run", ["motion"] = "not_run", ["passed"] = false };
		var clips = new System.Collections.Generic.List<object>(); row["clips"] = clips;
		Model model = null; bool owned = false; Scene scene = null; Editor.SceneRenderingWidget preview = null; Action onFrame = null; int frames = 0;
		SceneModel sphere = null; SkinnedModelRenderer component = null, second = null;
		try
		{
			var entry = root.GetProperty( "cases" ).EnumerateArray().Single( item => item.GetProperty( "id" ).GetString() == id );
			row["provenance"] = entry.GetProperty( "provenance" ).Clone();
			var sourcePath = entry.GetProperty( "path" ).GetString(); var sourceDirectory = System.IO.Path.GetDirectoryName( sourcePath ).Replace( '\\', '/' );
			var files = new System.Collections.Generic.Dictionary<string, byte[]>();
			foreach ( var path in entry.GetProperty( "dependencies" ).EnumerateArray().Select( item => item.GetString() ).Prepend( sourcePath ) )
			{
				var bytes = System.IO.File.ReadAllBytes( System.IO.Path.Combine( corpus, path ) );
				Require( bytes.Length <= 32 * 1024 * 1024 && Hash( bytes ) == root.GetProperty( "files" ).GetProperty( path ).GetProperty( "sha256" ).GetString(), "Missing, oversized or changed pinned input: " + path );
				files.Add( path, bytes );
			}
			row["source_sha256"] = Hash( files[sourcePath] );
			using var sourceJson = System.Text.Json.JsonDocument.Parse( files[sourcePath] );
			var directory = id + "/" + form; string relative;
			byte[] Resolve( string uri )
			{
				Require( !Uri.TryCreate( uri, UriKind.Absolute, out _ ) && !uri.Contains( '\\' ), "Unexpected nonlocal dependency." );
				var path = System.IO.Path.GetRelativePath( corpus, System.IO.Path.GetFullPath( System.IO.Path.Combine( corpus, sourceDirectory, Uri.UnescapeDataString( uri ) ) ) ).Replace( '\\', '/' );
				return files.TryGetValue( path, out var bytes ) ? bytes : throw new InvalidOperationException( "Uninventoried dependency: " + path );
			}
			float[] ReadSourceKeyTimes( int index )
			{
				var sourceRoot = sourceJson.RootElement; var accessor = sourceRoot.GetProperty( "accessors" )[index];
				Require( accessor.GetProperty( "componentType" ).GetInt32() == 5126 && accessor.GetProperty( "type" ).GetString() == "SCALAR" && !accessor.TryGetProperty( "sparse", out _ ), "Focused timeline provenance expects a nonsparse scalar float input accessor." );
				var view = sourceRoot.GetProperty( "bufferViews" )[accessor.GetProperty( "bufferView" ).GetInt32()];
				var buffer = sourceRoot.GetProperty( "buffers" )[view.GetProperty( "buffer" ).GetInt32()]; var bytes = Resolve( buffer.GetProperty( "uri" ).GetString() );
				var count = accessor.GetProperty( "count" ).GetInt32(); Require( count > 0 && count <= 100000, "Timeline count exceeds focused provenance limit." );
				var offset = (view.TryGetProperty( "byteOffset", out var vo ) ? vo.GetInt32() : 0) + (accessor.TryGetProperty( "byteOffset", out var ao ) ? ao.GetInt32() : 0);
				var stride = view.TryGetProperty( "byteStride", out var vs ) ? vs.GetInt32() : 4; var times = new float[count];
				for ( var i = 0; i < count; i++ ) times[i] = System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian( bytes.AsSpan( checked(offset + i * stride), 4 ) );
				return times;
			}
			if ( form == "original-source" )
			{
				relative = directory + "/" + System.IO.Path.GetFileName( sourcePath );
				foreach ( var file in files ) Put( directory + "/" + System.IO.Path.GetRelativePath( sourceDirectory, file.Key ).Replace( '\\', '/' ), file.Value );
			}
			else
			{
				var compiled = Sandbox.Gltf.GltfCompiler.Compile( files[sourcePath], Resolve ); var bundle = Sandbox.Gltf.GltfArtifactDependencies.Prepare( compiled, Resolve );
				relative = directory + "/model.glb"; Put( relative + "_c", bundle.Artifact ); foreach ( var file in bundle.Dependencies ) Put( directory + "/" + file.Key, file.Value );
				Require( !aggregate.FileExists( prefix + "/" + relative ), "Compiled form unexpectedly has a source sibling." );
			}
			engine.GetType( "NativeEngine.g_pResourceSystem", true ).GetMethod( "InvalidateDatabase", flags ).Invoke( null, null );
			var logical = prefix + "/" + relative; row["asset_path"] = logical; row["load"] = "failed"; model = Model.Load( logical );
			owned = Valid( model ) && string.Equals( model.ResourcePath, logical, StringComparison.OrdinalIgnoreCase ); row["load"] = owned ? "passed" : "failed"; Require( owned, "Actual Model.Load failed." );
			row["bone_count"] = model.BoneCount; row["animation_names"] = model.AnimationNames.ToArray(); Require( model.AnimationCount > 0, "No clips exposed by Model.Load." );
			scene = Scene.CreateEditorScene(); CameraComponent camera;
			using ( scene.Push() ) { camera = scene.CreateObject().Components.Create<CameraComponent>(); camera.BackgroundColor = Color.Black; camera.DebugMode = SceneCameraDebugMode.Albedo; camera.EnablePostProcessing = false; camera.FieldOfView = 50; }
			var center = model.RenderBounds.Center; var radius = MathF.Max( model.RenderBounds.Size.Length, .01f );
			Require( float.IsFinite( radius ) && radius < 1e10f, "Cannot frame model bounds safely." );
			var sourceDirection = new Vector3( 1, .65f, 1 ); var sourceUp = new Vector3( 0, 1, 0 );
			var nativeDirection = new Vector3( sourceDirection.z, sourceDirection.x, sourceDirection.y ); var nativeUp = new Vector3( sourceUp.z, sourceUp.x, sourceUp.y );
			camera.WorldPosition = center + nativeDirection.Normal * radius * 2.8f; camera.WorldRotation = Rotation.LookAt( center - camera.WorldPosition, nativeUp ); camera.ZNear = MathF.Max( radius * .0001f, .0001f ); camera.ZFar = radius * 30;
			row["framing"] = new { camera_preset = "source-positive", source_direction = new[] { sourceDirection.x, sourceDirection.y, sourceDirection.z }, native_direction = new[] { nativeDirection.x, nativeDirection.y, nativeDirection.z }, source_up = new[] { sourceUp.x, sourceUp.y, sourceUp.z }, native_up = new[] { nativeUp.x, nativeUp.y, nativeUp.z }, exact_upstream_extrinsics_known = false, center = center.ToString(), radius, position = camera.WorldPosition.ToString(), rotation = camera.WorldRotation.ToString(), camera.FieldOfView, camera.ZNear, camera.ZFar };
			preview = new Editor.SceneRenderingWidget { Scene = scene, Camera = camera, WindowTitle = "glTF animation validation: " + id, Size = new Vector2( 384, 384 ) }; onFrame = () => frames++; preview.OnPreFrame += onFrame; preview.Show();
			async Task WaitFrames()
			{
				var start = frames; var timer = System.Diagnostics.Stopwatch.StartNew();
				while ( frames < start + 3 ) { Require( timer.Elapsed < TimeSpan.FromSeconds( 8 ), "Preview stopped receiving real frames." ); await Task.Delay( 20, cancellationToken ); await SafePoint(); }
			}
			async Task<(Color[] Pixels, object Metadata)> Capture( string label, float time )
			{
				await WaitFrames(); using var target = Texture.CreateRenderTarget().WithSize( 192, 192 ).WithFormat( ImageFormat.RGBA8888 ).WithInitialColor( Color.Magenta ).Create( "gltf_motion_" + Guid.NewGuid().ToString( "N" ) );
				var selectedName = component?.SceneModel?.CurrentSequence.Name; var beforeTime = component?.Sequence.Time; var secondTime = second?.Sequence.Time;
				if ( component is not null ) Require( selectedName == component.Sequence.Name && MathF.Abs( beforeTime.Value - time ) < .002f, "Requested and evaluated clip/time differ before capture." );
				using ( scene.Push() ) { target.Clear( Color.Magenta ); Require( (bool)render.Invoke( renderer, new object[] { camera, target } ), "Capture submission failed." ); }
				await WaitFrames(); using var bitmap = target.GetBitmap(); Require( bitmap is not null, "Capture readback failed." );
				Require( bitmap.Width == 192 && bitmap.Height == 192, "Capture dimensions differ." ); var pixels = bitmap.GetPixels(); Require( pixels.Length == 192 * 192, "Capture pixel count differs." );
				var afterTime = component?.Sequence.Time;
				if ( component is not null ) Require( selectedName == component.SceneModel.CurrentSequence.Name && MathF.Abs( afterTime.Value - beforeTime.Value ) < .000001f && secondTime == second?.Sequence.Time, "Rendering mutated a controlled instance clock or clip." );
				var png = bitmap.ToPng(); var path = System.IO.Path.Combine( output, id.Replace( '/', '_' ) + "_" + form + "_" + label + ".png" ); System.IO.File.WriteAllBytes( path, png );
				return (pixels, new { path, sha256 = Hash( png ), requested_time = time, evaluated_time_before = beforeTime, evaluated_time_after = afterTime, selected_clip_name = selectedName,
					selected_clip_index = selectedName is null ? -1 : Array.IndexOf( model.AnimationNames.ToArray(), selectedName ), primary_component_id = component?.Id.ToString(), secondary_component_id = second?.Id.ToString(), secondary_time = secondTime,
					width = 192, height = 192, frame = Application.FrameCount, preview_frames = frames, fresh_target = true, renderer = "managed SceneBridge.RenderToTexture", renderer_mvid = rendererAssembly.ManifestModule.ModuleVersionId.ToString() });
			}
			bool Visible( Color pixel ) => MathF.Abs( pixel.r ) > .03f || MathF.Abs( pixel.g ) > .03f || MathF.Abs( pixel.b ) > .03f;
			int Difference( Color[] a, Color[] b ) => a.Zip( b ).Count( pair => MathF.Abs( pair.First.r - pair.Second.r ) > .03f || MathF.Abs( pair.First.g - pair.Second.g ) > .03f || MathF.Abs( pair.First.b - pair.Second.b ) > .03f );
			Require( !(await Capture( "empty_before", 0 )).Pixels.Any( Visible ), "Initial fresh-target empty control failed." );
			sphere = new SceneModel( scene.SceneWorld, Model.Sphere, new Transform( center, Rotation.Identity, radius / 50 ) );
			Require( (await Capture( "sphere_control", 0 )).Pixels.Count( Visible ) > 40, "Native sphere visibility control failed." ); sphere.Delete(); sphere = null;
			Require( !(await Capture( "empty_after_sphere", 0 )).Pixels.Any( Visible ), "Post-sphere empty control failed." );
			using ( scene.Push() ) { component = scene.CreateObject().Components.Create<SkinnedModelRenderer>(); component.Model = model; component.UseAnimGraph = false; component.PlayAnimationsInEditorScene = true; component.PlaybackRate = 0; scene.EditorTick( 0, 0 ); }
			Require( component.SceneModel.IsValid(), "Default SkinnedModelRenderer did not create its SceneModel." ); row["component_type"] = component.GetType().FullName;
			using ( scene.Push() ) { second = scene.CreateObject().Components.Create<SkinnedModelRenderer>(); second.WorldPosition = center + new Vector3( radius * 1000, 0, 0 ); second.Model = model; second.UseAnimGraph = false; second.PlayAnimationsInEditorScene = true; second.PlaybackRate = 0; scene.EditorTick( 0, 0 ); }
			Require( second.SceneModel.IsValid() && component.Id != second.Id && ReferenceEquals( second.Model, component.Model ), "Expected two distinct real component instances sharing one model." );
			Transform[] NativePose( SkinnedModelRenderer value ) => Enumerable.Range( 0, model.BoneCount ).Select( value.SceneModel.GetBoneWorldTransform ).ToArray();
			for ( var clipIndex = 0; clipIndex < model.AnimationNames.Count; clipIndex++ )
			{
				var name = model.AnimationNames[clipIndex]; component.Sequence.Name = name; component.Sequence.Looping = false; component.Sequence.Time = 0; component.PlaybackRate = 0;
				second.Sequence.Name = model.AnimationNames[(clipIndex + 1) % model.AnimationNames.Count]; second.Sequence.Looping = false; second.Sequence.TimeNormalized = .37f; second.PlaybackRate = 0;
				using ( scene.Push() ) scene.EditorTick( 0, 0 );
				var duration = component.Sequence.Duration; Require( float.IsFinite( duration ) && duration > 0, "Invalid clip duration: " + name );
				var otherTime = second.Sequence.Time; var otherName = second.Sequence.Name; var otherPose = NativePose( second ); Require( otherPose.Length > 0, "No actual native rig for instance isolation evidence." );
				component.Sequence.Time = duration * .9f; component.PlaybackRate = 1; using ( scene.Push() ) scene.EditorTick( duration * 1.1f, duration * .2f );
				Require( component.Sequence.IsFinished && MathF.Abs( component.Sequence.Time - duration ) < .002f, "Nonlooping clip did not clamp and finish." );
				component.Sequence.Looping = true; component.Sequence.Time = duration * .9f; using ( scene.Push() ) scene.EditorTick( duration * 1.3f, duration * .2f );
				Require( !component.Sequence.IsFinished && MathF.Abs( component.Sequence.Time - duration * .1f ) < .002f, "Looping clip did not wrap exactly." );
				Require( second.Sequence.Time == otherTime && second.Sequence.Name == otherName && NativePose( second ).SequenceEqual( otherPose ), "Primary loop/end checks changed the second instance clock or native pose." );
				component.Sequence.Looping = false; component.PlaybackRate = 0; component.Sequence.Time = 0; using ( scene.Push() ) scene.EditorTick( 0, 0 );
				var sourceClip = sourceJson.RootElement.GetProperty( "animations" )[clipIndex]; var sourceTimelines = new System.Collections.Generic.List<object>(); var stepEdges = new System.Collections.Generic.List<object>();
				var samplerIndex = 0;
				foreach ( var sampler in sourceClip.GetProperty( "samplers" ).EnumerateArray() )
				{
					var input = sampler.GetProperty( "input" ).GetInt32(); var keys = ReadSourceKeyTimes( input ); var interpolation = sampler.TryGetProperty( "interpolation", out var declared ) ? declared.GetString() : "LINEAR";
					sourceTimelines.Add( new { sampler = samplerIndex, input_accessor = input, interpolation, key_times = keys } );
					if ( interpolation == "STEP" && keys.Length >= 3 )
					{
						// One representative interior STEP boundary per authored sampler;
						// complete numerical interpolation conformance remains a separate gate.
						var key = keys[1]; var epsilon = MathF.Max( .00001f, MathF.Min( key - keys[0], keys[2] - key ) * .0001f );
						var edgeCaptures = new System.Collections.Generic.List<object>(); var edgePixels = new System.Collections.Generic.List<Color[]>(); var edgePoses = new System.Collections.Generic.List<Transform[]>();
						foreach ( var edgeTime in new[] { key - epsilon, key, key + epsilon } )
						{
							component.Sequence.Time = edgeTime; using ( scene.Push() ) scene.EditorTick( edgeTime, 0 );
							var edge = await Capture( "clip" + clipIndex + "_sampler" + samplerIndex + "_step" + edgeCaptures.Count, edgeTime ); edgeCaptures.Add( edge.Metadata ); edgePixels.Add( edge.Pixels ); edgePoses.Add( NativePose( component ) );
						}
						var beforeToKey = Difference( edgePixels[0], edgePixels[1] ); var keyToAfter = Difference( edgePixels[1], edgePixels[2] );
						var edgePassed = !edgePoses[0].SequenceEqual( edgePoses[1] ) && edgePoses[1].SequenceEqual( edgePoses[2] ) && beforeToKey > 10 && keyToAfter <= 5;
						stepEdges.Add( new { sampler = samplerIndex, key_index = 1, key, epsilon, passed = edgePassed, before_to_key_pixel_difference = beforeToKey, key_to_after_pixel_difference = keyToAfter, captures = edgeCaptures } );
						Require( edgePassed, "Representative STEP key did not hold then jump at its authored boundary." );
					}
					samplerIndex++;
				}
				component.Sequence.Time = 0; using ( scene.Push() ) scene.EditorTick( 0, 0 );
				var images = new System.Collections.Generic.List<object>(); var first = await Capture( "clip" + clipIndex + "_frame00", 0 ); images.Add( first.Metadata ); Require( first.Pixels.Count( Visible ) > 40, "Loaded model is not visible." );
				component.PlaybackRate = 1; int maxMotion = 0; float maxClockError = 0;
				for ( var frame = 1; frame < 16; frame++ )
				{
					var targetTime = duration * frame / 16; var delta = duration / 16; using ( scene.Push() ) scene.EditorTick( targetTime, delta );
					maxClockError = MathF.Max( maxClockError, MathF.Abs( component.Sequence.Time - targetTime ) );
					Require( second.Sequence.Time == otherTime && second.Sequence.Name == otherName && NativePose( second ).SequenceEqual( otherPose ), "Primary playback changed the second instance clock or native pose." );
					var image = await Capture( "clip" + clipIndex + "_frame" + frame.ToString( "D2" ), component.Sequence.Time ); images.Add( image.Metadata ); maxMotion = Math.Max( maxMotion, Difference( first.Pixels, image.Pixels ) );
				}
				component.PlaybackRate = 0; component.Sequence.Time = 0; using ( scene.Push() ) scene.EditorTick( duration, 0 );
				var reset = await Capture( "clip" + clipIndex + "_seek_reset", 0 ); var resetDifference = Difference( first.Pixels, reset.Pixels );
				var passed = maxMotion > 10 && maxClockError < .002f && resetDifference <= 5;
				clips.Add( new { name, duration, passed, frames = images, max_motion_pixels = maxMotion, max_clock_error = maxClockError, seek_reset_pixel_difference = resetDifference, seek_reset = reset.Metadata,
					source_timelines = sourceTimelines, representative_step_edges = stepEdges,
					nonlooping_end_passed = true, looping_wrap_passed = true, independent_instance_passed = true, primary_component_id = component.Id, secondary_component_id = second.Id, secondary_name = otherName, secondary_paused_time = otherTime,
					secondary_native_bone_positions = otherPose.Select( value => new[] { value.Position.x, value.Position.y, value.Position.z } ).ToArray() } );
				Require( passed, "Clip failed motion/clock/seek-repeatability checks: " + name );
			}
			component.GameObject.Destroy(); second.GameObject.Destroy(); component = null; second = null; using ( scene.Push() ) scene.EditorTick( 0, 0 );
			Require( !(await Capture( "empty_after_model", 0 )).Pixels.Any( Visible ), "Post-model fresh-target empty control failed." ); row["motion"] = "passed"; row["passed"] = true;
		}
		catch ( Exception ex ) when ( ex is not OutOfMemoryException && ex is not OperationCanceledException ) { row["error"] = ex.ToString(); if ( (string)row["motion"] == "not_run" ) row["motion"] = "failed"; }
		finally
		{
			await MainThread.Wait(); if ( preview is not null && preview.IsValid && onFrame is not null ) Clean( "preview callback", () => preview.OnPreFrame -= onFrame );
			if ( sphere.IsValid() ) Clean( "sphere", () => sphere.Delete() ); if ( preview is not null && preview.IsValid ) Clean( "preview", () => { preview.Close(); preview.Destroy(); } );
			if ( scene is not null ) Clean( "private scene", () => scene.Destroy() ); if ( owned ) Clean( "owned model", () => typeof( Model ).GetMethod( "Destroy", flags ).Invoke( model, null ) );
			rows.Add( row ); var frame = Application.FrameCount; var timer = System.Diagnostics.Stopwatch.StartNew();
			while ( Application.FrameCount < frame + 3 && timer.Elapsed < TimeSpan.FromSeconds( 5 ) ) { await Task.Delay( 30, cancellationToken ); await MainThread.Wait(); }
			Require( cleanupErrors.Count == 0 && Application.FrameCount >= frame + 3, "Cleanup failed; stop allocating." );
		}
	}
	phase = "complete";
}
catch ( Exception ex ) { error = ex.ToString(); }
finally
{
	await MainThread.Wait(); if ( mounted ) Clean( "unmount", () => unmount.Invoke( aggregate, new object[] { mount } ) );
	if ( redirectObject is not null ) foreach ( var alias in aliases ) Clean( "redirect", () => removeRedirect.Invoke( redirectObject, new object[] { alias } ) );
	if ( redirect is not null ) Clean( "redirect filesystem", () => redirect.Dispose() ); if ( removeLog is not null && logHandler is not null ) Clean( "logging", () => removeLog.Invoke( null, new object[] { logHandler } ) );
}
var receipt = new { passed = error is null && phase == "complete" && rows.Count == selectedIds.Length * 2 && rows.All( row => (bool)row["passed"] ) && cleanupErrors.Count == 0, phase, error, cases = rows, cleanup_errors = cleanupErrors, fixture_sha256 = hashes, relevant_messages = messages,
	scope = "Actual Model.Load original and packaging-only compiled forms, existing SkinnedModelRenderer with normal Scene.EditorTick, 16 actual GPU frames per clip, clock and seek-repeatability checks. Motion evidence is not independent reference-animation or full appearance conformance.",
	engine_mvid = engine.ManifestModule.ModuleVersionId.ToString(), gltf_mvid = typeof( Sandbox.Gltf.GltfReader ).Assembly.ManifestModule.ModuleVersionId.ToString(), output,
	runner_sha256 = Hash( System.IO.File.ReadAllBytes( System.IO.Path.Combine( checkout.FullName, "engine/Tools/Gltf/editor/editor_morph_smoke.csx" ) ) ), corpus_commit = "90d7ede14c7e280af263824604b427a1ca02cb66", full_animation_parity_verified = false };
System.IO.File.WriteAllText( System.IO.Path.Combine( output, "receipt.json" ), System.Text.Json.JsonSerializer.Serialize( receipt, new System.Text.Json.JsonSerializerOptions { WriteIndented = true } ) );
return receipt;
