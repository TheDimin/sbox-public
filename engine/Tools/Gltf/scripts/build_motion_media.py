#!/usr/bin/env python3
"""Losslessly package real engine frames as timed APNG; never synthesize frames."""
import argparse
from fractions import Fraction
import hashlib
import json
import math
import shutil
from pathlib import Path
import struct
import zlib

from PIL import Image


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


TIMING_TOLERANCE_SECONDS = 1e-7


def frame_durations_ms(clip):
    """Use actual source sample times, including the final partial interval."""
    try:
        duration = float(clip["duration"])
        times = [float(frame["requested_time"]) for frame in clip["frames"]]
    except (KeyError, TypeError, ValueError) as error:
        raise ValueError("A clip needs its duration and every actual requested_time") from error
    if not math.isfinite(duration) or duration <= 0 or not times or times[0] != 0:
        raise ValueError("A movie needs a finite positive duration and an actual sample at zero")
    if any(not math.isfinite(time) or time < 0 or time >= duration for time in times):
        raise ValueError("Movie sample times must be finite and within [0, duration)")
    ends = [*times[1:], duration]
    if any(end <= start for start, end in zip(times, ends)):
        raise ValueError("Actual movie sample times must be strictly increasing")
    return [(end-start)*1000 for start, end in zip(times, ends)]


def read_apng_delays(path):
    """Read actual fcTL rational delays and validate the PNG chunk CRC/sequence."""
    data = path.read_bytes()
    if data[:8] != b"\x89PNG\r\n\x1a\n":
        raise ValueError("Not a PNG container")
    offset, sequence, declared_frames = 8, 0, None
    delays = []
    ended = False
    while offset < len(data):
        if offset+12 > len(data):
            raise ValueError("Truncated PNG chunk")
        length = struct.unpack_from(">I", data, offset)[0]
        end = offset+12+length
        if end > len(data):
            raise ValueError("PNG chunk exceeds file")
        kind = data[offset+4:offset+8]
        payload = data[offset+8:offset+8+length]
        crc = struct.unpack_from(">I", data, offset+8+length)[0]
        if zlib.crc32(kind+payload) & 0xffffffff != crc:
            raise ValueError("PNG chunk CRC differs")
        if kind == b"acTL":
            if length != 8 or declared_frames is not None:
                raise ValueError("Malformed or duplicate animation control")
            declared_frames = struct.unpack_from(">I", payload)[0]
        if kind in (b"fcTL", b"fdAT"):
            if length < 4 or struct.unpack_from(">I", payload)[0] != sequence:
                raise ValueError("APNG sequence differs")
            sequence += 1
        if kind == b"fcTL":
            if length != 26:
                raise ValueError("Malformed frame control")
            numerator, denominator = struct.unpack_from(">HH", payload, 20)
            if numerator == 0:
                raise ValueError("A captured state must have a positive representable hold")
            delays.append(Fraction(numerator, denominator or 100))
        offset = end
        if kind == b"IEND":
            ended = True
            break
    if not ended or offset != len(data) or not delays or declared_frames != len(delays):
        raise ValueError("Incomplete or inconsistent APNG container")
    return delays


def _holds(pixels, delays):
    holds, end = [], Fraction(0)
    for value, delay in zip(pixels, delays):
        end += delay
        if holds and holds[-1][1] == value:
            holds[-1] = (end, value)
        else:
            holds.append((end, value))
    return holds


def verify_timed_apng(frames, output, durations_ms):
    if not frames or len(frames) != len(durations_ms):
        raise ValueError("Every real captured frame needs one duration")
    if any(not math.isfinite(value) or value <= 0 for value in durations_ms):
        raise ValueError("Captured frame durations must be finite and positive")
    delays = read_apng_delays(output)
    with Image.open(output) as encoded:
        if encoded.n_frames != len(delays):
            raise ValueError("Decoded frame count differs from container controls")
        decoded = []
        for index in range(encoded.n_frames):
            encoded.seek(index)
            if encoded.size != frames[0].size:
                raise ValueError("Encoded frame dimensions differ from captures")
            decoded.append(encoded.convert("RGBA").tobytes())
    originals = [frame.convert("RGBA").tobytes() for frame in frames]
    expected_delays = [Fraction(value)/1000 for value in durations_ms]
    expected_holds = _holds(originals, expected_delays)
    actual_holds = _holds(decoded, delays)
    if len(expected_holds) != len(actual_holds):
        raise ValueError("Encoded visible states differ from actual captures")
    maximum_error = 0.0
    for (expected_end, expected), (actual_end, actual) in zip(expected_holds, actual_holds):
        if actual != expected:
            raise ValueError("APNG composited pixels differ from captured sample")
        error = abs(float(actual_end-expected_end))
        maximum_error = max(maximum_error, error)
        if error > TIMING_TOLERANCE_SECONDS:
            raise ValueError("APNG cumulative hold timing differs from actual sample times")
    # Verify every logical captured sample, including originals that Pillow
    # coalesced into one identical held state. No frame can be invented/dropped.
    actual_timeline = _holds(decoded, delays)
    logical_start, actual_index = Fraction(0), 0
    for original, delay in zip(originals, expected_delays):
        at = logical_start+delay/2
        while actual_index+1 < len(actual_timeline) and at >= actual_timeline[actual_index][0]:
            actual_index += 1
        if at >= actual_timeline[actual_index][0] or original != actual_timeline[actual_index][1]:
            raise ValueError("A logical captured sample is missing at its playback time")
        logical_start += delay
    return {"sampled_frames": len(frames), "encoded_frames": len(delays),
            "visible_holds": len(actual_holds), "encoded_duration_seconds": float(sum(delays)),
            "maximum_hold_timing_error_seconds": maximum_error,
            "encoded_frame_delays": [[delay.numerator, delay.denominator] for delay in delays]}


