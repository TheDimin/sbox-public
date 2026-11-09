#!/usr/bin/env python3
"""Check isolated 512x375 canonical capture against its authored marker pixels.

This is an image-space check, independent of the engine's UV reference code.
It does not approve color, lighting, filtering, or the entire image.
"""
import argparse
import hashlib
import json
import math
from pathlib import Path
from PIL import Image

parser = argparse.ArgumentParser()
parser.add_argument('image', type=Path)
parser.add_argument('--orthographic-height', type=float, default=2.933333333333334)
args = parser.parse_args()
image = Image.open(args.image).convert('RGB')
if image.size != (512, 375): raise ValueError('Expected the pinned canonical 512x375 camera')

def color(label, pixel):
    r, g, b = pixel
    return {'green': g > 100 and g > 2 * r and g > 2 * b,
            'red': r > 100 and r > 2 * g and r > 2 * b,
            'yellow': min(r, g) > 100 and min(r, g) > 2 * b}[label]

def pixels(box, predicate):
    ratio = 2.933333333333334 / args.orthographic_height
    # Original boxes record the canonical camera's unchanged target and front
    # axis. A different inferred orthographic extent rescales their locations.
    box = (max(0, math.floor(256 + (box[0] - 256) * ratio)),
           max(0, math.floor(187.5 + (box[1] - 187.5) * ratio)),
           min(512, math.ceil(256 + (box[2] - 256) * ratio)),
           min(375, math.ceil(187.5 + (box[3] - 187.5) * ratio)))
    x0, y0, x1, y1 = box
    return [(x, y) for y in range(y0, y1) for x in range(x0, x1) if predicate(image.getpixel((x, y)))]

def distance(arrow_box, marker_box, marker_color):
    arrow = pixels(arrow_box, lambda p: max(p) < 45)
    marker = pixels(marker_box, lambda p: color(marker_color, p))
    if len(arrow) < 100 or len(marker) < 20: raise ValueError('Missing arrow or authored marker pixels')
    x = sum(p[0] for p in marker) / len(marker)
    y = sum(p[1] for p in marker) / len(marker)
    return min(math.hypot(p[0] - x, p[1] - y) for p in arrow)

rows = [
    ('rotation', (51, 188, 178, 316), (96, 308, 115, 326), [('red', (172, 232, 191, 251)), ('yellow', (140, 278, 160, 299))]),
    ('scale', (194, 188, 319, 316), (250, 246, 266, 264), [('yellow', (280, 277, 302, 299))]),
    ('all', (335, 188, 462, 316), (379, 276, 396, 293), []),
]
results = []
for name, arrow_box, green_box, negatives in rows:
    green = distance(arrow_box, green_box, 'green')
    wrong = {label: distance(arrow_box, box, label) for label, box in negatives}
    passed = green < 22 * 2.933333333333334 / args.orthographic_height and all(green < other for other in wrong.values())
    results.append({'quad': name, 'green_distance_pixels': green, 'negative_marker_distances': wrong, 'passed': passed})
print(json.dumps({'path': str(args.image.resolve()), 'sha256': hashlib.sha256(args.image.read_bytes()).hexdigest(),
                  'checks': results, 'passed': all(r['passed'] for r in results),
                  'scope': 'Canonical camera arrow tip proximity to authored markers; user visual approval remains pending.'}, indent=2))
raise SystemExit(0 if all(r['passed'] for r in results) else 1)
