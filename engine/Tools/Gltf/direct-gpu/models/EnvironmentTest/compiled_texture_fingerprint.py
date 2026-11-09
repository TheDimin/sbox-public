#!/usr/bin/env python3
"""Bounded Source2 texture DATA/stream fingerprints; not resident GPU proof."""
import argparse
import hashlib
import json
from pathlib import Path
import struct


def fingerprint(path):
    path = path.resolve(strict=True)
    raw = path.read_bytes()
    if not 16 <= len(raw) <= 64 * 1024 * 1024:
        raise ValueError('Expected a bounded compiled texture')
    nonstreaming, version, resource_version, relative, count = struct.unpack_from('<IHH i I', raw)
    table = 8 + relative
    if version != 12 or not 16 <= nonstreaming <= len(raw) or not 1 <= count <= 64 or table < 16 or table + count * 12 > nonstreaming:
        raise ValueError('Invalid resource header/block table')
    sha = lambda value: hashlib.sha256(value).hexdigest()
    blocks = []
    for index in range(count):
        position = table + index * 12
        label = raw[position:position + 4].decode('ascii')
        offset, size = struct.unpack_from('<iI', raw, position + 4)
        start = position + 4 + offset
        if start < table + count * 12 or start + size > nonstreaming:
            raise ValueError('Resource block exceeds nonstreaming boundary')
        blocks.append({'type': label, 'bytes': size, 'sha256': sha(raw[start:start + size])})
    if sum(row['type'] == 'DATA' for row in blocks) != 1:
        raise ValueError('Expected exactly one texture DATA block')
    return {'path': str(path), 'wholeFileSha256': sha(raw), 'resourceVersion': resource_version,
            'nonstreamingBytes': nonstreaming, 'streamingBytes': len(raw) - nonstreaming,
            'streamingSha256': sha(raw[nonstreaming:]), 'blocks': blocks,
            'scope': 'Disk block/stream identity only. Future rebuilds can distinguish metadata changes from texture DATA/stream changes; no historical or resident-GPU equivalence implied.'}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('path', type=Path)
    args = parser.parse_args()
    print(json.dumps(fingerprint(args.path), indent=2))
