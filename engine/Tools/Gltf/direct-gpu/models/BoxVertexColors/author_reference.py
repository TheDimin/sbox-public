"""Independent bounded stdlib reference and adversarial fixtures for one cube.

Source license: CC0-1.0, Marco Hutter, glTF-Sample-Assets commit
90d7ede14c7e280af263824604b427a1ca02cb66. No production reader is imported.
"""
import base64
import hashlib
import json
import pathlib
import struct

HERE = pathlib.Path(__file__).resolve().parent
REPO = HERE.parents[4]
ROOT = REPO / "Temp/gltf-reference/glTF-Sample-Assets/Models/BoxVertexColors"
SOURCES = ["glTF/BoxVertexColors.gltf", "glTF-Binary/BoxVertexColors.glb", "glTF-Embedded/BoxVertexColors.gltf"]
HASHES = {
    SOURCES[0]: "53d15f2357d410d0cc9e798065709054474db25ba52274f012bb7fb96a2eaddf",
    SOURCES[1]: "9c48227f33b0ba2fbcf23b98ebf60d1c8ae0c6e6c5281e0aa3cc58affee10382",
    SOURCES[2]: "245e793ceea921ef3117b7d0c44d9e5640dd81b626ab0b6ff870fc0c0b582511",
    "glTF/buffer.bin": "b508427bea091cc29cbe68d8b08e75db27e33559f72104a37343c7e808e72d26",
}
POSITIONS = [
    [0,0,0], [1,0,0], [1,1,0], [0,1,0], [1,0,1], [0,0,1], [0,1,1], [1,1,1],
    [1,0,0], [1,0,1], [1,1,1], [1,1,0], [0,0,1], [0,0,0], [0,1,0], [0,1,1],
    [0,1,0], [1,1,0], [1,1,1], [0,1,1], [0,0,1], [1,0,1], [1,0,0], [0,0,0],
]


def pinned_bytes(path):
    value = (ROOT / path).read_bytes()
    assert hashlib.sha256(value).hexdigest() == HASHES[path], path
    return value


def unpack(path):
    value = pinned_bytes(path)
    binary = None
    if value[:4] == b"glTF":
        assert struct.unpack_from("<3I", value) == (0x46546c67, 2, len(value))
        offset = 12
        while offset < len(value):
            count, kind = struct.unpack_from("<2I", value, offset)
            assert count % 4 == 0 and count <= len(value) - offset - 8
            chunk = value[offset + 8:offset + 8 + count]
            if kind == 0x4e4f534a:
                doc = json.loads(chunk)
            elif kind == 0x004e4942:
                binary = chunk
            offset += 8 + count
    else:
        doc = json.loads(value)
    if binary is None:
        uri = doc["buffers"][0]["uri"]
        binary = base64.b64decode(uri.split(",", 1)[1]) if uri.startswith("data:") else pinned_bytes("glTF/" + uri)
    return doc, binary


def decode(doc, binary):
    primitive = doc["meshes"][0]["primitives"][0]
    def accessor(index):
        acc = doc["accessors"][index]
        view = doc["bufferViews"][acc["bufferView"]]
        n = {"SCALAR": 1, "VEC3": 3, "VEC4": 4}[acc["type"]]
        code, size, divisor = {5121: ("B", 1, 255), 5123: ("H", 2, 65535), 5126: ("f", 4, 1)}[acc["componentType"]]
        start = view.get("byteOffset", 0) + acc.get("byteOffset", 0)
        stride = view.get("byteStride", size * n)
        values = [list(struct.unpack_from("<" + code * n, binary, start + i * stride)) for i in range(acc["count"])]
        return [[v / divisor for v in row] for row in values] if acc.get("normalized") else values
    color = accessor(primitive["attributes"]["COLOR_0"])
    if len(color[0]) == 3:
        color = [row + [1] for row in color]
    return {"positions": accessor(primitive["attributes"]["POSITION"]), "normals": accessor(primitive["attributes"]["NORMAL"]),
            "colors": color, "indices": [v[0] for v in accessor(primitive["indices"])],
            "color1": accessor(primitive["attributes"]["COLOR_1"]) if "COLOR_1" in primitive["attributes"] else None}


def read_pinned(path):
    result = decode(*unpack(path))
    # Original source is RGB; expose that explicit shape to literal-source tests.
    result["colors"] = [row[:3] for row in result["colors"]]
    return result


def color_for_draw(value, semantic_index):
    return [max(0, min(1, v)) for v in value] if semantic_index == 0 else list(value)


def base_color(color, factor, linear_texture):
    return [a * b * c for a, b, c in zip(color_for_draw(color, 0), factor, linear_texture)]


def srgb_decode(v):
    return v / 12.92 if v <= .04045 else ((v + .055) / 1.055) ** 2.4


