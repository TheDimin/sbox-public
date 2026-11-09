#!/usr/bin/env python3
"""Capture the pinned BoxVertexColors source and its compiled input privately.

Use only inside the model-worker editor lock and resource guard. This attaches to
an already verified editor and does not touch the shared Run or publish results.
"""
import argparse
import hashlib
import json
import re
import sys
import uuid
from pathlib import Path

BASE = Path(__file__).resolve().parents[3]
ROOT = BASE.parents[2]
sys.path.insert(0, str(BASE / 'scripts'))
from run_editor_tests import CliSession, bounded, check_status, decoded, digest, discover, require, save, tool_value
from run_editor_probes import invoke_probe, result_value

PIN = '90d7ede14c7e280af263824604b427a1ca02cb66'


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--pid', type=int, required=True)
    p.add_argument('--project', type=Path, required=True)
    p.add_argument('--confirmed-discovery', type=Path, required=True)
    p.add_argument('--confirmed-sha256', required=True)
    p.add_argument('--task', type=Path, required=True)
    p.add_argument('--test-receipt', type=Path, required=True)
    p.add_argument('--engine-sha256', required=True)
    p.add_argument('--gltf-sha256', required=True)
    p.add_argument('--admitted', action='store_true')
    args = p.parse_args()
    require(all(re.fullmatch(r'[0-9a-f]{64}', value) for value in (args.engine_sha256, args.gltf_sha256)), 'Expected exact loaded producer hashes')
    task = args.task.resolve(strict=True)
    allowed = (ROOT / 'Temp/gltf-validation/direct-gpu/model-workers').resolve(strict=True)
    require(args.admitted and task.is_relative_to(allowed) and task != allowed, 'Expected a guarded isolated model-worker task')
    transport = task / 'capture-transport'
    require(not transport.exists(), 'Capture transport already exists')
    project = args.project.resolve(strict=True)
    approved_raw = bounded(args.confirmed_discovery)
    require(re.fullmatch(r'[0-9a-f]{64}', args.confirmed_sha256) and digest(approved_raw) == args.confirmed_sha256, 'Discovery hash differs')
    approved = decoded(approved_raw)
    require(approved.get('version') == 1 and approved.get('mode') == 'discover' and approved.get('tests_dispatched') is False and approved.get('pid') == args.pid and approved.get('project') == str(project), 'Discovery identifies a different editor')
    test = decoded(bounded(args.test_receipt))
    require(test.get('passed') is True and test.get('expected_cases') == 103 and test.get('passed_cases') == 103 and not test.get('cleanup_errors'), 'Native and unit preflight did not pass')
    compiled_rows = [row.get('result', {}) for row in test.get('tests', []) if row.get('result', {}).get('Form') == 'glTF/compiled_glb' and 'Path' in row.get('result', {}) and 'Sha256' in row.get('result', {})]
    require(len(compiled_rows) == 1 and compiled_rows[0].get('Passed') is True, 'Compiled input receipt is missing')
    compiled = (task / 'compiled/boxvertexcolors.glb_c').resolve(strict=True)
    require(str(compiled) == compiled_rows[0]['Path'] and digest(bounded(compiled)) == compiled_rows[0]['Sha256'], 'Compiled input differs from test receipt')
    recipes_raw = bounded(ROOT / 'research/gltf-validation/reference-recipes.json', 32 * 1024 * 1024)
    recipes = decoded(recipes_raw)
    require(recipes.get('corpusPin') == PIN, 'Pinned recipe revision differs')
    recipe = next(row for row in recipes['cases'] if row['id'] == 'BoxVertexColors/glTF')
    corpus = Path(recipes['corpusRoot']).resolve(strict=True)
    source = (corpus / recipe['input']['path']).resolve(strict=True)
    require(source.is_relative_to(corpus) and digest(bounded(source)) == recipe['input']['sha256'], 'Source input differs from pinned recipe')
    driver = BASE / 'editor' / 'editor_corpus.csx'
    driver_raw = bounded(driver, 8 * 1024 * 1024)
    cli = (ROOT / 'game/bin/managed/sbox-cli').resolve(strict=True)
    transport.mkdir()
    inputs = {'driverSha256': digest(driver_raw), 'runnerSha256': digest(bounded(Path(__file__))), 'recipeFileSha256': digest(recipes_raw), 'cliSha256': digest(bounded(cli)), 'testReceiptSha256': digest(bounded(args.test_receipt))}
    save(transport / 'inputs.json', inputs)
    session = None
    owned = []
    records = []
    try:
        session = CliSession(cli, args.pid, transport, 300)
        live, exchange = discover(session, args.pid, project, inputs, transport)
        require(live['tools'] == approved.get('tools'), 'Live tool schemas differ from approved discovery')
        check_status(tool_value(session.call(live['tools']['editor_status'], {})), project)
        case_path = exchange / 'gltf-run-case.json'
        require(not case_path.exists(), 'Existing case request must not be replaced')
        script = exchange / f'gltf-boxvertexcolors-{uuid.uuid4().hex}.csx'
        def put(path, raw):
            with path.open('xb') as f:
                f.write(raw)
            owned.append((path, digest(raw)))
        put(script, driver_raw)
        cases = [('source', source, corpus, recipe['input']['path'], 'BoxVertexColors/glTF'),
                 ('compiled', compiled, compiled.parent, compiled.name, 'BoxVertexColors/compiled_glb')]
        for label, input_file, corpus_root, input_path, case_id in cases:
            require(digest(bounded(driver, 8 * 1024 * 1024)) == inputs['driverSha256'], 'Capture driver changed')
            check_status(tool_value(session.call(live['tools']['editor_status'], {})), project)
            output = task / 'captures' / label
            request = {'schemaVersion': 1, 'id': case_id, 'sourcePath': str(input_file), 'corpusRoot': str(corpus_root), 'inputPath': input_path, 'outputDir': str(output), 'recipe': recipe, 'timeoutSeconds': 90}
            raw = (json.dumps(request, indent=2) + '\n').encode()
            put(case_path, raw)
            out = transport / f'{label}-probe'; out.mkdir()
            result = invoke_probe(session, live['tools']['run_csharp'], script, out)
            require(result.get('isError') is not True, f'{label}: run_csharp failed')
            value = result_value(result)
            save(transport / f'{label}-result.json', value)
            require(isinstance(value, dict) and value.get('id') == case_id and value.get('load', {}).get('status') == 'passed' and value.get('render', {}).get('status') == 'passed' and not value.get('cleanup_errors') and value.get('recipeApplied', {}).get('complete') is True, f'{label}: public capture failed')
            producer = value.get('producer', {})
            require(producer.get('engine', {}).get('diskSha256') == args.engine_sha256 and producer.get('gltf', {}).get('diskSha256') == args.gltf_sha256, f'{label}: loaded producer hash differs')
            image = Path(value['capture']['path']).resolve(strict=True)
            require(image.is_relative_to(output.resolve()) and digest(bounded(image)) == value['capture']['sha256'], f'{label}: capture digest differs')
            png = bounded(image); require(png[:8] == b'\x89PNG\r\n\x1a\n' and int.from_bytes(png[16:20], 'big') == 512 and int.from_bytes(png[20:24], 'big') == 512, f'{label}: capture is not 512x512 PNG')
            records.append({'label': label, 'input': str(input_file), 'inputSha256': digest(bounded(input_file)), 'capture': value['capture'], 'recipeApplied': value['recipeApplied'], 'producer': producer, 'cleanupConfirmed': value['cleanupConfirmed']})
            require(digest(bounded(case_path)) == digest(raw), 'Exchange request changed')
            case_path.unlink(); owned.pop()
            check_status(tool_value(session.call(live['tools']['editor_status'], {})), project)
        close_result = session.close()
        session = None
        require(close_result.get('exit_code') == 0 and close_result.get('interrupted_request') is None, 'CLI session did not finish cleanly')
        save(task / 'captures-summary.json', {'passed': True, 'inputs': inputs, 'cases': records, 'cliClose': close_result, 'visualApproval': 'pending', 'scope': 'Isolated public source and compiled Model.Load captures using the pinned 512px recipe; no published Run or independent material-parity claim.'})
        print(json.dumps({'passed': True, 'summary': str(task / 'captures-summary.json')}), flush=True)
        return 0
    except Exception as error:
        save(task / 'captures-summary.json', {'passed': False, 'error': str(error), 'cases': records, 'visualApproval': 'pending'})
        print(json.dumps({'passed': False, 'error': str(error), 'summary': str(task / 'captures-summary.json')}), flush=True)
        return 1
    finally:
        for path, expected in reversed(owned):
            try:
                if path.is_file() and digest(bounded(path)) == expected:
                    path.unlink()
            except (OSError, ValueError):
                pass
        if session is not None:
            session.close()


if __name__ == '__main__':
    raise SystemExit(main())
