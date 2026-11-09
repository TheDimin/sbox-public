// Actual Model.Load morph proof. References are independently constructed from
// literal source positions/deltas; no helper constructs the tested GPU model.
var engine = typeof( Model ).Assembly;
var checkout = new System.IO.DirectoryInfo( System.IO.Path.GetDirectoryName( engine.Location ) );
while ( checkout is not null && !System.IO.File.Exists( System.IO.Path.Combine( checkout.FullName, "engine", "Sandbox-Engine.slnx" ) ) ) checkout = checkout.Parent;
if ( checkout is null ) throw new InvalidOperationException( "Cannot locate checkout." );
var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static;
var id = Guid.NewGuid().ToString( "N" ); var output = System.IO.Path.Combine( checkout.FullName, "Temp/gltf-validation/editor-morph-pose", id ); var prefix = "gltf-morph-pose/" + id;
var captures = new System.Collections.Generic.List<object>(); var cases = new System.Collections.Generic.List<object>(); var errors = new System.Collections.Generic.List<string>();
var ownedModels = new System.Collections.Generic.List<Model>(); var ownedMeshes = new System.Collections.Generic.List<Mesh>(); var aliases = new System.Collections.Generic.List<string>();
var normalReferences = new System.Collections.Generic.List<float[]>();
Scene scene = null; CameraComponent camera = null; Editor.SceneRenderingWidget preview = null;
SkinnedModelRenderer actor = null, second = null; SceneObject reference = null;
Material material = null; object redirectObject = null, renderer = null; IDisposable redirect = null;
BaseFileSystem aggregate = null, mount = null; System.Reflection.MethodInfo unmount = null, removeAlias = null, render = null;
Action onFrame = null; int frames = 0; float tickTime = 0; string phase = "initialize", failure = null;
const float inches = 1f / .0254f;
var literals = new[] { new Vector2( -.6f, -.5f ), new Vector2( .6f, -.5f ), new Vector2( 0, .5f ) };
void Require( bool value, string message ) { if ( !value ) throw new InvalidOperationException( message ); }
void Clean( string label, Action action ) { try { action(); } catch ( Exception e ) { errors.Add( label + ": " + e ); } }
string Hash( byte[] bytes ) => Convert.ToHexStringLower( System.Security.Cryptography.SHA256.HashData( bytes ) );
async Task Safe()
{
	await MainThread.Wait(); Require( Application.IsEditor && !Game.IsPlaying, "Requires the idle physical editor." );
	var timer = System.Diagnostics.Stopwatch.StartNew(); while ( Graphics.IsActive ) { Require( timer.Elapsed < TimeSpan.FromSeconds( 5 ), "No safe graphics boundary." ); await Task.Delay( 20, cancellationToken ); await MainThread.Wait(); }
}
void Tick() { tickTime += .001f; scene.EditorTick( tickTime, .001f ); }
async Task WaitFrames()
{
	var before = frames; var timer = System.Diagnostics.Stopwatch.StartNew();
	while ( frames < before + 3 ) { Require( timer.Elapsed < TimeSpan.FromSeconds( 8 ), "Private preview stopped receiving frames." ); await Task.Delay( 30, cancellationToken ); await Safe(); }
}
byte[] Fixture( bool animated, float value )
{
	var root = System.Text.Json.Nodes.JsonNode.Parse( System.IO.File.ReadAllText( System.IO.Path.Combine( checkout.FullName, "research/gltf-validation/fixtures/signed_morph.gltf" ) ) );
	var uri = root["buffers"][0]["uri"].GetValue<string>(); var original = Convert.FromBase64String( uri[(uri.IndexOf( ',' ) + 1)..] );
	Require( original.Length == 132, "The pinned diagnostic's binary layout changed." );
	var bytes = new byte[168]; original.CopyTo( bytes, 0 );
	System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian( bytes.AsSpan( 124, 4 ), value ); System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian( bytes.AsSpan( 128, 4 ), -value );
	for ( var vertex = 0; vertex < 3; vertex++ ) { var at = 132 + vertex * 12; System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian( bytes.AsSpan( at, 4 ), .25f ); System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian( bytes.AsSpan( at + 4, 4 ), .5f ); System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian( bytes.AsSpan( at + 8, 4 ), -.2f ); }
	root["bufferViews"].AsArray().Add( new System.Text.Json.Nodes.JsonObject { ["buffer"] = 0, ["byteOffset"] = 132, ["byteLength"] = 36 } );
	root["accessors"].AsArray().Add( new System.Text.Json.Nodes.JsonObject { ["bufferView"] = 5, ["componentType"] = 5126, ["count"] = 3, ["type"] = "VEC3" } ); root["meshes"][0]["primitives"][0]["targets"][0]["NORMAL"] = 5;
	root["buffers"][0]["byteLength"] = bytes.Length; root["buffers"][0]["uri"] = "data:application/octet-stream;base64," + Convert.ToBase64String( bytes );
	root["asset"]["generator"] = "CC0 actual-loader signed/local-normal morph diagnostic";
	if ( animated ) root["animations"][0]["name"] = "Signed";
	else
	{
		root.AsObject().Remove( "animations" ); root["meshes"][0]["weights"] = new System.Text.Json.Nodes.JsonArray( value );
		root["nodes"] = new System.Text.Json.Nodes.JsonArray( new System.Text.Json.Nodes.JsonObject { ["mesh"] = 0 }, new System.Text.Json.Nodes.JsonObject { ["mesh"] = 0, ["weights"] = new System.Text.Json.Nodes.JsonArray( -2f ), ["translation"] = new System.Text.Json.Nodes.JsonArray( 0, 1.5f, 0 ), ["scale"] = new System.Text.Json.Nodes.JsonArray( 1, 2, .5f ) } );
		root["scenes"][0]["nodes"] = new System.Text.Json.Nodes.JsonArray( 0, 1 );
	}
	return System.Text.Encoding.UTF8.GetBytes( root.ToJsonString() );
}
void Put( string name, byte[] bytes )
{
	var path = System.IO.Path.Combine( output, name ); System.IO.File.WriteAllBytes( path, bytes ); var alias = "/" + prefix + "/" + name;
	redirectObject.GetType().GetMethod( "AddAbsFile", flags ).Invoke( redirectObject, new object[] { alias, path } ); aliases.Add( alias );
}
SkinnedModelRenderer Create( Model model, bool animated, bool hidden )
{
	using var scope = scene.Push(); var result = scene.CreateObject().Components.Create<SkinnedModelRenderer>( false );
	result.Model = model; result.UseAnimGraph = false; result.PlaybackRate = 0; result.PlayAnimationsInEditorScene = true; result.WorldPosition = hidden ? new Vector3( 0, 10000, 0 ) : Vector3.Zero; result.Enabled = true;
	if ( animated ) { result.Sequence.Name = "Signed"; result.Sequence.Looping = false; }
	Require( result.SceneObject is SceneModel && result.SceneObject.IsValid(), "The real component did not create a SceneModel." ); return result;
}
Model Reference( float weight, bool twoNodes )
{
	var vertices = new System.Collections.Generic.List<Vertex>(); var indices = new System.Collections.Generic.List<int>();
	for ( var node = 0; node < (twoNodes ? 2 : 1); node++ )
	{
		var w = node == 0 ? weight : -2f; var scaleY = node == 0 ? 1f : 2f; var scaleZ = node == 0 ? 1f : .5f; var yTranslation = node == 0 ? 0f : 1.5f;
		// Source N=(.25*w,.5*w,1-.2*w); apply inverse transpose of
		// the literal node scale, then the explicit cyclic coordinate basis.
		var normal = new Vector3( (1 - .2f * w) / scaleZ, .25f * w, .5f * w / scaleY ).Normal;
		var tangent = Vector3.Cross( Vector3.Up, normal ); if ( tangent.Length < .1f ) tangent = Vector3.Cross( Vector3.Forward, normal ); tangent = tangent.Normal;
		foreach ( var p in literals )
		{
			var position = new Vector3( 0, (p.x + .25f * w) * inches, (p.y * scaleY + yTranslation) * inches );
			indices.Add( vertices.Count ); vertices.Add( new Vertex( position ) { Normal = normal, Tangent = new Vector4( tangent.x, tangent.y, tangent.z, -1 ), Color = Color32.White } );
		}
	}
	var mesh = new Mesh( "independent_literal_morph_reference", material ); mesh.CreateVertexBuffer( vertices.Count, vertices ); mesh.CreateIndexBuffer( indices.Count, indices ); mesh.Bounds = BBox.FromPoints( vertices.Select( v => v.Position ) ); ownedMeshes.Add( mesh );
	var model = Model.Builder.WithName( "morph_reference_" + Guid.NewGuid().ToString( "N" ) ).AddMesh( mesh ).Create(); Require( model is { IsValid: true, IsError: false }, "Native reference failed." ); ownedModels.Add( model ); return model;
}
async Task<Color[]> Capture( string name, SceneCameraDebugMode mode, bool settleBefore = true )
{
	phase = name; camera.DebugMode = mode; if ( settleBefore ) await WaitFrames(); using var target = Texture.CreateRenderTarget().WithSize( 192, 192 ).WithFormat( ImageFormat.RGBA8888 ).WithInitialColor( Color.Magenta ).Create( "morph_pose_" + Guid.NewGuid().ToString( "N" ) );
	using ( scene.Push() ) { typeof( Scene ).GetMethod( "PreCameraRender", flags ).Invoke( scene, null ); typeof( CameraComponent ).GetMethod( "InitializeRendering", flags ).Invoke( camera, new object[] { false } ); target.Clear( Color.Magenta ); Require( (bool)render.Invoke( renderer, new object[] { camera, target } ), "Managed capture submission failed." ); }
	await WaitFrames(); using var bitmap = target.GetBitmap(); Require( bitmap is not null, "Readback failed." ); var png = bitmap.ToPng(); var path = System.IO.Path.Combine( output, name + ".png" ); System.IO.File.WriteAllBytes( path, png );
	captures.Add( new { name, camera_initialized_after_debug_mode = true, mode = mode.ToString(), settleBefore, path, sha256 = Hash( png ), width = bitmap.Width, height = bitmap.Height, frames, frame = Application.FrameCount } ); return bitmap.GetPixels();
}
bool Visible( Color p ) => p.r > .03f || p.g > .03f || p.b > .03f;
async Task Empty( string label )
{
	Require( !(await Capture( label + "_albedo", SceneCameraDebugMode.Albedo )).Any( Visible ), "Fresh albedo empty control failed." );
	Require( !(await Capture( label + "_normals", SceneCameraDebugMode.NormalMap )).Any( Visible ), "Fresh normal empty control failed." );
}
void RemoveActors()
{
	if ( actor is not null ) { actor.Enabled = false; actor.GameObject.Destroy(); actor = null; }
	if ( second is not null ) { second.Enabled = false; second.GameObject.Destroy(); second = null; }
	if ( reference.IsValid() ) reference.Delete(); reference = null;
	if ( scene is not null ) { using var scope = scene.Push(); scene.ProcessDeletes(); }
}
try
{
	await Safe(); System.IO.Directory.CreateDirectory( output );
	material = Material.Load( "materials/default/white.vmat" ).CreateCopy( "morph_pose_reference_" + id ); material.SetFeature( "F_RENDER_BACKFACES", 0 );
	var managed = engine.GetType( "Sandbox.Rendering.ManagedSceneRendering", true ); renderer = managed.GetProperty( "Renderer", flags ).GetValue( null ); Require( renderer is not null, "Managed scene renderer unavailable." ); render = renderer.GetType().GetMethod( "RenderToTexture", flags, null, new[] { typeof( CameraComponent ), typeof( Texture ) }, null ); Require( render is not null, "Managed capture API unavailable." );
	var redirectType = engine.GetType( "Sandbox.Internal.RedirectFileSystem", true ); redirectObject = redirectType.GetMethod( "Create", flags ).Invoke( null, new object[] { output } ); redirect = (IDisposable)redirectObject; removeAlias = redirectType.GetMethod( "RemoveAbsFile", flags );
	var ctor = typeof( BaseFileSystem ).GetConstructors( flags ).Single( c => { var p = c.GetParameters(); return p.Length == 1 && p[0].ParameterType.IsInstanceOfType( redirectObject ); } ); mount = (BaseFileSystem)ctor.Invoke( new[] { redirectObject } ); aggregate = Sandbox.FileSystem.Mounted; Require( aggregate is not null, "Mounted filesystem unavailable." ); unmount = typeof( BaseFileSystem ).GetMethod( "UnMount", flags, null, new[] { typeof( BaseFileSystem ) }, null ); typeof( BaseFileSystem ).GetMethod( "Mount", flags, null, new[] { typeof( BaseFileSystem ) }, null ).Invoke( aggregate, new object[] { mount } );
	scene = Scene.CreateEditorScene(); using ( scene.Push() ) { camera = scene.CreateObject().Components.Create<CameraComponent>(); camera.WorldPosition = new Vector3( 200, 0, inches ); camera.WorldRotation = Rotation.FromYaw( 180 ); camera.Orthographic = true; camera.OrthographicHeight = 160; camera.ZNear = 1; camera.ZFar = 1000; camera.BackgroundColor = Color.Black; camera.DebugMode = SceneCameraDebugMode.Albedo; camera.EnablePostProcessing = false; }
	preview = new Editor.SceneRenderingWidget { Scene = scene, Camera = camera, WindowTitle = "glTF actual signed/static morph + local normal proof", Size = new Vector2( 384, 384 ) }; onFrame = () => frames++; preview.OnPreFrame += onFrame; preview.Show();
	foreach ( var definition in new[] { (Name: "animated_one", Animated: true, Value: 1f), (Name: "animated_two", Animated: true, Value: 2f), (Name: "static_quarter", Animated: false, Value: .25f), (Name: "static_two", Animated: false, Value: 2f) } )
	{
		var source = Fixture( definition.Animated, definition.Value ); Put( definition.Name + ".gltf", source ); Put( definition.Name + ".glb_c", Sandbox.Gltf.GltfCompiler.Compile( source ) );
		foreach ( var form in new[] { ".gltf", ".glb_c" } )
		{
			var loaded = Model.Load( prefix + "/" + definition.Name + form ); Require( loaded is { IsValid: true, IsError: false }, "Actual morph Model.Load failed." ); ownedModels.Add( loaded );
			Require( loaded.AnimationCount == (definition.Animated ? 1 : 0), "Unexpected source animation count." );
			foreach ( var sample in definition.Animated ? new[] { (Time: 0f, Weight: 0f, BindPose: false), (Time: 1f, Weight: definition.Value, BindPose: false), (Time: 2f, Weight: -definition.Value, BindPose: false), (Time: 2f, Weight: 0f, BindPose: true) } : new[] { (Time: 0f, Weight: definition.Value, BindPose: false) } )
			{
				var label = definition.Name + (form == ".gltf" ? "_source" : "_compiled") + "_time_" + sample.Time + (sample.BindPose ? "_reset_bind_pose" : "");
				RemoveActors(); await Empty( label + "_before" ); actor = Create( loaded, definition.Animated, false ); second = Create( loaded, definition.Animated, true );
				if ( definition.Animated ) { actor.Sequence.TimeNormalized = sample.Time / 2; second.Sequence.TimeNormalized = .5f; }
				Tick(); if ( sample.BindPose ) ((SceneModel)actor.SceneObject).UpdateToBindPose();
				// Submit immediately after the first actual update, before preview frames
				// can hide a one-frame morph evaluation lag. Readback still waits for GPU.
				var actualAlbedo = await Capture( label + "_actual", SceneCameraDebugMode.Albedo, settleBefore: false );
				if ( sample.BindPose ) ((SceneModel)actor.SceneObject).UpdateToBindPose();
				var actualNormals = await Capture( label + "_actual_normals", SceneCameraDebugMode.NormalMap, settleBefore: false );
				var isolation = !definition.Animated || MathF.Abs( second.Sequence.Time - 1 ) < .00001f && MathF.Abs( actor.Sequence.Time - sample.Time ) < .00001f;
				if ( definition.Animated )
				{
					var secondMorph = ((SceneModel)second.SceneObject).Morphs.Get( "gltf_node_0_morph_0_positive" ); isolation &= MathF.Abs( secondMorph - definition.Value ) < .00001f;
				}
				RemoveActors(); await Empty( label + "_after_actual" );
				var expected = Reference( sample.Weight, !definition.Animated ); reference = new SceneObject( scene.SceneWorld, expected, Transform.Zero );
				var expectedAlbedo = await Capture( label + "_reference", SceneCameraDebugMode.Albedo ); var expectedNormals = await Capture( label + "_reference_normals", SceneCameraDebugMode.NormalMap ); reference.Delete(); reference = null; await Empty( label + "_after_reference" );
				var intersection = 0; var union = 0; var actualCoverage = 0; var expectedCoverage = 0; double normalError = 0; var normalMean = new float[3];
				for ( var pixel = 0; pixel < actualAlbedo.Length; pixel++ )
				{
					var a = Visible( actualAlbedo[pixel] ); var b = Visible( expectedAlbedo[pixel] ); if ( a ) actualCoverage++; if ( b ) expectedCoverage++; if ( a || b ) union++;
					if ( a && b ) { intersection++; normalError += Math.Abs( actualNormals[pixel].r - expectedNormals[pixel].r ) + Math.Abs( actualNormals[pixel].g - expectedNormals[pixel].g ) + Math.Abs( actualNormals[pixel].b - expectedNormals[pixel].b ); normalMean[0] += expectedNormals[pixel].r; normalMean[1] += expectedNormals[pixel].g; normalMean[2] += expectedNormals[pixel].b; }
				}
				var iou = union > 0 ? (double)intersection / union : 0; var meanNormalError = intersection > 0 ? normalError / (intersection * 3) : 1;
				if ( intersection > 0 ) for ( var channel = 0; channel < 3; channel++ ) normalMean[channel] /= intersection; normalReferences.Add( normalMean );
				cases.Add( new { label, sample.Time, sample.Weight, sample.BindPose, actualCoverage, expectedCoverage, iou, meanNormalError, expectedNormalMean = normalMean, isolation, passed = actualCoverage > 30 && expectedCoverage > 30 && iou > .97 && meanNormalError < .015 && isolation, source_sha256 = Hash( source ) } );
				RemoveActors();
			}
		}
	}
	phase = "complete";
}
catch ( Exception e ) { failure = e.ToString(); }
finally
{
	await MainThread.Wait(); RemoveActors();
	if ( preview is not null && preview.IsValid && onFrame is not null ) preview.OnPreFrame -= onFrame; if ( preview is not null && preview.IsValid ) { preview.Close(); preview.Destroy(); } scene?.Destroy();
	foreach ( var model in ownedModels.Distinct() ) Clean( "model", () => typeof( Model ).GetMethod( "Destroy", flags ).Invoke( model, null ) );
	if ( ownedMeshes.Count > 0 ) Clean( "reference meshes", () => engine.GetType( "Sandbox.GltfModelLoader", true ).GetMethod( "ReleaseMeshes", flags ).Invoke( null, new object[] { ownedMeshes } ) );
	if ( material is not null ) Clean( "owned reference material", () => typeof( Material ).GetMethod( "Destroy", flags ).Invoke( material, null ) );
	if ( aggregate is not null && mount is not null && unmount is not null ) Clean( "unmount", () => unmount.Invoke( aggregate, new object[] { mount } ) );
	if ( redirectObject is not null ) foreach ( var alias in aliases ) Clean( "redirect", () => removeAlias.Invoke( redirectObject, new object[] { alias } ) ); redirect?.Dispose();
}
var normalSensitivity = normalReferences.Count > 1 ? normalReferences.Max( a => normalReferences.Max( b => Math.Abs( a[0] - b[0] ) + Math.Abs( a[1] - b[1] ) + Math.Abs( a[2] - b[2] ) ) ) : 0;
var receipt = new { passed = failure is null && errors.Count == 0 && cases.Count == 20 && normalSensitivity > .05f && cases.All( item => (bool)item.GetType().GetProperty( "passed" ).GetValue( item ) ), phase, failure, errors, cases, captures, normal_reference_sensitivity = normalSensitivity, output, renderer = "managed SceneBridge.RenderToTexture; global renderer unchanged", engine_mvid = engine.ManifestModule.ModuleVersionId, gltf_mvid = typeof( Sandbox.Gltf.GltfReader ).Assembly.ManifestModule.ModuleVersionId, source_sha256 = Hash( System.IO.File.ReadAllBytes( System.IO.Path.Combine( checkout.FullName, "engine/Tools/Gltf/editor/editor_morph_pose.csx" ) ) ), scope = "Actual Model.Load source/compiled signed clip morphs0/+1/-1/+2/-2, first-update capture and explicit bind reset preserving selected clock, static mesh defaults and node override-2, independent component clocks/controls, local normal deltas before nonuniform node scale, independent literal native raster references and fresh empty controls. No joint-animation timing, tangent morph, regenerated-flat-normal, authored morph identity, manual override or default native renderer claim." };
System.IO.File.WriteAllText( System.IO.Path.Combine( output, "receipt.json" ), System.Text.Json.JsonSerializer.Serialize( receipt, new System.Text.Json.JsonSerializerOptions { WriteIndented = true } ) ); return receipt;
