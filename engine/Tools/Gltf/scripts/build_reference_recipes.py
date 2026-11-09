#!/usr/bin/env python3
"""Author deterministic, explicitly inferred recipes from pinned source media.

This reads source data only; it is not an engine loader or a visual approval.
The camera preset classifications follow inspection of all 150 primary previews.
"""
import hashlib
import json
import math
from pathlib import Path
from urllib.parse import quote

from PIL import Image
import build_corpus_manifest as corpus
import build_reference_render_settings as render_settings

HERE = Path(__file__).resolve().parent
CHECKOUT = HERE.parents[3]
ROOT = CHECKOUT / "Temp/gltf-reference/glTF-Sample-Assets"
PIN = "90d7ede14c7e280af263824604b427a1ca02cb66"
RAW = f"https://raw.githubusercontent.com/KhronosGroup/glTF-Sample-Assets/{PIN}/"

# Explicit visual classifications, not a universal camera declared matched.
FRONT = set("AnimatedTriangle AnisotropyDiscTest AnisotropyRotationTest AnisotropyStrengthTest AttenuationTest CompareAlphaCoverage CompareAmbientOcclusion CompareAnisotropy CompareBaseColor CompareClearcoat CompareDispersion CompareEmissiveStrength CompareIor CompareIridescence CompareMetallic CompareNormal CompareRoughness CompareSheen CompareSpecular CompareTransmission CompareVolume DiffuseTransmissionTest EmissiveStrengthTest IORTestGrid InterpolationTest IridescenceSuzanne MeshPrimitiveModes MetalRoughSpheres MetalRoughSpheresNoTextures MultipleScenes NegativeScaleTest NodeVisibilityTest NormalTangentMirrorTest NormalTangentTest PointLightIntensityTest PrimitiveModeNormalsTest SimpleMeshes SimpleMorph SimpleSkin SimpleSparseAccessor SpecGlossVsMetalRough SpecularTest TextureCoordinateTest TextureEncodingTest TextureLinearInterpolationTest TextureSettingsTest TextureTransformMultiTest TextureTransformTest TransmissionOrderTest TransmissionRoughnessTest TransmissionThinwallTestGrid Triangle TriangleWithoutIndices UnlitTest VertexColorTest".split())
THREE_QUARTER = set("Box BoxWithSpaces BoxInterleaved BoxTextured BoxTexturedNonPowerOfTwo BoxVertexColors MultiUVTest UnicodeTest".split())
HIGH = set("ABeautifulGame CarConcept MosquitoInAmber IridescentDishWithOlives MandarinOrange ToyCar".split())
LOW = set("ChairDamaskPurplegold GlamVelvetSofa SheenChair SheenWoodLeatherSofa CommercialRefrigerator ChronographWatch".split())
SLIGHT = set("AnimatedColorsCube MorphStressTest MeshoptCubeTest".split())


def sha(path):
    h = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(65536), b""):
            h.update(block)
    return h.hexdigest()


def normalize(v):
    n = math.sqrt(sum(x*x for x in v))
    if not math.isfinite(n) or n <= 0:
        raise ValueError("Zero/nonfinite direction")
    return [x/n for x in v]


# The capture presentation copies the Khronos glTF Sample Viewer defaults that
# render the reference images (build_reference_render_settings.VIEWER_DEFAULTS):
# Cannon_Exterior IBL and visible sky, exposure 1, no added lights. Punctual
# lights come only from the file. Change both sides together.
VIEWER = render_settings.VIEWER_DEFAULTS
SBOX_ENVIRONMENT = render_settings.SBOX_ENVIRONMENT


def environment_for(name):
    env = SBOX_ENVIRONMENT
    base = CHECKOUT / "game/core" / env["resource"]
    source, compiled = base.with_suffix(".exr"), base.with_suffix(".vtex_c")
    gain = env["iblGain"] * VIEWER["environment"]["iblIntensity"] * VIEWER["exposure"]
    return {"type": "cubemap", "texturePath": env["resource"] + ".vtex",
            "sourcePath": str(source), "sourceSha256": sha(source),
            "compiledPath": str(compiled), "compiledSha256": sha(compiled),
            "width": env["cubeSize"], "height": env["cubeSize"], "mipCount": env["mipCount"],
            "sizeInches": 100000, "yawDegrees": env["yawDegrees"], "tint": [gain, gain, gain, 1],
            "visibleSky": True,
            "sourceReferenceEnvironment": VIEWER["environment"]["name"],
            "viewerRotationDegrees": VIEWER["environment"]["rotationDegrees"]}


