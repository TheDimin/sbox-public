#!/usr/bin/env python3
"""Prepare isolated TextureTransformTest lighting diagnostics; do not execute or replace approved captures."""
import copy
import hashlib
import json
import shutil
import struct
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[6]
BASE = ROOT / 'research/gltf-validation'
PIN = '90d7ede14c7e280af263824604b427a1ca02cb66'


def material_grid(output, source):
    """Opaque clamped gray/white texels versus independent linear factors.

    This derivative is a diagnostic fixture, never the canonical model. The
    original geometry buffer and Arrow.png are copied byte-for-byte.
    """
    from PIL import Image
    image = Image.open(source.parent / 'Arrow.png').convert('RGBA')
    assert image.getpixel((32, 16)) == (255, 255, 255, 255)
    original = json.loads(source.read_text())
    output.mkdir()
    for name in ('Arrow.png', 'TextureTransformTest.bin'):
        shutil.copyfile(source.parent / name, output / name)
    uv = b''.join(struct.pack('<ff', u, v) * 4 for u, v in [(1.25, .125), (.25, .125)])
    (output / 'controls.bin').write_bytes(uv)
    gray = ((224 / 255 + .055) / 1.055) ** 2.4
    root = {'asset': {'version': '2.0', 'generator': 'Independent opaque border/factor control; not canonical source'},
            'buffers': [original['buffers'][0], {'uri': 'controls.bin', 'byteLength': len(uv)}],
            'bufferViews': original['bufferViews'] + [{'buffer': 1, 'byteOffset': i * 32, 'byteLength': 32} for i in range(2)],
            'accessors': original['accessors'] + [{'bufferView': len(original['bufferViews']) + i, 'componentType': 5126, 'count': 4, 'type': 'VEC2'} for i in range(2)],
            'images': [{'uri': 'Arrow.png'}], 'samplers': original['samplers'], 'textures': [{'source': 0, 'sampler': 0}],
            'materials': [{'pbrMetallicRoughness': dict({'metallicFactor': 0, 'roughnessFactor': 1}, **({'baseColorTexture': {'index': 0}} if i < 2 else {'baseColorFactor': [gray if i == 2 else 1] * 3 + [1]}))} for i in range(4)],
            'meshes': [], 'nodes': [], 'scenes': [{'nodes': list(range(4))}], 'scene': 0}
    for i in range(4):
        primitive = copy.deepcopy(original['meshes'][0]['primitives'][0])
        primitive['attributes']['TEXCOORD_0'] = len(original['accessors']) + i % 2
        primitive['material'] = i
        root['meshes'].append({'primitives': [primitive]})
        root['nodes'].append({'mesh': i, 'translation': [-.6 if i % 2 == 0 else .6, .6 if i < 2 else -.6, 0], 'scale': [.7, .7, .7]})
    path = output / 'opaque-gray-factor-control.gltf'
    path.write_text(json.dumps(root, indent=2) + '\n')
    return path


def prepare(output, canonical_intensity=None, compiled=None, reference_framing=False):
    output = Path(output).resolve()
    if not output.is_relative_to(ROOT / 'Temp/gltf-validation/direct-gpu') or output.exists():
        raise ValueError('Output must be new under Temp/gltf-validation/direct-gpu')
    recipes = json.loads((BASE / 'reference-recipes.json').read_text())
    corpus = json.loads((BASE / 'corpus-case-manifest.json').read_text())
    if recipes['corpusPin'] != PIN or corpus['corpus']['commit'] != PIN:
        raise ValueError('Corpus pin differs')
    recipe = next(row for row in recipes['cases'] if row['id'] == 'TextureTransformTest/glTF')
    entry = next(row for row in corpus['cases'] if row['id'] == recipe['id'])
    root = Path(recipes['corpusRoot']).resolve(strict=True)
    source = (root / entry['path']).resolve(strict=True)
    if not source.is_relative_to(root) or hashlib.sha256(source.read_bytes()).hexdigest() != corpus['files'][entry['path']]['sha256']:
        raise ValueError('Pinned TextureTransformTest source identity differs')
    for dependency in source.parent.iterdir():
        if dependency.suffix not in ('.gltf', '.bin', '.png'):
            continue
        key = str(dependency.relative_to(root))
        if hashlib.sha256(dependency.read_bytes()).hexdigest() != corpus['files'][key]['sha256']:
            raise ValueError('Pinned model dependency identity differs: ' + key)
    lights = recipe['presentation']['lights']
    if len(lights) != 1 or lights[0]['type'] != 'directional' or lights[0]['color'] != [1, 1, 1] or lights[0]['intensity'] != 2:
        raise ValueError('Expected reviewed recipe with one neutral intensity-2 directional light')
    output.mkdir(parents=True)
    controls = [('baseline', 2), ('locked', 2), ('half', 1), ('quarter', .5)] if canonical_intensity is None else [('source', canonical_intensity), ('compiled', canonical_intensity)]
    for label, intensity in controls:
        variant = copy.deepcopy(recipe)
        variant['presentation']['lights'][0]['intensity'] = intensity
        if label != 'baseline':
            # Camera-only settings are reset by editor ticks. The shared
            # runner's opt-in stock effect verifies retention after render.
            variant['presentation']['tonemapping'] = {'mode': 'Linear', 'exposureCompensationEv': 0}
        if reference_framing:
            # The preview's quads occupy ~85/300 of its width; the old generic
            # 1.25 bounds padding gives ~75/300. 2.6m fits the observed extent.
            # It remains an inferred camera, with the same front axis/target.
            variant['camera']['orthographicHeight'] = 2.6
            variant['camera']['preset'] = 'texture-transform-preview-framing'
        actual_source = Path(compiled).resolve(strict=True) if label == 'compiled' else source
        actual_root = actual_source.parent if label == 'compiled' else root
        actual_input = actual_source.name if label == 'compiled' else entry['path']
        request = {'schemaVersion': 1, 'id': entry['id'], 'sourcePath': str(actual_source),
                   'corpusRoot': str(actual_root), 'inputPath': actual_input,
                   'outputDir': str(output / label), 'recipe': variant, 'timeoutSeconds': 90}
        (output / f'{label}.json').write_text(json.dumps(request, indent=2) + '\n')
    if canonical_intensity is None:
        grid = material_grid(output / 'material-grid-input', source)
        variant = copy.deepcopy(recipe)
        variant['camera']['target'] = [0, 0, 0]
        variant['camera']['position'] = [0, 0, 5]
        variant['camera']['orthographicHeight'] = 2.4
        variant['presentation']['lights'][0]['intensity'] = .5
        variant['presentation']['tonemapping'] = {'mode': 'Linear', 'exposureCompensationEv': 0}
        request = {'schemaVersion': 1, 'id': entry['id'], 'sourcePath': str(grid), 'corpusRoot': str(grid.parent), 'inputPath': grid.name,
                   'outputDir': str(output / 'material-grid'), 'recipe': variant, 'timeoutSeconds': 90}
        (output / 'material-grid.json').write_text(json.dumps(request, indent=2) + '\n')
    return output


if __name__ == '__main__':
    if len(sys.argv) != 2:
        raise SystemExit('Usage: prepare_background_controls.py /absolute/Temp/gltf-validation/direct-gpu/new-directory')
    print(prepare(sys.argv[1]))
