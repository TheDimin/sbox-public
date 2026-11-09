"""Exposure receipt information survives the comparison's public setup projection."""
import unittest
import export_current_comparison as exporter


class ExposureReceiptTests(unittest.TestCase):
    def test_requested_and_observed_are_distinct_and_lock_is_unverified(self):
        presentation = {
            'exposure': 1,
            'exposureLocked': False,
            'exposureLockStatus': 'unverified',
            'exposureDetails': {
                'requested': {'enabled': True, 'minimum': 1, 'maximum': 1, 'compensation': 0},
                'observedAfterRender': {'enabled': True, 'minimum': 1, 'maximum': 2,
                                        'compensation': 0, 'rate': 20, 'sceneMainCamera': True},
                'locked': False, 'lockStatus': 'unverified'},
        }
        exported = exporter.public_setup({'presentation': presentation})['presentation']
        self.assertEqual(exported['exposureDetails']['requested']['maximum'], 1)
        self.assertEqual(exported['exposureDetails']['observedAfterRender']['maximum'], 2)
        self.assertIs(exported['exposureLocked'], False)
        self.assertEqual(exported['exposureLockStatus'], 'unverified')


if __name__ == '__main__':
    unittest.main()
