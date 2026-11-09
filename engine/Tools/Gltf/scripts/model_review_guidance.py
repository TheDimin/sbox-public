"""Pinned upstream purpose + explicitly editorial, model-specific visual checks.

This catalog describes what to inspect. It never evaluates an image, changes a
capture fingerprint or turns visual approval into glTF conformance.
"""
from pathlib import Path
import hashlib
import re
from urllib.parse import quote

# Each row: model | recommended checks | limits of this comparison.
# The checks/limits are our inference from the pinned README and fixture features,
# not quotations of Khronos acceptance criteria. Source summaries are joined below.
CATALOG = r'''
ABeautifulGame|Check the chess-piece silhouettes, transparent pieces and thickness-dependent tint; look through overlapping pieces at the board.|One board view cannot establish refraction, volume attenuation or every viewing angle; use controlled lighting and backgrounds.
AlphaBlendModeTest|Check OPAQUE ignores texture alpha, BLEND reveals the checks, and MASK follows its 0.25/0.5/0.75 cutoffs.|The README permits thin masked borders disappearing with mipmapping. One view still does not verify overlap/sorting at every angle.
AnimatedColorsCube|Watch the cube's base colors change through the clip as well as its transforms; frozen colors indicate a missing animation target.|A moving cube alone does not verify KHR_animation_pointer material-color animation.
AnimatedCube|Watch the textured cube rotate continuously; its face orientation and texture should follow the same rigid object.|One pose does not establish rotation interpolation, loop continuity or playback timing.
AnimatedMorphCube|Watch both thin and angle morph targets change the cube's shape; faces should remain connected and shading should follow deformation.|A rotating or translated rigid cube does not demonstrate morph weights; one sampled loop is not a real-time performance test.
AnimatedTriangle|Watch the triangle's node rotation while its geometry remains intact.|A single triangle image proves neither the rotation channel nor interpolation between keys.
AnimationPointerUVs|Watch texture patterns move with animated UV transforms on each material input, not merely object movement.|One static texture placement cannot establish animation-pointer support across the listed material channels.
AnisotropyBarnLamp|Inspect directional highlights on the lamp, the clearcoat layer and transmitted light through its transparent parts.|A recognizable lamp does not establish anisotropy, emissive strength and volume behavior independently; isolate them under controlled lights.
AnisotropyDiscTest|Inspect highlight direction around the textured disc; check the directional pattern against the reference.|A circular highlight or visible texture alone does not establish the anisotropy texture's direction and strength channels.
AnisotropyRotationTest|Compare highlight orientations between the rotation cases, keeping camera and light fixed.|Changing the light can mimic a changed anisotropy rotation; a controlled common setup is needed.
AnisotropyStrengthTest|Compare the highlight shape as anisotropy strength changes across the samples.|Overall brightness differences alone cannot distinguish anisotropy strength from lighting or roughness.
AntiqueCamera|Check the camera body, lens and tripod remain complete, with their surface textures on the correct parts.|This object view is not a calibrated lighting test and does not exercise all material or texture-filter settings.
AttenuationTest|Compare tint through different thicknesses and scales; thin and thick transmission paths should not collapse to one uniform appearance.|Background visibility alone does not verify attenuation distance, thickness or scale conversion.
Avocado|Inspect the avocado's pit, flesh and skin boundaries and the placement of its painted texture.|One attractive render does not establish texture color-space handling, mip selection or sampler behavior.
BarramundiFish|Inspect the fish's fins, body silhouette and textured scales for missing geometry or texture seams.|A single view does not demonstrate all normals, tangent-space details or texture filtering.
BoomBox|Check the radio's body and speaker textures, and distinguish its glowing front panel from externally lit surfaces.|A bright panel under a bright light does not isolate emissive behavior; repeat with controlled external lighting.
BoomBoxWithAxes|Check the radio and labeled X/Y/Z tripod together; axis orientation must agree with the object's placement.|A correct-looking radio without the axis tripod does not verify coordinate orientation.
Box With Spaces|Check that the box and all its material textures load despite spaces and encoded spaces in their resource names.|A fallback-colored cube can hide failed texture URI resolution; inspect the expected texture content too.
Box|Check the cube silhouette and all visible faces, the red non-metallic material, sharp face boundaries and sensible face-to-face shading from its normals.|A cube existing is only a geometry smoke check. Exact screenshot brightness, shadows and background are not a gate without matched lighting/exposure; ambiguous shading needs a controlled engine-light check.
BoxAnimated|Watch the smaller box rise and rotate relative to the larger box; both translation and rotation channels must be visible.|One pose or one channel moving does not verify the two-channel animation or its timing.
BoxInterleaved|Check the cube shape and flat face shading stay intact when positions and normals share an interleaved buffer layout.|A surviving silhouette can hide a wrong normal stride; compare face lighting under a controlled light.
BoxTextured|Check the Cesium logo's orientation, scale and placement on the cube faces; confirm the texture is actually present.|A colored cube alone does not verify UVs, texture decoding or orientation; brightness is affected by lighting and exposure.
BoxTexturedNonPowerOfTwo|Check the logo remains correctly scaled and mapped with the non-power-of-two texture dimensions.|A near view does not establish minification, mipmap selection or all sampler modes for NPOT images.
BoxVertexColors|Inspect the per-face vertex colors and sharp cube edges; color must come through the COLOR_0 path.|A uniformly colored material is not evidence that vertex colors are being consumed.
BrainStem|Inspect the articulated robot's limbs and hierarchy throughout its animation, looking for detached parts or incorrect joint motion.|A rest pose cannot establish skinning or animation; a blocked/crashed attempt has no visual result to approve.
Cameras|Inspect the authored perspective and orthographic views separately, checking framing and perspective differences.|A capture from our review camera does not demonstrate that both glTF camera objects were imported and selected correctly.
CarConcept|Inspect paint, clearcoat, glass and material variants on the car; check that variant changes preserve the geometry.|One selected appearance cannot establish every material variant or its transmission and iridescence behavior.
CarbonFibre|Inspect the carbon-fibre weave and the direction of its elongated highlights around the ball.|A visible weave texture alone does not establish anisotropic shading; rotate a controlled light/view to separate the effects.
CesiumMan|Watch the character deform at its joints while the texture follows the skin; inspect limbs through the clip.|A textured rest pose cannot establish skeletal deformation or animation interpolation.
CesiumMilkTruck|Check the truck's body and wheel nodes, texture placement and animated parts together.|One view cannot establish every node transform or the full animation; separate rigid-node motion from skinning.
ChairDamaskPurplegold|Inspect the purple/gold upholstery pattern and grazing-angle sheen on the seat.|A fabric-colored chair does not isolate sheen from diffuse color or lighting; use controlled grazing views.
ChronographWatch|Inspect the watch face, hands, case and distinct material variants, including small texture details.|One watch appearance does not establish all variants, fine-detail filtering or exact specular lighting.
ClearCoatCarPaint|Look for a clearcoat highlight layered over the base car-paint material rather than replacing it.|A single glossy highlight cannot isolate the coat layer; compare controlled light/view changes.
ClearCoatTest|Compare the documented clearcoat cases, including coat strength, roughness and any coat normal detail.|A shiny surface alone does not establish each clearcoat input or separation from the base layer.
ClearcoatWicker|Check the woven base and the differently wrinkled clearcoat highlights remain distinct.|One shared normal pattern on both layers can look plausible but does not demonstrate separate coat normals.
CommercialRefrigerator|Inspect the refrigerator glass, condensation and bottles, then watch the door animation.|A closed-door still does not verify door motion or transparency ordering through multiple glass surfaces.
CompareAlphaCoverage|Compare the alpha-coverage examples side by side, especially edge coverage and holes against the same background.|Overall transparency is insufficient; sampling and anti-aliasing can mask differences between coverage methods.
CompareAmbientOcclusion|Compare the occluded and unoccluded examples, looking for the intended darkening in creases without changing base color.|Strong direct lighting can hide occlusion differences; use a common indirect-light setup.
CompareAnisotropy|Compare the paired highlight shapes with and without anisotropy under the same light.|Different camera or light directions can mimic the intended contrast.
CompareBaseColor|Compare factor-only color, texture color and texture multiplied by vertex color; verify the three authored cases remain distinct.|A texture appearing is not enough to verify factor and vertex-color multiplication or color-space handling.
CompareClearcoat|Compare the coat examples for a distinct top reflection layer while retaining the base material.|A generic glossy shader can look convincing without actually implementing clearcoat.
CompareDispersion|Look for wavelength-separated refraction in the dispersion example compared with the non-dispersive case.|Colored base textures or environment reflections can imitate rainbow edges; isolate transmission under a common background.
CompareEmissiveStrength|Compare emissive-only and emissive-strength examples without conflating surface emission with scene illumination.|Tone mapping may hide HDR intensity differences; a screenshot alone does not measure emitted radiance.
CompareIor|Compare changes in surface reflection and transmitted distortion between the IOR examples.|Brightness alone does not identify IOR; keep lighting and background fixed.
CompareIridescence|Compare angle-dependent color shifts with and without iridescence.|One frontal color cannot establish thin-film behavior across angles and thickness settings.
CompareMetallic|Compare metallic and non-metallic reflections under a shared environment, not only their base colors.|A bright highlight is not by itself proof of metallic response.
CompareNormal|Compare normal-mapped detail with the unmodified surface while keeping the silhouette stable.|Painted color detail can imitate relief; use directional light movement to isolate normal mapping.
CompareRoughness|Compare highlight width and reflection blur between roughness cases.|Different exposure or lighting intensity can obscure roughness; brightness is not the defining check.
CompareSheen|Compare the grazing-angle cloth highlight with and without sheen.|A diffuse color change is not evidence of a separate sheen response.
CompareSpecular|Compare the dielectric reflection strength/color between specular cases under identical lighting.|A metallic-looking highlight can hide incorrect specular parameters; inspect the intended non-metallic response.
CompareTransmission|Compare visibility and distortion through the transmission examples against the shared background.|Alpha blending can look transparent without reproducing refractive transmission.
CompareVolume|Compare the volume examples for thickness-dependent tint/attenuation through the object.|Surface transparency alone cannot establish travel through a material volume.
Corset|Inspect the corset, collar and mannequin silhouette; check that fabric and other material regions remain distinct.|A single material-colored mannequin is not enough to verify the mapped surface details.
Cube|Check the textured cube's hard, non-smoothed face boundaries and orientation.|A rounded-looking or uniformly shaded cube can hide wrong normals; exact reference lighting is not established.
CubeVisibility|Watch the authored cube visibility changes through the animation-pointer sequence.|A single visible cube says nothing about the hidden intervals or animated visibility property.
DamagedHelmet|Inspect the damaged panels, decals and surface relief; compare metallic and rough regions.|Texture detail alone does not isolate normal, occlusion and metallic-roughness channels under unknown lighting.
DiffuseTransmissionPlant|Inspect light passing through thin leaves, particularly from their back side.|A green opaque plant from the front does not demonstrate diffuse transmission.
DiffuseTransmissionTeacup|Inspect the cup's translucency under front and back lighting while retaining its surface detail.|A transparent-looking cup does not distinguish diffuse transmission from ordinary refractive transmission.
DiffuseTransmissionTest|Compare the authored diffuse-transmission cases under a shared front/back-light setup.|A single bright render cannot isolate diffuse-transmission color, strength and thickness behavior.
DirectionalLight|Inspect illumination from the authored directional light and compare surface orientation responses.|An added review light or unmatched exposure prevents a quantitative check of authored light calculations.
DispersionTest|Compare the dispersion cases for colored refractive separation through the object.|Rainbow surface color alone is not sufficient; the transmitted background must be inspected.
DragonAttenuation|Inspect the dragon's thickness-dependent tint against its background, including each material variant.|One material variant and one viewing direction cannot establish attenuation across the model.
DragonDispersion|Inspect spectral separation through the dragon against its background, especially thick and thin regions.|A multicolored reflection is not enough to establish dispersion in transmission.
Duck|Check the duck's silhouette and single texture, including its eyes and bill placement.|This basic textured object does not isolate lighting fidelity, mipmaps or normal-map behavior.
EmissiveStrengthTest|Compare the strength samples with controlled exposure; distinguish emission from reflected illumination.|Tone mapping and clipping can conceal intensity errors; exact HDR values need a controlled engine check.
EnvironmentTest|Inspect the roughness progression on both metal and dielectric spheres using the same environment.|An arbitrary added light cannot establish environment-lighting accuracy; environment and exposure must be controlled.
FlightHelmet|Inspect helmet, visor, straps and stand, keeping glass, metal and fabric regions distinct.|A convincing overall silhouette does not establish each material channel independently.
Fox|Review Survey, Walk and Run separately; check limb/ear/tail deformation and continuity throughout each cycle.|One pose or one clip does not establish all three animations, looping or real-time playback timing.
GlamVelvetSofa|Inspect velvet sheen at grazing angles and each material variant on the sofa.|One sofa color does not verify variants or separate sheen from specular reflections.
GlassBrokenWindow|Check that the broken holes are absent while the remaining glass transmits the background.|Treating all transparency as alpha blending does not verify masked holes plus transmissive glass.
GlassHurricaneCandleHolder|Inspect the holder's glass silhouette and thickness-dependent tint around the candle.|A transparent outline alone does not establish transmission and volume behavior.
GlassVaseFlowers|Compare the alpha-blended left glass with the transmission/volume right glass while keeping the flowers visible.|The two methods are not expected to be identical; inspect their distinct ordering and refraction behavior.
IORTestGrid|Compare distortion of the checker backdrop across the IOR sphere grid.|A single sphere or uncontrolled backdrop does not test the grid's IOR progression.
InterpolationTest|Review STEP, LINEAR and CUBICSPLINE across translation, rotation and scale; compare the intended simultaneous tracks.|One active sequence cannot verify the nine-track comparison; a still cannot distinguish interpolation behavior.
IridescenceAbalone|Inspect angle-dependent color across the shell's iridescent interior, retaining its surface structure.|One saturated shell color is not enough to verify view-dependent iridescence.
IridescenceDielectricSpheres|Compare iridescent color shifts across the non-metallic sphere cases.|Do not infer metallic behavior from a dielectric test; a single angle misses the film response.
IridescenceLamp|Inspect iridescent reflections and transmitted light on the lamp's transparent parts.|A colorful glass appearance alone does not isolate iridescence from transmission and volume.
IridescenceMetallicSpheres|Compare the iridescent metallic sphere cases under a common environment.|Non-metallic substitutions or one view can hide an incorrect metallic iridescence response.
IridescenceSuzanne|Inspect color shifts across Suzanne's curved surfaces as viewing angle changes.|One still is a limited sample of the view-dependent response, not a full iridescence test.
IridescentDishWithOlives|Inspect the dish, olives and transparent/tinted material interactions; keep the different material regions separate.|The asset name alone does not establish iridescence support; inspect the actual declared transmission, volume, IOR and specular inputs.
Lantern|Inspect the lantern's frame, glass and illuminated interior as separate material regions.|A bright lantern image does not distinguish emissive surfaces from actual emitted scene light.
LightVisibility|Watch authored light visibility changes, checking the illumination as well as visible lamp geometry.|Hiding a mesh alone does not verify that the light's contribution is disabled.
LightsPunctualLamp|Inspect illumination from the lamp's authored punctual lights, including direction and range where used.|A visible lamp lit only by our review light does not verify imported punctual lights.
MandarinOrange|Inspect transmitted/scattered light through the orange under back lighting.|A textured orange viewed from the front does not establish diffuse transmission.
MaterialsVariantsShoe|Switch Beach, Midnight and Street; each should change the intended shoe materials without changing the mesh.|A default-material screenshot cannot verify the variant-selection API or all three looks.
MeshPrimitiveModes|Check all seven primitive types: points, lines, loops, strips, triangles, triangle strips and fans.|A triangle-only render does not verify the other primitive modes or their topology.
MeshoptCubeTest|Compare all grid cells across compressed layouts, normalized attributes and index sizes; watch rotating cells too.|A few intact cubes do not establish every compression/layout case; the animated columns also need motion evidence.
MetalRoughSpheres|Compare the texture-driven metallic/roughness grid: highlight width and metal/dielectric response should follow its rows and columns.|A visible grid is not a material pass; keep light/environment/exposure fixed to diagnose roughness and metallic behavior.
MetalRoughSpheresNoTextures|Compare the factor-driven metallic/roughness grid, including smooth/rough and metal/dielectric extremes.|Do not substitute texture sampling success for factor handling; uncontrolled lighting can mask incorrect values.
MorphPrimitivesTest|Check the static mesh weight of 0.5 raises the centre at half strength across both the large red and small green primitives.|This is a static morph-weight check, not necessarily an animation test. A flat mesh or a missing red/green region does not satisfy it.
MorphStressTest|Review Individuals, TheWave and Pulse; check all eight position/normal morph targets, first separately and then simultaneously.|The README explicitly describes a stress sample that can expose runtime limits. Record any limit; do not treat it as an automatic pass or exclusion.
MosquitoInAmber|Inspect the mosquito through the amber, including thickness-dependent tint and refraction.|A tinted transparent shell does not alone establish volume attenuation or correct IOR.
MultiUVTest|Compare texture placement from both UV sets, checking that the second set is actually selected where authored.|A correct base-color texture on UV0 can hide a missing or wrongly selected UV1 stream.
MultipleScenes|Verify the default scene shows the square, then explicitly select the separate triangle scene.|One scene rendered successfully does not verify scene selection or both scene graphs.
NegativeScaleTest|Inspect mirrored/scaled objects for correct visible faces, winding and lighting.|Matching positions alone can hide flipped normals or incorrect backface culling under negative scale.
NodePerformanceTest|Check the expected node/mesh/primitive/texture content is present before measuring its scaling behavior.|A screenshot cannot establish performance; time/memory measurements need a controlled benchmark.
NodeVisibilityTest|Inspect every intended visible/hidden node and the effects of its visibility property.|One visible object does not verify hidden nodes or descendants' visibility behavior.
NormalTangentMirrorTest|Compare the supplied-tangent normal-map blocks with their geometric references; use a clear horizon reflection to inspect mirrored orientations.|A textured surface alone cannot reveal tangent-handedness errors; this sample tests supplied tangents, not generated tangents.
NormalTangentTest|Compare the normal-map blocks with their geometric references under a clear horizon environment.|This sample needs generated tangents; plausible color/detail is not sufficient to verify their orientation.
OrientationTest|Check the colored axes, labels and transformed nodes against the intended coordinate directions.|A familiar object silhouette alone does not establish axis mapping, rotations and translations.
PlaysetLightTest|Inspect the playset's authored lights and resulting illumination on its objects.|A generic review light can conceal missing imported lights; exact screenshot lighting is not established by object presence.
PointLightIntensityTest|Compare the equally intense colored point lights; color should act as a multiplier rather than renormalizing brightness.|Unknown exposure or added lights prevent a quantitative intensity check.
PotOfCoals|Inspect the pot's clearcoat and glowing coals as separate surface effects.|Glowing pixels do not prove that the coals illuminate nearby geometry; emission and scene lighting are separate checks.
PotOfCoalsAnimationPointer|Watch the animated heat-refraction effect while inspecting the clearcoated pot and emissive coals.|A static pot or ordinary object animation does not verify the material-property animation.
PrimitiveModeNormalsTest|Inspect unlit-looking points/lines without normals, flat-shaded triangles without normals and lit primitives with normals.|Rendering every primitive with one generic shading mode does not verify these documented distinctions.
RecursiveSkeletons|Watch reused meshes and nested/recursive skeleton relationships for independent, correct deformation.|One correctly posed mesh does not verify every reused mesh and skeleton binding.
RiggedFigure|Watch the figure's joint deformation and overall articulated motion through the clip.|A rest pose cannot establish the animation or skinning behavior.
RiggedSimple|Inspect the simple rig through bending poses; vertices should follow the correct joints without separating.|One upright pose does not validate joint influences, inverse-bind transforms or animated normals.
ScatteringSkull|Inspect light transport through thin and thick skull regions under controlled back lighting.|An opaque skull or a uniformly tinted transparent shell does not establish subsurface scattering.
SciFiHelmet|Inspect the helmet's hard-surface details and distinct material regions under a stable light.|A recognizable helmet alone does not verify normal, roughness and metallic inputs independently.
SheenChair|Inspect the chair's grazing fabric sheen and each material variant.|One static color choice does not establish variants or the sheen layer.
SheenCloth|Inspect the cloth folds and grazing-angle sheen, separating it from base color.|A bright cloth image alone cannot distinguish sheen from diffuse lighting.
SheenTestGrid|Compare the sheen sphere grid under a common camera and light setup.|One sphere or uncontrolled illumination does not exercise the grid's material progression.
SheenWoodLeatherSofa|Inspect wood, leather and fabric regions, their tiling textures and distinct sheen/specular responses.|One overall sofa image can hide incorrect texture scale or a shared material response across different surfaces.
SimpleInstancing|Check the 5×5×5 arrangement of 125 instances with varying translations, rotations and scales, rather than a single cube.|A correct image does not establish one-draw efficiency, instance ownership or resource sharing; those need separate measurements.
SimpleMaterial|Check that the authored material is applied to the simple mesh rather than a fallback material.|A visible mesh alone does not establish material factors or lighting correctness.
SimpleMeshes|Check both nodes using the shared triangle mesh, with their intended transforms and attributes.|One triangle does not verify both nodes or correct shared-mesh instantiation.
SimpleMorph|Watch the triangle change shape through the morph animation without substituting rigid motion.|A single shape cannot establish interpolation of morph weights.
SimpleSkin|Watch the strip bend under its joints; inspect continuity and shaded normals across the bend.|A straight strip alone does not establish skinning, and a sampled movie does not measure real-time playback cadence.
SimpleSparseAccessor|Inspect the intended displaced vertices produced by sparse accessor overrides.|A base mesh appearing is insufficient if sparse replacements were ignored; non-overridden vertices should remain unchanged.
SimpleTexture|Inspect the texture's orientation and placement on the simple mesh.|A plain-colored fallback does not demonstrate texture loading, UV selection or sampling.
SpecGlossVsMetalRough|Compare the specular/glossiness and metallic/roughness material examples under the same lighting.|A metallic/roughness-only fallback does not verify the specular/glossiness extension.
SpecularSilkPouf|Inspect silk sheen and specular reflections on the pouf, especially at grazing angles.|One fabric-colored render does not isolate the two reflection contributions.
SpecularTest|Compare the authored specular cases for reflection strength and color under common lighting.|Glossiness alone does not prove the specular extension; roughness and lighting can obscure its effect.
Sponza|Inspect architecture, hanging fabric and surface normals/materials before using the scene for lighting comparisons.|The preview's lights are not part of the asset; matching that screenshot requires a separately specified lighting setup.
StainedGlassLamp|Inspect colored glass, frame and each material variant, including transmitted light through the panels.|A colorful opaque lampshade does not establish transmission, volume or variant selection.
SunglassesKhronos|Inspect the frame/logo and transmitted, angle-dependent appearance of the lenses.|One tinted lens view cannot establish IOR, volume and iridescence independently.
Suzanne|Inspect Suzanne's silhouette and smooth curved shading for missing geometry or normal discontinuities.|This general test object is not a calibrated lighting or full PBR conformance fixture.
TextureCoordinateTest|Compare the labeled texture orientation and UV-to-geometry placement on the test surfaces.|A texture appearing somewhere does not verify its origin, orientation or each coordinate mapping.
TextureEncodingTest|Compare equal-material representations within each row: factors and textures with different embedded metadata should agree.|Base color/emission need sRGB decoding while metallic/roughness stay linear; visual ambiguity needs controlled numeric/readback checks.
TextureLinearInterpolationTest|Compare the two green spheres: the factor-only and filtered-texture versions should be nearly the same color.|This isolates filtering after sRGB decoding; unrelated lighting/exposure changes can hide the difference.
TextureSettingsTest|Compare Test against Sample pass: expect green checks, with solid green boxes for the clamp cases; watch for red failure marks.|One green box does not establish every wrap or sidedness case; inspect every labeled cell.
TextureTransformMultiTest|Inspect texture transforms on each listed material input, not just base color.|A correct base-color transform cannot establish normal, emissive or extension-channel transforms.
TextureTransformTest|Inspect all six base-color quads: arrows should hit green markers, not yellow (unapplied) or red (opposite rotation), with the intended offsets and scales.|Correct UVs without the extension transform are not enough; compare every quad.
ToyCar|Inspect paint clearcoat, transparent parts and fabric/sheen regions on the toy car.|A recognizable toy does not independently establish transmission, sheen and clearcoat.
TrafficCone|Inspect the cone's retroreflective regions with the light near and away from the viewing direction.|A bright stripe under arbitrary lighting does not establish retroreflection toward the light source.
TransmissionOrderTest|Inspect overlapping transparent objects in the authored order, including alpha and transmission interactions.|One isolated transparent object cannot reveal ordering errors among overlapping surfaces.
TransmissionRoughnessTest|Compare blur and refraction across roughness/IOR combinations against the same background.|A visible background alone does not establish the interaction between rough transmission and IOR.
TransmissionTest|Inspect the transmitted background and reflection behavior across the authored transmission examples.|Alpha blending can mimic transparency without implementing the transmission extension.
TransmissionThinwallTestGrid|Compare the grid's transmitted checker backdrop across its authored thin-wall/IOR cases.|One sphere cannot verify the grid; inspect the actual material parameters rather than inferring thin-film support from the sample title.
Triangle|Check the single indexed triangle's shape and winding without relying on a fallback mesh.|This minimal geometry test has no basis for claiming complex materials, animation or lighting fidelity.
TriangleWithoutIndices|Check the non-indexed triangle appears with the intended winding and vertex positions.|An indexed fallback triangle would not test this path; the image alone cannot prove index-buffer absence.
TwoSidedPlane|Inspect the plane from both sides; back-facing geometry should remain visible as authored.|A front-only capture cannot establish the double-sided flag or back-face shading.
USDShaderBallForGltf|Inspect the shader ball's curved, recessed and layered surfaces and its transmitted/volume material appearance.|A single overall material impression does not establish every glTF shading parameter or USD equivalence.
Unicode❤♻Test|Check that geometry and materials resolve through Unicode file, mesh and material names.|A renamed ASCII substitute would bypass the intended name/path handling; visual output alone does not inspect retained names.
UnlitTest|Check the orange and blue objects appear uniformly colored without light-driven surface shading.|A normally lit orange/blue object does not verify unlit behavior; change external lighting in a controlled engine check.
VertexColorTest|Compare the authored COLOR_0 examples and their interaction with material color.|Matching a uniform base color is insufficient if vertex colors were ignored or incorrectly multiplied.
VirtualCity|Inspect the city layout, repeated buildings and material placement for missing or misplaced content.|An overview image does not establish scene-loading performance, culling correctness or every distant detail.
WaterBottle|Inspect the bottle's metal/roughness material plus normal, occlusion and emissive details.|A bottle silhouette alone does not establish the separate material-map channels.
XmpMetadataRoundedCube|Inspect the rounded cube as a rendering sanity check, then examine the attached XMP metadata through the appropriate data/API path.|Metadata preservation cannot be approved from pixels; this is principally a non-visual extension test.
'''


