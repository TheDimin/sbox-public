"""Independent oracle controls only; these do not execute the C# reader or GPU."""
import copy
import pathlib
import struct
import unittest

import author_reference as oracle


class ReferenceTests(unittest.TestCase):
    def test_accessor_uses_actual_stride_and_both_offsets(self):
        data = bytes(8) + struct.pack('<ffff', 99, 1, 2, 88) + struct.pack('<ffff', 99, 3, 4, 88)
        doc = {'bufferViews': [{'buffer': 0, 'byteOffset': 8, 'byteLength': 32, 'byteStride': 16}],
               'accessors': [{'bufferView': 0, 'byteOffset': 4, 'componentType': 5126, 'count': 2, 'type': 'VEC2'}]}
        self.assertEqual(oracle.accessor(doc, [data], 0), [[1, 2], [3, 4]])

    def test_sparse_without_base_starts_with_zero(self):
        doc = {'bufferViews': [{'buffer': 0, 'byteOffset': 0, 'byteLength': 1},
                               {'buffer': 0, 'byteOffset': 4, 'byteLength': 12}],
               'accessors': [{'componentType': 5126, 'count': 3, 'type': 'VEC3', 'sparse': {
                   'count': 1, 'indices': {'bufferView': 0, 'componentType': 5121}, 'values': {'bufferView': 1}}}]}
        self.assertEqual(oracle.accessor(doc, [b'\x02\0\0\0' + struct.pack('<fff', 1, -2, 3)], 0),
                         [[0, 0, 0], [0, 0, 0], [1, -2, 3]])

    def test_linear_interpolation_and_clamping_preserve_target_order(self):
        times, values = [1, 3], [[-1, 2], [3, -2]]
        self.assertEqual(oracle.sample_weights(times, values, -9), [-1, 2])
        self.assertEqual(oracle.sample_weights(times, values, 1.5), [0, 1])
        self.assertEqual(oracle.sample_weights(times, values, 9), [3, -2])

    def test_pinned_actual_source_shape_and_key_counts(self):
        reference = oracle.build()
        self.assertEqual(reference['targetNames'], [f'Key {i}' for i in range(1, 9)])
        self.assertEqual([p['vertexCount'] for p in reference['primitives']], [24, 1504])
        self.assertEqual([len(c['times']) for c in reference['clips']], [281, 59, 191])
        self.assertTrue(all(c['interpolation'] == 'LINEAR' for c in reference['clips']))
        self.assertEqual(len(reference['forms']), 2)
        self.assertTrue(all(not a['sparse'] for f in reference['forms'] for a in f['accessors']))

    def test_each_eighth_target_has_an_independent_geometry_witness(self):
        r = oracle.build()
        self.assertTrue(all(all(x == 0 for v in r['values'][str(t['POSITION'])] for x in v)
                            for t in r['primitives'][0]['targets']))
        moving = r['primitives'][1]
        witnesses = []
        for target in range(8):
            weights = [0] * 8
            weights[target] = 1
            changed = [v for v in range(1504) if oracle.morphed(r, 1, v, weights, 'POSITION') !=
                       oracle.morphed(r, 1, v, [0] * 8, 'POSITION')]
            self.assertTrue(changed, f'Target {target} must move real geometry')
            witnesses.append(changed)
        self.assertEqual(len({tuple(x) for x in witnesses}), 8)

    def test_signed_weights_differ_from_clamping_and_last_target_truncation(self):
        r = oracle.build()
        weights = [-.5, 1.75, -.25, 1.5, -.75, 1.25, -.125, 2]
        correct = [oracle.morphed(r, 1, v, weights, 'POSITION') for v in range(1504)]
        clamp = [min(1, max(0, w)) for w in weights]
        truncated = weights[:7] + [0]
        self.assertNotEqual(correct, [oracle.morphed(r, 1, v, clamp, 'POSITION') for v in range(1504)])
        self.assertNotEqual(correct, [oracle.morphed(r, 1, v, truncated, 'POSITION') for v in range(1504)])

    def test_target_permutation_is_detectable_and_zero_normal_is_explicit(self):
        r = oracle.build()
        self.assertNotEqual(r['values'][str(r['primitives'][1]['targets'][0]['POSITION'])],
                            r['values'][str(r['primitives'][1]['targets'][7]['POSITION'])])
        self.assertIsNone(oracle.normalized([0, 0, 0]))
        self.assertEqual(oracle.normalized([0, -2, 0]), [0, -1, 0])


if __name__ == '__main__':
    unittest.main()
