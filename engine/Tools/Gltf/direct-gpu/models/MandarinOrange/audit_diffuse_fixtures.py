#!/usr/bin/env python3
"""Independent literal-fixture audit, never an engine/extension acceptance test."""
from pathlib import Path
import base64,hashlib,json,math,struct,zlib

owned=Path(__file__).resolve().parent
oracle=json.loads(((Path(__file__).resolve().parents[6]/'research/gltf-validation/direct-gpu/models/MandarinOrange') / 'diffuse-transmission-oracle.json').read_text())
fixture=json.loads(((Path(__file__).resolve().parents[6]/'research/gltf-validation/direct-gpu/models/MandarinOrange') / 'diffuse-transmission-seven-uv-samplers.gltf').read_text())
checks=[]
def record(name,details):checks.append({'name':name,'passed':True,**details})
def png(encoded):
    assert encoded[:8]==b'\x89PNG\r\n\x1a\n'
    at=8;data=b''
    while at<len(encoded):
        size=struct.unpack_from('>I',encoded,at)[0];kind=encoded[at+4:at+8];body=encoded[at+8:at+8+size];crc=struct.unpack_from('>I',encoded,at+8+size)[0]
        assert zlib.crc32(kind+body)&0xffffffff==crc
        if kind==b'IHDR':width,height,bits,color,compression,filtering,interlace=struct.unpack('>IIBBBBB',body);assert (bits,color,compression,filtering,interlace)==(8,6,0,0,0)
        if kind==b'IDAT':data+=body
        at+=size+12
    rows=zlib.decompress(data);stride=width*4+1;assert len(rows)==stride*height
    assert all(rows[y*stride]==0 for y in range(height)), 'Literal fixture deliberately uses unfiltered PNG rows'
    return [tuple(rows[y*stride+1+x*4:y*stride+5+x*4]) for y in range(height) for x in range(width)]
def linear(value):
    s=value/255
    return s/12.92 if s<=.04045 else ((s+.055)/1.055)**2.4
literal=png(((Path(__file__).resolve().parents[6]/'research/gltf-validation/direct-gpu/models/MandarinOrange') / 'diffuse-transmission-role-control.png').read_bytes())
assert literal==[(128,64,192,64)]
assert all(abs(linear(v)-e)<1e-15 for v,e in zip(literal[0],oracle['linear_rgb']))
assert oracle['expected_strength']==.5*literal[0][3]/255
record('literal_RGBA_and_linear_alpha_sRGB_RGB',{'rgba':literal[0],'strength':oracle['expected_strength'],'linear_color':oracle['expected_color']})

buffer=base64.b64decode(fixture['buffers'][0]['uri'].split(',',1)[1]);attributes=fixture['meshes'][0]['primitives'][0]['attributes']
uv={}
for semantic,component,stride,offset,expected in [('TEXCOORD_41',5123,8,4,(32768/65535,16384/65535)),('TEXCOORD_49',5126,12,4,(1.5,.75))]:
    accessor=fixture['accessors'][attributes[semantic]];view=fixture['bufferViews'][accessor['bufferView']]
    assert accessor['componentType']==component and view['byteStride']==stride and accessor['byteOffset']==offset
    start=view['byteOffset']+offset
    for vertex in range(4):
        values=struct.unpack_from('<HH' if component==5123 else '<ff',buffer,start+vertex*stride)
        decoded=tuple(x/65535 for x in values) if component==5123 else values
        assert decoded==expected
    wrong=struct.unpack_from('<HH' if component==5123 else '<ff',buffer,view['byteOffset'])
    assert wrong!=values, 'Wrong accessor offset must change observed source values'
    uv[semantic]=expected
record('literal_descriptor_stride_offset_and_normalization',{'uv':uv,'wrong_offset_negative_sensitive':True})

extension=fixture['materials'][0]['extensions']['KHR_materials_diffuse_transmission']
images=[png(base64.b64decode(image['uri'].split(',',1)[1])) for image in fixture['images']]
assert images[1]==[(255,0,0,16),(255,0,0,64),(255,0,0,128),(255,0,0,240)]
assert images[2]==[(32,64,128,255),(64,128,192,255),(128,192,64,255),(192,32,96,255)]
# Literal transformed coordinates; these expressions do not call a producer's
# UV transform/evaluator or infer expected values from rendered agreement.
strength_u=uv['TEXCOORD_41'][0]*.5+1
color_u=uv['TEXCOORD_49'][0]*.5-1
assert 1.25<strength_u<1.251 and color_u==-.25
repeat_u=strength_u-math.floor(strength_u)
mirror_u=abs(color_u)
assert repeat_u<.5 and mirror_u==.25
strength_pixel=images[1][0];color_pixel=images[2][2]
assert .5*strength_pixel[3]/255==oracle['seven_uv_fixture']['expected_strength']
assert all(abs(linear(value)*factor-e)<1e-15 for value,factor,e in zip(color_pixel,[.5,.75,.25],oracle['seven_uv_fixture']['expected_color']))
assert images[1][1][3]!=strength_pixel[3], 'Clamping strength U must select another alpha'
assert images[2][3][:3]!=color_pixel[:3], 'Repeating color U must select another color'
record('independent_point_repeat_mirror_and_transfer_oracle',{'strength_texel':strength_pixel,'color_texel':color_pixel,'wrong_sampler_negatives_sensitive':True})

ratio=.5*(1-(.04+.96*(1-math.cos(math.pi/6))**5))/.96
assert abs(ratio-oracle['equal_radius_60_degree_lambert_fresnel_ratio'])<1e-15
assert abs(ratio-.5**.6)>.1
record('literal_Lambert_mirrored_Fresnel_angular_ratio',{'expected':ratio,'wrong_native_roughness_exponent':.5**.6})
result={'status':'passed_independent_fixture_audit_only','scope':'Generated input bytes, transfer values, adversarial descriptor/sampler sensitivity and literal BSDF arithmetic. No engine reader, shader, GPU, public loading or model appearance acceptance.','checks':checks,'engine_tests_executed':False,'inputs':[{'path':name,'sha256':hashlib.sha256((owned/name).read_bytes()).hexdigest()} for name in ['diffuse-transmission-role-control.png','diffuse-transmission-seven-uv-samplers.gltf','diffuse-transmission-oracle.json']]}
((Path(__file__).resolve().parents[6]/'research/gltf-validation/direct-gpu/models/MandarinOrange') / 'diffuse-fixture-audit.json').write_text(json.dumps(result,indent=2)+'\n')
print(json.dumps({'scope':result['scope'],'checks_passed':len(checks),'artifact':str((Path(__file__).resolve().parents[6]/'research/gltf-validation/direct-gpu/models/MandarinOrange') / 'diffuse-fixture-audit.json')}))
