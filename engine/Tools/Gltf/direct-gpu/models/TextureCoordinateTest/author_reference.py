"""Bounded independent source audit for one pinned Khronos model; no producer imports."""
import argparse
import base64
import copy
import hashlib
import io
import json
import pathlib
import struct

from PIL import Image

REVISION = "90d7ede14c7e280af263824604b427a1ca02cb66"
FORM_FILES = ["glTF/TextureCoordinateTest.gltf", "glTF-Embedded/TextureCoordinateTest.gltf", "glTF-Binary/TextureCoordinateTest.glb"]


def load(path):
    raw = path.read_bytes()
    if raw[:4] == b"glTF":
        magic, version, size = struct.unpack_from("<III", raw)
        assert version == 2 and size == len(raw)
        offset, binary, source = 12, None, None
        while offset < len(raw):
            length, kind = struct.unpack_from("<II", raw, offset)
            content = raw[offset+8:offset+8+length]
            assert len(content) == length and length % 4 == 0
            if kind == 0x4e4f534a:
                source = json.loads(content)
            elif kind == 0x004e4942:
                binary = content
            else:
                raise ValueError("Unexpected source chunk")
            offset += 8+length
    else:
        source, binary = json.loads(raw), None
    def resolve(buffer):
        if "uri" not in buffer:
            return binary[:buffer["byteLength"]]
        uri = buffer["uri"]
        return base64.b64decode(uri.split(",", 1)[1]) if uri.startswith("data:") else (path.parent / uri).read_bytes()
    return source, [resolve(b) for b in source["buffers"]]


def accessor(source, buffers, index, offset_delta=0, forced_stride=None):
    value = source["accessors"][index]
    view = source["bufferViews"][value["bufferView"]]
    component = value["componentType"]
    code, size = {5121: ("B", 1), 5123: ("H", 2), 5126: ("f", 4)}[component]
    components = {"SCALAR": 1, "VEC2": 2, "VEC3": 3}[value["type"]]
    stride = forced_stride or view.get("byteStride", size*components)
    offset = view.get("byteOffset", 0) + value.get("byteOffset", 0) + offset_delta
    values = [list(struct.unpack_from("<"+code*components, buffers[view["buffer"]], offset+i*stride)) for i in range(value["count"])]
    if value.get("normalized", False):
        assert component in (5121, 5123)
        maximum = 255 if component == 5121 else 65535
        values = [[v/maximum for v in row] for row in values]
    return dict(accessor=index, view=value["bufferView"], buffer=view["buffer"],
                viewOffset=view.get("byteOffset", 0), viewLength=view["byteLength"],
                accessorOffset=value.get("byteOffset", 0), stride=stride,
                componentType=component, components=components, count=value["count"],
                normalized=value.get("normalized", False), values=values)


def primitives(source, buffers):
    result = []
    for mesh, definition in enumerate(source["meshes"]):
        assert len(definition["primitives"]) == 1
        p = definition["primitives"][0]
        result.append(dict(mesh=mesh, node=next(i for i, n in enumerate(source["nodes"]) if n.get("mesh") == mesh),
                           material=p["material"], mode=p.get("mode", 4),
                           indices=accessor(source, buffers, p["indices"]),
                           streams={name: accessor(source, buffers, index) for name, index in p["attributes"].items()}))
    return result


def layout_fixture(source, buffers):
    """Derived test input, preserving every original byte and accessor."""
    fixture = copy.deepcopy(source)
    assert len(buffers) == 1
    data = bytearray(buffers[0] + b"\xd7"*16)
    buffer_index = 0
    for mesh in [0, 1, 2, 4]:
        attrs = fixture["meshes"][mesh]["primitives"][0]["attributes"]
        uv0 = accessor(source, buffers, attrs["TEXCOORD_0"])["values"]
        start = len(data)
        data.extend(b"\xd7"*52)
        for vertex, (u, v) in enumerate(uv0):
            struct.pack_into("<HH", data, start+4+vertex*12, round((1-u)*65535), round(v*65535))
        view = len(fixture["bufferViews"])
        fixture["bufferViews"].append(dict(buffer=buffer_index, byteOffset=start, byteLength=52, byteStride=12, target=34962))
        attrs["TEXCOORD_1"] = len(fixture["accessors"])
        fixture["accessors"].append(dict(bufferView=view, byteOffset=4, componentType=5123, normalized=True, count=4, type="VEC2", name="DerivedMirroredU_InterleavedNormalized"))
        material = fixture["materials"][fixture["meshes"][mesh]["primitives"][0]["material"]]
        material["pbrMetallicRoughness"]["baseColorTexture"]["texCoord"] = 1
    fixture["buffers"][0] = dict(uri="data:application/octet-stream;base64,"+base64.b64encode(data).decode(), byteLength=len(data))
    return fixture, [bytes(data)]


