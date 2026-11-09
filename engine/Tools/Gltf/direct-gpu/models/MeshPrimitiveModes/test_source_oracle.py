"""Small stdlib audit of the pinned source. Never executes the C# producer/GPU path."""
import base64
import hashlib
import json
from pathlib import Path
import struct
import unittest

CHECKOUT = Path(__file__).resolve().parents[6]
MODEL = CHECKOUT / "Temp/gltf-reference/glTF-Sample-Assets/Models/MeshPrimitiveModes"
INDICES = [
    [0, 1, 2, 3, 4, 5, 6],
    [0, 1, 0, 2, 0, 3, 0, 4, 0, 5, 0, 6],
    [0, 1, 2, 3, 4, 5, 6],
    [0, 1, 2, 3, 4, 5, 6],
    [0, 1, 2, 0, 2, 3, 0, 3, 4, 0, 4, 5, 0, 5, 6, 0, 6, 1],
    [2, 3, 1, 4, 6, 5],
    [0, 1, 2, 3, 4, 5, 6, 1],
]
LISTS = [
    INDICES[0], INDICES[1],
    [0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 0],
    [0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6],
    INDICES[4], [2, 3, 1, 1, 3, 4, 1, 4, 6, 6, 4, 5], INDICES[4],
]
POSITIONS = [(0., 0., 0.), (.866, -.5, 0.), (.866, .5, 0.), (0., 1., 0.),
             (-.866, .5, 0.), (-.866, -.5, 0.), (0., -1., 0.)]
TRANSLATIONS = [(0, 3, 0), (-2, 0, 0), (0, 0, 0), (2, 0, 0), (-2, -3, 0), (0, -3, 0), (2, -3, 0)]
OFFSETS = [0, 14, 38, 52, 66, 102, 114]


def source(embedded=False):
    name = "glTF-Embedded" if embedded else "glTF"
    root = json.loads((MODEL / name / "MeshPrimitiveModes.gltf").read_bytes())
    uri = root["buffers"][0]["uri"]
    blob = base64.b64decode(uri.split(",", 1)[1]) if embedded else (MODEL / name / uri).read_bytes()
    return root, blob


def accessor(root, blob, number):
    a = root["accessors"][number]
    v = root["bufferViews"][a["bufferView"]]
    shape = "H" if a["componentType"] == 5123 else "3f"
    width = struct.calcsize("<" + shape)
    at = v.get("byteOffset", 0) + a.get("byteOffset", 0)
    return [struct.unpack_from("<" + shape, blob, at + i * v.get("byteStride", width)) for i in range(a["count"])]


class SourceOracleTests(unittest.TestCase):
    def test_pinned_three_input_hashes(self):
        hashes = {
            "glTF/MeshPrimitiveModes.gltf": "2a8b3209c25e2fca5105539136a05f42d170767f0eb16148cd3825758e5ee977",
            "glTF/buffer.bin": "4369522e5929bed650242fea069c687aa2ea1230caa61c378610b2154c1048d7",
            "glTF-Embedded/MeshPrimitiveModes.gltf": "9c6dd4c66dd5f4f15c052224b5c1446947ada7f935c521137520dbebbc7f2312",
        }
        for relative, expected in hashes.items():
            self.assertEqual(hashlib.sha256((MODEL / relative).read_bytes()).hexdigest(), expected)

    def test_actual_format_inventory(self):
        self.assertEqual(sorted(p.name for p in MODEL.iterdir() if p.is_dir() and p.name.startswith("glTF")), ["glTF", "glTF-Embedded"])

    def test_external_and_embedded_are_identical_data(self):
        a, ab = source()
        b, bb = source(True)
        a["buffers"][0].pop("uri")
        b["buffers"][0].pop("uri")
        self.assertEqual(a, b)
        self.assertEqual(ab, bb)

    def test_all_modes_are_indexed_uint16_with_actual_offsets(self):
        for embedded in (False, True):
            root, blob = source(embedded)
            for mode, mesh in enumerate(root["meshes"]):
                p, = mesh["primitives"]
                self.assertEqual((p["mode"], p["indices"], p["attributes"]), (mode, mode, {"POSITION": 7}))
                a = root["accessors"][p["indices"]]
                self.assertEqual((a["componentType"], a["byteOffset"], a["bufferView"]), (5123, OFFSETS[mode], 0))
                self.assertEqual([x[0] for x in accessor(root, blob, mode)], INDICES[mode])

    def test_every_position_component_is_literal_float32(self):
        root, blob = source()
        expected = [struct.unpack("<3f", struct.pack("<3f", *p)) for p in POSITIONS]
        self.assertEqual(accessor(root, blob, 7), expected)
        a = root["accessors"][7]
        self.assertEqual((a["componentType"], a.get("byteOffset", 0), root["bufferViews"][a["bufferView"]]["byteOffset"]), (5126, 0, 132))

    def test_authored_translations_and_world_bounds(self):
        root, blob = source()
        self.assertEqual([tuple(x["translation"]) for x in root["nodes"]], TRANSLATIONS)
        positions = accessor(root, blob, 7)
        world = [(p[2] / .0254, (p[0] + t[0]) / .0254, (p[1] + t[1]) / .0254) for p in positions for t in TRANSLATIONS]
        self.assertEqual([min(x[i] for x in world) for i in range(3)], [0, -112.83464553787952, -157.48031496062993])
        self.assertEqual([max(x[i] for x in world) for i in range(3)], [0, 112.83464553787952, 157.48031496062993])

    def test_literal_loop_closure_and_strip_winding_sensitivity(self):
        self.assertEqual(LISTS[2][:-2], LISTS[3])
        self.assertEqual(LISTS[2][-2:], [6, 0])
        self.assertEqual([len(x) for x in LISTS], [7, 12, 14, 12, 18, 12, 18])
        for mode in (4, 5, 6):
            for at in range(0, len(LISTS[mode]), 3):
                a, b, c = (POSITIONS[i] for i in LISTS[mode][at:at + 3])
                self.assertGreater((b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0]), .4)
        wrong_strip_odd = [3, 1, 4]
        a, b, c = (POSITIONS[i] for i in wrong_strip_odd)
        self.assertLess((b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0]), 0)

    def test_oracle_detects_wrong_origin_stride_and_index_offset(self):
        root, blob = source()
        proper = accessor(root, blob, 7)
        self.assertNotEqual([struct.unpack_from("<3f", blob, i * 12) for i in range(7)], proper)
        self.assertNotEqual([struct.unpack_from("<3f", blob, 132 + i * 4) for i in range(7)], proper)
        self.assertNotEqual([struct.unpack_from("<H", blob, i * 2)[0] for i in range(12)], INDICES[1])


if __name__ == "__main__":
    unittest.main(verbosity=2)
