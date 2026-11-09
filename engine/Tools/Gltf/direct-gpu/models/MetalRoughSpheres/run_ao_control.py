#!/usr/bin/env python3
"""Coordinator-only: capture isolated MetalRoughSpheres forms under one guard admission.

Attach to the verified existing editor; do not launch or alter global settings.
All source materials and camera settings come from the pinned recipe. Results live in a new /tmp directory and are
never published or marked visually approved by this runner.
"""
import argparse
import json
import re
import shutil
import uuid
from PIL import Image, ImageChops, ImageStat
import sys
from pathlib import Path

BASE = Path(__file__).resolve().parents[3]
ROOT = BASE.parents[2]
sys.path.insert(0, str(BASE / 'scripts'))

import importlib.util
spec = importlib.util.spec_from_file_location('metalrough_requests', BASE / 'direct-gpu/models/MetalRoughSpheres/prepare_requests.py')
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
prepare = module.prepare
from run_editor_tests import CliSession, bounded, check_status, decoded, digest, discover, require, save, tool_value
from run_editor_probes import invoke_probe, result_value



def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--pid', type=int, required=True)
    parser.add_argument('--project', type=Path, required=True)
    parser.add_argument('--confirmed-discovery', type=Path, required=True)
    parser.add_argument('--confirmed-sha256', required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--timeout', type=int, default=180)
    parser.add_argument('--admitted', action='store_true')
    parser.add_argument('--preflight', type=Path, required=True)
    parser.add_argument('--packages', type=Path, required=True)
    parser.add_argument('--previews', action='store_true')
    args = parser.parse_args()
    output = args.output.resolve()
    session = None
    owned = []
    try:
        require(args.admitted and args.pid > 0 and 30 <= args.timeout <= 600, 'Coordinator admission, PID and bounded timeout required')
        require(output != Path('/tmp') and output.is_relative_to('/tmp') and not output.exists(), 'Output must be new and strictly under /tmp')
        project = args.project.resolve(strict=True)
        require(project.is_file() and project.suffix == '.sbproj', 'Expected existing project')
        confirmed_raw = bounded(args.confirmed_discovery)
        require(re.fullmatch(r'[0-9a-f]{64}', args.confirmed_sha256) and digest(confirmed_raw) == args.confirmed_sha256, 'Reviewed discovery hash differs')
        confirmed = decoded(confirmed_raw)
        require(confirmed.get('version') == 1 and confirmed.get('mode') == 'discover' and confirmed.get('tests_dispatched') is False and confirmed.get('pid') == args.pid and confirmed.get('project') == str(project), 'Discovery identifies another editor')
        output.mkdir(parents=True)
        cli = (ROOT / 'game/bin/managed/sbox-cli').resolve(strict=True)
        driver = BASE / 'direct-gpu/models/MetalRoughSpheres/ao_capture.csx'
        driver_raw = bounded(driver, 8 * 1024 * 1024)
        inputs = {'driverSha256': digest(driver_raw), 'runnerSha256': digest(bounded(Path(__file__), 1024 * 1024)),
                  'sweepGeneratorSha256': digest(bounded(BASE / 'direct-gpu/models/MetalRoughSpheres/prepare_requests.py', 1024 * 1024)),
                  'cliSha256': digest(bounded(cli))}
        save(output / 'inputs.json', inputs)
        session = CliSession(cli, args.pid, output, args.timeout)
        live, exchange = discover(session, args.pid, project, inputs, output)
        require(live['tools'] == confirmed.get('tools'), 'Live schemas differ from reviewed discovery')
        check_status(tool_value(session.call(live['tools']['editor_status'], {})), project)
        preflight = decoded(bounded(args.preflight, 1024 * 1024))
        packages = decoded(bounded(args.packages, 1024 * 1024))
        requests = output / 'requests'
        require(not args.previews, 'Feedback controls use one source and one compiled form.')
        all_inventory = prepare(requests, preflight, packages, False, variants=('glTF',))
        inventory = [entry for entry in all_inventory if entry['variant'] == 'glTF']
        require(len(inventory) == 2 and {x['form'] for x in inventory} == {'source', 'compiled'}, 'Expected one canonical pair')
        # The same verified requested stock tonemap is used on every capture.
        for entry in inventory:
            path = requests / (entry['label'] + '.json')
            request = decoded(bounded(path, 1024 * 1024))
            request['recipe']['presentation']['tonemapping'] = {'mode': 'ACES', 'exposureCompensationEv': 0}
            save(path, request)
        # One source form only; driver renders the same instance twice.
        inventory = [next(x for x in inventory if x['form'] == 'source')]
        save(output / 'preflight.json', preflight)
        save(output / 'packages.json', packages)
        case_path = exchange / 'gltf-run-case.json'
        require(not case_path.exists(), 'Existing case request must not be replaced')
        script = exchange / f'gltf-metalrough-{uuid.uuid4().hex}.csx'
        def put(path, data):
            with path.open('xb') as f:
                f.write(data)
            owned.append((path, digest(data)))
        put(script, driver_raw)
        records = []
        producer = None
        for entry in inventory:
            label = entry['label']
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
            require(isinstance(value, dict) and value.get('id') == 'MetalRoughSpheres/' + entry['variant'] and not value.get('cleanup_errors'), f'{label}: wrong case or cleanup failure')
            require(value.get('render', {}).get('status') == 'passed', f'{label}: capture failure')
            require(value.get('producer'), f'{label}: missing producer identity')
            require(len(value.get('gpuReadbacks', [])) == 5, f'{label}: expected five actual GPU readbacks')
            if producer is None:
                producer = value['producer']
            require(value['producer'] == producer, 'Loaded producer identity changed')
            records.append({'label': label, 'form': entry['form'], 'variant': entry['variant'], 'lightIntensity': entry['lightIntensity'], 'debugMode': entry.get('debugMode', 'Normal'), 'controlTexture': {'originalSha256': entry.get('originalTextureSha256'), 'swappedSha256': entry.get('swappedTextureSha256')} if entry.get('swappedTextureSha256') else None, 'capture': value.get('capture'), 'render': value.get('render'), 'recipeApplied': value.get('recipeApplied'), 'gpuReadbacks': value.get('gpuReadbacks'), 'materialProof': value.get('materialProof'), 'aoProof': value.get('aoProof')})
            require(digest(bounded(case_path)) == digest(raw), 'Exchange case request changed')
            case_path.unlink(); owned.pop()
            check_status(tool_value(session.call(live['tools']['editor_status'], {})), project)
        save(output / 'summary.json', {'inputs': inputs, 'producer': producer, 'cases': records, 'visualApproval': 'pending', 'scope': 'Public MetalRoughSpheres actual or derived GLB source, pinned camera and source material; HDRI environment. Isolated native diagnostics; not a published or approved result.'})
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
