#!/usr/bin/env python3
"""Local glTF comparison site: Khronos reference vs in-engine capture, with feedback saved to disk.

Usage: python3 engine/Tools/Gltf/compare/serve.py [--port 8765]
Feedback file: research/gltf-validation/review-feedback.json
"""
import argparse, json, mimetypes, os, sys, tempfile, threading
from datetime import datetime, timezone
from http.server import ThreadingHTTPServer, BaseHTTPRequestHandler
from pathlib import Path
from urllib.parse import unquote, urlparse

ROOT = Path(__file__).resolve().parents[4]
HERE = Path(__file__).resolve().parent
RUN = ROOT / "Temp/gltf-validation/current/run.json"
RECIPES = ROOT / "Temp/gltf-validation/current/recipes.json"
ASSETS = ROOT / "Temp/gltf-validation/current/assets"
FEEDBACK = ROOT / "research/gltf-validation/review-feedback.json"
RENDERED = ROOT / "Temp/gltf-reference/rendered"
LEGACY = ROOT / "research/gltf-validation/user-feedback.json"
LOCK = threading.Lock()


def load_json(path, default):
    try:
        return json.loads(path.read_text())
    except (OSError, ValueError):
        return default


def corpus_root():
    return Path(load_json(RECIPES, {}).get("corpusRoot", ROOT / "Temp/gltf-reference/glTF-Sample-Assets"))


def first_line(text):
    return (text or "").strip().splitlines()[0][:300] if text else None


def cases():
    run = load_json(RUN, {"cases": []})
    legacy = {}
    for d in load_json(LEGACY, {}).get("decisions", []):
        legacy[d["caseId"]] = {"status": d.get("status"), "notes": d.get("notes", ""), "at": d.get("reviewedAt")}
    out = []
    for c in run["cases"]:
        ref = c.get("recipeSnapshot", {}).get("reference", {})
        cap = c.get("capture") or {}
        cap_path = cap.get("path")
        rendered = RENDERED / c["id"] / "reference.png"
        out.append({
            "rendered": f'{c["id"]}/reference.png' if rendered.is_file() else None,
            "id": c["id"],
            "model": c["id"].split("/")[0],
            "variant": c["id"].split("/", 1)[1] if "/" in c["id"] else "",
            "load": c.get("load", {}).get("status"),
            "loadError": first_line(c.get("load", {}).get("error")),
            "render": c.get("render", {}).get("status"),
            "renderError": first_line(c.get("render", {}).get("error")),
            "reference": ref.get("path") if ref.get("animated") else large_reference(ref.get("path")),
            "referenceAnimated": bool(ref.get("animated")),
            "capture": os.path.relpath(cap_path, ASSETS) if cap_path and Path(cap_path).exists() else None,
            "captureSha": cap.get("sha256"),
            "matchStatus": c.get("recipeSnapshot", {}).get("matchStatus"),
            "legacy": legacy.get(c["id"]),
        })
    return {"runId": run.get("run", {}).get("id") if isinstance(run.get("run"), dict) else None,
            "cases": out}


LARGE_NAMES = ("screenshot_large", "screenshot_Large", "screenshot-large", "large")


def large_reference(rel):
    """Prefer the larger screenshot Khronos ships next to the small one."""
    if not rel:
        return rel
    folder = corpus_root() / Path(rel).parent
    for stem in LARGE_NAMES:
        for ext in (".png", ".jpg", ".jpeg"):
            if (folder / (stem + ext)).is_file():
                return str(Path(rel).parent / (stem + ext))
    return rel


def safe_file(base, rel):
    p = (base / unquote(rel)).resolve()
    return p if p.is_relative_to(base.resolve()) and p.is_file() else None


class H(BaseHTTPRequestHandler):
    def log_message(self, *a):
        pass

    def send(self, code, body, ctype="application/json"):
        if isinstance(body, (dict, list)):
            body = json.dumps(body).encode()
        self.send_response(code)
        self.send_header("Content-Type", ctype)
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self):
        path = urlparse(self.path).path
        if path in ("/", "/index.html"):
            return self.send(200, (HERE / "index.html").read_bytes(), "text/html; charset=utf-8")
        if path == "/api/cases":
            return self.send(200, cases())
        if path == "/api/feedback":
            return self.send(200, load_json(FEEDBACK, {"schemaVersion": 1, "entries": {}}))
        for prefix, base in (("/ref/", corpus_root()), ("/cap/", ASSETS), ("/rend/", RENDERED)):
            if path.startswith(prefix):
                f = safe_file(base, path[len(prefix):])
                if not f:
                    return self.send(404, {"error": "not found"})
                return self.send(200, f.read_bytes(), mimetypes.guess_type(f.name)[0] or "application/octet-stream")
        self.send(404, {"error": "not found"})

    def do_POST(self):
        if urlparse(self.path).path != "/api/feedback":
            return self.send(404, {"error": "not found"})
        try:
            entry = json.loads(self.rfile.read(int(self.headers.get("Content-Length", 0))))
            case_id = entry["caseId"]
        except (ValueError, KeyError):
            return self.send(400, {"error": "bad request"})
        with LOCK:
            data = load_json(FEEDBACK, {"schemaVersion": 1, "entries": {}})
            if entry.get("clear"):
                data["entries"].pop(case_id, None)
            else:
                data["entries"][case_id] = {
                    "verdict": entry.get("verdict"),
                    "issues": entry.get("issues", []),
                    "notes": entry.get("notes", ""),
                    "captureSha256": entry.get("captureSha"),
                    "reviewedAt": datetime.now(timezone.utc).isoformat(timespec="seconds"),
                }
            data["updatedAt"] = datetime.now(timezone.utc).isoformat(timespec="seconds")
            FEEDBACK.parent.mkdir(parents=True, exist_ok=True)
            fd, tmp = tempfile.mkstemp(dir=FEEDBACK.parent, suffix=".tmp")
            with os.fdopen(fd, "w") as f:
                json.dump(data, f, indent=2, sort_keys=True)
                f.write("\n")
            os.replace(tmp, FEEDBACK)
        self.send(200, {"ok": True})


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--port", type=int, default=8765)
    a = ap.parse_args()
    if not RUN.exists():
        sys.exit(f"No run results at {RUN}. Run engine/Tools/Gltf/scripts/Run first.")
    print(f"glTF compare: http://127.0.0.1:{a.port}/  (feedback -> {FEEDBACK.relative_to(ROOT)})")
    ThreadingHTTPServer(("127.0.0.1", a.port), H).serve_forever()


if __name__ == "__main__":
    main()