def write_timed_apng(frames, output, durations_ms):
    if not frames or len(frames) != len(durations_ms) or any(not math.isfinite(value) or value <= 0 for value in durations_ms):
        raise ValueError("Every actual frame needs one finite positive duration")
    # Installed Pillow supports rational fcTL delays. Do not round milliseconds
    # or impose a uniform interval on timestamped samples. Verification below
    # rejects older/coarser encoders instead of silently changing clip speed.
    frames[0].save(output, format="PNG", save_all=True, append_images=frames[1:],
                   duration=durations_ms, loop=0, disposal=0, blend=0, optimize=False)
    return verify_timed_apng(frames, output, durations_ms)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--receipt", required=True, type=Path)
    parser.add_argument("--receipt-sha256", required=True)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    assert digest(args.receipt) == args.receipt_sha256, "Receipt hash differs"
    receipt = json.loads(args.receipt.read_text())
    assert receipt["phase"] == "complete" and receipt["error"] is None
    assert not receipt["cleanup_errors"]
    args.output.mkdir(parents=True, exist_ok=True)
    items, failed = [], []
    for case in receipt["cases"]:
        if not case["passed"]:
            failed.append({"id": case["id"], "form": case["form"],
                           "source_sha256": case["source_sha256"],
                           "error": case.get("error", "").split("\n")[0]})
            continue
        assert case["load"] == case["motion"] == "passed"
        for index, clip in enumerate(case["clips"]):
            assert clip["passed"]
            frames, provenance = [], []
            for frame in clip["frames"]:
                source = Path(frame["path"])
                assert digest(source) == frame["sha256"]
                with Image.open(source) as original:
                    image = original.convert("RGBA")
                assert image.size == (frame["width"], frame["height"]) == (192, 192)
                frames.append(image)
                provenance.append({key: value for key, value in frame.items() if key != "path"})
            name = case["id"].replace("/", "_") + "_" + case["form"] + "_clip" + str(index) + ".png"
            output = args.output / name
            durations_ms = frame_durations_ms(clip)
            timing = write_timed_apng(frames, output, durations_ms)
            poster_name = "poster-" + clip["frames"][0]["sha256"] + ".png"
            shutil.copyfile(clip["frames"][0]["path"], args.output / poster_name)
            items.append({"id": case["id"], "form": case["form"], "clip": clip["name"],
                          "duration": clip["duration"], "sampled_frames": len(frames),
                          "encoded_frames": timing["encoded_frames"],
                          "frame_duration_ms": durations_ms[0] if max(durations_ms)-min(durations_ms) < 1e-10 else None,
                          "frame_durations_ms": durations_ms,
                          "encoded_duration_seconds": timing["encoded_duration_seconds"],
                          "encoded_frame_delays": timing["encoded_frame_delays"],
                          "maximum_hold_timing_error_seconds": timing["maximum_hold_timing_error_seconds"],
                          "file": name, "sha256": digest(output), "bytes": output.stat().st_size,
                          "source_sha256": case["source_sha256"], "provenance": case["provenance"],
                          "framing": case.get("framing"),
                          "frames": provenance, "motion_pixels": clip["max_motion_pixels"],
                          "clock_error": clip["max_clock_error"], "seek_reset_difference": clip["seek_reset_pixel_difference"],
                          "poster_file": poster_name, "poster_sha256": clip["frames"][0]["sha256"],
                          "source_timelines": clip.get("source_timelines", []),
                          "step_edges": [{key: edge[key] for key in ("key", "epsilon", "passed")}
                                         for edge in clip.get("representative_step_edges", [])]})
            for image in frames:
                image.close()
    assert items
    manifest = {"scope": "Lossless APNG containers of actual s&box GPU capture frames. No generated/interpolated frames. Actual requested source times and rational held durations are preserved; identical held samples may share encoded frames. A deterministically sampled movie is separate from wall-clock playback proof.",
                "engine_mvid": receipt["engine_mvid"], "gltf_mvid": receipt["gltf_mvid"],
                "renderer_mvid": items[0]["frames"][0].get("renderer_mvid", receipt.get("renderer_mvid")),
                "corpus_commit": receipt["corpus_commit"], "source_receipt_sha256": args.receipt_sha256,
                "full_animation_parity_verified": False, "native_default_pipeline_verified": False,
                "media_builder_sha256": digest(Path(__file__)), "clips": items, "failed_cases": failed}
    path = args.output / "manifest.json"
    path.write_text(json.dumps(manifest, indent=2) + "\n")
    print(json.dumps({"clips": len(items), "models": len({item["id"] for item in items}),
                      "sampled_frames": sum(item["sampled_frames"] for item in items),
                      "failed_forms": len(failed), "manifest_sha256": digest(path)}))


if __name__ == "__main__":
    main()
