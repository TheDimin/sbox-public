#!/usr/bin/env python3
"""Tiny codec fixtures only; these tests are not s&box model capture evidence."""
import importlib.util
from pathlib import Path
import tempfile
import unittest

from PIL import Image

WRITER = Path(__file__).resolve().parents[3] / "scripts" / "build_motion_media.py"
SPEC = importlib.util.spec_from_file_location("gltf_shared_motion_writer", WRITER)
MEDIA = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MEDIA)


class MotionMediaTimingTests(unittest.TestCase):
    def test_actual_sixty_hertz_source_times_preserve_partial_final_interval(self):
        duration = 4.199997425079346
        clip = {"duration": duration, "frames": [{"requested_time": frame/60} for frame in range(252)]}
        delays = MEDIA.frame_durations_ms(clip)
        self.assertEqual(252, len(delays))
        self.assertAlmostEqual(1000/60, delays[0], places=12)
        self.assertLess(delays[-1], 1000/60)
        self.assertAlmostEqual(duration*1000, sum(delays), places=8)
        self.assertGreater(abs(17*252-duration*1000), 80)  # Sensitivity to previous pre-rounding.

    def test_nonuniform_capture_times_are_not_replaced_by_uniform_rate(self):
        clip = {"duration": .08, "frames": [{"requested_time": value} for value in (0, .01, .035, .05)]}
        delays = MEDIA.frame_durations_ms(clip)
        for expected, actual in zip((10, 25, 15, 30), delays):
            self.assertAlmostEqual(expected, actual, places=10)

    def test_invalid_or_missing_sample_times_fail_without_inventing_frames(self):
        cases = [
            {"duration": 1, "frames": []},
            {"duration": 1, "frames": [{"requested_time": .1}]},
            {"duration": 1, "frames": [{"requested_time": 0}, {"requested_time": 0}]},
            {"duration": 1, "frames": [{"requested_time": 0}, {"requested_time": 1}]},
            {"duration": 1, "frames": [{"requested_time": 0}, {"requested_time": float("nan")}]},
            {"duration": 1, "frames": [{"requested_time": 0}, {}]},
            {"duration": float("inf"), "frames": [{"requested_time": 0}]},
        ]
        for clip in cases:
            with self.subTest(clip=clip), self.assertRaises(ValueError):
                MEDIA.frame_durations_ms(clip)

    def test_rational_container_keeps_252_actual_fixture_states_and_total_time(self):
        duration = 4.199997425079346
        clip = {"duration": duration, "frames": [{"requested_time": frame/60} for frame in range(252)]}
        frames = self.images([(frame % 256, (frame*7) % 256, 91, 255) for frame in range(252)])
        self.addCleanup(lambda: [frame.close() for frame in frames])
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory)/"unit-fixture.png"
            measured = MEDIA.write_timed_apng(frames, output, MEDIA.frame_durations_ms(clip))
            self.assertEqual(252, measured["sampled_frames"])
            self.assertEqual(252, measured["encoded_frames"])
            self.assertLess(abs(measured["encoded_duration_seconds"]-duration), 1e-7)
            self.assertLess(measured["maximum_hold_timing_error_seconds"], 1e-7)
            self.assertEqual(252, len(MEDIA.read_apng_delays(output)))

    def test_identical_held_samples_keep_every_logical_capture_and_hold_duration(self):
        frames = self.images([(255, 0, 0, 255)]*2 + [(0, 0, 255, 255)]*2 + [(0, 255, 0, 255)])
        self.addCleanup(lambda: [frame.close() for frame in frames])
        delays = [3.5, 7.25, 9.5, 12.125, 15.625]
        with tempfile.TemporaryDirectory() as directory:
            measured = MEDIA.write_timed_apng(frames, Path(directory)/"unit-holds.png", delays)
            self.assertEqual(5, measured["sampled_frames"])
            self.assertLessEqual(measured["encoded_frames"], 5)
            self.assertAlmostEqual(.048, measured["encoded_duration_seconds"], places=10)
            self.assertEqual(3, measured["visible_holds"])

    def test_changed_composited_pixels_cannot_pass_verification(self):
        frames = self.images([(255, 0, 0, 255), (0, 0, 255, 255)])
        mutated = self.images([(255, 0, 0, 255), (0, 255, 0, 255)])
        self.addCleanup(lambda: [frame.close() for frame in frames+mutated])
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory)/"unit-pixels.png"
            MEDIA.write_timed_apng(frames, output, [10, 20])
            with self.assertRaises(ValueError):
                MEDIA.verify_timed_apng(mutated, output, [10, 20])

    def test_old_rounded_duration_regression_is_detected(self):
        frames = self.images([(255, 0, 0, 255), (0, 0, 255, 255), (0, 255, 0, 255)])
        self.addCleanup(lambda: [frame.close() for frame in frames])
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory)/"unit-wrong-speed.png"
            frames[0].save(output, format="PNG", save_all=True, append_images=frames[1:], duration=17, loop=0, disposal=0, blend=0)
            with self.assertRaises(ValueError):
                MEDIA.verify_timed_apng(frames, output, [1000/60]*3)

    def test_container_crc_corruption_is_detected(self):
        frames = self.images([(255, 0, 0, 255), (0, 0, 255, 255)])
        self.addCleanup(lambda: [frame.close() for frame in frames])
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory)/"unit-corrupt.png"
            MEDIA.write_timed_apng(frames, output, [10, 20])
            data = bytearray(output.read_bytes())
            marker = data.index(b"fcTL")
            data[marker+24] ^= 1
            output.write_bytes(data)
            with self.assertRaises(ValueError):
                MEDIA.read_apng_delays(output)

    @staticmethod
    def images(colors):
        return [Image.new("RGBA", (2, 2), color) for color in colors]


if __name__ == "__main__":
    unittest.main()
