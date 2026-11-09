#!/usr/bin/env python3
"""Export only the current Run result and matching recipes to the comparison site.

Automated loading/rendering results never approve visual appearance. Reviews live
in the user's browser/export and are keyed to the complete comparison fingerprint.
"""
from __future__ import annotations
import argparse
from collections import Counter
import hashlib
import importlib.machinery
import importlib.util
import json
from pathlib import Path
import re
import shutil
import tempfile
from urllib.parse import quote
from visual_comparison_identity import comparison_identity, decoded_media

_stock_runner = None


def stock_proof_runner():
    # Share the exact bounded validator with Run; importing it dispatches no jobs.
    global _stock_runner
    if _stock_runner is None:
        loader = importlib.machinery.SourceFileLoader('_gltf_stock_proof', str(Path(__file__).resolve().with_name('Run')))
        spec = importlib.util.spec_from_loader(loader.name, loader)
        _stock_runner = importlib.util.module_from_spec(spec)
        loader.exec_module(_stock_runner)
    return _stock_runner


def read(path):
    return json.loads(Path(path).read_text())


def sha(path):
    h = hashlib.sha256()
    with Path(path).open('rb') as stream:
        for block in iter(lambda: stream.read(65536), b''):
            h.update(block)
    return h.hexdigest()


def fingerprint(value):
    return hashlib.sha256(json.dumps(value, sort_keys=True, separators=(',', ':'), ensure_ascii=False, allow_nan=False).encode()).hexdigest()


def validate_recipe_proofs(run, results, recipes):
    version = run.get('recipeProofVersion')
    if version is not None and (type(version) is not int or version != 1):
        raise ValueError('Unsupported recipe proof version')
    proof_mode = version == 1 or any('recipeSnapshot' in row or 'recipeSha256' in row for row in results.values())
    if not proof_mode:
        return  # Legacy runs still require the exact globally pinned document.
    for case_id, row in results.items():
        snapshot, expected = row.get('recipeSnapshot'), row.get('recipeSha256')
        if not isinstance(snapshot, dict) or snapshot.get('id') != case_id:
            raise ValueError(f'Missing or mismatched recipe snapshot: {case_id}')
        if not isinstance(expected, str) or not re.fullmatch('[0-9a-f]{64}', expected):
            raise ValueError(f'Missing or malformed recipe fingerprint: {case_id}')
        try:
            observed, current = fingerprint(snapshot), fingerprint(recipes[case_id])
        except (TypeError, ValueError) as error:
            raise ValueError(f'Invalid recipe proof: {case_id}') from error
        if observed != expected:
            raise ValueError(f'Recipe snapshot fingerprint differs: {case_id}')
        if observed != current:
            raise ValueError(f'Recipe snapshot differs from current requested recipe: {case_id}')


def validate_stock_resource_proofs(run, results):
    producer = run.get('producer', {})
    proof_mode = 'stockResourceProofVersion' in producer or any('stockResourceProof' in row for row in results.values())
    captures = [row for row in results.values() if row.get('capture') or row.get('render', {}).get('status') == 'passed']
    if not proof_mode or not captures:
        return  # Untouched legacy evidence remains readable, without certification.
    runner = stock_proof_runner()
    proof = runner.capture_stock_proof(producer)
    if not runner.complete_stock_resource_proof(proof) or proof != runner.stock_resource_proof():
        raise ValueError('Stock resource proof is unverified or differs from current on-disk resources')
    for row in captures:
        if row.get('stockResourceProof') != proof:
            raise ValueError(f'Stock resource proof missing or stale on capture: {row["id"]}')


def clean_error(value):
    text = str(value or '').splitlines()[0] if value else ''
    return re.sub(r'(?:/(?:home|Users|tmp|run|var|opt|mnt|media)/[^\s,;]+|[A-Za-z]:[\\/][^\s,;]+)', '[local path]', text)[:600] or None


def unique_rows(rows, label):
    result = {}
    for row in rows:
        if row['id'] in result:
            raise ValueError(f'Duplicate {label} case: {row["id"]}')
        result[row['id']] = row
    return result


