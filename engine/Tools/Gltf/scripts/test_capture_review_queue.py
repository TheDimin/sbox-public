import copy
import unittest
from generate_capture_review_queue import build_queue, markdown, canonical, digest, load_review_baselines, validate_reviews


def fixture():
    producer = {name: {'mvid': 'old', 'diskSha256': 'old-sha'} for name in ('engine', 'gltf', 'renderer')}
    guide = {'checks': ['Inspect the rotating triangle.'], 'limits': 'A still cannot prove motion.'}
    base = {'corpusPin': 'pin', 'run': {'id': 'old-run', 'status': 'complete', 'producer': producer},
            'models': [{'id': 'Triangle', 'reviewGuidance': guide}], 'cases': []}
    raw = []
    for variant in ('glTF', 'glTF-Binary'):
        case_id = 'Triangle/' + variant
        base['cases'].append({'id': case_id, 'modelId': 'Triangle', 'variant': variant,
                              'comparisonFingerprint': variant + '-old', 'capture': {'kind': 'image', 'sha256': variant + '-pixels'},
                              'load': {'status': 'passed'}, 'render': {'status': 'passed'}})
        raw.append({'id': case_id, 'capture': {'kind': 'image', 'sha256': variant + '-pixels'},
                    'load': {'status': 'passed'}, 'render': {'status': 'passed', 'observation': {'nativeCaptureSubmitted': True}},
                    'producer': producer, 'cleanupConfirmed': True})
    current = {'corpusPin': 'pin', 'run': copy.deepcopy(base['run']), 'cases': raw}
    website = copy.deepcopy(base)
    feedback = {'corpusPin': 'pin', 'decisions': [{'caseId': 'Triangle/glTF', 'fingerprint': 'glTF-old', 'status': 'rejected',
                                                 'reviewer': 'user', 'notes': 'Motion is missing.', 'reviewedAt': '2026-11-06T07:00:00Z'}]}
    return current, website, base, feedback


def capture_batch(values):
    current, website, baseline, feedback = values
    current['run']['id'] = website['run']['id'] = 'new-run'
    row = current['cases'][0]
    return {'schemaVersion': 1, 'runId': 'new-run', 'cases': [{
        'caseId': row['id'], 'startedAt': '2026-11-06T08:00:00Z', 'completedAt': '2026-11-06T08:01:00Z',
        'resultSha256': digest(canonical(row)), 'captureSha256': row['capture']['sha256'],
        'producerSha256': digest(canonical(row['producer']))}]}


