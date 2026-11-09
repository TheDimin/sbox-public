#!/usr/bin/env python3
"""Read-only result joins; write only the two requested review queue outputs.

Freshness requires a completed native capture execution, not a new label, recipe,
producer field or alternate format. The feedback-era baseline lives in Git.
"""
import argparse
from collections import Counter
from datetime import datetime
import hashlib
import json
import re
from pathlib import Path
import subprocess
from urllib.parse import urlencode
from export_current_comparison import clean_error, public_setup

ROOT = Path(__file__).resolve().parents[4]
SITE = ROOT / 'Temp/gltf-validation/comparison-site'
BASELINE = '6f5eb5657c0f21812e1cbe10b231919837d21944'
URL = 'https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/'
GROUPS = {
    'fresh-ready-for-review': 'Fresh captures — ready for your review',
    'fresh-held-known-issues': 'Fresh captures — held for known issues',
    'awaiting-regeneration': 'Awaiting verified regeneration',
    'failed': 'Current load or capture failures',
    'blocked-or-not-run': 'Blocked or not run',
    'existing-unreviewed': 'Existing captures — not a new fix',
    'approved-current': 'Your approval still matches the current comparison',
}


def digest(value):
    return hashlib.sha256(value).hexdigest()


def read_json(path):
    return json.loads(Path(path).read_text())


def indexed(rows, key='id'):
    result = {row[key]: row for row in rows}
    if len(result) != len(rows):
        raise ValueError('Duplicate case IDs')
    return result


def timestamp(value):
    return datetime.fromisoformat(value.replace('Z', '+00:00'))


def producer_identity(value):
    """Only actual loaded binary hashes/MVIDs, never host paths or runner labels."""
    runtime = value.get('runtime', value)
    return {name: {key: runtime.get(name, {}).get(key) for key in ('mvid', 'diskSha256')}
            for name in ('engine', 'gltf', 'renderer')}


def canonical(value):
    return json.dumps(value, sort_keys=True, separators=(',', ':')).encode()


def execution_proof(row, feedback_at, receipt_loader, receipt):
    """Verify the coordinator's bounded list of actually completed native calls."""
    if not receipt:
        return False, 'No post-feedback native capture batch receipt is recorded.'
    try:
        started, completed = timestamp(receipt['startedAt']), timestamp(receipt['completedAt'])
        if started <= feedback_at or completed < started:
            return False, 'The native capture does not have valid post-feedback execution times.'
    except (KeyError, ValueError, TypeError):
        return False, 'Native capture times are missing or invalid.'
    capture = row.get('capture') or {}
    if not row.get('cleanupConfirmed') or not row.get('render', {}).get('observation', {}).get('nativeCaptureSubmitted'):
        return False, 'A completed native capture submission is not proven.'
    if not row.get('producer') or not capture.get('sha256'):
        return False, 'Actual capture or loaded producer identity is missing.'
    body = receipt_loader(row)
    if digest(body) != receipt.get('resultSha256'):
        raise ValueError('Native result receipt hash differs: ' + row['id'])
    result = json.loads(body)
    for key in ('id', 'execution', 'producer', 'capture', 'load', 'render', 'cleanupConfirmed',
                'input', 'recipeId', 'recipeSnapshot', 'recipeSha256', 'recipeApplied', 'stockResourceProof'):
        if result.get(key) != row.get(key):
            raise ValueError(f'Native result receipt differs from current row: {row["id"]}: {key}')
    if capture['sha256'] != receipt.get('captureSha256') or digest(canonical(row['producer'])) != receipt.get('producerSha256'):
        raise ValueError('Capture/producer differs from executed batch: ' + row['id'])
    return True, 'Actual native capture completed after the saved feedback; visual correctness remains yours to judge.'


def load_review_baselines(feedback, baseline_commit=None, loader=None):
    """Read each explicitly named Git snapshot once; never scan history or write copies."""
    selected = baseline_commit or feedback.get('latestReviewSiteCommit') or BASELINE
    commits = [selected, *(r['siteCommit'] for r in feedback['decisions'] if 'siteCommit' in r)]
    if any(not isinstance(commit, str) or not re.fullmatch(r'[0-9a-f]{40}', commit) for commit in commits):
        raise ValueError('Review site commit must be an exact Git commit hash')
    documents, hashes = {}, {}
    for commit in sorted(set(commits)):
        try:
            body = loader(commit) if loader else subprocess.check_output(
                ['git', 'show', commit + ':dist/data.json'], cwd=SITE, stderr=subprocess.PIPE)
        except (subprocess.CalledProcessError, OSError, KeyError) as error:
            raise ValueError('Unknown or unavailable review site commit: ' + commit) from error
        documents[commit] = json.loads(body)
        hashes[commit] = digest(body)
    return selected, documents[selected], documents, hashes


