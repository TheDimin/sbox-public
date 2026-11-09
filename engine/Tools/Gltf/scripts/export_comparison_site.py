#!/usr/bin/env python3
"""Export a sanitized, evidence-backed snapshot for the glTF comparison Site.

No engine is run. A capture is associated with a corpus variant only through an
explicit receipt/source-hash mapping. Missing or historical proof is never a
current conformance pass. Upstream thumbnails are visual context, not pixel oracles.
"""
from __future__ import annotations
import argparse
from collections import Counter
from datetime import datetime, timezone
import hashlib
import json
import math
from pathlib import Path
import re
import shutil
import struct
from urllib.parse import quote
from uuid import UUID

PIN = '90d7ede14c7e280af263824604b427a1ca02cb66'
CURRENT_ENGINE = '802a970d-7ed4-4341-aa32-b9261e93528a'
CURRENT_GLTF = '72ebb3cb-bbba-4bac-9f2e-9a75e931f854'
BASE = 'https://github.com/KhronosGroup/glTF-Sample-Assets'
RAW = 'https://raw.githubusercontent.com/KhronosGroup/glTF-Sample-Assets/' + PIN + '/'
DEFAULT_UNIT_RECEIPT = 'editor-gltf-unit-tests/88f170b047bb4c4d80ee920b19e6e635/receipt.json'
# An explicit reviewed mapping, rather than discovery of whichever run is newest.
REVIEWED_UNIT_RECEIPTS = {
    DEFAULT_UNIT_RECEIPT: ('208b21850dc4cf89f97f057f93b0e3ab72f3d45bb68a4bb68229371018f1a89c', 732, 701, 31, 0, 0),
}
FORMS = ('original-source', 'derived-packaging-glb_c')


def sha(path):
    h = hashlib.sha256()
    with path.open('rb') as stream:
        for part in iter(lambda: stream.read(65536), b''):
            h.update(part)
    return h.hexdigest()


def require(value, message):
    if not value:
        raise ValueError(message)


def read(path):
    require(path.is_file() and path.stat().st_size <= 32 * 1024 * 1024, 'Missing/oversized input: ' + str(path))
    return json.loads(path.read_text(encoding='utf-8'))


def timestamp(path):
    return datetime.fromtimestamp(path.stat().st_mtime, timezone.utc).isoformat()


def digest(value, description):
    require(isinstance(value, str) and re.fullmatch(r'[0-9a-f]{64}', value), 'Invalid SHA256: ' + description)
    return value


def mvid(value, description):
    try:
        require(isinstance(value, str) and str(UUID(value)) == value.lower() and UUID(value).int != 0,
                'Invalid module identity: ' + description)
    except (ValueError, AttributeError, TypeError):
        raise ValueError('Invalid module identity: ' + description) from None
    return value.lower()


def stable_id(*values):
    return hashlib.sha256(json.dumps(values, ensure_ascii=False, separators=(',', ':')).encode()).hexdigest()[:24]


def compact_error(value):
    # Exception messages are useful; engine/user source paths and stacks are not.
    lines = str(value or '').splitlines()
    text = lines[0] if lines else ''
    text = re.sub(r'^(?:Sandbox\.Gltf\.GltfException|System\.[A-Za-z]+Exception):\s*', '', text)
    text = re.sub(r'(?:/(?:home|Users|tmp|run|var|opt|mnt|media)/[^\s,;]+|[A-Za-z]:[\\/][^\s,;]+)', '[local path]', text)
    text = re.sub(r'\b(?:Survive|ValArk)\b', '[project]', text, flags=re.IGNORECASE)
    return text[:400]


def apply_execution_metadata(case, record, run, run_hash, record_hash, recorded_at, historical):
    """Preserve dispatch/admission state without inventing a load observation."""
    status = record.get('status')
    require(status in {'not_dispatched', 'excluded_required_extension', 'inventory_error', 'not_run',
                       'resource_admission_pending', 'passed', 'failed_or_unverified', 'resource_failure',
                       'not_completed', 'failed'}, 'Unknown corpus execution status')
    pending = status == 'resource_admission_pending'
    if pending:
        require(record.get('classification') == 'applicable' and record.get('forms') == [] and
                case['status'] != 'out-of-scope', 'Resource-pending assets must remain applicable and unattempted')
    estimate = record.get('estimated_peak_bytes')
    if estimate is not None:
        require(type(estimate) is int and estimate > 0, 'Invalid recorded resource estimate')
    observation = {
        'id': 'execution-' + stable_id(case['id'], run_hash, record_hash), 'status': status,
        'reason': compact_error(record.get('reason')) or None, 'receiptHash': record_hash,
        'runReceiptHash': run_hash, 'recordedAt': recorded_at, 'historical': historical,
        'engineBuild': run['engine_mvid'], 'gltfBuild': run['gltf_mvid'],
    }
    if estimate is not None:
        observation['estimatedPeakBytes'] = estimate
        observation['estimateIsHardCap'] = False
    case.setdefault('executionHistory', []).append(observation)
    if historical and case.get('executionHistorical') is False:
        return
    case.update(executionStatus=status, executionReason=observation['reason'], executionHistorical=historical,
                executionReceiptHash=record_hash, executionRunReceiptHash=run_hash, executionRecordedAt=recorded_at)
    if estimate is not None:
        case.update(estimatedPeakBytes=estimate, estimateIsHardCap=False)
    gate_index = next((index for index, gate in enumerate(case['gates']) if gate['name'] == 'Resource admission'), None)
    if pending:
        gate = {'name': 'Resource admission', 'status': 'unverified',
                'detail': observation['reason'] or 'Applicable asset awaits a separately budgeted run; no load was attempted in this dispatch.'}
    elif gate_index is not None and record.get('forms'):
        gate = {'name': 'Resource admission', 'status': 'passed',
                'detail': 'A later run admitted this asset for actual source/compiled attempts; inspect their separate load and capture results.'}
    else:
        return
    if gate_index is None:
        case['gates'].append(gate)
    else:
        case['gates'][gate_index] = gate


def resource_pending_count(cases):
    return sum(case.get('executionStatus') == 'resource_admission_pending' and
               case.get('executionHistorical') is False for case in cases)


