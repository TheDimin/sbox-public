// Actual Model.Load coordinate/culling diagnostic. Independent stock native
// references use literal axis permutation, never the conversion helper under test.
var engine = typeof( Model ).Assembly;
var checkout = new System.IO.DirectoryInfo( System.IO.Path.GetDirectoryName( engine.Location ) );
while ( checkout is not null && !System.IO.File.Exists( System.IO.Path.Combine( checkout.FullName, "engine", "Sandbox-Engine.slnx" ) ) ) checkout = checkout.Parent;
if ( checkout is null ) throw new InvalidOperationException( "Cannot locate checkout." );
var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static;
var id = Guid.NewGuid().ToString( "N" );
var output = System.IO.Path.Combine( checkout.FullName, "Temp/gltf-validation/editor-orientation-axes", id );
var prefix = "gltf-orientation-axes/" + id;
var nativeCapture = Environment.GetEnvironmentVariable( "SBOX_GLTF_AXES_NATIVE" ) == "1";
var captures = new System.Collections.Generic.List<object>(); var rows = new System.Collections.Generic.List<object>();
var errors = new System.Collections.Generic.List<string>(); var aliases = new System.Collections.Generic.List<string>();
var ownedModels = new System.Collections.Generic.List<Model>(); var ownedMeshes = new System.Collections.Generic.List<Mesh>();
var shown = new System.Collections.Generic.List<SceneObject>();
Scene scene = null; CameraComponent camera = null; Editor.SceneRenderingWidget preview = null;
Material stockControl = null; object redirectObject = null; IDisposable redirect = null;
BaseFileSystem aggregate = null, mount = null; System.Reflection.MethodInfo unmount = null, removeAlias = null;
object renderer = null; System.Reflection.MethodInfo render = null;
Action onFrame = null; int frames = 0; string phase = "initialize", failure = null;
const float inches = 1f / .0254f;
var jsonOptions = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
var definitions = new[]
{
	(Name: "positive_x_red", N: new Vector3( 1, 0, 0 ), U: new Vector3( 0, 1, 0 ), V: new Vector3( 0, 0, 1 ), C: new Color( 1, 0, 0 )),
	(Name: "negative_x_cyan", N: new Vector3( -1, 0, 0 ), U: new Vector3( 0, -1, 0 ), V: new Vector3( 0, 0, 1 ), C: new Color( 0, 1, 1 )),
	(Name: "positive_y_green", N: new Vector3( 0, 1, 0 ), U: new Vector3( 0, 0, 1 ), V: new Vector3( 1, 0, 0 ), C: new Color( 0, 1, 0 )),
	(Name: "negative_y_magenta", N: new Vector3( 0, -1, 0 ), U: new Vector3( 0, 0, -1 ), V: new Vector3( 1, 0, 0 ), C: new Color( 1, 0, 1 )),
	(Name: "positive_z_blue", N: new Vector3( 0, 0, 1 ), U: new Vector3( 1, 0, 0 ), V: new Vector3( 0, 1, 0 ), C: new Color( 0, 0, 1 )),
	(Name: "negative_z_yellow", N: new Vector3( 0, 0, -1 ), U: new Vector3( -1, 0, 0 ), V: new Vector3( 0, 1, 0 ), C: new Color( 1, 1, 0 ))
};
Vector3[] Points( int axis )
{
	var d = definitions[axis]; var center = d.N * 1.05f;
	return new[] { center - d.U * .3f - d.V * .2f, center + d.U * .4f - d.V * .2f, center - d.U * .1f + d.V * .5f };
}
Vector3 Expected( Vector3 p, bool negative ) => new Vector3( p.z + .75f, (negative ? -p.x : p.x) + .25f, p.y - .5f ) * inches;
Vector3 ExpectedNormal( Vector3 n, bool negative ) => new( n.z, negative ? -n.x : n.x, n.y );
float[] XYZ( Vector3 p ) => new[] { p.x, p.y, p.z };
string Hash( byte[] bytes ) => Convert.ToHexStringLower( System.Security.Cryptography.SHA256.HashData( bytes ) );
void Require( bool value, string message ) { if ( !value ) throw new InvalidOperationException( message ); }
void Clean( string label, Action action ) { try { action(); } catch ( Exception e ) { errors.Add( label + ": " + e ); } }
async Task Safe()
{
	await MainThread.Wait(); Require( Application.IsEditor && !Game.IsPlaying, "Editor must be idle." );
	var timer = System.Diagnostics.Stopwatch.StartNew();
	while ( Graphics.IsActive ) { Require( timer.Elapsed < TimeSpan.FromSeconds( 5 ), "No safe graphics boundary." ); await Task.Delay( 20, cancellationToken ); await MainThread.Wait(); }
}
async Task WaitFrames()
{
	var before = frames; var timer = System.Diagnostics.Stopwatch.StartNew();
	while ( frames < before + 3 ) { Require( timer.Elapsed < TimeSpan.FromSeconds( 8 ), "Preview stopped receiving frames." ); await Task.Delay( 30, cancellationToken ); await Safe(); }
}
byte[] Fixture( bool negative )
{
	using var bytes = new System.IO.MemoryStream(); using var writer = new System.IO.BinaryWriter( bytes );
	var meshes = new System.Text.Json.Nodes.JsonArray(); var materials = new System.Text.Json.Nodes.JsonArray();
	var views = new System.Text.Json.Nodes.JsonArray(); var accessors = new System.Text.Json.Nodes.JsonArray();
	var nodes = new System.Text.Json.Nodes.JsonArray(); var children = new System.Text.Json.Nodes.JsonArray();
	nodes.Add( new System.Text.Json.Nodes.JsonObject { ["name"] = "translated_root", ["translation"] = new System.Text.Json.Nodes.JsonArray( .25f, -.5f, .75f ), ["scale"] = new System.Text.Json.Nodes.JsonArray( negative ? -1 : 1, 1, 1 ), ["children"] = children } );
	for ( var axis = 0; axis < definitions.Length; axis++ )
	{
		var points = Points( axis ); var start = (int)bytes.Position;
		foreach ( var p in points ) { writer.Write( p.x ); writer.Write( p.y ); writer.Write( p.z ); }
		views.Add( new System.Text.Json.Nodes.JsonObject { ["buffer"] = 0, ["byteOffset"] = start, ["byteLength"] = 36 } );
		var min = points.Aggregate( (a, b) => Vector3.Min( a, b ) ); var max = points.Aggregate( (a, b) => Vector3.Max( a, b ) );
		accessors.Add( new System.Text.Json.Nodes.JsonObject { ["bufferView"] = views.Count - 1, ["componentType"] = 5126, ["type"] = "VEC3", ["count"] = 3, ["min"] = new System.Text.Json.Nodes.JsonArray( min.x, min.y, min.z ), ["max"] = new System.Text.Json.Nodes.JsonArray( max.x, max.y, max.z ) } ); var position = accessors.Count - 1;
		start = (int)bytes.Position; var n = definitions[axis].N;
		for ( var vertex = 0; vertex < 3; vertex++ ) { writer.Write( n.x ); writer.Write( n.y ); writer.Write( n.z ); }
		views.Add( new System.Text.Json.Nodes.JsonObject { ["buffer"] = 0, ["byteOffset"] = start, ["byteLength"] = 36 } );
		accessors.Add( new System.Text.Json.Nodes.JsonObject { ["bufferView"] = views.Count - 1, ["componentType"] = 5126, ["type"] = "VEC3", ["count"] = 3 } ); var normal = accessors.Count - 1;
		var color = definitions[axis].C;
		materials.Add( new System.Text.Json.Nodes.JsonObject { ["name"] = definitions[axis].Name, ["doubleSided"] = false, ["pbrMetallicRoughness"] = new System.Text.Json.Nodes.JsonObject { ["baseColorFactor"] = new System.Text.Json.Nodes.JsonArray( color.r, color.g, color.b, 1 ), ["metallicFactor"] = 0, ["roughnessFactor"] = 1 } } );
		meshes.Add( new System.Text.Json.Nodes.JsonObject { ["name"] = definitions[axis].Name, ["primitives"] = new System.Text.Json.Nodes.JsonArray( new System.Text.Json.Nodes.JsonObject { ["attributes"] = new System.Text.Json.Nodes.JsonObject { ["POSITION"] = position, ["NORMAL"] = normal }, ["material"] = axis } ) } );
		children.Add( nodes.Count ); nodes.Add( new System.Text.Json.Nodes.JsonObject { ["name"] = definitions[axis].Name, ["mesh"] = axis } );
	}
	var root = new System.Text.Json.Nodes.JsonObject { ["asset"] = new System.Text.Json.Nodes.JsonObject { ["version"] = "2.0", ["generator"] = "CC0 asymmetric orientation diagnostic" }, ["scene"] = 0, ["scenes"] = new System.Text.Json.Nodes.JsonArray( new System.Text.Json.Nodes.JsonObject { ["nodes"] = new System.Text.Json.Nodes.JsonArray( 0 ) } ), ["nodes"] = nodes, ["meshes"] = meshes, ["materials"] = materials, ["bufferViews"] = views, ["accessors"] = accessors, ["buffers"] = new System.Text.Json.Nodes.JsonArray( new System.Text.Json.Nodes.JsonObject { ["byteLength"] = bytes.Length, ["uri"] = "data:application/octet-stream;base64," + Convert.ToBase64String( bytes.ToArray() ) } ) };
	return System.Text.Encoding.UTF8.GetBytes( root.ToJsonString() );
}
void Put( string name, byte[] bytes )
{
	var path = System.IO.Path.Combine( output, name ); System.IO.File.WriteAllBytes( path, bytes );
	var alias = "/" + prefix + "/" + name; redirectObject.GetType().GetMethod( "AddAbsFile", flags ).Invoke( redirectObject, new object[] { alias, path } ); aliases.Add( alias );
}
void Hide()
{
	foreach ( var obj in shown ) if ( obj.IsValid() ) obj.Delete(); shown.Clear();
}
void ShowControls( bool negative, bool reverse )
{
	for ( var axis = 0; axis < definitions.Length; axis++ )
	{
		var normal = ExpectedNormal( definitions[axis].N, negative ); var tangent = Vector3.Cross( Vector3.Up, normal ); if ( tangent.Length < .1f ) tangent = Vector3.Cross( Vector3.Forward, normal ); tangent = tangent.Normal;
		var vertices = Points( axis ).Select( p => new Vertex( Expected( p, negative ) ) { Normal = normal, Tangent = new Vector4( tangent.x, tangent.y, tangent.z, -1 ), Color = Color32.White } ).ToArray();
		var indices = negative != reverse ? new[] { 0, 2, 1 } : new[] { 0, 1, 2 };
		var mesh = new Mesh( "independent_axis_control", stockControl ); mesh.CreateVertexBuffer( 3, vertices.AsSpan() ); mesh.CreateIndexBuffer( 3, indices.AsSpan() ); mesh.Bounds = BBox.FromPoints( vertices.Select( v => v.Position ) ); ownedMeshes.Add( mesh );
		var model = Model.Builder.WithName( "axis_reference_" + Guid.NewGuid().ToString( "N" ) ).AddMesh( mesh ).Create(); ownedModels.Add( model );
		Require( model is { IsValid: true, IsError: false }, "Native stock reference model failed." );
		shown.Add( new SceneObject( scene.SceneWorld, model, Transform.Zero ) { ColorTint = definitions[axis].C } );
	}
}
async Task<Color[]> Capture( string label )
{
	phase = label; await WaitFrames();
	using var target = Texture.CreateRenderTarget().WithSize( 192, 192 ).WithFormat( ImageFormat.RGBA8888 ).WithInitialColor( Color.Magenta ).Create( "axis_capture_" + Guid.NewGuid().ToString( "N" ) );
	using ( scene.Push() ) { target.Clear( Color.Magenta ); Require( nativeCapture ? camera.RenderToTexture( target ) : (bool)render.Invoke( renderer, new object[] { camera, target } ), "Capture submission failed." ); }
	await WaitFrames(); using var bitmap = target.GetBitmap(); Require( bitmap is not null, "Capture readback failed." );
	var png = bitmap.ToPng(); var path = System.IO.Path.Combine( output, label + ".png" ); System.IO.File.WriteAllBytes( path, png );
	captures.Add( new { label, path, sha256 = Hash( png ), width = bitmap.Width, height = bitmap.Height, frames, frame = Application.FrameCount } ); return bitmap.GetPixels();
}
int Classify( Color p )
{
	var r = p.r > .15f; var g = p.g > .15f; var b = p.b > .15f;
	if ( r && !g && !b ) return 0; if ( !r && g && b ) return 1; if ( !r && g && !b ) return 2;
	if ( r && !g && b ) return 3; if ( !r && !g && b ) return 4; if ( r && g && !b ) return 5;
	return -1;
}
bool Nonblack( Color p ) => p.r > .03f || p.g > .03f || p.b > .03f;
try
{
	await Safe(); System.IO.Directory.CreateDirectory( output );
	stockControl = Material.Load( "materials/default/white.vmat" ).CreateCopy( "axis_stock_control_" + id ); stockControl.SetFeature( "F_RENDER_BACKFACES", 0 );
	if ( !nativeCapture )
	{
		var bridge = engine.GetType( "Sandbox.Rendering.ManagedSceneRendering", true ); renderer = bridge.GetProperty( "Renderer", flags ).GetValue( null ); Require( renderer is not null, "Managed capture renderer unavailable." );
		render = renderer.GetType().GetMethod( "RenderToTexture", flags, null, new[] { typeof( CameraComponent ), typeof( Texture ) }, null ); Require( render is not null, "Managed capture API unavailable." );
	}
	var redirectType = engine.GetType( "Sandbox.Internal.RedirectFileSystem", true ); redirectObject = redirectType.GetMethod( "Create", flags ).Invoke( null, new object[] { output } ); redirect = (IDisposable)redirectObject; removeAlias = redirectType.GetMethod( "RemoveAbsFile", flags );
	var ctor = typeof( BaseFileSystem ).GetConstructors( flags ).Single( c => { var p = c.GetParameters(); return p.Length == 1 && p[0].ParameterType.IsInstanceOfType( redirectObject ); } );
	mount = (BaseFileSystem)ctor.Invoke( new[] { redirectObject } ); aggregate = Sandbox.FileSystem.Mounted; Require( aggregate is not null, "Mounted filesystem unavailable." ); unmount = typeof( BaseFileSystem ).GetMethod( "UnMount", flags, null, new[] { typeof( BaseFileSystem ) }, null );
	typeof( BaseFileSystem ).GetMethod( "Mount", flags, null, new[] { typeof( BaseFileSystem ) }, null ).Invoke( aggregate, new object[] { mount } );
	scene = Scene.CreateEditorScene(); using ( scene.Push() ) { camera = scene.CreateObject().Components.Create<CameraComponent>(); camera.BackgroundColor = Color.Black; camera.FieldOfView = 50; camera.ZNear = 1; camera.ZFar = 1000; camera.EnablePostProcessing = false; camera.DebugMode = SceneCameraDebugMode.Albedo; }
	preview = new Editor.SceneRenderingWidget { Scene = scene, Camera = camera, WindowTitle = "glTF asymmetric axis / camera / winding diagnostic", Size = new Vector2( 384, 384 ) }; onFrame = () => frames++; preview.OnPreFrame += onFrame; preview.Show();
	foreach ( var negative in new[] { false, true } )
	{
		var source = Fixture( negative ); var stem = negative ? "negative_x_scale" : "positive_scale"; Put( stem + ".gltf", source ); Put( stem + ".glb_c", Sandbox.Gltf.GltfCompiler.Compile( source ) );
		foreach ( var cameraX in new[] { -1, 1 } )
		{
			var center = new Vector3( .75f, .25f, -.5f ) * inches;
			camera.WorldPosition = center + new Vector3( 1, cameraX, .65f ).Normal * (6 * inches); camera.WorldRotation = Rotation.LookAt( center - camera.WorldPosition, Vector3.Up );
			foreach ( var form in new[] { ".gltf", ".glb_c" } )
			{
				var label = stem + "_camera_x_" + cameraX + (form == ".gltf" ? "_source" : "_compiled"); Hide(); Require( !(await Capture( label + "_empty_before" )).Any( Nonblack ), "Fresh empty control failed." );
				var model = Model.Load( prefix + "/" + stem + form ); Require( model is { IsValid: true, IsError: false }, "Actual Model.Load failed." ); if ( !ownedModels.Contains( model ) ) ownedModels.Add( model );
				shown.Add( new SceneObject( scene.SceneWorld, model, Transform.Zero ) ); var actual = await Capture( label + "_actual" ); Hide(); Require( !(await Capture( label + "_empty_after_actual" )).Any( Nonblack ), "Deleted actual asset remains visible." );
				ShowControls( negative, false ); var reference = await Capture( label + "_independent_reference" ); Hide(); Require( !(await Capture( label + "_empty_after_reference" )).Any( Nonblack ), "Deleted reference remains visible." );
				ShowControls( negative, true ); var reversed = await Capture( label + "_reversed_native_reference" ); Hide(); Require( !(await Capture( label + "_empty_after_reverse" )).Any( Nonblack ), "Deleted reverse reference remains visible." );
				var perAxis = new System.Collections.Generic.List<object>(); var passed = true; var visibleAxes = 0; var windingDifference = 0;
				for ( var pixel = 0; pixel < reference.Length; pixel++ ) if ( Classify( reference[pixel] ) != Classify( reversed[pixel] ) ) windingDifference++;
				for ( var axis = 0; axis < definitions.Length; axis++ )
				{
					var a = 0; var b = 0; var intersection = 0; var union = 0;
					for ( var pixel = 0; pixel < actual.Length; pixel++ ) { var x = Classify( actual[pixel] ) == axis; var y = Classify( reference[pixel] ) == axis; if ( x ) a++; if ( y ) b++; if ( x && y ) intersection++; if ( x || y ) union++; }
					if ( b > 5 ) visibleAxes++; var iou = union == 0 ? 1 : (double)intersection / union; var match = iou > .97; passed &= match;
					perAxis.Add( new { axis = definitions[axis].Name, actual_pixels = a, reference_pixels = b, iou, passed = match } );
				}
				passed &= visibleAxes >= 2 && windingDifference > 30;
				rows.Add( new { label, negative_node_x_scale = negative, source_camera_direction = new[] { (float)cameraX, .65f, 1f }, source_up = new[] { 0, 1, 0 }, engine_camera_direction = new[] { 1f, (float)cameraX, .65f }, engine_up = new[] { 0, 0, 1 }, camera_position = XYZ( camera.WorldPosition ), visibleAxes, windingDifference, perAxis, passed, fixture_sha256 = Hash( source ) } );
			}
		}
	}
	phase = "complete";
}
catch ( Exception e ) { failure = e.ToString(); }
finally
{
	await MainThread.Wait(); Hide();
	if ( preview is not null && preview.IsValid && onFrame is not null ) preview.OnPreFrame -= onFrame;
	if ( preview is not null && preview.IsValid ) { preview.Close(); preview.Destroy(); } scene?.Destroy();
	foreach ( var model in ownedModels ) Clean( "model", () => typeof( Model ).GetMethod( "Destroy", flags ).Invoke( model, null ) );
	if ( ownedMeshes.Count > 0 ) Clean( "reference meshes", () => engine.GetType( "Sandbox.GltfModelLoader", true ).GetMethod( "ReleaseMeshes", flags ).Invoke( null, new object[] { ownedMeshes } ) );
	if ( stockControl is not null ) Clean( "stock material copy", () => typeof( Material ).GetMethod( "Destroy", flags ).Invoke( stockControl, null ) );
	if ( aggregate is not null && mount is not null && unmount is not null ) Clean( "unmount", () => unmount.Invoke( aggregate, new object[] { mount } ) );
	if ( redirectObject is not null ) foreach ( var alias in aliases ) Clean( "redirect", () => removeAlias.Invoke( redirectObject, new object[] { alias } ) );
	redirect?.Dispose();
}
var receipt = new { passed = failure is null && rows.Count == 8 && errors.Count == 0 && rows.All( row => (bool)row.GetType().GetProperty( "passed" ).GetValue( row ) ), phase, failure, errors, rows, captures, output, renderer = nativeCapture ? "native CameraComponent.RenderToTexture; no clear injection" : "managed SceneBridge.RenderToTexture; global renderer unchanged", engine_mvid = engine.ManifestModule.ModuleVersionId, gltf_mvid = typeof( Sandbox.Gltf.GltfReader ).Assembly.ManifestModule.ModuleVersionId, source_sha256 = Hash( System.IO.File.ReadAllBytes( System.IO.Path.Combine( checkout.FullName, "engine/Tools/Gltf/editor/editor_orientation_axes.csx" ) ) ), scope = "Actual loader source/compiled asymmetric colored axes, explicit source-space cameras/up, hierarchy/negative determinant and independent native stock front/reversed winding controls. No production basis change; native-default evidence only if explicitly selected and all controls pass." };
System.IO.File.WriteAllText( System.IO.Path.Combine( output, "receipt.json" ), System.Text.Json.JsonSerializer.Serialize( receipt, jsonOptions ) ); return receipt;
