#!/usr/bin/env python3
"""Independent source/image oracle audit. No engine, GPU or image edits."""
import hashlib
import json
import struct
import subprocess
import sys
from pathlib import Path
from PIL import Image

root = Path(sys.argv[1]).resolve() if len(sys.argv) > 1 else Path(__file__).resolve().parents[6]
corpus = root / 'Temp/gltf-reference/glTF-Sample-Assets'
revision = subprocess.check_output(['git', '-C', str(corpus), 'rev-parse', 'HEAD'], text=True).strip()
assert revision == '90d7ede14c7e280af263824604b427a1ca02cb66'
model = corpus / 'Models/AlphaBlendModeTest'
j = json.loads((model / 'glTF/AlphaBlendModeTest.gltf').read_text())
b = (model / 'glTF/AlphaBlendModeTest.bin').read_bytes()
assert [p.name for p in model.iterdir() if p.is_dir() and p.name.startswith('glTF')] == ['glTF', 'glTF-Binary'] or set(p.name for p in model.iterdir() if p.is_dir() and p.name.startswith('glTF')) == {'glTF', 'glTF-Binary'}
assert len(j['materials']) == 6 and len(j['meshes']) == 9
assert [m.get('alphaMode', 'OPAQUE') for m in j['materials']] == ['OPAQUE', 'BLEND', 'MASK', 'MASK', 'MASK', 'OPAQUE']
assert [m.get('alphaCutoff', .5) for m in j['materials']] == [.5, .5, .25, .75, .5, .5]
assert [m.get('doubleSided', False) for m in j['materials']] == [False, True, True, True, True, True]
assert all('COLOR_0' not in m['primitives'][0]['attributes'] for m in j['meshes'])
image = Image.open(model / 'glTF/AlphaBlendLabels.png')
assert image.mode == 'RGBA' and image.size == (1024, 1024)
probes = []
for y, alpha in zip([256,384,512,640,768], [45,87,128,169,210]):
    rgba = image.getpixel((512,y))
    assert rgba == (223,229,71,alpha)
    probes.append({'texel': [512,y], 'rgba': rgba, 'uv_texel_center': [(512.5)/1024,(y+.5)/1024], 'linear_alpha': alpha/255,
                   'coverage_opaque_blend_mask25_mask50_mask75': [1,alpha/255,int(alpha/255 >= .25),int(alpha/255 >= .5),int(alpha/255 >= .75)]})
# Separately establish nonzero unpremultiplied RGB at alpha zero.
zero_alpha = [(x,y,image.getpixel((x,y))) for y in range(0,1024,8) for x in range(0,1024,8) if image.getpixel((x,y))[3] == 0 and max(image.getpixel((x,y))[:3]) > 100]
assert zero_alpha
print(json.dumps({'scope':'independent pinned JSON/PNG source audit only; not an engine test or GPU readback', 'revision':revision,
 'source_forms':['glTF','glTF-Binary'], 'derived_forms':['glTF/compiled_glb','glTF-Binary/compiled_glb'],
 'hashes':{str(p.relative_to(model)):hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(model.glob('glTF*/*')) if p.is_file()},
 'probes':probes,'alpha_zero_nonzero_rgb_example':zero_alpha[0], 'panels':{'meshes':[6,3,0,8,1],'nodes':[8,4,5,7,6], 'materials':[5,1,2,4,3]},
 'license':'CC-BY-4.0; Ed Mackey / Analytical Graphics, Inc.'},indent=2))
