#!/usr/bin/env python3
"""Small independent JSON/BIN/GLB source oracle; never imports a producer evaluator."""
import base64
import hashlib
import json
from pathlib import Path
import struct
import sys

PIN = '90d7ede14c7e280af263824604b427a1ca02cb66'
HERE = Path(__file__).resolve().parent
COMPONENTS = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4}
FORMATS = {5121: 'B', 5123: 'H', 5126: 'f'}


def digest(value):
    return hashlib.sha256(value).hexdigest()


def load(path):
    source = path.read_bytes()
    if source[:4] == b'glTF':
        magic, version, length = struct.unpack_from('<III', source)
        assert version == 2 and length == len(source)
        size, kind = struct.unpack_from('<II', source, 12)
        assert kind == 0x4E4F534A
        root = json.loads(source[20:20+size])
        offset = 20+size
        size, kind = struct.unpack_from('<II', source, offset)
        assert kind == 0x004E4942
        binary = source[offset+8:offset+8+size]
    else:
        root = json.loads(source)
        binary = (path.parent / root['buffers'][0]['uri']).read_bytes()
    return source, root, binary


def accessor(root, binary, index):
    a = root['accessors'][index]
    v = root['bufferViews'][a['bufferView']]
    components = COMPONENTS[a['type']]
    fmt = '<'+FORMATS[a['componentType']]*components
    packed = struct.calcsize(fmt)
    stride, offset = v.get('byteStride', packed), a.get('byteOffset', 0)
    view = binary[v.get('byteOffset', 0):v.get('byteOffset', 0)+v['byteLength']]
    values = [list(struct.unpack_from(fmt, view, offset+i*stride)) for i in range(a['count'])]
    return dict(accessor=index, view=a['bufferView'], viewOffset=v.get('byteOffset', 0), offset=offset, stride=stride,
                count=a['count'], componentType=a['componentType'], components=components, normalized=a.get('normalized', False),
                viewLength=len(view), viewSha256=digest(view), values=values)


def author(checkout):
    corpus = checkout/'Temp/gltf-reference/glTF-Sample-Assets/Models/MorphPrimitivesTest'
    decoded = json.loads(((Path(__file__).resolve().parents[6]/'research/gltf-validation/direct-gpu/models/MorphPrimitivesTest') / 'draco-reference.json').read_text())
    assert decoded['pin'] == PIN
    forms = {}
    for variant in ('glTF', 'glTF-Binary', 'glTF-Draco'):
        path = corpus/variant/('MorphPrimitivesTest.glb' if variant == 'glTF-Binary' else 'MorphPrimitivesTest.gltf')
        source, root, binary = load(path)
        if variant == 'glTF-Draco':
            assert digest(source) == decoded['sourceSha256'] and digest(binary) == decoded['bufferSha256']
        assert root['scene'] == 0 and root['meshes'][0]['weights'] == [0.5] and root['nodes'][0]['children'] == [1]
        assert root['nodes'][1]['mesh'] == 0 and 'weights' not in root['nodes'][1]
        assert len(root['meshes']) == 1 and len(root['meshes'][0]['primitives']) == 2
        files = {p.name: dict(length=p.stat().st_size, sha256=digest(p.read_bytes())) for p in sorted(path.parent.iterdir()) if p.is_file()}
        primitives = []
        for pi, p in enumerate(root['meshes'][0]['primitives']):
            assert p.get('mode', 4) == 4 and len(p['targets']) == 1 and list(p['targets'][0]) == ['POSITION']
            morph = accessor(root, binary, p['targets'][0]['POSITION'])
            if variant == 'glTF-Draco':
                ref = decoded['primitives'][pi]
                attributes = {}
                for semantic, data in ref['attributes'].items():
                    raw = base64.b64decode(data['rawBase64'], validate=True)
                    assert digest(raw) == data['rawSha256']
                    a = root['accessors'][p['attributes'][semantic]]
                    c = COMPONENTS[a['type']]
                    assert data['count'] == a['count'] and data['components'] == c
                    attributes[semantic] = dict(accessor=p['attributes'][semantic], componentType=data['componentType'], components=c,
                        count=a['count'], normalized=False, offset=0, stride=c*4, viewLength=len(raw), viewSha256=digest(raw),
                        values=[data['values'][i*c:(i+1)*c] for i in range(a['count'])], materializedDraco=True)
                indices = ref['indices']
            else:
                attributes = {s: accessor(root, binary, i) for s, i in p['attributes'].items()}
                indices = [x[0] for x in accessor(root, binary, p['indices'])['values']]
            assert len(attributes['POSITION']['values']) == (21, 9)[pi] and len(indices) == (72, 24)[pi]
            assert len(morph['values']) == attributes['POSITION']['count']
            primitives.append(dict(primitive=pi, material=p['material'], attributes=attributes, indices=indices, morph=morph))
        forms[variant] = dict(source=path.name, sourceSha256=digest(source), files=files, primitives=primitives)
    result = dict(version=1, pin=PIN, authorSha256=digest(Path(__file__).read_bytes()), decoder=decoded['decoder'],
                  license='CC-BY-4.0; model by ft-lab; upstream LICENSE.md retained in pinned corpus', forms=forms,
                  scope='Independent raw source geometry/morph oracle. Draco uses a separate official decoder integration with shared codec ancestry. No engine/GPU/image pass claim.')
    out = (Path(__file__).resolve().parents[6]/'research/gltf-validation/direct-gpu/models/MorphPrimitivesTest') / 'reference.json'
    out.write_text(json.dumps(result, separators=(',', ':'), allow_nan=False)+'\n')
    return dict(path=str(out), sha256=digest(out.read_bytes()), forms=len(forms), vertices=30, triangles=32)


if __name__ == '__main__':
    print(json.dumps(author(Path(sys.argv[1]).resolve(strict=True))))
