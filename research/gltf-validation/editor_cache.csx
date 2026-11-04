// Run as a method body in the already-running s&box editor's run_csharp tool.
// This tests private scenes and an owned template copy; it does not edit the active scene.
var engineLocation = typeof( Model ).Assembly.Location;
var root = new System.IO.DirectoryInfo( System.IO.Path.GetDirectoryName( engineLocation ) );
while ( root is not null && !System.IO.File.Exists( System.IO.Path.Combine( root.FullName, "engine", "Sandbox-Engine.slnx" ) ) ) root = root.Parent;
if ( root is null ) throw new InvalidOperationException( "Could not locate the s&box checkout from loaded engine: " + engineLocation );
var output = System.IO.Path.Combine( root.FullName, "Temp", "gltf-validation", "editor-cache" );
System.IO.Directory.CreateDirectory( output );
var receiptPath = System.IO.Path.Combine( output, "receipt.json" );
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
var completed = new System.Collections.Generic.List<string>();
var cleanupErrors = new System.Collections.Generic.List<string>();
var leases = new System.Collections.Generic.List<IDisposable>();
var startFrame = Application.FrameCount;
var phase = "initialize";
string error = null;
Scene firstScene = null;
Scene secondScene = null;
Material template = null;
object firstSystem = null;
object secondSystem = null;
Material shared = null;
Material changed = null;
bool firstDestroyed = false;
bool secondDestroyed = false;
bool finalWrapperInvalid = false;
bool disposedSystemRejected = false;

void Require( bool condition, string message ) { if ( !condition ) throw new InvalidOperationException( message ); }
bool Near( float a, float b ) => MathF.Abs( a - b ) < .0001f;
void DisposeOwned( string name, Action action )
{
	try { action(); }
	catch ( Exception e ) { cleanupErrors.Add( name + ": " + e ); }
}

