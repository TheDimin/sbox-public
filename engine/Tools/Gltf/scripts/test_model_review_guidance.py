import copy
import json
from pathlib import Path
import unittest
from model_review_guidance import model_guidance, enrich_guidance, variant_guidance

ROOT = Path(__file__).resolve().parents[4]
CORPUS = json.loads((ROOT / 'research/gltf-validation/corpus-case-manifest.json').read_text())
CORPUS_ROOT = ROOT / 'Temp/gltf-reference/glTF-Sample-Assets'

class GuidanceTests(unittest.TestCase):
    def test_every_model_has_specific_sourced_purpose_and_review_limits(self):
        guides = model_guidance(CORPUS, CORPUS_ROOT)
        self.assertEqual(len(guides), 150)
        self.assertEqual(len({g['checks'][0] for g in guides.values()}), 150)
        for guide in guides.values():
            self.assertTrue(guide['documentedPurpose'])
            self.assertIn(CORPUS['corpus']['commit'], guide['documentedSource'])
            self.assertLessEqual(len(guide['documentedPurpose'].split()), 23)
            self.assertIn('our inference', guide['reviewBasis'])
            self.assertTrue(guide['limits'])

    def test_diagnostic_exceptions_and_box_scope(self):
        guides = model_guidance(CORPUS, CORPUS_ROOT)
        box = guides['Box']
        self.assertEqual(box['documentedPurpose'], 'One mesh and one material. Start with this.')
        self.assertIn('normals', box['checks'][0])
        self.assertIn('matched lighting/exposure', box['limits'])
        self.assertIn('static', guides['MorphPrimitivesTest']['checks'][0])
        self.assertIn('0.5', guides['MorphPrimitivesTest']['checks'][0])
        self.assertIn('generated tangents', guides['NormalTangentTest']['limits'])
        self.assertIn('supplied tangents', guides['NormalTangentMirrorTest']['limits'])
        self.assertIn('square', guides['MultipleScenes']['checks'][0])
        self.assertIn('not establish all three', guides['Fox']['limits'])

    def test_animation_and_material_requirements_come_from_source_not_filenames(self):
        rows = {c['id']: variant_guidance(c) for c in CORPUS['cases']}
        self.assertEqual(len(rows), 337)
        self.assertEqual(rows['MorphPrimitivesTest/glTF']['animationClips'], 0)
        self.assertEqual(rows['Fox/glTF']['animationClips'], 3)
        self.assertEqual(rows['InterpolationTest/glTF']['animationClips'], 9)
        self.assertTrue(rows['NormalTangentTest/glTF']['controlledNormalMapViewRequired'])
        self.assertFalse(rows['Box/glTF']['controlledNormalMapViewRequired'])
        self.assertTrue(any('Draco' in line for line in rows['Box/glTF-Draco']['checks']))

    def test_enrichment_cannot_modify_evidence_fingerprints_or_statuses(self):
        data = {'models': [{'id': m} for m in dict.fromkeys(c['model'] for c in CORPUS['cases'])],
                'cases': [{'id': c['id'], 'comparisonFingerprint': 'unchanged-' + c['id'],
                           'load': {'status': 'blocked'}, 'capture': None,
                           'visualReview': {'status': 'pending'}} for c in CORPUS['cases']]}
        before = copy.deepcopy(data)
        enrich_guidance(data, CORPUS, CORPUS_ROOT)
        for case, old in zip(data['cases'], before['cases']):
            self.assertEqual({k:v for k,v in case.items() if k != 'reviewGuidance'}, old)

    def test_readme_drift_is_rejected(self):
        altered = copy.deepcopy(CORPUS)
        altered['files']['Models/Box/README.md']['sha256'] = '0' * 64
        with self.assertRaisesRegex(ValueError, 'Pinned README changed'):
            model_guidance(altered, CORPUS_ROOT)

if __name__ == '__main__':
    unittest.main()
