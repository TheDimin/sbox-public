#!/usr/bin/env python3
"""Ingest only the two canonical NormalTangentTest source captures, with scoped four-form proof."""
import copy
from collections import Counter
import shutil
from urllib.parse import quote,urlencode
from ingest_completed_model import ROOT,DIST,CORPUS,read,write,verified,sha
from export_current_comparison import public_setup,public_run
from visual_comparison_identity import comparison_identity,decoded_media,digest
from group_visual_reviews import group_identity

def ingest():
    expected='58b9573afeea9300ff95fef736cd27ce0d8c5060654dbcd69d3038f7a27c4cad';owner=read(verified(ROOT/'research/gltf-validation/direct-gpu/models/NormalTangentTest/result.json',expected));data=read(DIST/'data.json');before=copy.deepcopy(data)
    positive=set();proofs=[]
    for proof in owner['receipts'][-2:]:
        receipt=read(verified(proof['path'],proof['sha256']))
        if receipt.get('cleanup_errors') or receipt['incomplete_cases'] or receipt['engine_mvid']!=owner['producer']['engine_mvid']:raise ValueError('Invalid numerical proof')
        for t in receipt['tests']:
            if t['passed']:positive.add(tuple(t['arguments']))
        proofs.append({'receiptSha256':proof['sha256'],'passed':receipt['passed_cases'],'expected':receipt['expected_cases'],'failed':receipt['failed_cases'],'scope':proof['scope']})
    if positive!={('glTF',False),('glTF',True),('glTF-Binary',False),('glTF-Binary',True)}:raise ValueError('Four-form public proof not complete')
    reference=owner['reference'];refpath=verified(reference['path'],reference['sha256']);refrelative=refpath.relative_to(CORPUS);ref={'path':str(refrelative),'url':f'https://raw.githubusercontent.com/KhronosGroup/glTF-Sample-Assets/{data["corpusPin"]}/'+quote(str(refrelative)),'sha256':reference['sha256'],'kind':'image','animated':False};refpixels=decoded_media(refpath)
    cases={r['id']:r for r in data['cases']};media=DIST/'assets/completed';media.mkdir(parents=True,exist_ok=True);ids=[];groups=set()
    limits=owner['known_limits']+['Numerical tangent/normal/index proof does not establish complete PBR or ambient-occlusion fidelity. User appearance approval remains pending.']
    notice='Unmatched appearance: this output is brighter than the upstream reference and lacks its unknown HDRI horizon reflection. Camera, lighting and exposure are inferred. Tangent/normal numerical checks do not establish complete PBR/AO appearance.'
    for canonical in owner['canonical_captures']:
        native=read(verified(canonical['receipt'],canonical['receipt_sha256']));case_id=native['id']
        if native['load']['status']!='passed' or native['render']['status']!='passed' or not native['render']['observation']['nativeCaptureSubmitted'] or native.get('cleanup_errors') or not native['cleanupConfirmed']:raise ValueError('Incomplete canonical capture')
        if native['capture']!=canonical['capture']:raise ValueError('Manifest capture differs from executed receipt')
        for key in ['engine','gltf']:
            if native['producer'][key]['mvid']!=owner['producer'][key+'_mvid']:raise ValueError('Capture producer differs')
        image=verified(native['capture']['path'],native['capture']['sha256']);target=media/(native['capture']['sha256']+'.png');shutil.copy2(image,target);pixels=decoded_media(target)
        if pixels['frames'][0]['size']!=[512,483]:raise ValueError('Only canonical source512x483 images may enter this flow')
        row=copy.deepcopy(cases[case_id]);applied=public_setup(native['recipeApplied']);applied['limitations']=applied.get('limitations',[])+limits
        row.update(capture={'url':str(target.relative_to(DIST)),'sha256':native['capture']['sha256'],'kind':'image','frames':1,'duration':0},reference=ref,referenceNotice=notice,load={'status':'passed','error':None},render={'status':'passed','error':None},reason=None,recipeApplied=applied,recipe={'camera':applied['camera'],'presentation':applied.get('presentation'),'matchStatus':'needs-tuning','provenance':'Actual recorded public capture setup; source reference camera/HDRI unknown.','limitations':limits},visualReview={'status':'pending','reviewer':None},updated=True,visualChanged=True,compatibleReviewFingerprints=[],visualIdentityVersion=2)
        row['evidence']={'type':'isolated-model-execution','sourceForm':case_id,'derivedCompiled':False,'manifestSha256':expected,'nativeCaptureReceiptSha256':canonical['receipt_sha256'],'receiptSha256':canonical['receipt_sha256'],'producer':public_run({'producer':native['producer']})['producer'],'inputSha256':native['input']['sha256'],'publicNumericalFormsPassed':4,'formProofReceipts':proofs,'scope':'Canonical source glTF/glb appearance evidence only. Four public source/derived numerical forms passed across two receipts. Derived compiled images are192px diagnostics and are not canonical comparison images.','limitations':limits}
        row['comparisonFingerprint']=comparison_identity(row,pixels,refpixels);row['reviewGroupFingerprint']=group_identity(row,pixels,refpixels);row['provenanceFingerprint']=digest(row['evidence']);groups.add(row['reviewGroupFingerprint']);ids.append(case_id)
        row['whatChanged']='Public source materials now produce canonical source glTF/glb captures. Four source/derived forms separately pass bounded GPU tangent/normal/index and lifecycle checks. Appearance remains brighter and lacks the reference HDRI horizon; compiled diagnostic images are not promoted.'
        cases[case_id]=row
    assert len(ids)==2 and all('::compiled' not in k for k in ids)
    for row in before['cases']:
        if row['id'] not in ids:assert cases[row['id']]==row
    assert data['savedReviews']==before['savedReviews']
    data['cases']=list(cases.values());isolated=[r for r in cases.values()if r.get('evidence',{}).get('type')=='isolated-model-execution'];data['evidenceCollection'].update(isolatedForms=len(isolated),isolatedModels=len({r['modelId']for r in isolated}));data['summary'].update(captures=sum(bool(r.get('capture'))for r in cases.values()),variants=len(cases),load=dict(Counter(r['load']['status']for r in cases.values())),render=dict(Counter(r['render']['status']for r in cases.values())))
    queue=read(DIST/'review-queue.json');queued={r['caseId']:r for r in queue['queue']};guide=next(m for m in data['models']if m['id']=='NormalTangentTest')['reviewGuidance']
    for case_id in ids:
        r=cases[case_id];queued[case_id]={'caseId':case_id,'modelId':'NormalTangentTest','variant':r['variant'],'group':'fresh-ready-for-review','url':'/#'+urlencode({'model':r['modelId'],'variant':case_id}),'comparisonFingerprint':r['comparisonFingerprint'],'reviewGroupFingerprint':r['reviewGroupFingerprint'],'captureSha256':r['capture']['sha256'],'captureKind':'image','loadStatus':'passed','renderStatus':'passed','freshness':{'nativeRecaptureVerified':True,'fingerprintChanged':True},'check':guide['checks'][0],'limitations':' '.join(limits),'source':r['sourceUrl'],'evidence':r['evidence'],'savedReviewStillMatches':False,'savedReview':None,'relatedFeedback':[]}
    queue['queue']=list(queued.values());queue['summary']=dict(Counter(r['group']for r in queued.values()));write(DIST/'data.json',data);write(DIST/'review-queue.json',queue)
    report={'model':'NormalTangentTest','canonicalSourceCaptures':2,'newVisualReviewComparisons':len(groups),'publicNumericalFormsReused':4,'compiledCanonicalCaptures':0,'totalCaptures':data['summary']['captures'],'totalCases':len(cases),'unchangedOtherRows':len(before['cases'])-2,'savedVerdictsUnchanged':True};write(ROOT/'Temp/gltf-validation/normal-site-ingest.json',report);print(__import__('json').dumps(report))
if __name__=='__main__':ingest()