def presentation_for(name, color, width, height):
    return {"background": {"type": "solid", "color": color},
            "environment": environment_for(name),
            # No added lights: file punctual lights only, as in the Sample Viewer.
            "lights": [],
            "exposure": VIEWER["exposure"],
            "tonemapping": {"mode": SBOX_ENVIRONMENT["tonemapping"], "exposureCompensationEv": 0},
            "resolution": {"width": width, "height": height}}


def point(m, p):
    return [sum(m[i][j]*p[j] for j in range(3))+m[i][3] for i in range(3)]


def bounds(doc):
    rows = corpus.selected_nodes(doc)
    if rows is None:
        rows = [(i, corpus.local(node)) for i, node in enumerate(doc.get("nodes", []))]
    points = []
    for index, world in rows:
        node = doc["nodes"][index]
        if "mesh" not in node:
            continue
        for prim in doc["meshes"][node["mesh"]]["primitives"]:
            position = prim.get("attributes", {}).get("POSITION")
            if position is None:
                continue
            a = doc["accessors"][position]
            lo, hi = a.get("min"), a.get("max")
            if not lo or not hi:
                continue
            if a.get("normalized"):
                divisor = {5120:127, 5121:255, 5122:32767, 5123:65535}.get(a["componentType"], 1)
                lo = [max(-1, x/divisor) for x in lo]
                hi = [max(-1, x/divisor) for x in hi]
            for mask in range(8):
                points.append(point(world, [hi[j] if mask & (1 << j) else lo[j] for j in range(3)]))
    if not points:
        return [-.5,-.5,-.5], [.5,.5,.5], "fallback-unit-box"
    return [min(x[j] for x in points) for j in range(3)], [max(x[j] for x in points) for j in range(3)], "declared-position-bounds/node-rest-transforms"


