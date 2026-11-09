// Coordinator-only preflight for a mounted, compiled environment texture.
// A valid cubemap is not evidence that public glTF draws receive IBL.
await MainThread.Wait();
if ( !Application.IsEditor || Game.IsPlaying || Graphics.IsActive ) throw new InvalidOperationException( "Requires an idle editor at a safe graphics boundary." );
var path = "research/gltf-validation/fixtures/environment/artist_workshop_1k.vtex";
var texture = Texture.Load( path );
if ( texture is null || !texture.IsValid || texture.IsError ) throw new InvalidOperationException( "Compiled HDR environment texture unavailable: " + path );
if ( texture.Depth != 6 || texture.Width != texture.Height || texture.Mips < 2 ) throw new InvalidOperationException( "Expected a square cubemap with a mip chain." );
var scene = Scene.CreateEditorScene();
SceneCubemap probe = null;
try
{
    probe = new SceneCubemap( scene.SceneWorld, texture, BBox.FromPositionAndSize( Vector3.Zero, 100000 ) );
    probe.Projection = SceneCubemap.ProjectionMode.Sphere;
    probe.Feathering = .01f;
    probe.RenderDirty();
    if ( !probe.IsValid() || probe.Texture is null || !probe.Texture.IsValid || probe.Texture.IsError ) throw new InvalidOperationException( "SceneCubemap did not retain a valid texture." );
    return new { passed = true, path, texture.Width, texture.Height, texture.Depth, texture.Mips, format = texture.ImageFormat.ToString(), projection = probe.Projection.ToString(), scope = "Texture and SceneCubemap construction only; no rendered pixels, HDR range, roughness filtering, orientation, public glTF IBL binding or visual acceptance established." };
}
finally
{
    if ( probe.IsValid() ) probe.Delete();
    if ( scene.IsValid() ) scene.Destroy();
}
