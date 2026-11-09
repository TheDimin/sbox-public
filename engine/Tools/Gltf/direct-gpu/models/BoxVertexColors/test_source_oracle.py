"""Small independent controls; no Sandbox.Gltf code or engine execution."""
import pathlib
import unittest

import author_reference as reference


class ColorReferenceTests(unittest.TestCase):
    def test_all_pinned_forms_match_literal_cube(self):
        for path in reference.SOURCES:
            value = reference.read_pinned(path)
            self.assertEqual(value["positions"], reference.POSITIONS)
            self.assertEqual(value["colors"], reference.POSITIONS)
            self.assertEqual(len(value["indices"]), 36)
            self.assertEqual(value["indices"][:6], [0, 2, 1, 0, 3, 2])

    def test_source_cannot_detect_position_color_alias_without_fixture(self):
        self.assertEqual(reference.read_pinned(reference.SOURCES[0])["colors"], reference.POSITIONS)
        for fixture in reference.fixtures():
            decoded = reference.decode(fixture["document"], fixture["binary"])
            self.assertNotEqual([rgba[:3] for rgba in decoded["colors"]], reference.POSITIONS)

    def test_float_and_normalized_integer_fixtures_have_rgb_alpha_one(self):
        for fixture in reference.fixtures():
            actual = reference.decode(fixture["document"], fixture["binary"])
            for vertex, rgba in enumerate(actual["colors"]):
                expected = fixture["expected_colors"][vertex]
                for a, b in zip(rgba, expected):
                    self.assertAlmostEqual(a, b, places=7)
                if fixture["components"] == 3:
                    self.assertEqual(rgba[3], 1)

    def test_clamp_applies_to_zero_but_preserves_other_sets(self):
        raw = [-.25, .5, 1.25, .25]
        self.assertEqual(reference.color_for_draw(raw, 0), [0, .5, 1, .25])
        self.assertEqual(reference.color_for_draw(raw, 1), raw)

    def test_linear_product_detects_gamma_decode_and_missing_alpha(self):
        color, factor, texel = [.5, .25, .75, .5], [.5, .75, .25, .5], [.25, .5, .75, .5]
        self.assertEqual(reference.base_color(color, factor, texel), [.0625, .09375, .140625, .125])
        self.assertGreater(abs(reference.srgb_decode(.5) * .5 * .25 - .0625), .03)


if __name__ == "__main__":
    unittest.main()
