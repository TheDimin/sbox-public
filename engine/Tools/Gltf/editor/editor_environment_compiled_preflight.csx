// Coordinator-only preflight of the EXISTING compiled HDR environment. No Compile calls.
// RegisterFile may trigger normal AssetSystem bookkeeping; this is not proof of global compiler idleness.
await MainThread.Wait();
if ( !Application.IsEditor || Game.IsPlaying || Graphics.IsActive ) throw new InvalidOperationException( "Requires idle editor outside rendering." );
const string token = "7257c65269394f26805b6a17b9e9d551";
const string expectedExr = "ce074b5f3800017b20eea002544ef3030b05897f521d5142a403332fdc4cb0d6";
const string expectedVtex = "3fd8b91cafe8cd10695fa6b73e1502b38205964c1cbfe5332e1a9a094e41b94c";
const string expectedCompiled = "8273d4022f9f7ccdb43abce967c91d42f8519d6779492315510f635024947a19";
var root = new System.IO.DirectoryInfo( System.IO.Path.GetDirectoryName( typeof( Model ).Assembly.Location ) );
while ( root is not null && !System.IO.File.Exists( System.IO.Path.Combine( root.FullName, "engine/Sandbox-Engine.slnx" ) ) ) root = root.Parent;
if ( root is null ) throw new InvalidOperationException( "Checkout unavailable." );
var relative = "materials/codex_gltf_ibl_" + token + "/artist_workshop_1k";
var absolute = System.IO.Path.Combine( root.FullName, "game/core", relative );
var exr = absolute + ".exr"; var definition = absolute + ".vtex"; var compiled = absolute + ".vtex_c";
string Hash( string path ) { using var stream = System.IO.File.OpenRead( path ); return Convert.ToHexStringLower( System.Security.Cryptography.SHA256.HashData( stream ) ); }
if ( !System.IO.File.Exists( exr ) || !System.IO.File.Exists( definition ) || !System.IO.File.Exists( compiled ) || Hash( exr ) != expectedExr || Hash( definition ) != expectedVtex || Hash( compiled ) != expectedCompiled ) throw new InvalidOperationException( "Existing HDR source, definition or compiled file digest differs." );
var asset = Editor.AssetSystem.FindByPath( compiled ); var didRegister = asset is null;
if ( asset is null ) asset = Editor.AssetSystem.RegisterFile( compiled );
if ( asset is null ) throw new InvalidOperationException( "AssetSystem could not register the existing compiled texture." );
var observation = new { assetType = asset.AssetType?.FileExtension, source = asset.GetSourceFile( true ), compiled = asset.GetCompiledFile( true ), asset.IsCompiled, asset.IsCompiledAndUpToDate, asset.IsCompileFailed, asset.CanRecompile };
if ( !string.Equals( asset.AssetType?.FileExtension, "vtex", StringComparison.OrdinalIgnoreCase ) ) throw new InvalidOperationException( "Registered resource is not a vtex asset." );
var texture = Texture.Load( relative + ".vtex" );
if ( texture is null || !texture.IsValid || texture.IsError || texture.Depth != 6 || texture.Width != texture.Height || texture.Width < 1 || texture.Width > 2048 || texture.Mips < 2 ) throw new InvalidOperationException( "Existing compiled resource did not load as a valid cube with mips." );
var faceRanges = new System.Collections.Generic.List<object>(); string readbackError = null;
bool hdrReadbackPassed = false;
try
{
for ( var face = 0; face < 6; face++ )
{
    // A successful native call must overwrite every destination element. Zero
    // initialization previously made an untouched buffer look like valid black.
    var pixels = Enumerable.Repeat( float.NaN, checked(texture.Width * texture.Height * 4) ).ToArray();
    texture.GetPixels( (0, 0, texture.Width, texture.Height), face, 0, pixels.AsSpan(), ImageFormat.RGBA32323232F );
    var rgb = pixels.Where( (_, i) => i % 4 != 3 ).ToArray();
    if ( rgb.Any( x => !float.IsFinite( x ) ) ) throw new InvalidOperationException( "Compiled cubemap readback contains nonfinite RGB." );
    faceRanges.Add( new { face, minimum = rgb.Min(), maximum = rgb.Max(), mean = rgb.Average( x => (double)x ) } );
    if ( rgb.Max() > 1 ) hdrReadbackPassed = true;
}
if ( !hdrReadbackPassed ) throw new InvalidOperationException( "No decoded HDR sample above 1; resource construction does not establish HDR readback." );
}
catch ( Exception error ) { readbackError = error.ToString(); }
Scene scene = null; SceneCubemap probe = null;
try
{
    scene = Scene.CreateEditorScene();
    probe = new SceneCubemap( scene.SceneWorld, texture, BBox.FromPositionAndSize( Vector3.Zero, 100000 ) );
    probe.Projection = SceneCubemap.ProjectionMode.Sphere; probe.Feathering = .01f; probe.RenderDirty();
    if ( !probe.IsValid() || probe.Texture is null || !probe.Texture.IsValid || probe.Texture.IsError ) throw new InvalidOperationException( "SceneCubemap did not retain texture." );
    return new { passed = hdrReadbackPassed && readbackError is null, constructionPassed = true, hdrReadbackPassed, token, didRegister, resource = relative + ".vtex", textureResourcePath = texture.ResourcePath, source = exr, sourceSha256 = Hash( exr ), definition, definitionSha256 = Hash( definition ), compiled, compiledSha256 = Hash( compiled ), texture.Width, texture.Height, texture.Depth, texture.Mips, format = texture.ImageFormat.ToString(), faceRanges, readbackError, asset = observation, projection = probe.Projection.ToString(), engineMvid = typeof( Model ).Assembly.ManifestModule.ModuleVersionId.ToString(), toolsMvid = typeof( Editor.AssetSystem ).Assembly.ManifestModule.ModuleVersionId.ToString(), scope = "Construction and complete finite HDR readback are separate results; no Compile call. All-zero or untouched readback cannot pass. Pixel-based IBL, mip filtering and visual match remain unproven." };
}
finally
{
    if ( probe.IsValid() ) probe.Delete();
    if ( scene.IsValid() ) scene.Destroy();
}