def validate_reviews(feedback, baseline, reviewed_baselines=None):
    reviews = indexed(feedback['decisions'], 'caseId')
    documents = reviewed_baselines or {}
    case_indexes = {}
    sources = {}
    for case_id, review in reviews.items():
        commit = review.get('siteCommit')
        if 'siteCommit' in review and commit not in documents:
            raise ValueError('Unknown review site commit: ' + str(commit))
        document = documents[commit] if 'siteCommit' in review else baseline
        if document.get('corpusPin') != feedback.get('corpusPin'):
            raise ValueError('Review site corpus pin differs: ' + case_id)
        if commit not in case_indexes:
            case_indexes[commit] = indexed(document['cases'])
        cases = case_indexes[commit]
        if case_id not in cases or review['fingerprint'] != cases[case_id]['comparisonFingerprint']:
            raise ValueError('Feedback does not match its reviewed site: ' + case_id)
        if review.get('reviewer') != 'user' or review['status'] not in {'approved', 'rejected', 'pending'}:
            raise ValueError('Not a supported user decision: ' + case_id)
        sources[case_id] = (cases[case_id], document)
    feedback_at = max(timestamp(r['reviewedAt']) for r in reviews.values())
    return reviews, sources, feedback_at


def build_queue(current, website, baseline, feedback, receipt_loader, execution_receipt=None, reviewed_baselines=None):
    if len({x.get('corpusPin') for x in (current, website, baseline, feedback)}) != 1:
        raise ValueError('Corpus pins differ')
    raw, shown, old = (indexed(x['cases']) for x in (current, website, baseline))
    if set(raw) != set(shown) or set(shown) != set(old):
        raise ValueError('Current, exported and baseline case IDs differ')
    if current['run']['id'] != website['run']['id'] or current['run']['status'] != website['run']['status']:
        raise ValueError('Website export does not match current run')
    if current['run']['status'] not in {'complete', 'completed', 'interrupted', 'failed', 'stopped'}:
        raise ValueError('Wait for a terminal current run before preparing the review queue')
    reviews, reviewed_sources, feedback_at = validate_reviews(feedback, baseline, reviewed_baselines)
    batch = {}
    known_failures = {}
    if execution_receipt:
        if execution_receipt.get('schemaVersion') != 1 or execution_receipt.get('runId') != current['run']['id']:
            raise ValueError('Execution receipt does not match current run')
        if execution_receipt['runId'] == baseline['run']['id']:
            raise ValueError('Execution receipt still refers to the feedback-era run')
        batch = indexed(execution_receipt['cases'], 'caseId')
        known_failures = indexed(execution_receipt.get('knownFailures', []), 'caseId')
        if set(known_failures) - set(raw):
            raise ValueError('Known failure references an unknown case')
        for case_id, proof in batch.items():
            reviewed_run = reviewed_sources.get(case_id, (None, baseline))[1]['run']['id']
            if proof.get('sourceRunId') in {baseline['run']['id'], reviewed_run}:
                raise ValueError('Capture originates from the feedback-era run: ' + case_id)
            if proof.get('reviewHoldReason') is not None and (not isinstance(proof['reviewHoldReason'], str) or not proof['reviewHoldReason'].strip()):
                raise ValueError('Review hold reason must be nonempty text: ' + case_id)
            if case_id not in raw or not raw[case_id].get('capture'):
                raise ValueError('Executed batch lists an absent/unsuccessful capture: ' + case_id)
    models = indexed(website['models'])
    baseline_models = indexed(baseline['models'])
    related = {}
    for case_id, review in reviews.items():
        related.setdefault(reviewed_sources[case_id][0]['modelId'], []).append(review)
    output = []
    for case_id in sorted(shown):
        item, result = shown[case_id], raw[case_id]
        before, before_document = reviewed_sources.get(case_id, (old[case_id], baseline))
        for key in ('load', 'render'):
            if item[key]['status'] != result[key]['status'] or item[key].get('error') != clean_error(result[key].get('error')):
                raise ValueError('Exported status differs from current run: ' + case_id)
        if result.get('input') and item.get('sourceHash') != result['input'].get('sha256'):
            raise ValueError('Exported source differs from current run: ' + case_id)
        if item.get('recipeApplied') != public_setup(result.get('recipeApplied')):
            raise ValueError('Exported applied recipe differs from current run: ' + case_id)
        if result.get('recipeSnapshot') and item.get('recipe') != public_setup(result['recipeSnapshot']):
            raise ValueError('Exported recipe differs from captured recipe: ' + case_id)
        capture, actual = item.get('capture'), result.get('capture')
        if bool(capture) != bool(actual) or (capture and any(capture.get(k) != actual.get(k) for k in ('sha256', 'kind', 'frames', 'duration'))):
            raise ValueError('Exported capture differs from current run: ' + case_id)
        if capture and any(item[k]['status'] != 'passed' for k in ('load', 'render')):
            raise ValueError('Capture is attached to a failed case: ' + case_id)
        review = reviews.get(case_id)
        exact = bool(review and review['fingerprint'] == item['comparisonFingerprint'])
        model_reviews = related.get(item['modelId'], [])
        rejected = any(r['status'] == 'rejected' for r in model_reviews)
        fingerprint_changed = item['comparisonFingerprint'] != before['comparisonFingerprint'] and before['comparisonFingerprint'] not in item.get('compatibleReviewFingerprints', [])
        capture_changed = (capture or {}).get('sha256') != (before.get('capture') or {}).get('sha256')
        current_producer = producer_identity(result.get('producer') or current['run'].get('producer', {}))
        prior_producer = producer_identity(before.get('producer') or before_document['run'].get('producer', {}))
        fresh, reason = (False, 'No current capture is available.')
        if capture:
            fresh, reason = execution_proof(result, feedback_at, receipt_loader, batch.get(case_id))
        hold_reason = batch.get(case_id, {}).get('reviewHoldReason')
        if capture and fresh and fingerprint_changed and hold_reason:
            group = 'fresh-held-known-issues'
        elif capture and exact and review['status'] == 'approved':
            group = 'approved-current'
        elif capture and fresh and fingerprint_changed:
            group = 'fresh-ready-for-review'
        elif not capture and any(item[k]['status'] == 'failed' for k in ('load', 'render')):
            group = 'failed'
        elif not capture:
            group = 'blocked-or-not-run'
        elif rejected or fingerprint_changed:
            group = 'awaiting-regeneration'
        else:
            group = 'existing-unreviewed'
        guidance = models[item['modelId']].get('reviewGuidance') or baseline_models[item['modelId']].get('reviewGuidance')
        if not guidance or not guidance.get('checks'):
            raise ValueError('Model-specific check guidance is missing: ' + item['modelId'])
        output.append({
            'caseId': case_id, 'modelId': item['modelId'], 'variant': item['variant'], 'group': group,
            'url': URL + '#' + urlencode({'model': item['modelId'], 'variant': case_id}),
            'check': ' '.join(guidance['checks']), 'limitations': guidance['limits'],
            'source': guidance.get('documentedSource'), 'captureKind': (capture or {}).get('kind'),
            'comparisonFingerprint': item['comparisonFingerprint'], 'captureSha256': (capture or {}).get('sha256'),
            'freshness': {'nativeRecaptureVerified': fresh, 'reason': reason,
                          'fingerprintChanged': fingerprint_changed, 'captureBytesChanged': capture_changed,
                          'producerChanged': current_producer != prior_producer,
                          'producer': current_producer, 'execution': batch.get(case_id),
                          'comparedAgainstReviewSiteCommit': review.get('siteCommit') if review else None},
            'loadStatus': item['load']['status'], 'renderStatus': item['render']['status'],
            'error': item['render'].get('error') or item['load'].get('error') or item.get('reason'),
            'savedReview': review, 'savedReviewStillMatches': exact, 'relatedFeedback': model_reviews,
            'reviewHoldReason': hold_reason,
            'knownFailureReason': known_failures.get(case_id, {}).get('reason'),
        })
    counts = Counter(row['group'] for row in output)
    return {'schemaVersion': 2, 'corpusPin': website['corpusPin'], 'runId': current['run']['id'],
            'runStatus': current['run']['status'], 'reviewsUnchanged': True,
            'executionTimingScope': (execution_receipt or {}).get('timingScope'),
            'publication': 'Local current export; site links show these results only after the owner publishes them.',
            'summary': {key: counts[key] for key in GROUPS}, 'queue': output}


