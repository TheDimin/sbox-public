#!/usr/bin/env python3
"""Lightweight independent audit of the frozen one-model literal oracle.

This checks source provenance and reference transcription, not Sandbox.Gltf or
the GPU producer. Model data: Ed Mackey / Analytical Graphics, Inc., CC-BY-4.0.
"""
import hashlib
import json
from pathlib import Path
import re
import struct
import unittest

OWNED = Path(__file__).resolve().parent
CHECKOUT = OWNED.parents[5]
MODEL = CHECKOUT / "Temp/gltf-reference/glTF-Sample-Assets/Models/UnlitTest"
HASHES = {
    "glTF/UnlitTest.gltf": "b611ca0892cb1a0118e92b72ff52b3afed3e4903c15378daa72c4c252a0334e1",
    "glTF/UnlitTest.bin": "a6a81ec57560242f29ee2c2a94f452a923b5548bf256b6529b0c07dea3f5c907",
    "glTF-Binary/UnlitTest.glb": "e07b68c6fd9fbf73d372c610a681b89d6b013bdd1a506e46a411df82210a2481",
}


def glb_chunks(data):
    magic, version, size = struct.unpack_from("<III", data)
    assert (magic, version, size) == (0x46546C67, 2, len(data))
    result, offset = {}, 12
    while offset < len(data):
        length, kind = struct.unpack_from("<II", data, offset)
        assert length % 4 == 0 and offset + 8 + length <= len(data)
        result[kind] = data[offset + 8:offset + 8 + length]
        offset += 8 + length
    assert offset == len(data)
    return result


def literal_vectors(name):
    source = (OWNED / "UnlitTestLiteralGeometry.cs").read_text()
    part = source.split(f"Vector3[] {name} =", 1)[1].split("};", 1)[0]
    triples = re.findall(r"new\( ([^)]+) \)", part)
    return [tuple(struct.unpack("<f", struct.pack("<f", float(item.strip().rstrip("f"))))[0]
                  for item in triple.split(",")) for triple in triples]


class ReferenceAudit(unittest.TestCase):
    def test_pinned_input_hashes(self):
        for name, expected in HASHES.items():
            self.assertEqual(hashlib.sha256((MODEL / name).read_bytes()).hexdigest(), expected)

    def test_literal_geometry_matches_both_real_source_forms(self):
        external = (MODEL / "glTF/UnlitTest.bin").read_bytes()
        chunks = glb_chunks((MODEL / "glTF-Binary/UnlitTest.glb").read_bytes())
        self.assertEqual(chunks[0x004E4942], external)
        for name, offset in (("Positions", 0), ("Normals", 1152)):
            expected = [struct.unpack_from("<3f", external, offset + vertex * 12) for vertex in range(96)]
            self.assertEqual(literal_vectors(name), expected)
        source = (OWNED / "UnlitTestLiteralGeometry.cs").read_text().split("uint[] Indices =", 1)[1].split("};", 1)[0]
        self.assertEqual(tuple(map(int, re.findall(r"\d+", source))), struct.unpack_from("<132H", external, 2304))

    def test_both_materials_and_node_bindings(self):
        json_source = json.loads((MODEL / "glTF/UnlitTest.gltf").read_bytes())
        glb_source = json.loads(glb_chunks((MODEL / "glTF-Binary/UnlitTest.glb").read_bytes())[0x4E4F534A])
        for source in (json_source, glb_source):
            self.assertEqual(source["extensionsRequired"], ["KHR_materials_unlit"])
            self.assertEqual(source["nodes"][0]["translation"], [-1.2, 0, 0])
            self.assertEqual(source["nodes"][1]["translation"], [1.2, 0, 0])
            for material, factor in enumerate(((1, .217637640824031, 0, 1), (0, .217637640824031, 1, 1))):
                self.assertEqual(source["materials"][material]["extensions"], {"KHR_materials_unlit": {}})
                self.assertEqual(source["materials"][material]["pbrMetallicRoughness"]["baseColorFactor"], list(factor))
                self.assertEqual(source["meshes"][material]["primitives"][0]["material"], material)

    def test_world_basis_units_and_material_swap_controls(self):
        points = [(p[2] / .0254, (p[0] + x) / .0254, p[1] / .0254)
                  for x in (-1.2, 1.2) for p in literal_vectors("Positions")]
        for axis, extent in enumerate((1 / .0254, 2.2 / .0254, 1 / .0254)):
            self.assertAlmostEqual(min(p[axis] for p in points), -extent, places=10)
            self.assertAlmostEqual(max(p[axis] for p in points), extent, places=10)
        self.assertGreater(abs(.217637640824031 - 127 / 255), .25)
        self.assertNotEqual(literal_vectors("Positions"), literal_vectors("Normals"))


if __name__ == "__main__":
    unittest.main(verbosity=2)
