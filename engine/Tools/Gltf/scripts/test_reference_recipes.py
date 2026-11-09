"""Lightweight inventory tests; these do not render or approve visual matches."""
import copy
import json
import math
import struct
from pathlib import Path
import unittest
from fractions import Fraction

import build_reference_recipes as recipes


class ReferenceRecipeTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.inventory = json.loads((recipes.CHECKOUT / "research/gltf-validation/reference-recipes.json").read_text())
        cls.corpus = json.loads((recipes.CHECKOUT / "research/gltf-validation/corpus-case-manifest.json").read_text())

    def test_every_actual_case_once(self):
        actual = [row["id"] for row in self.inventory["cases"]]
        self.assertEqual(337, len(actual))
        self.assertEqual(len(actual), len(set(actual)))
        self.assertEqual({x["id"] for x in self.corpus["cases"]}, set(actual))

    def test_every_model_has_inspected_primary_reference(self):
        models = {x["id"].split("/")[0] for x in self.inventory["cases"]}
        self.assertEqual(150, len(models))
        for row in self.inventory["cases"]:
            self.assertTrue(row["provenance"]["inspectedReference"])
            self.assertEqual("image", row["reference"]["kind"])
            path = Path(self.inventory["corpusRoot"]) / row["reference"]["path"]
            self.assertTrue(path.is_file(), row["id"])
            self.assertEqual(recipes.sha(path), row["reference"]["sha256"])

    def test_declared_pin_and_case_inputs(self):
        self.assertEqual(recipes.PIN, self.inventory["corpusPin"])
        self.assertEqual(1, self.inventory["schemaVersion"])
        cases = {x["id"]: x for x in self.corpus["cases"]}
        for row in self.inventory["cases"]:
            self.assertEqual(cases[row["id"]]["path"], row["input"]["path"])
            self.assertEqual(self.corpus["files"][row["input"]["path"]]["sha256"], row["input"]["sha256"])

    def test_all_recipe_parameters_are_valid(self):
        for row in self.inventory["cases"]:
            recipes.validate_recipe(row)

    def test_no_automatic_reference_match_claim(self):
        for row in self.inventory["cases"]:
            self.assertEqual("needs-tuning", row["matchStatus"])
            self.assertTrue(row["limitations"])
            self.assertNotEqual("exact-reference", row["provenance"]["camera"])

    def test_inspected_presets_are_not_one_universal_camera(self):
        presets = {x["camera"]["preset"] for x in self.inventory["cases"]}
        self.assertGreaterEqual(len(presets), 8)
        by_model = {x["id"].split("/")[0]: x for x in self.inventory["cases"]}
        self.assertEqual("front-flat", by_model["Triangle"]["camera"]["preset"])
        self.assertEqual("orientation-positive", by_model["OrientationTest"]["camera"]["preset"])
        self.assertEqual("interior-corridor", by_model["Sponza"]["camera"]["preset"])

    def test_reference_animation_timing_is_explicit(self):
        for row in self.inventory["cases"]:
            ref = row["reference"]
            self.assertEqual(sum(ref["frameDurationsMs"]), ref["durationMs"])
            self.assertEqual(ref["frames"], len(ref["frameDurationsMs"]))
            if ref["animated"]:
                self.assertIn("timingRelationship", row["animation"])

    def test_fox_records_dense_actual_poses_without_changing_clip_speed(self):
        # Fox's upstream JPEG has no cadence. The older website recorded only
        # 16 Survey poses (214ms holds), which is visibly coarse at normal speed.
        for row in self.inventory["cases"]:
            if not row["id"].startswith("Fox/"):
                continue
            with self.subTest(variant=row["id"]):
                source = recipes.ROOT / row["input"]["path"]
                doc, binary = recipes.corpus.container(source)
                accessor = doc["accessors"][doc["animations"][0]["samplers"][0]["input"]]
                view = doc["bufferViews"][accessor["bufferView"]]
                data = source.read_bytes() if binary else (source.parent / doc["buffers"][view["buffer"]]["uri"]).read_bytes()
                offset = (binary[0] if binary else 0) + view.get("byteOffset", 0) + accessor.get("byteOffset", 0)
                source_duration = struct.unpack_from("<f", data, offset + (accessor["count"] - 1) * view.get("byteStride", 4))[0]
                animation = recipes.animation_for("Fox", doc, row["reference"])
                self.assertEqual(animation, row["animation"], "Run must consume the dense canonical recipe")
                recording = animation["recording"]
                self.assertFalse(row["reference"]["animated"])
                self.assertEqual("apng", recording["kind"])
                self.assertEqual("Survey", animation["name"])
                self.assertEqual(3.4166667461395264, animation["duration"])
                self.assertEqual(source_duration, animation["duration"])
                self.assertEqual(1, animation["rate"])
                self.assertEqual(animation["duration"], recording["duration"])
                self.assertEqual(60, recording["targetFps"])
                self.assertEqual(206, recording["frameCount"])
                times, delays = recording["sampleTimes"], recording["frameDurationsSeconds"]
                self.assertEqual(times, animation["captureTimes"])
                self.assertEqual(206, len(times))
                self.assertEqual(0, times[0])
                self.assertTrue(all(b > a for a, b in zip(times, times[1:])))
                self.assertLess(times[-1], animation["duration"])
                self.assertLessEqual(max(delays), 1 / 60 + 1e-12)
                self.assertAlmostEqual(animation["duration"], sum(delays), places=12)
                encoded = [Fraction(str(delay)).limit_denominator(65535) for delay in delays]
                self.assertTrue(all(0 < delay.numerator <= 65535 for delay in encoded))
                self.assertLess(abs(float(sum(encoded)) - animation["duration"]), 1e-7)
                self.assertIn("chosen", recording["timingProvenance"])
                self.assertIn("static", recording["timingProvenance"])
                # The opt-in must not change other animated assets with still previews.
                control = recipes.animation_for("AnotherModel", doc, row["reference"])
                self.assertEqual("image", control["recording"]["kind"])
                self.assertEqual(1, control["rate"])

    def test_reviewed_cube_and_triangle_capture_one_dense_authored_cycle(self):
        # The old cube samples skipped every moving half-second. The triangle's
        # 1.25-second GIF sampling looped from source time .20 back to zero.
        for model, expected_duration, expected_frames in (
            ("AnimatedColorsCube", 3, 180), ("AnimatedTriangle", 1, 60)
        ):
            row = next(row for row in self.inventory["cases"] if row["id"] == model + "/glTF")
            doc, _ = recipes.corpus.container(recipes.ROOT / row["input"]["path"])
            animation = recipes.animation_for(model, doc, row["reference"])
            recording = animation["recording"]
            with self.subTest(model=model):
                self.assertEqual(1, animation["rate"])
                self.assertEqual(expected_duration, animation["duration"])
                self.assertEqual(expected_duration, recording["duration"])
                self.assertEqual(expected_frames, recording["frameCount"])
                self.assertEqual(0, recording["sampleTimes"][0])
                self.assertLess(recording["sampleTimes"][-1], expected_duration)
                self.assertTrue(all(b > a for a, b in zip(recording["sampleTimes"], recording["sampleTimes"][1:])))
                self.assertAlmostEqual(expected_duration, sum(recording["frameDurationsSeconds"]), places=12)
                self.assertLessEqual(max(recording["frameDurationsSeconds"]), 1 / 60 + 1e-12)
                self.assertIn(.25, recording["sampleTimes"])
                self.assertEqual(recording["sampleTimes"], animation["captureTimes"])

    def test_invalid_zero_camera_direction_fails(self):
        row = copy.deepcopy(self.inventory["cases"][0])
        row["camera"]["position"] = row["camera"]["target"][:]
        with self.assertRaises(ValueError):
            recipes.validate_recipe(row)

    def test_invalid_nonfinite_or_missing_clip_fails(self):
        row = copy.deepcopy(self.inventory["cases"][0])
        row["camera"]["verticalFov"] = math.nan
        with self.assertRaises(ValueError):
            recipes.validate_recipe(row)
        row = copy.deepcopy(self.inventory["cases"][0])
        row["animation"]["recording"]["frameCount"] += 1
        with self.assertRaises(ValueError):
            recipes.validate_recipe(row)
        row = copy.deepcopy(self.inventory["cases"][0])
        row["animation"]["clip"] = -1
        with self.assertRaises(ValueError):
            recipes.validate_recipe(row)

    def test_unsafe_reference_path_fails(self):
        row = copy.deepcopy(self.inventory["cases"][0])
        row["reference"]["path"] = "../outside.png"
        with self.assertRaises(ValueError):
            recipes.validate_recipe(row)


if __name__ == "__main__":
    unittest.main()
