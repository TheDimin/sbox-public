#!/usr/bin/env python3
"""Write the camera and presentation settings for our own Sample Viewer reference renders.

The cameras come from the s&box capture recipes, so the reference render and the
engine capture use the same view. The environment, exposure and tone map are the
Khronos glTF Sample Viewer startup defaults (see the `viewerDefaults.source` field).

Output: research/gltf-validation/reference-render-settings.json
"""
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[4]
RECIPES = ROOT / "Temp/gltf-validation/current/recipes.json"
OUT = ROOT / "research/gltf-validation/reference-render-settings.json"
SCALE = 2  # render references at twice the capture resolution

VIEWER_DEFAULTS = {
    "source": {
        "viewer": "https://github.com/KhronosGroup/glTF-Sample-Viewer src/main.js, src/logic/uimodel.js (main, read 2026-11-09)",
        "renderer": "https://github.com/KhronosGroup/glTF-Sample-Renderer source/GltfState/gltf_state.js; npm @khronosgroup/gltf-viewer 1.1.0",
    },
    "environment": {
        "name": "Cannon_Exterior",
        "url": "https://raw.githubusercontent.com/KhronosGroup/glTF-Sample-Environments/low_resolution_hdrs/Cannon_Exterior.hdr",
        "license": "CC0-1.0, Greg Zaal",
        "rotationDegrees": 90.0,
        "iblIntensity": 1.0,
        "showAsBackground": True,
        "blurBackground": True,
    },
    "exposure": 1.0,
    "toneMap": "KHR_PBR_NEUTRAL",
    "clearColorLinear": [0.188, 0.208, 0.259, 1.0],
    "usePunctual": True,
    "useDirectionalLightsWithDisabledIBL": True,
    "internalMSAA": 4,
}

# The s&box capture side of the same presentation. build_reference_recipes.py
# writes this into every recipe; keep it next to VIEWER_DEFAULTS so both stay in sync.
# yawDegrees: SceneCubemap/SceneSkyBox yaw that puts the panorama where the Sample
# Viewer puts it with rotationDegrees (verified with the sun-lit sky region).
# iblGain: probe tint for viewer IBL intensity 1. Measured 2026-11-09 on 4 models:
# metals fit near 2.0, dielectrics near 1.4 (s&box specular/diffuse IBL balance
# differs); 1.5 keeps diffuse within ~10% and does not over-light dielectrics.
SBOX_ENVIRONMENT = {
    "resource": "materials/gltf_reference_environment/cannon_exterior",
    "source": "Temp/gltf-reference/sample-viewer-assets/Cannon_Exterior.hdr converted losslessly to float EXR (s&box vtex rejects .hdr input)",
    "cubeSize": 256,
    "mipCount": 7,
    "yawDegrees": 180.0,
    "iblGain": 1.5,
    "tonemapping": "KhronosPbrNeutral",
    "gaps": [
        "The visible sky is the sharp cubemap; the Sample Viewer shows a blurred background.",
        "Public Model.Load does not instance KHR_lights_punctual lights, so file lights are absent in captures.",
        "s&box IBL filtering (GGXCubeMapBlur, 256 px cube, BC6H) differs from the viewer's own GGX/Lambertian/Charlie prefilter.",
    ],
}


def main():
    recipes = json.loads(RECIPES.read_text())
    cases = []
    for recipe in recipes["cases"]:
        camera = recipe["camera"]
        resolution = recipe["presentation"]["resolution"]
        animation = recipe["animation"]
        cases.append({
            "id": recipe["id"],
            "input": recipe["input"]["path"],
            "inputSha256": recipe["input"]["sha256"],
            "camera": {
                "space": camera["space"],
                "projection": camera["projection"],
                "position": camera["position"],
                "target": camera["target"],
                "up": camera["up"],
                "verticalFovDegrees": camera["verticalFov"],
                "orthographicHeight": camera["orthographicHeight"],
                "preset": camera.get("preset"),
            },
            "resolution": {"width": resolution["width"] * SCALE, "height": resolution["height"] * SCALE},
            "animation": {
                "clip": animation.get("clip"),
                "name": animation.get("name"),
                "sampleTimes": animation["recording"]["sampleTimes"],
                "kind": animation["recording"]["kind"],
                "fps": animation["recording"].get("fps"),
            },
        })
    OUT.write_text(json.dumps({
        "schemaVersion": 1,
        "corpusRoot": recipes["corpusRoot"],
        "corpusPin": recipes["corpusPin"],
        "recipesFile": str(RECIPES.relative_to(ROOT)),
        "viewerDefaults": VIEWER_DEFAULTS,
        "sboxPresentation": SBOX_ENVIRONMENT,
        "cases": cases,
    }, indent=1) + "\n")
    print(f"{len(cases)} cases -> {OUT.relative_to(ROOT)}")


if __name__ == "__main__":
    main()
