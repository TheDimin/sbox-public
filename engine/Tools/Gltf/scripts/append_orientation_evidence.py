#!/usr/bin/env python3
"""Append verified paired camera evidence without awarding complete glTF parity."""
import argparse
import hashlib
import json
import shutil
from datetime import datetime, timezone
from pathlib import Path


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base", required=True, type=Path)
    parser.add_argument("--receipt", required=True, type=Path)
    parser.add_argument("--receipt-sha256", required=True)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--default-only", action="store_true", help="Consume the normal corpus source-positive camera, rather than the paired diagnostic")
    parser.add_argument("--allow-load-failures", action="store_true", help="Preserve failed loader forms while importing only controlled captures")
    parser.add_argument("--unit-receipt", type=Path)
    parser.add_argument("--unit-sha256")
    args = parser.parse_args()
    assert digest(args.receipt) == args.receipt_sha256
    run = json.loads(args.receipt.read_text())
    data = json.loads(args.base.read_text())
    assert run["capture_renderer"] == "managed"
    assert run["captured_forms"] in ((2, 4) if args.allow_load_failures else (4,))
    assert not args.allow_load_failures or args.default_only
    assert run["loaded_forms"] == run["captured_forms"]
    assert not run["cleanup_errors"] and run["error"] is None
    assert run["engine_mvid"] == data["summary"]["currentEngine"]
    assert {row["id"] for row in run["cases"]} == {"OrientationTest/glTF", "OrientationTest/glTF-Binary"}
    args.output.mkdir(parents=True, exist_ok=True)
    assets = args.output / "assets"
    assets.mkdir(exist_ok=True)
    cases = {case["id"]: case for case in data["cases"]}
    evidence_rows = []
    for row in run["cases"]:
        case_receipt = Path(row["receipt_path"])
        assert case_receipt.resolve().is_relative_to(Path(run["output"]).resolve())
        result = json.loads(case_receipt.read_text())
        case = cases[row["id"]]
        assert result["source_file_manifest"][result["source_path"]]["sha256"] == case["sourceHash"]
        assert {form["form"] for form in result["forms"]} == {"original-source", "derived-packaging-glb_c"}
        failed_forms = []
        for form in result["forms"]:
            if form["load"]["status"] != "passed":
                assert args.allow_load_failures and form["load"]["status"] == "failed" and form["render_capture"]["status"] == "not_run"
                failed_forms.append(form)
                continue
            assert form["load"]["status"] == "passed"
            observation = form["render_capture"]["observation"]
            assert observation["controls_passed"] and not observation["cleanup_errors"]
            assert observation["source_sha256"] == case["sourceHash"]
            framing = observation["framing"]
            if args.default_only:
                assert framing["camera_preset"] == "source-positive"
                assert framing["source_direction"] == [1, .65, 1] and framing["native_direction"] == [1, 1, .65]
                assert framing["source_up"] == [0, 1, 0] and framing["native_up"] == [0, 0, 1]
                presets = [("source-positive", "albedo", [1, .65, 1], [1, 1, .65])]
            else:
                assert framing["original_colors"]["cyan_negative_X"] > 30
                assert framing["original_colors"]["red_positive_X"] == 0
                assert framing["matched_colors"]["red_positive_X"] > 30
                assert framing["matched_colors"]["cyan_negative_X"] == 0
                presets = (
                ("source-positive", "positive_source_camera_albedo", [1, .65, 1], [1, 1, .65]),
                ("original-negative-X", "original_camera_albedo", [-1, .65, 1], [1, -1, .65]),
                )
            for preset, label, source_direction, native_direction in presets:
                capture = next(p for p in observation["paths"] if p["label"] == label)
                source = Path(capture["path"]).resolve(strict=True)
                assert source.is_relative_to(case_receipt.parent.resolve()) and digest(source) == capture["sha256"]
                assert capture["width"] == capture["height"] == 192
                shutil.copyfile(source, assets / (capture["sha256"] + ".png"))
                identifier = "camera-" + hashlib.sha256((row["id"] + form["form"] + preset + run["engine_mvid"]).encode()).hexdigest()[:24]
                assert not any(e["id"] == identifier for e in data["evidence"])
                colors = framing.get("matched_colors" if preset == "source-positive" else "original_colors")
                if args.default_only:
                    from PIL import Image
                    pixels = list(Image.open(source).convert("RGB").getdata())
                    colors = {
                        "red_positive_X": sum(r > 50 and g < 25 and b < 25 for r, g, b in pixels),
                        "cyan_negative_X": sum(r < 25 and g > 50 and b > 50 for r, g, b in pixels),
                        "green_positive_Y": sum(r < 25 and g > 50 and b < 25 for r, g, b in pixels),
                        "blue_positive_Z": sum(r < 25 and g < 25 and b > 50 for r, g, b in pixels),
                    }
                    assert colors["cyan_negative_X"] == 0 and all(colors[key] > 30 for key in ("red_positive_X", "green_positive_Y", "blue_positive_Z"))
                evidence_rows.append({
                    "id": identifier, "caseId": row["id"], "modelId": "OrientationTest",
                    "title": f"{'Source' if form['form'] == 'original-source' else 'Compiled GLB_c'} · {preset} camera · Albedo",
                    "kind": "actual-loader-render", "status": "captured", "historical": False,
                    "image": "assets/" + capture["sha256"] + ".png", "imageHash": capture["sha256"],
                    "engineBuild": run["engine_mvid"], "gltfBuild": run["gltf_mvid"], "rendererBuild": run["renderer_mvid"],
                    "renderer": "managed SceneBridge.RenderToTexture", "artifactForm": form["form"],
                    "receiptHash": args.receipt_sha256, "caseReceiptHash": digest(case_receipt),
                    "sourceHash": case["sourceHash"], "controlsPassed": True, "comparisonEligible": False,
                    "capturedAt": datetime.fromtimestamp(args.receipt.stat().st_mtime, timezone.utc).isoformat(),
                    "cameraPreset": preset, "preferredCamera": preset == "source-positive",
                    "sourceCameraDirection": source_direction, "nativeCameraDirection": native_direction,
                    "sourceCameraUp": [0, 1, 0], "nativeCameraUp": [0, 0, 1],
                    "cameraFraming": framing if args.default_only else framing["positive_source_camera" if preset == "source-positive" else "original"],
                    "axisColorPixels": colors,
                    "scope": "Camera-only comparison on the same loaded model. B(x,y,z)=(z,x,y), determinant +1; no producer basis change. Exact upstream camera/light calibration is unknown; full sample parity remains unverified.",
                    "notes": "Source-positive camera matches the upstream preview's visible +X/+Y/+Z faces. Original negative-X camera is retained as a control. Albedo debug mode; lighting and calibrated pixel parity are not asserted.",
                })
                case.setdefault("evidenceIds", []).insert(0, identifier)
        failed_names = {form["form"] for form in failed_forms}
        case.update(status="failed" if failed_forms else "passed", loadStatus="failed" if "original-source" in failed_names else "passed", compiledLoadStatus="failed" if "derived-packaging-glb_c" in failed_names else "passed", loadHistorical=False, compiledLoadHistorical=False, visualStatus="unverified", error="\n".join(form["form"] + ": " + form["error"] for form in failed_forms) or None)
        case["loadBuild"] = case["compiledLoadBuild"] = run["engine_mvid"]
        case["gates"] = [
            {"name": "Source and compiled Model.Load", "status": "passed", "detail": "Both actual loader forms returned valid models on the declared producer."},
            {"name": "Source-positive camera default" if args.default_only else "Camera hemisphere regression", "status": "passed", "detail": "Normal corpus camera uses source-positive direction/up and captures red/green/blue with no cyan; fresh empty controls pass. Earlier paired hemisphere control remains historical." if args.default_only else "Same model/renderer: negative-X gives cyan; source-positive gives red, with blue and green visible. Fresh empty controls pass."},
            {"name": "Full sample parity", "status": "unverified", "detail": "Exact upstream camera/light calibration and complete geometry/material acceptance remain unverified."},
        ]
        if failed_forms:
            case["gates"] = [
                {"name": "Actual Model.Load", "status": "failed", "detail": case["error"]},
                {"name": "Current camera / appearance", "status": "unverified", "detail": "No current capture exists for the rejected form. Historical camera evidence remains selectable; it does not establish a current load pass."},
            ]
    assert len(evidence_rows) == (run["captured_forms"] if args.default_only else 8)
    data["evidence"] = evidence_rows + data["evidence"]
    for evidence in data["evidence"]:
        if evidence["kind"] == "actual-loader-motion" and "cameraPreset" not in evidence:
            evidence.update(cameraPreset="original-negative-X · refresh pending", sourceCameraDirection=[-1, .65, 1], nativeCameraDirection=[1, -1, .65], sourceCameraUp=[0, 1, 0], nativeCameraUp=[0, 0, 1])
    current = [e for e in data["evidence"] if e.get("image") and not e.get("historical")]
    data["summary"].update(currentCapturedModels=len({e["modelId"] for e in current}), currentCapturedVariants=len({e["caseId"] for e in current}), currentImages=len(current), updatedAt=datetime.now(timezone.utc).isoformat())
    active = [case for case in cases.values() if case.get("status") != "out-of-scope"]
    for key, field, value in (("loadPassed", "loadStatus", "passed"), ("loadFailed", "loadStatus", "failed"), ("compiledLoadPassed", "compiledLoadStatus", "passed"), ("compiledLoadFailed", "compiledLoadStatus", "failed")):
        data["summary"][key] = sum(case.get(field) == value for case in active)
    data["summary"]["currentLoadPassed"] = data["summary"]["loadPassed"]
    data["summary"]["currentLoadFailed"] = data["summary"]["loadFailed"]
    data["summary"]["notRun"] = len(active) - data["summary"]["loadPassed"] - data["summary"]["loadFailed"]
    if args.unit_receipt:
        assert args.unit_sha256 and digest(args.unit_receipt) == args.unit_sha256
        unit = json.loads(args.unit_receipt.read_text())
        assert unit["is_editor"] and unit["phase"] == "complete" and not unit["cleanup_errors"]
        assert unit["gltf_loaded_mvid"] == data["summary"]["currentGltf"]
        assert unit["expected_cases"] == unit["passed_cases"] + unit["failed_cases"] and unit["blocked_cases"] == unit["partial_cases"] == 0
        data["summary"]["previousUnitTests"] = data["summary"].get("unitTests")
        data["summary"]["unitTests"] = {
            "passed": unit["passed_cases"], "failed": unit["failed_cases"], "total": unit["expected_cases"], "authored": unit["expected_cases"],
            "blocked": 0, "partial": 0, "receiptHash": args.unit_sha256,
            "engineBuild": unit["engine_mvid"], "gltfBuild": unit["gltf_loaded_mvid"], "historical": False,
            "sourceSha256": unit["test_source_sha256"], "manifestHash": unit["manifest_sha256"],
            "recordedAt": datetime.fromtimestamp(args.unit_receipt.stat().st_mtime, timezone.utc).isoformat(),
            "scope": "Full pure reader/evaluator/codec suite in the editor; corpus rendering and API parity remain separate.",
        }
    (args.output / "data.json").write_text(json.dumps(data, indent=2) + "\n")
    print(json.dumps({"added_camera_captures": len(evidence_rows), "current_images": len(current), "current_models": len({e["modelId"] for e in current}), "receipt_sha256": args.receipt_sha256}))


if __name__ == "__main__":
    main()
