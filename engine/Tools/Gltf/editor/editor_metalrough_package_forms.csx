// Bounded packaging-only MetalRoughSpheres setup, executed under the model lock and guard.
// Produces isolated derived .glb_c forms and required external images; no native model/capture.
await MainThread.Wait();
if ( !Application.IsEditor || Game.IsPlaying || Graphics.IsActive ) throw new InvalidOperationException( "Requires idle editor outside rendering." );
var root = new System.IO.DirectoryInfo( System.IO.Path.GetDirectoryName( typeof( Model ).Assembly.Location ) );
while ( root is not null && !System.IO.File.Exists( System.IO.Path.Combine( root.FullName, "engine/Sandbox-Engine.slnx" ) ) ) root = root.Parent;
if ( root is null ) throw new InvalidOperationException( "Checkout unavailable." );
var corpus = System.IO.Path.Combine( root.FullName, "Temp/gltf-reference/glTF-Sample-Assets/Models/MetalRoughSpheres" );
var output = System.IO.Path.Combine( root.FullName, "Temp/gltf-validation/metalrough-forms", Guid.NewGuid().ToString( "N" ) );
System.IO.Directory.CreateDirectory( output );
string Hash( byte[] bytes ) => Convert.ToHexStringLower( System.Security.Cryptography.SHA256.HashData( bytes ) );
var rows = new System.Collections.Generic.List<object>();
foreach ( var item in new[] {
    (Variant: "glTF", File: "MetalRoughSpheres.gltf", Sha: "a24fd6fd24b2baef80a5d56b465a55635f7ec79039414e25c341d1402cc28048"),
    (Variant: "glTF-Binary", File: "MetalRoughSpheres.glb", Sha: "450c05557b0823a10835e32c05caf77a7b175eba94516555c1c9bb084f563f01"),
    (Variant: "glTF-Embedded", File: "MetalRoughSpheres.gltf", Sha: "748e20d638978194ef773854266bcaa2e966af5cb9d4369e5120b7d43eaa1783") } )
{
    cancellationToken.ThrowIfCancellationRequested(); await MainThread.Wait();
    if ( !Application.IsEditor || Game.IsPlaying || Graphics.IsActive ) throw new InvalidOperationException( "Editor is not idle at safe boundary." );
    var sourceDirectory = System.IO.Path.Combine( corpus, item.Variant ); var sourcePath = System.IO.Path.Combine( sourceDirectory, item.File );
    var source = System.IO.File.ReadAllBytes( sourcePath ); if ( Hash( source ) != item.Sha ) throw new InvalidOperationException( "Pinned source digest differs: " + item.Variant );
    byte[] Resolve( string uri )
    {
        if ( System.IO.Path.IsPathRooted( uri ) || uri.Contains( ".." ) || uri.Contains( '\\' ) || uri.Contains( '/' ) ) throw new InvalidOperationException( "Invalid source dependency." );
        return System.IO.File.ReadAllBytes( System.IO.Path.Combine( sourceDirectory, uri ) );
    }
    var bytes = Sandbox.Gltf.GltfCompiler.Compile( source, Resolve );
    var directory = System.IO.Path.Combine( output, item.Variant ); System.IO.Directory.CreateDirectory( directory );
    var destination = System.IO.Path.Combine( directory, "MetalRoughSpheres.glb_c" ); System.IO.File.WriteAllBytes( destination, bytes );
    var deps = new System.Collections.Generic.List<object>();
    if ( item.Variant == "glTF" )
        foreach ( var image in new[] { "Spheres_BaseColor.png", "Spheres_MetalRough.png" } )
        {
            var data = Resolve( image ); System.IO.File.WriteAllBytes( System.IO.Path.Combine( directory, image ), data ); deps.Add( new { path = image, sha256 = Hash( data ) } );
        }
    rows.Add( new { item.Variant, sourcePath, sourceSha256 = item.Sha, compiledPath = destination, compiledSha256 = Hash( bytes ), bytes = bytes.Length, dependencies = deps } );
}
return new { output, rows, engineMvid = typeof( Model ).Assembly.ManifestModule.ModuleVersionId.ToString(), gltfMvid = typeof( Sandbox.Gltf.GltfReader ).Assembly.ManifestModule.ModuleVersionId.ToString(), scope = "Derived GLB packaging only, external images retained; not public Model.Load, GPU, capture or material-parity evidence." };
