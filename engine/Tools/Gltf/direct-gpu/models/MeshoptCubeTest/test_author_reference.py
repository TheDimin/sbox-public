import unittest
from author_reference import build, values


class ReferenceTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.oracle = build()

    def test_actual_formats_required_policy_and_codec_matrix(self):
        self.assertEqual(list(self.oracle['forms']), ['glTF', 'glTF-Meshopt'])
        self.assertEqual(self.oracle['forms']['glTF']['required'], ['KHR_mesh_quantization'])
        self.assertEqual(self.oracle['forms']['glTF-Meshopt']['required'], ['KHR_mesh_quantization', 'KHR_meshopt_compression'])
        codecs = self.oracle['compression']
        self.assertEqual(len(codecs), 60)
        self.assertEqual({x['header'] for x in codecs}, {0xa0, 0xa1, 0xd1, 0xe1})
        self.assertEqual({x['filter'] for x in codecs}, {'NONE', 'OCTAHEDRAL', 'EXPONENTIAL', 'COLOR', 'QUATERNION'})

    def test_interleaved_normal_color_offsets_defeat_tight_float_assumption(self):
        a = self.oracle['accessors']
        self.assertEqual((a[4]['offset'], a[5]['offset'], a[6]['offset']), (0, 12, 16))
        self.assertEqual([a[i]['stride'] for i in [4, 5, 6]], [20, 20, 20])
        self.assertEqual([a[i]['component_type'] for i in [4, 5, 6]], [5126, 5120, 5121])
        self.assertTrue(any(x[0] == -1 for x in a[5]['values']))

    def test_normalized_minimum_saturates(self):
        self.assertEqual(values(dict(componentType=5120, count=1, type='VEC4', normalized=True), {}, bytes([128, 129, 0, 127])), [[-1, -1, 0, 1]])

    def test_literal_topology_and_all_cube_bounds(self):
        a = self.oracle['accessors']
        for mesh in self.oracle['meshes'][10:]:
            primitive = mesh['primitives'][0]
            p = a[primitive['attributes']['POSITION']]['values']
            idx = a[primitive['indices']]['values']
            self.assertEqual(len(p), 24)
            self.assertEqual(len(idx), 36)
            self.assertEqual([min(v[c] for v in p) for c in range(3)], [-.5] * 3)
            self.assertEqual([max(v[c] for v in p) for c in range(3)], [.5] * 3)
            self.assertTrue(all(0 <= v[0] < 24 for v in idx))

    def test_literal_rotation_keys_and_normalized_int16(self):
        a = self.oracle['accessors']
        for sampler in self.oracle['animations'][0]['samplers']:
            self.assertEqual(a[sampler['input']]['values'], [[0], [1], [2]])
            self.assertEqual(a[sampler['output']]['component_type'], 5122)
            self.assertEqual(a[sampler['output']]['values'][0], [0, 0, 0, 1])
            self.assertEqual(a[sampler['output']]['values'][1], [0, 23170 / 32767, 0, 23170 / 32767])


if __name__ == '__main__':
    unittest.main()
