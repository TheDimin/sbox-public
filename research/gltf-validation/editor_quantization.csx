// Actual Model.Load KHR_mesh_quantization acceptance in an existing editor.
// Synthetic untextured geometry; packaging fixture is not GPU optimized.
var engineLocation = typeof( Model ).Assembly.Location;
var root = new System.IO.DirectoryInfo( System.IO.Path.GetDirectoryName( engineLocation ) );
while ( root is not null && !System.IO.File.Exists( System.IO.Path.Combine( root.FullName, "engine", "Sandbox-Engine.slnx" ) ) ) root = root.Parent;
if ( root is null ) throw new InvalidOperationException( "Cannot locate checkout from loaded engine." );
var selection = Environment.GetEnvironmentVariable( "SBOX_GLTF_QUANTIZATION_CASE" ) ?? "all";
if ( !new[] { "source", "compiled", "all" }.Contains( selection, StringComparer.Ordinal ) ) throw new ArgumentException( "Expected source, compiled or all." );
var selected = selection == "all" ? new[] { "source", "compiled" } : new[] { selection };
var runId = Guid.NewGuid().ToString( "N" );
var virtualBase = "gltf-quantization/" + runId;
var output = System.IO.Path.Combine( root.FullName, "Temp", "gltf-validation", "editor-quantization", runId );
System.IO.Directory.CreateDirectory( output );
var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static;
var cases = new System.Collections.Generic.List<object>();
var cleanupErrors = new System.Collections.Generic.List<string>();
var fixtureHashes = new System.Collections.Generic.Dictionary<string, string>();
var redirects = new System.Collections.Generic.List<string>();
var ownedModels = new System.Collections.Generic.HashSet<Model>();
object redirectObject = null;
IDisposable redirect = null;
BaseFileSystem managedMount = null, mountedAggregate = null;
System.Reflection.MethodInfo unmount = null, removeRedirect = null;
bool mounted = false;
string phase = "initialize", error = null;
int passed = 0, loadCalls = 0;
var startingFrame = Application.FrameCount;
string Hash( byte[] bytes ) => Convert.ToHexStringLower( System.Security.Cryptography.SHA256.HashData( bytes ) );
void Require( bool condition, string message ) { if ( !condition ) throw new InvalidOperationException( message ); }
void Cleanup( string name, Action action ) { try { action(); } catch ( Exception e ) { cleanupErrors.Add( name + ": " + e ); } }
bool Valid( Model value ) => value is { IsValid: true } && !value.IsError && !ReferenceEquals( value, Model.Error );
bool Near( float a, float b, float tolerance = .01f ) => MathF.Abs( a - b ) <= tolerance;