def camera_for(name, doc, lo, hi, aspect):
    target = [(a+b)*.5 for a,b in zip(lo,hi)]
    ext = [b-a for a,b in zip(lo,hi)]
    radius = max(math.sqrt(sum(x*x for x in ext))*.5, .01)
    # These views are inferred from the pinned previews, not source-authored
    # camera claims. Frame the animated pivot or foreground instead of the
    # static rest bounds / large photographic backdrop. For a reviewed 180°
    # presentation correction, orbit the camera around the model's up axis;
    # never change the shared glTF basis or other models' approved views.
    if name == "AnimatedTriangle":
        return {"space":"gltf-metres","position":[0,0,4],"target":[0,0,0],"up":[0,1,0],"projection":"orthographic","verticalFov":45,"orthographicHeight":4.5,"preset":"triangle-rotation-origin"}, "inferred-from-preview"
    if name in {"DragonAttenuation", "DragonDispersion"}:
        return {"space":"gltf-metres","position":[0,.7,5.7],"target":[0,.5,0],"up":[0,1,0],"projection":"perspective","verticalFov":45,"orthographicHeight":4.5,"preset":"dragon-foreground"}, "inferred-from-preview"
    if name == "DispersionTest":
        return {"space":"gltf-metres","position":[.04,.025,.21],"target":[0,0,0],"up":[0,1,0],"projection":"perspective","verticalFov":45,"orthographicHeight":.15,"preset":"dispersion-foreground"}, "inferred-from-preview"
    if name == "Duck":
        # The selected authored camera points away from the object; upstream
        # does not identify the preview camera, so use an inferred object view.
        direction = normalize([1,.7,1]); distance = radius*1.15/math.sin(math.radians(45)*.5)
        return {"space":"gltf-metres","position":[a+b*distance for a,b in zip(target,direction)],"target":target,"up":[0,1,0],"projection":"perspective","verticalFov":45,"orthographicHeight":radius*2.05,"preset":"duck-object-view"}, "inferred-from-preview"
    preset, direction, projection = "object-orbit", [1,.45,1.6], "perspective"
    if name == "MetalRoughSpheres":
        # Four named meshes contain only the grid; the fifth also contains
        # labels whose asymmetric extent must not move its visual center.
        grid_meshes = {"Spheres.001", "Spheres.002", "Spheres.003", "Spheres.004"}
        grid_points = []
        for index, world in corpus.selected_nodes(doc) or []:
            node = doc["nodes"][index]
            if "mesh" not in node: continue
            mesh = doc["meshes"][node["mesh"]]
            if mesh.get("name") not in grid_meshes: continue
            for prim in mesh["primitives"]:
                accessor = doc["accessors"][prim["attributes"]["POSITION"]]
                low, high = accessor["min"], accessor["max"]
                grid_points.extend(point(world, [high[j] if mask & (1 << j) else low[j] for j in range(3)]) for mask in range(8))
        if not grid_points: raise ValueError("MetalRoughSpheres grid bounds unavailable")
        target = [(min(p[j] for p in grid_points) + max(p[j] for p in grid_points)) * .5 for j in range(3)]
        preset, direction, projection = "sphere-grid-depth", [.08,.12,1], "orthographic"
    elif name in FRONT: preset, direction, projection = "front-flat", [0,0,1], "orthographic"
    elif name in THREE_QUARTER: preset, direction = "box-three-quarter", [1,.65,1]
    elif name in HIGH: preset, direction = "high-object", [1,1,1.5]
    elif name in LOW: preset, direction = "low-product", [1,.25,2]
    elif name in SLIGHT: preset, direction, projection = "shallow-grid", [.15,.12,1], "orthographic"
    elif name == "OrientationTest": preset, direction, projection = "orientation-positive", [1,.65,1], "orthographic"
    elif name == "Fox": preset, direction = "fox-right-profile", [2,.3,.5]
    elif name == "CesiumMan": preset, direction = "walking-character", [1,.3,2]
    elif name == "AnimatedMorphCube": preset, direction, projection = "morph-cube-quarter-turn", [-1,.3,0], "orthographic"
    elif name == "BoxAnimated": preset, direction = "box-animation-tall", [1,.35,1.5]
    elif name == "SimpleInstancing": preset, direction = "instancing-lattice", [1,.65,1]
    elif name in {"AnimatedCube","Cube","SciFiHelmet","TwoSidedPlane"}: preset, direction = "cube-preview", [1,.55,1.5]
    elif name == "RiggedFigure": preset, direction = "figure-front", [.35,.15,2]
    elif name == "RiggedSimple": preset, direction, projection = "rigged-column-front", [0,.05,-1], "orthographic"
    elif name in {"IridescenceDielectricSpheres","IridescenceMetallicSpheres","DispersionTest"}: preset, direction = "deep-test-grid", [.8,.45,1.6]
    elif name == "MorphPrimitivesTest": preset, direction = "low-morph-plane", [1,.9,2]
    elif name == "Sponza":
        axis = max((0,2), key=lambda i: ext[i]); position = target[:]; position[axis] = lo[axis]+ext[axis]*.08; position[1] = lo[1]+ext[1]*.25; target[axis] = lo[axis]+ext[axis]*.82; target[1] = position[1]
        return {"space":"gltf-metres","position":position,"target":target,"up":[0,1,0],"projection":"perspective","verticalFov":60,"orthographicHeight":radius*2.2,"preset":"interior-corridor"}, "inferred-from-preview"
    # Use genuine source camera parameters where available, retaining the fact
    # that the upstream preview does not identify which camera rendered it.
    selected = corpus.selected_nodes(doc) or []
    authored = [(i,m) for i,m in selected if "camera" in doc["nodes"][i]]
    if authored:
        i, world = authored[0]; c = doc["cameras"][doc["nodes"][i]["camera"]]; position = point(world,[0,0,0]); forward = normalize([-world[r][2] for r in range(3)]); up = normalize([world[r][1] for r in range(3)]); target = [a+b*radius*2 for a,b in zip(position,forward)]
        return {"space":"gltf-metres","position":position,"target":target,"up":up,"projection":c["type"],"verticalFov":math.degrees(c.get("perspective",{}).get("yfov",math.pi/4)),"orthographicHeight":2*c.get("orthographic",{}).get("ymag",radius*1.1),"preset":"authored-camera","authoredNode":i,"authoredCamera":doc["nodes"][i]["camera"]}, "authored-camera/inferred-selection"
    direction = normalize(direction)
    vertical = max(ext[1], max(ext[0],ext[2])/aspect, .02)*1.25
    distance = radius*1.15/math.sin(math.radians(45)*.5)
    return {"space":"gltf-metres","position":[a+b*distance for a,b in zip(target,direction)],"target":target,"up":[0,1,0],"projection":projection,"verticalFov":45,"orthographicHeight":max(vertical,radius*2.05 if preset not in {"front-flat","rigged-column-front","sphere-grid-depth"} else vertical),"preset":preset}, "inferred-from-preview"


