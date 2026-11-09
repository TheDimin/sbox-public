// Coordinator-only: guarded import/compile/texture preflight for one staged unique HDRI.
// The caller writes {"token":"<32 lowercase hex>"} to exchangeDirectory/gltf-environment-import.json
// and holds resource admission until this call and any editor compiler activity have completed.
await MainThread.Wait();
if ( !Application.IsEditor || Game.IsPlaying || Graphics.IsActive ) throw new InvalidOperationException( "Requires idle editor outside rendering." );
var requestPath = System.IO.Path.Combine( exchangeDirectory, "gltf-environment-import.json" );
using var request = System.Text.Json.JsonDocument.Parse( System.IO.File.ReadAllBytes( requestPath ) );
var token = request.RootElement.GetProperty( "token" ).GetString();
if ( token is null || token.Length != 32 || token.Any( c => !(c is >= '0' and <= '9' or >= 'a' and <= 'f') ) ) throw new InvalidOperationException( "Invalid unique staging token." );
var root = new System.IO.DirectoryInfo( System.IO.Path.GetDirectoryName( typeof( Model ).Assembly.Location ) );
while ( root is not null && !System.IO.File.Exists( System.IO.Path.Combine( root.FullName, "engine/Sandbox-Engine.slnx" ) ) ) root = root.Parent;
if ( root is null ) throw new InvalidOperationException( "Checkout unavailable." );
var relative = "materials/codex_gltf_ibl_" + token + "/artist_workshop_1k";
var source = System.IO.Path.Combine( root.FullName, "game/core", relative + ".exr" );
var definition = System.IO.Path.Combine( root.FullName, "game/core", relative + ".vtex" );
string HashFile( string path ) { using var stream = System.IO.File.OpenRead( path ); return Convert.ToHexStringLower( System.Security.Cryptography.SHA256.HashData( stream ) ); }
var expectedSource = request.RootElement.GetProperty( "sourceSha256" ).GetString();
var expectedDefinition = request.RootElement.GetProperty( "definitionSha256" ).GetString();
if ( !System.IO.File.Exists( source ) || !System.IO.File.Exists( definition ) || HashFile( source ) != expectedSource || HashFile( definition ) != expectedDefinition ) throw new InvalidOperationException( "Staged EXR or definition digest differs." );
var timer = System.Diagnostics.Stopwatch.StartNew();
// FileSystem watchers need not discover engine-core additions. Register only
// this already verified, uniquely named definition; never create another asset.
var requireExisting = request.RootElement.TryGetProperty( "requireExisting", out var existingValue ) && existingValue.ValueKind == System.Text.Json.JsonValueKind.True;
var asset = Editor.AssetSystem.FindByPath( definition );
if ( asset is null && !requireExisting ) asset = Editor.AssetSystem.RegisterFile( definition );
if ( asset is null ) throw new InvalidOperationException( "Editor AssetSystem could not find or register the staged vtex." );
var discoveredSource = System.IO.Path.GetFullPath( asset.GetSourceFile( true ) ?? "" );
if ( !string.Equals( discoveredSource, definition, StringComparison.Ordinal ) ) throw new InvalidOperationException( "AssetSystem selected a different source definition." );
var reflect = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
var native = asset.GetType().GetField( "native", reflect )?.GetValue( asset ) ?? throw new InvalidOperationException( "Expected existing NativeAsset." );
object NativeCall( string name ) => (native.GetType().GetMethod( name, reflect ) ?? throw new MissingMethodException( name )).Invoke( native, null );
object State() => new { reason = (string)NativeCall( "GetCompileStateReason_Transient" ), needsDependencyUpdate = (bool)NativeCall( "NeedAnyDependencyUpdate_Virtual" ), asset.IsCompiled, asset.IsCompileFailed, asset.IsCompiledAndUpToDate };
var before = State();
NativeCall( "RequireDependencyInfo_Virtual" );
var afterDependencyRequest = State();
// NativeAsset.Compile returns the synchronous RecompileAsset result. Capture
// its return explicitly; do not mistake a later timeout for compiler idleness.
var compileReturned = asset.Compile( true );
var afterCompile = State();
if ( asset.IsCompileFailed ) return new { passed = false, token, resource = relative + ".vtex", compileOutcome = "failed", isCompiled = asset.IsCompiled, upToDate = asset.IsCompiledAndUpToDate, isCompileFailed = true, compileReturned, before, afterDependencyRequest, afterCompile, sourceSha256 = HashFile( source ), definitionSha256 = HashFile( definition ) };
if ( !asset.IsCompiled || !asset.IsCompiledAndUpToDate ) return new { passed = false, token, resource = relative + ".vtex", compileOutcome = "uncertain", isCompiled = asset.IsCompiled, upToDate = asset.IsCompiledAndUpToDate, isCompileFailed = asset.IsCompileFailed, compileReturned, before, afterDependencyRequest, afterCompile, sourceSha256 = HashFile( source ), definitionSha256 = HashFile( definition ) };
Scene scene = null; SceneCubemap probe = null;
try
{
    var compiled = asset.GetCompiledFile( true );
    if ( string.IsNullOrWhiteSpace( compiled ) || !System.IO.File.Exists( compiled ) ) throw new InvalidOperationException( "Compiled cubemap file absent." );
    var texture = Texture.Load( relative + ".vtex" );
    if ( texture is null || !texture.IsValid || texture.IsError || texture.Depth != 6 || texture.Width != texture.Height || texture.Mips < 2 ) throw new InvalidOperationException( "Compiled asset did not load as a valid square cubemap with mips." );
    scene = Scene.CreateEditorScene();
    probe = new SceneCubemap( scene.SceneWorld, texture, BBox.FromPositionAndSize( Vector3.Zero, 100000 ) );
    probe.Projection = SceneCubemap.ProjectionMode.Sphere; probe.Feathering = .01f; probe.RenderDirty();
    if ( !probe.IsValid() || probe.Texture is null || !probe.Texture.IsValid || probe.Texture.IsError ) throw new InvalidOperationException( "SceneCubemap did not retain valid texture." );
    return new { passed = true, compileOutcome = "compiled", compileReturned, before, afterDependencyRequest, afterCompile, token, resource = relative + ".vtex", textureResourcePath = texture.ResourcePath, source, sourceSha256 = HashFile( source ), definition, definitionSha256 = HashFile( definition ), compiled, compiledSha256 = HashFile( compiled ), texture.Width, texture.Height, texture.Depth, texture.Mips, format = texture.ImageFormat.ToString(), projection = probe.Projection.ToString(), engineMvid = typeof( Model ).Assembly.ManifestModule.ModuleVersionId.ToString(), toolsMvid = typeof( Editor.AssetSystem ).Assembly.ManifestModule.ModuleVersionId.ToString(), elapsedSeconds = timer.Elapsed.TotalSeconds, scope = "Native asset import and valid cubemap construction only. HDR dynamic range, filtering, mapping, IBL pixels and visual parity remain unproven." };
}
catch ( Exception error )
{
    return new { passed = false, compileOutcome = "compiled", compileReturned, before, afterDependencyRequest, afterCompile, token, resource = relative + ".vtex", sourceSha256 = HashFile( source ), definitionSha256 = HashFile( definition ), preflightError = error.ToString() };
}
finally
{
    if ( probe.IsValid() ) probe.Delete();
    if ( scene.IsValid() ) scene.Destroy();
}