def fixtures():
    result = []
    original, source_binary = unpack(SOURCES[0])
    for component_type in (5126, 5121, 5123):
        for count in (3, 4):
            name = {5126: "float", 5121: "unorm8", 5123: "unorm16"}[component_type] + str(count)
            doc = json.loads(json.dumps(original)); binary = bytearray(source_binary)
            size = {5126: 4, 5121: 1, 5123: 2}[component_type]
            stride = ((count * size + 8 + 3) // 4) * 4
            start = len(binary); colors = []
            for vertex in range(24):
                # Asymmetric fractional color: source POSITION and COLOR offset
                # aliasing, RGB swizzle and sRGB decode all visibly disagree.
                values = [.25 + .25 * (vertex % 3), .5, .125 + .125 * (vertex % 4), .25 + .25 * (vertex % 3)][:count]
                if component_type != 5126:
                    maximum = 255 if component_type == 5121 else 65535
                    integers = [round(v * maximum) for v in values]
                    payload = struct.pack("<" + ("B" if size == 1 else "H") * count, *integers)
                    expected = [v / maximum for v in integers]
                else:
                    payload = struct.pack("<" + "f" * count, *values); expected = values
                binary += bytes([0xa5, 0x5a, 0xc3, 0x3c]) + payload + bytes([0xcd]) * (stride - 4 - len(payload))
                colors.append(expected + ([1] if count == 3 else []))
            view_index = len(doc["bufferViews"])
            doc["bufferViews"].append({"buffer": 0, "byteOffset": start, "byteLength": stride * 24, "byteStride": stride, "target": 34962})
            acc = {"bufferView": view_index, "byteOffset": 4, "componentType": component_type, "type": "VEC" + str(count), "count": 24}
            if component_type != 5126:
                acc["normalized"] = True
            doc["accessors"].append(acc)
            primitive = doc["meshes"][0]["primitives"][0]; primitive["attributes"]["COLOR_0"] = len(doc["accessors"]) - 1
            # Additional sets stay independently addressable and un-clamped.
            other_start = len(binary)
            binary += struct.pack("<4f", -.25, .5, 1.25, .75) * 24
            doc["bufferViews"].append({"buffer": 0, "byteOffset": other_start, "byteLength": 384, "target": 34962})
            doc["accessors"].append({"bufferView": len(doc["bufferViews"]) - 1, "componentType": 5126, "count": 24, "type": "VEC4"})
            primitive["attributes"]["COLOR_1"] = len(doc["accessors"]) - 1
            doc["materials"] = [{"pbrMetallicRoughness": {"baseColorFactor": [.5, .25, .75, .5], "metallicFactor": 0, "roughnessFactor": 1}}]
            primitive["material"] = 0
            doc["buffers"] = [{"byteLength": len(binary), "uri": "data:application/octet-stream;base64," + base64.b64encode(binary).decode()}]
            result.append({"name": name, "document": doc, "binary": bytes(binary), "component_type": component_type, "components": count,
                           "stride": stride, "expected_colors": colors, "expected_color1": [-.25, .5, 1.25, .75]})
    # Core requires COLOR_0 clamping even without morph targets.
    fixture = result[1]
    doc = json.loads(json.dumps(fixture["document"])); binary = bytearray(fixture["binary"])
    acc = doc["accessors"][doc["meshes"][0]["primitives"][0]["attributes"]["COLOR_0"]]
    start = doc["bufferViews"][acc["bufferView"]]["byteOffset"] + 4
    for vertex in range(24):
        struct.pack_into("<4f", binary, start + vertex * fixture["stride"], -.25, .5, 1.25, .25)
    doc["buffers"][0]["uri"] = "data:application/octet-stream;base64," + base64.b64encode(binary).decode()
    result.append({**fixture, "name": "float4_clamp", "document": doc, "binary": bytes(binary), "expected_colors": [[-.25, .5, 1.25, .25]] * 24})
    return result


def main():
    reference = read_pinned(SOURCES[0])
    reference.update(corpus_revision="90d7ede14c7e280af263824604b427a1ca02cb66", source_hashes=HASHES,
                     source_color_components=3, license="CC0-1.0", attribution="Marco Hutter")
    ((pathlib.Path(__file__).resolve().parents[6]/'research/gltf-validation/direct-gpu/models/BoxVertexColors') / "reference.json").write_text(json.dumps(reference, indent=2) + "\n")
    target = (pathlib.Path(__file__).resolve().parents[6]/'research/gltf-validation/direct-gpu/models/BoxVertexColors') / "fixtures"; target.mkdir(exist_ok=True)
    inventory = []
    for fixture in fixtures():
        encoded = (json.dumps(fixture["document"], separators=(",", ":")) + "\n").encode()
        path = fixture["name"] + ".gltf"; (target / path).write_bytes(encoded)
        inventory.append({key: value for key, value in fixture.items() if key not in ("document", "binary")}
                         | {"path": path, "sha256": hashlib.sha256(encoded).hexdigest()})
    ((pathlib.Path(__file__).resolve().parents[6]/'research/gltf-validation/direct-gpu/models/BoxVertexColors') / "fixtures.json").write_text(json.dumps(inventory, indent=2) + "\n")
    audits = []
    for source in SOURCES:
        doc, binary = unpack(source)
        primitive = doc["meshes"][0]["primitives"][0]
        streams = {}
        for semantic, accessor_index in primitive["attributes"].items():
            acc = doc["accessors"][accessor_index]
            view = doc["bufferViews"][acc["bufferView"]]
            streams[semantic] = {"accessor": accessor_index, "component_type": acc["componentType"], "type": acc["type"],
                                 "count": acc["count"], "normalized": acc.get("normalized", False),
                                 "view": acc["bufferView"], "view_offset": view.get("byteOffset", 0),
                                 "accessor_offset": acc.get("byteOffset", 0), "stride": view.get("byteStride", 12)}
        audits.append({"path": source, "sha256": HASHES[source], "binary_sha256": hashlib.sha256(binary[:936]).hexdigest(),
                       "attributes": streams, "indices": 36, "source_material": None, "default_scene": 0,
                       "source_colors_equal_positions": True, "scope": "independent_stdlib_source_audit_not_rendered_evidence"})
    ((pathlib.Path(__file__).resolve().parents[6]/'research/gltf-validation/direct-gpu/models/BoxVertexColors') / "source-audit.json").write_text(json.dumps(audits, indent=2) + "\n")


if __name__ == "__main__":
    main()