def markdown(queue):
    lines = ['# Capture review queue', '',
             'Generated from the terminal current run, its matching website export, and your saved feedback.', '',
             '**Fresh means a verified native recapture, not a confirmed fix. Only you judge visual correctness.**', '',
             queue['publication'], '',
             'Saved reviews are unchanged. An approval still applies only to its exact comparison fingerprint. '
             'Old alternate formats are not later fixes. Browser decisions made after the saved feedback export are not included.', '',
             '| Category | Cases |', '| --- | ---: |']
    lines += [f'| {title} | {queue["summary"][key]} |' for key, title in GROUPS.items()]
    if queue.get('executionTimingScope') == 'batch-boundaries':
        lines += ['', 'Execution times identify the actual capture batches; they are not per-case stopwatch measurements.']
    for key, title in GROUPS.items():
        rows = [r for r in queue['queue'] if r['group'] == key]
        lines += ['', '## ' + title, '']
        if not rows:
            lines.append('None.'); continue
        last_model = None
        for row in rows:
            if row['modelId'] != last_model:
                last_model = row['modelId']
                lines += ['### ' + last_model, '', row['check'], '', 'Limit: ' + row['limitations'], '']
                notes = list(dict.fromkeys(r.get('notes', '') for r in row['relatedFeedback'] if r.get('notes')))
                if notes:
                    lines += ['Your saved feedback: ' + ' / '.join(' '.join(n.split()) for n in notes), '']
            detail = ('sampled animation' if row['captureKind'] in {'video', 'apng'} else 'still image') if row['captureKind'] else row['loadStatus'] + ' / ' + row['renderStatus']
            lines.append(f'- [ ] [{row["variant"]}]({row["url"]}) — {detail}')
            if key in {'fresh-ready-for-review', 'fresh-held-known-issues'}:
                lines.append('  - Verified new execution; ' + ('capture bytes changed.' if row['freshness']['captureBytesChanged'] else 'capture bytes are unchanged.'))
                if row['savedReviewStillMatches']:
                    lines.append('  - Your saved ' + row['savedReview']['status'] + ' decision still matches; it has not been reset.')
                if key == 'fresh-held-known-issues':
                    lines.append('  - Hold for investigation: ' + row['reviewHoldReason'] + ' This is not a new user rejection or a request for approval.')
            elif key == 'awaiting-regeneration':
                lines.append('  - ' + row['freshness']['reason'])
            if row.get('knownFailureReason'):
                lines.append('  - Earlier attempt: ' + row['knownFailureReason'])
            if row['error']:
                lines.append('  - ' + ' '.join(str(row['error']).split())[:600])
    return '\n'.join(lines) + '\n'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--run', type=Path, default=ROOT / 'Temp/gltf-validation/current/run.json')
    parser.add_argument('--website-data', type=Path, default=ROOT / 'Temp/gltf-validation/current/website/data.json')
    parser.add_argument('--feedback', type=Path, default=ROOT / 'research/gltf-validation/user-feedback.json')
    parser.add_argument('--baseline-site-commit', help='Default comparison source; otherwise latestReviewSiteCommit, then legacy baseline')
    parser.add_argument('--output-dir', type=Path, required=True)
    parser.add_argument('--execution-receipt', type=Path, help='Coordinator receipt listing only actually captured cases in this new batch')
    args = parser.parse_args()
    run_bytes, website_bytes, feedback_bytes = (p.read_bytes() for p in (args.run, args.website_data, args.feedback))
    current, website, feedback = (json.loads(x) for x in (run_bytes, website_bytes, feedback_bytes))
    baseline_commit, baseline, reviewed_baselines, baseline_hashes = load_review_baselines(feedback, args.baseline_site_commit)
    def receipt_loader(row):
        path = (args.run.parent / 'assets' / row['id'] / 'result.json').resolve()
        if not path.is_relative_to((args.run.parent / 'assets').resolve()):
            raise ValueError('Unsafe case receipt path')
        for file, expected in [(Path(row['capture']['path']), row['capture']['sha256']),
                               (args.website_data.parent / next(c['capture']['url'] for c in website['cases'] if c['id'] == row['id']), row['capture']['sha256'])]:
            if digest(file.read_bytes()) != expected:
                raise ValueError('Capture bytes differ from receipt: ' + row['id'])
        return path.read_bytes()
    execution_bytes = args.execution_receipt.read_bytes() if args.execution_receipt else None
    queue = build_queue(current, website, baseline, feedback, receipt_loader,
                        json.loads(execution_bytes) if execution_bytes else None, reviewed_baselines)
    queue['inputs'] = {'baselineSiteCommit': baseline_commit, 'baselineDataSha256': baseline_hashes[baseline_commit],
                       'reviewSiteDataSha256': baseline_hashes,
                       'runSha256': digest(run_bytes), 'websiteDataSha256': digest(website_bytes), 'feedbackSha256': digest(feedback_bytes),
                       'executionReceiptSha256': digest(execution_bytes) if execution_bytes else None}
    # Do not publish a queue from inputs that changed during the bounded read.
    for path, content in [(args.run, run_bytes), (args.website_data, website_bytes), (args.feedback, feedback_bytes)]:
        if path.read_bytes() != content:
            raise ValueError('Input changed during queue generation: ' + str(path))
    if args.execution_receipt and args.execution_receipt.read_bytes() != execution_bytes:
        raise ValueError('Execution receipt changed during queue generation')
    args.output_dir.mkdir(parents=True, exist_ok=True)
    (args.output_dir / 'capture-review-queue.json').write_text(json.dumps(queue, ensure_ascii=False, indent=2) + '\n')
    (args.output_dir / 'CAPTURE_REVIEW_QUEUE.md').write_text(markdown(queue))
    print(json.dumps(queue['summary'], indent=2))


if __name__ == '__main__':
    main()
