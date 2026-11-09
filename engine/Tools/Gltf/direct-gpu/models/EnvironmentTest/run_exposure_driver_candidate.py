#!/usr/bin/env python3
"""Caller owns native flock and guard through terminal receipt inspection."""
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import threading
import uuid

OWNED = Path(__file__).resolve().parent
BASE = OWNED.parents[2]
ROOT = BASE.parents[2]
sys.path.insert(0, str(BASE / 'scripts'))
from run_editor_tests import CliSession, check_status, discover, save, tool_value
from run_editor_probes import invoke_probe, result_value


def main():
    sha = lambda data: hashlib.sha256(data).hexdigest()
    selection_path = (Path(__file__).resolve().parents[6]/'research/gltf-validation/direct-gpu/models/EnvironmentTest') / 'exposure-driver-request.json'
    selection_bytes = selection_path.read_bytes()
    selection = json.loads(selection_bytes)
    candidate = Path(selection['candidatePath'])
    ready = json.loads((ROOT / 'Temp/gltf-validation/editor-session-ready.json').read_text())
    assert ready['ready'] is True
    host = json.loads(subprocess.check_output(['/home/damian/.local/bin/codex-resource-guard', 'status']))
    editor = next(row for row in host['editors'] if row['pid'] == ready['pid'])
    assert editor['start'] == ready['start'] and Path(editor['project']).resolve() == Path(ready['project']).resolve()

    def verify():
        assert selection_path.read_bytes() == selection_bytes
        for path, expected in [(candidate, selection['candidateSha256']), (ROOT / selection['sharedBasePath'], selection['sharedBaseSha256']), (Path(selection['sourcePath']), selection['sourceSha256'])]:
            assert sha(path.read_bytes()) == expected, 'Frozen source changed: ' + str(path)
        for name, producer in selection['producers'].items():
            assert sha((ROOT / 'game/bin/managed' / (name + '.dll')).read_bytes()) == producer['sha256']

    verify()
    output = ROOT / 'Temp/gltf-validation/direct-gpu' / selection['runLabel']
    output.mkdir(exist_ok=False)
    (output / 'transport').mkdir()
    (output / 'frozen-request.json').write_bytes(selection_bytes)
    (output / 'frozen-driver.csx').write_bytes(candidate.read_bytes())
    session = CliSession(ROOT / 'game/bin/managed/sbox-cli', ready['pid'], output / 'transport', 240)
    owned = []
    uncertain = False
    results = []

    def put(path, data):
        with path.open('xb') as stream:
            stream.write(data)
        owned.append((path, sha(data)))

    def remove(path):
        assert owned[-1][0] == path and sha(path.read_bytes()) == owned[-1][1]
        path.unlink()
        owned.pop()

    try:
        discovery_raw = Path(ready['discoveryPath']).read_bytes()
        assert sha(discovery_raw) == ready['discoverySha256']
        live, exchange = discover(session, ready['pid'], Path(ready['project']), {}, output / 'transport')
        assert live['tools'] == json.loads(discovery_raw)['tools']
        check_status(tool_value(session.call(live['tools']['editor_status'], {})), Path(ready['project']))
        script = exchange / ('exposure_candidate_' + uuid.uuid4().hex + '.csx')
        put(script, candidate.read_bytes())
        for case in selection['cases']:
            verify()
            recipe = {'id': case['label'], 'camera': {'space': 'gltf-metres', 'projection': 'orthographic', 'position': [0, 0, 2.54], 'target': [0, 0, 0], 'up': [0, 1, 0], 'orthographicHeight': 2.54}, 'presentation': {'resolution': {'width': 320, 'height': 240}, 'background': {'color': [0, 0, 0, 1]}, 'lights': [], 'environment': {'type': 'engine-default'}, 'exposure': case['baseExposure'], 'tonemapping': {'mode': 'ACES', 'exposureCompensationEv': case['ev']}}}
            request = {'schemaVersion': 1, 'id': 'FieldExposure/' + case['label'], 'corpusRoot': str(OWNED), 'sourcePath': selection['sourcePath'], 'inputPath': Path(selection['sourcePath']).name, 'outputDir': str(output / case['label']), 'recipe': recipe, 'timeoutSeconds': 90}
            case_path = exchange / 'gltf-run-case.json'
            request_bytes = (json.dumps(request, indent=2) + '\n').encode()
            (output / (case['label'] + '-request.json')).write_bytes(request_bytes)
            put(case_path, request_bytes)
            transport = output / (case['label'] + '-transport')
            transport.mkdir()
            uncertain = True
            result = result_value(invoke_probe(session, live['tools']['run_csharp'], script, transport))
            uncertain = False
            save(output / (case['label'] + '-result.json'), result)
            remove(case_path)
            assert result['load']['status'] == 'passed' and result['render']['status'] == 'passed', json.dumps(result.get('render'))
            assert result['cleanupConfirmed'] is True and not result['cleanup_errors']
            for key, name in [('engine', 'Sandbox.Engine'), ('gltf', 'Sandbox.Gltf'), ('renderer', 'Sandbox.SceneRenderer')]:
                assert result['producer'][key]['mvid'] == selection['producers'][name]['mvid']
                assert result['producer'][key]['diskSha256'] == selection['producers'][name]['sha256']
            details = result['recipeApplied']['presentation']['exposureDetails']
            expected = case['literalExpectedScalar']
            assert details['nativeScalarConverged'] is True and details['nativeScalarAfterFinalRenderConverged'] is True
            assert details['expectedNativeScalar'] == expected
            assert abs(details['nativeScalarAfterFinalRender'] - expected) <= max(.001, expected * .005)
            assert all(row['scalar'] > 0 for row in details['nativeScalarSamples'])
            assert result['recipeApplied']['presentation']['exposureLockStatus'] == 'native-scalar-converged-after-render'
            assert len(result['animationFrames']) == 1
            results.append({'label': case['label'], 'literalExpectedScalar': expected, 'resultPath': str(output / (case['label'] + '-result.json')), 'resultSha256': sha((output / (case['label'] + '-result.json')).read_bytes()), 'capture': result['capture'], 'exposure': details})
        from PIL import Image
        import numpy as np
        bright, dark = [np.asarray(Image.open(row['capture']['path']).convert('RGB'), dtype=np.int16) for row in results]
        changed = int(np.any(np.abs(bright - dark) > 3, axis=2).sum())
        passed = changed > 1000 and float(bright.mean()) > float(dark.mean()) + 3
        receipt = {'passed': passed, 'expectedCases': selection['expectedCases'], 'passedCases': len(results), 'requestSha256': sha(selection_bytes), 'driverSha256': selection['candidateSha256'], 'sharedBaseSha256': selection['sharedBaseSha256'], 'changedPixelsAbove3of255': changed, 'base2Ev0Mean': float(bright.mean()), 'base2EvMinus2Mean': float(dark.mean()), 'results': results, 'scope': selection['scope'], 'cleanupErrors': []}
        save(output / 'receipt.json', receipt)
        assert passed, 'Actual negative-EV image did not visibly darken'
        check_status(tool_value(session.call(live['tools']['editor_status'], {})), Path(ready['project']))
        verify()
        print(json.dumps({'receipt': str(output / 'receipt.json'), 'receiptSha256': sha((output / 'receipt.json').read_bytes()), 'passed': passed, 'changedPixels': changed}), flush=True)
    except Exception:
        if uncertain:
            release = output / 'coordinator-confirmed-idle'
            waiter = threading.Event()
            while not release.exists():
                print('Holding admission until coordinator confirms native idle: ' + str(release), file=sys.stderr, flush=True)
                waiter.wait(30)
        raise
    finally:
        for path, expected in reversed(owned):
            if path.exists() and sha(path.read_bytes()) == expected:
                path.unlink()
        session.close()


if __name__ == '__main__':
    main()
