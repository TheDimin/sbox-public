#!/usr/bin/env python3
"""Independent authored border/white landmarks in the unchanged canonical camera.

This measures contrast, not lighting or whole-model visual approval. It reads
the pinned PNG with Pillow and computes texture coordinates with scalar math;
it does not call the engine UV, sampler or material implementation.
"""
import argparse
import hashlib
import json
import math
from pathlib import Path
from statistics import median

from PIL import Image

ROOT = Path(__file__).resolve().parents[6]
SOURCE = ROOT / 'Temp/gltf-reference/glTF-Sample-Assets/Models/TextureTransformTest/glTF'
ARROW_SHA = '5df5b251fed0ac306cf859a30b59a4953d152483a4bed2b84e5bba1c667a1e64'


def expected(u, v, material):
    if material == 3:
        a, su, sv, ou, ov = math.pi / 8, 1, 1, 0, 0
    elif material == 4:
        a, su, sv, ou, ov = 0, 1.5, 1.5, 0, 0
    else:
        a, su, sv, ou, ov = .3, 1.5, 1.5, -.2, -.1
    return ou + math.cos(a) * u * su + math.sin(a) * v * sv, ov - math.sin(a) * u * su + math.cos(a) * v * sv


def audit(path, orthographic_height=2.933333333333334):
    raw = (SOURCE / 'Arrow.png').read_bytes()
    if hashlib.sha256(raw).hexdigest() != ARROW_SHA:
        raise ValueError('Pinned Arrow.png changed')
    arrow = Image.open(SOURCE / 'Arrow.png').convert('RGBA')
    if arrow.getchannel('A').getextrema() != (255, 255):
        raise ValueError('Pinned arrow opacity changed')
    if set(arrow.getpixel((x, y)) for y in range(128) for x in range(128) if x in (0, 127) or y in (0, 127)) != {(224, 224, 224, 255)}:
        raise ValueError('Authored gray clamp edge changed')
    image = Image.open(path).convert('RGB')
    if image.size != (512, 375):
        raise ValueError('Expected unchanged canonical 512x375 camera')
    scale = 375 / orthographic_height
    rows = []
    for material, center in [(3, -1.1), (4, 0), (5, 1.1)]:
        samples = {'gray': [], 'white': []}
        for y in range(375):
            wy = -.045633742881207695 + (187.5 - y - .5) / scale
            v = .5 - (wy + .55)
            if not .06 < v < .94:
                continue
            for x in range(512):
                wx = (x + .5 - 256) / scale
                u = wx - center + .5
                if not .06 < u < .94:
                    continue
                # Exclude every authored correctness/error marker rectangle.
                if any(abs(wx - mx) < size / 2 + .03 and abs(wy - my) < size / 2 + .03 for mx, my, size in [
                    (-1.1790482244, -1.0662674858, .15), (-.8221825407, -.8278174593, .15),
                    (-.5837325142, -.4709517756, .15), (.0185449729, -.5685449729, .1),
                    (.2778174593, -.8278174593, .15), (1.03, -.8, .1)]):
                    continue
                tu, tv = expected(u, v, material)
                # Stay away from the filter transition. Far outside the square
                # is solid authored gray under CLAMP_TO_EDGE, never transparency.
                if tu < -.03 or tu > 1.03 or tv < -.03 or tv > 1.03:
                    kind = 'gray'
                elif .03 < tu < .97 and .03 < tv < .97:
                    tx, ty = int(tu * 128), int(tv * 128)
                    if any(arrow.getpixel((xx, yy)) != (255, 255, 255, 255) for yy in range(max(0, ty - 2), min(128, ty + 3)) for xx in range(max(0, tx - 2), min(128, tx + 3))):
                        continue
                    kind = 'white'
                else:
                    continue
                samples[kind].append(image.getpixel((x, y)))
        if min(map(len, samples.values())) < 100:
            raise ValueError('Insufficient independent gray/white landmarks')
        values = {k: [median(p[c] for p in pixels) for c in range(3)] for k, pixels in samples.items()}
        contrast = min(values['white'][c] - values['gray'][c] for c in range(3))
        rows.append({'material': material, 'landmarkCounts': {k: len(v) for k, v in samples.items()}, 'medianRgb': values,
                     'minimumChannelContrast': contrast, 'grayWhiteSeparationVisible': contrast >= 8})
    return {'image': str(path.resolve()), 'sha256': hashlib.sha256(path.read_bytes()).hexdigest(), 'orthographicHeightMetres': orthographic_height,
            'authoredArrow': {'sha256': ARROW_SHA, 'alphaRange': [255, 255], 'clampedEdgeRgba': [224, 224, 224, 255], 'interiorRgba': [255, 255, 255, 255]},
            'quads': rows, 'passed': all(row['grayWhiteSeparationVisible'] for row in rows),
            'scope': 'Independent opaque authored border/white contrast at transformed-UV landmarks; lighting, filtering and user visual approval remain separate.'}


def audit_material_grid(path):
    image = Image.open(path).convert('RGB')
    if image.size != (512, 375):
        raise ValueError('Expected 512x375 material control')
    values = []
    for i in range(4):
        cx = round(256 + (-.6 if i % 2 == 0 else .6) * 375 / 2.4)
        cy = round(187.5 - (.6 if i < 2 else -.6) * 375 / 2.4)
        values.append([median(image.getpixel((x, y))[c] for y in range(cy - 5, cy + 6) for x in range(cx - 5, cx + 6)) for c in range(3)])
    errors = [max(abs(a - b) for a, b in zip(values[i], values[i + 2])) for i in range(2)]
    contrast = min(values[1][c] - values[0][c] for c in range(3))
    return {'image': str(path.resolve()), 'sha256': hashlib.sha256(path.read_bytes()).hexdigest(),
            'medianRgb': dict(zip(['clamped224Texture', 'whiteTexture', 'linearFactorFor224', 'whiteFactor'], values)),
            'textureFactorMaximumChannelErrors': errors, 'textureGrayWhiteContrast': contrast,
            'passed': max(errors) <= 4 and contrast >= 8,
            'scope': 'Opaque clamped source Arrow.png gray/white versus independently sRGB-decoded linear uniform factors under identical native lighting; not a general mip/filter or visual approval.'}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('image', type=Path)
    parser.add_argument('--orthographic-height', type=float, default=2.933333333333334)
    parser.add_argument('--material-grid', action='store_true')
    args = parser.parse_args()
    result = audit_material_grid(args.image) if args.material_grid else audit(args.image, args.orthographic_height)
    print(json.dumps(result, indent=2))
    raise SystemExit(0 if result['passed'] else 1)
