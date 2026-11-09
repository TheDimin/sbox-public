#!/usr/bin/env python3
"""Prepare isolated native IBL capture requests, never execute or publish them.

The coordinator must first mount/import the CC0 HDR fixture and verify resource
identity. The output is limited to a fresh /tmp subdirectory, not current Run.
"""
import argparse
import copy
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[4]
BASE = ROOT / 'research/gltf-validation'
HDR = BASE / 'fixtures/environment/artist_workshop_1k.hdr'
BLACK = ROOT / 'game/core/dev/env_cubemap_black.exr'
BLACK_COMPILED = ROOT / 'game/core/dev/env_cubemap_black.vtex_c'
PIN = '90d7ede14c7e280af263824604b427a1ca02cb66'
HDR_HASH = 'f6da9025acadb1149c973fe2bef53d55f4e7144285a87ec1a357520fbcad4767'


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def prepare(output, texture_path, compiled_path, lighting_source=None):
    output = output.resolve()
    if output == Path('/tmp') or not output.is_relative_to('/tmp') or output.exists():
        raise ValueError('Output must be a new directory strictly beneath /tmp')
    if (not texture_path or texture_path.startswith('/') or '\\' in texture_path
            or any(part in ('', '.', '..') for part in texture_path.split('/'))):
        raise ValueError('Expected mounted resource-relative texture path')
    compiled_path = compiled_path.resolve(strict=True)
    lighting_source = HDR if lighting_source is None else lighting_source.resolve(strict=True)
    if not lighting_source.is_file():
        raise ValueError("Missing actual environment source")
    for path in (HDR, BLACK, BLACK_COMPILED):
        if not path.is_file():
            raise ValueError(f'Missing source or compiled file: {path}')
    if sha(HDR) != HDR_HASH:
        raise ValueError('Artist Workshop source hash changed')
    doc = json.loads((BASE / 'reference-recipes.json').read_text())
    manifest = json.loads((BASE / 'corpus-case-manifest.json').read_text())
    if doc['corpusPin'] != PIN or manifest['corpus']['commit'] != PIN:
        raise ValueError('Unexpected corpus pin')
    recipe = next(r for r in doc['cases'] if r['id'] == 'CompareMetallic/glTF')
    entry = next(r for r in manifest['cases'] if r['id'] == recipe['id'])
    corpus = Path(doc['corpusRoot']).resolve(strict=True)
    model = (corpus / entry['path']).resolve(strict=True)
    if not model.is_relative_to(corpus):
        raise ValueError('Model escapes the corpus')
    expected = manifest['corpus']['files'][entry['path']]['sha256'] if 'files' in manifest['corpus'] else manifest['files'][entry['path']]['sha256']
    if sha(model) != expected:
        raise ValueError('Model source hash differs from pinned manifest')
    recipe = copy.deepcopy(recipe)
    recipe['presentation']['lights'] = []
    recipe['presentation']['background']['color'] = [0, 0, 0, 1]
    recipe['presentation']['exposure'] = 1
    envs = {
        'no_environment': {'type': 'engine-default'},
        'black_environment': {'type': 'cubemap', 'texturePath': texture_path,
                              'sourcePath': str(lighting_source), 'sourceSha256': sha(lighting_source),
                              'compiledPath': str(compiled_path), 'compiledSha256': sha(compiled_path)},
        'artist_workshop': {'type': 'cubemap', 'texturePath': texture_path,
                            'sourcePath': str(lighting_source), 'sourceSha256': sha(lighting_source),
                            'compiledPath': str(compiled_path), 'compiledSha256': sha(compiled_path)},
    }
    output.mkdir(parents=True)
    for name, environment in envs.items():
        if environment['type'] == 'cubemap':
            environment.update(sizeInches=100000, yawDegrees=0, tint=([0, 0, 0, 1] if name == 'black_environment' else [1, 1, 1, 1]))
        row = copy.deepcopy(recipe)
        row['presentation']['environment'] = environment
        request = {'schemaVersion': 1, 'id': entry['id'], 'sourcePath': str(model),
                   'corpusRoot': str(corpus), 'inputPath': entry['path'],
                   'outputDir': str(output / name), 'recipe': row, 'timeoutSeconds': 90}
        (output / f'{name}.json').write_text(json.dumps(request, indent=2) + '\n')
    return output


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', required=True, type=Path)
    parser.add_argument('--texture-path', required=True)
    parser.add_argument('--compiled-path', required=True, type=Path)
    args = parser.parse_args()
    print(prepare(args.output, args.texture_path, args.compiled_path))