def media(path):
    with Image.open(path) as im:
        durations=[]
        for i in range(getattr(im,"n_frames",1)):
            im.seek(i); durations.append(im.info.get("duration",0))
        return {"path":path.relative_to(ROOT).as_posix(),"url":RAW+quote(path.relative_to(ROOT).as_posix(),safe="/"),"kind":"image","sha256":sha(path),"format":path.suffix[1:].lower(),"width":im.width,"height":im.height,"frames":len(durations),"animated":len(durations)>1,"frameDurationsMs":durations,"durationMs":sum(durations)}


def animation_for(name, doc, reference):
    clips=[]
    for i,a in enumerate(doc.get("animations",[])):
        end=max((doc["accessors"][s["input"]].get("max",[0])[0] for s in a["samplers"]),default=0)
        clips.append({"index":i,"name":a.get("name"),"duration":end})
    chosen=clips[0] if clips else None
    duration=chosen["duration"] if chosen else 0
    motion=bool(reference["animated"] and chosen and duration>0)
    dense_fox=name=="Fox" and chosen is not None and duration>0
    dense_review=name in {"AnimatedColorsCube", "AnimatedTriangle"} and chosen is not None and duration>0
    gif_timing=motion and reference["durationMs"]>0 and all(x>0 for x in reference["frameDurationsMs"])
    rate=1
    if gif_timing and name in {"AnimatedMorphCube","BoxAnimated","RiggedSimple","RiggedFigure"}:
        rate=duration/(reference["durationMs"]/1000)
    if dense_fox or dense_review:
        # Capture more real poses, not faster playback of the old 16-frame clip.
        # Equal intervals also avoid an unencodable sub-microsecond final hold
        # when a FLOAT-authored duration lies just beyond a 60Hz boundary.
        target_fps=60; count=max(2,math.ceil(duration*target_fps))
        times=[duration*i/count for i in range(count)]
        delays=[b-a for a,b in zip(times,[*times[1:],duration])]
        provenance = ("chosen 60fps capture target with evenly spaced actual engine samples at exact authored duration; upstream reference is a static JPEG with no frame cadence" if dense_fox else "chosen 60fps capture target with evenly spaced actual engine samples over one authored cycle; reference GIF timing is not assumed to represent an integer number of cycles")
        recording={"kind":"apng","fps":count/duration,"targetFps":target_fps,"frameCount":count,"duration":duration,"sampleTimes":times,"frameDurationsSeconds":delays,"timingProvenance":provenance}
    elif gif_timing:
        delays=[x/1000 for x in reference["frameDurationsMs"]]; elapsed=[];t=0
        for delay in delays:elapsed.append(t);t+=delay
        times=[(x*rate)%duration for x in elapsed]
        recording={"kind":"apng","fps":len(times)/t,"frameCount":len(times),"duration":t,"sampleTimes":times,"frameDurationsSeconds":delays,"timingProvenance":"exact source GIF delays; source-time mapping inferred from inspected motion"}
    else:
        count=max(1,math.ceil(duration*30)) if motion else 1
        times=[i/30 for i in range(count)] if motion else [0]
        recording={"kind":"apng" if motion else "image","fps":30 if motion else None,"frameCount":count,"duration":duration if motion else 0,"sampleTimes":times,"frameDurationsSeconds":[min(1/30,duration-t) for t in times] if motion else [],"timingProvenance":"authored-duration playback at inferred 30fps; not four deterministic stills" if motion else "static reference: one selected pose"}
    result={"clip":chosen["index"] if chosen else None,"name":chosen["name"] if chosen else None,"time":0,"rate":rate,"duration":duration,"loop":bool(chosen),"captureTimes":[0]+([duration*.25,duration*.5,duration*.75] if chosen and duration>0 else []),"recording":recording,"clips":clips,"referenceDuration":reference["durationMs"]/1000,"timingRelationship":"source GIF delays exact; phase and cycle/rate mapping inferred, not an engine clock semantic change" if gif_timing else "authored rate retained; reference correspondence needs tuning"}
    if dense_fox or dense_review:
        result["captureTimes"]=times
        if dense_review:
            result["timingRelationship"]="one authored cycle at original speed; reference GIF timing and phase are not assumed to match"
    if name=="InterpolationTest":
        result["simultaneousClips"]=[x["index"] for x in clips]
        result["requires"]=["simultaneous independent clips: upstream GIF visibly animates all nine nodes"]
    return result


