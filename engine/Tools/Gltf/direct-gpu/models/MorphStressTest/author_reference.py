"""Decode pinned Khronos JSON/BIN independently, using only Python's standard library.

CC-BY-4.0 geometry by Ed Mackey / Analytical Graphics, Inc. (2021).
Input license: glTF-Sample-Assets/Models/MorphStressTest/LICENSE.md.
"""
import bisect
import hashlib
import json
import math
import pathlib
import struct

HERE = pathlib.Path(__file__).resolve().parent
ROOT = HERE.parents[4]
MODEL = ROOT / 'Temp/gltf-reference/glTF-Sample-Assets/Models/MorphStressTest'
PIN = '90d7ede14c7e280af263824604b427a1ca02cb66'
COMPONENTS = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4}
TYPES = {5120: 'b', 5121: 'B', 5122: 'h', 5123: 'H', 5125: 'I', 5126: 'f'}


def load(path):
    data = path.read_bytes()
    if path.suffix == '.gltf':
        document = json.loads(data)
        return document, [(path.parent / b['uri']).read_bytes() for b in document['buffers']]
    magic, version, length = struct.unpack_from('<III', data)
    assert magic == 0x46546c67 and version == 2 and length == len(data)
    chunks, at = [], 12
    while at < length:
        size, kind = struct.unpack_from('<II', data, at)
        chunks.append((kind, data[at + 8:at + 8 + size]))
        at += size + 8
    return json.loads(chunks[0][1]), [chunks[1][1]]


def accessor(document, buffers, index):
    a = document['accessors'][index]
    components, fmt = COMPONENTS[a['type']], TYPES[a['componentType']]
    size = struct.calcsize('<' + fmt)
    result = [[0] * components for _ in range(a['count'])]
    if 'bufferView' in a:
        view = document['bufferViews'][a['bufferView']]
        begin = view.get('byteOffset', 0) + a.get('byteOffset', 0)
        stride = view.get('byteStride', components * size)
        result = [list(struct.unpack_from('<' + fmt * components, buffers[view['buffer']], begin + i * stride))
                  for i in range(a['count'])]
    if 'sparse' in a:
        sparse = a['sparse']
        iv = document['bufferViews'][sparse['indices']['bufferView']]
        vv = document['bufferViews'][sparse['values']['bufferView']]
        ifmt = TYPES[sparse['indices']['componentType']]
        for i in range(sparse['count']):
            element = struct.unpack_from('<' + ifmt, buffers[iv['buffer']], iv.get('byteOffset', 0) +
                                         sparse['indices'].get('byteOffset', 0) + i * struct.calcsize(ifmt))[0]
            result[element] = list(struct.unpack_from('<' + fmt * components, buffers[vv['buffer']],
                vv.get('byteOffset', 0) + sparse['values'].get('byteOffset', 0) + i * components * size))
    return result


def sample_weights(times, values, time):
    if time <= times[0]:
        return values[0][:]
    if time >= times[-1]:
        return values[-1][:]
    left = bisect.bisect_right(times, time) - 1
    alpha = (time - times[left]) / (times[left + 1] - times[left])
    return [x + (y - x) * alpha for x, y in zip(values[left], values[left + 1])]


def morphed(reference, primitive, vertex, weights, semantic):
    if len(weights) not in (8, 9):
        raise ValueError('Oracle weights must identify the actual eight targets or the explicit ninth-target fixture')
    p = reference['primitives'][primitive]
    result = reference['values'][str(p['attributes'][semantic])][vertex][:]
    for t, weight in enumerate(weights):
        target = p['targets'][t if t < 8 else 7]  # Only the explicit ninth fixture duplicates authored target 7.
        if semantic in target:
            delta = reference['values'][str(target[semantic])][vertex]
            result = [x + weight * d for x, d in zip(result, delta)]
    return result


def normalized(value):
    length = math.sqrt(sum(x * x for x in value))
    return [x / length for x in value] if length > 1e-12 else None


def build():
    forms, shared = [], None
    for variant, suffix in [('glTF', 'gltf'), ('glTF-Binary', 'glb')]:
        relative = f'{variant}/MorphStressTest.{suffix}'
        doc, buffers = load(MODEL / relative)
        values = {str(i): accessor(doc, buffers, i) for i in range(len(doc['accessors']))}
        if shared is None:
            shared = values
        else:
            assert shared == values, 'Upstream variants require variant-specific numerical references'
        desc = []
        for i, a in enumerate(doc['accessors']):
            v = doc['bufferViews'][a['bufferView']]
            raw = buffers[v['buffer']][v.get('byteOffset', 0):v.get('byteOffset', 0) + v['byteLength']]
            desc.append({'accessor': i, 'count': a['count'], 'components': COMPONENTS[a['type']],
                         'componentType': a['componentType'], 'normalized': a.get('normalized', False),
                         'byteOffset': a.get('byteOffset', 0), 'viewByteOffset': v.get('byteOffset', 0),
                         'byteStride': v.get('byteStride', COMPONENTS[a['type']] * struct.calcsize(TYPES[a['componentType']])),
                         'viewLength': v['byteLength'], 'sparse': 'sparse' in a,
                         'sha256': hashlib.sha256(raw).hexdigest()})
        forms.append({'name': variant, 'path': relative, 'accessors': desc})
    doc, _ = load(MODEL / 'glTF/MorphStressTest.gltf')
    mesh = doc['meshes'][0]
    primitives = [{'attributes': p['attributes'], 'targets': p['targets'], 'indices': p['indices'],
                   'material': p['material'], 'vertexCount': len(shared[str(p['attributes']['POSITION'])])}
                  for p in mesh['primitives']]
    clips = []
    for clip in doc['animations']:
        s = clip['samplers'][0]
        times = [v[0] for v in shared[str(s['input'])]]
        weights = [v[0] for v in shared[str(s['output'])]]
        clips.append({'name': clip['name'], 'interpolation': s['interpolation'], 'times': times,
                      'weights': [weights[i:i + 8] for i in range(0, len(weights), 8)]})
    files = {str(p.relative_to(MODEL)): {'length': p.stat().st_size,
             'sha256': hashlib.sha256(p.read_bytes()).hexdigest()} for p in sorted(MODEL.glob('glTF*/*')) if p.is_file()}
    return {'revision': PIN, 'license': 'CC-BY-4.0', 'credit': 'Ed Mackey / Analytical Graphics, Inc., 2021',
            'files': files, 'forms': forms, 'targetNames': mesh['extras']['targetNames'],
            'meshWeights': mesh['weights'], 'nodeWeights': doc['nodes'][0].get('weights'),
            'primitives': primitives, 'clips': clips, 'values': shared,
            'signedWeights': [-.5, 1.75, -.25, 1.5, -.75, 1.25, -.125, 2]}


if __name__ == '__main__':
    ((pathlib.Path(__file__).resolve().parents[6]/'research/gltf-validation/direct-gpu/models/MorphStressTest') / 'reference.json').write_text(json.dumps(build(), separators=(',', ':')) + '\n')
