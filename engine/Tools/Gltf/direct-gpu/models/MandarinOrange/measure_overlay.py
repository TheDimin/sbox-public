#!/usr/bin/env python3
"""Measure aligned native Mandarin RTTs against the pinned standalone reference."""
import argparse
import hashlib
import json
from pathlib import Path

import numpy as np
from PIL import Image, ImageFilter


ROOT = Path(__file__).resolve().parents[6]
REFERENCE = ROOT / "Temp/gltf-reference/glTF-Sample-Assets/Models/MandarinOrange/screenshot/screenshot-large.jpg"
REFERENCE_SHA = "6ad73b4594c28493bd77397da3631eee48859ddc42e31d3fbf90119ca9c58c33"
SIZE = (1151, 1309)
REC709 = np.array([.2126, .7152, .0722])


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def read_rgb(path, expected_sha):
    if sha(path) != expected_sha:
        raise ValueError(f"Bytes changed: {path}")
    with Image.open(path) as image:
        if image.size != SIZE:
            raise ValueError(f"Expected exact {SIZE} pixel alignment: {path}")
        return np.array(image.convert("RGB"), dtype=np.int16)


def morph(mask, filter_type, width):
    return np.asarray(
        Image.fromarray(mask.astype(np.uint8) * 255).filter(filter_type(width))
    ) > 0


def reference_foreground(rgb):
    # Standalone reference has a smooth pink-gray backdrop. Fruit has a marked
    # green-minus-blue separation. Close small JPEG/specular interruptions.
    seeded = rgb[:, :, 1] - rgb[:, :, 2] > 16
    return morph(morph(seeded, ImageFilter.MaxFilter, 5), ImageFilter.MinFilter, 5)


def luminance(rgb, linear):
    rgb = rgb.astype(np.float64) / 255
    if linear:
        rgb = np.where(rgb <= .04045, rgb / 12.92, ((rgb + .055) / 1.055) ** 2.4)
    return rgb @ REC709


def compare(row, reference, reference_mask):
    engine = read_rgb(row["path"], row["sha256"])
    sky = read_rgb(row["sky_path"], row["sky_sha256"])
    engine_mask = np.max(np.abs(engine - sky), axis=2) > 3
    if engine_mask.sum() != row["foreground_pixels"]:
        raise ValueError("Decoded native foreground disagrees with native readback")
    overlap = engine_mask & reference_mask
    union = engine_mask | reference_mask
    # Score the common interior, clear of three pixels of mixed edge coverage.
    matched = morph(overlap, ImageFilter.MinFilter, 7)
    if not matched.any():
        raise ValueError("No common foreground")
    scores = {}
    for linear in (False, True):
        engine_y = luminance(engine[matched], linear)
        reference_y = luminance(reference[matched], linear)
        delta = np.abs(engine_y - reference_y)
        scores["linear_luminance" if linear else "display_luma"] = {
            "similarity": float(np.minimum(engine_y, reference_y).sum() /
                                np.maximum(engine_y, reference_y).sum()),
            "native_mean": float(engine_y.mean()),
            "reference_mean": float(reference_y.mean()),
            "mean_absolute_error": float(delta.mean()),
            "p90_absolute_error": float(np.percentile(delta, 90)),
        }
    return {
        "profile": row["profile"],
        "capture_sha256": row["sha256"],
        "environment_yaw": row["environment_yaw"],
        "fixed_exposure": row["exposure_min"],
        "matched_interior_pixels": int(matched.sum()),
        "engine_foreground_pixels": int(engine_mask.sum()),
        "reference_foreground_pixels": int(reference_mask.sum()),
        "silhouette_iou": float(overlap.sum() / union.sum()),
        **scores,
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("receipt", type=Path)
    args = parser.parse_args()
    receipt = json.loads(args.receipt.read_text())
    if not receipt["passed"] or receipt["cleanup_errors"]:
        raise ValueError("Require an actual passed native receipt with clean teardown")
    reference = read_rgb(REFERENCE, REFERENCE_SHA)
    reference_mask = reference_foreground(reference)
    measures, = receipt["measurements"]
    rows = [row for row in measures["value"] if row["phase"] == "source_hdri_sky_presentation"]
    if not rows:
        raise ValueError("Native receipt is missing presentation rows")
    result = {
        "receipt": str(args.receipt.resolve()),
        "receipt_sha256": sha(args.receipt),
        "reference": str(REFERENCE),
        "reference_sha256": REFERENCE_SHA,
        "dimensions": SIZE,
        "formula": "Sum of pixelwise min(engine Y, reference Y) divided by sum of pixelwise max, over the eroded intersection of the reference and engine foreground masks",
        "display_luma": "Rec.709 coefficients on encoded 8-bit RGB display codes normalized to [0, 1]",
        "linear_luminance": "Rec.709 coefficients after applying the inverse sRGB transfer function",
        "silhouette_iou": "Foreground mask intersection divided by union before erosion",
        "profiles": sorted(
            (compare(row, reference, reference_mask) for row in rows),
            key=lambda result: result["display_luma"]["similarity"], reverse=True,
        ),
    }
    print(json.dumps(result, indent=2))


if __name__ == "__main__":
    main()
