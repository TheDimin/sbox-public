#!/usr/bin/env python3
"""Export native positives, then run the pinned official core validator.

Shader negative controls are valid assets and are included. Intentionally
malformed reader/schema cases are excluded explicitly, never waived.
"""
import base64
import copy
import hashlib
import json
from pathlib import Path
import struct
import sys

owned = Path(__file__).resolve().parent
checkout = owned.parents[5]
sys.path.insert(0, str(checkout / 'engine/Tools/Gltf/scripts'))
import validator_adapter as adapter

output = (Path(__file__).resolve().parents[6]/'research/gltf-validation/direct-gpu/models/MandarinOrange') / 'diffuse-positive-fixtures'
reports = checkout / 'Temp/gltf-validation/direct-gpu/mandarin-diffuse-fixture-validation'
output.mkdir(exist_ok=True)
reports.mkdir(parents=True, exist_ok=True)
extension = 'KHR_materials_diffuse_transmission'
plane = json.loads(((Path(__file__).resolve().parents[6]/'research/gltf-validation/direct-gpu/models/MandarinOrange') / 'diffuse-transmission-plane.gltf').read_text())
seven = json.loads(((Path(__file__).resolve().parents[6]/'research/gltf-validation/direct-gpu/models/MandarinOrange') / 'diffuse-transmission-seven-uv-samplers.gltf').read_text())
encoded = base64.b64encode(((Path(__file__).resolve().parents[6]/'research/gltf-validation/direct-gpu/models/MandarinOrange') / 'diffuse-transmission-role-control.png').read_bytes()).decode()
fixtures = {}
def f(value): return struct.unpack('<f', struct.pack('<f', value))[0]
def add(name, value):
    assert name not in fixtures
    fixtures[name] = copy.deepcopy(value)
def draw(name, factor, color=(1,1,1), metallic=0, texture=False, source=None):
    root = copy.deepcopy(source or plane)
    root['materials'][0]['pbrMetallicRoughness']['metallicFactor'] = metallic
    ext = root['materials'][0]['extensions'][extension]
    ext['diffuseTransmissionFactor'] = f(factor)
    ext['diffuseTransmissionColorFactor'] = list(map(f, color))
    if texture:
        ext['diffuseTransmissionTexture'] = {'index':0}
        ext['diffuseTransmissionColorTexture'] = {'index':0}
        root['images'] = [{'uri':'data:image/png;base64,' + encoded}]
        root['textures'] = [{'source':0,'sampler':0}]
        root['samplers'] = [{'wrapS':10497,'wrapT':10497,'minFilter':9729,'magFilter':9729}]
    add(name, root)

for name, factor in [('back_factor_zero',0),('back_factor_one',1),('back_factor_quarter',.25),('back_sixty_degree_lambert_fresnel_control',1),('front_factor_zero',0),('front_factor_one',1),('front_factor_quarter',.25),('ibl_front_red_factor_zero',0),('ibl_back_green_factor_one',1),('ibl_factor_quarter',.25)]:
    draw(name, factor)
draw('metallic_suppresses_diffuse_transmission',1,metallic=1)
color=(.5,.75,.25)
linear=(.10793025005694963,.03845209378053243,.13177878142645328)
strength=f(f(.5)*f(f(64)/f(255)))
draw('alpha_linear_color_srgb',.5,color,texture=True)
draw('independent_uniform_reference',strength,linear)
draw('wrong_color_transfer_negative',strength,tuple(f(f(f(v)/f(255))*f(c)) for v,c in zip((128,64,192),color)))
draw('wrong_rgb_strength_negative',f(f(.5)*f(f(128)/f(255))),linear)
draw('seven_uv_repeat_mirror_actual',.5,color,source=seven)
draw('seven_uv_independent_uniform_reference',f(f(.5)*f(f(16)/f(255))),(.10793025005694963,.3953363442793598,.01281736459351081))
clamp=copy.deepcopy(seven);clamp['samplers'][0]['wrapS']=33071
draw('wrong_strength_clamp_negative',.5,color,source=clamp)
repeat=copy.deepcopy(seven);repeat['samplers'][1]['wrapS']=10497
draw('wrong_color_repeat_negative',.5,color,source=repeat)
different=copy.deepcopy(fixtures['alpha_linear_color_srgb']);different['materials'][0]['extensions'][extension]['diffuseTransmissionFactor']=.75
add('distinct_factor_cache',different)