def read_forms(model):
    forms = []
    for name in FORM_FILES:
        path = model / name
        source, buffers = load(path)
        image = source["images"][0]
        if "uri" in image:
            uri = image["uri"]
            png = base64.b64decode(uri.split(",", 1)[1]) if uri.startswith("data:") else (path.parent / uri).read_bytes()
        else:
            view = source["bufferViews"][image["bufferView"]]
            png = buffers[view["buffer"]][view.get("byteOffset", 0):view.get("byteOffset", 0)+view["byteLength"]]
        assert hashlib.sha256(png).hexdigest() == hashlib.sha256((model / "glTF/TextureCoordinateTemplate.png").read_bytes()).hexdigest()
        forms.append(dict(name=path.parent.name, path=name, sha256=hashlib.sha256(path.read_bytes()).hexdigest(),
                          sceneNodes=source["scenes"][source["scene"]]["nodes"], primitives=primitives(source, buffers),
                          texture=dict(index=0, image=0, sampler=0, texCoord=0, transform=[1, 0, 0, 0, 1, 0], wrapS=10497, wrapT=10497, minFilter=None, magFilter=None)))
    return forms


def landmarks(path):
    image = Image.open(io.BytesIO(path.read_bytes())).convert("RGBA")
    assert image.size == (512, 512)
    # Label pixels are vertically asymmetric. Keep a surrounding same-row white
    # control; checkerboard-only probes would fail to reject a whole-image V flip.
    candidates = {0: [(386, 57), (421, 61), (398, 65)],
                  1: [(110, 58), (97, 62), (70, 66)],
                  2: [(454, 430), (466, 436), (437, 455)],
                  4: [(78, 430), (92, 435), (64, 454)]}
    rows = []
    for mesh, points in candidates.items():
        for x, y in points:
            # Search a bounded local text window for a pixel which is black and
            # white after a V flip. The literal output is frozen with source hash.
            found = next(((xx, yy) for yy in range(y-2, y+3) for xx in range(x-2, x+3)
                          if image.getpixel((xx, yy))[:3] == (0, 0, 0)
                          and image.getpixel((xx, 511-yy))[:3] == (255, 255, 255)), None)
            if found is None:
                raise ValueError((mesh, x, y))
            xx, yy = found
            white = next((wx for wx in range(max(0, xx-10), min(512, xx+11)) if image.getpixel((wx, yy))[:3] == (255, 255, 255)), None)
            rows.append(dict(mesh=mesh, x=xx, y=yy, u=(xx+.5)/512, v=(yy+.5)/512,
                             rgba=list(image.getpixel((xx, yy))), wrongVrgba=list(image.getpixel((xx, 511-yy))),
                             whiteU=(white+.5)/512, whiteV=(yy+.5)/512))
    return rows


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--checkout", type=pathlib.Path, default=pathlib.Path(__file__).resolve().parents[6])
    parser.add_argument("--output", type=pathlib.Path, default=(pathlib.Path(__file__).resolve().parents[6]/'research/gltf-validation/direct-gpu/models/TextureCoordinateTest') / "source-audit.json")
    args = parser.parse_args()
    model = args.checkout / "Temp/gltf-reference/glTF-Sample-Assets/Models/TextureCoordinateTest"
    source, buffers = load(model / FORM_FILES[0])
    fixture, fixture_buffers = layout_fixture(source, buffers)
    files = FORM_FILES + ["glTF/TextureCoordinateTest.bin", "glTF/TextureCoordinateTemplate.png", "LICENSE.md", "metadata.json"]
    result = dict(corpusRevision=REVISION, files={f: hashlib.sha256((model/f).read_bytes()).hexdigest() for f in files},
                  forms=read_forms(model), derivedFixture=dict(primitives=primitives(fixture, fixture_buffers), scope="synthetic alternate-layout and coordinate-selection control; not an upstream variant"),
                  landmarks=landmarks(model / "glTF/TextureCoordinateTemplate.png"),
                  scope="independent Python struct/json/Pillow oracle; no GPU or rendered proof")
    args.output.write_text(json.dumps(result, indent=2)+"\n")
    print(json.dumps(dict(output=str(args.output), forms=len(result["forms"]), primitivesPerForm=5, landmarks=len(result["landmarks"]))))


if __name__ == "__main__":
    main()
