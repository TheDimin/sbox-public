#!/usr/bin/env python3
"""Append hash-verified actual GPU motion media to an exported comparison dataset.

The base publication remains immutable; earlier producers become historical.
APNGs contain captured frames only. This does not award full sample parity.
"""
import argparse
import copy
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
    parser.add_argument("--motion-manifest", required=True, type=Path)
    parser.add_argument("--motion-sha256", required=True)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    assert digest(args.motion_manifest) == args.motion_sha256, "Motion manifest hash differs"
    motion = json.loads(args.motion_manifest.read_text())
    data = json.loads(args.base.read_text())
    assert motion["full_animation_parity_verified"] is False
    assert motion["native_default_pipeline_verified"] is False
    assert motion["clips"] and len(motion["clips"]) <= 64
    for identifier in {clip["id"] for clip in motion["clips"]}:
        assert {clip["form"] for clip in motion["clips"] if clip["id"] == identifier} == {"original-source", "derived-packaging-glb_c"}, "Both source forms are required for the load gate"
    summary = data["summary"]
    previous = {key: summary.get(key) for key in ("currentEngine", "currentGltf")}
    summary["currentEngine"] = motion["engine_mvid"]
    summary["currentGltf"] = motion["gltf_mvid"]
    summary["currentRenderer"] = motion["renderer_mvid"]
    for evidence in data["evidence"]:
        evidence["historical"] = evidence.get("engineBuild") != motion["engine_mvid"]
        if evidence["historical"] and evidence.get("cameraPreset"):
            evidence["cameraPreset"] = evidence["cameraPreset"].replace(" · refresh pending", " · historical preset")
    for check in data.get("validationChecks", []):
        check["historical"] = check.get("engineBuild") != motion["engine_mvid"]
    cases = {case["id"]: case for case in data["cases"]}
    for case in cases.values():
        for form in case.get("formResults", []):
            form["historical"] = form.get("engineBuild") != motion["engine_mvid"]
        if case.get("status") == "out-of-scope":
            continue
        case.setdefault("previousProducerObservations", []).append({
            **previous, "loadStatus": case.get("loadStatus"),
            "gates": copy.deepcopy(case.get("gates", [])), "error": case.get("error")})
        case.update(status="not-run", loadStatus="not-run", visualStatus="unverified", error=None)
        case["loadHistorical"] = True
        case["compiledLoadHistorical"] = True
        case["compiledLoadStatus"] = "not-run"
        case["gates"] = [
            {"name": "Actual Model.Load", "status": "not-run", "detail": "No result for the declared current producer; earlier evidence remains selectable."},
            {"name": "Full sample parity", "status": "unverified", "detail": "Independent complete geometry, materials, animation, API and appearance acceptance is still required."}]
    args.output.mkdir(parents=True, exist_ok=True)
    assets = args.output / "assets"
    assets.mkdir(exist_ok=True)
    evidence_rows = []
    for clip in motion["clips"]:
        case = cases[clip["id"]]
        source = (args.motion_manifest.parent / clip["file"]).resolve(strict=True)
        assert source.parent == args.motion_manifest.parent.resolve() and not source.is_symlink()
        assert digest(source) == clip["sha256"] and source.stat().st_size == clip["bytes"]
        assert case["sourceHash"] == clip["source_sha256"]
        assert clip["sampled_frames"] == len(clip["frames"]) >= 2
        assert clip["clock_error"] < .002 and clip["seek_reset_difference"] <= 5
        for frame in clip["frames"]:
            assert frame["renderer_mvid"] == motion["renderer_mvid"]
            assert frame["selected_clip_name"] == clip["clip"]
            assert abs(frame["evaluated_time_before"] - frame["evaluated_time_after"]) < .000001
        destination = assets / (clip["sha256"] + ".png")
        shutil.copyfile(source, destination)
        poster = (args.motion_manifest.parent / clip["poster_file"]).resolve(strict=True)
        assert poster.parent == args.motion_manifest.parent.resolve() and not poster.is_symlink()
        assert digest(poster) == clip["poster_sha256"] == clip["frames"][0]["sha256"]
        poster_destination = assets / (clip["poster_sha256"] + ".png")
        shutil.copyfile(poster, poster_destination)
        identifier = "motion-" + hashlib.sha256((clip["id"] + clip["form"] + clip["clip"] + clip["sha256"]).encode()).hexdigest()[:24]
        form = "Source" if clip["form"] == "original-source" else "Compiled GLB_c"
        evidence = {
            "id": identifier, "caseId": case["id"], "modelId": case["modelId"],
            "title": f"{form} · {clip['clip']} · {clip['sampled_frames']} sampled motion frames",
            "image": "assets/" + destination.name, "imageHash": clip["sha256"],
            "posterImage": "assets/" + poster_destination.name, "posterHash": clip["poster_sha256"],
            "kind": "actual-loader-motion", "status": "captured", "historical": False,
            "engineBuild": motion["engine_mvid"], "gltfBuild": motion["gltf_mvid"],
            "rendererBuild": motion["renderer_mvid"], "renderer": "managed SceneBridge.RenderToTexture",
            "sourceHash": clip["source_sha256"], "receiptHash": motion["source_receipt_sha256"],
            "artifactForm": clip["form"], "controlsPassed": True, "comparisonEligible": False,
            "capturedAt": datetime.fromtimestamp(args.motion_manifest.stat().st_mtime, timezone.utc).isoformat(),
            "capturedAtBasis": "Observed motion manifest creation time; original engine frame numbers and clip times retained.",
            "scope": "Actual public Model.Load and normal SkinnedModelRenderer/EditorTick. Managed renderer only; default native pipeline and full sample parity remain unverified.",
            "notes": f"Lossless APNG of {clip['sampled_frames']} actual timed GPU frames, with no generated or interpolated frames. Two component clocks/native poses stay independent; finish, loop and seek-repeatability checks passed. Upstream preview uses different framing/lighting.",
            "clipName": clip["clip"], "clipDuration": clip["duration"],
            "sampledFrames": clip["sampled_frames"], "frameDurationMs": clip["frame_duration_ms"],
            "encodedFrames": clip.get("encoded_frames", clip["sampled_frames"]),
            "sourceTimelines": clip.get("source_timelines", []), "stepEdges": clip.get("step_edges", []),
            "frames": clip["frames"], "width": 192, "height": 192,
            "motionPixels": clip["motion_pixels"], "clockError": clip["clock_error"],
            "seekResetDifference": clip["seek_reset_difference"]}
        framing = clip.get("framing")
        if framing:
            assert framing["camera_preset"] == "source-positive"
            assert framing["source_direction"] == [1, .65, 1]
            assert framing["native_direction"] == [1, 1, .65]
            assert framing["source_up"] == [0, 1, 0] and framing["native_up"] == [0, 0, 1]
            evidence.update(cameraPreset=framing["camera_preset"],
                            sourceCameraDirection=framing["source_direction"], nativeCameraDirection=framing["native_direction"],
                            sourceCameraUp=framing["source_up"], nativeCameraUp=framing["native_up"], cameraFraming=framing)
        evidence_rows.append(evidence)
        case.setdefault("evidenceIds", []).insert(0, identifier)
        case.update(status="passed", loadStatus="passed", visualStatus="unverified", error=None)
        case.update(loadHistorical=False, compiledLoadHistorical=False,
                    loadBuild=motion["engine_mvid"], compiledLoadBuild=motion["engine_mvid"],
                    loadGltfBuild=motion["gltf_mvid"], compiledLoadGltfBuild=motion["gltf_mvid"],
                    compiledLoadStatus="passed", loadReceiptHash=motion["source_receipt_sha256"])
        case["gates"] = [
            {"name": "Actual Model.Load", "status": "passed", "detail": "Original source and compiled-only GLB_c loaded through the normal public API."},
            {"name": "Sampled GPU motion", "status": "passed", "detail": "Actual managed rendering; independent instances, clock, finish, loop and repeatable seek checks passed."},
            {"name": "Full animation / appearance / API parity", "status": "unverified", "detail": "Sampled motion is not complete interpolation conformance, authored skeleton API parity, or default-native rendering acceptance."}]
    data["evidence"] = evidence_rows + data["evidence"]
    for failed in motion.get("failed_cases", []):
        case = cases[failed["id"]]
        assert case["sourceHash"] == failed["source_sha256"]
        case.update(status="failed", loadStatus="failed", visualStatus="unverified", error=failed["error"], loadHistorical=False, loadBuild=motion["engine_mvid"], loadGltfBuild=motion["gltf_mvid"])
        if failed["form"] == "derived-packaging-glb_c":
            case.update(compiledLoadStatus="failed", compiledLoadHistorical=False, compiledLoadBuild=motion["engine_mvid"], compiledLoadGltfBuild=motion["gltf_mvid"])
        case["gates"] = [
            {"name": "Actual Model.Load", "status": "failed", "detail": failed["error"]},
            {"name": "Motion and full parity", "status": "unverified", "detail": "A rejected morph asset has no accepted motion capture; no helper render is substituted."}]
    active = [case for case in cases.values() if case.get("status") != "out-of-scope"]
    summary["loadPassed"] = summary["currentLoadPassed"] = sum(case["loadStatus"] == "passed" for case in active)
    summary["loadFailed"] = summary["currentLoadFailed"] = sum(case["loadStatus"] == "failed" for case in active)
    summary["notRun"] = len(active) - summary["loadPassed"] - summary["loadFailed"]
    summary["compiledLoadPassed"] = summary["currentLoadPassed"]
    summary["compiledLoadFailed"] = summary["currentLoadFailed"]
    summary["currentCapturedModels"] = len({row["modelId"] for row in evidence_rows})
    summary["historicalCapturedModels"] = len({row["modelId"] for row in data["evidence"] if row.get("historical") and row.get("image")})
    summary["sampledMotionClips"] = len(evidence_rows)
    summary["sampledMotionFrames"] = sum(row["sampledFrames"] for row in evidence_rows)
    summary["currentLimitations"] = [
        "Current animated evidence uses the explicitly labeled managed renderer. The default native renderer has no exposed per-mesh FLOAT weight decoder selection; no global renderer setting was changed.",
        "Logical morph/API parity, more than four influences, authored skeleton/API parity, complete interpolation/appearance conformance and compiled-vmdl performance remain incomplete.",
        "Core sampler clamp/mirror/nearest failures remain recorded on their earlier producer; pure-reader tests are separate from captured model acceptance."]
    summary["updatedAt"] = datetime.now(timezone.utc).isoformat()
    summary["unitTests"]["historical"] = True
    summary["notes"] = "Current counts describe sampled motion on the declared producer. Earlier captures retain their exact producer and historical label. No full parity claim."
    data.setdefault("provenance", {})["motionManifestHash"] = args.motion_sha256
    data["provenance"]["motionExportSourceHash"] = digest(Path(__file__))
    data["provenance"]["motionSourceReceiptHash"] = motion["source_receipt_sha256"]
    (args.output / "data.json").write_text(json.dumps(data, indent=2) + "\n")
    print(json.dumps({"current_motion_clips": len(evidence_rows), "current_motion_frames": summary["sampledMotionFrames"], "models": summary["currentCapturedModels"], "output": str(args.output)}))


if __name__ == "__main__":
    main()