def catalog():
    rows = {}
    for line in CATALOG.strip().splitlines():
        model, check, limit = line.split('|')
        if model in rows:
            raise ValueError('Duplicate guidance: ' + model)
        rows[model] = {'checks': [s.strip() for s in check.split(' || ')], 'limits': limit}
    return rows


def model_guidance(corpus, corpus_root):
    """Read only the pinned local README; no inference is presented as upstream text."""
    entries = catalog()
    models = {c['model'] for c in corpus['cases']}
    if set(entries) != models:
        raise ValueError('Guidance coverage differs from corpus: ' + str(set(entries) ^ models))
    pin = corpus['corpus']['commit']
    for model, guide in entries.items():
        relative = f'Models/{model}/README.md'
        path = Path(corpus_root) / relative
        digest = hashlib.sha256(path.read_bytes()).hexdigest()
        if digest != corpus['files'].get(relative, {}).get('sha256'):
            raise ValueError('Pinned README changed: ' + model)
        body = path.read_text(encoding='utf-8')
        match = re.search(r'^## Summary\s*\n(.*?)(?=^## |\Z)', body, re.M | re.S)
        if not match:
            raise ValueError('Missing documented summary: ' + model)
        summary = ' '.join(match[1].split())
        # Keep a short attributed excerpt; checks below are our separate review advice.
        summary = re.sub(r'\[([^]]+)\]\([^)]*\)', r'\1', summary).replace('`', '')
        words = summary.split()
        guide.update(documentedPurpose=' '.join(words[:22]) + (' …' if len(words) > 22 else ''),
                     documentedSource=f'https://github.com/KhronosGroup/glTF-Sample-Assets/blob/{pin}/{quote(relative, safe="/")}#summary',
                     readmeSha256=hashlib.sha256(path.read_bytes()).hexdigest(),
                     reviewBasis='Recommended review checks (our inference from the pinned sample and source features).')
    return entries


