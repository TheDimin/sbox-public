"""Self-checks for layout mutations of OrientationTest, not engine proof."""

import struct
import unittest

import layout_fixture
import source_oracle as oracle


class LayoutFixtureTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.original, cls.original_buffers = oracle.read_container(oracle.sample_variants()["glTF"])
        cls.changed, cls.changed_buffers = layout_fixture.interleave(cls.original, cls.original_buffers)

    def test_mutation_preserves_exact_decoded_geometry_normals_materials_transforms(self):
        original = oracle.describe(self.original, self.original_buffers)
        changed = oracle.describe(self.changed, self.changed_buffers)
        self.assertEqual(original["geometry_sha256"], changed["geometry_sha256"])

    def test_position_and_normal_share_view_with_distinct_accessor_offsets(self):
        for mesh in self.changed["meshes"]:
            primitive = mesh["primitives"][0]
            position = self.changed["accessors"][primitive["attributes"]["POSITION"]]
            normal = self.changed["accessors"][primitive["attributes"]["NORMAL"]]
            self.assertEqual(position["bufferView"], normal["bufferView"])
            self.assertEqual(4, position["byteOffset"])
            self.assertEqual(20, normal["byteOffset"])
            view = self.changed["bufferViews"][position["bufferView"]]
            self.assertEqual(32, view["byteStride"])
            self.assertGreaterEqual(view["byteOffset"], 16)

    def test_index_types_and_values_remain_authored_with_nonzero_accessor_offsets(self):
        observed = set()
        for old, new in zip(self.original["meshes"], self.changed["meshes"]):
            index = new["primitives"][0]["indices"]
            old_index = old["primitives"][0]["indices"]
            descriptor = self.changed["accessors"][index]
            observed.add(descriptor["componentType"])
            self.assertEqual(4, descriptor["byteOffset"])
            self.assertEqual(oracle.read_accessor(self.original, self.original_buffers, old_index), oracle.read_accessor(self.changed, self.changed_buffers, index))
        self.assertEqual({5121, 5123}, observed)

    def test_assuming_packed_float3_or_buffer_origin_reads_poison(self):
        mismatches = 0
        for mesh in self.changed["meshes"]:
            index = mesh["primitives"][0]["attributes"]["POSITION"]
            accessor = self.changed["accessors"][index]
            view = self.changed["bufferViews"][accessor["bufferView"]]
            source = self.changed_buffers[view["buffer"]]
            expected = oracle.read_accessor(self.changed, self.changed_buffers, index)
            for vertex in range(accessor["count"]):
                wrong = struct.unpack_from("<fff", source, view["byteOffset"] + vertex * 12)
                mismatches += wrong != expected[vertex]
        self.assertGreater(mismatches, 1000)


if __name__ == "__main__":
    unittest.main()
