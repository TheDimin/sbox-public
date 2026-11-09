#!/usr/bin/env python3
"""Losslessly transcode RGBE to 32-bit linear EXR and verify every sample.

Run only inside the coordinator's resource guard. Output must be a new /tmp file.
OIIO's Radiance decoder yields linear RGB float samples; no display transform.
"""
import hashlib
import json
from pathlib import Path
import sys

import numpy as np
import OpenImageIO as oiio


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def read(path):
    stream = oiio.ImageInput.open(str(path))
    if stream is None:
        raise RuntimeError(f'Cannot open {path}: {oiio.geterror()}')
    try:
        spec = stream.spec()
        data = stream.read_image(oiio.FLOAT)
        if data is None:
            raise RuntimeError(f'Cannot read {path}: {stream.geterror()}')
        return spec, np.asarray(data, dtype=np.float32)
    finally:
        stream.close()


def main():
    if len(sys.argv) != 3:
        raise SystemExit('Usage: transcode_environment_exr.py input.hdr /tmp/new-file.exr')
    source = Path(sys.argv[1]).resolve(strict=True)
    target = Path(sys.argv[2]).resolve()
    if target.exists() or not target.is_relative_to('/tmp') or target.suffix != '.exr':
        raise ValueError('Target must be a new EXR strictly under /tmp')
    if sha(source) != 'f6da9025acadb1149c973fe2bef53d55f4e7144285a87ec1a357520fbcad4767':
        raise ValueError('HDR source digest differs')
    spec, data = read(source)
    if (spec.width, spec.height, spec.nchannels) != (1024, 512, 3) or not np.isfinite(data).all() or float(data.max()) <= 1:
        raise ValueError('Expected finite, unclipped 1024x512 linear HDR RGB')
    out_spec = oiio.ImageSpec(spec.width, spec.height, 3, oiio.FLOAT)
    out_spec.attribute('compression', 'zip')
    output = oiio.ImageOutput.create(str(target))
    if output is None or not output.open(str(target), out_spec):
        raise RuntimeError('EXR output unavailable: ' + oiio.geterror())
    try:
        if not output.write_image(np.ascontiguousarray(data)):
            raise RuntimeError('EXR write failed: ' + output.geterror())
    finally:
        output.close()
    decoded_spec, decoded = read(target)
    if (decoded_spec.width, decoded_spec.height, decoded_spec.nchannels) != (1024, 512, 3) or not np.array_equal(data, decoded):
        raise ValueError('EXR decoded floats differ from source HDR floats')
    print(json.dumps({'source': str(source), 'sourceSha256': sha(source), 'exr': str(target), 'exrSha256': sha(target), 'width': spec.width, 'height': spec.height, 'channels': 3, 'pixelFormat': 'float32', 'compression': 'zip', 'min': float(data.min()), 'max': float(data.max()), 'channelMean': [float(x) for x in data.mean(axis=(0, 1), dtype=np.float64)], 'exactFloatRoundtrip': True, 'oiioVersion': oiio.VERSION_STRING, 'scope': 'HDR decoder floats exactly equal EXR decoder floats; no tone mapping or color-space transform.'}))


if __name__ == '__main__':
    main()
