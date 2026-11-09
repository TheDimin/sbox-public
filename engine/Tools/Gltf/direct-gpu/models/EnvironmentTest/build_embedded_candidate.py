#!/usr/bin/env python3
"""Private derived-environment capture candidate; shared driver stays frozen."""
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
Texture embeddedEnvironmentCube = null;
System.Text.Json.JsonDocument embeddedEnvironmentDocument = null;''')
replace('        var texture = Texture.Load( texturePath );', '''        var texture = Texture.Load( texturePath );
        var derivedFixture = Text( Field( environment, "derivedEmbeddedFixture" ) );
        if ( derivedFixture is not null )
        {
            Require( System.IO.Path.IsPathFullyQualified( derivedFixture ) && System.IO.Path.GetFullPath( derivedFixture ).StartsWith( "/tmp/", StringComparison.Ordinal ), "Derived environment fixture must be an absolute /tmp path." );
            embeddedEnvironmentDocument = System.Text.Json.JsonDocument.Parse( System.IO.File.ReadAllBytes( derivedFixture ) );
            var fixture = embeddedEnvironmentDocument.RootElement;
            var sourceDocument = Text( Field( fixture, "source" ) );
            Require( HashFile( sourceDocument ) == Text( Field( fixture, "sourceSha256" ) ) && Text( Field( fixture, "sourceSha256" ) ) == "43bb563c795f299fd14a2c0f3127f921768a7be1510f41310a6b046fde723679", "Pinned embedded-IBL document differs." );
            foreach ( var buffer in Field( fixture, "buffers" ).EnumerateArray() )
                Require( HashFile( Text( Field( buffer, "sourcePath" ) ) ) == Text( Field( buffer, "sha256" ) ), "Pinned embedded-IBL source buffer differs." );
            var payloadPath = Text( Field( fixture, "payload" ) );
            var payload = System.IO.File.ReadAllBytes( payloadPath );
            Require( Hash( payload ) == Text( Field( fixture, "payloadSha256" ) ), "Derived cube payload differs." );
            var cubeSize = (int)Number( Field( fixture, "size" ), -1 );
            var cubeMips = (int)Number( Field( fixture, "mips" ), -1 );
            Require( cubeSize == 256 && cubeMips == 5, "Unexpected authored EnvironmentTest cube layout." );
            embeddedEnvironmentCube = Texture.CreateCube( cubeSize, cubeSize, ImageFormat.RGBA32323232F ).WithMips( cubeMips ).WithData( payload ).WithStaticUsage().Finish();
            embeddedEnvironmentCube.MarkUsed( ushort.MaxValue );
            var observations = new System.Collections.Generic.List<object>();
            foreach ( var row in Field( fixture, "samples" ).EnumerateArray() )
            {
                var face = (int)Number( Field( row, "face" ), -1 );
                var mip = (int)Number( Field( row, "mip" ), -1 );
                var size = cubeSize >> mip;
                foreach ( var sample in new[] { (Name: "firstPixel", X: 0, Y: 0), (Name: "centerPixel", X: size / 2, Y: size / 2), (Name: "lastPixel", X: size - 1, Y: size - 1) } )
                {
                    var actual = Enumerable.Repeat( float.NaN, 4 ).ToArray();
                    var expected = Field( row, sample.Name ).EnumerateArray().Select( x => x.GetSingle() ).ToArray();
                    embeddedEnvironmentCube.GetPixels( (sample.X, sample.Y, 1, 1), face, mip, actual.AsSpan(), ImageFormat.RGBA32323232F );
                    Require( actual.Length == expected.Length && actual.SequenceEqual( expected ), "Derived source cube GPU face/mip samples differ." );
                    observations.Add( new { face, mip, sample.Name, actual } );
                }
            }
            exposureReceipt["derivedEmbeddedEnvironment"] = new { fixture = fixture.Clone(), observations, scope = "Private reconstructed source fixture only; public glTF EXT_lights_image_based remains unconsumed. Authored diffuse SH retained as metadata, not applied; native stock roughness/LOD mapping may differ." };
            texture = embeddedEnvironmentCube;
        }''')
replace('    Clean( "scene", () =>', '''    Clean( "derived environment texture", () => embeddedEnvironmentCube?.Dispose() );
    Clean( "derived environment fixture", () => embeddedEnvironmentDocument?.Dispose() );
    Clean( "scene", () =>''')
target = Path('/tmp/gltf-native-embedded-environment-candidate.csx')
target.write_text(candidate)
print(target, hashlib.sha256(target.read_bytes()).hexdigest())
