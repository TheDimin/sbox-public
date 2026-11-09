"""Independent OrientationTest expectations, written before the GPU backend.

Run only through the validation coordinator. These tests validate the oracle and
the pinned sample; they do not establish that the engine rendered anything.
"""

import importlib.util
import math
from pathlib import Path
import unittest


MODULE = Path(__file__).with_name("source_oracle.py")
SPEC = importlib.util.spec_from_file_location("orientation_oracle", MODULE)
oracle = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(oracle)


class SourceOracleTests(unittest.TestCase):
    def assert_vector(self, expected, actual, tolerance=1e-6):
        self.assertEqual(len(expected), len(actual))
        for a, b in zip(expected, actual):
            self.assertAlmostEqual(a, b, delta=tolerance)

    def test_basis_is_cyclic_proper_and_only_positions_change_units(self):
        self.assert_vector((3 / .0254, 1 / .0254, 2 / .0254), oracle.engine_position((1, 2, 3)))
        self.assert_vector((3, 1, 2), oracle.engine_direction((1, 2, 3)))
        self.assert_vector((0, 0, 1), oracle.cross(oracle.engine_direction((0, 0, 1)), oracle.engine_direction((1, 0, 0))))

    def test_geometry_fingerprint_normalizes_only_json_number_spelling(self):
        self.assertEqual(oracle.geometry_fingerprint({"value": [1, 0, -0.0]}), oracle.geometry_fingerprint({"value": [1.0, 0.0, 0]}))
        self.assertNotEqual(oracle.geometry_fingerprint({"value": [1.0]}), oracle.geometry_fingerprint({"value": [1.0000000000000002]}))

    def test_literal_source_camera_matches_reference_positive_faces(self):
        self.assert_vector((1, 1, .65), oracle.engine_direction((1, .65, 1)))
        self.assert_vector((0, 0, 1), oracle.engine_direction((0, 1, 0)))
        self.assert_vector((1, -1, .65), oracle.engine_direction((-1, .65, 1)))

    def test_column_major_matrix_translation_is_not_transposed(self):
        matrix = (2, 0, 0, 0, 0, 3, 0, 0, 0, 0, 4, 0, 5, 7, 11, 1)
        self.assert_vector((7, 13, 23), oracle.transform_point(matrix, (1, 2, 3)))

    def test_quaternion_positive_z_rotation_uses_column_vector_direction(self):
        matrix = oracle.node_matrix({"rotation": [0, 0, math.sqrt(.5), math.sqrt(.5)]})
        self.assert_vector((0, 1, 0), oracle.transform_point(matrix, (1, 0, 0)))

    def test_nonuniform_scaled_rotated_hierarchy_keeps_inherited_shear(self):
        parent = oracle.node_matrix({"scale": [2, 3, 4], "translation": [5, 7, 11]})
        child = oracle.node_matrix({"rotation": [0, 0, math.sin(math.pi / 8), math.cos(math.pi / 8)]})
        world = oracle.multiply(parent, child)
        self.assert_vector((5 + math.sqrt(2), 7 + 3 / math.sqrt(2), 11), oracle.transform_point(world, (1, 0, 0)))
        x, y = world[0:3], world[4:7]
        self.assertGreater(abs(sum(a * b for a, b in zip(x, y))), 1)

    def test_normal_uses_inverse_transpose_not_position_transform(self):
        matrix = oracle.node_matrix({"scale": [2, 3, 4], "translation": [8, 9, 10]})
        expected = oracle.normalize((.5, 1 / 3, .25))
        self.assert_vector(expected, oracle.transform_normal(matrix, (1, 1, 1)))

    def test_reflection_preserves_front_face_via_explicit_negative_determinant(self):
        matrix = oracle.node_matrix({"scale": [-1, 1, 1]})
        self.assertEqual(-1, oracle.determinant(matrix))
        self.assert_vector((-1, 0, 0), oracle.transform_normal(matrix, (1, 0, 0)))

    def test_all_pinned_container_variants_have_same_geometry(self):
        variants = oracle.sample_variants()
        self.assertEqual({"glTF", "glTF-Embedded", "glTF-Binary"}, set(variants))
        records = [oracle.describe_sample(path) for path in variants.values()]
        self.assertEqual(1, len({record["geometry_sha256"] for record in records}))
        for record in records:
            self.assertEqual(13, len(record["primitives"]))
            self.assertEqual(7, len(record["materials"]))
            self.assertEqual(0, record["animation_count"])
            self.assertEqual(0, record["skin_count"])
            self.assertEqual({5121, 5123}, {p["index_component_type"] for p in record["primitives"]})

    def test_each_transformed_arrow_points_at_independently_baked_same_color_target(self):
        record = oracle.describe_sample(oracle.sample_variants()["glTF"])
        by_name = {primitive["node_name"]: primitive for primitive in record["primitives"]}
        for axis in "XYZ":
            for sign in (1, 2):
                arrow = by_name[f"Arrow{axis}{sign}"]
                target = by_name[f"Target{axis}{sign}"]
                self.assertEqual(arrow["material"], target["material"])
                self.assertLess(oracle.closest_distance(arrow["source_world_positions"], target["source_world_positions"]), .1)

    def test_transposed_quaternion_negative_control_breaks_arrow_target_alignment(self):
        root, buffers = oracle.read_container(oracle.sample_variants()["glTF"])
        good = oracle.describe(root, buffers)
        bad = oracle.describe(root, buffers, transpose_quaternions=True)
        good_by_name = {p["node_name"]: p for p in good["primitives"]}
        bad_by_name = {p["node_name"]: p for p in bad["primitives"]}
        for axis in "XYZ":
            name = f"Arrow{axis}1"
            target = good_by_name[f"Target{axis}1"]["source_world_positions"]
            self.assertLess(oracle.closest_distance(good_by_name[name]["source_world_positions"], target), .1)
            self.assertGreater(oracle.closest_distance(bad_by_name[name]["source_world_positions"], target), .5)

    def test_transposed_authored_matrix_negative_control_changes_matrix_arrow(self):
        root, buffers = oracle.read_container(oracle.sample_variants()["glTF"])
        good = oracle.describe(root, buffers)
        # Rotation-only transpose keeps translation in its correct fourth column,
        # isolating the bug demonstrated by the upstream failure illustration.
        bad = oracle.describe(root, buffers, transpose_matrices=True)
        good_by_name = {p["node_name"]: p for p in good["primitives"]}
        bad_by_name = {p["node_name"]: p for p in bad["primitives"]}
        for axis in "XYZ":
            name = f"Arrow{axis}2"
            delta = max(math.dist(a, b) for a, b in zip(good_by_name[name]["source_world_positions"], bad_by_name[name]["source_world_positions"]))
            self.assertGreater(delta, .4)


if __name__ == "__main__":
    unittest.main()
