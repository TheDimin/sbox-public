#!/usr/bin/env python3
"""Build an isolated patch against the pinned shared driver, never edit it."""
import difflib
import hashlib
from pathlib import Path

HERE = Path(__file__).resolve().parent
BASE = HERE.parents[2]
source_path = BASE / 'editor' / 'editor_corpus.csx'
original = source_path.read_text()
assert hashlib.sha256(source_path.read_bytes()).hexdigest() == 'cb7e42fc5bcc3f20f2fdedb1cf77618a96fb2d1222499a18fa70a241654f6396', 'Shared driver freeze changed'
needle = 'string DiskMvid( string path )'
function = '''// Read-only native scalar query: the managed renderer uses this same function
// with update=false when native already advanced the exposure system.
float NativeExposureScalar()
{
    var sceneCamera = typeof( CameraComponent ).GetProperty( "SceneCamera", flags ).GetValue( camera );
    Require( sceneCamera is not null, "Capture SceneCamera is unavailable." );
    var toneMapping = sceneCamera.GetType().GetField( "ToneMapping", flags ).GetValue( sceneCamera );
    Require( toneMapping is not null, "Capture native ToneMapping is unavailable." );
    var system = toneMapping.GetType().GetMethod( "GetNative", flags ).Invoke( toneMapping, null );
    Require( (IntPtr)system.GetType().GetField( "self", flags ).GetValue( system ) != IntPtr.Zero, "Native exposure system is null." );
    var tools = typeof( Model ).Assembly.GetType( "NativeEngine.RenderTools" );
    var method = tools.GetMethod( "TonemapFrameUpdate", flags | System.Reflection.BindingFlags.Static );
    return Convert.ToSingle( method.Invoke( null, new object[] { system, false } ) );
}
'''
assert original.count(needle) == 1
candidate = original.replace(needle, function + needle)
needle = '        bool submitted; using ( scene.Push() ) { targetTexture.Clear( Color.Magenta ); submitted = camera.RenderToTexture( targetTexture ); }'
warmup = '''        if ( explicitTonemapping is not null )
        {
            var expectedScalar = exposure * Math.Pow( 2, exposureCompensationEv );
            var samples = new System.Collections.Generic.List<object>();
            var settling = System.Diagnostics.Stopwatch.StartNew();
            var converged = false;
            // Only this branch installs fixed min=max exposure. Auto-exposure
            // recipes without it keep their existing stable-output policy.
            for ( var attempt = 0; attempt < 60 && settling.Elapsed.TotalSeconds < 20; attempt++ )
            {
                await SafePoint();
                bool warmSubmitted; using ( scene.Push() ) { targetTexture.Clear( Color.Magenta ); warmSubmitted = camera.RenderToTexture( targetTexture ); }
                Require( warmSubmitted, "Native exposure warmup rejected capture." );
                var scalar = NativeExposureScalar();
                samples.Add( new { attempt, scalar = float.IsFinite( scalar ) ? (object)scalar : scalar.ToString(), expectedScalar, frame = Application.FrameCount, wallSeconds = settling.Elapsed.TotalSeconds } );
                exposureReceipt["nativeScalarSamples"] = samples;
                Require( float.IsFinite( scalar ) && scalar > 0, "Native exposure scalar is invalid." );
                if ( Math.Abs( scalar - expectedScalar ) <= Math.Max( .001, expectedScalar * .005 ) ) { converged = true; break; }
                await WaitFrames();
            }
            exposureReceipt["nativeScalarConverged"] = converged;
            exposureReceipt["expectedNativeScalar"] = expectedScalar;
            Require( converged, "Native exposure failed to converge within 60 real captures/20 seconds." );
            // Publish a subsequent frame, after the observed scalar became
            // current, rather than the frame whose histogram advanced it.
            await WaitFrames();
        }
'''
assert candidate.count(needle) == 1
candidate = candidate.replace(needle, warmup + needle)
needle = '            exposureReceipt["locked"] = retained; exposureReceipt["lockStatus"] = retained ? "component-settings-retained-after-render" : "failed";'
final_check = '''            var finalScalar = NativeExposureScalar();
            var expectedScalar = exposure * Math.Pow( 2, exposureCompensationEv );
            var finalConverged = float.IsFinite( finalScalar ) && finalScalar > 0
                && Math.Abs( finalScalar - expectedScalar ) <= Math.Max( .001, expectedScalar * .005 );
            exposureReceipt["nativeScalarAfterFinalRender"] = float.IsFinite( finalScalar ) ? (object)finalScalar : finalScalar.ToString();
            exposureReceipt["nativeScalarAfterFinalRenderConverged"] = finalConverged;
            Require( finalConverged, "Final native exposure scalar is outside the fixed target tolerance." );
            exposureReceipt["locked"] = retained && finalConverged;
            exposureReceipt["lockStatus"] = retained && finalConverged ? "native-scalar-converged-after-render" : "failed";'''
assert candidate.count(needle) == 1
candidate = candidate.replace(needle, final_check)
target = Path('/tmp/gltf-native-exposure-candidate.csx')
target.write_text(candidate)
patch = ''.join(difflib.unified_diff(original.splitlines(True), candidate.splitlines(True), fromfile='a/engine/Tools/Gltf/editor/editor_corpus.csx', tofile='b/engine/Tools/Gltf/editor/editor_corpus.csx'))
(HERE / 'native-exposure-convergence.patch').write_text(patch)
print(target, hashlib.sha256(target.read_bytes()).hexdigest())
