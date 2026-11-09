#!/usr/bin/env python3
"""Independent pinned JSON/BIN audit. Does not exercise the engine renderer."""
import hashlib
import json
import math
import pathlib
import struct

root = pathlib.Path('Temp/gltf-reference/glTF-Sample-Assets/Models/AnimatedColorsCube/glTF')
raw = (root / 'AnimatedColorsCube.gltf').read_bytes()
bin = (root / 'AnimatedColorsCube.bin').read_bytes()
assert hashlib.sha256(raw).hexdigest() == '5639d1aa3489a4ebfeda2045876b671b94e0bb046c66e069c959b62939e8abca'
assert hashlib.sha256(bin).hexdigest() == 'c5980c793c830b16b60b8eac80765b26dd2f868aa13b48ee76fbab8c42dd5b88'
doc = json.loads(raw)
assert 'KHR_animation_pointer' in doc['extensionsUsed'] and 'KHR_animation_pointer' not in doc.get('extensionsRequired', [])
channels = doc['animations'][0]['channels']
assert [c['target']['path'] for c in channels] == ['translation', 'rotation', 'pointer']
assert channels[2]['target']['extensions']['KHR_animation_pointer']['pointer'] == '/materials/0/pbrMetallicRoughness/baseColorFactor'
assert all(s['interpolation'] == 'LINEAR' for s in doc['animations'][0]['samplers'])
def values(i):
    a = doc['accessors'][i]; v = doc['bufferViews'][a['bufferView']]
    assert a['componentType'] == 5126 and v['buffer'] == 0
    n = {'SCALAR': 1, 'VEC3': 3, 'VEC4': 4}[a['type']]
    stride = v.get('byteStride', n * 4)
    start = v.get('byteOffset', 0) + a.get('byteOffset', 0)
    return [struct.unpack_from('<' + 'f' * n, bin, start + j * stride) for j in range(a['count'])]
def near(a, b):
    assert len(a) == len(b) and all(math.isclose(x, y, abs_tol=2e-6) for x, y in zip(a, b)), (a, b)
times, colors = values(12), values(13)
assert len(times) == len(colors) == 151
for key, expected in {0:(.8,.02,.02,1), 15:(.41,.41,.02,1), 30:(.02,.8,.02,1), 75:(.02,.41,.41,1), 90:(.02,.02,.8,1), 150:(.8,.02,.02,1)}.items():
    near(times[key], (key/60,)); near(colors[key], expected)
near(tuple((x+y)/2 for x,y in zip(colors[15],colors[16])), (.3905288726,.4294711202,.02,1))
assert abs(colors[15][0]-colors[75][0]) > .38 and abs(colors[15][2]-colors[75][2]) > .38
assert len(values(9)) == 181 and times[-1][0] == 2.5 and values(9)[-1][0] == 3
print('PASS: pinned source hashes, optional target, LINEAR tracks, six colour keys, between-key colour, wrong-phase control and track endpoints')
