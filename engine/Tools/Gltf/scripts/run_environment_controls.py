#!/usr/bin/env python3
"""Coordinator: stage/import HDRI and capture no/black/HDR controls in one admission.

Run only as the foreground child of `codex-resource-guard run` after host status,
editor/window/project checks and reviewed tool discovery. Never launches editors.
If import completion is uncertain after watched files were staged, this process
intentionally holds its guard reservation until the coordinator intervenes.
"""
import argparse
import json
import os
import re
import subprocess
import sys
import threading
import uuid
from pathlib import Path

from run_editor_tests import (CliSession, bounded, check_status, decoded, digest,
                              discover, require, save, tool_value)
from run_editor_probes import invoke_probe, result_value
from prepare_environment_controls import prepare

BASE = Path(__file__).resolve().parent
ROOT = BASE.parents[3]


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--pid', type=int, required=True)
    p.add_argument('--project', type=Path, required=True)
    p.add_argument('--confirmed-discovery', type=Path, required=True)
    p.add_argument('--confirmed-sha256', required=True)
    p.add_argument('--output', type=Path, required=True, help='New directory strictly under /tmp')
    p.add_argument('--timeout', type=int, default=180)
    p.add_argument('--admitted', action='store_true')
    p.add_argument('--resume-stage', type=Path, required=True, help='Exact original stage.stdout JSON')
    p.add_argument('--resume-converted', type=Path, help='Prior import-value.json for the same already-transcoded asset; no file writes')
    args = p.parse_args()
    output = args.output.resolve()
    session = None
    staged = False
    compile_confirmed = False
    owned_exchange = []
    try:
        require(args.admitted and args.pid > 0 and 30 <= args.timeout <= 600, 'Requires coordinator admission, PID and bounded timeout')
        require(output != Path('/tmp') and output.is_relative_to('/tmp') and not output.exists(), 'Output must be new and strictly under /tmp')
        project = args.project.resolve(strict=True)
        require(project.is_file() and project.suffix == '.sbproj', 'Expected .sbproj')
        approved_raw = bounded(args.confirmed_discovery)
        require(re.fullmatch(r'[0-9a-f]{64}', args.confirmed_sha256) and digest(approved_raw) == args.confirmed_sha256, 'Reviewed discovery hash differs')
        approved = decoded(approved_raw)
        require(approved.get('version') == 1 and approved.get('mode') == 'discover' and approved.get('tests_dispatched') is False and approved.get('pid') == args.pid and approved.get('project') == str(project), 'Discovery belongs to another editor')
        output.mkdir(parents=True)
        cli = (ROOT / 'game/bin/managed/sbox-cli').resolve(strict=True)
        scripts = {name: bounded(BASE / name, 8 * 1024 * 1024) for name in ('editor_environment_import_probe.csx', 'editor_corpus.csx', 'transcode_environment_exr.py', 'prepare_environment_controls.py')}
        inputs = {'source_sha256': {name: digest(data) for name, data in scripts.items()}, 'cli_sha256': digest(bounded(cli))}
        save(output / 'inputs.json', inputs)
        session = CliSession(cli, args.pid, output, args.timeout)
        live, exchange = discover(session, args.pid, project, inputs, output)
        require(live['tools'] == approved.get('tools'), 'Live schemas differ from reviewed discovery')
        check_status(tool_value(session.call(live['tools']['editor_status'], {})), project)
        stage_value = None
        if args.resume_stage is not None:
            stage_value = decoded(bounded(args.resume_stage, 4096))
            resource = stage_value.get('resource', '')
            match = re.fullmatch(r'materials/codex_gltf_ibl_([0-9a-f]{32})/artist_workshop_1k\.vtex', resource)
            require(match is not None, 'Resume stage has an invalid resource name')
            token = match.group(1)
            stem = ROOT / 'game/core' / resource.removesuffix('.vtex')
            source = stem.with_suffix('.hdr'); definition = stem.with_suffix('.vtex')
            require(source.resolve(strict=True) == source and definition.resolve(strict=True) == definition, 'Staged files must not be symlinks')
            require(stage_value.get('source') == str(source) and stage_value.get('vtex') == str(definition), 'Resume stage paths differ')
            require(stage_value.get('sourceSha256') == 'f6da9025acadb1149c973fe2bef53d55f4e7144285a87ec1a357520fbcad4767' and digest(bounded(source, 4 * 1024 * 1024)) == stage_value['sourceSha256'], 'Staged HDR differs')
            if args.resume_converted is None:
                require(digest(bounded(definition, 65536)) == stage_value.get('vtexSha256'), 'Staged vtex differs')
            save(output / 'resumed-stage.json', stage_value)
        else:
            raise ValueError('A previous staged asset is required')
        request_path = exchange / 'gltf-environment-import.json'
        case_path = exchange / 'gltf-run-case.json'
        require(not request_path.exists() and not case_path.exists(), 'Exchange request already exists; stop without replacing it')
        def put(path, data):
            with path.open('xb') as f:
                f.write(data)
            owned_exchange.append((path, digest(data)))
        import_script = exchange / f'gltf-env-{token}-import.csx'
        corpus_script = exchange / f'gltf-env-{token}-corpus.csx'
        put(import_script, scripts['editor_environment_import_probe.csx'])
        put(corpus_script, scripts['editor_corpus.csx'])
        exr = stem.with_suffix('.exr')
        if args.resume_converted is not None:
            previous = decoded(bounded(args.resume_converted, 4096))
            conversion = decoded(bounded(args.resume_converted.parent / 'conversion.json', 4096))
            require(previous.get('token') == token and previous.get('resource') == resource, 'Converted receipt identifies another asset')
            require(conversion.get('exactFloatRoundtrip') is True and conversion.get('sourceSha256') == stage_value['sourceSha256'], 'Converted source provenance differs')
            require(exr.resolve(strict=True) == exr and digest(bounded(exr, 32 * 1024 * 1024)) == conversion.get('exrSha256') == previous.get('sourceSha256'), 'Existing EXR digest differs')
            actual_definition_sha = digest(bounded(definition, 65536))
            require(actual_definition_sha == previous.get('definitionSha256'), 'Existing converted vtex differs')
            require((resource.removesuffix('.vtex') + '.exr').encode() in bounded(definition, 65536), 'Existing vtex does not reference same EXR')
            save(output / 'conversion.json', conversion)
        else:
            # Conversion runs under this guard but outside watched paths; only a
            # verified exact linear float roundtrip is published to the existing asset.
            converted = output / 'artist_workshop_1k.exr'
            transcode = subprocess.run([sys.executable, str(BASE / 'transcode_environment_exr.py'), str(source), str(converted)], capture_output=True, text=True, check=False)
            (output / 'transcode.stdout').write_text(transcode.stdout)
            (output / 'transcode.stderr').write_text(transcode.stderr)
            require(transcode.returncode == 0, 'Linear EXR conversion failed')
            conversion = json.loads(transcode.stdout)
            require(conversion.get('exactFloatRoundtrip') is True and conversion.get('sourceSha256') == stage_value['sourceSha256'], 'EXR roundtrip or source identity differs')
            exr = stem.with_suffix('.exr')
            old_definition = bounded(definition, 65536)
            old_reference = (resource.removesuffix('.vtex') + '.hdr').encode()
            new_reference = (resource.removesuffix('.vtex') + '.exr').encode()
            require(old_definition.count(old_reference) == 1 and not exr.exists(), 'Expected original HDR vtex and absent EXR')
            new_definition = old_definition.replace(old_reference, new_reference)
            # Watched changes begin here. From this point, unresolved errors retain
            # the reservation until an asset-specific terminal result is confirmed.
            staged = True
            with exr.open('xb') as f:
                f.write(bounded(converted, 32 * 1024 * 1024))
            require(digest(bounded(exr, 32 * 1024 * 1024)) == conversion['exrSha256'], 'Staged EXR differs')
            pending = definition.with_name(definition.name + '.' + token + '.pending')
            with pending.open('xb') as f:
                f.write(new_definition)
            os.replace(pending, definition)
            actual_definition_sha = digest(bounded(definition, 65536))
            require(actual_definition_sha == digest(new_definition), 'Updated vtex differs')
            save(output / 'conversion.json', conversion)
        staged = True
        put(request_path, (json.dumps({'token': token, 'sourceSha256': conversion['exrSha256'], 'definitionSha256': actual_definition_sha, 'requireExisting': args.resume_converted is not None}) + '\n').encode())
        import_result = invoke_probe(session, live['tools']['run_csharp'], import_script, output)
        require(import_result.get('isError') is not True, 'Import probe failed; inspect compiler state before releasing admission')
        value = result_value(import_result)
        save(output / 'import-value.json', value)
        require(isinstance(value, dict) and value.get('token') == token and value.get('resource') == stage_value['resource'], 'Import returned another asset')
        require(value.get('sourceSha256') == conversion['exrSha256'] and value.get('definitionSha256') == actual_definition_sha, 'Imported source identity differs')
        if value.get('compileOutcome') == 'failed' and value.get('isCompileFailed') is True:
            compile_confirmed = True
            raise ValueError('Native asset compile returned a confirmed terminal failure; see import-value.json')
        require(value.get('compileOutcome') == 'compiled', 'Import did not confirm a terminal compiled asset')
        compile_confirmed = True
        require(value.get('passed') is True, 'Cubemap preflight failed; see import-value.json')
        check_status(tool_value(session.call(live['tools']['editor_status'], {})), project)
        controls = prepare(output / 'requests', value['resource'], Path(value['compiled']), exr)
        observations = []
        for label in ('no_environment', 'black_environment', 'artist_workshop'):
            raw = (controls / (label + '.json')).read_bytes()
            put(case_path, raw)
            result = invoke_probe(session, live['tools']['run_csharp'], corpus_script, output)
            require(result.get('isError') is not True, f'{label}: tool failure')
            row = result_value(result)
            save(output / f'{label}-result.json', row)
            require(isinstance(row, dict) and row.get('id') == 'CompareMetallic/glTF' and not row.get('cleanup_errors'), f'{label}: wrong case or cleanup failed')
            observations.append({'label': label, 'load': row.get('load'), 'render': row.get('render'), 'capture': row.get('capture')})
            require(digest(bounded(case_path)) == digest(raw), 'Owned exchange request changed')
            case_path.unlink(); owned_exchange.pop()
            require(row.get('render', {}).get('status') == 'passed', f'{label}: capture failed')
            check_status(tool_value(session.call(live['tools']['editor_status'], {})), project)
        save(output / 'summary.json', {'import': value, 'controls': observations, 'visualApproval': 'pending', 'scope': 'Native import and three unedited control captures; no automatic material or reference approval.'})
        print(json.dumps({'status': 'completed', 'output': str(output), 'visualApproval': 'pending'}), flush=True)
        return 0
    except Exception as error:
        print(json.dumps({'status': 'failed', 'error': str(error), 'output': str(output), 'importCompletionConfirmed': compile_confirmed}), file=sys.stderr, flush=True)
        if staged and not compile_confirmed:
            # A failed/timeout RPC is not proof that the editor stopped compiling.
            # Keep the guard parent and editor intact until the coordinator decides.
            print('Holding resource admission: staged asset completion is uncertain. Inspect editor and intervene explicitly.', file=sys.stderr, flush=True)
            hold = threading.Event()
            while not hold.wait(30):
                print('Still holding admission; staged asset completion has not been confirmed.', file=sys.stderr, flush=True)
        return 1
    finally:
        for path, expected in reversed(owned_exchange):
            try:
                if path.is_file() and digest(bounded(path)) == expected:
                    path.unlink()
            except (OSError, ValueError):
                pass
        if session is not None:
            session.close()


if __name__ == '__main__':
    sys.exit(main())