try
{
	await MainThread.Wait();
	Require( Application.IsEditor && !Graphics.IsActive, "Run only inside the editor on its main thread outside a Graphics scope." );
	phase = "fixtures";
	// SHORT positions use a node scale of 0.5; BYTE normals and tangents are normalized.
	// The independent expected world-space geometry is specified below in s&box units.
	const string json = """{"asset":{"version":"2.0"},"extensionsUsed":["KHR_mesh_quantization"],"extensionsRequired":["KHR_mesh_quantization"],"buffers":[{"uri":"data:application/octet-stream;base64,AAAAAAAAAAACAAAAAAAAAAAAAgAAAAAAAAB/AAAAfwAAAH8AfwAAf38AAH9/AAB/AAABAAIA","byteLength":54}],"bufferViews":[{"buffer":0,"byteOffset":0,"byteLength":24,"byteStride":8},{"buffer":0,"byteOffset":24,"byteLength":12,"byteStride":4},{"buffer":0,"byteOffset":36,"byteLength":12,"byteStride":4},{"buffer":0,"byteOffset":48,"byteLength":6}],"accessors":[{"bufferView":0,"componentType":5122,"count":3,"type":"VEC3","min":[0,0,0],"max":[2,2,0]},{"bufferView":1,"componentType":5120,"normalized":true,"count":3,"type":"VEC3"},{"bufferView":2,"componentType":5120,"normalized":true,"count":3,"type":"VEC4"},{"bufferView":3,"componentType":5123,"count":3,"type":"SCALAR"}],"meshes":[{"primitives":[{"attributes":{"POSITION":0,"NORMAL":1,"TANGENT":2},"indices":3}]}],"nodes":[{"mesh":0,"scale":[0.5,0.5,0.5]}],"scenes":[{"nodes":[0]}],"scene":0}""";
	var source = System.Text.Encoding.UTF8.GetBytes( json );
	Require( Hash( source ) == "d167032ca860cc3b122051c9cd6043e45ac5ea42ed1c899bc52a361fa21454f9", "Synthetic fixture digest mismatch." );
	var supported = new System.Collections.Generic.HashSet<string>( StringComparer.Ordinal ) { "KHR_mesh_quantization" };
	var compiled = Sandbox.Gltf.GltfCompiler.Compile( source, supportedExtensions: supported );
	var files = new System.Collections.Generic.Dictionary<string, byte[]> { ["source.gltf"] = source, ["compiled-only.glb_c"] = compiled };
	var redirectType = typeof( Model ).Assembly.GetType( "Sandbox.Internal.RedirectFileSystem", true );
	redirectObject = redirectType.GetMethod( "Create", flags ).Invoke( null, new object[] { output } );
	redirect = (IDisposable)redirectObject;
	var addRedirect = redirectType.GetMethod( "AddAbsFile", flags ); removeRedirect = redirectType.GetMethod( "RemoveAbsFile", flags );
	foreach ( var file in files )
	{
		var physical = System.IO.Path.Combine( output, file.Key ); System.IO.File.WriteAllBytes( physical, file.Value );
		fixtureHashes.Add( file.Key, Hash( file.Value ) );
		var alias = "/" + virtualBase + "/" + file.Key;
		addRedirect.Invoke( redirectObject, new object[] { alias, physical } ); redirects.Add( alias );
	}
	var wrapper = typeof( BaseFileSystem ).GetConstructors( flags ).Single( ctor =>
	{
		var parameters = ctor.GetParameters(); return parameters.Length == 1 && parameters[0].ParameterType.IsInstanceOfType( redirectObject );
	} );
	managedMount = (BaseFileSystem)wrapper.Invoke( new[] { redirectObject } );
	mountedAggregate = Sandbox.FileSystem.Mounted ?? throw new InvalidOperationException( "Managed mounted filesystem unavailable." );
	var mount = typeof( BaseFileSystem ).GetMethod( "Mount", flags, null, new[] { typeof( BaseFileSystem ) }, null );
	unmount = typeof( BaseFileSystem ).GetMethod( "UnMount", flags, null, new[] { typeof( BaseFileSystem ) }, null );
	Require( mount is not null && unmount is not null, "Managed mount APIs unavailable." );
	mount.Invoke( mountedAggregate, new object[] { managedMount } ); mounted = true;
	foreach ( var file in files ) Require( mountedAggregate.FileExists( virtualBase + "/" + file.Key ) && Hash( mountedAggregate.ReadAllBytes( virtualBase + "/" + file.Key ).ToArray() ) == fixtureHashes[file.Key], "Mounted fixture mismatch: " + file.Key );
	Require( !mountedAggregate.FileExists( virtualBase + "/compiled-only.glb" ) && !mountedAggregate.FileExists( virtualBase + "/compiled-only.gltf" ), "Compiled fixture unexpectedly has a source sibling." );
	typeof( Model ).Assembly.GetType( "NativeEngine.g_pResourceSystem", true ).GetMethod( "InvalidateDatabase", flags ).Invoke( null, null );

	phase = "actual-model-load";
	const float unit = 1f / .0254f;
	var expected = new[] { Vector3.Zero, new Vector3( 0, unit, 0 ), new Vector3( 0, 0, unit ) };
	foreach ( var name in selected )
	{
		cancellationToken.ThrowIfCancellationRequested();
		var path = virtualBase + (name == "source" ? "/source.gltf" : "/compiled-only.glb");
		var result = new System.Collections.Generic.Dictionary<string, object> { ["name"] = name, ["path"] = path, ["passed"] = false, ["compiled_source_absent"] = name == "compiled" };
		cases.Add( result );
		try
		{
			loadCalls++; var model = Model.Load( path );
			result["model_null"] = model is null; result["model_valid"] = model?.IsValid; result["model_error"] = model?.IsError;
			Require( Valid( model ), "Actual Model.Load did not return a valid model: " + path );
			ownedModels.Add( model );
			var info = model.MeshInfo;
			Require( info is not null && info.TotalTriangles == 1 && info.TotalDrawCalls == 1, "Expected exactly one triangle and draw." );
			var bounds = model.RenderBounds;
			Require( Near( bounds.Mins.x, 0, .2f ) && Near( bounds.Mins.y, 0, .2f ) && Near( bounds.Mins.z, 0, .2f ) && Near( bounds.Maxs.x, 0, .2f ) && Near( bounds.Maxs.y, unit, .2f ) && Near( bounds.Maxs.z, unit, .2f ), "Quantized node scale or world bounds differ from the independent reference." );
			var material = info.Meshes.SelectMany( x => x.DrawCalls ).Select( x => x.Material ).FirstOrDefault( x => x is { IsValid: true } && !x.IsError );
			Require( material is not null && material.ShaderName.Contains( "complex", StringComparison.OrdinalIgnoreCase ), "Expected a valid stock complex material." );
			var read = typeof( Model ).GetMethod( "TryReadDrawCall", flags ) ?? throw new InvalidOperationException( "Native draw readback API unavailable." );
			var args = new object[] { 0, 0, null, null, null };
			Require( (bool)read.Invoke( model, args ), "Could not read native draw geometry." );
			var vertices = (Vertex[])args[2]; var indices = (uint[])args[3]; var streams = Convert.ToInt32( args[4] );
			Require( (streams & 3) == 3 && vertices.Length == 3 && indices.Length == 3 && indices.Distinct().Count() == 3 && indices.All( i => i < vertices.Length ), "Missing position/normal streams or unexpected triangle geometry." );
			foreach ( var target in expected ) Require( vertices.Count( v => Near( v.Position.x, target.x ) && Near( v.Position.y, target.y ) && Near( v.Position.z, target.z ) ) == 1, "Decoded positions differ from the independently calculated reference." );
			Require( vertices.All( v => Near( v.Normal.x, 1 ) && Near( v.Normal.y, 0 ) && Near( v.Normal.z, 0 ) ), "Decoded normalized normals differ from +X." );
			loadCalls++; var repeated = Model.Load( path ); if ( Valid( repeated ) ) ownedModels.Add( repeated );
			loadCalls++; var equivalent = Model.Load( "/" + path ); if ( Valid( equivalent ) ) ownedModels.Add( equivalent );
			loadCalls++; var asyncModel = await Model.LoadAsync( path ); await MainThread.Wait(); if ( Valid( asyncModel ) ) ownedModels.Add( asyncModel );
			Require( ReferenceEquals( model, repeated ) && ReferenceEquals( model, equivalent ) && ReferenceEquals( model, asyncModel ), "Repeat, equivalent and async loads must share the cached model." );
			if ( name == "compiled" ) { loadCalls++; var direct = Model.Load( virtualBase + "/compiled-only.glb_c" ); if ( Valid( direct ) ) ownedModels.Add( direct ); Require( ReferenceEquals( model, direct ), "Canonical cold reference and compiled alias differ." ); }
			result["triangles"] = info.TotalTriangles; result["draw_calls"] = info.TotalDrawCalls; result["shader"] = material.ShaderName;
			result["bounds_min"] = bounds.Mins.ToString(); result["bounds_max"] = bounds.Maxs.ToString(); result["streams"] = streams;
			result["positions"] = vertices.Select( v => v.Position.ToString() ).ToArray(); result["normals"] = vertices.Select( v => v.Normal.ToString() ).ToArray();
			result["passed"] = true; passed++;
		}
		catch ( OperationCanceledException ) { throw; }
		catch ( Exception e ) { result["error"] = e.ToString(); }
	}
	phase = "complete";
}
catch ( Exception e ) { error = e.ToString(); }
finally
{
	await MainThread.Wait();
	var destroy = typeof( Model ).GetMethod( "Destroy", flags );
	foreach ( var model in ownedModels ) if ( Valid( model ) ) Cleanup( "model", () => destroy.Invoke( model, null ) );
	await Task.Delay( 100 ); await MainThread.Wait();
	if ( mounted ) Cleanup( "managed mount", () => unmount.Invoke( mountedAggregate, new object[] { managedMount } ) );
	if ( removeRedirect is not null ) foreach ( var alias in redirects ) Cleanup( "redirect " + alias, () => removeRedirect.Invoke( redirectObject, new object[] { alias } ) );
	if ( redirect is not null ) Cleanup( "redirect filesystem", () => redirect.Dispose() );
}
var receipt = new
{
	passed = error is null && phase == "complete" && cleanupErrors.Count == 0 && cases.Count == selected.Length && passed == selected.Length,
	selection, phase, error, cases, expected_cases = selected.Length, passed_cases = passed, load_calls = loadCalls, fixture_sha256 = fixtureHashes, cleanup_errors = cleanupErrors,
	scope = "Actual editor Model.Load/LoadAsync of a synthetic required KHR_mesh_quantization triangle; native geometry readback and metadata only. GLB_c is a lossless packaging fixture, not GPU optimized; rendering and overall extension conformance are not tested.",
	position_readback_epsilon_inches = .01f, normal_component_epsilon = .01f, tolerances_are_release_criteria = false,
	is_editor = Application.IsEditor, starting_frame = startingFrame, final_frame = Application.FrameCount, output,
	engine_mvid = typeof( Model ).Assembly.ManifestModule.ModuleVersionId.ToString(), gltf_mvid = typeof( Sandbox.Gltf.GltfCompiler ).Assembly.ManifestModule.ModuleVersionId.ToString()
};
var receiptPath = System.IO.Path.Combine( output, "receipt.json" );
System.IO.File.WriteAllText( receiptPath, System.Text.Json.JsonSerializer.Serialize( receipt, new System.Text.Json.JsonSerializerOptions { WriteIndented = true } ) );
return receiptPath;
