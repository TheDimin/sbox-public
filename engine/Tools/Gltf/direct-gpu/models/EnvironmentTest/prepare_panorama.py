#!/usr/bin/env python3
"""Prepare a linear native IBL fixture OUTSIDE watched content.

Use inside resource-guard run for decoding. This does not import/compile assets,
launch editors, mutate model textures, or establish rendered IBL support.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re
import shutil


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def vtex(resource_exr):
    if not re.fullmatch(r"materials/gltf_reference_environment/[a-z0-9_]+\.exr", resource_exr):
        raise ValueError("Invalid canonical environment resource")
    return '''<!-- dmx encoding keyvalues2_noids 4 format vtex 1 -->
"CDmeVtex"
{
    "m_inputTextureArray" "element_array"
    [
        "CDmeInputTexture"
        {
            "m_name" "string" "inputTexture0"
            "m_fileName" "string" "%s"
            "m_colorSpace" "string" "linear"
            "m_fileExt" "string" ""
            "m_nMinBitsPerChannel" "int" "-1"
            "m_typeString" "string" "CUBE"
            "m_imageProcessorArray" "element_array" []
        }
    ]
    "m_outputTypeString" "string" "CUBE"
    "m_outputFormat" "string" "BC6H"
    "m_outputClearColor" "vector4" "0 0 0 0"
    "m_nOutputMinDimension" "int" "0"
    "m_nOutputMaxDimension" "int" "0"
    "m_textureOutputChannelArray" "element_array"
    [
        "CDmeTextureOutputChannel"
        {
            "m_inputTextureArray" "string_array" [ "inputTexture0" ]
            "m_srcChannels" "string" "rgba"
            "m_dstChannels" "string" "rgba"
            "m_mipAlgorithm" "CDmeImageProcessor"
            {
                "m_algorithm" "string" "GGXCubeMapBlur"
                "m_stringArg" "string" ""
                "m_vFloat4Arg" "vector4" "0 0 0 0"
            }
            "m_outputColorSpace" "string" "linear"
        }
    ]
    "m_vClamp" "vector3" "0 0 0"
    "m_bNoLod" "bool" "0"
}
''' % resource_exr


def prepare(source, expected, output, name, provenance, source_url=None, author=None, license_url=None):
    import numpy as np
    import OpenImageIO as oiio

    source = source.resolve(strict=True)
    output = output.resolve()
    if (output == Path('/tmp') or not output.is_relative_to('/tmp') or output.exists()
            or not re.fullmatch(r'[a-z0-9_]+', name)):
        raise ValueError('Require a new directory strictly under /tmp and a safe resource name')
    if sha(source) != expected:
        raise ValueError('Pinned source SHA256 differs')
    stream = oiio.ImageInput.open(str(source))
    if stream is None:
        raise ValueError(oiio.geterror())
    try:
        spec = stream.spec()
        if (spec.width != spec.height * 2 or spec.width > 8192
                or spec.width < 128 or spec.nchannels not in (3, 4)):
            raise ValueError('Require a bounded 2:1 RGB/RGBA panorama')
        pixels = np.asarray(stream.read_image(oiio.FLOAT), dtype=np.float32)
    finally:
        stream.close()
    if not np.isfinite(pixels).all() or pixels[..., :3].min() < 0 or pixels[..., :3].max() <= 1:
        raise ValueError('Require finite, nonnegative, unclipped HDR pixels')
    output.mkdir(parents=True)
    target = output / (name + '.exr')
    if source.suffix.lower() == '.exr':
        shutil.copyfile(source, target)  # Original EXR bytes remain exact.
    else:
        outspec = oiio.ImageSpec(spec.width, spec.height, spec.nchannels, oiio.FLOAT)
        outspec.attribute('compression', 'zip')
        writer = oiio.ImageOutput.create(str(target))
        if writer is None or not writer.open(str(target), outspec):
            raise ValueError('Cannot create linear EXR')
        try:
            if not writer.write_image(np.ascontiguousarray(pixels)):
                raise ValueError(writer.geterror())
        finally:
            writer.close()
    check = oiio.ImageInput.open(str(target))
    try:
        decoded = np.asarray(check.read_image(oiio.FLOAT), dtype=np.float32)
    finally:
        check.close()
    if not np.array_equal(decoded, pixels):
        raise ValueError('EXR roundtrip changed decoded source samples')
    relative = 'materials/gltf_reference_environment/' + name
    definition = output / (name + '.vtex')
    definition.write_text(vtex(relative + '.exr'))
    receipt = {'schemaVersion': 1, 'name': name, 'source': str(source),
               'sourceSha256': expected, 'exr': str(target), 'exrSha256': sha(target),
               'definition': str(definition), 'definitionSha256': sha(definition),
               'resource': relative + '.vtex', 'width': spec.width, 'height': spec.height,
               'channels': spec.nchannels, 'minimum': float(pixels[..., :3].min()),
               'maximum': float(pixels[..., :3].max()), 'oiioVersion': oiio.VERSION_STRING,
               'exactDecodedRoundtrip': True, 'originalBytesPreserved': source.suffix.lower() == '.exr',
               'provenance': provenance, 'attribution': {'sourceUrl': source_url, 'author': author, 'licenseUrl': license_url}, 'nativeImport': 'pending',
               'scope': 'Source HDR preserved; no tone mapping, recoloring or display transform. Native import, cubemap orientation, HDR readback, mips and rendered controls remain pending.'}
    (output / 'fixture.json').write_text(json.dumps(receipt, indent=2) + '\n')
    return receipt


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', required=True, type=Path)
    parser.add_argument('--sha256', required=True)
    parser.add_argument('--output', required=True, type=Path)
    parser.add_argument('--name', required=True)
    parser.add_argument('--provenance', required=True)
    parser.add_argument('--source-url')
    parser.add_argument('--author')
    parser.add_argument('--license-url')
    args = parser.parse_args()
    print(json.dumps(prepare(args.source, args.sha256, args.output, args.name, args.provenance, args.source_url, args.author, args.license_url), indent=2))