def apply_reviewed_presentation(name, row):
    # Actual source+canonical captures and independent contrast/arrow checks:
    # texture-background-audit/TextureTransformTest-feedback-result.json.
    if name == "TextureTransformTest":
        row["camera"]["orthographicHeight"] = 2.6
        row["camera"]["preset"] = "texture-transform-preview-framing"
    return row


def validate_recipe(row):
    c=row["camera"]
    if c["space"]!="gltf-metres" or c["projection"] not in {"perspective","orthographic"}: raise ValueError("Camera space/projection")
    for key in ["position","target","up"]:
        if len(c[key])!=3 or not all(math.isfinite(x) for x in c[key]): raise ValueError("Camera vector")
    d=normalize([b-a for a,b in zip(c["position"],c["target"])]);u=normalize(c["up"])
    if abs(sum(a*b for a,b in zip(d,u)))>.999: raise ValueError("Parallel camera up")
    if not math.isfinite(c["verticalFov"]) or not 0<c["verticalFov"]<179 or not math.isfinite(c["orthographicHeight"]) or c["orthographicHeight"]<=0: raise ValueError("Camera projection value")
    a=row["animation"]
    if a["clip"] is not None and (not isinstance(a["clip"],int) or a["clip"]<0): raise ValueError("Animation clip")
    if not all(math.isfinite(a[k]) for k in ["time","rate","duration"]) or a["duration"]<0: raise ValueError("Animation value")
    recording=a["recording"]; times=recording["sampleTimes"]
    if recording["kind"] not in {"image","apng","video"} or recording["frameCount"]!=len(times) or not times: raise ValueError("Recording inventory")
    if not all(math.isfinite(t) and 0<=t<=a["duration"]+1e-8 for t in times): raise ValueError("Recording source time")
    if recording["kind"]=="image":
        if len(times)!=1 or recording["duration"]!=0 or recording["frameDurationsSeconds"]: raise ValueError("Static recording")
    else:
        delays=recording["frameDurationsSeconds"]
        if recording["fps"] is None or not math.isfinite(recording["fps"]) or recording["fps"]<=0: raise ValueError("Recording fps")
        if len(delays)!=len(times) or not all(math.isfinite(t) and t>0 for t in delays): raise ValueError("Recording delays")
        if not math.isfinite(recording["duration"]) or recording["duration"]<=0 or not math.isclose(sum(delays),recording["duration"],rel_tol=1e-9,abs_tol=1e-9): raise ValueError("Recording duration")
    ref=Path(row["reference"]["path"])
    if ref.is_absolute() or ".." in ref.parts: raise ValueError("Reference escapes corpus")
    if row["presentation"]["resolution"]["width"]<=0 or row["presentation"]["resolution"]["height"]<=0: raise ValueError("Viewport")


