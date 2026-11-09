// Bounded checks of the actual loaded cubemap; failed/unchanged readbacks remain evidence.
await MainThread.Wait();
if ( !Application.IsEditor || Game.IsPlaying || Graphics.IsActive ) throw new InvalidOperationException( "Requires idle editor." );
var resource = "materials/gltf_reference_environment/field.vtex";
var texture = Texture.Load( resource );
if ( texture is not { IsValid: true, IsError: false } || texture.Depth != 6 ) throw new InvalidOperationException( "Invalid Field cubemap." );
var rows = new System.Collections.Generic.List<object>();
for ( int face = 0; face < 6; face++ )
foreach ( var pos in new[] { 0, 8, 32 } )
{
    var sample = new[] { float.NaN, float.NaN, float.NaN, float.NaN }; string error = null;
    try { texture.GetPixels( (pos,pos,1,1), face, 0, sample.AsSpan(), ImageFormat.RGBA32323232F ); }
    catch ( Exception ex ) { error = ex.ToString(); }
    rows.Add( new { face, x = pos, y = pos, values = sample.Select( x => float.IsFinite( x ) ? (float?)x : null ).ToArray(), error } );
}
return new { resource, texture.Width, texture.Height, texture.Depth, texture.Mips, format = texture.ImageFormat.ToString(), rows, engineMvid = typeof( Model ).Assembly.ManifestModule.ModuleVersionId.ToString(), scope = "18 bounded GPU face samples, not full HDR, orientation, mip filtering or reference-match proof." };