def public_setup(value):
    """Keep requested/applied setup, not runtime traces or local diagnostics."""
    if not isinstance(value, dict):
        return None
    result = {k: value[k] for k in ('camera', 'presentation', 'provenance', 'matchStatus', 'limitations', 'complete') if k in value}
    presentation = result.get('presentation')
    if isinstance(presentation, dict) and isinstance(presentation.get('environment'), dict):
        result['presentation'] = dict(presentation)
        result['presentation']['environment'] = {
            k: v for k, v in presentation['environment'].items()
            if k not in {'sourcePath', 'compiledPath'}
        }
    animation = value.get('animation')
    if isinstance(animation, dict):
        result['animation'] = {k: animation[k] for k in ('clip', 'name', 'time', 'rate', 'duration', 'loop', 'referenceDuration', 'timingRelationship', 'mode', 'requestedRate', 'playbackRate') if k in animation}
        recording = animation.get('recording')
        if isinstance(recording, dict):
            result['animation']['recording'] = {k: recording[k] for k in ('kind', 'fps', 'frameCount', 'duration', 'timingProvenance') if k in recording}
    return result


def public_run(value):
    result = {k: value[k] for k in ('id', 'status', 'startedAt', 'completedAt') if k in value}
    for key in ('attempted', 'requested'):
        if type(value.get(key)) is int and value[key] >= 0:
            result[key] = value[key]
    # IDs and content hashes are useful; process paths and host diagnostics are not.
    def builds(item):
        if not isinstance(item, dict):
            return {}
        safe = {}
        for key, field in item.items():
            if re.search(r'path|log|host|process|pid|directory', key, re.I):
                continue
            if isinstance(field, dict):
                nested = builds(field)
                if nested:
                    safe[key] = nested
            elif isinstance(field, str) and re.fullmatch(r'(?:[0-9a-f]{32,64}|[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12})', field, re.I):
                safe[key] = field
        return safe
    result['producer'] = builds(value.get('producer'))
    return result


