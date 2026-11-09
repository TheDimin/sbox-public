#!/usr/bin/env python3
"""Replace the two explicitly reviewed comparison rows after native capture checks."""

import copy
import shutil

from export_current_comparison import public_run, public_setup
from group_visual_reviews import group_identity
from ingest_completed_model import CORPUS, DIST, ROOT, read, sha, verified, write
from visual_comparison_identity import comparison_identity, decoded_media, digest


REVIEWED = {
    'MandarinOrange/glTF::diffuse-transmission-review': '93008dff7f61a2f316419af09eb7fb6c82bcc3009707829ccc63c3f7a2c6e88b',
    'NormalTangentTest/glTF-Binary::canonical-normal-review': '2e380de7f0b5695d1ce0af550f994222eaaeb325444be13db1c30b7267172a9d',
}


def replace_comparison(case_id, *, image, approved_sha, dimensions, recipe_applied,
                       notice, limitations, evidence, reference=None, overlay_note=None):
    """Ingest a caller-verified, root-inspected native capture into one existing row."""
    data = read(DIST / 'data.json')
    queue = read(DIST / 'review-queue.json')
    original = next(row for row in data['cases'] if row['id'] == case_id)
    assert original['comparisonFingerprint'] == REVIEWED[case_id]
    image = verified(image, approved_sha)
    capture_pixels = decoded_media(image)
    assert len(capture_pixels['frames']) == 1
    assert capture_pixels['frames'][0]['size'] == list(dimensions)
    old_pixels = decoded_media(DIST / original['capture']['url'])
    assert capture_pixels != old_pixels
    reference = copy.deepcopy(reference or original['reference'])
    reference_pixels = decoded_media(verified(CORPUS / reference['path'], reference['sha256']))
    applied = public_setup(recipe_applied)
    assert applied.get('complete')
    applied['limitations'] = limitations
    target = DIST / 'assets/completed' / f'{approved_sha}.png'

    row = copy.deepcopy(original)
    row.update(
        capture={'url': str(target.relative_to(DIST)), 'sha256': approved_sha, 'kind': 'image', 'frames': 1, 'duration': 0},
        reference=reference,
        recipeApplied=applied,
        recipe={'camera': applied.get('camera'), 'presentation': applied.get('presentation'),
                'provenance': 'Recorded native capture settings. Reference setup remains inferred.',
                'matchStatus': 'needs-tuning', 'limitations': limitations},
        referenceNotice=notice,
        whatChanged=notice,
        previousCapture={'url': original['capture']['url'], 'sha256': original['capture']['sha256'],
                         'fingerprint': original['comparisonFingerprint'], 'label': 'Earlier reviewed capture ↗',
                         'notice': 'The earlier image and decision remain in the comparison history.'},
        visualReview={'status': 'pending', 'reviewer': None},
        compatibleReviewFingerprints=[], updated=True, visualChanged=True,
    )
    if overlay_note:
        assert case_id.startswith('MandarinOrange/')
        assert reference_pixels['frames'][0]['size'] == list(dimensions)
        row['overlay'] = {'note': overlay_note}
        row['additionalReference'] = {**original['reference'], 'label': 'Upstream with/without transmission comparison ↗'}
    if case_id.startswith('NormalTangentTest/'):
        assert all(now > old for now, old in zip(dimensions, old_pixels['frames'][0]['size'], strict=True))
        row['sourceProof']['label'] = 'Earlier 512×483 source proof ↗'
        row['sourceProof']['notice'] = 'This source capture belongs to the earlier functional proof. Review the current high-resolution canonical render above.'

    row['evidence'] = {'type': original['evidence']['type'], 'sourceForm': original['evidence']['sourceForm'],
                       'derivedCompiled': original['evidence']['derivedCompiled'], **evidence, 'limitations': limitations}
    row['comparisonFingerprint'] = comparison_identity(row, capture_pixels, reference_pixels)
    row['reviewGroupFingerprint'] = group_identity(row, capture_pixels, reference_pixels)
    row['provenanceFingerprint'] = digest(row['evidence'])
    assert row['comparisonFingerprint'] != original['comparisonFingerprint']
    data['cases'] = [row if candidate['id'] == case_id else candidate for candidate in data['cases']]
    data.setdefault('previousComparisons', []).append(original)

    item = next(item for item in queue['queue'] if item['caseId'] == case_id)
    item.update(comparisonFingerprint=row['comparisonFingerprint'], reviewGroupFingerprint=row['reviewGroupFingerprint'],
                captureSha256=approved_sha, check=notice, limitations=' '.join(limitations), evidence=row['evidence'],
                freshness={'nativeRecaptureVerified': True, 'fingerprintChanged': True},
                savedReviewStillMatches=False, savedReview=None)
    shutil.copy2(image, target)
    assert sha(target) == approved_sha
    write(DIST / 'data.json', data)
    write(DIST / 'review-queue.json', queue)
    return row


