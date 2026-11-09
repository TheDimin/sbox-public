#!/usr/bin/env python3
"""Prepare isolated Duck lighting diagnostics; do not execute or replace approved captures."""
import copy
import hashlib
import json
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[4]
BASE = ROOT / 'research/gltf-validation'
PIN = '90d7ede14c7e280af263824604b427a1ca02cb66'


def prepare(output):
    output = Path(output).resolve()
    if output == Path('/tmp') or not output.is_relative_to('/tmp') or output.exists():
        raise ValueError('Output must be a new directory strictly beneath /tmp')
    recipes = json.loads((BASE / 'reference-recipes.json').read_text())
    corpus = json.loads((BASE / 'corpus-case-manifest.json').read_text())
    if recipes['corpusPin'] != PIN or corpus['corpus']['commit'] != PIN:
        raise ValueError('Corpus pin differs')
    recipe = next(row for row in recipes['cases'] if row['id'] == 'Duck/glTF')
    entry = next(row for row in corpus['cases'] if row['id'] == recipe['id'])
    root = Path(recipes['corpusRoot']).resolve(strict=True)
    source = (root / entry['path']).resolve(strict=True)
    if not source.is_relative_to(root) or hashlib.sha256(source.read_bytes()).hexdigest() != corpus['files'][entry['path']]['sha256']:
        raise ValueError('Pinned Duck source identity differs')
    lights = recipe['presentation']['lights']
    if len(lights) != 1 or lights[0]['type'] != 'directional' or lights[0]['color'] != [1, 1, 1] or lights[0]['intensity'] != 2:
        raise ValueError('Expected reviewed recipe with one neutral intensity-2 directional light')
    output.mkdir(parents=True)
    for label, intensity in [('baseline', 2), ('half', 1), ('quarter', .5)]:
        variant = copy.deepcopy(recipe)
        variant['presentation']['lights'][0]['intensity'] = intensity
        request = {'schemaVersion': 1, 'id': entry['id'], 'sourcePath': str(source),
                   'corpusRoot': str(root), 'inputPath': entry['path'],
                   'outputDir': str(output / label), 'recipe': variant, 'timeoutSeconds': 90}
        (output / f'{label}.json').write_text(json.dumps(request, indent=2) + '\n')
    return output


if __name__ == '__main__':
    if len(sys.argv) != 2:
        raise SystemExit('Usage: prepare_duck_light_sweep.py /tmp/new-directory')
    print(prepare(sys.argv[1]))
