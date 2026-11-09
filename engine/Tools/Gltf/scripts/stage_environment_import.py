#!/usr/bin/env python3
"""Coordinator-owned *guarded* staging of the Artist Workshop native cubemap.

This writes to the editor-watched game/core tree, which can start asynchronous
asset compilation. Run only after coordinator resource admission and hold the
same foreground admission through compiler completion. It never overwrites an
existing asset and deliberately leaves staged inputs for inspection on failure.
"""
import argparse
import hashlib
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[4]
FIXTURE = ROOT / 'research/gltf-validation/fixtures/environment'
HDR_HASH = 'f6da9025acadb1149c973fe2bef53d55f4e7144285a87ec1a357520fbcad4767'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--token', required=True, help='Unique lowercase 32-character hex token')
    parser.add_argument('--admitted', action='store_true', help='Coordinator confirms resource guard is held through native asset compilation')
    args = parser.parse_args()
    if not args.admitted or not re.fullmatch(r'[0-9a-f]{32}', args.token):
        parser.error('Require --admitted and a 32-character lowercase hexadecimal token')
    hdr = (FIXTURE / 'artist_workshop_1k.hdr').read_bytes()
    if hashlib.sha256(hdr).hexdigest() != HDR_HASH:
        raise RuntimeError('Original HDR fixture hash mismatch')
    template = (FIXTURE / 'artist_workshop_1k.vtex').read_text()
    old = 'research/gltf-validation/fixtures/environment/artist_workshop_1k.hdr'
    if template.count(old) != 1:
        raise RuntimeError('Unexpected vtex source reference')
    relative = f'materials/codex_gltf_ibl_{args.token}/artist_workshop_1k'
    vtex = template.replace(old, relative + '.hdr').encode()
    directory = ROOT / 'game/core' / Path(relative).parent
    directory.mkdir(parents=False, exist_ok=False)
    hdr_path = directory / 'artist_workshop_1k.hdr'
    vtex_path = directory / 'artist_workshop_1k.vtex'
    # Publish HDR first and asset definition last; the watcher may react immediately.
    with hdr_path.open('xb') as stream:
        stream.write(hdr)
    with vtex_path.open('xb') as stream:
        stream.write(vtex)
    print(json.dumps({'source': str(hdr_path), 'sourceSha256': HDR_HASH,
                      'vtex': str(vtex_path), 'vtexSha256': hashlib.sha256(vtex).hexdigest(),
                      'resource': relative + '.vtex',
                      'status': 'staged only: await editor AssetSystem compilation and verify compiled texture, identity and rendered IBL'}, indent=2))


if __name__ == '__main__':
    main()