try
{
	await MainThread.Wait();
	Require( Application.IsEditor, "This proof must execute inside an s&box editor." );
	Require( !Graphics.IsActive, "The shared material cache must be exercised outside a Graphics scope." );
	var engine = typeof( Model ).Assembly;
	var stock = Material.Load( "materials/default/white.vmat" );
	Require( stock is { IsValid: true } && !stock.IsError, "Stock white material is unavailable." );
	var stockTint = stock.GetVector4( "g_vColorTint" );
	template = stock.CreateCopy( "gltf_editor_cache_template_" + Guid.NewGuid().ToString( "N" ) );
	Require( template is { IsValid: true }, "Could not create a private template copy." );
	var templateTint = template.GetVector4( "g_vColorTint" );
	var keyType = engine.GetType( "Sandbox.GltfMaterialKey", true );
	var bindingType = engine.GetType( "Sandbox.GltfMaterialTextureBinding", true );
	var cacheType = engine.GetType( "Sandbox.GltfMaterialCache", true );
	var systemType = engine.GetType( "Sandbox.GltfMaterialSceneSystem", true );
	var sharedCache = cacheType.GetProperty( "Shared", flags )?.GetValue( null ) ?? throw new InvalidOperationException( "Shared cache unavailable." );
	var cacheAcquire = cacheType.GetMethod( "Acquire", flags ) ?? throw new InvalidOperationException( "Cache acquisition unavailable." );
	var systemAcquire = systemType.GetMethod( "Acquire", flags ) ?? throw new InvalidOperationException( "Scene cache acquisition unavailable." );
	var makeKey = keyType.GetConstructors( flags ).Single();
	var getSystem = typeof( Scene ).GetMethods( flags ).Single( x => x.Name == "GetSystem" && x.IsGenericMethodDefinition && x.GetParameters().Length == 0 ).MakeGenericMethod( systemType );
	object Key( float value ) => makeKey.Invoke( new object[]
	{
		template, 0, Array.Empty<System.Collections.Generic.KeyValuePair<string, int>>(),
		new[] { new System.Collections.Generic.KeyValuePair<string, Vector4>( "g_vColorTint", new Vector4( value, 0, 0, 1 ) ) },
		Array.CreateInstance( bindingType, 0 ), new[] { new System.Collections.Generic.KeyValuePair<string, float>( "g_flRoughness", value ) }
	} );
	object Acquire( System.Reflection.MethodInfo method, object owner, float value )
	{
		var lease = method.Invoke( owner, new[] { Key( value ) } ) ?? throw new InvalidOperationException( "Cache returned a null lease." );
		leases.Add( (IDisposable)lease );
		return lease;
	}
	Material Value( object lease ) => (Material)(lease.GetType().GetProperty( "Value", flags )?.GetValue( lease ) ?? throw new InvalidOperationException( "Lease has no value." ));
	phase = "asset-and-scene-sharing";
	var assetLease = Acquire( cacheAcquire, sharedCache, .25f );
	shared = Value( assetLease );
	firstScene = Scene.CreateEditorScene();
	secondScene = Scene.CreateEditorScene();
	firstSystem = getSystem.Invoke( firstScene, null ) ?? throw new InvalidOperationException( "First private scene did not register the cache system." );
	secondSystem = getSystem.Invoke( secondScene, null ) ?? throw new InvalidOperationException( "Second private scene did not register the cache system." );
	Require( !ReferenceEquals( firstSystem, secondSystem ), "Private scenes share a system instance." );
	var firstLease = Acquire( systemAcquire, firstSystem, .25f );
	var secondLease = Acquire( systemAcquire, secondSystem, .25f );
	Require( shared.IsValid && ReferenceEquals( shared, Value( firstLease ) ) && ReferenceEquals( shared, Value( secondLease ) ), "Asset and scene leases did not share one material." );
	completed.Add( phase );

	phase = "descriptor-isolation";
	var changedLease = Acquire( systemAcquire, firstSystem, .75f );
	changed = Value( changedLease );
	Require( changed.IsValid && !ReferenceEquals( shared, changed ), "Distinct descriptor did not produce an independent material." );
	Require( Near( shared.GetVector4( "g_vColorTint" ).x, .25f ) && Near( changed.GetVector4( "g_vColorTint" ).x, .75f ), "Independent tint values did not survive." );
	Require( Near( shared.GetVector4( "g_flRoughness" ).x, .25f ) && Near( changed.GetVector4( "g_flRoughness" ).x, .75f ), "Independent roughness values did not survive." );
	Require( template.GetVector4( "g_vColorTint" ) == templateTint && stock.GetVector4( "g_vColorTint" ) == stockTint, "The template or stock material was modified." );
	completed.Add( phase );

	phase = "first-scene-shutdown";
	firstScene.Destroy(); firstDestroyed = true; firstScene = null;
	Require( shared.IsValid && ReferenceEquals( shared, Value( assetLease ) ) && ReferenceEquals( shared, Value( secondLease ) ), "Destroying the first scene broke remaining owners." );
	Require( !changed.IsValid, "The first scene's sole changed-material wrapper remains valid." );
	IDisposable unexpected = null;
	try
	{
		try { unexpected = (IDisposable)systemAcquire.Invoke( firstSystem, new[] { Key( .25f ) } ); }
		catch ( System.Reflection.TargetInvocationException e ) when ( e.InnerException is ObjectDisposedException ) { disposedSystemRejected = true; }
		Require( disposedSystemRejected, "Disposed scene system accepted a new lease." );
	}
	finally { unexpected?.Dispose(); }
	((IDisposable)firstSystem).Dispose();
	completed.Add( phase );

	phase = "second-scene-and-final-asset-release";
	secondScene.Destroy(); secondDestroyed = true; secondScene = null;
	Require( shared.IsValid && ReferenceEquals( shared, Value( assetLease ) ), "The asset owner did not outlive both private scenes." );
	((IDisposable)assetLease).Dispose();
	finalWrapperInvalid = !shared.IsValid;
	Require( finalWrapperInvalid, "The final release did not invalidate the procedural material wrapper." );
	Require( template.GetVector4( "g_vColorTint" ) == templateTint && stock.GetVector4( "g_vColorTint" ) == stockTint, "The material template or stock material changed." );
	completed.Add( phase );
}
catch ( Exception e ) { error = e.ToString(); }
finally
{
	await MainThread.Wait();
	if ( firstScene is not null ) DisposeOwned( "first scene", () => firstScene.Destroy() );
	if ( secondScene is not null ) DisposeOwned( "second scene", () => secondScene.Destroy() );
	for ( var i = leases.Count - 1; i >= 0; i-- )
	{
		var lease = leases[i];
		DisposeOwned( "lease", () => lease.Dispose() );
	}
	if ( template is not null ) DisposeOwned( "template", () => typeof( Material ).GetMethod( "Destroy", flags ).Invoke( template, null ) );
}
var receipt = new
{
	passed = error is null && cleanupErrors.Count == 0,
	scope = "editor process-scoped cache: two private scenes, asset lease, descriptor isolation and managed wrapper lifetime; no GPU release timing or game-context lifecycle proof",
	phase, error, completed, cleanup_errors = cleanupErrors, first_scene_destroyed = firstDestroyed, second_scene_destroyed = secondDestroyed,
	disposed_system_rejected = disposedSystemRejected, final_wrapper_invalid = finalWrapperInvalid,
	engine_location = engineLocation, engine_mvid = typeof( Model ).Assembly.ManifestModule.ModuleVersionId.ToString(),
	is_editor = Application.IsEditor, starting_frame = startFrame, final_frame = Application.FrameCount, output
};
System.IO.File.WriteAllText( receiptPath, System.Text.Json.JsonSerializer.Serialize( receipt, new System.Text.Json.JsonSerializerOptions { WriteIndented = true } ) );
return receipt;