def enrich_guidance(data, corpus, corpus_root):
    guides = model_guidance(corpus, corpus_root)
    source = {case['id']: case for case in corpus['cases']}
    if {case['id'] for case in data['cases']} != set(source):
        raise ValueError('Site guidance case IDs differ from pinned corpus')
    for model in data['models']:
        model['reviewGuidance'] = guides[model['id']]
    for case in data['cases']:
        case['reviewGuidance'] = variant_guidance(source[case['id']])
    return data


def variant_guidance(case):
    features = set(case.get('feature_ids', []))
    metadata = case.get('source_metadata', {})
    checks = []
    if case['source_form'] == 'glb':
        checks.append('This is the binary GLB form; compare it with the same model’s glTF form when available.')
    elif 'storage:self-contained' in features:
        checks.append('This form embeds its resources; confirm it resolves without the external resource layout.')
    else:
        checks.append('This form uses its declared resource URIs; missing buffers or textures must not be replaced silently.')
    for extension, instruction in {
        'KHR_draco_mesh_compression': 'Draco: inspect decoded shape, normals and UVs against the uncompressed form.',
        'EXT_meshopt_compression': 'Meshopt: inspect decoded attributes and topology against the uncompressed form.',
        'KHR_meshopt_compression': 'Meshopt: inspect decoded attributes and topology against the uncompressed form.',
        'KHR_mesh_quantization': 'Quantized attributes: inspect bounds, surface normals and UV placement.',
        'KHR_texture_basisu': 'BasisU textures: inspect color, transparency and material-channel content against the original texture form.',
    }.items():
        if extension in case.get('extensions_used', []):
            checks.append(instruction)
    return {'checks': checks, 'controlledNormalMapViewRequired': case['model'] in {'NormalTangentTest', 'NormalTangentMirrorTest'}, 'animationClips': metadata.get('animations', 0),
            'sceneCount': metadata.get('scenes', 0), 'skinCount': metadata.get('skins', 0),
            'sourceFeatureBasis': 'Pinned source inventory; feature presence is not a passed test.',
            'requiredExtensions': case.get('extensions_required', [])}
