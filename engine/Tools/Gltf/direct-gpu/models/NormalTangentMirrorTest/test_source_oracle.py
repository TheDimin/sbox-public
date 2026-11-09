"""Small independent-source audit, no engine build/native allocation."""
import math
import unittest
from author_reference import build, load, accessor, SOURCE


class SourceOracleTests(unittest.TestCase):
    def test_actual_variants_equal_and_have_both_handedness_signs(self):
        data = build()
        self.assertEqual(data['negative_w_vertices'], 80)
        self.assertEqual(len(data['files']), 6)

    def test_expected_frame_has_separate_green_and_handedness_controls(self):
        # Literal basis +X,+Y,+Z, W=-1 and linear normal RGB .75,.25,1.
        # scale .5 gives tangent-space [.25,-.25,1]; W flips the Y contribution.
        q = 1 / math.sqrt(1.125)
        expected = [.25*q, .25*q, q]
        self.assertGreater(expected[1], 0)
        wrong_green = [.25*q, -.25*q, q]
        self.assertGreater(max(abs(a-b) for a,b in zip(expected,wrong_green)), .47)
        # sRGB decoding is detectably wrong even if shader normalizes afterward.
        decode = lambda v: ((v+.055)/1.055)**2.4
        wrong = [(2*decode(.75)-1)*.5, -(2*decode(.25)-1)*.5, 1]
        length = math.sqrt(sum(x*x for x in wrong))
        self.assertGreater(max(abs(a-b/length) for a,b in zip(expected,wrong)), .2)

    def test_tangent_generation_is_inapplicable_to_actual_asset(self):
        doc, raw = load(SOURCE/'glTF/NormalTangentMirrorTest.gltf')
        primitive = doc['meshes'][0]['primitives'][0]
        self.assertEqual(primitive['attributes']['TANGENT'], 3)
        self.assertEqual(primitive['attributes']['NORMAL'], 2)
        self.assertNotIn('extensionsUsed', doc)
        self.assertTrue(doc['materials'][0]['doubleSided'])
        tangent = accessor(doc,raw,3)['values']
        for i in accessor(doc,raw,0)['values']:
            self.assertIn(tangent[i[0]][3], (-1,1))

    def test_pinned_export_precision_is_recorded_not_hidden(self):
        data = build()
        self.assertEqual(data['max_authored_normal_length_squared_error'], 8.879864006061666e-05)
        self.assertEqual(data['max_authored_normal_tangent_dot'], 1.1335942421197842e-05)


if __name__ == '__main__':
    unittest.main()
