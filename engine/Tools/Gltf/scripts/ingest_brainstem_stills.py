#!/usr/bin/env python3
"""Ingest BrainStem first-key stills only; sparse timing recording is not published."""
import copy
from collections import Counter
from pathlib import Path
import shutil
from urllib.parse import urlencode
from PIL import Image
from ingest_completed_model import ROOT,DIST,CORPUS,read,write,verified,sha
from visual_comparison_identity import comparison_identity,decoded_media,digest
from group_visual_reviews import group_identity


def ingest():
    expected='6ffbb1eadd2156988875b8713f5a39b5157468c64d746dc08290d11b6c5c3b4b'
    owner=read(verified(ROOT/'research/gltf-validation/direct-gpu/models/BrainStem/result.json',expected));data=read(DIST/'data.json');before=copy.deepcopy(data)
    if owner['status']!='public_source_compiled_gpu_sampled_pass_visual_review_pending':raise ValueError('Owner public result is not ready')
    receipts={}
    for record in owner['receipts']:
        receipt=read(verified(record['path'],record['sha256']))
        if not receipt.get('passed') or receipt['failed_cases'] or receipt['incomplete_cases'] or receipt.get('cleanup_errors'):raise ValueError('Incomplete public/source proof')
        if receipt['engine_mvid']!=owner['producer']['engine_mvid'] or receipt['gltf_mvid']!=owner['producer']['gltf_mvid']:raise ValueError('Producer mismatch')
        receipts[record['sha256']]=receipt
    gif=verified(owner['reference']['path'],owner['reference']['sha256']);frame=verified(owner['still_comparison']['reference']['path'],owner['still_comparison']['reference']['sha256'])
    with Image.open(gif) as a,Image.open(frame) as b:
        a.seek(0)
        if a.convert('RGBA').tobytes()!=b.convert('RGBA').tobytes():raise ValueError('Reference frame is not pinned GIFframe0')
    media=DIST/'assets/completed';media.mkdir(parents=True,exist_ok=True);ref_target=media/('brainstem-reference-frame0-'+sha(frame)[:24]+'.png');shutil.copy2(frame,ref_target)
    reference={'path':'Models/BrainStem/screenshot/screenshot.gif','url':str(ref_target.relative_to(DIST)),'frameUrl':str(ref_target.relative_to(DIST)),'kind':'image','animated':False,'sha256':sha(frame),'sourceSha256':owner['reference']['sha256'],'sourceFrameIndex':0,'width':130,'height':130}
    additional={'url':f'https://raw.githubusercontent.com/KhronosGroup/glTF-Sample-Assets/{data["corpusPin"]}/Models/BrainStem/screenshot/screenshot.gif','label':'Open original upstream animation ↗','sha256':owner['reference']['sha256']}
    limits=['Review skinning/articulation and source-material rendering in this still; lighting, framing and animation phase differ from the upstream reference. Appearance is not marked matched.',owner['limits'][0],owner['limits'][2],'A still does not establish animation interpolation or cadence. The separate sparse61-frame source recording is withheld from this comparison; no dense comparable clip is available.',owner['limits'][4],owner['limits'][6]]
    notice='Still review only: engine clip0/time0 versus upstream GIFframe0. Pose-phase correspondence is unknown; lighting, framing and exposure differ. Sparse motion is not presented as a matched animation.'
    cases={r['id']:r for r in data['cases']};inputs={r['variant']:r for r in owner['inputs']};new_ids=[];groups=set();refpixels=decoded_media(frame)
    for form in owner['forms']:
        if form['status']!='passed' or form['cleanup']!='passed' or form['receipt_sha256'] not in receipts:raise ValueError('Incomplete public form')
        variant,route=form['name'].split('/');compiled=route=='compiled';source_id='BrainStem/'+variant;case_id=source_id+('::compiled' if compiled else '')
        still=form['captures'][0];image=verified(still['path'],still['sha256']);target=media/(still['sha256']+'.png');shutil.copy2(image,target)
        source=inputs[variant];verified(source['path'],source['sha256'])
        for dependency in source.get('dependencies',[]):verified(dependency['path'],dependency['sha256'])
        row=copy.deepcopy(cases[source_id]);camera=still['camera'];row.update(id=case_id,sourceCaseId=source_id,variant=variant+(' → derived compiled GLB' if compiled else ''),capture={'url':str(target.relative_to(DIST)),'sha256':still['sha256'],'kind':'image','frames':1,'duration':0,'sampled':True},reference=reference,additionalReference=additional,referenceNotice=notice,load={'status':'passed','error':None},render={'status':'passed','error':None},reason=None)
        setup={'camera':camera,'animation':{'clip':0,'time':0,'mode':'actual first-key public renderer still; no animation-cadence proof'},'complete':True,'limitations':limits}
        row['recipeApplied']=setup;row['recipe']={'camera':camera,'animation':setup['animation'],'matchStatus':'needs-tuning','provenance':'Inferred192px orthographic lit view; upstream camera/light/exposure unknown.','limitations':limits}
        row['evidence']={'type':'isolated-model-execution','sourceForm':source_id,'derivedCompiled':compiled,'manifestSha256':expected,'receiptSha256':form['receipt_sha256'],'producer':{k:v for k,v in owner['producer'].items() if 'mvid' in k or 'sha256' in k},'sourceInputSha256':source['sha256'],'compiledBytesSeparatelyHashed':False if compiled else None,'scope':'Public source-material rendering at four sampled animation phases; main review uses actual first-key still only. GPU checks sample three vertices per primitive and full index connectivity, not every vertex/full specification.','limitations':limits}
        pixels=decoded_media(target);row['comparisonFingerprint']=comparison_identity(row,pixels,refpixels);row['reviewGroupFingerprint']=group_identity(row,pixels,refpixels);groups.add(row['reviewGroupFingerprint']);row['visualIdentityVersion']=2;row['provenanceFingerprint']=digest(row['evidence']);row['compatibleReviewFingerprints']=[];row['visualReview']={'status':'pending','reviewer':None};row['updated']=True;row['visualChanged']=True
        row['whatChanged']='Current public source/compiled forms now produce source-material first-key stills and bounded sampled GPU proof. Review articulated geometry and materials; framing, phase and lighting differ. Animation and final visual acceptance remain pending.'
        cases[case_id]=row;new_ids.append(case_id)
    if len(new_ids)!=12:raise ValueError('Expected12 public forms')
    for old in before['cases']:
        if old['modelId']!='BrainStem':assert cases[old['id']]==old
    assert data['savedReviews']==before['savedReviews']
    model=next(m for m in data['models']if m['id']=='BrainStem')
    # Content license remains Poser EULA, distinct from metadata-only CC BY.
    model['licenses']=[{'artist':'Keith Hunter','owner':'Smith Micro Software, Inc.','year':'2017','license':'Poser EULA','licenseUrl':f'https://github.com/KhronosGroup/glTF-Sample-Assets/blob/{data["corpusPin"]}/LICENSES/LicenseRef-Poser-EULA.txt','what':'BrainStem content; rendered media/reference only. Model files are not included.'}]
    data['cases']=list(cases.values());isolated=[r for r in cases.values()if r.get('evidence',{}).get('type')=='isolated-model-execution'];data['evidenceCollection'].update(isolatedForms=len(isolated),isolatedModels=len({r['modelId']for r in isolated}));data['summary'].update(captures=sum(bool(r.get('capture'))for r in cases.values()),variants=len(cases),load=dict(Counter(r['load']['status']for r in cases.values())),render=dict(Counter(r['render']['status']for r in cases.values())))
    queue=read(DIST/'review-queue.json');queued={r['caseId']:r for r in queue['queue']};guide=model['reviewGuidance']
    for case_id in new_ids:
        row=cases[case_id];queued[case_id]={'caseId':case_id,'modelId':'BrainStem','variant':row['variant'],'group':'fresh-ready-for-review','url':'/#'+urlencode({'model':'BrainStem','variant':case_id}),'comparisonFingerprint':row['comparisonFingerprint'],'reviewGroupFingerprint':row['reviewGroupFingerprint'],'captureSha256':row['capture']['sha256'],'captureKind':'image','loadStatus':'passed','renderStatus':'passed','freshness':{'nativeRecaptureVerified':True,'fingerprintChanged':True},'check':guide['checks'][0],'limitations':' '.join(limits),'source':row['sourceUrl'],'evidence':row['evidence'],'savedReviewStillMatches':False,'savedReview':None,'relatedFeedback':[]}
    queue['queue']=list(queued.values());queue['summary']=dict(Counter(r['group']for r in queued.values()));write(DIST/'data.json',data);write(DIST/'review-queue.json',queue)
    report={'model':'BrainStem','publicStillForms':len(new_ids),'uniqueReviewGroups':len(groups),'totalCaptures':data['summary']['captures'],'totalCases':len(cases),'savedVerdictsUnchanged':True,'sparseApngPublished':False,'modelPayloadPublished':False,'manifestSha256':expected,'receiptHashes':[r['sha256']for r in owner['receipts']]};write(ROOT/'Temp/gltf-validation/brainstem-site-ingest.json',report);print(__import__('json').dumps(report))
if __name__=='__main__':ingest()
