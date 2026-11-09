"""Regression checks for reviewed framing; visual approval still needs captures."""
import json
import math
import unittest

import build_reference_recipes as recipes


def camera(name):
    path = recipes.ROOT / "Models" / name / "glTF" / (name + ".gltf")
    doc = json.loads(path.read_text())
    lo, hi, _ = recipes.bounds(doc)
    return recipes.camera_for(name, doc, lo, hi, 1)[0], lo, hi


class ReviewedCameraTests(unittest.TestCase):
    def test_triangle_frames_rotation_origin_with_room_for_sweep(self):
        view, _, _ = camera("AnimatedTriangle")
        self.assertEqual(view["target"], [0, 0, 0])
        self.assertGreater(view["orthographicHeight"], 2 * math.sqrt(2))

    def test_fox_views_length_from_the_side(self):
        view, lo, hi = camera("Fox")
        self.assertGreater(hi[2] - lo[2], 3 * (hi[0] - lo[0]))
        delta = [a-b for a, b in zip(view["position"], view["target"])]
        self.assertGreater(abs(delta[0]), 3 * abs(delta[2]))

    def test_cube_view_orbits_180_degrees_without_changing_elevation(self):
        view, _, _ = camera("AnimatedMorphCube")
        delta = [a-b for a, b in zip(view["position"], view["target"])]
        self.assertGreater(abs(delta[0]), 3 * abs(delta[2]))
        self.assertLess(delta[0], 0)
        self.assertAlmostEqual(delta[1], -.3 * delta[0])
        self.assertEqual(view["up"], [0, 1, 0])

    def test_rigged_simple_view_orbits_180_degrees_without_changing_elevation(self):
        view, _, _ = camera("RiggedSimple")
        delta = [a-b for a, b in zip(view["position"], view["target"])]
        self.assertLess(delta[2], 0)
        self.assertAlmostEqual(delta[0], 0)
        self.assertAlmostEqual(delta[1], -.05 * delta[2])
        self.assertEqual(view["up"], [0, 1, 0])

    def test_foreground_views_do_not_fit_large_backdrops(self):
        for name in ("DragonAttenuation", "DragonDispersion", "DispersionTest"):
            with self.subTest(name=name):
                view, lo, hi = camera(name)
                distance = math.dist(view["position"], view["target"])
                diagonal = math.dist(lo, hi)
                self.assertLess(distance, 1.4 * diagonal)

    def test_duck_view_targets_model_not_unidentified_authored_camera(self):
        view, lo, hi = camera("Duck")
        self.assertEqual(view["preset"], "duck-object-view")
        for x, a, b in zip(view["target"], lo, hi):
            self.assertAlmostEqual(x, (a + b) / 2)
        delta = [a-b for a, b in zip(view["position"], view["target"])]
        self.assertEqual(view["up"], [0, 1, 0])
        self.assertEqual(view["projection"], "perspective")
        self.assertEqual(view["verticalFov"], 45)
        self.assertAlmostEqual(delta[0], delta[2])
        self.assertGreater(delta[0], 0)
        self.assertAlmostEqual(delta[1], .7 * delta[0])
        radius = math.dist(lo, hi) / 2
        self.assertAlmostEqual(math.dist(view["position"], view["target"]), radius * 1.15 / math.sin(math.radians(22.5)))
        # The original authored-camera heading was (1.361,-1.450) in XZ.
        # Opposite the rejected orbit is a negative quarter-turn from it.
        original_heading = math.atan2(1.3613677636, -1.4497096088)
        corrected_heading = math.atan2(delta[0], delta[2])
        self.assertAlmostEqual(math.degrees(corrected_heading-original_heading), -90, delta=3)

    def test_sphere_grid_view_shows_depth(self):
        view, lo, hi = camera("MetalRoughSpheres")
        self.assertEqual(view["preset"], "sphere-grid-depth")
        self.assertNotEqual(view["position"][1], view["target"][1])
        # Literal extents of the transformed sphere-only meshes: X -4..4,
        # Y -2.8..4, Z -3.6...4. Labels extend asymmetrically beyond these.
        for actual, expected in zip(view["target"], [0, .6, -1.6]):
            self.assertAlmostEqual(actual, expected, places=5)
        self.assertAlmostEqual(view["orthographicHeight"], 1.25 * max(b-a for a, b in zip(lo, hi)))

    def test_unreviewed_box_keeps_existing_preset(self):
        view, _, _ = camera("Box")
        self.assertEqual(view["preset"], "box-three-quarter")


if __name__ == "__main__":
    unittest.main()
