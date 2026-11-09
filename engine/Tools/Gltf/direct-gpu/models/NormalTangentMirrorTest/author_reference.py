"""Independent bounded raw-source oracle; no producer/evaluator imports.

Derived numeric data: Khronos NormalTangentMirrorTest, CC-BY-4.0. See upstream
LICENSE.md and metadata.json for attribution. This intentionally preserves the
authored tangents instead of recomputing them from UV derivatives.
"""
import hashlib
import json
import math
import struct
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[5]
SOURCE = ROOT / 'Temp/gltf-reference/glTF-Sample-Assets/Models/NormalTangentMirrorTest'
REVISION = '90d7ede14c7e280af263824604b427a1ca02cb66'


def load(path):
    data = path.read_bytes()
    if path.suffix == '.glb':
        assert struct.unpack_from('<III', data) == (0x46546c67, 2, len(data))
        size, kind = struct.unpack_from('<II', data, 12)
        assert kind == 0x4e4f534a
        document = json.loads(data[20:20 + size])
        offset = 20 + size
        size, kind = struct.unpack_from('<II', data, offset)
        assert kind == 0x004e4942
        return document, data[offset + 8:offset + 8 + size]
    document = json.loads(data)
    return document, (path.parent / document['buffers'][0]['uri']).read_bytes()


def accessor(document, data, index):
    a = document['accessors'][index]
    view = document['bufferViews'][a['bufferView']]
    fmt = {5123: 'H', 5126: 'f'}[a['componentType']]
    components = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4}[a['type']]
    stride = view.get('byteStride', components * struct.calcsize(fmt))
    offset = view.get('byteOffset', 0) + a.get('byteOffset', 0)
    return {'component_type': a['componentType'], 'components': components,
            'stride': stride, 'count': a['count'],
            'values': [list(struct.unpack_from('<' + fmt * components, data, offset + i * stride)) for i in range(a['count'])]}


def build():
    forms = [SOURCE / 'glTF/NormalTangentMirrorTest.gltf', SOURCE / 'glTF-Binary/NormalTangentMirrorTest.glb']
    document, data = load(forms[0])
    values = [accessor(document, data, i) for i in range(5)]
    other, binary = load(forms[1])
    assert values == [accessor(other, binary, i) for i in range(5)]
    assert document['nodes'] == [{'mesh': 0, 'name': 'NormalTangentTest_low'}]
    assert len(document['meshes']) == len(document['materials']) == 1
    assert values[1]['count'] == 2770 and values[0]['count'] == 15720
    normals, tangents = values[2]['values'], values[3]['values']
    assert sum(t[3] == -1 for t in tangents) == 80
    assert sum(t[3] == 1 for t in tangents) == 2690
    normal_error = max(abs(sum(x*x for x in n)-1) for n in normals)
    # Supplied Blender export directions are not perfectly orthogonal. The
    # independent GPU oracle orthogonalizes only after affine transformation.
    dots = [abs(sum(n[i]*t[i] for i in range(3))) for n,t in zip(normals,tangents)]
    files = {}
    for path in sorted(SOURCE.rglob('*')):
        if path.is_file() and path.parent.name in ('glTF', 'glTF-Binary'):
            raw = path.read_bytes()
            files[str(path.relative_to(SOURCE))] = {'length': len(raw), 'sha256': hashlib.sha256(raw).hexdigest()}
    return {'revision': REVISION, 'attribution': 'Khronos NormalTangentMirrorTest / CC-BY-4.0; upstream metadata.json and LICENSE.md',
            'files': files, 'accessors': values, 'material': document['materials'][0],
            'max_authored_normal_length_squared_error': normal_error,
            'max_authored_normal_tangent_dot': max(dots), 'negative_w_vertices': 80,
            'bounds_source_meters': [document['accessors'][1]['min'], document['accessors'][1]['max']]}


if __name__ == '__main__':
    target = (Path(__file__).resolve().parents[6]/'research/gltf-validation/direct-gpu/models/NormalTangentMirrorTest') / 'reference.json'
    target.write_text(json.dumps(build(), separators=(',', ':')) + '\n')
    print(target, target.stat().st_size)
