"""Bounded orchestration regressions; the CLI/editor are mocked here."""
import copy
import importlib.machinery
import importlib.util
import json
import struct
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parent
loader = importlib.machinery.SourceFileLoader('corpus_run', str(Path(__file__).resolve().parent / 'Run'))
spec = importlib.util.spec_from_loader(loader.name, loader)
run = importlib.util.module_from_spec(spec)
loader.exec_module(run)
import run_editor_tests as transport

STOCK_COMPILED = (
    'game/core/shaders/gltf_deform_cs.shader_c', 'game/core/shaders/complex.shader_c',
    'game/core/shaders/vertex_color.shader_c', 'game/core/shaders/depth_only.shader_c',
    'game/core/shaders/gltf_stock_sampler.shader_c',
    'game/core/shaders/tools_shading_complexity.shader_c', 'game/core/materials/default/white.vmat_c',
    'game/core/materials/default/default_ao_tga_7449568e.generated.vtex_c',
    'game/core/materials/default/default_bentnormal_tga_8334dba4.generated.vtex_c',
    'game/core/materials/default/white_vmat_g_tcolor_a0ce44b8.generated.vtex_c',
    'game/core/materials/default/white_vmat_g_tnormal_7a668049.generated.vtex_c',
    'game/core/materials/default/default_color_tga_29ae19a3.generated.vtex_c')