def ingest_normal_hd(handoff_path, handoff_sha, approved_sha):
    case_id = 'NormalTangentTest/glTF-Binary::canonical-normal-review'
    handoff = read(verified(handoff_path, handoff_sha))
    receipt = read(verified(handoff['receipt'], handoff['receiptSha256']))
    original = next(row for row in read(DIST / 'data.json')['cases'] if row['id'] == case_id)
    assert receipt['id'] == 'NormalTangentTest/canonical-compiled'
    assert receipt['input']['path'].endswith('.glb_c') and receipt['input']['sha256'] == original['evidence']['inputSha256']
    verified(receipt['input']['sourcePath'], receipt['input']['sha256'])
    assert receipt['load']['status'] == receipt['render']['status'] == 'passed'
    assert receipt['cleanupConfirmed'] and not receipt['cleanup_errors']
    observation = receipt['render']['observation']
    assert observation['nativeCaptureSubmitted'] and observation['renderer'] == 'ModelRenderer' and not observation['callbackErrors']
    assert receipt['capture'] == handoff['capture'] and receipt['capture']['sha256'] == approved_sha
    assert original['reference']['sha256'] == handoff['referenceSha256']
    verified(handoff['recipePath'], handoff['freeze']['request'])
    dimensions = handoff['dimensions']
    assert dimensions == [3 * value for value in handoff['referenceDimensions']]
    applied = copy.deepcopy(receipt['recipeApplied'])
    assert [applied['camera']['width'], applied['camera']['height']] == dimensions
    samples = applied['presentation'].pop('nativeConvergenceSamples')
    assert len(samples) == 7 and all(sample['nativeTonemapEnabled'] for sample in samples)
    for sample in samples:
        verified(sample['path'], sample['sha256'])
        assert [sample['width'], sample['height']] == dimensions
        exposure = sample['exposureAfterRender']
        assert exposure['minimum'] == exposure['maximum'] == 1 and exposure['compensation'] == 0
    assert samples[0]['sha256'] != approved_sha and all(sample['sha256'] == approved_sha for sample in samples[1:])
    prior = original['evidence']
    notice = 'New native 1380×1302 canonical capture. Inspect the paired mapped cells and arrows at full resolution. The metallic reflection horizon and colors still differ from the reference.'
    limitations = [
        'The canonical compiled view is the review target. The linked 512×483 source image belongs to the earlier functional proof.',
        'The chosen Field HDRI, inferred camera and fixed ACES exposure differ from the unknown upstream setup.',
        'Six settled native captures were byte-identical after one warm-up. The earlier 12-case functional gate belongs to its recorded producer.',
    ]
    evidence = {
        'manifestSha256': handoff_sha, 'receiptSha256': handoff['receiptSha256'],
        'producer': public_run({'producer': receipt['producer']})['producer'],
        'inputSha256': receipt['input']['sha256'], 'canonicalCompiled': True,
        'canonicalCaptureSha256': approved_sha, 'nativeCaptureDimensions': dimensions,
        'convergence': {'nativeRenders': 7, 'equalSettledPairs': 5, 'fixedExposure': 1, 'exposureCompensationEv': 0},
        'historicalFunctionalProof': {k: prior[k] for k in ('postfixGateReceiptSha256', 'postfixCasesPassed', 'producer', 'sourceReceiptSha256', 'sourceCaptureSha256')},
        'scope': 'Public canonical Model.Load and ModelRenderer at 1380×1302. Review legibility and recorded appearance.',
    }
    return replace_comparison(case_id, image=receipt['capture']['path'], approved_sha=approved_sha,
                              dimensions=dimensions, recipe_applied=applied, notice=notice,
                              limitations=limitations, evidence=evidence)


