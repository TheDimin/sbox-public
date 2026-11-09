#!/usr/bin/env python3
"""Extract an explicitly derived native test fixture, not loader extension support.

Preserve source images, authored mip levels, and light metadata. RGBD expansion
uses Babylon's documented fromRGBD gamma-2.2 implementation. Cube directions
are permuted from glTF (X,Y,Z) to engine (Z,X,Y); the authored Y rotation stays
separate as native Z yaw. No source model/image bytes are changed.
"""
import argparse
import hashlib
import io
import json
from pathlib import Path

import numpy as np
from PIL import Image

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[5]
SOURCE = ROOT / 'Temp/gltf-reference/glTF-Sample-Assets/Models/EnvironmentTest/glTF-IBL/EnvironmentTest.gltf'
SOURCE_SHA = '43bb563c795f299fd14a2c0f3127f921768a7be1510f41310a6b046fde723679'


def sha(data):
    return hashlib.sha256(data).hexdigest()


def expand_rgbd(pixels):
    pixels = np.asarray(pixels)
    if pixels.dtype != np.uint8 or pixels.ndim != 3 or pixels.shape[-1] not in (3, 4):
        raise ValueError('Require RGB/RGBA source PNG bytes')
    alpha = pixels[..., 3:4].astype(np.float32) / 255 if pixels.shape[-1] == 4 else np.ones((*pixels.shape[:2], 1), np.float32)
    if np.any(alpha <= 0):
        raise ValueError('RGBD divisor must be positive')
    return (np.power(pixels[..., :3].astype(np.float32) / 255, np.float32(2.2)) / alpha).astype(np.float32)


def directions(face, size):
    coordinate = 2 * (np.arange(size, dtype=np.float64) + .5) / size - 1
    u, v = np.meshgrid(coordinate, coordinate)
    one = np.ones_like(u)
    return np.stack([(one, -v, -u), (-one, -v, u), (u, one, v),
                     (u, -one, -v), (u, -v, one), (-u, -v, -one)][face], axis=-1)


def face_uv(direction):
    direction = np.asarray(direction, dtype=np.float64)
    major = np.abs(direction).argmax(axis=-1)
    positive = np.take_along_axis(direction, major[..., None], axis=-1)[..., 0] > 0
    face = major * 2 + ~positive
    x, y, z = np.moveaxis(direction, -1, 0)
    denominator = np.take_along_axis(np.abs(direction), major[..., None], axis=-1)[..., 0]
    sc = np.select([face == i for i in range(6)], [-z, z, x, x, x, -x])
    tc = np.select([face == i for i in range(6)], [-y, -y, z, -z, -y, -y])
    return face, (sc / denominator + 1) * .5, (tc / denominator + 1) * .5


def engine_faces(source_faces):
    size = source_faces[0].shape[0]
    source = np.stack(source_faces)
    result = []
    for face in range(6):
        engine = directions(face, size)
        gltf = engine[..., [1, 2, 0]]
        source_face, u, v = face_uv(gltf)
        ix = np.clip(np.floor(u * size).astype(int), 0, size - 1)
        iy = np.clip(np.floor(v * size).astype(int), 0, size - 1)
        result.append(source[source_face, iy, ix])
    return result