class RunTests(unittest.TestCase):
    @staticmethod
    def stock_proof():
        resources = {name: {'present': True, 'sha256': 'a' * 64, 'origin': 'loose-core'} for name in STOCK_COMPILED}
        for name, digest in run.AUDITED_SHADER_RESOURCES.items():
            resources[name]['sha256'] = digest
        resources.update({name: {'present': False, 'sha256': None, 'origin': 'unavailable'} for name in run.STOCK_SOURCE_RESOURCES})
        graph = {name: sorted(run.SHADER_DELEGATIONS.get(name, [])) for name in STOCK_COMPILED}
        return {'stockResourceProofVersion': 2, 'stockResources': resources, 'stockDependencies': graph, 'stockDependencyErrors': []}

    def test_stock_resource_only_edits_change_disk_producer_identity(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            for name in ['Sandbox.Engine.dll', 'Sandbox.Gltf.dll', 'Sandbox.SceneRenderer.dll']:
                path = root / 'game/bin/managed' / name
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(name.encode())
            for name in STOCK_COMPILED:
                path = root / name
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(b'original compiled resource')
            with patch.object(run, 'ROOT', root):
                before = run.disk_producer()
                for name in STOCK_COMPILED:
                    with self.subTest(resource=name):
                        path = root / name
                        path.write_bytes(b'changed compiled resource')
                        self.assertNotEqual(before, run.disk_producer())
                        path.write_bytes(b'original compiled resource')

    def test_legacy_without_stock_resource_proof_cannot_reuse_pixels(self):
        with tempfile.TemporaryDirectory() as temporary:
            previous, producer, corpus, entries, recipes, old_path = self.recipe_reuse_fixture(Path(temporary))
            producer.pop('stockResourceProofVersion', None)
            producer.pop('stockResources', None)
            rows = run.prepare_reuse_rows(previous, producer, corpus, entries, recipes, old_path)
            self.assertTrue(all(row['capture'] is None for row in rows))

    def test_previous_vertex_shader_parent_cannot_certify_rebuilt_captures(self):
        old_proof = self.stock_proof()
        old_proof['stockResources']['game/core/shaders/vertex_color.shader_c']['sha256'] = \
            'bded76cfc539630ec2c94bc75e0b0b57436f36c7fabd7b8a71aa885b44283bbf'
        self.assertFalse(run.complete_stock_resource_proof(old_proof))
        self.assertTrue(run.complete_stock_resource_proof(self.stock_proof()))
        with tempfile.TemporaryDirectory() as temporary:
            previous, producer, corpus, entries, recipes, old_path = self.recipe_reuse_fixture(Path(temporary))
            previous['run']['producer'].update(old_proof)
            for row in previous['cases']:
                row['stockResourceProof'] = copy.deepcopy(old_proof)
            self.assertFalse(run.same_render_producer(previous, producer))
            rows = run.prepare_reuse_rows(previous, producer, corpus, entries, recipes, old_path)
            self.assertTrue(all(row['capture'] is None for row in rows))

    def test_sampler_parent_and_delegations_are_required_for_capture_proof(self):
        sampler = 'game/core/shaders/gltf_stock_sampler.shader_c'
        old_proof = self.stock_proof()
        old_proof['stockResources'].pop(sampler, None)
        old_proof['stockDependencies'].pop(sampler, None)
        self.assertFalse(run.complete_stock_resource_proof(old_proof))
        current = self.stock_proof()
        self.assertTrue(run.complete_stock_resource_proof(current))
        missing_mode = copy.deepcopy(current)
        missing_mode['stockDependencies'][sampler] = []
        self.assertFalse(run.complete_stock_resource_proof(missing_mode))
        unknown_parent = copy.deepcopy(current)
        unknown_parent['stockResources'][sampler]['sha256'] = '0' * 64
        self.assertFalse(run.complete_stock_resource_proof(unknown_parent))

    @staticmethod
    def resource_bytes(dependencies=()):
        # Independent literal ResourceFileHeader + RERL relative pointers.
        block = bytearray(struct.pack('<iI', 8, len(dependencies)))
        block.extend(bytes(16 * len(dependencies)))
        for index, dependency in enumerate(dependencies):
            pointer = 8 + 16 * index + 8
            struct.pack_into('<i', block, pointer, len(block) - pointer)
            block.extend(dependency.encode() + b'\0')
        return struct.pack('<IHHII4siI', 28 + len(block), 12, 1, 8, 1, b'RERL', 8, len(block)) + block

    def stock_disk_fixture(self, directory):
        for name in ['Sandbox.Engine.dll', 'Sandbox.Gltf.dll', 'Sandbox.SceneRenderer.dll']:
            path = directory / 'game/bin/managed' / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(name.encode())
        for name in STOCK_COMPILED:
            path = directory / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(self.resource_bytes())

    def test_new_material_child_and_later_child_only_edit_cannot_reuse_pixels(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            self.stock_disk_fixture(root)
            shader_hashes = {name: run.sha(root / name) for name in STOCK_COMPILED if '/shaders/' in name}
            with patch.object(run, 'ROOT', root), patch.object(run, 'AUDITED_SHADER_RESOURCES', shader_hashes, create=True):
                before = run.disk_producer()
                material = root / 'game/core/materials/default/white.vmat_c'
                child = root / 'game/core/materials/default/new_child.vtex_c'
                child.write_bytes(self.resource_bytes())
                material.write_bytes(self.resource_bytes(['materials/default/new_child.vtex']))
                after = run.disk_producer()
                self.assertTrue(run.complete_stock_resource_proof(after))
                self.assertNotEqual(before, after)
                self.assertIn('game/core/materials/default/new_child.vtex_c', after['stockResources'])
                previous, _, corpus, entries, recipes, old_path = self.recipe_reuse_fixture(root)
                # A completed recapture has adopted the changed parent and child.
                previous['run']['producer'] = after
                for row in previous['cases']:
                    row['stockResourceProof'] = run.capture_stock_proof(after)
                    row['execution']['driverSha256'] = after['driverSha256']
                    for key, filename in [('engine', 'Sandbox.Engine.dll'), ('gltf', 'Sandbox.Gltf.dll'), ('renderer', 'Sandbox.SceneRenderer.dll')]:
                        row['producer'][key]['diskSha256'] = after['binaries'][filename]
                retained = run.prepare_reuse_rows(previous, after, corpus, entries, recipes, old_path)
                self.assertTrue(all(row['capture'] is not None for row in retained))
                child.write_bytes(self.resource_bytes() + b'changed streaming pixels')
                latest = run.disk_producer()
                self.assertTrue(run.complete_stock_resource_proof(latest))
                self.assertEqual(after['stockResources']['game/core/materials/default/white.vmat_c'],
                                 latest['stockResources']['game/core/materials/default/white.vmat_c'])
                self.assertNotEqual(after, latest)
                rows = run.prepare_reuse_rows(previous, latest, corpus, entries, recipes, old_path)
                self.assertTrue(all(row['capture'] is None for row in rows))

    def test_unknown_shader_topology_or_missing_child_refuses_reuse(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            self.stock_disk_fixture(root)
            shader_hashes = {name: run.sha(root / name) for name in STOCK_COMPILED if '/shaders/' in name}
            with patch.object(run, 'ROOT', root), patch.object(run, 'AUDITED_SHADER_RESOURCES', shader_hashes, create=True):
                self.assertTrue(run.complete_stock_resource_proof(run.disk_producer()))
                shader = root / 'game/core/shaders/vertex_color.shader_c'
                shader.write_bytes(self.resource_bytes() + b'unknown delegated mode')
                self.assertFalse(run.complete_stock_resource_proof(run.disk_producer()))
                shader.write_bytes(self.resource_bytes())
                (root / 'game/core/materials/default/white.vmat_c').write_bytes(
                    self.resource_bytes(['materials/default/missing.vtex']))
                self.assertFalse(run.complete_stock_resource_proof(run.disk_producer()))

    def test_dependency_links_and_malformed_names_are_not_certified(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            self.stock_disk_fixture(root)
            shader_hashes = {name: run.sha(root / name) for name in STOCK_COMPILED if '/shaders/' in name}
            with patch.object(run, 'ROOT', root), patch.object(run, 'AUDITED_SHADER_RESOURCES', shader_hashes):
                material = root / 'game/core/materials/default/white.vmat_c'
                child = root / 'game/core/materials/default/linked.vtex_c'
                target = root / 'actual.vtex_c'
                target.write_bytes(self.resource_bytes())
                child.symlink_to(target)
                material.write_bytes(self.resource_bytes(['materials/default/linked.vtex']))
                self.assertFalse(run.complete_stock_resource_proof(run.disk_producer()))
                for name in ['../outside.vtex', '/absolute.vtex', 'materials//child.vtex']:
                    material.write_bytes(self.resource_bytes([name]))
                    self.assertFalse(run.complete_stock_resource_proof(run.disk_producer()))
                material.write_bytes(self.resource_bytes()[:-1])
                self.assertFalse(run.complete_stock_resource_proof(run.disk_producer()))
                # Cyclic, valid RERL references are visited once and all bytes retained.
                child.unlink()
                child.write_bytes(self.resource_bytes(['materials/default/white.vmat']))
                material.write_bytes(self.resource_bytes(['materials/default/linked.vtex']))
                cyclic = run.disk_producer()
                self.assertTrue(run.complete_stock_resource_proof(cyclic))
                self.assertEqual(cyclic['stockDependencies']['game/core/materials/default/linked.vtex_c'],
                                 ['game/core/materials/default/white.vmat_c'])

    def test_optional_source_addition_and_removal_change_identity(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            self.stock_disk_fixture(root)
            shader_hashes = {name: run.sha(root / name) for name in STOCK_COMPILED if '/shaders/' in name}
            with patch.object(run, 'ROOT', root), patch.object(run, 'AUDITED_SHADER_RESOURCES', shader_hashes):
                missing = run.disk_producer()
                self.assertTrue(run.complete_stock_resource_proof(missing))
                source = root / 'game/core/shaders/complex.shader'
                source.write_bytes(b'newly available source')
                added = run.disk_producer()
                self.assertTrue(run.complete_stock_resource_proof(added))
                self.assertNotEqual(missing, added)
                source.unlink()
                self.assertEqual(missing, run.disk_producer())

    def recipe_reuse_fixture(self, directory):
        entries = [{'id': name, 'model': name.split('/')[0], 'path': 'Models/' + name + '/x.gltf', 'dependencies': []}
                   for name in ['Keep/glTF', 'Change/glTF']]
        corpus = {'files': {e['path']: {'sha256': '0' * 64} for e in entries}}
        recipes = {e['id']: {'id': e['id'], 'camera': {'position': [0, 0, 1]}} for e in entries}
        doc = {'schemaVersion': 1, 'corpusPin': run.PIN, 'cases': list(recipes.values())}
        old_path = directory / 'old-recipes.json'
        old_path.write_text(json.dumps(doc))
        producer = {'binaries': {'Sandbox.Engine.dll': 'engine', 'Sandbox.Gltf.dll': 'gltf', 'Sandbox.SceneRenderer.dll': 'renderer'},
                    'driverSha256': 'driver', 'deformShaderSha256': 'compute', 'runnerSha256': 'orchestrator', **self.stock_proof()}
        actual = {key: {'diskSha256': value, 'mvid': key, 'diskMvid': key}
                  for key, value in [('engine', 'engine'), ('gltf', 'gltf'), ('renderer', 'renderer')]}
        rows = run.initial_rows(corpus, entries)
        for row in rows:
            row.update(load={'status': 'passed'}, render={'status': 'passed'}, producer=copy.deepcopy(actual),
                       input={**row['input'], 'dependenciesVerified': True, 'dependencies': {}},
                       capture={'path': str(directory / (row['id'].split('/')[0] + '.png')), 'sha256': 'pixel-hash'},
                       cleanupConfirmed=True, execution={'driverSha256': 'driver', 'runnerSha256': 'original'},
                       stockResourceProof=run.capture_stock_proof(producer))
        previous = {'corpusPin': run.PIN, 'cases': rows, 'run': {'producer': producer, 'recipesSha256': run.sha(old_path)}}
        return previous, producer, corpus, entries, recipes, old_path

    def test_recipe_change_preserves_only_proven_unchanged_case(self):
        with tempfile.TemporaryDirectory() as temporary:
            previous, producer, corpus, entries, recipes, old_path = self.recipe_reuse_fixture(Path(temporary))
            recipes['Change/glTF']['camera']['position'] = [0, 0, 2]
            rows = run.prepare_reuse_rows(previous, producer, corpus, entries, recipes, old_path)
            self.assertEqual(rows[0]['capture'], previous['cases'][0]['capture'])
            self.assertEqual(rows[0]['producer'], previous['cases'][0]['producer'])
            self.assertEqual(rows[0]['execution'], previous['cases'][0]['execution'])
            self.assertEqual(rows[0]['recipeSnapshot'], recipes['Keep/glTF'])
            self.assertEqual(rows[1]['load']['status'], 'blocked')
            self.assertIsNone(rows[1]['capture'])
            self.assertNotIn('cleanupConfirmed', rows[1])

    def test_recipe_migration_requires_exact_old_document(self):
        with tempfile.TemporaryDirectory() as temporary:
            previous, producer, corpus, entries, recipes, old_path = self.recipe_reuse_fixture(Path(temporary))
            old_path.write_text(old_path.read_text() + ' ')
            with self.assertRaisesRegex(ValueError, 'previous recipe'):
                run.prepare_reuse_rows(previous, producer, corpus, entries, recipes, old_path)
            self.assertEqual(previous['cases'][0]['capture']['sha256'], 'pixel-hash')

    def test_canonical_recipe_difference_cannot_relabel_old_capture(self):
        with tempfile.TemporaryDirectory() as temporary:
            previous, producer, corpus, entries, recipes, old_path = self.recipe_reuse_fixture(Path(temporary))
            recipes['Change/glTF']['camera']['position'][-1] = 1.0
            rows = run.prepare_reuse_rows(previous, producer, corpus, entries, recipes, old_path)
            self.assertIsNone(rows[1]['capture'])
            self.assertEqual(rows[1]['load']['status'], 'blocked')

    def test_changed_driver_never_reuses_capture_or_cleanup_proof(self):
        with tempfile.TemporaryDirectory() as temporary:
            previous, producer, corpus, entries, recipes, old_path = self.recipe_reuse_fixture(Path(temporary))
            producer = {**producer, 'driverSha256': 'different-driver'}
            rows = run.prepare_reuse_rows(previous, producer, corpus, entries, recipes, old_path)
            self.assertTrue(all(row['capture'] is None and row['load']['status'] == 'blocked' for row in rows))
            self.assertTrue(all('cleanupConfirmed' not in row for row in rows))

    def test_existing_snapshots_do_not_need_legacy_document(self):
        with tempfile.TemporaryDirectory() as temporary:
            previous, producer, corpus, entries, recipes, old_path = self.recipe_reuse_fixture(Path(temporary))
            first = run.prepare_reuse_rows(previous, producer, corpus, entries, recipes, old_path)
            previous['cases'] = first
            old_path.unlink()
            again = run.prepare_reuse_rows(previous, producer, corpus, entries, recipes)
            self.assertEqual([row['capture'] for row in again], [row['capture'] for row in first])
            previous['cases'][0]['recipeSnapshot']['camera']['position'] = [1, 2, 3]
            with self.assertRaisesRegex(ValueError, 'previous recipe snapshot'):
                run.prepare_reuse_rows(previous, producer, corpus, entries, recipes)

    def test_resume_runs_changed_recipe_and_keeps_proven_unchanged_case(self):
        with tempfile.TemporaryDirectory() as temporary:
            previous, producer, corpus, entries, recipes, old_path = self.recipe_reuse_fixture(Path(temporary))
            previous['run']['pendingCase'] = None
            recipes['Change/glTF']['camera']['position'] = [0, 0, 2]
            proven = run.prepare_reuse_rows(previous, producer, corpus, entries, recipes, old_path)
            with patch.object(run, 'sha', return_value='new-full-document-hash'):
                rows, selected, _, _ = run.prepare_resume(previous, producer, corpus, entries, {e['id'] for e in entries}, proven)
            self.assertEqual(selected, {'Change/glTF'})
            self.assertEqual(rows[0]['capture'], previous['cases'][0]['capture'])

    def test_changed_source_or_actual_module_cannot_retain_old_pixels(self):
        with tempfile.TemporaryDirectory() as temporary:
            previous, producer, corpus, entries, recipes, old_path = self.recipe_reuse_fixture(Path(temporary))
            previous['cases'][0]['input']['dependenciesVerified'] = False
            previous['cases'][1]['producer']['engine']['diskSha256'] = 'other-engine'
            rows = run.prepare_reuse_rows(previous, producer, corpus, entries, recipes, old_path)
            self.assertTrue(all(row['capture'] is None and row['load']['status'] == 'blocked' for row in rows))

    def test_guard_refusal_preserves_current_matrix_snapshot_assets_and_website(self):
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            previous, producer, corpus, entries, recipes, old_path = self.recipe_reuse_fixture(directory)
            previous['run'].update(id='old-run', pendingCase=None, status='complete')
            current = directory / 'current'
            current.mkdir()
            (current / 'assets').mkdir()
            (current / 'website').mkdir()
            old_files = {current / 'run.json': json.dumps(previous).encode(),
                         current / 'recipes.json': old_path.read_bytes(),
                         current / 'assets/capture.png': b'old-pixels',
                         current / 'website/data.json': b'old-website'}
            for path, value in old_files.items():path.write_bytes(value)
            recipes['Change/glTF']['camera']['position'] = [0, 0, 2]
            doc = {'schemaVersion': 1, 'corpusPin': run.PIN, 'cases': list(recipes.values())}
            new_path = directory / 'new-recipes.json'
            new_path.write_text(json.dumps(doc))
            class Refusal:
                stdout = ['guard refused: insufficient swap\n']
                def wait(self):return 77
            with patch.object(run, 'CURRENT', current), patch.object(run, 'RECIPES', new_path), \
                 patch.object(run, 'inventory', return_value=(corpus, entries, doc, recipes)), \
                 patch.object(run, 'find_editor', return_value={'pid': 2, 'start': '3'}), \
                 patch.object(run, 'disk_producer', return_value=producer), \
                 patch.object(run.subprocess, 'Popen', return_value=Refusal()), patch.object(run, 'export') as exported:
                code = run.main(['--case', 'Change/glTF', '--previous-recipes', str(old_path)])
            self.assertEqual(code, 77)
            exported.assert_not_called()
            for path, value in old_files.items():self.assertEqual(path.read_bytes(), value)

    def test_new_dispatch_does_not_inherit_old_cleanup_confirmation(self):
        row = {'id': 'A/glTF', 'recipeId': 'A/glTF', 'input': {}, 'recipeSnapshot': {'id': 'A/glTF'},
               'recipeSha256': 'recipe', 'cleanupConfirmed': True, 'producer': {'old': True}, 'capture': {'path': 'old'}}
        run.clear_case_result(row)
        self.assertNotIn('cleanupConfirmed', row)
        self.assertNotIn('producer', row)
        self.assertEqual(row['load']['status'], 'not-run')
        self.assertIsNone(row['capture'])

    def test_looped_seeks_use_positive_presentation_timeline(self):
        recipe = {'animation': {'loop': True, 'recording': {'kind': 'apng', 'frameCount': 3,
                  'sampleTimes': [5.43, .03, .13], 'frameDurationsSeconds': [.1, .1, .1], 'duration': .3}}}
        run.validate_capture_plan(recipe)
        recipe['animation']['loop'] = False
        with self.assertRaises(ValueError):
            run.validate_capture_plan(recipe)
        recipe['animation']['loop'] = True
        recipe['animation']['recording']['frameDurationsSeconds'][1] = 0
        with self.assertRaises(ValueError):
            run.validate_capture_plan(recipe)

    def test_long_motion_has_aligned_bounded_deadline(self):
        recipe = {'animation': {'recording': {'frameCount': 1048}}}
        self.assertEqual(run.case_timeout(recipe, 180), 345)
        self.assertLessEqual(run.case_timeout(recipe, 800), 590)

    def test_selection_accounts_for_all_and_rejects_unknown(self):
        entries = [{'id': 'A/glTF', 'model': 'A'}, {'id': 'B/glTF', 'model': 'B'}]
        self.assertEqual(run.select(entries, [], []), {'A/glTF', 'B/glTF'})
        self.assertEqual(run.select(entries, ['B'], []), {'B/glTF'})
        with self.assertRaises(ValueError):
            run.select(entries, [], ['unknown'])

    def resume_fixture(self):
        entries = [{'id': name, 'model': name.split('/')[0], 'path': 'Models/' + name + '/x.gltf'}
                   for name in ['Done/glTF', 'BrainStem/glTF', 'BrainStem/glTF-Binary', 'Next/glTF']]
        corpus = {'files': {entry['path']: {'sha256': '0' * 64} for entry in entries}}
        producer = {'binaries': {'Engine': 'engine'}, 'driverSha256': 'driver',
                    'deformShaderSha256': 'shader', 'runnerSha256': 'new-orchestrator', **self.stock_proof()}
        rows = run.initial_rows(corpus, entries)
        rows[0].update(self.result('Done/glTF', render='passed'))
        rows[1].update(load={'status': 'failed', 'error': 'CLI closed'}, render={'status': 'failed', 'error': 'CLI closed'})
        for row in rows[2:]:
            row.update(load={'status': 'blocked', 'error': 'prior stop'}, render={'status': 'blocked', 'error': 'prior stop'})
        previous = {'corpusPin': run.PIN, 'cases': rows,
                    'run': {'recipesSha256': 'recipe', 'producer': {**producer, 'runnerSha256': 'old-orchestrator'},
                            'pendingCase': 'BrainStem/glTF', 'editor': {'pid': 123, 'start': '45'},
                            'interruption': {'kind': 'native-crash', 'pid': 123, 'start': '45',
                                             'bootId': Path('/proc/sys/kernel/random/boot_id').read_text().strip(),
                                             'caseId': 'BrainStem/glTF', 'reason': 'Observed native SIGSEGV'}}}
        return previous, producer, corpus, entries

    def test_resume_retains_completed_pixels_and_blocks_crashing_model(self):
        previous, producer, corpus, entries = self.resume_fixture()
        with tempfile.TemporaryDirectory() as temporary, patch.object(run, 'CURRENT', Path(temporary)), \
             patch.object(run, 'sha', return_value='recipe'), patch.object(run, 'original_process_absent', return_value=True):
            rows, selected, recovery, blocked = run.prepare_resume(previous, producer, corpus, entries, {e['id'] for e in entries})
        self.assertEqual(selected, {'Next/glTF'})
        self.assertEqual(rows[0]['producer'], previous['cases'][0]['producer'])
        self.assertEqual(rows[0]['execution']['runnerSha256'], 'old-orchestrator')
        self.assertEqual([row['load']['status'] for row in rows[1:3]], ['blocked', 'blocked'])
        self.assertNotIn('cleanupConfirmed', rows[1])
        self.assertEqual(recovery['kind'], 'native-crash')
        self.assertIn('BrainStem', blocked)

    def test_resume_refuses_live_original_process(self):
        previous, producer, corpus, entries = self.resume_fixture()
        with tempfile.TemporaryDirectory() as temporary, patch.object(run, 'CURRENT', Path(temporary)), \
             patch.object(run, 'sha', return_value='recipe'), patch.object(run, 'original_process_absent', return_value=False):
            with self.assertRaisesRegex(ValueError, 'still alive'):
                run.prepare_resume(previous, producer, corpus, entries, {e['id'] for e in entries})

    def test_resume_refuses_render_input_change(self):
        previous, producer, corpus, entries = self.resume_fixture()
        producer['driverSha256'] = 'different-driver'
        with patch.object(run, 'sha', return_value='recipe'):
            with self.assertRaisesRegex(ValueError, 'same corpus'):
                run.prepare_resume(previous, producer, corpus, entries, {e['id'] for e in entries})

    def test_absent_editor_is_observed_without_guessing_native_cause(self):
        previous, producer, corpus, entries = self.resume_fixture()
        previous['run'].pop('interruption')
        previous['run']['editor']['bootId'] = Path('/proc/sys/kernel/random/boot_id').read_text().strip()
        with tempfile.TemporaryDirectory() as temporary, patch.object(run, 'CURRENT', Path(temporary)), \
             patch.object(run, 'sha', return_value='recipe'), patch.object(run, 'original_process_absent', return_value=True):
            rows, selected, event, blocked = run.prepare_resume(previous, producer, corpus, entries, {e['id'] for e in entries})
        self.assertEqual(event['kind'], 'editor-exited')
        self.assertNotIn('signal', event)
        self.assertEqual(selected, {'Next/glTF'})
        self.assertNotIn('cleanupConfirmed', rows[1])

    def test_fresh_run_after_build_fix_can_retire_dead_pending_runtime(self):
        previous, producer, corpus, entries = self.resume_fixture()
        producer['binaries'] = {'Engine': 'fixed-build'}
        with patch.object(run, 'sha', return_value='recipe'), patch.object(run, 'original_process_absent', return_value=True):
            self.assertFalse(run.compatible_render_inputs(previous, producer, corpus, entries))
            self.assertEqual(run.verify_pending_retirement(previous)['kind'], 'native-crash')

    def test_fresh_run_refuses_live_or_malformed_boot_pending_runtime(self):
        previous, _, _, _ = self.resume_fixture()
        with patch.object(run, 'original_process_absent', return_value=False):
            with self.assertRaisesRegex(ValueError, 'still alive'):
                run.verify_pending_retirement(previous)
        previous['run']['interruption']['bootId'] = 'different-boot'
        with patch.object(run, 'original_process_absent', return_value=True):
            with self.assertRaisesRegex(ValueError, 'not verified'):
                run.verify_pending_retirement(previous)

    def test_previous_boot_event_retires_original_despite_reused_live_pid(self):
        previous, _, _, _ = self.resume_fixture()
        historical_boot = '8431a06c-8e7f-4e8c-97cd-e6c7360360a8'
        self.assertNotEqual(historical_boot, Path('/proc/sys/kernel/random/boot_id').read_text().strip())
        previous['run']['interruption']['bootId'] = historical_boot
        for editor_has_boot in (False, True):
            with self.subTest(editor_has_boot=editor_has_boot):
                if editor_has_boot:
                    previous['run']['editor']['bootId'] = historical_boot
                before = copy.deepcopy(previous)
                with patch.object(run, 'original_process_absent', return_value=False) as absent:
                    event = run.verify_pending_retirement(previous)
                absent.assert_not_called()
                self.assertIs(event, previous['run']['interruption'])
                self.assertEqual(event['bootId'], historical_boot)
                self.assertEqual(previous, before)

    def test_previous_boot_editor_without_event_has_unknown_exit_cause(self):
        previous, _, _, _ = self.resume_fixture()
        previous['run'].pop('interruption')
        previous['run']['editor']['bootId'] = '8431a06c-8e7f-4e8c-97cd-e6c7360360a8'
        with patch.object(run, 'original_process_absent', return_value=False) as absent:
            event = run.verify_pending_retirement(previous)
        absent.assert_not_called()
        self.assertEqual(event['bootId'], previous['run']['editor']['bootId'])
        self.assertEqual(event['kind'], 'editor-exited')
        self.assertNotIn('signal', event)

    def test_pending_retirement_refuses_missing_or_invalid_boot_identity(self):
        for boot in (None, '', 'different-boot', 123):
            with self.subTest(boot=boot):
                previous, _, _, _ = self.resume_fixture()
                previous['run']['interruption']['bootId'] = boot
                with patch.object(run, 'original_process_absent', return_value=True):
                    with self.assertRaises(ValueError):
                        run.verify_pending_retirement(previous)
        previous, _, _, _ = self.resume_fixture()
        previous['run'].pop('interruption')
        with patch.object(run, 'original_process_absent', return_value=True):
            with self.assertRaises(ValueError):
                run.verify_pending_retirement(previous)

    def test_pending_retirement_refuses_conflicting_editor_and_event_boots(self):
        previous, _, _, _ = self.resume_fixture()
        previous['run']['editor']['bootId'] = '8431a06c-8e7f-4e8c-97cd-e6c7360360a8'
        with patch.object(run, 'original_process_absent', return_value=True):
            with self.assertRaises(ValueError):
                run.verify_pending_retirement(previous)

    def test_previous_boot_event_still_requires_matching_case_pid_and_start(self):
        for field, mismatch in (('caseId', 'Other/glTF'), ('pid', 999), ('start', '46')):
            with self.subTest(field=field):
                previous, _, _, _ = self.resume_fixture()
                previous['run']['interruption'].update(
                    bootId='8431a06c-8e7f-4e8c-97cd-e6c7360360a8', **{field: mismatch})
                with patch.object(run, 'original_process_absent', return_value=True):
                    with self.assertRaises(ValueError):
                        run.verify_pending_retirement(previous)

    def test_same_boot_pending_process_uncertainty_is_not_retirement(self):
        previous, _, _, _ = self.resume_fixture()
        with patch.object(run, 'original_process_absent', side_effect=PermissionError('unreadable proc identity')):
            with self.assertRaises(PermissionError):
                run.verify_pending_retirement(previous)

    def exercise(self, results, verifier=None):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        directory = Path(temporary.name)
        current = directory / 'current'
        current.mkdir()
        entries = [{'id': 'A/glTF', 'model': 'A', 'path': 'Models/A/glTF/A.gltf'},
                   {'id': 'B/glTF', 'model': 'B', 'path': 'Models/B/glTF/B.gltf'}]
        corpus = {'files': {x['path']: {'sha256': '0' * 64} for x in entries}}
        state = {'run': {'status': 'running', 'producer': {'x': 1}, 'recipesSha256': 'recipe',
                         'attempted': 0, 'pendingCase': None}, 'cases': run.initial_rows(corpus, entries)}
        recipes = {x['id']: {'id': x['id'], 'animation': {'recording': {'kind': 'image'}}} for x in entries}
        calls = []
        class Session:
            pending = None
            def __init__(self, *args): pass
            def call(self, tool, arguments):
                calls.append(arguments)
                result = results[len(calls) - 1]
                if isinstance(result, Exception):
                    self.pending = 1
                    raise result
                return {'structuredContent': copy.deepcopy(result)}
            def close(self): return {'exit_code': 0}
        def verify(corpus, entry, root):
            if verifier:
                verifier(entry)
            return {'path': entry['path'], 'sha256': '0' * 64, 'dependenciesVerified': True, 'dependencies': {}}
        args = type('Args', (), {'timeout': 180, 'website_output': directory / 'site'})()
        patches = [patch.object(run, 'CURRENT', current), patch.object(run, 'read', return_value={'corpusRoot': str(directory)}),
                   patch.object(run, 'sha', return_value='recipe'), patch.object(run, 'disk_producer', return_value={'x': 1}),
                   patch.object(run, 'verify_inputs', side_effect=verify), patch.object(run, 'export', return_value=True),
                   patch.object(run.subprocess, 'check_output', return_value=run.PIN),
                   patch.object(transport, 'CliSession', Session),
                   patch.object(transport, 'discover', return_value=({'tools': {'run_csharp': {'name': 'run_csharp'}}}, directory))]
        for item in patches: item.start()
        self.addCleanup(lambda: [item.stop() for item in reversed(patches)])
        failure = None
        try:
            run.run_cases(args, state, corpus, entries, recipes, {'A/glTF', 'B/glTF'}, {'pid': 1, 'project': str(directory)})
        except Exception as error:
            failure = error
        return state, calls, failure

    @staticmethod
    def result(case, load='passed', render='failed', **extra):
        return {'id': case, 'load': {'status': load, 'error': None}, 'render': {'status': render, 'error': None},
                'capture': None, 'producer': {'actual': 'runtime'}, 'cleanupConfirmed': True, 'cleanup_errors': [], **extra}

    def test_ordinary_model_failure_does_not_abort_next_case(self):
        state, calls, error = self.exercise([self.result('A/glTF', 'failed'), self.result('B/glTF')])
        self.assertIsNone(error)
        self.assertEqual(len(calls), 2)
        self.assertEqual(state['run']['status'], 'complete')
        self.assertEqual(state['cases'][0]['load']['status'], 'failed')

    def test_transport_timeout_stops_remaining_cases(self):
        state, calls, error = self.exercise([TimeoutError('unknown runtime ownership')])
        self.assertIsInstance(error, TimeoutError)
        self.assertEqual(len(calls), 1)
        self.assertEqual(state['run']['status'], 'interrupted')
        self.assertEqual(state['cases'][1]['load']['status'], 'blocked')
        self.assertIsNone(state['cases'][0]['capture'])

    def test_human_play_stop_preserves_successful_load(self):
        state, calls, error = self.exercise([self.result('A/glTF', render='blocked', stopReason='human_play_active')])
        self.assertIsNotNone(error)
        self.assertEqual(len(calls), 1)
        self.assertEqual(state['cases'][0]['load']['status'], 'passed')
        self.assertEqual(state['cases'][0]['render']['status'], 'blocked')

    def test_cleanup_failure_preserves_load_and_stops(self):
        state, calls, error = self.exercise([self.result('A/glTF', cleanup_errors=['owned cleanup failed'], cleanupConfirmed=False)])
        self.assertIsNotNone(error)
        self.assertEqual(len(calls), 1)
        self.assertEqual(state['cases'][0]['load']['status'], 'passed')

    def test_input_failure_is_recorded_and_next_case_runs(self):
        def verify(entry):
            if entry['id'] == 'A/glTF': raise ValueError('source changed')
        state, calls, error = self.exercise([self.result('B/glTF')], verify)
        self.assertIsNone(error)
        self.assertEqual(len(calls), 1)
        self.assertEqual(state['cases'][0]['load']['status'], 'failed')
        self.assertEqual(state['run']['attempted'], 2)

    def test_streamed_apng_preserves_pixels_and_removes_raw_frames(self):
        from PIL import Image
        with tempfile.TemporaryDirectory() as temporary:
            output = Path(temporary)
            frames = []
            for i, color in enumerate(['red', 'blue']):
                path = output / f'frame{i}.png'
                Image.new('RGBA', (3, 2), color).save(path)
                frames.append({'path': str(path), 'sha256': run.sha(path)})
            result = {'render': {'status': 'passed'}, 'animationFrames': frames}
            recipe = {'animation': {'loop': True, 'recording': {'kind': 'apng', 'frameCount': 2,
                       'frameDurationsSeconds': [.05, .025], 'duration': .075}}}
            run.encode_motion(result, recipe, output)
            self.assertEqual(result['capture']['frames'], 2)
            self.assertTrue((output / 'capture.apng').is_file())
            self.assertTrue((output / 'poster.png').is_file())
            self.assertFalse(any(path.exists() for path in map(lambda f: Path(f['path']), frames)))
            self.assertNotIn('animationFrames', result)


if __name__ == '__main__':
    unittest.main()