def export_current(corpus_path, run_path, recipes_path, output):
    corpus, current, recipes = read(corpus_path), read(run_path), read(recipes_path)
    if current.get('schemaVersion') != 1 or recipes.get('schemaVersion') != 1:
        raise ValueError('Expected current run and recipe schemaVersion 1')
    pin = corpus['corpus']['commit']
    if current['run'].get('recipesSha256') != sha(recipes_path):
        raise ValueError('Reference recipes changed since the current run was initialized')
    if current.get('corpusPin', current['run'].get('corpusPin')) != pin:
        raise ValueError('Run corpus pin differs from inventory')
    if recipes.get('corpusPin') != pin:
        raise ValueError('Recipe corpus pin differs from inventory')
    inventory = unique_rows(corpus['cases'], 'inventory')
    results = unique_rows(current['cases'], 'run')
    recipe_rows = unique_rows(recipes['cases'], 'recipe')
    if set(results) != set(inventory):
        raise ValueError('Run must contain every pinned corpus case exactly once')
    if set(recipe_rows) - set(inventory):
        raise ValueError('Recipe contains an unknown corpus case')
    if set(recipe_rows) != set(inventory):
        raise ValueError('Recipes must cover every pinned corpus case')
    validate_recipe_proofs(current['run'], results, recipe_rows)
    validate_stock_resource_proofs(current['run'], results)
    output = Path(output)
    output.mkdir(parents=True, exist_ok=True)
    stage = Path(tempfile.mkdtemp(prefix='.current-export-', dir=output))
    media = stage / 'assets' / 'current'
    media.mkdir(parents=True)
    raw = f'https://raw.githubusercontent.com/KhronosGroup/glTF-Sample-Assets/{pin}/'
    github = f'https://github.com/KhronosGroup/glTF-Sample-Assets/blob/{pin}/'

    def copy_capture(value, stem, kind, expected):
        if not isinstance(expected, str) or not re.fullmatch('[0-9a-f]{64}', expected):
            raise ValueError('Missing capture SHA256')
        path = Path(value)
        if not path.is_absolute():
            path = Path(run_path).parent / path
        if not path.is_file() or sha(path) != expected:
            raise ValueError(f'Missing or changed capture: {path.name}')
        suffix = path.suffix.lower()
        if suffix not in {'.png', '.apng', '.jpg', '.jpeg', '.webp', '.mp4', '.webm'}:
            raise ValueError('Unsupported capture format')
        name = f'{stem}-{kind}-{expected[:16]}{suffix}'
        shutil.copyfile(path, media / name)
        return f'assets/current/{name}'

    try:
        cases, models = [], {}
        for case_id, source in inventory.items():
            result = results[case_id]
            if result.get('recipeId') != case_id:
                raise ValueError(f'Recipe identity mismatch: {case_id}')
            recipe = recipe_rows.get(case_id)
            source_hash = corpus['files'].get(source['path'], {}).get('sha256')
            actual_input = result.get('input')
            if actual_input and (actual_input.get('path') != source['path'] or actual_input.get('sha256') != source_hash):
                raise ValueError(f'Input identity mismatch: {case_id}')
            load_record = result.get('load', {'status': 'not-run'})
            render_record = result.get('render', {'status': 'not-run'})
            load = {'status': load_record.get('status'), 'error': load_record.get('error')}
            render = {'status': render_record.get('status'), 'error': render_record.get('error')}
            for name, status in [('load', load), ('render', render)]:
                if status.get('status') not in {'passed', 'failed', 'not-run', 'blocked', 'outside-scope'}:
                    raise ValueError(f'Unknown {name} status: {case_id}')
                status['error'] = clean_error(status.get('error'))
            if (load['status'] == 'passed' or render['status'] == 'passed') and not actual_input:
                raise ValueError(f'Executed success needs input identity: {case_id}')
            if load['status'] == 'passed' or render['status'] == 'passed':
                expected_dependencies = {p: corpus['files'].get(p, {}).get('sha256') for p in source.get('dependencies', [])}
                if actual_input.get('dependenciesVerified') is not True or any(actual_input.get('dependencies', {}).get(p) != h for p, h in expected_dependencies.items()):
                    raise ValueError(f'Executed success needs verified dependency content: {case_id}')
            capture = result.get('capture')
            if render['status'] == 'passed' and (load['status'] != 'passed' or not capture):
                raise ValueError(f'Render pass requires successful load and current capture: {case_id}')
            if render['status'] == 'passed' and not result.get('recipeApplied'):
                raise ValueError(f'Render pass requires applied recipe: {case_id}')
            if capture and (load['status'] != 'passed' or render['status'] != 'passed'):
                raise ValueError(f'Capture attached to unsuccessful case: {case_id}')
            identity = {
                'id': case_id, 'source': source_hash,
                'dependencies': {p: corpus['files'].get(p, {}).get('sha256') for p in source.get('dependencies', [])},
                'producer': result.get('producer') or current['run'].get('producer'), 'load': load, 'render': render,
                'capture': {k: v for k, v in (capture or {}).items() if k not in {'path', 'posterPath'}},
                'recipe': recipe, 'recipeApplied': result.get('recipeApplied'),
            }
            if 'stockResourceProof' in result:
                identity['stockResourceProof'] = result['stockResourceProof']
            media_out = None
            if capture:
                kind = capture.get('kind')
                if kind not in {'image', 'video', 'apng'}:
                    raise ValueError(f'Unknown capture kind: {case_id}')
                stem = hashlib.sha256(case_id.encode()).hexdigest()[:20]
                media_out = {'kind': kind, 'url': copy_capture(capture['path'], stem, 'capture', capture['sha256']), 'sha256': capture['sha256']}
                if capture.get('posterPath'):
                    media_out['posterUrl'] = copy_capture(capture['posterPath'], stem, 'poster', capture['posterSha256'])
                for key in ('frames', 'duration', 'sampled', 'description'):
                    if key in capture:
                        media_out[key] = capture[key]
            reference = (recipe or {}).get('reference')
            if reference and reference.get('kind') != 'unavailable' and not re.fullmatch('[0-9a-f]{64}', str(reference.get('sha256', ''))):
                raise ValueError(f'Reference media requires content hash: {case_id}')
            if reference and reference.get('path') and reference.get('kind') != 'unavailable':
                reference_path = Path(recipes['corpusRoot']) / reference['path']
                if not reference_path.is_file() or sha(reference_path) != reference['sha256']:
                    raise ValueError(f'Upstream reference changed: {case_id}')
            row = {
                'id': case_id, 'modelId': source['model'], 'variant': source['variant'],
                'sourceUrl': github + quote(source['path'], safe='/'), 'sourceHash': source_hash,
                'extensions': source.get('extensions_used', []),
                'load': load, 'render': render, 'reference': reference, 'capture': media_out,
                'recipe': public_setup(recipe), 'recipeApplied': public_setup(result.get('recipeApplied')),
                'reason': clean_error(result.get('reason')), 'provenanceFingerprint': fingerprint(identity),
                'visualIdentityVersion': 2,
                'visualReview': {'status': 'pending', 'reviewer': None},
            }
            capture_pixels = decoded_media(stage / media_out['url']) if media_out else None
            reference_pixels = decoded_media(reference_path) if reference and reference.get('path') and reference.get('kind') != 'unavailable' else {'sha256': (reference or {}).get('sha256'), 'kind': (reference or {}).get('kind')}
            row['comparisonFingerprint'] = comparison_identity(row, capture_pixels, reference_pixels)
            cases.append(row)
            if source['model'] not in models:
                models[source['model']] = {
                    'id': source['model'], 'name': source['model'],
                    'sourceUrl': github + 'Models/' + quote(source['model']) + '/README.md',
                    'licenses': source.get('provenance', {}).get('legal', []),
                }
        data = {'schemaVersion': 1, 'corpusPin': pin, 'run': public_run(current['run']), 'models': list(models.values()), 'cases': cases,
                'summary': {'models': len(models), 'variants': len(cases), 'load': dict(Counter(c['load']['status'] for c in cases)),
                            'render': dict(Counter(c['render']['status'] for c in cases)), 'captures': sum(bool(c['capture']) for c in cases)}}
        if pin == '90d7ede14c7e280af263824604b427a1ca02cb66':
            from model_review_guidance import enrich_guidance
            enrich_guidance(data, corpus, recipes['corpusRoot'])
        (stage / 'data.json').write_text(json.dumps(data, ensure_ascii=False, indent=2) + '\n')
        # Validate everything before touching the current output. Keep a transient
        # rollback only during this replacement; successful export retains no archive.
        destination = output / 'assets'
        rollback = stage / 'previous-assets'
        if destination.exists():
            destination.replace(rollback)
        try:
            (stage / 'assets').replace(destination)
            (stage / 'data.json').replace(output / 'data.json')
        except BaseException:
            if destination.exists():
                shutil.rmtree(destination)
            if rollback.exists():
                rollback.replace(destination)
            raise
        for obsolete in ('receipts', 'direct-gpu-provenance.json', 'motion-provenance.json'):
            path = output / obsolete
            if path.is_dir():
                shutil.rmtree(path)
            elif path.exists():
                path.unlink()
        return data
    finally:
        shutil.rmtree(stage)


def main():
    root = Path(__file__).resolve().parents[4]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--corpus', type=Path, default=root / 'research/gltf-validation/corpus-case-manifest.json')
    parser.add_argument('--run', type=Path, default=root / 'Temp/gltf-validation/current/run.json')
    parser.add_argument('--recipes', type=Path, default=root / 'research/gltf-validation/reference-recipes.json')
    parser.add_argument('--output', type=Path, required=True, help='Site dist directory or review draft')
    args = parser.parse_args()
    data = export_current(args.corpus, args.run, args.recipes, args.output)
    print(json.dumps(data['summary']))


if __name__ == '__main__':
    main()