def extract(output):
    output = output.resolve()
    if output == Path('/tmp') or not output.is_relative_to('/tmp') or output.exists():
        raise ValueError('Require a new directory strictly under /tmp')
    raw = SOURCE.read_bytes()
    if sha(raw) != SOURCE_SHA:
        raise ValueError('Pinned IBL document differs')
    doc = json.loads(raw)
    buffers = [(SOURCE.parent / row['uri']).read_bytes() for row in doc['buffers']]
    if len(buffers) != 1 or sha(buffers[0]) != '7c7a285d3ebd57900023cd56182a07da7ea52acf2b22b1a6320f747290fdf56e':
        raise ValueError('Pinned IBL source buffer differs')
    light = doc['extensions']['EXT_lights_image_based']['lights'][doc['scenes'][doc['scene']]['extensions']['EXT_lights_image_based']['light']]
    source_mips = []
    image_rows = []
    for mip, indices in enumerate(light['specularImages']):
        faces = []
        for face, index in enumerate(indices):
            image = doc['images'][index]
            view = doc['bufferViews'][image['bufferView']]
            offset = view.get('byteOffset', 0)
            encoded = buffers[view['buffer']][offset:offset + view['byteLength']]
            with Image.open(io.BytesIO(encoded)) as decoded:
                expected = light['specularImageSize'] >> mip
                if decoded.size != (expected, expected) or decoded.mode not in ('RGB', 'RGBA'):
                    raise ValueError('Unexpected authored cube image layout')
                values = expand_rgbd(np.asarray(decoded))
                image_rows.append({'mip': mip, 'face': face, 'image': index, 'mode': decoded.mode,
                                   'encodedSha256': sha(encoded), 'minimum': float(values.min()), 'maximum': float(values.max())})
                faces.append(values)
        source_mips.append(faces)
    q = light.get('rotation', [0, 0, 0, 1])
    if abs(q[0]) > 1e-7 or abs(q[2]) > 1e-7:
        raise ValueError('This model fixture supports only its authored Y-axis rotation')
    yaw = float(np.degrees(2 * np.arctan2(q[1], q[3])))
    payload = bytearray()
    samples = []
    for mip in range(len(source_mips) - 1, -1, -1):
        for face, rgb in enumerate(engine_faces(source_mips[mip])):
            rgba = np.concatenate([rgb, np.ones((*rgb.shape[:2], 1), np.float32)], axis=-1).astype('<f4')
            payload.extend(rgba.tobytes())
            samples.append({'face': face, 'mip': mip, 'size': len(rgb), 'firstPixel': rgba[0, 0].tolist(),
                            'centerPixel': rgba[len(rgb) // 2, len(rgb) // 2].tolist(), 'lastPixel': rgba[-1, -1].tolist()})
    output.mkdir(parents=True)
    data_path = output / 'authored-cube-native.rgba32f'
    data_path.write_bytes(payload)
    receipt = {'schemaVersion': 1, 'source': str(SOURCE), 'sourceSha256': SOURCE_SHA,
               'buffers': [{'sourcePath': str(SOURCE.parent / doc['buffers'][index]['uri']), 'sha256': sha(data), 'bytes': len(data)} for index, data in enumerate(buffers)],
               'payload': str(data_path), 'payloadSha256': sha(payload), 'payloadBytes': len(payload),
               'size': light['specularImageSize'], 'mips': len(source_mips), 'format': 'RGBA32323232F',
               'faceOrder': ['+X', '-X', '+Y', '-Y', '+Z', '-Z'], 'layout': 'smallest-mip-first,face0..5,row-major',
               'basis': 'native(Z,X,Y) from glTF(X,Y,Z), implemented as exact cube face permutations/rotations',
               'imageOriginPolicy': 'PNG rows used top-to-bottom with standard cube sc/tc directions; native orientation still requires rendered review',
               'rgbdDecode': 'pow(encodedRGB/255,2.2)/(encodedAlpha/255); RGB images use divisor1, following Babylon fromRGBD',
               'decodeSource': 'https://github.com/BabylonJS/Babylon.js/blob/master/packages/dev/core/src/Shaders/ShadersInclude/helperFunctions.fx',
               'specification': 'https://github.com/KhronosGroup/glTF/tree/main/extensions/2.0/Vendor/EXT_lights_image_based',
               'sourceLight': light, 'nativeYawDegrees': yaw, 'samples': samples, 'sourceImages': image_rows,
               'license': 'Source EnvironmentTest Adobe Stock License preserved; see original model LICENSE.md',
               'capability': 'Derived private-scene fixture only; public glTF optional extension remains unconsumed.',
               'limitations': ['Native SceneCubemap recomputes diffuse SH; authored irradiance coefficients are retained here but not applied.',
                               'Native stock shader roughness-to-LOD mapping may differ from the extension five authored roughness levels.',
                               'No native rendering, orientation match, shader parity, or final visual acceptance established by extraction.']}
    (output / 'fixture.json').write_text(json.dumps(receipt, indent=2) + '\n')
    return receipt


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    result = extract(args.output)
    print(json.dumps({k: result[k] for k in ['payload', 'payloadSha256', 'payloadBytes', 'nativeYawDegrees', 'capability']}, indent=2))
