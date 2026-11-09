#!/usr/bin/env python3
"""Add shared visual-review groups without recapture, verdict edits or provenance changes."""
import json
from pathlib import Path
from visual_comparison_identity import decoded_media, digest, review_context
ROOT=Path(__file__).resolve().parents[4]
DIST=ROOT/'Temp/gltf-validation/comparison-site/dist'
CORPUS=ROOT/'Temp/gltf-reference/glTF-Sample-Assets'

def group_identity(row,capture,reference):
    return digest({'version':1,'model':row['modelId'],'capture':capture,'reference':reference,'context':review_context(row)})

def reference_pixels(reference,dist=DIST,corpus=CORPUS):
    if reference.get('decodedMediaUrl'):return decoded_media(dist/reference['decodedMediaUrl'])
    if reference.get('frameUrl'):return decoded_media(dist/reference['frameUrl'])
    if reference.get('path') and reference.get('kind')!='unavailable':return decoded_media(corpus/reference['path'])
    return {'unavailable':True}


def add_review_groups(data,dist=DIST,corpus=CORPUS):
    for row in data['cases']:
        cap=row.get('capture');ref=row.get('reference') or {}
        row.pop('reviewGroupFingerprint',None)
        if not cap:continue
        # This derives grouping from existing media; it executes no renderer/test suite.
        pixels=decoded_media(dist/cap['url'])
        reference=reference_pixels(ref,dist,corpus)
        row['reviewGroupFingerprint']=group_identity(row,pixels,reference)
    return data

if __name__=='__main__':
    p=DIST/'data.json';before=json.loads(p.read_text());fingerprints={r['id']:r['comparisonFingerprint'] for r in before['cases']};verdicts=before.get('savedReviews',[])
    add_review_groups(before)
    assert fingerprints=={r['id']:r['comparisonFingerprint'] for r in before['cases']}
    assert verdicts==before.get('savedReviews',[])
    p.write_text(json.dumps(before,ensure_ascii=False,indent=2)+'\n')
    queue_path=DIST/'review-queue.json';queue=json.loads(queue_path.read_text());cases={c['id']:c for c in before['cases']}
    for r in queue['queue']:
        if cases[r['caseId']].get('reviewGroupFingerprint'):r['reviewGroupFingerprint']=cases[r['caseId']]['reviewGroupFingerprint']
    queue_path.write_text(json.dumps(queue,ensure_ascii=False,indent=2)+'\n')
    captured=[r for r in before['cases'] if r.get('capture')];updated=[r for r in captured if r.get('visualChanged')]
    print(json.dumps({'capturedForms':len(captured),'uniqueVisualComparisons':len({r['reviewGroupFingerprint'] for r in captured}),'updatedForms':len(updated),'uniqueUpdatedComparisons':len({r['reviewGroupFingerprint'] for r in updated}),'existingFingerprintsAndVerdictsUnchanged':True}))
