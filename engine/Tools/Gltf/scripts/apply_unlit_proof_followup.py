#!/usr/bin/env python3
"""Apply supplemental mixed-path proof without changing upstream visual review identity."""
import copy
import json
from pathlib import Path
from ingest_completed_model import ROOT,DIST,verified,read,write
from visual_comparison_identity import digest

def apply():
    path=ROOT/'research/gltf-validation/direct-gpu/models/UnlitTest/result.json'
    owner=read(verified(path,'ea3d5296aa55473468a241e0d032a41c44ffbac3a1bd4fdccfc3d55320f4b92a'));follow=owner['mixed_material_followup']
    proofs=[]
    for suite in owner['executed_suites'][1:]:
        receipt=read(verified(suite['receipt'],suite['sha256']))
        if not receipt.get('passed') or receipt['failed_cases'] or receipt['incomplete_cases'] or receipt.get('cleanup_errors'):raise ValueError('Incomplete supplemental proof')
        proofs.append({'type':'synthetic-mixed-path-control','receiptSha256':suite['sha256'],'passed':suite['passed'],'expected':suite['expected'],'producer':{k:v for k,v in follow['producer'].items() if 'mvid' in k or 'sha256' in k}})
    data=read(DIST/'data.json');before=copy.deepcopy(data);current={r['id']:r for r in data['cases']}
    limits=[owner['known_limits'][i] for i in [1,2,3,4]]
    summary='Later e12 synthetic controls support mixed scenes containing unlit and existing legacy-compatible opaque lit slots. Public material variants remain unsupported. These supplemental controls retain their own producer and do not replace or recapture the four01459 upstream model images.'
    for row in data['cases']:
        if row['modelId']!='UnlitTest':continue
        row['currentProof']={'type':'proof-only-supplemental-update','summary':summary,'displayWhatChanged':'The four original public model captures are unchanged. Later mixed-path controls add scoped legacy-compatible opaque lit support; no new visual review is requested.','displayLimitations':limits,'receipts':proofs}
        row['evidence']['proofOnlyFollowups']=proofs;row['provenanceFingerprint']=digest(row['evidence'])
    for old in before['cases']:
        now=current[old['id']]
        if old['modelId']!='UnlitTest':assert now==old
        else:
            for key in ['comparisonFingerprint','reviewGroupFingerprint','capture','reference','recipeApplied','visualChanged']:
                assert now[key]==old[key],key
    assert data['savedReviews']==before['savedReviews']
    queue=read(DIST/'review-queue.json')
    for row in queue['queue']:
        if row['modelId']=='UnlitTest':row['evidence']=current[row['caseId']]['evidence'];row['limitations']=' '.join(limits)
    write(DIST/'data.json',data);write(DIST/'review-queue.json',queue)
    print('Applied Unlit supplemental proof: visual fingerprints/groups, captures, timing, reference, capture-time context, queue groups and user verdicts unchanged.')
if __name__=='__main__':apply()
