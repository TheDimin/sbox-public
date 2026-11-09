#!/usr/bin/env python3
"""Independent stdlib pinned source audit; no producer or native decoder imports."""
import hashlib, json, pathlib, struct
ROOT = pathlib.Path(__file__).resolve().parents[6]
MODEL = ROOT / 'Temp/gltf-reference/glTF-Sample-Assets/Models/AnisotropyBarnLamp'
def sha(data): return hashlib.sha256(data).hexdigest()
def inspect(path):
    data = path.read_bytes()
    if path.suffix == '.glb':
        n = struct.unpack_from('<I', data, 12)[0]
        doc = json.loads(data[20:20+n]); binary = data[28+n:]
    else:
        doc = json.loads(data); binary = (path.parent / doc['buffers'][0]['uri']).read_bytes()
    result = {'variant': path.parent.name, 'file': path.name, 'sha256': sha(data), 'primitives': [], 'images': []}
    for mesh in doc['meshes']:
        primitive = mesh['primitives'][0]; rows = {}
        for semantic, accessor in list(primitive['attributes'].items()) + [('INDICES', primitive['indices'])]:
            a = doc['accessors'][accessor]; view = doc['bufferViews'][a['bufferView']]
            components = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4}[a['type']]
            element = components * {5123: 2, 5126: 4}[a['componentType']]
            start = view.get('byteOffset', 0) + a.get('byteOffset', 0); stride = view.get('byteStride', element)
            logical = b''.join(binary[start+i*stride:start+i*stride+element] for i in range(a['count']))
            rows[semantic] = {'count': a['count'], 'components': components, 'componentType': a['componentType'], 'sha256': sha(logical)}
        result['primitives'].append({'material': primitive['material'], 'streams': rows})
    for image in doc['images']:
        if 'uri' in image: encoded = (path.parent / image['uri']).read_bytes()
        else:
            v = doc['bufferViews'][image['bufferView']]; encoded = binary[v.get('byteOffset', 0):v.get('byteOffset', 0)+v['byteLength']]
        row = {'sha256': sha(encoded), 'length': len(encoded), 'mimeType': 'image/ktx2' if encoded.startswith(b'\xabKTX 20') else 'image/png'}
        if row['mimeType'] == 'image/ktx2':
            h = struct.unpack_from('<13I2Q', encoded, 12); dfd = encoded[h[9]:h[9]+h[10]]; kvd = encoded[h[11]:h[11]+h[12]]
            row['ktx'] = {'width': h[2], 'height': h[3], 'levels': h[7], 'supercompression': h[8], 'dfdSha256': sha(dfd), 'kvdSha256': sha(kvd), 'colorModel': dfd[12], 'primaries': dfd[13], 'transfer': dfd[14]}
        result['images'].append(row)
    return result
if __name__ == '__main__':
    data = {'corpusCommit': '90d7ede14c7e280af263824604b427a1ca02cb66', 'license': 'CC-BY-4.0', 'credit': 'Eric Chadwick; Copyright 2023 Wayfair, LLC', 'variants': [inspect(MODEL/v/('AnisotropyBarnLamp.glb' if v == 'glTF-Binary' else 'AnisotropyBarnLamp.gltf')) for v in ['glTF', 'glTF-Binary', 'glTF-KTX-BasisU']]}
    destination = (pathlib.Path(__file__).resolve().parents[6]/'research/gltf-validation/direct-gpu/models/AnisotropyBarnLamp') / 'source-audit.json'; destination.write_text(json.dumps(data, indent=2)+'\n')
    print(destination)
