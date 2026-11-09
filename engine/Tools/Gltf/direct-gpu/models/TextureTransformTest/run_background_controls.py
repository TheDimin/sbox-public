#!/usr/bin/env python3
"""Capture isolated TextureTransformTest lighting controls under one guard admission.

Attach to the verified existing editor; do not launch or alter global settings.
The light sweep changes only neutral light intensity. A separately requested
canonical run can use measured reference framing. Results live in a new model
output directory and are never published or marked visually approved here.
"""
import argparse
import json
import re
import uuid
from pathlib import Path

import sys
sys.path.insert(0, str(Path(__file__).resolve().parents[3] / 'scripts'))
from prepare_background_controls import prepare
from run_editor_tests import CliSession, bounded, check_status, decoded, digest, discover, require, save, tool_value
from run_editor_probes import invoke_probe, result_value

BASE = Path(__file__).resolve().parents[3]
ROOT = BASE.parents[2]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--pid', type=int, required=True)
    parser.add_argument('--project', type=Path, required=True)
    parser.add_argument('--confirmed-discovery', type=Path, required=True)
    parser.add_argument('--confirmed-sha256', required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--timeout', type=int, default=180)
    parser.add_argument('--admitted', action='store_true')
    parser.add_argument('--canonical-intensity', type=float, help='Capture source plus one canonical compiled GLB with exact authored image dependencies at this measured intensity')
    parser.add_argument('--reference-framing', action='store_true', help='Use 2.6m orthographic framing inferred from the source preview')
    args = parser.parse_args()
    output = args.output.resolve()
    session = None
    owned = []
    try:
        require(args.admitted and args.pid > 0 and 30 <= args.timeout <= 600, 'Coordinator admission, PID and bounded timeout required')
        require(output.is_relative_to(ROOT / 'Temp/gltf-validation/direct-gpu') and not output.exists(), 'Output must be new under Temp/gltf-validation/direct-gpu')
        project = args.project.resolve(strict=True)
        require(project.is_file() and project.suffix == '.sbproj', 'Expected existing project')
        confirmed_raw = bounded(args.confirmed_discovery)
        require(re.fullmatch(r'[0-9a-f]{64}', args.confirmed_sha256) and digest(confirmed_raw) == args.confirmed_sha256, 'Reviewed discovery hash differs')
        confirmed = decoded(confirmed_raw)
        require(confirmed.get('version') == 1 and confirmed.get('mode') == 'discover' and confirmed.get('tests_dispatched') is False and confirmed.get('pid') == args.pid and confirmed.get('project') == str(project), 'Discovery identifies another editor')
        output.mkdir(parents=True)
        cli = (ROOT / 'game/bin/managed/sbox-cli').resolve(strict=True)
        driver = BASE / 'editor' / 'editor_corpus.csx'
        driver_raw = bounded(driver, 8 * 1024 * 1024)
        inputs = {'driverSha256': digest(driver_raw), 'runnerSha256': digest(bounded(Path(__file__), 1024 * 1024)),
                  'sweepGeneratorSha256': digest(bounded(Path(__file__).with_name('prepare_background_controls.py'), 1024 * 1024)),
                  'cliSha256': digest(bounded(cli))}
        if args.canonical_intensity is not None:
            inputs['compileScriptSha256'] = digest(bounded(Path(__file__).with_name('compile_canonical.csx')))
        save(output / 'inputs.json', inputs)
        session = CliSession(cli, args.pid, output, args.timeout)
        live, exchange = discover(session, args.pid, project, inputs, output)
        require(live['tools'] == confirmed.get('tools'), 'Live schemas differ from reviewed discovery')
        check_status(tool_value(session.call(live['tools']['editor_status'], {})), project)
        case_path = exchange / 'gltf-run-case.json'
        require(not case_path.exists(), 'Existing case request must not be replaced')
        script = exchange / f'gltf-texture-background-{uuid.uuid4().hex}.csx'
        def put(path, data):
            with path.open('xb') as f:
                f.write(data)
            owned.append((path, digest(data)))
        put(script, driver_raw)
        compiled = None
        if args.canonical_intensity is not None:
            require(0 < args.canonical_intensity <= 2, 'Canonical intensity must be within (0,2]')
            source = ROOT / 'Temp/gltf-reference/glTF-Sample-Assets/Models/TextureTransformTest/glTF/TextureTransformTest.gltf'
            compiled = output / 'canonical/TextureTransformTest.glb_c'
            compile_source = Path(__file__).with_name('compile_canonical.csx')
            compile_script = exchange / f'gltf-texture-compile-{uuid.uuid4().hex}.csx'
            compile_request = exchange / 'texture-transform-compile-request.json'
            require(not compile_request.exists(), 'Existing compile request must not be replaced')
            put(compile_script, bounded(compile_source))
            compile_inputs = {p.name: digest(bounded(p)) for p in source.parent.iterdir() if p.suffix in ('.gltf', '.png', '.bin')}
            put(compile_request, json.dumps({'source': str(source), 'output': str(compiled), 'inputs': compile_inputs}).encode())
            compile_output = output / 'compile-transport'; compile_output.mkdir()
            compilation = invoke_probe(session, live['tools']['run_csharp'], compile_script, compile_output)
            require(compilation.get('isError') is not True, 'Canonical compilation failed')
            compilation = result_value(compilation)
            save(output / 'compilation.json', compilation)
            require(compiled.is_file() and compilation.get('sha256') == digest(bounded(compiled)), 'Compiled identity differs')
            require(compilation.get('inputs') == compile_inputs, 'Compilation source identity differs')
        requests = prepare(output / 'requests', args.canonical_intensity, compiled, args.reference_framing)
        records = []
        producer = None
        for label in (('baseline', 'locked', 'half', 'quarter', 'material-grid') if args.canonical_intensity is None else ('source', 'compiled')):
            require(digest(bounded(driver, 8 * 1024 * 1024)) == inputs['driverSha256'], 'Driver changed; stop without mixing source revisions')
            check_status(tool_value(session.call(live['tools']['editor_status'], {})), project)
            raw = bounded(requests / f'{label}.json', 1024 * 1024)
            put(case_path, raw)
            case_output = output / f'{label}-transport'
            case_output.mkdir()
            result = invoke_probe(session, live['tools']['run_csharp'], script, case_output)
            require(result.get('isError') is not True, f'{label}: run_csharp failure')
            value = result_value(result)
            save(output / f'{label}-result.json', value)
            require(isinstance(value, dict) and value.get('id') == 'TextureTransformTest/glTF' and not value.get('cleanup_errors'), f'{label}: wrong case or cleanup failure')
            require(value.get('render', {}).get('status') == 'passed', f'{label}: capture failure')
            require(value.get('producer'), f'{label}: missing producer identity')
            if producer is None:
                producer = value['producer']
            require(value['producer'] == producer, 'Loaded producer identity changed')
            records.append({'label': label, 'intensity': args.canonical_intensity if args.canonical_intensity is not None else {'baseline': 2, 'locked': 2, 'half': 1, 'quarter': .5, 'material-grid': .5}[label], 'capture': value.get('capture'), 'render': value.get('render'), 'recipeApplied': value.get('recipeApplied')})
            require(digest(bounded(case_path)) == digest(raw), 'Exchange case request changed')
            case_path.unlink(); owned.pop()
            check_status(tool_value(session.call(live['tools']['editor_status'], {})), project)
        save(output / 'summary.json', {'inputs': inputs, 'producer': producer, 'cases': records, 'visualApproval': 'pending', 'scope': 'First three source lighting controls retain source/camera/material; the separately labeled material-grid is an opaque texel-versus-linear-factor derivative. Canonical mode captures source and one derived compiled GLB. Isolated native diagnostics; not a published or approved result.'})
        print(json.dumps({'status': 'completed', 'output': str(output), 'visualApproval': 'pending'}), flush=True)
        return 0
    except Exception as error:
        print(json.dumps({'status': 'failed', 'error': str(error), 'output': str(output)}), flush=True)
        return 1
    finally:
        for path, expected in reversed(owned):
            try:
                if path.is_file() and digest(bounded(path)) == expected:
                    path.unlink()
            except (OSError, ValueError):
                pass
        # The transport waits for its attached child to exit rather than killing it.
        if session is not None:
            session.close()


if __name__ == '__main__':
    raise SystemExit(main())
