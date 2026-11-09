// Compile the actual stock shader through the editor. Never invoke a managed
// reload callback manually: native callback delivery is observed separately.
await MainThread.Wait();
if ( !Application.IsEditor || Game.IsPlaying || Graphics.IsActive ) throw new InvalidOperationException( "Use the idle editor." );
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
var root = new System.IO.DirectoryInfo( System.IO.Path.GetDirectoryName( typeof( Model ).Assembly.Location ) );
while ( root is not null && !System.IO.File.Exists( System.IO.Path.Combine( root.FullName, "engine/Sandbox-Engine.slnx" ) ) ) root = root.Parent;
if ( root is null ) throw new InvalidOperationException( "Cannot locate checkout." );
var output = System.IO.Path.Combine( root.FullName, "Temp/gltf-validation/editor-skinning-shader-compile", Guid.NewGuid().ToString( "N" ) ); System.IO.Directory.CreateDirectory( output );
var asset = Editor.AssetSystem.FindByPath( "shaders/skinning_cs.shader" );
if ( asset is null ) throw new InvalidOperationException( "Stock shader asset missing." );
var source = System.IO.Path.Combine( root.FullName, "game/core/shaders/skinning_cs.shader" ); var compiled = asset.GetCompiledFile( true );
string Digest( string path ) => System.IO.File.Exists( path ) ? Convert.ToHexStringLower( System.Security.Cryptography.SHA256.HashData( System.IO.File.ReadAllBytes( path ) ) ) : null;
long Revision( Resource value ) => (long)typeof( Resource ).GetProperty( "ReloadRevision", flags ).GetValue( value );
var template = Material.Load( "materials/dev/skinning_cs.vmat" );
if ( template is not { IsValid: true, IsError: false } ) throw new InvalidOperationException( "Stock compute material invalid." );
var shader = template.Shader; var shaderBefore = Revision( shader ); var materialBefore = Revision( template ); var compiledBefore = Digest( compiled );
var results = new System.Collections.Generic.List<object>(); var copies = new System.Collections.Generic.List<Material>();
bool compilePassed = false; string error = null; var start = System.Diagnostics.Stopwatch.StartNew(); var firstFrame = Application.FrameCount;
try
{
	compilePassed = asset.Compile( true ); if ( !compilePassed ) throw new InvalidOperationException( "Editor shader compile failed." );
	while ( Revision( shader ) == shaderBefore && start.Elapsed < TimeSpan.FromSeconds( 8 ) )
	{
		await Task.Delay( 50, cancellationToken ); await MainThread.Wait(); if ( Game.IsPlaying ) throw new InvalidOperationException( "User entered play mode." );
	}
	foreach ( var format in new[] { 1, 2 } )
	{
		var copy = template.CreateCopy( "gltf_weight_compile_" + Guid.NewGuid().ToString( "N" ) ); copies.Add( copy ); copy.SetFeature( "F_SKIN_WEIGHT_FORMAT", format );
		results.Add( new { requested = format, actual = copy.GetFeature( "F_SKIN_WEIGHT_FORMAT" ), valid = copy.IsValid && !copy.IsError, copy.ShaderName } );
		if ( copy.GetFeature( "F_SKIN_WEIGHT_FORMAT" ) != format || !copy.IsValid || copy.IsError ) throw new InvalidOperationException( "Compiled shader rejected a weight format feature." );
	}
	if ( template.GetFeature( "F_SKIN_WEIGHT_FORMAT" ) != 0 ) throw new InvalidOperationException( "Shared default feature was changed." );
}
catch ( Exception ex ) { error = ex.ToString(); }
finally { foreach ( var copy in copies ) if ( copy is not null && copy.IsValid ) typeof( Material ).GetMethod( "Destroy", flags ).Invoke( copy, null ); }
var receipt = new { passed = compilePassed && error is null && results.Count == 2, compile_passed = compilePassed, error, results,
	source_path = source, source_sha256 = Digest( source ), compiled_path = compiled, compiled_sha256_before = compiledBefore, compiled_sha256_after = Digest( compiled ),
	shader_revision_before = shaderBefore, shader_revision_after = Revision( shader ), material_revision_before = materialBefore, material_revision_after = Revision( template ),
	native_shader_reload_callback_observed = Revision( shader ) > shaderBefore, native_material_reload_callback_observed = Revision( template ) > materialBefore,
	first_frame = firstFrame, final_frame = Application.FrameCount, elapsed_ms = start.Elapsed.TotalMilliseconds, engine_mvid = typeof( Model ).Assembly.ManifestModule.ModuleVersionId, output,
	scope = "Actual editor stock shader compilation and owned material feature acceptance. Observed native reload callbacks are reported separately; no posed GPU or Model.Load acceptance claim." };
System.IO.File.WriteAllText( System.IO.Path.Combine( output, "receipt.json" ), System.Text.Json.JsonSerializer.Serialize( receipt, new System.Text.Json.JsonSerializerOptions { WriteIndented = true } ) ); return receipt;
