#!/usr/bin/env python3
"""Compare Sample Viewer reference renders with s&box captures.

Usage: diff_reference_capture.py OUTDIR CASE_ID...
Writes OUTDIR/<Model>_<Variant>.png (reference | capture | abs diff x4) and OUTDIR/metrics.json.
"""
import json, sys
from pathlib import Path
import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[4]
REND = ROOT / "Temp/gltf-reference/rendered"
import os
CAP = ROOT / os.environ.get("CAPTURE_ROOT", "Temp/gltf-validation/current") / "assets"


def srgb_to_linear(c):
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def stats(img, mask):
    px = img[mask]
    lum = (0.2126 * px[:, 0] + 0.7152 * px[:, 1] + 0.0722 * px[:, 2])
    return {"meanRGB": [round(float(v), 4) for v in px.mean(0)], "meanLuma": round(float(lum.mean()), 4),
            "p95Luma": round(float(np.percentile(lum, 95)), 4), "stdLuma": round(float(lum.std()), 4),
            "clippedPct": round(float((px.max(1) >= 0.995).mean() * 100), 2),
            "saturation": round(float((px.max(1) - px.min(1)).mean()), 4)}


def main():
    out = Path(sys.argv[1]); out.mkdir(parents=True, exist_ok=True)
    metrics = {}
    for case in sys.argv[2:]:
        cap_img = Image.open(CAP / case / "capture.png").convert("RGB")
        ref_img = Image.open(REND / case / "reference.png").convert("RGB").resize(cap_img.size, Image.LANCZOS)
        cap = np.asarray(cap_img, dtype=np.float64) / 255
        ref = np.asarray(ref_img, dtype=np.float64) / 255
        # Foreground: pixels that differ from each image's own corner background.
        def fg(a):
            bg = np.median(np.concatenate([a[:8, :8].reshape(-1, 3), a[:8, -8:].reshape(-1, 3)]), 0)
            return np.abs(a - bg).max(2) > 0.06
        mask = fg(ref) | fg(cap)
        diff = np.abs(ref - cap)
        lr, lc = srgb_to_linear(ref), srgb_to_linear(cap)
        m = {"size": list(cap_img.size), "foregroundPct": round(float(mask.mean() * 100), 1),
             "maeForeground": round(float(diff[mask].mean()), 4),
             "maeForegroundPerChannel": [round(float(v), 4) for v in diff[mask].mean(0)],
             "linearLumaRatio_capture_over_reference": round(float(
                 (lc[mask] @ [0.2126, 0.7152, 0.0722]).mean() / max(1e-6, (lr[mask] @ [0.2126, 0.7152, 0.0722]).mean())), 3),
             "silhouetteIoU": round(float((fg(ref) & fg(cap)).sum() / max(1, mask.sum())), 3),
             "reference": stats(ref, mask), "capture": stats(cap, mask)}
        metrics[case] = m
        w, h = cap_img.size
        sheet = Image.new("RGB", (w * 3, h))
        sheet.paste(ref_img, (0, 0)); sheet.paste(cap_img, (w, 0))
        sheet.paste(Image.fromarray(np.clip(diff * 4 * 255, 0, 255).astype(np.uint8)), (w * 2, 0))
        sheet.save(out / (case.replace("/", "_") + ".png"))
    (out / "metrics.json").write_text(json.dumps(metrics, indent=1) + "\n")
    print(json.dumps(metrics, indent=1))


if __name__ == "__main__":
    main()
