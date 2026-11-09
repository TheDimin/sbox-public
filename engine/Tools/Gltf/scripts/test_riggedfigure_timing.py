"""Reference-time planning checks; these do not render or approve animation."""
import struct
import unittest

import build_reference_recipes as recipes


class RiggedFigureTimingTests(unittest.TestCase):
    def test_reference_cycle_samples_one_complete_authored_clip(self):
        source = recipes.ROOT / "Models/RiggedFigure/glTF/RiggedFigure.gltf"
        doc, _ = recipes.corpus.container(source)
        reference = recipes.media(recipes.ROOT / "Models/RiggedFigure/screenshot/screenshot.gif")
        self.assertEqual(24, reference["frames"])
        self.assertEqual([100] * 24, reference["frameDurationsMs"])
        self.assertEqual(2400, reference["durationMs"])

        # Verify actual source key bytes, not only the accessor max metadata.
        samplers = doc["animations"][0]["samplers"]
        for index in {sampler["input"] for sampler in samplers}:
            accessor = doc["accessors"][index]
            view = doc["bufferViews"][accessor["bufferView"]]
            data = (source.parent / doc["buffers"][view["buffer"]]["uri"]).read_bytes()
            offset = view.get("byteOffset", 0) + accessor.get("byteOffset", 0)
            self.assertEqual(2, accessor["count"])
            self.assertEqual((0.0, 1.25), struct.unpack_from("<2f", data, offset))

        animation = recipes.animation_for("RiggedFigure", doc, reference)
        recording = animation["recording"]
        self.assertEqual(1.25, animation["duration"])
        self.assertAlmostEqual(1.25 / 2.4, animation["rate"])
        self.assertTrue(animation["loop"])
        self.assertEqual(24, recording["frameCount"])
        self.assertEqual([.1] * 24, recording["frameDurationsSeconds"])
        self.assertAlmostEqual(2.4, recording["duration"])
        for i, actual in enumerate(recording["sampleTimes"]):
            self.assertAlmostEqual(1.25 * i / 24, actual)
        self.assertEqual(0, recording["sampleTimes"][0])
        self.assertLess(recording["sampleTimes"][-1], 1.25)
        # The reference itself resets its last raised pose to the first; do not
        # inject an extra frame or wrap midway through the reference cycle.
        self.assertAlmostEqual(1.25 * 23 / 24, recording["sampleTimes"][-1])
        self.assertIn("inferred", animation["timingRelationship"])


if __name__ == "__main__":
    unittest.main()
