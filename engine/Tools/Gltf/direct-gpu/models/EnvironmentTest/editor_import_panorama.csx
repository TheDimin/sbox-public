// Coordinator-only guarded import of one prepared panorama. Hold the native
// lease and guard through terminal compiler state; no editor launch/restart.
// exchangeDirectory/gltf-reference-environment.json: {"fixture":"/tmp/.../fixture.json"}
await MainThread.Wait();
if ( !Application.IsEditor || Game.IsPlaying || Graphics.IsActive ) throw new InvalidOperationException( "Requires idle editor outside rendering." );
using var request = System.Text.Json.JsonDocument.Parse( System.IO.File.ReadAllBytes( System.IO.Path.Combine( exchangeDirectory, "gltf-reference-environment.json" ) ) );
var fixturePath = request.RootElement.GetProperty( "fixture" ).GetString();
if ( !System.IO.Path.IsPathFullyQualified( fixturePath ?? "" ) || !System.IO.Path.GetFullPath( fixturePath ).StartsWith( "/tmp/", StringComparison.Ordinal ) ) throw new InvalidOperationException( "Prepared fixture must be under /tmp." );
using var fixture = System.Text.Json.JsonDocument.Parse( System.IO.File.ReadAllBytes( fixturePath ) );
var spec = fixture.RootElement;
var readOnlyExisting = request.RootElement.TryGetProperty( "readOnlyExisting", out var readOnly ) && readOnly.ValueKind == System.Text.Json.JsonValueKind.True;
var name = spec.GetProperty( "name" ).GetString();
if ( string.IsNullOrEmpty( name ) || name.Any( c => !(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_') ) ) throw new InvalidOperationException( "Unsafe canonical resource name." );
var relative = "materials/gltf_reference_environment/" + name;
if ( spec.GetProperty( "resource" ).GetString() != relative + ".vtex" || !spec.GetProperty( "exactDecodedRoundtrip" ).GetBoolean() ) throw new InvalidOperationException( "Unexpected fixture identity or unverified conversion." );
var root = new System.IO.DirectoryInfo( System.IO.Path.GetDirectoryName( typeof( Model ).Assembly.Location ) );
while ( root is not null && !System.IO.File.Exists( System.IO.Path.Combine( root.FullName, "engine/Sandbox-Engine.slnx" ) ) ) root = root.Parent;
if ( root is null ) throw new InvalidOperationException( "Checkout unavailable." );
string Hash( string path ) { using var stream = System.IO.File.OpenRead( path ); return Convert.ToHexStringLower( System.Security.Cryptography.SHA256.HashData( stream ) ); }
var source = System.IO.Path.Combine( root.FullName, "game/core", relative + ".exr" );
var definition = System.IO.Path.Combine( root.FullName, "game/core", relative + ".vtex" );
void Stage( string input, string target, string hash )
{
    if ( !System.IO.Path.IsPathFullyQualified( input ?? "" ) || !System.IO.Path.GetFullPath( input ).StartsWith( "/tmp/", StringComparison.Ordinal ) || Hash( input ) != hash ) throw new InvalidOperationException( "Prepared input digest or location differs." );
    if ( System.IO.File.Exists( target ) )
    {
        if ( new System.IO.FileInfo( target ).LinkTarget is not null || Hash( target ) != hash ) throw new InvalidOperationException( "Existing canonical environment differs; refusing overwrite." );
        return;
    }
    if ( readOnlyExisting ) throw new InvalidOperationException( "Read-only preflight refuses to stage a missing file." );
    System.IO.Directory.CreateDirectory( System.IO.Path.GetDirectoryName( target ) );
    System.IO.File.Copy( input, target, false );
    if ( Hash( target ) != hash ) throw new InvalidOperationException( "Staged file differs." );
}
Stage( spec.GetProperty( "exr" ).GetString(), source, spec.GetProperty( "exrSha256" ).GetString() );
Stage( spec.GetProperty( "definition" ).GetString(), definition, spec.GetProperty( "definitionSha256" ).GetString() );
var expectedCompiled = definition + "_c";
if ( readOnlyExisting && (!System.IO.File.Exists( expectedCompiled ) || Hash( expectedCompiled ) != request.RootElement.GetProperty( "expectedCompiledSha256" ).GetString()) ) throw new InvalidOperationException( "Read-only preflight compiled digest differs." );
// Engine-core additions can have a source index entry with permanently invalid
// dependency info even after a successful native compile. Register the exact
// produced file, rather than confusing that stale entry with compiler activity.
var asset = System.IO.File.Exists( expectedCompiled )
    ? Editor.AssetSystem.FindByPath( expectedCompiled ) ?? Editor.AssetSystem.RegisterFile( expectedCompiled )
    : Editor.AssetSystem.FindByPath( definition ) ?? Editor.AssetSystem.RegisterFile( definition );
if ( asset is null || System.IO.Path.GetFullPath( asset.GetSourceFile( true ) ?? "" ) != definition ) throw new InvalidOperationException( "AssetSystem did not select the exact staged definition." );
bool? compileReturned = null;
if ( !asset.IsCompiledAndUpToDate )
{
    if ( readOnlyExisting ) return new { passed = false, compileOutcome = "uncertain", readOnlyExisting, asset.IsCompiled, asset.IsCompiledAndUpToDate, asset.IsCompileFailed, scope = "No Compile or staging call performed; existing asset index not current." };
    compileReturned = asset.Compile( true );
    if ( System.IO.File.Exists( expectedCompiled ) )
    {
        asset = Editor.AssetSystem.FindByPath( expectedCompiled ) ?? Editor.AssetSystem.RegisterFile( expectedCompiled ) ?? asset;
        if ( System.IO.Path.GetFullPath( asset.GetSourceFile( true ) ?? "" ) != definition ) throw new InvalidOperationException( "Compiled index selected another source." );
    }
}
var outcome = asset.IsCompileFailed ? "failed" : asset.IsCompiled && asset.IsCompiledAndUpToDate ? "compiled" : "uncertain";
if ( outcome != "compiled" ) return new { passed = false, compileOutcome = outcome, compileReturned, asset.IsCompileFailed, asset.IsCompiled, asset.IsCompiledAndUpToDate, definition, source };
var compiled = asset.GetCompiledFile( true );
if ( string.IsNullOrWhiteSpace( compiled ) || System.IO.Path.GetFullPath( compiled ) != expectedCompiled ) throw new InvalidOperationException( "AssetSystem selected another compiled resource." );
var texture = Texture.Load( relative + ".vtex" );
if ( !System.IO.File.Exists( compiled ) || texture is not { IsValid: true, IsError: false } || texture.Depth != 6 || texture.Width != texture.Height || texture.Mips < 2 ) throw new InvalidOperationException( "Compiled texture is not a valid square cubemap with mips." );
if ( texture.ResourcePath != relative + ".vtex" ) throw new InvalidOperationException( "Loaded texture resource path differs." );
if ( readOnlyExisting && (texture.ImageFormat != ImageFormat.BC6H || texture.Width != spec.GetProperty( "width" ).GetInt32() / 4) ) throw new InvalidOperationException( "Existing compiled resource descriptor differs from the verified panorama recipe." );
Scene scene = null; SceneCubemap probe = null;
try
{
    scene = Scene.CreateEditorScene();
    probe = new SceneCubemap( scene.SceneWorld, texture, BBox.FromPositionAndSize( Vector3.Zero, 100000 ) );
    probe.Projection = SceneCubemap.ProjectionMode.Sphere; probe.Feathering = .01f; probe.RenderDirty();
    if ( !probe.IsValid() || probe.Texture != texture ) throw new InvalidOperationException( "Native SceneCubemap did not retain the resource." );
    return new { passed = true, constructionPassed = true, compileOutcome = outcome, compileReturned, readOnlyExisting, resource = relative + ".vtex", textureResourcePath = texture.ResourcePath,
        source, sourceSha256 = Hash( source ), definition, definitionSha256 = Hash( definition ), compiled, compiledSha256 = Hash( compiled ),
        texture.Width, texture.Height, texture.Depth, texture.Mips, format = texture.ImageFormat.ToString(),
        environment = new { type = "cubemap", texturePath = relative + ".vtex", sourcePath = source, sourceSha256 = Hash( source ), compiledPath = compiled, compiledSha256 = Hash( compiled ), sizeInches = 100000, yawDegrees = 0, tint = new[] { 1, 1, 1, 1 }, sourceProvenance = spec.GetProperty( "provenance" ).GetString(), attribution = spec.GetProperty( "attribution" ).Clone() },
        textureGuid = texture.Guid, nativeDirtyVersion = typeof( Texture ).GetField( "DirtyVersion", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic ).GetValue( texture ),
        engineMvid = typeof( Model ).Assembly.ManifestModule.ModuleVersionId.ToString(),
        scope = readOnlyExisting ? "Exact existing compiled disk bytes, native asset/source/path correspondence, descriptor and SceneCubemap construction checked; no source write or Compile call. Resident GPU-byte identity and raw HDR/mip readback remain unproven; rendered IBL response is a separate receipt." : "Native compile and SceneCubemap construction only. Require positive/no-environment/black and wrong-exposure rendered controls; HDR readback, orientation, mips and reference fidelity remain unproven." };
}
finally
{
    if ( probe.IsValid() ) probe.Delete();
    if ( scene.IsValid() ) scene.Destroy();
}
