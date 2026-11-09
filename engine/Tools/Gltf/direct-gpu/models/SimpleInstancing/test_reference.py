"""Bounded independent oracle checks; no engine process, compiler or GPU."""
import math
import unittest
from author_reference import build, quaternion_rotation, transform


class OracleTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.reference = build()

    def test_all_pinned_layouts_and_grid(self):
        self.assertEqual([a["stride"] for a in self.reference["accessors"]], [4,12,12,12,16,12])
        self.assertEqual(self.reference["instances"], 125)
        self.assertEqual(self.reference["accessors"][3]["values"][31], [2.5,2.5,2.5])

    def test_last_quaternion_is_120_degrees_not_readme_90(self):
        q = self.reference["accessors"][4]["values"][-1]
        self.assertAlmostEqual(2*math.acos(q[3])*180/math.pi, 120)
        self.assertNotAlmostEqual(2*math.acos(q[3])*180/math.pi, 90)
        self.assertEqual(quaternion_rotation(q), [[0,0,1],[1,0,0],[0,1,0]])
        self.assertEqual(transform([1,0,0],[10]*3,q,[2]*3), [10,12,10])

    def test_transform_order_and_normals_have_independent_sensitivity(self):
        q = [.2,.3,.1,.9]
        p = transform([1,0,1],[3,5,7],q,[1,1.25,2])
        wrong = transform([4,5,8],[0,0,0],q,[1,1.25,2])
        self.assertGreater(max(abs(a-b) for a,b in zip(p,wrong)), 1)
        n = transform([1,1,0],[0,0,0],q,[1,1.25,2],normal=True)
        self.assertAlmostEqual(sum(x*x for x in n), 1)

    def test_same_mesh_is_not_125_unmodified_cubes(self):
        a = self.reference["accessors"]
        expected = transform(a[1]["values"][1],a[3]["values"][124],a[4]["values"][124],a[5]["values"][124])
        self.assertEqual(expected, [10,12,10])
        self.assertNotEqual(expected, a[1]["values"][1])

    def test_all_analytic_rotations_match_actual_rounded_source(self):
        for i, raw in enumerate(self.reference["accessors"][4]["values"]):
            xyz = [i//25, (i//5)%5, i%5]
            half_angle = max(xyz)*math.pi/16
            q = [v/4*math.sin(half_angle) for v in xyz] + [math.cos(half_angle)]
            length = math.sqrt(sum(v*v for v in q))
            self.assertLess(max(abs(a-b/length) for a,b in zip(raw,q)), 7e-8)


if __name__ == "__main__":
    unittest.main()
