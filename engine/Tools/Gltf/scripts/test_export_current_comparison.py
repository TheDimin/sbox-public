#!/usr/bin/env python3
import copy
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import export_current_comparison as exporter


class ExportTests(unittest.TestCase):
    def test_public_environment_keeps_hashes_without_local_paths(self):
        setup = {'presentation': {'environment': {
            'type': 'cubemap', 'texturePath': 'textures/environment.vtex',
            'sourcePath': '/private/source.exr', 'compiledPath': '/private/environment.vtex_c',
            'sourceSha256': 'a' * 64, 'compiledSha256': 'b' * 64,
            'Width': 256, 'Depth': 6, 'Mips': 7, 'limitations': 'HDR range and orientation unproven.'}}}
        before = copy.deepcopy(setup)
        public = exporter.public_setup(setup)['presentation']['environment']
        self.assertNotIn('sourcePath', public)
        self.assertNotIn('compiledPath', public)
        self.assertEqual(public['sourceSha256'], 'a' * 64)
        self.assertEqual(public['compiledSha256'], 'b' * 64)
        self.assertEqual(public['texturePath'], 'textures/environment.vtex')
        self.assertEqual(public['limitations'], 'HDR range and orientation unproven.')
        self.assertEqual(setup, before)

    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.output = self.root / 'site'
        self.source_hash = 'a' * 64
        self.pin = 'b' * 40
        self.corpus = {'corpus': {'commit': self.pin}, 'files': {'Models/Box/glTF/Box.gltf': {'sha256': self.source_hash}, 'Models/Box/glTF/buffer.bin': {'sha256': 'e' * 64}}, 'cases': [
            {'id': 'Box/glTF', 'model': 'Box', 'variant': 'glTF', 'path': 'Models/Box/glTF/Box.gltf', 'dependencies': ['Models/Box/glTF/buffer.bin']},
            {'id': 'Box/glTF-Binary', 'model': 'Box', 'variant': 'glTF-Binary', 'path': 'Models/Box/glTF-Binary/Box.glb'}]}
        self.run = {'schemaVersion': 1, 'corpusPin': self.pin, 'run': {'id': 'current', 'producer': {'engine': 'build-a'}}, 'cases': [{'id': c['id'], 'recipeId': c['id'], 'load': {'status': 'not-run'}, 'render': {'status': 'not-run'}, 'capture': None} for c in self.corpus['cases']]}
        self.recipes = {'schemaVersion': 1, 'corpusPin': self.pin, 'cases': [{'id': c['id'], 'reference': {'url': 'https://example.test/reference.png', 'sha256': 'c' * 64, 'kind': 'image'}, 'matchStatus': 'needs-tuning'} for c in self.corpus['cases']]}

    def export(self):
        self.run['run']['recipesSha256'] = __import__('hashlib').sha256(json.dumps(self.recipes).encode()).hexdigest()
        for name, value in [('corpus', self.corpus), ('run', self.run), ('recipes', self.recipes)]:
            (self.root / (name + '.json')).write_text(json.dumps(value))
        return exporter.export_current(self.root / 'corpus.json', self.root / 'run.json', self.root / 'recipes.json', self.output)

    def captured(self):
        capture = self.root / 'capture.png'
        __import__('PIL.Image', fromlist=['Image']).new('RGBA', (2, 2), 'red').save(capture)
        self.run['cases'][0] = {'id': 'Box/glTF', 'recipeId': 'Box/glTF', 'recipeApplied': {'camera': {'x': 1}}, 'input': {'path': 'Models/Box/glTF/Box.gltf', 'sha256': self.source_hash, 'dependenciesVerified': True, 'dependencies': {'Models/Box/glTF/buffer.bin': self.corpus['files']['Models/Box/glTF/buffer.bin']['sha256']}}, 'load': {'status': 'passed'}, 'render': {'status': 'passed'}, 'capture': {'path': 'capture.png', 'sha256': exporter.sha(capture), 'kind': 'image'}}

    def add_recipe_proofs(self):
        self.run['run']['recipeProofVersion'] = 1
        for row, recipe in zip(self.run['cases'], self.recipes['cases']):
            row['recipeSnapshot'] = copy.deepcopy(recipe)
            row['recipeSha256'] = exporter.fingerprint(recipe)

    def test_new_capture_stock_proof_is_required_and_checked_before_writing(self):
        self.captured()
        initial = self.export()
        before = (self.output / 'data.json').read_bytes()
        before_media = (self.output / initial['cases'][0]['capture']['url']).read_bytes()
        runner = exporter.stock_proof_runner()
        resources = {name: {'present': True, 'sha256': 'a' * 64, 'origin': 'loose-core'} for name in runner.STOCK_COMPILED_RESOURCES}
        for name, digest in runner.AUDITED_SHADER_RESOURCES.items():
            resources[name]['sha256'] = digest
        resources.update({name: {'present': False, 'sha256': None, 'origin': 'unavailable'} for name in runner.STOCK_SOURCE_RESOURCES})
        proof = {'stockResourceProofVersion': 2, 'stockResources': resources,
                 'stockDependencies': {name: sorted(runner.SHADER_DELEGATIONS.get(name, [])) for name in runner.STOCK_COMPILED_RESOURCES},
                 'stockDependencyErrors': []}
        self.run['run']['producer'].update(copy.deepcopy(proof))
        self.run['cases'][0]['stockResourceProof'] = copy.deepcopy(proof)
        original = copy.deepcopy(self.run)
        def missing_case_proof():
            self.run['cases'][0].pop('stockResourceProof')
        def uncertified_case_proof():
            self.run['cases'][0]['stockResourceProof']['stockDependencyErrors'] = ['unproven dependency']
        def old_child_bytes():
            self.run['cases'][0]['stockResourceProof']['stockResources'][runner.STOCK_COMPILED_RESOURCES[-1]]['sha256'] = 'b' * 64
        def missing_global_proof():
            self.run['run']['producer'].pop('stockResourceProofVersion')
        for mutate in [missing_case_proof, uncertified_case_proof, old_child_bytes, missing_global_proof]:
            with self.subTest(mutation=mutate.__name__):
                self.run = copy.deepcopy(original)
                mutate()
                with patch.object(runner, 'stock_resource_proof', return_value=proof), \
                     patch.object(exporter.tempfile, 'mkdtemp', wraps=exporter.tempfile.mkdtemp) as stage:
                    with self.assertRaisesRegex(ValueError, '[Ss]tock resource'):
                        self.export()
                    stage.assert_not_called()
                self.assertEqual(before, (self.output / 'data.json').read_bytes())
                self.assertEqual(before_media, (self.output / initial['cases'][0]['capture']['url']).read_bytes())
        self.run = copy.deepcopy(original)
        changed_disk = copy.deepcopy(proof)
        changed_disk['stockResources'][runner.STOCK_COMPILED_RESOURCES[-1]]['sha256'] = 'b' * 64
        with patch.object(runner, 'stock_resource_proof', return_value=changed_disk):
            with self.assertRaisesRegex(ValueError, '[Ss]tock resource'):
                self.export()
        self.assertEqual(before, (self.output / 'data.json').read_bytes())
        with patch.object(runner, 'stock_resource_proof', return_value=proof):
            proven = self.export()
        self.assertEqual(proven['summary']['captures'], 1)
        self.assertEqual(initial['cases'][0]['comparisonFingerprint'], proven['cases'][0]['comparisonFingerprint'])
        self.assertNotEqual(initial['cases'][0]['provenanceFingerprint'],proven['cases'][0]['provenanceFingerprint'])

    def test_invalid_case_recipe_proof_fails_before_website_mutation(self):
        self.captured()
        initial = self.export()
        before_data = (self.output / 'data.json').read_bytes()
        before_media = (self.output / initial['cases'][0]['capture']['url']).read_bytes()
        self.add_recipe_proofs()
        original = copy.deepcopy((self.run, self.recipes))
        def tamper_snapshot():
            self.run['cases'][0]['recipeSnapshot']['camera'] = {'x': 99}
        def tamper_hash():
            self.run['cases'][0]['recipeSha256'] = '0' * 64
        def change_current_recipe():
            # Even an updated global document hash cannot relabel the old pixels.
            self.recipes['cases'][0]['camera'] = {'x': 2}
        def remove_snapshot():
            self.run['cases'][0].pop('recipeSnapshot')
        def wrong_snapshot_identity():
            self.run['cases'][0]['recipeSnapshot']['id'] = 'wrong'
            self.run['cases'][0]['recipeSha256'] = exporter.fingerprint(self.run['cases'][0]['recipeSnapshot'])
        for mutate in [tamper_snapshot, tamper_hash, change_current_recipe, remove_snapshot, wrong_snapshot_identity]:
            with self.subTest(mutation=mutate.__name__):
                self.run, self.recipes = copy.deepcopy(original)
                mutate()
                with patch.object(exporter.tempfile, 'mkdtemp', wraps=exporter.tempfile.mkdtemp) as stage:
                    with self.assertRaisesRegex(ValueError, '[Rr]ecipe'):
                        self.export()
                    stage.assert_not_called()
                self.assertEqual(before_data, (self.output / 'data.json').read_bytes())
                self.assertEqual(before_media, (self.output / initial['cases'][0]['capture']['url']).read_bytes())

    def test_new_proof_mode_requires_all_case_proofs_before_creating_output(self):
        self.run['run']['recipeProofVersion'] = 1
        with self.assertRaisesRegex(ValueError, '[Rr]ecipe'):
            self.export()
        self.assertFalse(self.output.exists())

    def test_partial_proofs_cannot_downgrade_other_rows_to_legacy(self):
        self.run['cases'][0]['recipeSnapshot'] = copy.deepcopy(self.recipes['cases'][0])
        self.run['cases'][0]['recipeSha256'] = exporter.fingerprint(self.recipes['cases'][0])
        with self.assertRaisesRegex(ValueError, 'recipe snapshot'):
            self.export()
        self.assertFalse(self.output.exists())

    def test_unchanged_case_proofs_preserve_legacy_fingerprints_and_guidance(self):
        self.captured()
        pin = '90d7ede14c7e280af263824604b427a1ca02cb66'
        self.corpus['corpus']['commit'] = self.run['corpusPin'] = self.recipes['corpusPin'] = pin
        self.recipes['corpusRoot'] = str(self.root)
        def guidance(data, corpus, corpus_root):
            data['guidanceVersion'] = 'fixture'
            for row in data['cases']:
                row['reviewGuidance'] = {'instructions': ['Judge appearance yourself'], 'status': 'pending'}
        with patch('model_review_guidance.enrich_guidance', side_effect=guidance):
            legacy = self.export()
            self.add_recipe_proofs()
            proven = self.export()
        self.assertEqual(legacy, proven)

    def test_complete_inventory_without_history_fallback(self):
        data = self.export()
        self.assertEqual(len(data['cases']), 2)
        self.assertEqual(data['summary']['load'], {'not-run': 2})
        self.assertTrue(all(c['capture'] is None and c['visualReview']['status'] == 'pending' for c in data['cases']))

    def test_failure_replaces_success_and_deletes_old_media(self):
        self.captured()
        first = self.export()
        asset = self.output / first['cases'][0]['capture']['url']
        self.assertTrue(asset.exists())
        self.run['cases'][0].update(load={'status': 'failed', 'error': 'bad /home/person/private/file.cs\nstack'}, render={'status': 'not-run'}, capture=None)
        second = self.export()
        self.assertFalse(asset.exists())
        self.assertIsNone(second['cases'][0]['capture'])
        self.assertEqual(second['cases'][0]['load']['error'], 'bad [local path]')
        self.assertNotEqual(first['cases'][0]['comparisonFingerprint'], second['cases'][0]['comparisonFingerprint'])

    def test_no_automatic_visual_approval(self):
        self.captured()
        self.run['cases'][0]['visualReview'] = {'status': 'approved', 'reviewer': 'machine'}
        self.assertEqual(self.export()['cases'][0]['visualReview'], {'status': 'pending', 'reviewer': None})

    def test_metadata_changes_preserve_visual_review_but_reference_changes_it(self):
        self.captured()
        prior = self.export()['cases'][0]['comparisonFingerprint']
        mutations = [lambda: self.run['run']['producer'].update(engine='build-b'), lambda: self.recipes['cases'][0].update(camera={'x': 1}), lambda: (self.corpus['files']['Models/Box/glTF/buffer.bin'].update(sha256='f' * 64), self.run['cases'][0]['input']['dependencies'].update({'Models/Box/glTF/buffer.bin':'f'*64}))]
        for mutate in mutations:
            mutate()
            now = self.export()['cases'][0]['comparisonFingerprint']
            self.assertEqual(prior, now)
        self.recipes['cases'][0]['reference'].update(sha256='d' * 64)
        self.assertNotEqual(prior,self.export()['cases'][0]['comparisonFingerprint'])

    def test_invalid_capture_preserves_current_output(self):
        self.captured()
        self.export()
        prior = (self.output / 'data.json').read_bytes()
        self.run['cases'][0]['capture']['sha256'] = '0' * 64
        with self.assertRaisesRegex(ValueError, 'changed capture'):
            self.export()
        self.assertEqual(prior, (self.output / 'data.json').read_bytes())
        self.assertEqual(len(list((self.output / 'assets/current').iterdir())), 1)

    def test_new_pixels_change_url_and_remove_obsolete_outputs(self):
        self.captured()
        first = self.export()
        old = self.output / first['cases'][0]['capture']['url']
        (self.output / 'receipts').mkdir()
        (self.output / 'receipts/obsolete.json').write_text('{}')
        (self.output / 'direct-gpu-provenance.json').write_text('{}')
        __import__('PIL.Image', fromlist=['Image']).new('RGBA', (2, 2), 'blue').save(self.root / 'capture.png')
        self.run['cases'][0]['capture']['sha256'] = exporter.sha(self.root / 'capture.png')
        second = self.export()
        self.assertNotEqual(first['cases'][0]['capture']['url'], second['cases'][0]['capture']['url'])
        self.assertFalse(old.exists())
        self.assertFalse((self.output / 'receipts').exists())
        self.assertFalse((self.output / 'direct-gpu-provenance.json').exists())

    def test_failed_case_cannot_retain_success_capture(self):
        self.captured()
        self.run['cases'][0]['load']['status'] = 'failed'
        with self.assertRaisesRegex(ValueError, 'Render pass'):
            self.export()

    def test_missing_capture_cannot_be_render_pass(self):
        self.captured()
        self.run['cases'][0]['capture'] = None
        with self.assertRaisesRegex(ValueError, 'Render pass'):
            self.export()

    def test_pin_recipe_and_case_identity_are_enforced(self):
        for mutation in [lambda: self.run.update(corpusPin='0' * 40), lambda: self.recipes['cases'].pop(), lambda: self.run['cases'].append({'id': 'invented'})]:
            original = copy.deepcopy((self.run, self.recipes))
            mutation()
            with self.assertRaises(ValueError):
                self.export()
            self.run, self.recipes = original

    def test_canonical_blocked_interruption_stays_distinct_from_load_rejection(self):
        self.run['run'].update(status='interrupted', pendingCase='Box/glTF')
        self.run['cases'][0].update(load={'status': 'blocked', 'error': 'Native execution interrupted'}, render={'status': 'blocked', 'error': 'Native execution interrupted'})
        data = self.export()
        self.assertEqual(data['cases'][0]['load']['status'], 'blocked')
        self.assertEqual(data['cases'][0]['render']['status'], 'blocked')
        self.assertIsNone(data['cases'][0]['capture'])
        self.assertEqual(self.run['cases'][0]['load']['status'], 'blocked')

    def test_resume_preserves_original_case_producer_fingerprint(self):
        self.captured()
        self.run['cases'][0]['producer'] = copy.deepcopy(self.run['run']['producer'])
        before = self.export()['cases'][0]['comparisonFingerprint']
        self.run['run']['producer'] = {'engine': 'same engine', 'runnerSha256': 'new resume orchestration'}
        after = self.export()['cases'][0]['comparisonFingerprint']
        self.assertEqual(before, after)
        self.run['cases'][0]['producer'] = {'engine': 'changed engine'}
        self.assertEqual(before, self.export()['cases'][0]['comparisonFingerprint'])

    def test_recipe_edit_after_run_is_rejected(self):
        self.export()
        self.recipes['cases'][0]['camera'] = {'x': 2}
        (self.root / 'recipes.json').write_text(json.dumps(self.recipes))
        with self.assertRaisesRegex(ValueError, 'Recipes|recipes'):
            exporter.export_current(self.root / 'corpus.json', self.root / 'run.json', self.root / 'recipes.json', self.output)

    def test_public_data_omits_runtime_logs_paths_and_frame_traces(self):
        self.captured()
        self.run['run'].update(error='/home/private/trace.log', pid=1234)
        self.run['cases'][0]['render']['observation'] = {'runtime': 'debug details'}
        self.run['run']['producer'] = {'engine': {'mvid': '12345678-1234-1234-1234-123456789abc', 'path': '/home/private/engine.dll'}}
        self.run['cases'][0]['recipeApplied'].update(complete=False, trace=['runtime'], animation={'clip': 0, 'recording': {'frameCount': 300, 'sampleTimes': list(range(300))}})
        data = self.export()
        self.assertNotIn('error', data['run'])
        self.assertNotIn('observation', data['cases'][0]['render'])
        self.assertNotIn('pid', data['run'])
        self.assertNotIn('path', data['run']['producer']['engine'])
        self.assertNotIn('trace', data['cases'][0]['recipeApplied'])
        self.assertIs(data['cases'][0]['recipeApplied']['complete'], False)
        self.assertNotIn('sampleTimes', data['cases'][0]['recipeApplied']['animation']['recording'])

    def test_recipe_and_dependency_execution_identity_are_required(self):
        self.captured()
        for mutation in [lambda: self.run['cases'][0].update(recipeId='wrong'), lambda: self.run['cases'][0].update(recipeApplied=None), lambda: self.run['cases'][0]['input'].update(dependenciesVerified=False), lambda: self.run['cases'][0]['input']['dependencies'].clear()]:
            original = copy.deepcopy(self.run)
            mutation()
            with self.assertRaises(ValueError):
                self.export()
            self.run = original

    def test_transaction_failure_restores_media_and_data(self):
        self.captured()
        data = self.export()
        before = (self.output / 'data.json').read_bytes()
        original = Path.replace
        def failing_replace(path, target):
            if path.name == 'data.json':
                raise OSError('test disk failure')
            return original(path, target)
        with patch.object(Path, 'replace', failing_replace):
            with self.assertRaises(OSError):
                self.export()
        self.assertEqual(before, (self.output / 'data.json').read_bytes())
        self.assertTrue((self.output / data['cases'][0]['capture']['url']).exists())


if __name__ == '__main__':
    unittest.main()
