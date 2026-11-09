"""Check the staged corner-index ABI; this does not execute GPU code."""

import pathlib
import unittest

SOURCE = pathlib.Path(__file__).with_name("gltf_deform_corner_candidate.shader")


class CornerComputeContract(unittest.TestCase):
    def test_tangent_base_and_morphs_use_their_own_vertex_domains(self):
        source = SOURCE.read_text()
        self.assertIn("Read(source,baseVertex)", source)
        self.assertIn("Read(i,vertex) * g_Weights[d.Target]", source)
        self.assertIn("field.Padding0 == 1u ? outputVertex : vertex,vertex", source)
        self.assertIn("MorphedWithBaseVertex(role,semantic,source,vertex,vertex,fallback)", source)
        self.assertIn("uint vertex=g_SourceVertices[outputVertex]", source)

    def test_shared_source_vertex_does_not_force_two_corner_tangents_to_match(self):
        source_vertices = [0, 1, 2, 0, 2, 3]
        corner_tangents = [(1, 0, 0, 1), (1, 0, 0, 1), (1, 0, 0, 1),
                           (-1, 0, 0, -1), (-1, 0, 0, -1), (-1, 0, 0, -1)]
        morph_deltas = [(0, 0.1, 0), (0, 0, 0), (0, 0, 0), (0, 0, 0)]
        self.assertEqual(source_vertices[0], source_vertices[3])
        self.assertNotEqual(corner_tangents[0], corner_tangents[3])
        self.assertEqual(morph_deltas[source_vertices[0]], morph_deltas[source_vertices[3]])

    def test_flat_normal_uses_each_deformed_corner_and_submitted_winding(self):
        source = SOURCE.read_text()
        self.assertIn('g_ReverseWinding < Attribute("ReverseWinding")', source)
        for index in ("ia", "ib", "ic"):
            self.assertIn(f"Position(Position(LocalPosition({index}),Transform({index})),placement)", source)
        self.assertIn("if (g_ReverseWinding!=0) face=-face", source)
        self.assertIn("worldNormal=Unit(face,worldNormal)", source)
        self.assertIn("Find(1,0)==0xffffffffu && field.Padding0!=1u", source)

        # Unequal skinning maps a source XY triangle to XZ. A normal
        # transformed independently at the identity-weighted first vertex is
        # +Z; the actual deformed face normal is -Y in source coordinates.
        cross = lambda a, b: (a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0])
        self.assertEqual((0, -1, 0), cross((1, 0, 0), (0, 0, 1)))
        self.assertNotEqual((0, 0, 1), cross((1, 0, 0), (0, 0, 1)))


if __name__ == "__main__":
    unittest.main()
