"""Duck's inferred lighting rule; this does not assert visual approval."""
import json
import unittest

import build_reference_recipes as recipes


class DuckLightingTests(unittest.TestCase):
    def test_all_five_variants_choose_neutral_intensity_one(self):
        corpus = json.loads((recipes.CHECKOUT / "research/gltf-validation/corpus-case-manifest.json").read_text())
        variants = [case for case in corpus["cases"] if case["model"] == "Duck"]
        self.assertEqual({"glTF", "glTF-Binary", "glTF-Draco", "glTF-Embedded", "glTF-Quantized"}, {case["variant"] for case in variants})
        direction = [-.4, -.2, -.8]
        for case in variants:
            with self.subTest(case=case["id"]):
                self.assertEqual({"type": "directional", "direction": direction, "color": [1, 1, 1], "intensity": 1}, recipes.directional_light_for(case["model"], direction))
        self.assertEqual(2, recipes.directional_light_for("Box", direction)["intensity"])

    def test_duck_camera_remains_identical_for_each_source_variant(self):
        inventory = json.loads((recipes.CHECKOUT / "research/gltf-validation/reference-recipes.json").read_text())
        rows = [row for row in inventory["cases"] if row["id"].startswith("Duck/")]
        self.assertEqual(5, len(rows))
        for row in rows:
            with self.subTest(case=row["id"]):
                doc, _ = recipes.corpus.container(recipes.ROOT / row["input"]["path"])
                lo, hi, _ = recipes.bounds(doc)
                reference = row["reference"]
                camera, _ = recipes.camera_for("Duck", doc, lo, hi, reference["width"] / reference["height"])
                self.assertEqual(row["camera"], camera)


if __name__ == "__main__":
    unittest.main()
