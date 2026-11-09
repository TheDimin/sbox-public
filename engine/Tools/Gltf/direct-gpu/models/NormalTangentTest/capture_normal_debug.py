#!/usr/bin/env python3
"""Capture one reviewed recipe with the verified editor; never updates the corpus run."""
import hashlib
import json
import subprocess
import sys
from pathlib import Path

root = Path('/home/damian/projects/sbox-public')
sys.path.insert(0, str(root / 'engine/Tools/Gltf/scripts'))
from run_editor_tests import CliSession, discover, tool_value, validate_arguments
from run_editor_probes import check_status

def digest(data): return hashlib.sha256(data).hexdigest()
def require(condition, message):
    if not condition: raise RuntimeError(message)

variant = 'glTF'
require(len(sys.argv) == 3, 'Usage: capture_visual.py ENVIRONMENT_JSON LABEL')
env_path = Path(sys.argv[1]).resolve(strict=True)
label = sys.argv[2]
require(label.isalnum() or label.replace('-', '').isalnum(), 'Unsafe output label')
require(variant in ('glTF', 'glTF-Binary'), 'Unsupported upstream form')
output = root / ('Temp/gltf-validation/direct-gpu/normal-tangent-visual-' + label)
output.mkdir(parents=True, exist_ok=False)
transport = output / 'transport'
transport.mkdir()
case = output / 'case'
project = Path('/home/damian/projects/Survive/survive.sbproj').resolve(strict=True)
ready = json.loads((root / 'Temp/gltf-validation/editor-session-ready.json').read_bytes())
require(ready['ready'] is True, 'Editor readiness marker not ready')
pid = ready['pid']
approved_path = Path(ready['discoveryPath'])
approved_raw = approved_path.read_bytes()
require(digest(approved_raw) == ready['discoverySha256'], 'Discovery changed')
approved = json.loads(approved_raw)
recipes = json.loads((root / 'research/gltf-validation/reference-recipes.json').read_bytes())
recipe = next(x for x in recipes['cases'] if x['id'] == 'NormalTangentTest/' + variant)
recipe = json.loads(json.dumps(recipe))
choice = json.loads(env_path.read_bytes())
recipe['camera']['orthographicHeight'] = 2.28
recipe['presentation']['background']['color'] = [0.2,0.2,0.2980392156862745,1]
recipe['presentation']['environment'] = choice['environment']
recipe['presentation']['lights'][0]['intensity'] = choice['directionalIntensity']
recipe['presentation']['exposure'] = choice.get('exposure', 1)
recipe['presentation']['tonemapping'] = choice['tonemapping']
recipe['provenance']['camera'] = 'inferred from screenshot framing; orthographic height 2.28 metres'
recipe['provenance']['presentation'] = 'chosen outdoor comparison environment and intensity; reference HDRI and exposure unknown'
corpus = Path(recipes['corpusRoot']).resolve(strict=True)
require(subprocess.check_output(['git', '-C', str(corpus), 'rev-parse', 'HEAD'], text=True).strip() == '90d7ede14c7e280af263824604b427a1ca02cb66', 'Corpus revision changed')
relative = recipe['input']['path']
source = (corpus / relative).resolve(strict=True)
require(digest(source.read_bytes()) == recipe['input']['sha256'], 'Source changed')
driver_source = root / 'engine/Tools/Gltf/direct-gpu/models/NormalTangentTest/editor_normal_world_debug.csx'
driver_bytes = driver_source.read_bytes()
request = {'schemaVersion': 1, 'id': recipe['id'], 'sourcePath': str(source), 'corpusRoot': str(corpus), 'inputPath': relative, 'outputDir': str(case), 'recipe': recipe, 'timeoutSeconds': 120}
(output / 'request.json').write_text(json.dumps(request, indent=2) + '\n')
session = CliSession(root / 'game/bin/managed/sbox-cli', pid, transport, 180)
try:
    live, exchange = discover(session, pid, project, {}, transport)
    require(live['tools'] == approved['tools'], 'Live schemas differ from approved discovery')
    before = tool_value(session.call(live['tools']['editor_status'], {}))
    check_status(before, project)
    (output / 'status-before.json').write_text(json.dumps(before, indent=2) + '\n')
    driver = exchange / 'editor_corpus.csx'
    driver.write_bytes(driver_bytes)
    (exchange / 'gltf-run-case.json').write_text(json.dumps(request) + '\n')
    args = {'path': str(driver)}
    validate_arguments(live['tools']['run_csharp'], args)
    result = tool_value(session.call(live['tools']['run_csharp'], args))
    (output / 'returned-value.json').write_text(json.dumps(result, indent=2) + '\n')
    after = tool_value(session.call(live['tools']['editor_status'], {}))
    check_status(after, project)
    (output / 'status-after.json').write_text(json.dumps(after, indent=2) + '\n')
    require(result.get('id') == recipe['id'], 'Wrong case returned')
    require(not result.get('cleanup_errors'), 'Capture cleanup errors')
    require(result.get('load', {}).get('status') == 'passed' and result.get('render', {}).get('status') == 'passed', 'Load or render failed')
    print(json.dumps({'result': str(output / 'returned-value.json'), 'capture': result.get('capture'), 'driver_sha256': digest(driver_bytes)}, indent=2))
finally:
    status = session.close()
    (output / 'cli-close.json').write_text(json.dumps(status, indent=2) + '\n')
