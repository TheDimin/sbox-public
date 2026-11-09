#!/usr/bin/env python3
"""Import the documented Footprint Court environment under one native lease."""
import argparse
import json
import os
from pathlib import Path
import sys
import threading
import uuid

BASE = Path(__file__).resolve().parents[3]
CHECKOUT = BASE.parents[2]
sys.path.insert(0, str(BASE / 'scripts'))

from PIL import Image, ImageChops, ImageStat
from prepare_environment_controls import prepare
from run_editor_probes import invoke_probe, result_value
from run_editor_tests import CliSession, bounded, check_status, decoded, digest, discover, require, save, tool_value

TEMPLATE_STEM = "materials/gltf_reference_environment/footprint_court"
FIXTURE_SHA = {
    ".exr": "aa39bda8a22575f3402660b47f6855b28c1d7949c3d15b7c3aa7ec6c087c34cb",
    ".vtex": "9cd68c2cb328ee1f4efbf66befb741455a149a8b5953d778f244482a8f5b6a37",
}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--pid", type=int, required=True)
    parser.add_argument("--project", type=Path, required=True)
    parser.add_argument("--confirmed-discovery", type=Path, required=True)
    parser.add_argument("--confirmed-sha256", required=True)
    parser.add_argument("--fixture", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--admitted", action="store_true")
    args = parser.parse_args()
    require(args.admitted, "Hold the shared test lock and host resource guard")
    output = args.output.resolve()
    require(output != Path("/tmp") and output.is_relative_to("/tmp") and not output.exists(), "Use fresh /tmp output")
    project = args.project.resolve(strict=True)
    reviewed_raw = bounded(args.confirmed_discovery)
    require(digest(reviewed_raw) == args.confirmed_sha256, "Reviewed discovery changed")
    reviewed = decoded(reviewed_raw)
    require(reviewed.get("version") == 1 and reviewed.get("mode") == "discover"
            and reviewed.get("tests_dispatched") is False and reviewed.get("pid") == args.pid
            and reviewed.get("project") == str(project), "Reviewed discovery identifies another process or project")

    token = uuid.uuid4().hex
    stem = f"materials/codex_gltf_ibl_{token}/footprint_court"
    targets = {suffix: (CHECKOUT / "game/core" / stem).with_suffix(suffix) for suffix in FIXTURE_SHA}
    compiled = targets[".vtex"].with_suffix(".vtex_c")
    for path in [*targets.values(), compiled]:
        require(not path.exists() and not path.is_symlink(), f"Destination already exists: {path}")
    staged_bytes = {suffix: bounded(args.fixture / path.name, 16 * 1024 * 1024) for suffix, path in targets.items()}
    require(all(digest(raw) == FIXTURE_SHA[suffix] for suffix, raw in staged_bytes.items()), "Fixture digest changed")

    staged_bytes[".vtex"] = staged_bytes[".vtex"].replace(TEMPLATE_STEM.encode(), stem.encode())
    staged_digests = {suffix: digest(raw) for suffix, raw in staged_bytes.items()}

    template = bounded(BASE / "editor" / "editor_environment_import_probe.csx", 65536)
    marker = b'"materials/codex_gltf_ibl_" + token + "/artist_workshop_1k"'
    require(template.count(marker) == 1, "Import template changed")
    import_probe = template.replace(marker, json.dumps(stem).encode())
    corpus_probe = bounded(BASE / "editor" / "editor_corpus.csx", 8 * 1024 * 1024)
    cli = (CHECKOUT / "game/bin/managed/sbox-cli").resolve(strict=True)
    inputs = {"importTemplateSha256": digest(template), "importProbeSha256": digest(import_probe),
              "corpusProbeSha256": digest(corpus_probe), "fixtureSha256": FIXTURE_SHA, "stagedSha256": staged_digests, "cliSha256": digest(bounded(cli))}
    output.mkdir()
    save(output / "inputs.json", inputs)
    session = CliSession(cli, args.pid, output, 180)
    exchange_files = {}
    installed = {}
    directory_owned = False
    import_started = False
    terminal = False

    def put(path, data):
        with path.open("xb") as stream:
            exchange_files[path] = digest(data)
            stream.write(data)

    def remove_exchange(path):
        require(path.is_file() and digest(bounded(path)) == exchange_files[path], f"Exchange file changed: {path}")
        path.unlink()
        exchange_files.pop(path)

    try:
        live, exchange = discover(session, args.pid, project, inputs, output)
        require(live["tools"] == reviewed.get("tools"), "Reviewed schemas changed")
        check_status(tool_value(session.call(live["tools"]["editor_status"], {})), project)
        import_path = exchange / f"gltf-dragon-{token}-import.csx"
        capture_path = exchange / f"gltf-dragon-{token}-response.csx"
        request_path = exchange / "gltf-environment-import.json"
        capture_request = exchange / "gltf-run-case.json"
        require(not capture_request.exists(), "Another capture request is present")
        put(import_path, import_probe)
        put(capture_path, corpus_probe)
        put(request_path, json.dumps({"token": token, "sourceSha256": FIXTURE_SHA[".exr"],
                                      "definitionSha256": staged_digests[".vtex"]}).encode())

        compiled.parent.mkdir()
        directory_owned = True
        # Publish whole files without replacing anything. VTex must appear last.
        for suffix, path in targets.items():
            pending = path.with_name(f"{path.name}.{token}.pending")
            with pending.open("xb") as stream:
                stream.write(staged_bytes[suffix])
            try:
                os.link(pending, path)
                if suffix == ".vtex":
                    import_started = True
                installed[path] = staged_digests[suffix]
            finally:
                pending.unlink()
        raw = invoke_probe(session, live["tools"]["run_csharp"], import_path, output)
        require(raw.get("isError") is not True, "Import transport failed")
        value = result_value(raw)
        save(output / "import-value.json", value)
        require(isinstance(value, dict) and value.get("token") == token and value.get("resource") == stem + ".vtex",
                "Import response identity mismatch")
        terminal = value.get("compileOutcome") == "compiled" or (
            value.get("compileOutcome") == "failed" and value.get("isCompileFailed") is True)
        require(terminal and value.get("passed") is True, "Import did not yield a usable cubemap")
        require(value.get("sourceSha256") == FIXTURE_SHA[".exr"] and value.get("definitionSha256") == staged_digests[".vtex"]
                and Path(value["compiled"]) == compiled and value["compiledSha256"] == digest(bounded(compiled)),
                "Imported source or compiled identity mismatch")
        require(value.get("format") == "BC6H" and value.get("Depth") == 6 and value.get("Mips", 0) > 1,
                "Expected native BC6H cube and mips")
        check_status(tool_value(session.call(live["tools"]["editor_status"], {})), project)

        requests = prepare(output / "requests", stem + ".vtex", compiled, targets[".exr"])
        records = {}
        for name in ("no_environment", "black_environment", "artist_workshop"):
            # The shared fixture generator's last case is its positive cube case.
            label = "footprint_court" if name == "artist_workshop" else name
            put(capture_request, bounded(requests / (name + ".json")))
            raw = invoke_probe(session, live["tools"]["run_csharp"], capture_path, output)
            require(raw.get("isError") is not True, f"{label}: capture transport failed")
            value = result_value(raw)
            save(output / f"{label}-result.json", value)
            require(value.get("id") == "CompareMetallic/glTF" and value.get("render", {}).get("status") == "passed"
                    and not value.get("cleanup_errors"), f"{label}: capture failed")
            records[label] = value["capture"]
            remove_exchange(capture_request)
            check_status(tool_value(session.call(live["tools"]["editor_status"], {})), project)

        def image(label):
            path = Path(records[label]["path"])
            require(digest(bounded(path)) == records[label]["sha256"], f"{label}: capture changed")
            with Image.open(path) as data:
                return data.convert("RGB")

        black = image("black_environment")
        negative = sum(ImageStat.Stat(ImageChops.difference(image("no_environment"), black)).mean) / 3
        positive = sum(ImageStat.Stat(ImageChops.difference(image("footprint_court"), black)).mean) / 3
        save(output / "response.json", {"captures": records, "noVsZeroTintMeanRgb": negative, "positiveVsZeroTintMeanRgb": positive,
                                       "passed": negative <= 1 and positive >= 2 and positive > negative * 2,
                                       "scope": "Visible IBL response in public glTF draws; no orientation, exposure or mip-parity claim."})
        require(negative <= 1 and positive >= 2 and positive > negative * 2, "Positive IBL did not separate from controls")
    finally:
        if import_started and not terminal:
            print("Holding admission until the coordinator confirms native compiler state.", file=sys.stderr, flush=True)
            threading.Event().wait()
        # Before VTex publication the EXR has no compiler consumer. Once VTex
        # is published, preserve its files and receipt for coordinator inspection.
        if not import_started:
            for path, expected in installed.items():
                if path.is_file() and digest(bounded(path)) == expected:
                    path.unlink()
            if directory_owned and not any(compiled.parent.iterdir()):
                compiled.parent.rmdir()
        for path in tuple(exchange_files):
            if path.is_file() and digest(bounded(path)) == exchange_files[path]:
                path.unlink()
        session.close()


if __name__ == "__main__":
    main()
