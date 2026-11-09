#!/usr/bin/env python3
"""Source-oracle sensitivity controls; no GPU or rendered-model evidence."""
import importlib.util
from pathlib import Path
import unittest

SOURCE = Path(__file__).with_name("author_reference.py")
SPEC = importlib.util.spec_from_file_location("cube_tangent_reference", SOURCE)
REFERENCE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(REFERENCE)


class TangentReferenceTests(unittest.TestCase):
    def test_parallel_source_frame_has_no_invented_direction(self):
        tangent, reason = REFERENCE.tangent_direction([1, 0, 0], [1, 0, 0], REFERENCE.identity(), [1, 0, 0])
        self.assertIsNone(tangent)
        self.assertEqual("morphed_tangent_parallel_to_normal", reason)

    def test_signed_weight_cancellation_stays_undefined_without_clamping(self):
        weight = -1
        base = [1, 0, 0]
        delta = [1, 0, 0]
        morphed = [base[index]+weight*delta[index] for index in range(3)]
        tangent, reason = REFERENCE.tangent_direction(morphed, [0, 0, 1], REFERENCE.identity(), [0, 0, 1])
        self.assertEqual([0, 0, 0], morphed)
        self.assertIsNone(tangent)
        self.assertEqual("zero_morphed_tangent", reason)

    def test_near_parallel_but_defined_direction_is_not_waived(self):
        tangent, reason = REFERENCE.tangent_direction([1, 1e-10, 0], [1, 0, 0], REFERENCE.identity(), [1, 0, 0])
        self.assertIsNone(reason)
        self.assertEqual([0, 1, 0], tangent)

    def test_defined_direction_uses_transform_then_normal_projection(self):
        matrix = REFERENCE.identity()
        matrix[0][0] = 2
        matrix[1][1] = 3
        tangent, reason = REFERENCE.tangent_direction([1, 1, 0], [0, 0, 1], matrix, [0, 0, 1])
        self.assertIsNone(reason)
        self.assertAlmostEqual(2/(13**.5), tangent[0])
        self.assertAlmostEqual(3/(13**.5), tangent[1])
        self.assertEqual(0, tangent[2])

    def test_actual_pinned_sources_identify_only_parallel_face_tangents(self):
        checkout = Path(__file__).resolve().parents[6]
        model = checkout/"Temp/gltf-reference/glTF-Sample-Assets/Models/AnimatedMorphCube"
        for folder, extension in (("glTF", "gltf"), ("glTF-Binary", "glb")):
            with self.subTest(form=folder):
                source = REFERENCE.Source(model/folder/("AnimatedMorphCube."+extension))
                primitive = source.data["meshes"][0]["primitives"][0]
                tangents = source.accessor(primitive["attributes"]["TANGENT"])
                normals = source.accessor(primitive["attributes"]["NORMAL"])
                self.assertEqual(24, len(tangents))
                self.assertEqual([[1, 0, 0, 1]]*24, tangents)
                undefined = []
                for vertex, (normal, tangent) in enumerate(zip(normals, tangents)):
                    direction, reason = REFERENCE.tangent_direction(tangent[:3], normal, REFERENCE.identity(), normal)
                    if direction is None:
                        undefined.append(vertex)
                        self.assertEqual("morphed_tangent_parallel_to_normal", reason)
                self.assertEqual([8, 9, 10, 11, 16, 17, 18, 19], undefined)


if __name__ == "__main__":
    unittest.main()
