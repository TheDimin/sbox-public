#!/usr/bin/env python3
"""Independent pinned-source audit and literal photometric controls, no engine evaluator."""
import hashlib
import json
import math
from pathlib import Path

root = Path(__file__).resolve().parents[6]
model = root / 'Temp/gltf-reference/glTF-Sample-Assets/Models/PointLightIntensityTest'
expected_hashes = {
    'glTF/PointLightIntensityTest.gltf': '7913fe3e6829beba77f65ea749e40b3e95906bc2894afc443c22e3d91e263733',
    'glTF/PointLightIntensityTest.bin': '29c2e0d139c8fe4a190654179177166c5da1932a9378d19e8f7f74622e6b16d6',
    'glTF/LampColorNames.png': '92433d3e05d124a8e37d89f4f0f4863a02654409210a31b35c82154b0ea0a3cd',
    'glTF-Binary/PointLightIntensityTest.glb': '025ea8e44840a88ded75fe0665f693a14558d345c2955b054aec4edbaa23a341',
}
for name, expected in expected_hashes.items():
    assert hashlib.sha256((model / name).read_bytes()).hexdigest() == expected, name
d = json.loads((model / 'glTF/PointLightIntensityTest.gltf').read_text())
assert 'KHR_lights_punctual' in d['extensionsUsed'] and 'KHR_lights_punctual' not in d.get('extensionsRequired', [])
lights = d['extensions']['KHR_lights_punctual']['lights']
colors = [[1,1,1], [1,0,0], [0,0,1], [0,1,0], [.5,.5,.5], [0,0,1], [0,1,0], [1,0,0]]
assert len(lights) == len(colors) == 8
for light, color in zip(lights, colors):
    assert light['type'] == 'point' and light['intensity'] == 1 and light['range'] == 1.125 and light['color'] == color
assert d['nodes'][14]['children'] == [11,12,13]
assert [d['nodes'][n]['extensions']['KHR_lights_punctual']['light'] for n in [11,12,13]] == [5,6,7]
assert all(d['nodes'][n]['translation'] == [0,0,0.20000000298023224] for n in [11,12,13])
assert all(abs(x-.8)<1e-6 for x in d['materials'][0]['pbrMetallicRoughness']['baseColorFactor'][:3])
assert 'KHR_materials_unlit' in d['materials'][2]['extensions']
# Unit-intensity unbounded inverse-square illuminance (lux): the finite-range
# sample is tested separately, because its falloff is constrained but its
# exact smoothing formula is an implementation choice in the extension.
irradiance = [1 / distance**2 for distance in (.5,1,2)]
assert irradiance == [4,1,.25]
assert [sum(v[c] for v in colors[5:]) for c in range(3)] == colors[0]
assert colors[4] == [0.5*x for x in colors[0]]
assert math.isclose(1.125 / .0254, 44.29133858267717)
print(json.dumps({'source_hashes':expected_hashes, 'light_count':len(lights), 'unbounded_lux_at_half_one_two_m':irradiance, 'authored_range_m':1.125, 'authored_range_inches':1.125/.0254, 'scope':'Pinned source and independent algebra only; no engine rendering or public loader execution.'}, indent=2))