def ingest_mandarin_overlay(handoff_path, handoff_sha, approved_sha):
    handoff = read(verified(handoff_path, handoff_sha))
    case_id = 'MandarinOrange/glTF::diffuse-transmission-review'
    assert handoff['case_id'] == case_id and handoff['previous_fingerprint'] == REVIEWED[case_id]
    original = next(row for row in read(DIST / 'data.json')['cases'] if row['id'] == case_id)
    assert original['capture']['sha256'] == handoff['previous_capture_sha256']
    proof = handoff['native_receipt']
    receipt = read(verified(proof['path'], proof['sha256']))
    assert receipt['phase'] == 'complete' and receipt['expected_cases'] == receipt['passed_cases'] == 1
    assert not receipt['failed_cases'] and not receipt['incomplete_cases'] and not receipt['cleanup_errors']
    for key in ('engine_mvid', 'gltf_mvid', 'request_sha256'):
        assert receipt[key] == proof[key]
    measurements = receipt['measurements'][0]['value']
    observed = next(value for value in measurements if value['phase'] == 'source_hdri_sky_presentation')
    assert observed == handoff['presentation'] and observed['sha256'] == handoff['capture']['sha256'] == approved_sha
    assert observed['source_sha256'] == original['evidence']['inputSha256']
    assert observed['exposure_min'] == observed['exposure_max'] == 2.5 and observed['tone_mode'] == 'ACES'
    controls = [value for value in measurements if value['phase'] == 'actual_rtt_convergence']
    assert [len(value['rows']) for value in controls] == [8, 5, 5, 5]
    for value in controls:
        verified(value['path'], value['sha256'])
        assert value['native_tonemap_enabled'] and value['rows'][-1]['max_channel_delta_codes'] == 0
    dimensions = [observed['width'], observed['height']]
    upstream = handoff['reference']
    assert dimensions == upstream['dims'] == handoff['capture']['dims'] == [1151, 1309]
    reference_path = verified(upstream['path'], upstream['sha256']).relative_to(CORPUS)
    reference = {'path': str(reference_path), 'url': upstream['url'], 'sha256': upstream['sha256'],
                 'kind': 'image', 'animated': False, 'width': dimensions[0], 'height': dimensions[1]}
    provenance = handoff['metrics_provenance']
    scores = read(verified(provenance['path'], provenance['sha256']))
    assert scores['receipt_sha256'] == proof['sha256'] and scores['reference_sha256'] == reference['sha256']
    metrics = handoff['metrics']
    assert metrics['formula'] == scores['formula'] and {k: v for k, v in metrics.items() if k != 'formula'} == scores['profiles'][0]
    assert metrics['display_luma']['similarity'] >= .8 and metrics['capture_sha256'] == approved_sha
    historical = handoff['existing_functional_proof']
    historical_receipt = read(verified(historical['path'], historical['sha256']))
    assert historical_receipt['passed_cases'] == historical_receipt['expected_cases'] == historical['passed_cases'] == 3
    camera = {key: observed[key] for key in ('camera_position', 'camera_target', 'camera_up', 'projection', 'vertical_fov', 'near', 'far', 'width', 'height')}
    presentation = {key: observed[key] for key in ('environment_texture', 'environment_intensity', 'environment_yaw', 'illumination', 'tone_mode', 'exposure_min', 'exposure_max', 'exposure_compensation')}
    applied = {'complete': True, 'camera': camera, 'presentation': presentation}
    similarity = f"{metrics['display_luma']['similarity']:.2%} displayed luma similarity and {metrics['linear_luminance']['similarity']:.2%} after sRGB linearization."
    notice = f'New aligned 1151×1309 source render with fitted camera and fixed ACES exposure. {similarity} Use the overlay to compare the same-size original images.'
    overlay_note = 'The full-size images share the same canvas and use inferred camera alignment. The render and upstream viewer use different lighting and backgrounds.'
    evidence = {
        'manifestSha256': handoff_sha, 'receiptSha256': proof['sha256'], 'metricsSha256': provenance['sha256'],
        'producer': {key: receipt[key] for key in ('engine_mvid', 'gltf_mvid')},
        'inputSha256': observed['source_sha256'], 'mainCaptureSourceOnly': True,
        'selectedScene': observed['selected_scene'], 'authoredDefaultScene': observed['authored_default_scene'],
        'referenceTarget': upstream['scope'], 'brightnessComparison': metrics,
        'convergence': {'nativeRendersByState': [len(value['rows']) for value in controls], 'finalPairMaxRgbCodeDelta': 0,
                        'fixedExposure': 2.5, 'nativeTonemapActive': True, 'iblChangedForegroundPixels': observed['actual_ibl_changed_pixels']},
        'historicalFunctionalProof': {'receiptSha256': historical['sha256'], 'passed': 3, 'scope': historical['scope']},
        'scope': 'Actual public source render with original materials, aligned presentation and matched-foreground brightness measurement.',
    }
    return replace_comparison(case_id, image=ROOT / handoff['capture']['path'], approved_sha=approved_sha,
                              dimensions=dimensions, recipe_applied=applied, notice=notice,
                              limitations=handoff['limitations'], evidence=evidence, reference=reference,
                              overlay_note=overlay_note)
