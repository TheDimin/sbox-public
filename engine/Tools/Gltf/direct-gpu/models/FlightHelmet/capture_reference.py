#!/usr/bin/env python3
"""Capture one FlightHelmet source under the shared native lock and guard."""
import hashlib
import json
from pathlib import Path
import subprocess
import sys

from run_owned_tests import cli, corpus, guard, owned, require, root, validation, verify_inputs

sys.path.insert(0, str(root / "engine/Tools/Gltf/scripts"))
from run_editor_probes import check_status
from run_editor_tests import CliSession, discover, tool_value, validate_arguments


def digest(data):
    return hashlib.sha256(data).hexdigest()


def save(path, value):
    path.write_text(json.dumps(value, indent=2) + "\n")


def main(output):
    ready = json.loads((validation / "editor-session-ready.json").read_bytes())
    require(ready["ready"] and ready["bootId"] == Path("/proc/sys/kernel/random/boot_id").read_text().strip(),
            "Readiness marker does not name this boot")
    pid = ready["pid"]
    project = Path(ready["project"]).resolve()
    host = json.loads(subprocess.check_output([guard, "status"], text=True))
    editor = next(row for row in host["editors"] if row["pid"] == pid)
    require(str(editor["start"]) == str(ready["start"]) and Path(editor["project"]).resolve() == project,
            "Editor process identity changed")
    approved_bytes = Path(ready["discoveryPath"]).read_bytes()
    require(digest(approved_bytes) == ready["discoverySha256"], "Reviewed discovery differs")
    approved = json.loads(approved_bytes)
    audit = json.loads((owned / "source-audit.json").read_bytes())
    verify_inputs(audit)

    recipe_path = root / "research/gltf-validation/reference-recipes.json"
    recipes = json.loads(recipe_path.read_bytes())
    recipe = next(case for case in recipes["cases"] if case["id"] == "FlightHelmet/glTF-KTX-BasisU")
    camera_fit = json.loads((owned / "reference-fit.json").read_bytes())
    require(camera_fit["referenceSha256"] == recipe["reference"]["sha256"], "Camera fit names a different thumbnail")
    recipe["camera"] = camera_fit["camera"]
    recipe["provenance"]["camera"] = "Inferred from annotated thumbnail edges and pinned POSITION buffer views"
    recipe["provenance"]["presentation"] = "Warm neutral backdrop and authored texture lighting controls chosen for comparison"
    recipe["presentation"]["background"]["color"] = [.31, .27, .25, 1.]
    recipe["presentation"]["lights"].append({
        "type": "directional", "direction": [.75, -.4, .35], "color": [.82, .88, 1.], "intensity": .42,
    })
    recipe["presentation"]["tonemapping"] = {"mode": "ACES", "exposureCompensationEv": 0.25}
    recipe["limitations"] = [
        "The pose and perspective are inferred from the 130px pinned Khronos thumbnail.",
        "The original renderer, gradient studio environment, exposure and lighting are unknown.",
        "Optional KHR_materials_transmission uses the OPAQUE core material fallback on the lenses.",
    ]
    source = corpus / recipe["input"]["path"]
    require(digest(source.read_bytes()) == recipe["input"]["sha256"], "Source hash differs from recipe")
    driver = root / "engine/Tools/Gltf/editor/editor_corpus.csx"
    driver_bytes = driver.read_bytes()
    output.mkdir(parents=True, exist_ok=False)
    transport = output / "transport"
    transport.mkdir()
    request = {
        "schemaVersion": 1, "id": recipe["id"], "inputPath": recipe["input"]["path"], "sourcePath": str(source),
        "corpusRoot": str(corpus), "outputDir": str(output / "case"), "timeoutSeconds": 120, "recipe": recipe,
    }
    save(output / "request.json", request)
    session = CliSession(cli, pid, transport, 180)
    try:
        live, exchange = discover(session, pid, project, {}, transport)
        require(live["tools"] == approved["tools"], "Live CLI schemas differ from reviewed discovery")
        before = tool_value(session.call(live["tools"]["editor_status"], {}))
        check_status(before, project)
        require(not before["SceneHasUnsavedChanges"], "Editor has unsaved scene work")
        save(output / "status-before.json", before)
        script = exchange / driver.name
        script.write_bytes(driver_bytes)
        save(exchange / "gltf-run-case.json", request)
        args = {"path": str(script)}
        validate_arguments(live["tools"]["run_csharp"], args)
        result = tool_value(session.call(live["tools"]["run_csharp"], args))
        save(output / "returned-value.json", result)
        after = tool_value(session.call(live["tools"]["editor_status"], {}))
        check_status(after, project)
        save(output / "status-after.json", after)
        verify_inputs(audit)
        require(result.get("id") == recipe["id"] and not result.get("cleanup_errors"), "Wrong case or cleanup errors")
        require(result["load"]["status"] == "passed" and result["render"]["status"] == "passed", "Load or render failed")
        save(output / "capture-provenance.json", {"case": recipe["id"], "driverSha256": digest(driver_bytes),
             "recipeFileSha256": digest(recipe_path.read_bytes()), "audit": audit, "cameraFit": camera_fit})
        print(json.dumps({"receipt": str(output / "returned-value.json"), "capture": result["capture"],
                          "driverSha256": digest(driver_bytes)}))
    finally:
        save(output / "cli-close.json", session.close())


if __name__ == "__main__":
    main(Path(sys.argv[1]).resolve())
