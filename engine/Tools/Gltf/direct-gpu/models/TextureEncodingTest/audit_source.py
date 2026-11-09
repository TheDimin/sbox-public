#!/usr/bin/env python3
"""Bounded pinned-source audit. No production reader, decoder or renderer imports."""
import hashlib
import json
from pathlib import Path
import struct
import zlib

ROOT = Path(__file__).resolve().parents[6]
MODEL = ROOT / "Temp/gltf-reference/glTF-Sample-Assets/Models/TextureEncodingTest"


def png_chunks(data):
    assert data[:8] == b"\x89PNG\r\n\x1a\n"
    rows, pos = [], 8
    while pos < len(data):
        n, = struct.unpack_from(">I", data, pos)
        assert n <= len(data) - pos - 12
        name = data[pos + 4:pos + 8]
        payload = data[pos + 8:pos + 8 + n]
        crc, = struct.unpack_from(">I", data, pos + 8 + n)
        assert zlib.crc32(name + payload) == crc
        rows.append((name.decode("ascii"), payload))
        pos += n + 12
        if name == b"IEND":
            assert not n and pos == len(data)
            return rows
    raise AssertionError("Missing IEND")


def audit():
    manifest = json.loads((ROOT / "research/gltf-validation/corpus-case-manifest.json").read_bytes())
    assert manifest["corpus"]["commit"] == "90d7ede14c7e280af263824604b427a1ca02cb66"
    cases = [c for c in manifest["cases"] if c["model"] == "TextureEncodingTest"]
    assert sorted(c["variant"] for c in cases) == ["glTF", "glTF-Binary"]
    source = json.loads((MODEL / "glTF/TextureEncodingTest.gltf").read_bytes())
    binary = (MODEL / "glTF-Binary/TextureEncodingTest.glb").read_bytes()
    magic, version, length, json_length, chunk = struct.unpack_from("<IIIII", binary)
    assert magic == 0x46546c67 and version == 2 and length == len(binary) and chunk == 0x4e4f534a
    glb = json.loads(binary[20:20 + json_length])
    assert source["materials"] == glb["materials"]
    assert len(source["materials"]) == 14 and len(source["images"]) == 8
    assert not glb["buffers"][0].get("uri")
    for image in glb["images"]:
        assert "bufferView" in image and image["mimeType"] == "image/png" and "uri" not in image
    images = []
    for index, image in enumerate(source["images"]):
        data = (MODEL / "glTF" / image["uri"]).read_bytes()
        chunks = png_chunks(data)
        header = dict(chunks)["IHDR"]
        width, height, depth, color, compression, filter_method, interlace = struct.unpack(">IIBBBBB", header)
        assert compression == filter_method == interlace == 0
        row = {"index": index, "file": image["uri"], "sha256": hashlib.sha256(data).hexdigest(), "bytes": len(data), "width": width, "height": height, "bit_depth": depth, "color_type": color, "chunks": [name for name, _ in chunks]}
        if index < 6:
            assert (width, height, depth, color) == (1, 1, 8, 2)
            expected = [0, 136, 0 if index < 3 else 255]
            assert list(zlib.decompress(b"".join(payload for name, payload in chunks if name == "IDAT"))) == [0] + expected
            row["literal_rgba"] = expected + [255]
            if index % 3 == 1:
                assert struct.unpack(">I", dict(chunks)["gAMA"])[0] == 20000
        else:
            assert (width, height, depth, color) == (1024, 256, 4, 3)
            assert len(dict(chunks)["PLTE"]) == 24 and dict(chunks)["tRNS"] == b"\0"
        view = glb["bufferViews"][glb["images"][index]["bufferView"]]
        bin_offset = 20 + json_length + 8 + view.get("byteOffset", 0)
        assert binary[bin_offset:bin_offset + view["byteLength"]] == data
        images.append(row)
    return {"corpus_commit": manifest["corpus"]["commit"], "model": "TextureEncodingTest", "actual_variants": ["glTF", "glTF-Binary"], "required_forms": ["original-source", "portable-compiled-glb_c"], "source_sha256": hashlib.sha256((MODEL / "glTF/TextureEncodingTest.gltf").read_bytes()).hexdigest(), "glb_sha256": hashlib.sha256(binary).hexdigest(), "materials": 14, "images": images, "literal_linear_136": ((136 / 255 + .055) / 1.055) ** 2.4, "execution_scope": "Python source audit only; C# / native / GPU execution pending"}


if __name__ == "__main__":
    print(json.dumps(audit(), indent=2))
