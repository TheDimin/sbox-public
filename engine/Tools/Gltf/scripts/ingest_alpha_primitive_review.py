#!/usr/bin/env python3
"""Increment only root-inspected source comparisons; never rerun or reset the corpus."""
import argparse, copy, json, shutil
from collections import Counter
from urllib.parse import urlencode
from ingest_completed_model import ROOT, DIST, CORPUS, read, write, verified, sha
from export_current_comparison import public_setup, public_run
from visual_comparison_identity import comparison_identity, decoded_media, digest
from group_visual_reviews import group_identity


def ingest(handoff_path, handoff_hash, approved):
    owner = read(verified(handoff_path, handoff_hash))
    data = read(DIST / 'data.json'); before = copy.deepcopy(data)
    assert owner['corpus_revision'] == data['corpusPin']
    accepted = set(); receipts = []; raw_receipts = {}
    for name, proof in owner['receipts'].items():
        receipt = read(verified(proof['path'], proof['sha256']))
        raw_receipts[name] = receipt
        assert receipt['phase'] == 'complete' and not receipt['incomplete_cases'] and not receipt['cleanup_errors']
        for test in receipt['tests']:
            if test['passed']:
                accepted.add((test['suite'], test['method'], json.dumps(test.get('arguments', []), sort_keys=True)))
        receipts.append({'name': name, 'sha256': proof['sha256'], 'expected': receipt['expected_cases'], 'passed': receipt['passed_cases'], 'failed': receipt['failed_cases']})
    assert len(accepted) == owner['unique_acceptance_cases'] == 21
    assert sum(r['failed'] for r in receipts) == 3
    cases = {r['id']: r for r in data['cases']}; changed = []
    queue = read(DIST / 'review-queue.json'); old_queue = copy.deepcopy(queue)
    queued = {r['caseId']: r for r in queue['queue']}
    media = DIST / 'assets/completed'; media.mkdir(parents=True, exist_ok=True)
    for source in owner['captures']:
        case_id = source['caseId']
        if case_id not in approved:
            continue
        assert case_id in {'AlphaBlendModeTest/glTF', 'MeshPrimitiveModes/glTF'}
        assert approved[case_id] == source['capture']['sha256']
        if source.get('presentation_receipt'):
            proof = source['presentation_receipt']; actual = read(verified(proof['path'], proof['sha256']))
            assert actual['expected_cases'] == actual['passed_cases'] == 1 and not actual['failed_cases'] and not actual['incomplete_cases'] and not actual['cleanup_errors']
            measurements = [v for m in actual['measurements'] for v in (m['value'] if isinstance(m.get('value'), list) else [m.get('value')]) if isinstance(v, dict)]
            observed = next(v for v in measurements if v.get('phase') == 'source_front_presentation')
            assert observed['nativeCaptureSubmitted'] and not observed['callbackErrors']
            assert observed['finalHash'] == approved[case_id]
            image = verified(observed['finalPath'], approved[case_id]); applied = public_setup(source['recipeApplied'])
            input_proof = source['input']; producer = {k: {'mvid': actual[k + '_mvid']} for k in ['engine', 'gltf']}
            capture_receipt_hash = proof['sha256']
            settled = next(v for v in measurements if v.get('phase') == 'source_rtt_convergence')
            assert settled['settled'] and settled['finalHash'] == approved[case_id]
            assert all(r['changedPixels'] == r['maximumChannelDeltaCodes'] == r['meanAbsoluteDeltaCodes'] == 0 for r in settled['rows'][-4:])
            empty = [v for v in measurements if v.get('phase') == 'empty_rtt_convergence']
            assert len(empty) == 2 and empty[0]['Sha256'] == empty[1]['Sha256']
            assert all(v['settled'] and v['rows'][-1]['stablePairs'] == 4 for v in empty)
            exposure = [v for v in measurements if v.get('phase') == 'rtt_exposure_observation']
            assert len(exposure) == 20 and all(v['minimum'] == v['maximum'] == 1 and v['compensationEv'] == -.5 for v in exposure)
        else:
            proof = source['capture_receipt']; native = read(verified(proof['path'], proof['sha256']))
            assert native['id'] == case_id and native['load']['status'] == native['render']['status'] == 'passed'
            assert native['render']['observation']['nativeCaptureSubmitted'] and native['cleanupConfirmed'] and not native['cleanup_errors']
            assert native['capture']['sha256'] == approved[case_id]
            image = verified(native['capture']['path'], approved[case_id]); applied = public_setup(native['recipeApplied'])
            input_proof = native['input']; producer = native['producer']; capture_receipt_hash = proof['sha256']
        assert input_proof['sha256'] == cases[case_id]['sourceHash'] and input_proof['dependenciesVerified']
        verified(CORPUS / input_proof['path'], input_proof['sha256'])
        for dependency, expected in input_proof['dependencies'].items(): verified(CORPUS / dependency, expected)
        target = media / (approved[case_id] + '.png')
        pixels = decoded_media(image); reference = source['reference']; refpath = verified(CORPUS / reference['path'], reference['sha256'])
        assert pixels['frames'][0]['size'] == [reference['width'], reference['height']]
        assert cases[case_id]['reference']['sha256'] == reference['sha256']
        for item in owner['producer']['loaded']['modules']:
            name = {'Sandbox.Engine': 'engine', 'Sandbox.Gltf': 'gltf', 'Sandbox.SceneRenderer': 'renderer'}.get(item['name'])
            if name and name in producer: assert producer[name]['mvid'] == item['loadedMvid']
        limits = ['Primary image is source glTF only, rendered through public Model.Load and ordinary ModelRenderer at the upstream reference resolution without resampling.',
                  '21 unique bounded acceptance cases passed across four receipts; three original fixture failures remain recorded and their corrected cases passed. This is not one clean 21-case batch.',
                  'Explicit embedded-image fixtures do not establish automatic standalone compilation or packaging. Windows, performance, arbitrary intersecting transparency and complete corpus conformance remain untested.',
                  'Camera and lighting are inferred. User appearance approval is pending.']
        if case_id.startswith('MeshPrimitiveModes'):
            purpose = 'Points, line lists/loops/strips and triangle lists/strips/fans are visible. Filled triangles are lit gray rather than upstream white; raster and material appearance remain for review.'
            limits.append('Requested exposure was not retained as a verified lock. Filled triangles are lit gray rather than the upstream white reference.')
        else:
            purpose = 'Inspect OPAQUE, MASK cutoff and BLEND overlap, labels and depth behavior. Bounded functional checks do not establish exact reference appearance.'
            limits.append('Front framing, lighting and background are inferred and visibly differ from the pinned upstream image. A settled exposure setting does not establish reference calibration.')
        applied['limitations'] = applied.get('limitations', []) + limits
        row = copy.deepcopy(cases[case_id]); old = copy.deepcopy(row)
        row.update(capture={'url': str(target.relative_to(DIST)), 'sha256': approved[case_id], 'kind': 'image', 'frames': 1, 'duration': 0}, load={'status': 'passed', 'error': None}, render={'status': 'passed', 'error': None}, reason=None,
                   recipeApplied=applied, recipe={'camera': applied['camera'], 'presentation': applied.get('presentation'), 'matchStatus': 'needs-tuning', 'provenance': 'Actual public source capture; inferred reference setup.', 'limitations': limits},
                   referenceNotice=purpose + ' SOURCE ONLY; canonical functional proof is separate.', whatChanged=purpose,
                   updated=True, visualChanged=True, visualReview={'status': 'pending', 'reviewer': None}, compatibleReviewFingerprints=[], visualIdentityVersion=2)
        row['evidence'] = {'type': 'isolated-model-execution', 'sourceForm': case_id, 'derivedCompiled': False, 'mainCaptureSourceOnly': True, 'manifestSha256': handoff_hash, 'receiptSha256': capture_receipt_hash, 'producer': public_run({'producer': producer})['producer'], 'inputSha256': input_proof['sha256'], 'publicAcceptanceUniqueCases': 21, 'retainedOriginalFailures': 3, 'formProofReceipts': receipts, 'scope': purpose, 'limitations': limits}
        proof_name = 'modes' if case_id.startswith('MeshPrimitiveModes') else 'fixture-retry'
        values = [v for m in raw_receipts[proof_name]['measurements'] for v in m.get('value', []) if isinstance(v, dict) and v.get('compiled') is True]
        if proof_name == 'modes':
            proof_frame = next(f for v in values for f in v.get('frames', []) if f.get('label') == 'public_material_lit')
        else:
            proof_frame = next(v for v in values if v.get('Path') and v.get('Sha256'))
        proof_image = verified(proof_frame['Path'], proof_frame['Sha256']); proof_target = media / (proof_frame['Sha256'] + '.png')
        assert decoded_media(proof_image)['frames'][0]['size'] == [192, 192]
        shutil.copy2(proof_image, proof_target)
        row['relatedCaptureProof'] = {'url': str(proof_target.relative_to(DIST)), 'sha256': proof_frame['Sha256'], 'label': 'Compiled functional capture · 192px only ↗', 'notice': 'Separate actual 192×192 derived compiled functional capture. This is not a compiled capture at the primary source resolution or an appearance approval. Explicit fixture image embedding does not prove automatic packaging.'}
        row['evidence']['canonicalCompiledCaptureSha256'] = proof_frame['Sha256']
        row['comparisonFingerprint'] = comparison_identity(row, pixels, decoded_media(refpath)); row['reviewGroupFingerprint'] = group_identity(row, pixels, decoded_media(refpath)); row['provenanceFingerprint'] = digest(row['evidence'])
        assert row['comparisonFingerprint'] != old['comparisonFingerprint']
        data.setdefault('previousComparisons', []).append(old)
        queued[case_id] = {'caseId': case_id, 'modelId': row['modelId'], 'variant': row['variant'], 'group': 'fresh-ready-for-review', 'url': '/#' + urlencode({'model': row['modelId'], 'variant': case_id}), 'comparisonFingerprint': row['comparisonFingerprint'], 'reviewGroupFingerprint': row['reviewGroupFingerprint'], 'captureSha256': approved[case_id], 'captureKind': 'image', 'loadStatus': 'passed', 'renderStatus': 'passed', 'freshness': {'nativeRecaptureVerified': True, 'fingerprintChanged': True}, 'check': purpose, 'limitations': ' '.join(limits), 'source': row['sourceUrl'], 'evidence': row['evidence'], 'savedReviewStillMatches': False, 'savedReview': None, 'relatedFeedback': []}
        shutil.copy2(image, target); cases[case_id] = row; changed.append(case_id)
    assert set(changed) == set(approved)
    assert all(cases[r['id']] == r for r in before['cases'] if r['id'] not in changed)
    assert data['savedReviews'] == before['savedReviews']
    assert all(queued[r['caseId']] == r for r in old_queue['queue'] if r['caseId'] not in changed)
    assert data.get('previousComparisons', [])[:len(before.get('previousComparisons', []))] == before.get('previousComparisons', [])
    data['cases'] = list(cases.values()); isolated = [r for r in cases.values() if r.get('evidence', {}).get('type') == 'isolated-model-execution']
    data['evidenceCollection'].update(isolatedForms=len(isolated), isolatedModels=len({r['modelId'] for r in isolated}))
    data['summary'].update(captures=sum(bool(r.get('capture')) for r in cases.values()), variants=len(cases), load=dict(Counter(r['load']['status'] for r in cases.values())), render=dict(Counter(r['render']['status'] for r in cases.values())))
    queue['queue'] = list(queued.values()); queue['summary'] = dict(Counter(r['group'] for r in queued.values()))
    write(DIST / 'data.json', data); write(DIST / 'review-queue.json', queue)
    report = {'changedCaseIds': changed, 'otherRowsPreserved': len(before['cases']) - len(changed), 'totalRows': len(cases), 'captured': data['summary']['captures'], 'savedVerdictsUnchanged': True, 'priorHistoryUnchanged': True, 'newVariantRows': 0, 'uniqueAcceptanceCases': 21, 'originalFailuresRetained': 3, 'userApproval': 'pending', 'dataSha256': sha(DIST / 'data.json')}
    write(ROOT / 'Temp/gltf-validation/alpha-primitive-site-ingest.json', report); print(json.dumps(report))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(); parser.add_argument('handoff'); parser.add_argument('--sha256', required=True); parser.add_argument('--approved', required=True, help='JSON object of root-inspected case IDs and PNG SHA256s')
    args = parser.parse_args(); ingest(args.handoff, args.sha256, json.loads(args.approved))