class QueueTests(unittest.TestCase):
    def queue(self, values, batch=None, loader=canonical):
        return build_queue(*values, loader, batch)

    def test_old_alternate_format_is_not_a_fix(self):
        queue = self.queue(fixture())
        self.assertEqual(queue['summary']['fresh-ready-for-review'], 0)
        self.assertEqual(queue['summary']['awaiting-regeneration'], 2)

    def test_visual_context_change_with_actual_receipt_is_fresh(self):
        values = fixture()
        values[0]['cases'][0]['producer'] = {'engine': {'mvid': 'new', 'diskSha256': 'new-sha'}}
        values[1]['cases'][0]['comparisonFingerprint'] = 'new-fingerprint'
        batch = capture_batch(values)
        queue = self.queue(values, batch)
        self.assertEqual(queue['summary']['fresh-ready-for-review'], 1)
        self.assertFalse(queue['queue'][0]['freshness']['captureBytesChanged'])
        self.assertIn('capture bytes are unchanged', markdown(queue))

    def test_actual_recapture_with_identical_fingerprint_keeps_rejection(self):
        values = fixture(); batch = capture_batch(values)
        queue = self.queue(values, batch)
        self.assertEqual(queue['summary']['fresh-ready-for-review'], 0)
        self.assertTrue(queue['queue'][0]['savedReviewStillMatches'])
        self.assertEqual(queue['queue'][0]['savedReview']['status'], 'rejected')
        self.assertIn('Saved reviews are unchanged', markdown(queue))

    def test_new_producer_same_visual_identity_does_not_requeue(self):
        values=fixture();values[0]['cases'][0]['producer']={'engine':{'mvid':'new-build'}}
        batch=capture_batch(values);queue=self.queue(values,batch)
        self.assertEqual(queue['summary']['fresh-ready-for-review'],0)
        self.assertTrue(queue['queue'][0]['freshness']['producerChanged'])
        self.assertFalse(queue['queue'][0]['freshness']['fingerprintChanged'])

    def test_new_producer_and_fingerprint_without_execution_are_not_fresh(self):
        values = fixture()
        values[0]['cases'][0]['producer'] = {'engine': {'mvid': 'relabeled'}}
        values[1]['cases'][0]['comparisonFingerprint'] = 'relabeled'
        self.assertEqual(self.queue(values)['summary']['fresh-ready-for-review'], 0)

    def test_changed_pixels_without_native_receipt_are_not_fresh(self):
        values = fixture()
        for data in values[:2]: data['cases'][0]['capture']['sha256'] = 'changed-pixels'
        values[1]['cases'][0]['comparisonFingerprint'] = 'changed'
        self.assertEqual(self.queue(values)['summary']['fresh-ready-for-review'], 0)

    def test_relabelled_old_receipt_fails(self):
        values = fixture(); batch = capture_batch(values)
        values[0]['cases'][0]['producer'] = {'engine': {'mvid': 'relabeled'}}
        with self.assertRaisesRegex(ValueError, 'receipt hash differs'): self.queue(values, batch)

    def test_pre_feedback_execution_is_not_fresh(self):
        values = fixture(); batch = capture_batch(values)
        batch['cases'][0]['startedAt'] = '2026-11-06T06:00:00Z'
        self.assertEqual(self.queue(values, batch)['summary']['fresh-ready-for-review'], 0)

    def test_stale_export_or_baseline_is_rejected(self):
        for mutation in ('capture', 'status', 'baseline'):
            values = fixture()
            if mutation == 'capture': values[1]['cases'][0]['capture']['sha256'] = 'stale'
            elif mutation == 'status': values[1]['cases'][0]['load']['status'] = 'failed'
            else: values[2]['cases'][0]['comparisonFingerprint'] = 'wrong-baseline'
            with self.subTest(mutation=mutation), self.assertRaises(ValueError): self.queue(values)

    def test_failure_separate_from_regeneration_and_approvals_untouched(self):
        values = fixture()
        values[3]['decisions'][0]['status'] = 'approved'
        for data in values[:2]:
            row = data['cases'][1]; row['capture'] = None
            row['load'] = row['render'] = {'status': 'failed', 'error': 'Load failed'}
        before = copy.deepcopy(values)
        first = self.queue(values)
        self.assertEqual(first['summary']['approved-current'], 1)
        self.assertEqual(first['summary']['failed'], 1)
        self.assertEqual(first, self.queue(values))
        self.assertEqual(values, before)

    def test_active_run_and_missing_cases_rejected(self):
        values = fixture(); values[0]['run']['status'] = values[1]['run']['status'] = 'running'
        with self.assertRaisesRegex(ValueError, 'terminal'): self.queue(values)
        values = fixture(); values[0]['cases'].pop()
        with self.assertRaisesRegex(ValueError, 'IDs differ'): self.queue(values)

    def test_fresh_known_issue_is_held_without_creating_a_review(self):
        values = fixture(); batch = capture_batch(values)
        batch['cases'][0]['reviewHoldReason'] = 'Renders solid black; vertex-colour material path remains under investigation.'
        values[1]['cases'][0]['comparisonFingerprint'] = 'new-image-fingerprint'
        before = copy.deepcopy(values)
        queue = self.queue(values, batch)
        self.assertEqual(queue['summary']['fresh-held-known-issues'], 1)
        self.assertEqual(queue['summary']['fresh-ready-for-review'], 0)
        self.assertTrue(queue['queue'][0]['freshness']['nativeRecaptureVerified'])
        self.assertFalse(queue['queue'][0]['savedReviewStillMatches'])
        self.assertEqual(values, before)
        self.assertIn('Hold for investigation: Renders solid black', markdown(queue))

    def test_retained_new_capture_keeps_actual_origin_batch(self):
        values = fixture(); batch = capture_batch(values)
        values[1]['cases'][0]['comparisonFingerprint']='changed-visual-context'
        batch['timingScope'] = 'batch-boundaries'
        batch['cases'][0]['sourceRunId'] = 'first-post-feedback-run'
        queue = self.queue(values, batch)
        self.assertEqual(queue['summary']['fresh-ready-for-review'], 1)
        self.assertEqual(queue['queue'][0]['freshness']['execution']['sourceRunId'], 'first-post-feedback-run')
        self.assertIn('not per-case stopwatch', markdown(queue))
        batch['cases'][0]['sourceRunId'] = 'old-run'
        with self.assertRaisesRegex(ValueError, 'feedback-era run'): self.queue(values, batch)

    def test_known_native_failure_remains_waiting(self):
        values = fixture(); batch = capture_batch(values)
        for data in values[:2]:
            row = data['cases'][1]; row['capture'] = None
            row['load'] = row['render'] = {'status': 'blocked'}
        batch['knownFailures'] = [{'caseId': 'Triangle/glTF-Binary', 'reason': 'Native SIGSEGV; excluded from recovery.'}]
        queue = self.queue(values, batch)
        self.assertEqual(queue['summary']['blocked-or-not-run'], 1)
        self.assertIn('SIGSEGV', queue['queue'][1]['knownFailureReason'])
        self.assertEqual(queue['queue'][1]['group'], 'blocked-or-not-run')

    def mixed_reviews(self):
        values = fixture(); current, website, oldest, feedback = values
        old_commit, new_commit = 'a' * 40, 'b' * 40
        latest = copy.deepcopy(oldest); latest['run']['id'] = 'latest-reviewed-run'
        for row in latest['cases']:
            row['comparisonFingerprint'] += '-new'
            row['capture']['sha256'] += '-new'
        website.update(copy.deepcopy(latest))
        current['run']['id'] = latest['run']['id']
        for raw, row in zip(current['cases'], latest['cases']): raw['capture'] = copy.deepcopy(row['capture'])
        feedback['decisions'][0]['siteCommit'] = old_commit
        feedback['decisions'].append({'caseId': latest['cases'][1]['id'], 'fingerprint': latest['cases'][1]['comparisonFingerprint'],
            'siteCommit': new_commit, 'status': 'approved', 'reviewer': 'user', 'notes': 'Looks right now.', 'reviewedAt': '2026-11-06T09:00:00Z'})
        feedback['latestReviewSiteCommit'] = new_commit
        return current, website, latest, feedback, {old_commit: oldest, new_commit: latest}

    def test_mixed_review_generations_use_each_exact_source(self):
        current, website, baseline, feedback, sources = self.mixed_reviews()
        before = copy.deepcopy(feedback)
        queue = build_queue(current, website, baseline, feedback, canonical, reviewed_baselines=sources)
        self.assertEqual(queue['summary']['approved-current'], 1)
        self.assertTrue(queue['queue'][0]['freshness']['captureBytesChanged'])
        self.assertEqual(queue['queue'][0]['freshness']['comparedAgainstReviewSiteCommit'], 'a' * 40)
        self.assertFalse(queue['queue'][1]['freshness']['captureBytesChanged'])
        self.assertEqual(feedback, before)
        website['cases'][1]['comparisonFingerprint'] = 'changed-after-approval'
        queue = build_queue(current, website, baseline, feedback, canonical, reviewed_baselines=sources)
        self.assertEqual(queue['summary']['approved-current'], 0)
        self.assertFalse(queue['queue'][1]['savedReviewStillMatches'])

    def test_mixed_review_forgery_and_unknown_commit_rejected(self):
        for change in ('fingerprint', 'siteCommit'):
            current, website, baseline, feedback, sources = self.mixed_reviews()
            feedback['decisions'][0][change] = 'forged' if change == 'fingerprint' else 'c' * 40
            with self.subTest(change=change), self.assertRaises(ValueError):
                build_queue(current, website, baseline, feedback, canonical, reviewed_baselines=sources)

    def test_baseline_loader_defaults_latest_and_reads_each_commit_once(self):
        current, website, baseline, feedback, sources = self.mixed_reviews()
        calls = []
        def loader(commit):
            calls.append(commit)
            return canonical(sources[commit])
        chosen, document, loaded, hashes = load_review_baselines(feedback, loader=loader)
        self.assertEqual(chosen, 'b' * 40)
        self.assertEqual(document, baseline)
        self.assertEqual(sorted(calls), ['a' * 40, 'b' * 40])
        self.assertEqual(set(hashes), set(sources))
        feedback['decisions'][1].pop('siteCommit')
        validate_reviews(feedback, baseline, loaded)  # Legacy decision uses passed baseline.
        feedback['decisions'][0]['siteCommit'] = 'c' * 40
        with self.assertRaisesRegex(ValueError, 'Unknown or unavailable'):
            load_review_baselines(feedback, loader=loader)
        feedback['decisions'][0]['siteCommit'] = 'HEAD'
        with self.assertRaisesRegex(ValueError, 'exact Git commit'):
            load_review_baselines(feedback, loader=loader)

    def test_old_native_capture_is_not_fresh_after_latest_mixed_feedback(self):
        current, website, baseline, feedback, sources = self.mixed_reviews()
        values = (current, website, baseline, feedback)
        batch = capture_batch(values)  # 08:00 capture predates the newest 09:00 review.
        queue = build_queue(*values, canonical, batch, sources)
        self.assertEqual(queue['summary']['fresh-ready-for-review'], 0)
        self.assertEqual(queue['summary']['approved-current'], 1)

    def test_batch_cannot_claim_old_run_or_unreturned_capture(self):
        for mutation in ('run', 'missing', 'submitted', 'producer'):
            values = fixture(); batch = capture_batch(values)
            if mutation == 'run': batch['runId'] = 'old-run'
            elif mutation == 'missing': batch['cases'][0]['caseId'] = 'Absent/glTF'
            elif mutation == 'submitted': values[0]['cases'][0]['render']['observation']['nativeCaptureSubmitted'] = False
            else: batch['cases'][0]['producerSha256'] = 'wrong-producer'
            with self.subTest(mutation=mutation):
                if mutation == 'submitted': self.assertEqual(self.queue(values, batch)['summary']['fresh-ready-for-review'], 0)
                else:
                    with self.assertRaises(ValueError): self.queue(values, batch)


if __name__ == '__main__':
    unittest.main()
