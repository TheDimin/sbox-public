#!/usr/bin/env python3
"""Import one prepared panorama and optionally run isolated IBL controls.

Coordinator holds shared native lease and >=2 GiB host admission for this entire
foreground command. It attaches to an existing, verified editor only. Results
are private /tmp diagnostics, never a published reference match.
"""
import argparse
import copy
import json
from pathlib import Path
import sys
import threading
import uuid

HERE = Path(__file__).resolve().parent
BASE = HERE.parents[2]
ROOT = BASE.parents[2]
sys.path.insert(0, str(BASE / 'scripts'))
from run_editor_tests import (CliSession, bounded, check_status, decoded, digest,
                              discover, require, save, tool_value)
from run_editor_probes import invoke_probe, result_value


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--pid', type=int, required=True)
    parser.add_argument('--project', type=Path, required=True)
    parser.add_argument('--discovery', type=Path, required=True)
    parser.add_argument('--discovery-sha256', required=True)
    parser.add_argument('--fixture', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--controls', action='store_true')
    parser.add_argument('--ibl-only', action='store_true', help='Only no-environment/positive captures; no exposure claim')
    parser.add_argument('--read-only-existing', action='store_true', help='Reject missing/stale assets rather than staging/compiling')
    parser.add_argument('--expected-compiled-sha256')
    parser.add_argument('--lighting-flags', action='store_true', help='Default/enabled/disabled flags using the isolated constant-cube candidate')
    parser.add_argument('--embedded-fixture', type=Path, help='Paired no-environment/derived authored-IBL private fixture capture')
    parser.add_argument('--vertical-fov', type=float, help='Explicit inferred camera crop, recorded separately from source pose')
    parser.add_argument('--model', choices=['EnvironmentTest', 'MetalRoughSpheres'], default='EnvironmentTest')
    parser.add_argument('--capture-driver', type=Path, help='Explicit isolated candidate script; default is the frozen shared driver')
    parser.add_argument('--admitted', action='store_true')
    args = parser.parse_args()
    require(args.admitted, 'Coordinator must hold guard admission and native lease')
    require(sum([args.controls, args.ibl_only, args.lighting_flags, args.embedded_fixture is not None]) <= 1, 'Select one diagnostic mode')
    require(not args.read_only_existing or args.expected_compiled_sha256, 'Read-only native preflight requires frozen compiled digest')
    require(not args.lighting_flags or args.capture_driver, 'Lighting flags require an explicit isolated diagnostic driver')
    require(not args.embedded_fixture or (args.capture_driver and args.model == 'EnvironmentTest'), 'Embedded fixture requires an isolated EnvironmentTest driver')
    output = args.output.resolve()
    require(output.is_relative_to('/tmp') and output != Path('/tmp') and not output.exists(), 'Require new /tmp output directory')
    project = args.project.resolve(strict=True)
    discovery_raw = bounded(args.discovery)
    require(digest(discovery_raw) == args.discovery_sha256, 'Reviewed discovery changed')
    approved = decoded(discovery_raw)
    require(approved.get('pid') == args.pid and approved.get('project') == str(project)
            and approved.get('mode') == 'discover' and approved.get('tests_dispatched') is False,
            'Discovery belongs to another editor')
    fixture = args.fixture.resolve(strict=True)
    output.mkdir(parents=True)
    session = CliSession(ROOT / 'game/bin/managed/sbox-cli', args.pid, output, 240)
    owned = []
    import_uncertain = False

    def put(path, data):
        require(not path.exists(), 'Refusing existing exchange file: ' + str(path))
        with path.open('xb') as stream:
            stream.write(data)
        owned.append((path, digest(data)))

    def remove(path):
        require(owned[-1][0] == path and digest(bounded(path, 8 * 1024 * 1024)) == owned[-1][1], 'Owned exchange input changed')
        path.unlink(); owned.pop()

    try:
        driver = args.capture_driver.resolve(strict=True) if args.capture_driver else BASE / 'editor' / 'editor_corpus.csx'
        sources = {name: bounded(path, 8 * 1024 * 1024) for name, path in {
            'import': HERE / 'editor_import_panorama.csx', 'capture': driver}.items()}
        save(output / 'inputs.json', {name: digest(data) for name, data in sources.items()})
        live, exchange = discover(session, args.pid, project, {}, output)
        require(live['tools'] == approved.get('tools'), 'Tool schemas changed from reviewed discovery')
        check_status(tool_value(session.call(live['tools']['editor_status'], {})), project)
        script = exchange / ('gltf_environment_import_' + uuid.uuid4().hex + '.csx')
        put(script, sources['import'])
        request_path = exchange / 'gltf-reference-environment.json'
        put(request_path, json.dumps({'fixture': str(fixture), 'readOnlyExisting': args.read_only_existing, 'expectedCompiledSha256': args.expected_compiled_sha256}).encode())
        import_output = output / 'import'; import_output.mkdir()
        import_uncertain = True
        value = result_value(invoke_probe(session, live['tools']['run_csharp'], script, import_output))
        save(output / 'import-result.json', value)
        require(isinstance(value, dict), 'Import returned no structured terminal state')
        if value.get('compileOutcome') in ('compiled', 'failed'):
            import_uncertain = False
        remove(request_path)
        require(value.get('passed') is True and value.get('compileOutcome') == 'compiled', 'Native import failed or did not reach terminal compiled state; inspect receipt and compiler before releasing admission')
        save(output / 'environment.json', value['environment'])
        if not (args.controls or args.ibl_only or args.lighting_flags or args.embedded_fixture):
            print(json.dumps({'output': str(output), 'environment': value['environment'], 'scope': value['scope']}, indent=2))
            return
        capture_script = exchange / ('gltf_environment_controls_' + uuid.uuid4().hex + '.csx')
        put(capture_script, sources['capture'])
        recipes = decoded(bounded(ROOT / 'research/gltf-validation/reference-recipes.json', 16 * 1024 * 1024))
        original = next(row for row in recipes['cases'] if row['id'] == args.model + '/glTF')
        corpus = Path(recipes['corpusRoot']).resolve(strict=True)
        source = corpus / original['input']['path']
        require(digest(bounded(source, 8 * 1024 * 1024)) == original['input']['sha256'], 'Pinned model source changed')
        results = []
        producer = None
        if args.embedded_fixture:
            labels = ['core_no_environment', 'derived_embedded_environment']
            embedded_fixture = decoded(bounded(args.embedded_fixture, 1024 * 1024))
        else:
            labels = ['no_environment', 'positive_environment'] if args.ibl_only else ['flags_default', 'flags_enabled', 'flags_disabled'] if args.lighting_flags else ['no_environment', 'black_environment', 'positive_environment', 'wrong_exposure']
        for label in labels:
            check_status(tool_value(session.call(live['tools']['editor_status'], {})), project)
            require(digest(bounded(driver, 8 * 1024 * 1024)) == digest(sources['capture']), 'Capture source changed')
            recipe = copy.deepcopy(original)
            if args.vertical_fov is not None:
                require(1 < args.vertical_fov < 170 and recipe['camera']['projection'] == 'perspective', 'Require a bounded perspective camera FOV')
                recipe['camera']['verticalFov'] = args.vertical_fov
                recipe['provenance']['camera'] = 'Authored pose, explicitly inferred vertical-FOV crop; not exact upstream camera'
            presentation = recipe['presentation']
            presentation['lights'] = []
            presentation['environment'] = copy.deepcopy(value['environment'])
            presentation['exposure'] = 1
            if args.embedded_fixture:
                presentation.pop('tonemapping', None)
                presentation['environment']['derivedEmbeddedFixture'] = str(args.embedded_fixture.resolve(strict=True))
                presentation['environment']['yawDegrees'] = embedded_fixture['nativeYawDegrees']
                if label == 'core_no_environment':
                    presentation['environment'] = {'type': 'engine-default'}
                recipe['provenance']['presentation'] = 'Derived private-scene source IBL fixture; public optional extension not consumed, native SH/roughness mapping differs'
            elif args.lighting_flags:
                presentation.pop('tonemapping', None)
                presentation['environment']['diagnosticConstantRadiance'] = True
                presentation['diagnosticLightingFlags'] = label.removeprefix('flags_')
            elif args.ibl_only:
                presentation.pop('tonemapping', None)
            else:
                presentation['tonemapping'] = {'mode': 'ACES', 'exposureCompensationEv': 2 if label == 'wrong_exposure' else 0}
            if label == 'no_environment':
                presentation['environment'] = {'type': 'engine-default'}
            elif label == 'black_environment':
                presentation['environment']['tint'] = [0, 0, 0, 1]
            request = {'schemaVersion': 1, 'id': original['id'], 'sourcePath': str(source), 'corpusRoot': str(corpus),
                       'inputPath': original['input']['path'], 'outputDir': str(output / label), 'recipe': recipe, 'timeoutSeconds': 120}
            case_path = exchange / 'gltf-run-case.json'
            put(case_path, json.dumps(request).encode())
            transport = output / (label + '-transport'); transport.mkdir()
            observed = result_value(invoke_probe(session, live['tools']['run_csharp'], capture_script, transport))
            save(output / (label + '-result.json'), observed)
            remove(case_path)
            require(observed.get('render', {}).get('status') == 'passed' and not observed.get('cleanup_errors'), label + ': capture or cleanup failed')
            if not (args.lighting_flags or args.embedded_fixture or args.ibl_only):
                require(observed['recipeApplied']['presentation']['exposureLocked'] is True, label + ': exposure settings were overwritten')
            if producer is None:
                producer = observed['producer']
            require(producer == observed['producer'], 'Loaded producer changed between controls')
            results.append({'label': label, 'result': str(output / (label + '-result.json')), 'capture': observed['capture']})
        from PIL import Image
        import numpy as np
        arrays = {row['label']: np.asarray(Image.open(row['capture']['path']).convert('RGB'), dtype=np.int16) for row in results}
        def changed(a, b):
            return int(np.any(np.abs(arrays[a] - arrays[b]) > 3, axis=2).sum())
        if args.ibl_only:
            difference = changed('no_environment', 'positive_environment')
            save(output / 'summary.json', {'iblResponse': difference > 1000, 'changedPixelsAbove3of255': difference, 'results': results,
                 'scope': 'Current exact existing compiled native cubemap, same public EnvironmentTest source/camera/materials, no direct lights. No exposure match, HDR float-readback, mip-filtering or source-extension claim.'})
            require(difference > 1000, 'Current environment did not materially change geometry pixels')
            print(json.dumps({'output': str(output), 'iblResponse': True, 'changedPixelsAbove3of255': difference}, indent=2))
            return
        if args.embedded_fixture:
            changes = changed('core_no_environment', 'derived_embedded_environment')
            save(output / 'summary.json', {'derivedEnvironmentChangedPixelsAbove3of255': changes, 'results': results,
                 'fixture': str(args.embedded_fixture.resolve(strict=True)), 'fixtureSha256': digest(bounded(args.embedded_fixture, 1024 * 1024)),
                 'scope': 'Whole source model with a private derived authored-cube fixture. GPU float samples checked. No actual EXT_lights_image_based loader consumption, authored SH application, exact roughness-LOD mapping, exposure match, or final visual approval.', 'visualApproval': 'pending'})
            require(changes > 1000, 'Derived source environment did not materially change model pixels')
            print(json.dumps({'output': str(output), 'derivedEnvironmentChangedPixelsAbove3of255': changes}, indent=2))
            return
        if args.lighting_flags:
            changes = {'defaultVsEnabled': changed('flags_default', 'flags_enabled'), 'defaultVsDisabled': changed('flags_default', 'flags_disabled'), 'enabledVsDisabled': changed('flags_enabled', 'flags_disabled')}
            flags = {row['label']: json.loads(Path(row['result']).read_text())['render']['observation'] for row in results}
            save(output / 'summary.json', {'flagBehaviorMeasured': True, 'pixelChangesAbove3of255': changes, 'observations': flags, 'results': results,
                 'scope': 'Same public source/camera/materials and GPU-verified constant-white all-face/all-mip cube. Only NeedsEnvironmentMap and NeedsLightProbe vary. IsEnvMap classification recorded but never set.', 'visualApproval': 'not-applicable-diagnostic'})
            print(json.dumps({'output': str(output), 'flagBehaviorMeasured': True, 'pixelChangesAbove3of255': changes}, indent=2))
            return
        changes = {'noVsBlack': changed('no_environment', 'black_environment'),
                   'blackVsPositive': changed('black_environment', 'positive_environment'),
                   'positiveVsWrongExposure': changed('positive_environment', 'wrong_exposure')}
        control_passed = changes['noVsBlack'] < 100 and changes['blackVsPositive'] > 1000 and changes['positiveVsWrongExposure'] > 1000
        save(output / 'summary.json', {'controlsPassed': control_passed, 'pixelChangesAbove3of255': changes, 'results': results,
             'scope': 'Same public source/camera/materials, no direct lights. Native IBL and exposure controls only; chosen environment is not a matched-reference or extension-support claim.', 'visualApproval': 'pending'})
        require(control_passed, 'Native IBL/exposure pixel controls failed; see summary')
        print(json.dumps({'output': str(output), 'controlsPassed': True, 'pixelChangesAbove3of255': changes}, indent=2))
    except Exception:
        if import_uncertain:
            # A timed-out call may still own native compiler work. Preserve the
            # foreground guard parent until the coordinator checks the editor.
            release = output / 'coordinator-confirmed-import-idle'
            waiter = threading.Event()
            while not release.exists():
                print('Holding admission: native import completion uncertain. Coordinator must inspect compiler state and create ' + str(release), file=sys.stderr, flush=True)
                waiter.wait(30)
        raise
    finally:
        for path, expected in reversed(owned):
            if path.exists() and digest(bounded(path, 8 * 1024 * 1024)) == expected:
                path.unlink()
        session.close()


if __name__ == '__main__':
    main()
