#!/usr/bin/env python3
"""Audit seven spatially disjoint primitive regions in native capture receipts."""
import json, sys
from pathlib import Path
from PIL import Image

receipt = json.loads(Path(sys.argv[1]).read_text())
rows = receipt['measurements'][0]['value']
regions = [(64, 0, 128, 64), (0, 64, 64, 128), (64, 64, 128, 128), (128, 64, 192, 128), (0, 128, 64, 192), (64, 128, 128, 192), (128, 128, 192, 192)]
result = []
for entry in rows:
    if 'input_sha256' not in entry: continue
    frames = {f['label']: f for f in entry['frames']}
    names = ['public_source_material', 'independent_literal_reference', 'wrong_topology_reference']
    images = [Image.open(frames[n]['Path']).convert('RGB') for n in names]
    assert all(im.size == (192,192) for im in images)
    per_mode = []
    for mode, (x0,y0,x1,y1) in enumerate(regions):
        masks = [[max(im.getpixel((x,y))) > 7.65 for y in range(y0,y1) for x in range(x0,x1)] for im in images]
        a,b,n = masks
        row = {'mode':mode,'actual':sum(a),'reference':sum(b),'negative':sum(n),'actual_reference_differing_pixels':sum(x!=y for x,y in zip(a,b)), 'reference_negative_differing_pixels':sum(x!=y for x,y in zip(b,n))}
        assert row['actual'] > 0 and row['reference'] > 0 and row['actual_reference_differing_pixels'] == 0 and row['reference_negative_differing_pixels'] > 0, row
        per_mode.append(row)
    result.append({'compiled':entry['compiled'],'regions':per_mode,'scope':'Foreground only in seven fixed spatial regions, Lit camera; negative point/line geometry is degenerate and may be culled. No independent point size, material or full visual parity claim.'})
print(json.dumps(result,indent=2))