def build():
    manifest=json.loads(((Path(__file__).resolve().parents[4]/'research/gltf-validation') / "corpus-case-manifest.json").read_text()); index=json.loads((ROOT/"Models/model-index.json").read_text()); models={x["name"]:x for x in index}; references={name:media(ROOT/"Models"/name/x["screenshot"]) for name,x in models.items()}; rows=[]
    for case in manifest["cases"]:
        name=case["model"]; source=ROOT/case["path"]; doc,_=corpus.container(source); ref=references[name]; lo,hi,origin=bounds(doc)
        if name=="SimpleInstancing":
            existing=json.loads(((Path(__file__).resolve().parents[4]/'research/gltf-validation') / "direct-gpu/models/SimpleInstancing/reference.json").read_text())
            if existing["revision"]!=PIN: raise ValueError("Instancing reference pin changed")
            lo,hi=existing["world_bounds"]["min"],existing["world_bounds"]["max"]
            origin="existing independent SimpleInstancing reference.json: bounds of all125 instance rows"
        if name=="BoxAnimated":
            hi[1]+=2.5199999809265137
            origin+="; authored translation maximum2.5199999809265137m included for top box motion"
        camera,provenance=camera_for(name,doc,lo,hi,ref["width"]/ref["height"]); anim=animation_for(name,doc,ref)
        longest=512; scale=longest/max(ref["width"],ref["height"]); width=max(64,round(ref["width"]*scale));height=max(64,round(ref["height"]*scale)); black=name in {"Triangle","TriangleWithoutIndices","SimpleMeshes","SimpleSparseAccessor","MeshPrimitiveModes","AnimatedTriangle","Cameras","PointLightIntensityTest","TrafficCone"}; white=name.startswith("Compare") or name in {"MaterialsVariantsShoe","StainedGlassLamp","SunglassesKhronos","IridescentDishWithOlives"}; color=[0,0,0,1] if black else [1,1,1,1] if white else [.35,.35,.35,1];forward=normalize([b-a for a,b in zip(camera["position"],camera["target"])])
        limits=["Lighting copies our Sample Viewer reference renders; both use Khronos PBR Neutral tone mapping; the sky is not blurred, and file punctual lights are not instanced by public Model.Load.","Upstream preview renderer, exact camera selection, environment, exposure and tone mapping are not documented; this is not an exact matched-reference claim.","Reference appearance is accepted only by the user; recipe inference does not approve a model."]
        if provenance.startswith("inferred"):limits.append("Camera inferred from inspected source preview; declared-rest bounds framing may need model-specific tuning for deformation or instancing.")
        if ref["animated"]:limits.append("Reference animation timing is recorded exactly, but authored clip phase/loop count may differ; no playback-rate equivalence is invented.")
        if name=="InterpolationTest":limits.append("Reference animates all nine independent clips simultaneously; a driver supporting only clip0 must report recipe not fully applied.")
        if name=="SimpleSkin":limits.append("The upstream GIF switches to wireframe presentation around its later section; this is not encoded by the source glTF animation. The recipe does not invent a material toggle.")
        if name=="CesiumMan":limits.append("The reference GIF appears to orbit the character; this fixed-camera baseline does not claim to reproduce its undocumented camera motion.")
        if "scene" not in doc:limits.append("Source has no default scene. Recipe framing does not authorize silently selecting another scene or hiding the public loader error.")
        row={"id":case["id"],"input":{"path":case["path"],"sha256":manifest["files"][case["path"]]["sha256"]},"reference":ref,"camera":camera,"presentation":presentation_for(name,color,width,height),"animation":anim,"provenance":{"camera":provenance,"presentation":"Khronos Sample Viewer defaults (Cannon_Exterior IBL, exposure 1, file lights only); see reference-render-settings.json","animation":"authored-timeline with reference-media timing" if anim["clip"] is not None else "static","inspectedReference":True,"reviewMethod":"all150 pinned previews and all15 animated preview phase strips visually inspected","bounds":origin,"notes":["Authored-camera selection is inferred even when its numeric parameters are exact source values."]},"matchStatus":"needs-tuning","limitations":limits}
        apply_reviewed_presentation(name, row)
        validate_recipe(row);rows.append(row)
    return {"schemaVersion":1,"corpusPin":PIN,"corpusRoot":str(ROOT),"caseCount":len(rows),"modelCount":len(models),"visualApproval":"user-only; invalidate when input/reference/recipe/producer/capture changes","cases":rows}


if __name__=="__main__":
    output=(Path(__file__).resolve().parents[4]/'research/gltf-validation') / "reference-recipes.json";temporary=output.with_suffix(".tmp");temporary.write_text(json.dumps(build(),indent=2,allow_nan=False)+"\n");temporary.replace(output);print(output)