def source_inventory(test_root):
    methods = rows = cases = classes = 0
    for path in sorted(test_root.glob('*.cs')):
        pending_test = False
        pending_rows = 0
        file_methods = 0
        for line in path.read_text(encoding='utf-8-sig').splitlines():
            if re.match(r'\s*\[(?:Data)?TestMethod(?:\(|\])', line):
                pending_test = True
            if re.match(r'\s*\[DataRow\(', line):
                pending_rows += 1
            if re.match(r'\s*public\s+(?:async\s+)?(?:void|Task)\s+\w+\s*\(', line):
                if pending_test:
                    file_methods += 1
                    methods += 1
                    rows += pending_rows
                    cases += max(1, pending_rows)
                pending_rows = 0
                pending_test = False
        classes += bool(file_methods)
    return {'classes': classes, 'methods': methods, 'dataRows': rows, 'cases': cases,
            'scope': 'Source inventory only; authored cases are not executed passes.'}


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--checkout', type=Path, default=Path(__file__).resolve().parents[4])
    parser.add_argument('--output', type=Path)
    parser.add_argument('--corpus-receipt', type=Path, action='append', default=[],
                        help='Reviewed editor_corpus.csx run receipt; repeat to include additional runs. Newer current runs supersede older loads.')
    parser.add_argument('--current-engine-mvid', default=CURRENT_ENGINE)
    parser.add_argument('--current-gltf-mvid', default=CURRENT_GLTF)
    parser.add_argument('--unit-receipt', type=Path,
                        help='Explicit unit run receipt. Defaults to the reviewed 732-case editor run.')
    parser.add_argument('--unit-receipt-sha256',
                        help='Reviewed hash required for a unit receipt outside the built-in reviewed mapping.')
    parser.add_argument('--sampling-receipt', type=Path,
                        help='Reviewed 20-case Model.Load sampling diagnostic receipt; requires a current corpus run for renderer identity.')
    parser.add_argument('--sampling-receipt-sha256',
                        help='Explicit reviewed SHA256 of the sampling diagnostic receipt.')
    args = parser.parse_args(argv)
    require(bool(args.sampling_receipt) == bool(args.sampling_receipt_sha256),
            'Sampling receipt and reviewed SHA256 must be supplied together')
    current_engine = mvid(args.current_engine_mvid, 'current engine')
    current_gltf = mvid(args.current_gltf_mvid, 'current glTF')
    root = args.checkout.resolve(strict=True)
    source = root / 'research/gltf-validation'
    validation = (root / 'Temp/gltf-validation').resolve()
    output = (args.output or validation / 'comparison-site/dist').resolve()
    output.mkdir(parents=True, exist_ok=True)
    assets = output / 'assets'; assets.mkdir(exist_ok=True)
    manifest_path = source / 'corpus-case-manifest.json'
    manifest = read(manifest_path)
    require(manifest['corpus']['commit'] == PIN and len(manifest['cases']) == 337, 'Unexpected pinned inventory')
    upstream = root / 'Temp/gltf-reference/glTF-Sample-Assets'
    model_index = read(upstream / 'Models/model-index.json')
    by_model = {item['name']: item for item in model_index}
    inputs = {'corpus-case-manifest.json': sha(manifest_path)}
    snapshot_inputs = [manifest_path]
    evidence = []
    corpus_runs = []
    cases = []
    by_id = {}
    manifest_by_id = {row['id']: row for row in manifest['cases']}
    require(len(manifest_by_id) == len(manifest['cases']), 'Duplicate corpus case identity')
    for row in manifest['cases']:
        excluded = row['classification'] == 'out_of_scope_required_extension'
        item = {
            'id': row['id'], 'modelId': row['model'], 'variant': row['variant'], 'sourceForm': row['source_form'],
            'path': row['path'], 'sourceHash': manifest['files'][row['path']]['sha256'],
            'githubUrl': BASE + '/blob/' + PIN + '/' + quote(row['path'], safe='/'),
            'status': 'out-of-scope' if excluded else 'not-run', 'loadStatus': 'not-run', 'visualStatus': 'not-run',
            'extensions': row.get('extensions_used', []), 'requiredExtensions': row.get('extensions_required', []),
            'optionalFallbacks': row.get('unsupported_optional_extensions', []),
            'excludedExtensions': row.get('unsupported_required_extensions', []), 'error': None,
            'gates': [
                {'name': 'Load', 'status': 'not-run', 'detail': 'No actual Model.Load result recorded for this exact variant.'},
                {'name': 'Rendered appearance', 'status': 'not-run', 'detail': 'No current complete appearance acceptance.'},
                {'name': 'Full sample parity', 'status': 'unverified', 'detail': 'Complete geometry, material, animation, rendering and lifecycle acceptance is still required for this variant.'},
            ], 'evidenceIds': [],
        }
        if excluded:
            item['gates'].insert(0, {'name': 'Scope', 'status': 'out-of-scope', 'detail': 'Required extensions outside the agreed subset: ' + ', '.join(item['excludedExtensions'])})
        cases.append(item); by_id[item['id']] = item

    def input_path(value):
        path = Path(value)
        if not path.is_absolute():
            path = root / path if (root / path).is_file() else validation / path
        path = path.resolve(strict=True)
        require(path.is_relative_to(validation), 'Receipt must be inside the glTF validation evidence directory')
        return path

    def receipt(relative):
        path = input_path(relative)
        value = read(path)
        inputs[path.relative_to(validation).as_posix()] = sha(path)
        snapshot_inputs.append(path)
        return value, path

    def matched(case_id, source_hash):
        case = by_id[case_id]
        require(case['sourceHash'] == source_hash, 'Receipt source hash does not match pinned variant: ' + case_id)
        return case

    def load_result(case_id, source_hash, passed, value, path, detail, error=None):
        case = matched(case_id, source_hash)
        historical = value.get('engine_mvid') != current_engine or value.get('gltf_mvid') != current_gltf
        case.update(loadStatus='passed' if passed else 'failed', status='unverified' if passed else 'failed', error=compact_error(error) or None,
                    loadBuild=value.get('engine_mvid'), loadGltfBuild=value.get('gltf_mvid'), loadReceiptHash=sha(path),
                    loadRecordedAt=timestamp(path), loadHistorical=historical)
        gate = next(index for index, row in enumerate(case['gates']) if row['name'] == 'Load')
        case['gates'][gate] = {'name': 'Load', 'status': 'passed' if passed else 'failed', 'detail': detail if passed else compact_error(error) or detail}
        return case

    def capture(case_id, value, path, capture_row, title, kind, scope, notes, status='unverified', source_hash=None, metadata=None):
        case = matched(case_id, source_hash or value['source_sha256'])
        file = Path(capture_row['path']).resolve(strict=True)
        require(file.is_relative_to(validation) and file.suffix.lower() == '.png', 'Capture is outside glTF evidence')
        image_hash = sha(file)
        require(image_hash == capture_row['sha256'] and file.stat().st_size < 8 * 1024 * 1024, 'Capture hash/size differs')
        destination = assets / (image_hash + '.png')
        if not destination.exists(): shutil.copy2(file, destination)
        require(sha(destination) == image_hash, 'Published capture copy differs')
        historical = value.get('engine_mvid') != current_engine or value.get('gltf_mvid') != current_gltf
        item = {'id': 'evidence-' + stable_id(case_id, sha(path), capture_row.get('label', capture_row.get('name')), image_hash, kind),
                'caseId': case_id, 'modelId': case['modelId'], 'title': title,
                'image': 'assets/' + destination.name, 'kind': kind, 'scope': scope,
                'engineBuild': value.get('engine_mvid'), 'gltfBuild': value.get('gltf_mvid'),
                'capturedAt': timestamp(path), 'capturedAtBasis': 'Observed receipt file modification time (UTC)',
                'comparisonEligible': False, 'historical': historical, 'notes': notes, 'status': status,
                'receiptHash': sha(path), 'sourceHash': case['sourceHash'], 'imageHash': image_hash,
                'comparisonNote': 'The upstream preview uses a different, unrecorded camera/lighting setup; no automatic pixel-parity claim.'}
        if metadata:
            item.update(metadata)
        evidence.append(item); case['evidenceIds'].append(item['id'])
        return item

    # Current actual loader: only exact unmodified pinned Triangle forms are mapped.
    value, path = receipt('editor-model-load/83de5450e1c24864814b2d33640e2510/receipt.json')
    for name, case_id, key in [
        ('khronos-triangle', 'Triangle/glTF', 'khronos-triangle.gltf'),
        ('khronos-triangle-embedded', 'Triangle/glTF-Embedded', 'khronos-triangle-embedded.gltf')]:
        results = [r for r in value['cases'] if r.get('name') == name]
        require(results and all(r.get('passed') is True for r in results), 'Expected recorded Triangle loader assertions')
        load_result(case_id, value['fixture_sha256'][key], True, value, path,
                    'Actual Model.Load/LoadAsync, geometry/material metadata and repeated/equivalent-path cache checks passed. Rendering was not exercised in this run.')

    # Each Box source form was actually loaded and rejected. The derived compiled
    # fixture is reported in the diagnostic summary, never as a GitHub variant.
    value, path = receipt('editor-model-texture-load/a9ed9e3ba8a64f0c953d701bf7d5add5/receipt.json')
    for name, case_id, key in [
        ('source-gltf', 'BoxTextured/glTF', 'source_sha256'),
        ('original-embedded-gltf', 'BoxTextured/glTF-Embedded', 'embedded_sha256'),
        ('original-binary-glb', 'BoxTextured/glTF-Binary', 'binary_sha256')]:
        result = next(r for r in value['cases'] if r['name'] == name)
        load_result(case_id, value[key], result['passed'], value, path, 'Actual public loader result.', result.get('error'))
    value, path = receipt('editor-animation-playback/71cf76e4b3f34d12b1670e18e7db3bc7/receipt.json')
    result = value['gltf_animation_model_load']
    load_result('AnimatedTriangle/glTF-Embedded', result['source_sha256'], result['valid_model'], value, path,
                'Actual animated sample Model.Load result.', result.get('error'))

    # Historical captures are genuine loaded-model renders, clearly kept separate
    # from the current loader check and from the current direct-builder probe.
    for folder, case_id, key, name in [
        ('475cd3bf0b3641cba6aa0ae7c66ccccc', 'Triangle/glTF', 'khronos-triangle.gltf', 'khronos-triangle'),
        ('c6e93595558d4ab980504c7f31817dde', 'Triangle/glTF-Embedded', 'khronos-triangle-embedded.gltf', 'khronos-triangle-embedded')]:
        value, path = receipt('editor-model-load-render-managed/' + folder + '/receipt.json')
        require(value['passed'] is True and not value['cleanup_errors'], 'Historical loaded render is incomplete')
        for label, title in [('loaded-albedo', 'Loaded model · albedo (earlier build)'), ('loaded-world-normal', 'Loaded model · world normals (earlier build)'), ('loaded-front-specular', 'Loaded model · front-light specular (earlier build)')]:
            row = next(r for r in value['captures'] if r['name'] == name and r['label'] == label)
            capture(case_id, value, path, row, title, 'actual-loader-render',
                    'Actual Model.Load/LoadAsync and stock-material reference rendering in the editor; narrow static geometry/material proof only.',
                    'All recorded stock-reference comparisons and controls passed on the earlier build. This is not current full glTF or Khronos appearance acceptance.',
                    'historical-pass', value['fixture_sha256'][key])
        by_id[case_id]['visualStatus'] = 'unverified'
        by_id[case_id]['gates'][1] = {'name': 'Rendered appearance', 'status': 'unverified', 'detail': 'A narrow loaded-model render passed on an earlier build. Current complete appearance acceptance is still pending.'}

    value, path = receipt('editor-render/receipt.json')
    require(value['passed'] is False, 'Reviewed failed geometry-probe receipt changed')
    by_id['Triangle/glTF']['gates'].append({
        'name': 'Historical helper diagnostics', 'status': 'failed',
        'detail': 'Direct-builder probe bypassed Model.Load and failed its empty-frame control. Its images are withheld; it does not determine the actual loader outcome.'})

    value, path = receipt('editor-texture/receipt.json')
    require(value['passed'] is True, 'Texture helper receipt is not a passing scoped capability proof')
    for name, title in [('source', 'Texture helper · source'), ('compiled', 'Texture helper · packaged GLB')]:
        row = next(r for r in value['captures'] if r['name'] == name)
        capture('BoxTextured/glTF', value, path, row, title, 'native-texture-probe',
                'Historical direct-builder stock-albedo helper; this bypasses Model.Load and covers only opaque base color.',
                'The helper rendered and its base-level texels matched. A separate recorded BoxTextured Model.Load run rejected this model. PBR, sampler, mip and full material acceptance are unproven.', 'historical-capability-pass')
        by_id['BoxTextured/glTF']['visualStatus'] = 'unverified'

    # Only explicitly supplied, reviewed corpus runs are imported. The case and
    # standalone form receipts must agree, and each original dependency must be
    # exactly the pinned input. Derived packaging is never an upstream variant.
    verified_upstream = set()
    corpus_inputs = [input_path(path) for path in args.corpus_receipt]
    require(len(corpus_inputs) == len(set(corpus_inputs)), 'Duplicate corpus receipt input')
    corpus_inputs.sort(key=lambda path: (
        read(path).get('engine_mvid') == current_engine and read(path).get('gltf_mvid') == current_gltf,
        path.stat().st_mtime_ns, sha(path)))

    def replace_gate(case, name, status, detail):
        row = {'name': name, 'status': status.replace('_', '-'), 'detail': compact_error(detail)}
        existing = next((index for index, item in enumerate(case['gates']) if item['name'] == name), None)
        if existing is None:
            case['gates'].append(row)
        else:
            case['gates'][existing] = row

    def module_contract(value, description):
        engine = mvid(value.get('engine_mvid'), description + ' engine')
        gltf = mvid(value.get('gltf_mvid'), description + ' glTF')
        require(value.get('engine_disk_mvid') == engine and value.get('gltf_disk_mvid') == gltf,
                'Loaded/disk engine or glTF module mismatch: ' + description)
        digest(value.get('engine_binary_sha256'), description + ' engine binary')
        digest(value.get('gltf_binary_sha256'), description + ' glTF binary')
        renderer = value.get('capture_renderer')
        if renderer is not None:
            require(renderer in ('native', 'managed'), 'Unknown capture renderer')
            renderer_mvid = mvid(value.get('renderer_mvid'), description + ' renderer')
            require(value.get('renderer_disk_mvid') == renderer_mvid, 'Loaded/disk renderer module mismatch')
            digest(value.get('renderer_binary_sha256'), description + ' renderer binary')
        return engine, gltf

    for run_path in corpus_inputs:
        run, run_path = receipt(run_path)
        run_hash = sha(run_path)
        run_id = 'run-' + stable_id(run_hash)
        require(run.get('manifest_sha256') == inputs['corpus-case-manifest.json'], 'Corpus run targets another manifest')
        require(run.get('expected_inventory_cases') == len(cases) and run.get('is_editor') is True,
                'Not a reviewed editor corpus inventory run')
        engine_mvid, gltf_mvid = module_contract(run, run_id)
        runner_hash = digest(run.get('runner_source_sha256'), 'corpus runner source')
        run_inputs, inputs_path = receipt(run_path.parent / 'inputs.json')
        require(run_inputs.get('pin') == PIN and run_inputs.get('manifest_sha256') == inputs['corpus-case-manifest.json'],
                'Corpus run pin/manifest input mismatch')
        require(run_inputs.get('runner_source_sha256') == runner_hash and
                run_inputs.get('request_sha256') == run.get('request_sha256'), 'Corpus run request/source mismatch')
        selected = run_inputs.get('selected_ids')
        require(isinstance(selected, list) and len(selected) == len(set(selected)) and set(selected) <= set(by_id),
                'Invalid or duplicate corpus selection IDs')
        summaries = run.get('cases')
        require(isinstance(summaries, list) and len(summaries) == run.get('completed_case_receipts') and
                len(summaries) == len({row.get('id') for row in summaries}) and
                {row.get('id') for row in summaries} <= set(selected) and run.get('selected_cases') == len(selected),
                'Invalid corpus case receipt inventory')
        historical = engine_mvid != current_engine or gltf_mvid != current_gltf
        loaded_count = captured_count = actual_failed_count = 0
        run_form_ids = []
        for summary in summaries:
            case_id = summary['id']
            row = manifest_by_id[case_id]
            case = by_id[case_id]
            key = hashlib.sha256(case_id.encode()).hexdigest()[:20]
            expected_case_path = (run_path.parent / 'cases' / key / 'receipt.json').resolve(strict=True)
            require(input_path(summary['receipt_path']) == expected_case_path, 'Corpus case receipt path/identity mismatch')
            record, case_path = receipt(expected_case_path)
            require(record.get('id') == case_id and record.get('corpus_commit') == PIN and
                    record.get('manifest_sha256') == inputs['corpus-case-manifest.json'] and
                    record.get('runner_source_sha256') == runner_hash and record.get('source_path') == row['path'] and
                    record.get('classification') == row['classification'] == summary.get('classification') and
                    record.get('status') == summary.get('status'),
                    'Corpus case identity/pin/manifest mismatch: ' + case_id)
            expected_files = {name: manifest['files'][name] for name in [row['path'], *row['dependencies']]}
            require(record.get('source_file_manifest') == expected_files, 'Corpus source/dependency manifest differs: ' + case_id)
            forms = record.get('forms')
            require(isinstance(forms, list) and len(forms) == len({form.get('form') for form in forms}) and
                    all(form.get('form') in row['required_forms'] for form in forms), 'Invalid/duplicate corpus forms: ' + case_id)
            if 'estimated_peak_bytes' in record:
                require(record['estimated_peak_bytes'] == row['estimated_peak_bytes'], 'Resource estimate differs from pinned manifest')
            apply_execution_metadata(case, record, run, run_hash, sha(case_path), timestamp(case_path), historical)
            # Pending/excluded rows have no engine attempt. Their recorded input
            # hashes must match the manifest, but there is no captured result that
            # would justify rereading their potentially large source dependencies.
            for relative, expected_file in (expected_files.items() if forms else []):
                if relative in verified_upstream:
                    continue
                upstream_file = (upstream / relative).resolve(strict=True)
                require(upstream_file.is_relative_to(upstream.resolve()) and upstream_file.is_file() and
                        upstream_file.stat().st_size == expected_file['bytes'] and sha(upstream_file) == expected_file['sha256'],
                        'Pinned source/dependency bytes differ: ' + relative)
                verified_upstream.add(relative)
            for form in forms:
                form_name = form['form']
                require(form_name in FORMS, 'Unknown corpus form')
                standalone, form_path = receipt(case_path.parent / (form_name + '.json'))
                require(standalone == form, 'Case and standalone form receipts differ: ' + case_id + '/' + form_name)
                form_hash = sha(form_path)
                form_id = 'form-' + stable_id(case_id, form_name, run_hash)
                run_form_ids.append(form_id)
                for field, expected in [('engine_mvid', engine_mvid), ('gltf_mvid', gltf_mvid),
                                        ('capture_renderer', run.get('capture_renderer')),
                                        ('renderer_mvid', run.get('renderer_mvid')),
                                        ('renderer_sha256', run.get('renderer_binary_sha256'))]:
                    if field in form:
                        require(form[field] == expected, 'Form/run module identity mismatch: ' + case_id)
                load = form.get('load', {})
                load_status = load.get('status')
                require(load_status in ('passed', 'failed', 'not_run'), 'Unknown actual loader result')
                if load_status == 'passed':
                    require(form.get('model_loaded') is True, 'Passing load has no valid actual model')
                    loaded_count += 1
                elif load_status == 'failed':
                    require(form.get('model_loaded') is not True, 'Failed load contradicts valid-model flag')
                    actual_failed_count += 1
                source_hash = case['sourceHash']
                artifact_hash = form.get('compiled_fixture_sha256') if form_name == FORMS[1] else source_hash
                if load_status != 'not_run':
                    digest(artifact_hash, 'loaded artifact ' + case_id + '/' + form_name)
                title = 'Source' if form_name == FORMS[0] else 'Packaging derivative (.glb_c)'
                capture_gate = form.get('render_capture', {})
                capture_status = capture_gate.get('status')
                require(capture_status in ('not_run', 'captured', 'failed', 'unverified'), 'Unknown corpus capture status')
                observed = capture_gate.get('observation') or {}
                if observed:
                    require(observed.get('source_id') == case_id and observed.get('source_path') == row['path'] and
                            observed.get('source_sha256') == source_hash and observed.get('form') == form_name,
                            'Capture source/form identity mismatch')
                    require(observed.get('engine_mvid') == engine_mvid and observed.get('gltf_mvid') == gltf_mvid,
                            'Capture/run engine or glTF identity mismatch')
                form_evidence = []
                if capture_status == 'captured':
                    require(load_status == 'passed' and observed.get('controls_passed') is True and
                            not observed.get('failure') and not observed.get('cleanup_errors') and not form.get('cleanup_errors'),
                            'Captured model lacks passing visibility/cleanup controls')
                    require(all(observed.get(field) == 0 for field in
                                ('empty_before_nonblack', 'empty_after_sphere_nonblack', 'empty_after_nonblack')) and
                            isinstance(observed.get('reference_coverage'), int) and observed['reference_coverage'] > 50 and
                            isinstance(observed.get('asset_nonblack_pixels'), int) and observed['asset_nonblack_pixels'] > 0,
                            'Captured model visibility controls contradict status')
                    require(run.get('capture_renderer') in ('native', 'managed'), 'Captured model has no renderer provenance')
                    for field, expected in [('capture_renderer', run['capture_renderer']),
                                            ('renderer_mvid', run['renderer_mvid']),
                                            ('renderer_sha256', run['renderer_binary_sha256'])]:
                        require(observed.get(field) == expected and form.get(field) == expected,
                                'Capture/form/run renderer identity mismatch')
                    paths = observed.get('paths')
                    require(isinstance(paths, list) and len(paths) == len({item.get('label') for item in paths}),
                            'Duplicate capture labels')
                    by_label = {item['label']: item for item in paths}
                    require({'albedo', 'lit'} <= set(by_label), 'Captured model lacks source/compiled albedo and lit views')
                    for label in ('albedo', 'lit'):
                        image_row = by_label[label]
                        for field in ('capture_renderer', 'renderer_mvid', 'renderer_sha256'):
                            require(image_row.get(field) == observed[field], 'Capture image renderer identity mismatch')
                        image_path = Path(image_row['path']).resolve(strict=True)
                        require(image_path.is_relative_to(form_path.parent / form_name / 'captures'),
                                'Image is not in its source form capture directory')
                        require(image_row.get('fresh_render_target') is True and
                                image_row.get('debug_mode') == ('Albedo' if label == 'albedo' else 'Normal'),
                                'Capture image view/target differs from recorded contract')
                        with image_path.open('rb') as stream:
                            png_header = stream.read(24)
                        require(len(png_header) == 24 and png_header[:8] == b'\x89PNG\r\n\x1a\n' and
                                png_header[12:16] == b'IHDR' and
                                struct.unpack('>II', png_header[16:24]) == (image_row.get('width'), image_row.get('height')),
                                'Capture PNG dimensions/signature differ')
                        image_evidence = capture(case_id, run, form_path, image_row,
                            title + ' · ' + label + (' (earlier build)' if historical else ''), 'actual-loader-render',
                            'Actual public Model.Load; ' + title.lower() + ', ' + run['capture_renderer'] + ' renderer. Fresh-target empty/sphere controls passed.',
                            'Visibility capture only. The upstream preview has no matched camera/light/exposure; complete material, animation and appearance parity remains unverified.',
                            'historical-captured' if historical else 'captured', source_hash,
                            {'artifactForm': form_name, 'sourceForm': row['source_form'], 'sourceVariant': row['variant'], 'captureLabel': label,
                             'artifactHash': artifact_hash, 'renderer': run['capture_renderer'],
                             'rendererBuild': run['renderer_mvid'], 'rendererBinaryHash': run['renderer_binary_sha256'],
                             'engineBinaryHash': run['engine_binary_sha256'], 'gltfBinaryHash': run['gltf_binary_sha256'],
                             'runReceiptHash': run_hash, 'caseReceiptHash': sha(case_path), 'formReceiptHash': form_hash,
                             'runnerSourceHash': runner_hash, 'formId': form_id, 'controlsPassed': True,
                             'width': image_row['width'], 'height': image_row['height']})
                        form_evidence.append(image_evidence['id'])
                    captured_count += 1
                form_result = {
                    'id': form_id, 'runId': run_id, 'form': form_name, 'sourceForm': row['source_form'], 'sourceVariant': row['variant'],
                    'sourceHash': source_hash, 'artifactHash': artifact_hash, 'isUpstreamOriginal': form_name == FORMS[0],
                    'loadStatus': load_status.replace('_', '-'), 'captureStatus': capture_status.replace('_', '-'),
                    'visualStatus': 'unverified', 'engineBuild': engine_mvid, 'gltfBuild': gltf_mvid,
                    'moduleIdentityScope': 'form and run' if 'engine_mvid' in form and 'gltf_mvid' in form else 'run receipt; form source identity verified',
                    'renderer': run.get('capture_renderer'), 'rendererBuild': run.get('renderer_mvid'),
                    'rendererBinaryHash': run.get('renderer_binary_sha256'),
                    'engineBinaryHash': run['engine_binary_sha256'], 'gltfBinaryHash': run['gltf_binary_sha256'],
                    'historical': historical, 'recordedAt': timestamp(form_path), 'receiptHash': form_hash,
                    'runReceiptHash': run_hash, 'caseReceiptHash': sha(case_path), 'runnerSourceHash': runner_hash,
                    'sourceDependencyHashes': {name: meta['sha256'] for name, meta in expected_files.items()},
                    'error': compact_error(form.get('error')) or None, 'evidenceIds': form_evidence,
                    'gates': [{'name': name, 'status': form.get(name, {}).get('status', 'unverified').replace('_', '-'),
                               'detail': compact_error(form.get(name, {}).get('reason'))}
                              for name in ('load', 'geometry', 'materials', 'render_capture', 'animation', 'render_appearance')],
                }
                case.setdefault('formResults', []).append(form_result)
                # Current source loads stay distinct from packaging-derived loads.
                # A supplied historical run cannot displace a current observation.
                can_update = not historical or case.get('loadHistorical', True)
                if can_update and case['status'] != 'out-of-scope':
                    if form_name == FORMS[0] and load_status != 'not_run':
                        load_result(case_id, source_hash, load_status == 'passed', run, form_path,
                                    'Actual Model.Load of the pinned upstream original. Packaging-derived loads are reported separately.',
                                    form.get('error') if load_status == 'failed' else None)
                        case['loadFormId'] = form_id
                    if form_name == FORMS[1]:
                        case['compiledLoadStatus'] = load_status.replace('_', '-')
                        case['compiledLoadHistorical'] = historical
                        case['compiledLoadBuild'] = engine_mvid
                        case['compiledLoadGltfBuild'] = gltf_mvid
                        case['compiledLoadFormId'] = form_id
                        if load_status == 'failed':
                            case['status'] = 'failed'
                    replace_gate(case, title + ' load', load_status,
                                 form.get('error') if load_status == 'failed' else load.get('reason'))
                    replace_gate(case, title + ' capture', capture_status,
                                 capture_gate.get('reason') if capture_status != 'captured' else
                                 'Actual loaded model visibility captured with passing controls; full appearance remains unverified.')
                    if capture_status == 'captured':
                        case['visualStatus'] = 'unverified'
                        replace_gate(case, 'Rendered appearance', 'unverified',
                                     'Actual loaded source/packaging images exist; camera/light/exposure differs from the upstream preview. Complete appearance acceptance is pending.')
        require(loaded_count == run.get('loaded_forms') and captured_count == run.get('captured_forms'),
                'Run totals contradict actual per-form load/capture receipts')
        corpus_runs.append({'id': run_id, 'receiptHash': run_hash, 'manifestHash': inputs['corpus-case-manifest.json'],
                            'runnerSourceHash': runner_hash, 'requestHash': run.get('request_sha256'),
                            'pin': PIN, 'selectedCases': len(selected), 'completedCases': len(summaries),
                            'loadedForms': loaded_count, 'failedLoadForms': actual_failed_count, 'capturedForms': captured_count,
                            'resourceAdmissionPending': sum(item.get('status') == 'resource_admission_pending' for item in summaries),
                            'engineBuild': engine_mvid, 'gltfBuild': gltf_mvid, 'renderer': run.get('capture_renderer'),
                            'rendererBuild': run.get('renderer_mvid'), 'historical': historical,
                            'recordedAt': timestamp(run_path), 'formIds': run_form_ids,
                            'scope': 'Actual original/packaging-derived Model.Load and qualified visibility observations; no full sample-conformance pass.'})

    def evidence_order(item):
        # Prefer actual controlled current pixels without treating any capture as
        # a matched reference-image pass. Hash identity breaks ties deterministically.
        tier = 0 if not item['historical'] and item.get('controlsPassed') is True and item['kind'] == 'actual-loader-render' else 1 if not item['historical'] else 2
        form_order = 0 if item.get('artifactForm') == FORMS[0] else 1
        label_order = 0 if item.get('captureLabel') == 'albedo' else 1
        return tier, form_order, label_order, item['caseId'], -datetime.fromisoformat(item['capturedAt']).timestamp(), item['id']

    evidence.sort(key=evidence_order)
    evidence_by_id = {item['id']: item for item in evidence}
    for case in cases:
        case['evidenceIds'].sort(key=lambda key: evidence_order(evidence_by_id[key]))

    validation_checks = []
    current_limitations = []
    resource_pending = resource_pending_count(cases)
    if resource_pending:
        current_limitations.append(str(resource_pending) + ' applicable larger variants await separately budgeted runs. Resource admission is pending; these assets remain in scope and their unattempted loads are unverified.')
    if args.sampling_receipt:
        sampling, sampling_path = receipt(args.sampling_receipt)
        sampling_hash = sha(sampling_path)
        require(sampling_hash == digest(args.sampling_receipt_sha256, 'reviewed sampling receipt'),
                'Sampling receipt differs from its reviewed SHA256')
        require(sampling.get('phase') == 'complete' and sampling.get('expected_cases') == 20 and
                sampling.get('is_editor') is True and sampling.get('error') is None and sampling.get('cleanup_errors') == [],
                'Sampling run is incomplete or has execution/cleanup errors')
        require(mvid(sampling.get('engine_mvid'), 'sampling engine') == current_engine and
                mvid(sampling.get('gltf_mvid'), 'sampling glTF') == current_gltf,
                'Sampling receipt does not target the declared current modules')
        renderer_mvid = mvid(sampling.get('renderer_mvid'), 'sampling renderer')
        current_renderers = {run['rendererBuild'] for run in corpus_runs if not run['historical'] and run.get('rendererBuild')}
        require(renderer_mvid in current_renderers, 'Sampling renderer is not verified by a current corpus run')
        for field in ('engine_sha256', 'gltf_sha256', 'renderer_sha256', 'source_sha256'):
            digest(sampling.get(field), 'sampling ' + field)
        for field, evidence_field in [('engine_sha256', 'engineBinaryHash'), ('gltf_sha256', 'gltfBinaryHash'),
                                      ('renderer_sha256', 'rendererBinaryHash')]:
            current_hashes = {item[evidence_field] for item in evidence if not item['historical'] and item.get(evidence_field)}
            require(sampling[field] in current_hashes, 'Sampling binary hash differs from current captured modules')
        sampling_rows = sampling.get('cases')
        require(isinstance(sampling_rows, list) and len(sampling_rows) == 20 and
                len({(row.get('name'), row.get('form')) for row in sampling_rows}) == 20,
                'Sampling diagnostic needs 20 unique cases')
        names = {row.get('name') for row in sampling_rows}
        require(len(names) == 10 and all(isinstance(name, str) and re.fullmatch(r'[a-z0-9_]+', name) for name in names) and
                all({row['form'] for row in sampling_rows if row['name'] == name} == {'gltf', 'glb'} for name in names),
                'Sampling diagnostic original glTF/independent GLB pairs differ')
        fixtures = sampling.get('fixture_sha256')
        require(isinstance(fixtures, dict) and fixtures and all(
            re.fullmatch(r'[a-z0-9_]+/(?:gltf/(?:model\.gltf|geometry\.bin|image\.png)|glb/model\.glb)', name)
            for name in fixtures), 'Invalid sampling fixture inventory')
        for name, fixture_hash in fixtures.items():
            digest(fixture_hash, 'sampling fixture ' + name)
        sanitized_rows = []
        for row in sampling_rows:
            require(row.get('load') == 'passed' and row.get('empty_after') == 'passed' and
                    type(row.get('passed')) is bool and row.get('numeric_appearance') in ('passed', 'failed') and
                    row['passed'] == (row['numeric_appearance'] == 'passed'),
                    'Sampling load/numeric/empty-control observations disagree')
            error, tolerance = row.get('max_rgb_error'), row.get('tolerance')
            require(all(type(number) in (int, float) and math.isfinite(number) and number >= 0 for number in (error, tolerance)) and
                    row['passed'] == (error <= tolerance), 'Sampling numeric error contradicts its recorded result')
            for field in ('expected_linear_rgb', 'actual_rgb_mean', 'control_rgb_mean'):
                values = row.get(field)
                require(isinstance(values, list) and len(values) == 3 and
                        all(type(number) in (int, float) and math.isfinite(number) for number in values),
                        'Sampling RGB observation is missing/nonfinite')
            require(abs(error - max(abs(actual - control) for actual, control in
                                    zip(row['actual_rgb_mean'], row['control_rgb_mean']))) <= 1e-6,
                    'Sampling numeric error differs from recorded RGB observations')
            require(all(name + '/' + suffix in fixtures for name, suffix in [
                (row['name'], 'gltf/model.gltf'), (row['name'], 'gltf/geometry.bin'),
                (row['name'], 'gltf/image.png'), (row['name'], 'glb/model.glb')]), 'Sampling fixture pair hash missing')
            sanitized_rows.append({'id': 'sampling-' + stable_id(sampling_hash, row['name'], row['form']),
                                   'name': row['name'], 'form': row['form'], 'feature': compact_error(row.get('feature')),
                                   'loadStatus': 'passed', 'status': row['numeric_appearance'],
                                   'maxRgbError': error, 'tolerance': tolerance, 'emptyControl': 'passed',
                                   'expectedLinearRgb': row['expected_linear_rgb'], 'actualRgbMean': row['actual_rgb_mean'],
                                   'controlRgbMean': row['control_rgb_mean']})
        passed = sum(row['passed'] for row in sampling_rows)
        require(sampling.get('passed') is (passed == 20), 'Sampling overall result contradicts numeric cases')
        failed_features = sorted({row['feature'] for row in sanitized_rows if row['status'] == 'failed'})
        check = {'id': 'model-texture-sampling', 'title': 'Actual Model.Load texture sampling diagnostics',
                 'status': 'passed' if passed == 20 else 'failed', 'passed': passed, 'total': 20, 'failed': 20 - passed,
                 'loadPassed': 20, 'receiptHash': sampling_hash, 'engineBuild': current_engine, 'gltfBuild': current_gltf,
                 'rendererBuild': renderer_mvid, 'engineBinaryHash': sampling['engine_sha256'],
                 'gltfBinaryHash': sampling['gltf_sha256'], 'rendererBinaryHash': sampling['renderer_sha256'],
                 'runnerSourceHash': sampling['source_sha256'], 'recordedAt': timestamp(sampling_path),
                 'historical': False, 'failedFeatures': failed_features,
                 'passedFeatures': sorted({row['feature'] for row in sanitized_rows if row['status'] == 'passed'}),
                 'scope': 'Self-generated CC0 diagnostic fixtures, each loaded as original glTF and independently hand-packed GLB. Numeric U-wrap, magnification, color-transfer/factor and one minified-mip check only; no full core-sampling or corpus appearance pass.',
                 'rootCause': 'Under investigation; these observations do not identify which engine or shader layer causes the failures.',
                 'cases': sanitized_rows, 'fixtureSha256': fixtures}
        validation_checks.append(check)
        if failed_features:
            current_limitations.append('Core texture sampling is incomplete: ' + ', '.join(failed_features) +
                                       ' failed numeric Model.Load checks (' + str(20 - passed) + '/20 cases). Root cause remains under investigation; full appearance parity is unverified.')

    models = []
    for model_id, upstream_row in by_model.items():
        rows = [r for r in manifest['cases'] if r['model'] == model_id]
        legal = rows[0]['provenance']['legal']
        licenses = [{key: row.get(key, '') for key in ('artist', 'owner', 'year', 'what', 'license', 'licenseUrl', 'text', 'spdx')} for row in legal]
        screenshot = upstream_row.get('screenshot')
        models.append({'id': model_id, 'name': upstream_row.get('label', model_id), 'tags': upstream_row.get('tags', []),
                       'referenceImage': RAW + quote('Models/' + model_id + '/' + screenshot, safe='/') if screenshot else None,
                       'referenceScope': 'Upstream preview, not a matched camera/light/exposure oracle.',
                       'githubUrl': BASE + '/tree/' + PIN + '/Models/' + quote(model_id),
                       'readmeUrl': BASE + '/blob/' + PIN + '/Models/' + quote(model_id) + '/README.md',
                       'license': '; '.join(dict.fromkeys(row['license'] for row in licenses if row['license'])),
                       'licenseUrl': next((row['licenseUrl'] for row in licenses if row['licenseUrl']), None),
                       'licenses': licenses, 'cases': [row['id'] for row in rows]})
    models.sort(key=lambda row: row['name'].casefold())
    value, path = receipt(args.unit_receipt or DEFAULT_UNIT_RECEIPT)
    unit_relative = path.relative_to(validation).as_posix()
    reviewed_unit = REVIEWED_UNIT_RECEIPTS.get(unit_relative)
    expected_unit_hash = args.unit_receipt_sha256 or (reviewed_unit[0] if reviewed_unit else None)
    require(expected_unit_hash is not None and sha(path) == digest(expected_unit_hash, 'reviewed unit receipt'),
            'Unit receipt needs an explicit reviewed SHA256')
    unit_counts = tuple(value[field] for field in
                        ('expected_cases', 'passed_cases', 'failed_cases', 'blocked_cases', 'partial_cases'))
    if reviewed_unit and not args.unit_receipt_sha256:
        require(unit_counts == reviewed_unit[1:], 'Reviewed unit totals changed')
    require(value.get('phase') == 'complete' and value.get('selection') == 'all' and value.get('is_editor') is True and
            all(type(count) is int and count >= 0 for count in unit_counts) and
            unit_counts[0] == sum(unit_counts[1:]), 'Incomplete or contradictory unit run')
    test_rows = value.get('tests')
    require(isinstance(test_rows, list) and len(test_rows) == unit_counts[0], 'Unit run row count mismatch')
    unit_histogram = Counter(row.get('status') for row in test_rows)
    require(set(unit_histogram) <= {'passed', 'failed', 'blocked', 'partial'} and
            tuple(unit_histogram[status] for status in ('passed', 'failed', 'blocked', 'partial')) == unit_counts[1:],
            'Unit totals disagree with executed case results')
    identities = [stable_id(row.get('suite'), row.get('name'), row.get('arguments'), row.get('argument_types')) for row in test_rows]
    require(len(identities) == len(set(identities)), 'Duplicate executed unit case identity')
    unit_engine = mvid(value.get('engine_mvid'), 'unit engine')
    unit_gltf = mvid(value.get('gltf_loaded_mvid'), 'unit glTF')
    require(value.get('gltf_disk_mvid') == unit_gltf, 'Unit loaded/disk glTF module mismatch')
    unit_sources = value.get('test_source_sha256')
    require(isinstance(unit_sources, dict) and unit_sources and
            all(Path(name).name == name and name.endswith('.cs') for name in unit_sources), 'Invalid unit source hash inventory')
    for name, source_hash in unit_sources.items():
        digest(source_hash, 'unit source ' + name)
    unit_summary = {'passed': unit_counts[1], 'failed': unit_counts[2], 'total': unit_counts[0],
                    'blocked': unit_counts[3], 'partial': unit_counts[4], 'receiptHash': sha(path),
                    'engineBuild': unit_engine, 'gltfBuild': unit_gltf,
                    'historical': unit_engine != current_engine or unit_gltf != current_gltf,
                    'recordedAt': timestamp(path), 'sourceSha256': unit_sources,
                    'manifestHash': digest(value.get('manifest_sha256'), 'unit manifest'),
                    'scope': 'Executed pure glTF suite in the editor; this does not establish corpus render conformance.'}
    authored = source_inventory(root / 'engine/Tests/Sandbox.Test.Gltf')
    unit_summary['authored'] = authored['cases']
    test_sources = sorted((root / 'engine/Tests/Sandbox.Test.Gltf').glob('*.cs'))
    snapshot_inputs.extend(test_sources)
    snapshot_at = datetime.fromtimestamp(max(item.stat().st_mtime for item in snapshot_inputs), timezone.utc).isoformat()
    current_load_passed = sum(row['loadStatus'] == 'passed' and row.get('loadHistorical') is False for row in cases)
    current_load_failed = sum(row['loadStatus'] == 'failed' and row.get('loadHistorical') is False for row in cases)
    applicable = sum(row['status'] != 'out-of-scope' for row in cases)
    data = {'schemaVersion': 1, 'summary': {
        'models': len(models), 'variants': len(cases), 'applicable': applicable,
        'outOfScope': sum(r['status'] == 'out-of-scope' for r in cases),
        'loadPassed': current_load_passed, 'loadFailed': current_load_failed,
        'notRun': applicable - current_load_passed - current_load_failed,
        'currentLoadPassed': current_load_passed, 'currentLoadFailed': current_load_failed,
        'resourceAdmissionPending': resource_pending,
        'historicalLoadPassed': sum(r['loadStatus'] == 'passed' and r.get('loadHistorical') is True for r in cases),
        'historicalLoadFailed': sum(r['loadStatus'] == 'failed' and r.get('loadHistorical') is True for r in cases),
        'observedLoadPassed': sum(r['loadStatus'] == 'passed' for r in cases),
        'observedLoadFailed': sum(r['loadStatus'] == 'failed' for r in cases),
        'loadCountsScope': 'Headline loadPassed/loadFailed/notRun apply to the declared current engine/glTF modules; historical observations remain on their exact variant rows.',
        'capturedModels': len({r['modelId'] for r in evidence}), 'unitTests': unit_summary,
        'currentCapturedModels': len({r['modelId'] for r in evidence if not r['historical']}),
        'historicalCapturedModels': len({r['modelId'] for r in evidence if r['historical']}),
        'corpusRuns': len(corpus_runs), 'corpusLoadedForms': sum(row['loadedForms'] for row in corpus_runs),
        'corpusCapturedForms': sum(row['capturedForms'] for row in corpus_runs),
        'compiledLoadPassed': sum(row.get('compiledLoadStatus') == 'passed' and row.get('compiledLoadHistorical') is False for row in cases),
        'compiledLoadFailed': sum(row.get('compiledLoadStatus') == 'failed' and row.get('compiledLoadHistorical') is False for row in cases),
        'historicalCompiledLoadPassed': sum(row.get('compiledLoadStatus') == 'passed' and row.get('compiledLoadHistorical') is True for row in cases),
        'historicalCompiledLoadFailed': sum(row.get('compiledLoadStatus') == 'failed' and row.get('compiledLoadHistorical') is True for row in cases),
        'updatedAt': snapshot_at, 'pin': PIN, 'currentEngine': current_engine, 'currentGltf': current_gltf,
        'currentLimitations': current_limitations,
        'notes': 'Headline load counts use the declared current modules; historical results are separate. Loading, controlled visibility captures and full appearance conformance are distinct. Resource-pending assets remain applicable and unverified. Current complete sample parity is unproven.'},
        'models': models, 'cases': cases, 'evidence': evidence, 'corpusRuns': corpus_runs, 'validationChecks': validation_checks,
        'provenance': {'corpusRepository': BASE, 'commit': PIN, 'sourceInventory': authored, 'inputSha256': inputs,
                       'capturePolicy': 'Only explicit glTF receipt PNGs with verified hashes. No gameplay/editor desktop screenshots or generated substitute renders.',
                       'upstreamImagePolicy': 'Pinned upstream preview URLs; each model retains its attribution and license/README links.',
                       'authoredSourceSha256': {item.name: sha(item) for item in test_sources},
                       'exporterSha256': sha(Path(__file__).resolve())}}
    require(len(models) == 150 and len(cases) == 337 and len({r['id'] for r in cases}) == 337, 'Incomplete dataset')
    require(all(row['caseId'] in by_id and not row['comparisonEligible'] for row in evidence), 'Invalid comparison proof mapping')
    require(len({row['id'] for row in evidence}) == len(evidence), 'Duplicate evidence identity')
    form_ids = [form['id'] for case in cases for form in case.get('formResults', [])]
    require(len(form_ids) == len(set(form_ids)), 'Duplicate corpus form identity')
    execution_ids = [item['id'] for case in cases for item in case.get('executionHistory', [])]
    require(len(execution_ids) == len(set(execution_ids)), 'Duplicate corpus execution identity')
    evidence_ids = {row['id'] for row in evidence}
    require(all(set(row['evidenceIds']) <= evidence_ids for row in cases), 'Broken case evidence reference')
    require(all(row['referenceImage'] is None or row['referenceImage'].startswith(RAW) for row in models), 'Unpinned reference preview')
    require(all(row['image'].startswith('assets/') and sha(output / row['image']) == row['imageHash'] for row in evidence), 'Broken exported image')
    require(all(row['status'] != 'passed' for row in cases), 'No current complete sample conformance pass is established')
    serialized = json.dumps(data, ensure_ascii=False, separators=(',', ':'), allow_nan=False) + '\n'
    require(not re.search(r'/(?:home|Users|tmp|run|var|opt|mnt|media)/|[A-Za-z]:\\\\|\b(?:Survive|ValArk)\b|\\n\s+at\s', serialized,
                          flags=re.IGNORECASE), 'Local/private path or stack leaked into site data')
    destination = output / 'data.json'; temporary = output / 'data.json.tmp'; temporary.write_text(serialized, encoding='utf-8'); temporary.replace(destination)
    print(json.dumps({'models': len(models), 'variants': len(cases), 'evidence': len(evidence), 'authoredCases': authored['cases'],
                      'loadResults': dict(Counter(row['loadStatus'] for row in cases)), 'dataSha256': sha(destination), 'output': str(destination)}))


if __name__ == '__main__':
    main()
