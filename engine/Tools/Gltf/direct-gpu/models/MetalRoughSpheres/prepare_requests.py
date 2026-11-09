"""Prepare isolated public Model.Load requests for actual and derived model forms."""
import copy
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[6]
BASE = ROOT / 'research/gltf-validation'
MODEL = 'MetalRoughSpheres'
PIN = '90d7ede14c7e280af263824604b427a1ca02cb66'


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def prepare(output, preflight, packages, previews=False, variants=None):
    output = Path(output).resolve()
    if output == Path('/tmp') or not output.is_relative_to('/tmp') or output.exists():
        raise ValueError('Output must be new and strictly below /tmp')
    if not preflight.get('passed') or preflight.get('Depth') != 6 or preflight.get('Mips', 0) < 2:
        raise ValueError('Valid IBL preflight missing')
    source, compiled = Path(preflight['source']).resolve(strict=True), Path(preflight['compiled']).resolve(strict=True)
    if sha(source) != preflight['sourceSha256'] or sha(compiled) != preflight['compiledSha256']:
        raise ValueError('Environment bytes changed since preflight')
    recipes = json.loads((BASE / 'reference-recipes.json').read_text())
    manifest = json.loads((BASE / 'corpus-case-manifest.json').read_text())
    if recipes['corpusPin'] != PIN or manifest['corpus']['commit'] != PIN:
        raise ValueError('Pin differs')
    origin = Path(recipes['corpusRoot']).resolve(strict=True)
    forms = {row['Variant']: row for row in packages['rows']}
    available_variants = ('glTF', 'glTF-Binary', 'glTF-Embedded')
    if set(forms) != set(available_variants):
        raise ValueError('Expected exactly the three upstream forms')
    variants = available_variants if variants is None else tuple(variants)
    if not variants or not set(variants).issubset(available_variants):
        raise ValueError('Unknown model variant')
    pkgroot = Path(packages['output']).resolve(strict=True)
    output.mkdir(parents=True)
    index = []
    for variant in variants:
        case_id = MODEL + '/' + variant
        recipe = copy.deepcopy(next(row for row in recipes['cases'] if row['id'] == case_id))
        entry = next(row for row in manifest['cases'] if row['id'] == case_id)
        source_path = (origin / entry['path']).resolve(strict=True)
        if not source_path.is_relative_to(origin) or sha(source_path) != manifest['files'][entry['path']]['sha256']:
            raise ValueError('Pinned source differs')
        package_path = Path(forms[variant]['compiledPath']).resolve(strict=True)
        if not package_path.is_relative_to(pkgroot) or sha(package_path) != forms[variant]['compiledSha256']:
            raise ValueError('Packaged form differs')
        for dependency in forms[variant]['dependencies']:
            if sha(package_path.parent / dependency['path']) != dependency['sha256']:
                raise ValueError('Packaged external image differs')
        env = {'type': 'cubemap', 'texturePath': preflight['resource'], 'sourcePath': str(source),
               'sourceSha256': preflight['sourceSha256'], 'compiledPath': str(compiled),
               'compiledSha256': preflight['compiledSha256'], 'sizeInches': 100000,
               'yawDegrees': 0, 'tint': [1, 1, 1, 1], 'sourceReferenceEnvironment': 'unknown'}
        recipe['presentation']['environment'] = env
        modes = [('hdr-only', 0), ('hdr-fill', .5)] if previews and variant == 'glTF' else ([] if previews else [('hdr-fill', .5)])
        for mode, intensity in modes:
            adjusted = copy.deepcopy(recipe)
            adjusted['presentation']['lights'] = [] if intensity == 0 else [{**adjusted['presentation']['lights'][0], 'intensity': intensity}]
            candidates = [('source', origin, entry['path'], source_path)] if previews else [
                ('source', origin, entry['path'], source_path),
                ('compiled', pkgroot, package_path.relative_to(pkgroot).as_posix(), package_path)]
            for form, corpus, rel, disk in candidates:
                label = f'{variant}-{form}-{mode}'
                request = {'schemaVersion': 1, 'id': case_id, 'sourcePath': str(disk), 'corpusRoot': str(corpus),
                           'inputPath': rel, 'outputDir': str(output / label), 'recipe': adjusted, 'timeoutSeconds': 150}
                (output / (label + '.json')).write_text(json.dumps(request, indent=2) + '\n')
                index.append({'label': label, 'variant': variant, 'form': form, 'lightIntensity': intensity, 'sourceSha256': sha(disk)})
    (output / 'inventory.json').write_text(json.dumps(index, indent=2) + '\n')
    return index
