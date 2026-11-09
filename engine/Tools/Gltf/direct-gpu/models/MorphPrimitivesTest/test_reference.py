#!/usr/bin/env python3
"""Oracle author controls only, independent of the reader/renderer."""
import json
from pathlib import Path
import unittest

HERE = Path(__file__).resolve().parent


class ReferenceControls(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.ref = json.loads(((Path(__file__).resolve().parents[6]/'research/gltf-validation/direct-gpu/models/MorphPrimitivesTest') / 'reference.json').read_text())

    def test_two_primitives_are_distinct_and_both_required(self):
        for form in self.ref['forms'].values():
            p = form['primitives']
            self.assertEqual([len(x['attributes']['POSITION']['values']) for x in p], [21, 9])
            self.assertEqual([len(x['indices']) for x in p], [72, 24])
            self.assertEqual([x['material'] for x in p], [0, 1])

    def test_default_half_weight_raises_both_primitives(self):
        for form in self.ref['forms'].values():
            for p in form['primitives']:
                y = [v[1] for v in p['morph']['values']]
                self.assertGreater(max(y)*.5, .099)
                self.assertEqual(min(y), 0)

    def test_signed_weight_control_detects_clamping(self):
        p = self.ref['forms']['glTF']['primitives'][1]
        moving = next(i for i, v in enumerate(p['morph']['values']) if v[1] > .1)
        base = p['attributes']['POSITION']['values'][moving]
        delta = p['morph']['values'][moving]
        negative = base[1] - .75*delta[1]
        over_one = base[1] + 1.5*delta[1]
        self.assertLess(negative, -.14)
        self.assertGreater(over_one, .29)

    def test_per_primitive_normals_uvs_must_not_be_reused(self):
        p = self.ref['forms']['glTF']['primitives']
        self.assertNotEqual(p[0]['attributes']['POSITION']['viewOffset'], p[1]['attributes']['POSITION']['viewOffset'])
        self.assertNotEqual(p[0]['morph']['viewOffset'], p[1]['morph']['viewOffset'])
        self.assertEqual(set(p[0]['attributes']), {'POSITION', 'NORMAL', 'TEXCOORD_0'})
        self.assertEqual(set(p[1]['attributes']), {'POSITION', 'NORMAL', 'TEXCOORD_0'})

    def test_binary_is_real_upstream_variant(self):
        self.assertEqual(self.ref['forms']['glTF-Binary']['source'], 'MorphPrimitivesTest.glb')
        self.assertNotEqual(self.ref['forms']['glTF']['sourceSha256'], self.ref['forms']['glTF-Binary']['sourceSha256'])

    def test_draco_reference_is_variant_local_and_independent_integration(self):
        self.assertIn('shared codec ancestry', self.ref['decoder']['independence'])
        for p in self.ref['forms']['glTF-Draco']['primitives']:
            self.assertTrue(all(a['materializedDraco'] for a in p['attributes'].values()))
            self.assertLess(max(p['indices']), p['attributes']['POSITION']['count'])


if __name__ == '__main__':
    unittest.main()
