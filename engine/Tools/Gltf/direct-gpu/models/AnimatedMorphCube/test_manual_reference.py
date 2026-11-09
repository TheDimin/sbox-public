#!/usr/bin/env python3
"""Independent manual-weight oracle controls; no rendering or GPU evidence."""
import importlib.util
import json
import math
from pathlib import Path
import unittest

SPEC = importlib.util.spec_from_file_location("cube_manual_reference", Path(__file__).with_name("author_reference.py"))
REFERENCE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(REFERENCE)


class ManualReferenceTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.checkout = Path(__file__).resolve().parents[6]
        cls.root = cls.checkout/"Temp/gltf-reference/glTF-Sample-Assets"
        cls.forms = [REFERENCE.author(cls.root/"Models/AnimatedMorphCube"/folder/("AnimatedMorphCube."+extension), cls.root)
                     for folder, extension in (("glTF", "gltf"), ("glTF-Binary", "glb"), ("glTF-Quantized", "gltf"))]

    def samples(self, form):
        return {sample["label"]: sample for sample in form["manualSamples"]}

    def test_all_forms_have_nine_explicit_manual_cases_with_stable_identity(self):
        expected = ["signed_first_minus2", "signed_first_minus1", "signed_first_zero", "signed_first_one", "signed_first_two",
                    "override_first_at_middle", "override_first_after_seek", "both_overridden", "clear_first_keep_second"]
        for form in self.forms:
            with self.subTest(form=form["source"]):
                self.assertEqual(expected, [sample["label"] for sample in form["manualSamples"]])
                self.assertEqual(24, form["vertexCount"])

    def test_signed_range_is_not_clamped_normalized_or_used_as_source_time(self):
        for form in self.forms:
            samples = self.samples(form)
            for label, weight in (("minus2", -2), ("minus1", -1), ("zero", 0), ("one", 1), ("two", 2)):
                with self.subTest(form=form["source"], weight=weight):
                    sample = samples["signed_first_"+label]
                    self.assertEqual([weight, 0], sample["weights"])
                    self.assertEqual(0, sample["time"])
                    self.assertEqual({"0": weight, "1": 0}, sample["overrides"])

    def test_signed_first_target_positions_extrapolate_the_same_nonzero_delta(self):
        for form in self.forms:
            samples = self.samples(form)
            zero = samples["signed_first_zero"]["positionsInches"]
            one = samples["signed_first_one"]["positionsInches"]
            magnitude = 0
            for label, weight in (("minus2", -2), ("minus1", -1), ("two", 2)):
                for base, unit, actual in zip(zero, one, samples["signed_first_"+label]["positionsInches"]):
                    for b, u, a in zip(base, unit, actual):
                        magnitude = max(magnitude, abs(u-b))
                        self.assertAlmostEqual(b+weight*(u-b), a, delta=1e-10)
            self.assertGreater(magnitude, 1, "This source control must detect ignored or clamped POSITION deltas.")

    def test_position_only_first_target_keeps_authored_normals_for_every_signed_weight(self):
        for form in self.forms:
            samples = self.samples(form)
            normal = samples["signed_first_zero"]["normals"]
            for label in ("minus2", "minus1", "one", "two"):
                self.assertEqual(normal, samples["signed_first_"+label]["normals"])

    def test_partial_override_uses_the_current_authored_other_weight(self):
        for form in self.forms:
            samples = self.samples(form)
            for label, index in (("override_first_at_middle", 63), ("override_first_after_seek", 32)):
                time = form["keyTimes"][index]
                authored = next(sample for sample in form["samples"] if sample["time"] == time)
                actual = samples[label]
                self.assertEqual(time, actual["time"])
                self.assertEqual({"0": -.5}, actual["overrides"])
                self.assertEqual([-.5, authored["weights"][1]], actual["weights"])

    def test_both_overrides_and_clear_one_keep_other_unbounded_weight(self):
        for form in self.forms:
            samples = self.samples(form)
            authored = next(sample for sample in form["samples"] if sample["time"] == form["keyTimes"][63])
            self.assertEqual([-.5, 2.5], samples["both_overridden"]["weights"])
            self.assertEqual([authored["weights"][0], 2.5], samples["clear_first_keep_second"]["weights"])
            self.assertEqual({"1": 2.5}, samples["clear_first_keep_second"]["overrides"])
            for label in ("both_overridden", "clear_first_keep_second"):
                self.assertTrue(all(math.isfinite(value) for normal in samples[label]["normals"] for value in normal))

    def test_preexisting_authored_geometry_and_movie_expectations_are_unchanged(self):
        old_path = self.checkout/"Temp/gltf-validation/direct-gpu/AnimatedMorphCube-reference.json"
        old_forms = json.loads(old_path.read_text())["forms"]
        for old in old_forms:
            new = next(form for form in self.forms if form["source"] == old["source"])
            for key in ("samples", "media", "targetStreams", "sourceIndices", "sha256", "bufferSha256"):
                self.assertEqual(old[key], new[key], (old["source"], key))


if __name__ == "__main__":
    unittest.main()
