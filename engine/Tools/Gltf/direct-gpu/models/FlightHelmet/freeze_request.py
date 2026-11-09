#!/usr/bin/env python3
"""Bind the owner suite to the actual loaded producer hashes before requesting the editor."""
import hashlib
import json
from pathlib import Path
import sys

root = Path(__file__).resolve().parents[6]
owned = Path(__file__).resolve().parent


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main(output, canonical):
    managed = root / "game/bin/managed"
    shared = root / "research/gltf-validation/direct-gpu"
    sources = [
        shared / "GltfGpuTriangleGateTests.cs",
        shared / "models/Fox/GltfGpuBackendContractTests.cs",
        shared / "models/Fox/GltfGpuRawTriangleChecks.cs",
        owned / "PublicBasisTests.cs",
    ]
    framework = root / "engine/Tests/Sandbox.Test.Gltf/bin/Release/net10.0/Microsoft.VisualStudio.TestPlatform.TestFramework.dll"
    spec = {
        "version": 1,
        **{f"{name.lower()}_sha256": digest(managed / f"Sandbox.{name}.dll") for name in ("Engine", "Gltf", "Tools")},
        "framework_sha256": digest(framework),
        "sources": [{"path": path.relative_to(root).as_posix(), "sha256": digest(path)} for path in sources],
        "suites": [{
            "type": "Sandbox.Gltf.DirectGpu.FlightHelmetValidation." + ("CanonicalBasisTests" if canonical else "PublicBasisTests"),
            "kind": "mstest",
            "expected_cases": 1 if canonical else 3,
            "setup": [{
                "kind": "method",
                "name": "Configure",
                "arguments": [
                    "$checkout",
                    output,
                    digest((Path(__file__).resolve().parents[6]/'research/gltf-validation/direct-gpu/models/FlightHelmet') / "source-audit.json"),
                    digest((Path(__file__).resolve().parents[6]/'research/gltf-validation/direct-gpu/models/FlightHelmet') / "png-reference.json"),
                ],
            }],
            "measurements_property": "Measurements",
        }],
    }
    request = (Path(__file__).resolve().parents[6]/'research/gltf-validation/direct-gpu/models/FlightHelmet') / ("canonical-basis-request.json" if canonical else "public-basis-request.json")
    request.write_text(json.dumps(spec, indent=2) + "\n")
    print(request, digest(request))


if __name__ == "__main__":
    main(sys.argv[1], "--canonical" in sys.argv[2:])
