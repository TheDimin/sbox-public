"""Add glTF normal sampling to the shared provided Surface/Lit graph candidate.

This emits graph JSON only. The coordinator must instantiate the actual provided
nodes, review generated shader, compile, and render before material acceptance.
No custom node/shader is introduced and the source PNG is never rewritten.
"""
import hashlib
import importlib.util
import json
from pathlib import Path

HERE = Path(__file__).resolve().parent
BASE = HERE.parent/'TextureEncodingTest/author_surface_orm_candidate.py'


def extend(graph):
    nodes = graph['nodes']
    def add(kind, identifier, **properties):
        nodes.append({'_class': kind, 'Identifier': identifier, **properties})
        return identifier
    def link(identifier, output='Result'):
        return {'Identifier': identifier, 'Output': output}
    def parameter(kind, identifier, name, value):
        zero = 0 if kind == 'Float' else '0,0,0'
        return add(kind, identifier, Name=name, Value=value, Min=zero, Max=zero, IsAttribute=False, UI={'Type':'Default'})

    uv1 = parameter('Float', 'normal_uv1', 'GltfNormalUv1', 0)
    selected = add('Lerp','normal_uv_selected',A=link('uv0'),B=link('uv1'),C=link(uv1))
    affine = add('AppendVector','normal_uv_affine',A=link(selected),B=link('constant_one'))
    rowu = parameter('Float3','normal_row_u','GltfNormalUvRowU','1,0,0')
    rowv = parameter('Float3','normal_row_v','GltfNormalUvRowV','0,1,0')
    u = add('DotProduct','normal_u',InputA=link(affine,'Output'),InputB=link(rowu))
    v = add('DotProduct','normal_v',InputA=link(affine,'Output'),InputB=link(rowv))
    uv = add('CombineVector','normal_uv',X=link(u),Y=link(v))
    sample = add('TextureSampler','normal_texture',Image='',Coords=link(uv,'XY'),
                 Sampler={'Filter':'Bilinear','AddressU':'Wrap','AddressV':'Wrap','Attribute':'GltfNormalSampler'},
                 UI={'Name':'GltfNormalTexture','IsAttribute':False,'Default':'0.5,0.5,1,1','Extension':'Color',
                     'CustomExtension':'gltf_normal','Processor':'None','ColorSpace':'Linear','ImageFormat':'RGBA8888','SrgbRead':False})
    rgb = add('CombineVector','normal_rgb',X=link(sample,'R'),Y=link(sample,'G'),Z=link(sample,'B'))
    doubled = add('Multiply','normal_rgb_times_two',A=link(rgb,'XYZ'),DefaultB=2)
    decoded = add('Subtract','normal_signed_rgb',A=link(doubled),DefaultB=1)
    scale = parameter('Float','normal_scale','GltfNormalScale',1)
    # Provided TransformNormal flips Y for Source1. Compensate once here;
    # the source normal map remains OpenGL positive-green and RGB stays linear.
    negative_scale = add('Multiply','normal_engine_y_correction',A=link(scale),DefaultB=-1)
    xyz_scale = add('CombineVector','normal_xyz_scale',X=link(scale),Y=link(negative_scale),Z=link('constant_one'))
    scaled = add('Multiply','normal_scaled',A=link(decoded),B=link(xyz_scale,'XYZ'))
    has_normal = parameter('Float','normal_has_texture','GltfHasNormalTexture',0)
    flat = parameter('Float3','normal_exact_flat','','0,0,1')
    selected_normal = add('Lerp','normal_present_or_flat',A=link(flat),B=link(scaled),C=link(has_normal))
    normalized = add('Normalize','normal_unit',Input=link(selected_normal))
    front = add('IsFrontFace','normal_front_facing')
    sign = add('Multiply','normal_front_twice',A=link(front),DefaultB=2)
    signed = add('Subtract','normal_facing_sign',A=link(sign),DefaultB=1)
    two_sided = parameter('Float','normal_double_sided','GltfDoubleSided',0)
    facing = add('Lerp','normal_apply_facing',A=link('constant_one'),B=link(signed),C=link(two_sided))
    result = add('Multiply','normal_complete_backface_reversal',A=link(normalized),B=link(facing))
    next(node for node in nodes if node['Identifier']=='surface')['Normal'] = link(result)
    graph['Description'] = 'Provided glTF Surface/Lit normal candidate: explicit linear RGB, XY scale, engine Y correction and complete backface reversal; GPU proof pending'
    graph['RenderBackfaces'] = True
    ids = {n['Identifier'] for n in nodes}
    assert len(ids) == len(nodes)
    for node in nodes:
        for value in node.values():
            if isinstance(value,dict) and 'Identifier' in value:
                assert value['Identifier'] in ids
    return graph


if __name__ == '__main__':
    spec = importlib.util.spec_from_file_location('surface_orm_author',BASE)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    graph = extend(module.graph())
    target = (Path(__file__).resolve().parents[6]/'research/gltf-validation/direct-gpu/models/NormalTangentMirrorTest') / 'surface_normal_candidate.shdrgrph'
    target.write_text(json.dumps(graph,indent=2)+'\n')
    print(json.dumps({'path':str(target),'nodes':len(graph['nodes']),
                      'base_generator_sha256':hashlib.sha256(BASE.read_bytes()).hexdigest(),
                      'graph_sha256':hashlib.sha256(target.read_bytes()).hexdigest()}))
