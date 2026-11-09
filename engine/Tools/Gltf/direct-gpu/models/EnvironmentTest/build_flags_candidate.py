#!/usr/bin/env python3
"""Isolate constant-radiance and actual object-lighting flag controls."""
import hashlib
from pathlib import Path

HERE = Path(__file__).resolve().parent
BASE = HERE.parents[2]
source = BASE / 'editor' / 'editor_corpus.csx'
assert hashlib.sha256(source.read_bytes()).hexdigest() == 'cb7e42fc5bcc3f20f2fdedb1cf77618a96fb2d1222499a18fa70a241654f6396'
candidate = source.read_text()

def replace(old, new):
    global candidate
    assert candidate.count(old) == 1, old
    candidate = candidate.replace(old, new)

replace('SceneCubemap environmentProbe = null;', '''SceneCubemap environmentProbe = null;
Texture diagnosticConstantCube = null;
object lightingFlagsBefore = null;''')
replace('string DiskMvid( string path )', '''object LightingFlags( SceneObject value )
{
    var type = typeof( SceneObject ).Assembly.GetType( "Sandbox.Rendering.SceneObjectFlags" );
    var has = value.Flags.GetType().GetMethod( "HasFlag", flags );
    return new { isEnvMap = (bool)has.Invoke( value.Flags, new object[] { Enum.Parse( type, "IsEnvMap" ) } ),
        needsEnvironmentMap = value.Flags.NeedsEnvironmentMap, needsLightProbe = value.Flags.NeedsLightProbe,
        opaque = value.Flags.IsOpaque, translucent = value.Flags.IsTranslucent };
}
string DiskMvid( string path )''')
replace('        var texture = Texture.Load( texturePath );', '''        var texture = Texture.Load( texturePath );
        if ( Field( environment, "diagnosticConstantRadiance" ).ValueKind == System.Text.Json.JsonValueKind.True )
        {
            // A constant cube removes source image/filter/orientation ambiguity.
            // It is an explicit diagnostic, not source-reference environment.
            using var payload = new System.IO.MemoryStream();
            using ( var writer = new System.IO.BinaryWriter( payload, System.Text.Encoding.UTF8, true ) )
                for ( var mip = 4; mip >= 0; mip-- )
                    for ( var face = 0; face < 6; face++ )
                        for ( var pixel = 0; pixel < (16 >> mip) * (16 >> mip); pixel++ )
                            for ( var channel = 0; channel < 4; channel++ ) writer.Write( 1.0f );
            var bytes = payload.ToArray();
            diagnosticConstantCube = Texture.CreateCube( 16, 16, ImageFormat.RGBA32323232F ).WithMips( 5 ).WithData( bytes ).WithStaticUsage().Finish();
            diagnosticConstantCube.MarkUsed( ushort.MaxValue );
            var samples = new System.Collections.Generic.List<object>();
            for ( var mip = 0; mip < 5; mip++ )
                for ( var face = 0; face < 6; face++ )
                {
                    var pixel = Enumerable.Repeat( float.NaN, 4 ).ToArray();
                    diagnosticConstantCube.GetPixels( (0, 0, 1, 1), face, mip, pixel.AsSpan(), ImageFormat.RGBA32323232F );
                    Require( pixel.All( x => x == 1.0f ), "Constant cube face/mip GPU sample differs from literal white radiance." );
                    samples.Add( new { face, mip, pixel } );
                }
            exposureReceipt["diagnosticEnvironment"] = new { type = "constant-white-radiance", payloadSha256 = Hash( bytes ), samples, sourceMaterialsUnchanged = true, scope = "All face/mip samples verified; not source environment or matched reference." };
            texture = diagnosticConstantCube;
        }''')
replace('    var instance = (typeof( ModelRenderer )', '''    lightingFlagsBefore = LightingFlags( renderer.SceneObject );
    var lightingMode = Text( Field( presentation, "diagnosticLightingFlags" ) );
    Require( lightingMode is null or "default" or "enabled" or "disabled", "Invalid lighting flag diagnostic mode." );
    if ( lightingMode is "enabled" or "disabled" )
    {
        renderer.SceneObject.Flags.NeedsEnvironmentMap = lightingMode == "enabled";
        renderer.SceneObject.Flags.NeedsLightProbe = lightingMode == "enabled";
    }
    var instance = (typeof( ModelRenderer )''')
replace('nativeCaptureSubmitted = submitted, widgetFrames, bounds = boundsObserved', 'nativeCaptureSubmitted = submitted, widgetFrames, bounds = boundsObserved, lightingFlagsBefore, lightingFlagsAfterRender = LightingFlags( renderer.SceneObject )')
replace('    Clean( "scene", () =>', '    Clean( "diagnostic cube", () => diagnosticConstantCube?.Dispose() );\n    Clean( "scene", () =>')
target = Path('/tmp/gltf-native-lighting-flags-candidate.csx')
target.write_text(candidate)
print(target, hashlib.sha256(target.read_bytes()).hexdigest())
