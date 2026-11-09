"""Independent stdlib oracle from the pinned asset's real uncompressed fallback.

No Sandbox evaluator or meshoptimizer decoder contributes expected values. The
fallback is an explicit upstream representation, not an invented source variant.
Run only this small authoring/check script; engine execution belongs to coordinator.
"""
import hashlib
import json
import pathlib
import struct

HERE = pathlib.Path(__file__).resolve().parent
ROOT = HERE.parents[4]
MODEL = ROOT / 'Temp/gltf-reference/glTF-Sample-Assets/Models/MeshoptCubeTest'
REVISION = '90d7ede14c7e280af263824604b427a1ca02cb66'
TYPES = {5120: ('b', 1), 5121: ('B', 1), 5122: ('h', 2), 5123: ('H', 2), 5125: ('I', 4), 5126: ('f', 4)}
COMPONENTS = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4}


def digest(data):
    return hashlib.sha256(data).hexdigest()


def values(accessor, view, data):
    code, size = TYPES[accessor['componentType']]
    count = COMPONENTS[accessor['type']]
    offset = accessor.get('byteOffset', 0)
    stride = view.get('byteStride', count * size)
    rows = [list(struct.unpack_from('<' + code * count, data, offset + i * stride)) for i in range(accessor['count'])]
    if accessor.get('normalized'):
        maximum = {5120: 127, 5121: 255, 5122: 32767, 5123: 65535}[accessor['componentType']]
        rows = [[max(v / maximum, -1) for v in row] for row in rows]
    return rows


def build():
    source = MODEL / 'glTF'
    document = json.loads((source / 'MeshoptCubeTest.gltf').read_text())
    buffers = [(source / b['uri']).read_bytes() for b in document['buffers']]
    views = [buffers[v['buffer']][v.get('byteOffset', 0):v.get('byteOffset', 0) + v['byteLength']] for v in document['bufferViews']]
    accessors = []
    for i, accessor in enumerate(document['accessors']):
        view = document['bufferViews'][accessor['bufferView']]
        accessors.append(dict(index=i, view=accessor['bufferView'], offset=accessor.get('byteOffset', 0),
                              stride=view.get('byteStride', COMPONENTS[accessor['type']] * TYPES[accessor['componentType']][1]),
                              component_type=accessor['componentType'], components=COMPONENTS[accessor['type']], count=accessor['count'],
                              normalized=accessor.get('normalized', False), values=values(accessor, view, views[accessor['bufferView']])) )
    forms = {}
    for variant in ['glTF', 'glTF-Meshopt']:
        directory = MODEL / variant
        actual = json.loads((directory / 'MeshoptCubeTest.gltf').read_text())
        files = {}
        for path in sorted(directory.iterdir()):
            if path.is_file():
                data = path.read_bytes()
                files[path.name] = dict(length=len(data), sha256=digest(data))
        # Both actual formats share layouts and compressed bytes; only the
        # required declaration and fallback URI differ.
        assert actual['accessors'] == document['accessors']
        assert actual['bufferViews'] == document['bufferViews']
        forms[variant] = dict(source='MeshoptCubeTest.gltf', files=files, required=actual['extensionsRequired'])
    compression = []
    for i, view in enumerate(document['bufferViews']):
        extension = view.get('extensions', {}).get('KHR_meshopt_compression')
        if extension:
            encoded = buffers[extension['buffer']][extension.get('byteOffset', 0):extension.get('byteOffset', 0) + extension['byteLength']]
            compression.append(dict(view=i, mode=extension['mode'], filter=extension.get('filter', 'NONE'), header=encoded[0],
                                    stride=extension['byteStride'], count=extension['count'], encoded_sha256=digest(encoded)))
    return dict(revision=REVISION, expected_provenance='Pinned upstream uncompressed fallback decoded using independent Python struct; no producer evaluation.',
                forms=forms, views=[dict(length=len(data), sha256=digest(data)) for data in views], accessors=accessors,
                compression=compression, nodes=document['nodes'], meshes=document['meshes'], animations=document['animations'],
                materials=document['materials'], textures=document['textures'], samplers=document['samplers'], images=document['images'])


if __name__ == '__main__':
    output = json.dumps(build(), indent=2, allow_nan=False) + '\n'
    ((pathlib.Path(__file__).resolve().parents[6]/'research/gltf-validation/direct-gpu/models/MeshoptCubeTest') / 'reference.json').write_text(output)
    print('reference.json', digest(output.encode()), len(output.encode()))