# Reader positives are material-only assets except its independent seven-role
# projection. Unsupported malformed optional payload and excluded/schema/error
# cases are intentional negatives, not purported valid positive fixtures.
def reader(ext): return {'asset':{'version':'2.0'},'extensionsUsed':[extension],'materials':[{'extensions':{extension:ext}}]}
add('reader_defaults',reader({}))
add('reader_explicit_zero',reader({'diffuseTransmissionFactor':0,'diffuseTransmissionColorFactor':[0,0,0]}))
required=reader({'diffuseTransmissionFactor':.5});required['extensionsRequired']=[extension]
add('reader_required_supported',required)
roles=copy.deepcopy(plane);roles['extensionsUsed']=[extension,'KHR_texture_transform']
roles['images']=[{'uri':'data:image/png;base64,'+encoded}]
roles['samplers']=[{'magFilter':9728,'minFilter':9729,'wrapS':33071,'wrapT':33648},{'magFilter':9729,'minFilter':9728,'wrapS':10497,'wrapT':33071}]
roles['textures']=[{'source':0,'sampler':i%2} for i in range(7)]
attrs=roles['meshes'][0]['primitives'][0]['attributes']
for i in range(50):attrs['TEXCOORD_'+str(i)]=2
sets=[2,7,11,19,23,41,49]
def info(i): return {'index':i,'texCoord':0,'extensions':{'KHR_texture_transform':{'texCoord':sets[i],'offset':[f(f(i)/f(10)),.125],'scale':[.5,2]}}}
m=roles['materials'][0];m['pbrMetallicRoughness']['baseColorTexture']=info(0);m['pbrMetallicRoughness']['metallicRoughnessTexture']=info(1)
for key,i in [('normalTexture',2),('occlusionTexture',3),('emissiveTexture',4)]:m[key]=info(i)
m['extensions']={extension:{'diffuseTransmissionFactor':.5,'diffuseTransmissionTexture':info(5),'diffuseTransmissionColorTexture':info(6)}}
add('reader_seven_arbitrary_source_sets',roles)

binary=checkout/'Temp/gltf-validation/validator/gltf_validator'
binary_sha=adapter.verify_validator(binary)
inputs=[]
for name,root in fixtures.items():
    path=output/(name+'.gltf');path.write_text(json.dumps(root,indent=2)+'\n');inputs.append((name,path))
inputs += [('base_plane',(Path(__file__).resolve().parents[6]/'research/gltf-validation/direct-gpu/models/MandarinOrange') / 'diffuse-transmission-plane.gltf'),('base_seven_uv',(Path(__file__).resolve().parents[6]/'research/gltf-validation/direct-gpu/models/MandarinOrange') / 'diffuse-transmission-seven-uv-samplers.gltf'),('actual_MandarinOrange',checkout/'Temp/gltf-reference/glTF-Sample-Assets/Models/MandarinOrange/glTF/MandarinOrange.gltf')]
results=[]
for name,path in inputs:
    raw=adapter.run_validator(binary,path)
    report_path=reports/(name+'.json');report_path.write_text(json.dumps(raw['report'],indent=2)+'\n')
    issues=raw['issues']
    results.append({'name':name,'path':str(path.relative_to(checkout)),'sha256':adapter.sha256_file(path),'numErrors':issues['numErrors'],'numWarnings':issues['numWarnings'],'numInfos':issues['numInfos'],'truncated':issues['truncated'],'report':str(report_path.relative_to(checkout)),'report_sha256':adapter.sha256_file(report_path),'messages':issues['messages']})
manifest={'status':'passed_core_only' if all(r['numErrors']==0 and not r['truncated'] for r in results) else 'failed','validator_version':adapter.VALIDATOR_VERSION,'validator_sha256':binary_sha,'validator_source_commit':adapter.VALIDATOR_SOURCE_COMMIT,'scope':'All positive core fixtures: exact native-control input bytes, reader positive structures (reader role resolver PNG embedded equivalently), base fixtures and actual Mandarin source. Unsupported diffuse-transmission notices retained: this validator does not validate that extension. No GPU/render acceptance.','intentional_negative_exclusions':['malformed unsupported optional extension payload','schema invalid factor/color/type/texture indices (11 rows)','excluded material models (2 rows)','undeclared extensionsUsed error'], 'fixtures':results}
path=(Path(__file__).resolve().parents[6]/'research/gltf-validation/direct-gpu/models/MandarinOrange') / 'diffuse-positive-fixtures-validation.json';path.write_text(json.dumps(manifest,indent=2)+'\n')
print(json.dumps({'status':manifest['status'],'fixtures':len(results),'core_errors':sum(r['numErrors'] for r in results),'report':str(path),'error_messages':[{ 'name':r['name'],'messages':[m for m in r['messages'] if m['severity']==0]} for r in results if r['numErrors']]}))
sys.exit(0 if manifest['status']=='passed_core_only' else 1)
