"""Independent provided-graph arithmetic controls, not shader/GPU evidence."""
import copy
import json
import math
import unittest
from pathlib import Path


def evaluate(graph, rgb=(.75,.25,1), scale=.5, front=True, double_sided=True, has_normal=True):
    nodes = {n['Identifier']:n for n in graph['nodes']}
    def value(link):
        node = nodes[link['Identifier']]
        kind = node['_class']
        if kind == 'Float':
            return {'GltfNormalScale':scale,'GltfDoubleSided':float(double_sided),'GltfHasNormalTexture':float(has_normal)}.get(node['Name'],node['Value'])
        if kind == 'Float3': return [float(x) for x in node['Value'].split(',')]
        if kind == 'TextureSampler':
            # Actual GPU transfer remains a separate gate; literal role values.
            sample = list(rgb)
            if node['UI']['SrgbRead']:
                sample = [((x+.055)/1.055)**2.4 for x in sample]
            return sample[{'R':0,'G':1,'B':2}[link['Output']]]
        if kind == 'CombineVector':
            return [value(node[c]) for c in 'XYZ']
        if kind == 'IsFrontFace': return float(front)
        if kind == 'Normalize':
            a = value(node['Input']); magnitude = math.sqrt(sum(x*x for x in a)); return [x/magnitude for x in a]
        if kind in ('Multiply','Subtract'):
            a = value(node['A']) if node.get('A') else node.get('DefaultA',0)
            b = value(node['B']) if node.get('B') else node.get('DefaultB',1)
            if isinstance(a,list) and not isinstance(b,list): b = [b]*len(a)
            if isinstance(b,list) and not isinstance(a,list): a = [a]*len(b)
            op = (lambda x,y:x*y) if kind=='Multiply' else (lambda x,y:x-y)
            return [op(x,y) for x,y in zip(a,b)] if isinstance(a,list) else op(a,b)
        if kind == 'Lerp':
            a,b,c = (value(node[k]) for k in 'ABC'); return [x+(y-x)*c for x,y in zip(a,b)] if isinstance(a,list) else a+(b-a)*c
        raise AssertionError(kind)
    ts = value(nodes['surface']['Normal'])
    # Independently modeled engine TransformNormal Y correction; literal
    # N=(0,0,1),T=(1,0,0),W=-1 => B=(0,-1,0).
    return [ts[0],ts[1],ts[2]]


class NormalGraphTests(unittest.TestCase):
    def setUp(self):
        self.graph = json.loads(((Path(__file__).resolve().parents[6]/'research/gltf-validation/direct-gpu/models/NormalTangentMirrorTest') / 'surface_normal_candidate.shdrgrph').read_text())
        q = 1/math.sqrt(1.125)
        self.expected = [.25*q,.25*q,q]

    def close(self, actual, expected):
        self.assertLess(max(abs(a-b) for a,b in zip(actual,expected)),1e-12)

    def test_literal_normal_scale_handedness_and_engine_y_correction(self):
        self.close(evaluate(self.graph),self.expected)

    def test_backface_reverses_entire_mapped_direction(self):
        self.close(evaluate(self.graph,front=False),[-x for x in self.expected])

    def test_single_sided_does_not_apply_two_sided_lighting(self):
        self.close(evaluate(self.graph,front=False,double_sided=False),self.expected)

    def test_zero_scale_is_flat_normal(self):
        self.close(evaluate(self.graph,scale=0),[0,0,1])

    def test_absent_texture_is_exact_flat_without_byte_quantization(self):
        self.close(evaluate(self.graph,rgb=(128/255,128/255,1),has_normal=False),[0,0,1])

    def test_deliberate_green_error_is_observable(self):
        node = next(n for n in self.graph['nodes'] if n['Identifier']=='normal_engine_y_correction')
        node['DefaultB']=1
        self.assertGreater(max(abs(a-b) for a,b in zip(evaluate(self.graph),self.expected)),.47)

    def test_deliberate_transfer_error_is_observable(self):
        node = next(n for n in self.graph['nodes'] if n['Identifier']=='normal_texture')
        node['UI']['SrgbRead']=True
        self.assertGreater(max(abs(a-b) for a,b in zip(evaluate(self.graph),self.expected)),.2)

    def test_deliberate_omitted_scale_is_observable(self):
        self.assertGreater(max(abs(a-b) for a,b in zip(evaluate(self.graph,scale=1),self.expected)),.17)

    def test_normal_sampling_retains_independent_uv_sampler_and_ignores_alpha(self):
        nodes = {n['Identifier']:n for n in self.graph['nodes']}
        sample = nodes['normal_texture']
        self.assertEqual(sample['Sampler']['Attribute'],'GltfNormalSampler')
        self.assertEqual(sample['Coords'],{'Identifier':'normal_uv','Output':'XY'})
        self.assertEqual(nodes['normal_row_u']['Name'],'GltfNormalUvRowU')
        self.assertEqual(nodes['normal_row_v']['Name'],'GltfNormalUvRowV')
        self.assertEqual(nodes['normal_rgb']['Z'],{'Identifier':'normal_texture','Output':'B'})
        self.assertEqual(nodes['normal_xyz_scale']['Z'],{'Identifier':'constant_one','Output':'Result'})


if __name__=='__main__': unittest.main()
