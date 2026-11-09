#!/usr/bin/env python3
"""Publish-data ingest for one already executed owner model; no test or renderer dispatch."""
import argparse
import copy
from collections import Counter
import hashlib
import json
from pathlib import Path
import shutil
from urllib.parse import quote, urlencode
from PIL import Image
from group_visual_reviews import group_identity
from visual_comparison_identity import comparison_identity, decoded_media, digest
ROOT=Path(__file__).resolve().parents[4]
DIST=ROOT/'Temp/gltf-validation/comparison-site/dist'
CORPUS=ROOT/'Temp/gltf-reference/glTF-Sample-Assets'

def sha(path):return hashlib.sha256(Path(path).read_bytes()).hexdigest()
def read(path):return json.loads(Path(path).read_text())
def write(path,value):Path(path).write_text(json.dumps(value,ensure_ascii=False,indent=2)+'\n')
def verified(path,expected):
    path=Path(path);path=path if path.is_absolute() else ROOT/path
    if sha(path)!=expected:raise ValueError('Owner evidence hash differs: '+str(path))
    return path

def normalize_owner(owner):
    if owner.get('model')!='MandarinOrange':return owner
    owner=copy.deepcopy(owner);owner['executed_suite']['receipt']=owner['executed_suite']['path']
    owner['known_limits']=owner['limitations'];owner['recipe']=owner['presentation_recipe'];owner['feature_measurements']=[]
    owner['actual_public_model_forms']=len(owner['captures']);owner['variants']=[]
    for captured in owner['captures']:
        row=copy.deepcopy(captured);row['form']=row['form'].replace('derived-glb_c','compiled_glb');row.update(passed=True,engine_image={'path':captured['path'],'sha256':captured['sha256']})
        owner['variants'].append(row)
    # Full authored comparison remains intact; the notice points to its core-fallback half.
    extension_reference=owner['reference'];owner['reference']=copy.deepcopy(owner['additional_reference']);owner['reference']['comparison']=owner['limitations'][1]
    owner['additional_reference']=extension_reference
    owner['reference_notice']='Core fallback comparison: the RIGHT half of the upstream image omits optional diffuse transmission, matching this output’s supported path. The LEFT half enables that extension. Camera, light and exposure are inferred, so this is not a calibrated pixel match.'
    owner['what_changed']='Original core textures now render through public Model.Load(path,0) and the ordinary renderer in source and derived compiled forms. The valid asset has no authored default scene; scene0 was explicitly selected. Optional diffuse transmission uses standard core fallback; camera/light/exposure are inferred.'
    return owner


def ingest(manifest_path,expected_hash):
    manifest_path=verified(manifest_path,expected_hash);owner=normalize_owner(read(manifest_path));data=read(DIST/'data.json');before=copy.deepcopy(data)
    if owner.get('status')!='passed' or owner.get('corpus_revision')!=data['corpusPin']:raise ValueError('Owner model is not a completed public result for this corpus')
    suite=owner['executed_suite'];receipt=read(verified(suite['receipt'],suite['sha256']))
    if not receipt.get('passed') or receipt['passed_cases']!=suite['passed'] or receipt['expected_cases']!=suite['expected'] or receipt['failed_cases'] or receipt['incomplete_cases'] or receipt.get('cleanup_errors'):raise ValueError('Public owner receipt is incomplete or failed')
    if receipt.get('engine_mvid')!=owner['producer']['engine_mvid'] or receipt.get('gltf_mvid')!=owner['producer']['gltf_mvid']:raise ValueError('Loaded public producer differs')
    model=owner['model'];cases={r['id']:r for r in data['cases']};reference=owner['reference'];refpath=verified(reference['path'],reference['sha256']);relative_ref=refpath.relative_to(CORPUS)
    upstream_ref={'path':str(relative_ref),'url':f'https://raw.githubusercontent.com/KhronosGroup/glTF-Sample-Assets/{data["corpusPin"]}/'+quote(str(relative_ref)), 'sha256':reference['sha256'],'kind':'image','animated':False}
    with Image.open(refpath) as im:upstream_ref.update(width=im.width,height=im.height)
    # Display only the public scope; older candidate/history receipts remain in the owner manifest.
    limitations=[owner['known_limits'][0],reference['comparison'],'Windows, model-editor parity and performance remain untested. User appearance approval is pending.']
    recipe={'camera':owner['recipe'],'provenance':'Recorded owner public validation recipe. Upstream reference camera/exposure is unknown.','matchStatus':'needs-tuning','limitations':limitations}
    applied={'camera':owner['recipe'],'complete':True,'limitations':limitations}
    new_ids=[];groups=set();changed=[]
    media_dir=DIST/'assets/completed';media_dir.mkdir(parents=True,exist_ok=True)
    for form in owner['variants']:
        if form.get('passed') is not True or not ('Model.Load' in form.get('scope','') and 'ordinary ModelRenderer' in form.get('scope','')):raise ValueError('Not a completed public renderer form')
        variant,route=form['form'].split('/');compiled=route=='compiled_glb';case_id=f'{model}/{variant}'+('::compiled' if compiled else '')
        source_id=f'{model}/{variant}';row=copy.deepcopy(cases[source_id]);old=copy.deepcopy(cases.get(case_id))
        image=verified(form['engine_image']['path'],form['engine_image']['sha256']);target=media_dir/(form['engine_image']['sha256']+image.suffix)
        if not target.exists():shutil.copy2(image,target)
        pixels=decoded_media(target);refpixels=decoded_media(refpath)
        row.update(id=case_id,sourceCaseId=source_id,variant=variant+(' → derived compiled GLB' if compiled else ''),capture={'url':str(target.relative_to(DIST)),'sha256':form['engine_image']['sha256'],'kind':'image','frames':1,'duration':0},reference=upstream_ref,recipe=recipe,recipeApplied=applied,load={'status':'passed','error':None},render={'status':'passed','error':None},reason=None,visualReview={'status':'pending','reviewer':None})
        row['evidence']={'type':'isolated-model-execution','sourceForm':source_id,'derivedCompiled':compiled,'manifestSha256':expected_hash,'receiptSha256':suite['sha256'],'producer':{k:v for k,v in owner['producer'].items() if 'mvid' in k or 'sha256' in k},'inputSha256':form['input_sha256'],'scope':form['scope'],'focusedTests':{'passed':suite['passed'],'expected':suite['expected'],'supplementalFeatureMeasurements':len(owner['feature_measurements'])},'limitations':limitations}
        if owner.get('reference_notice'):row['referenceNotice']=owner['reference_notice']
        if owner.get('additional_reference'):
            additional=owner['additional_reference'];additional_path=verified(additional['path'],additional['sha256']).relative_to(CORPUS)
            row['additionalReference']={'url':f'https://raw.githubusercontent.com/KhronosGroup/glTF-Sample-Assets/{data["corpusPin"]}/'+quote(str(additional_path)),'purpose':'Extension-enabled source screenshot','sha256':additional['sha256']}
        row['provenanceFingerprint']=digest(row['evidence']);row['comparisonFingerprint']=comparison_identity(row,pixels,refpixels);row['reviewGroupFingerprint']=group_identity(row,pixels,refpixels);row['visualIdentityVersion']=2
        same=bool(old and old['comparisonFingerprint']==row['comparisonFingerprint']);row['updated']=True;row['visualChanged']=old.get('visualChanged',False) if same else True
        row['compatibleReviewFingerprints']=copy.deepcopy(old.get('compatibleReviewFingerprints',[])) if same else []
        row['whatChanged']=owner.get('what_changed') or 'Required unlit source materials now load through public Model.Load and render through ordinary ModelRenderer in both source and derived compiled forms. Four identical captures share one visual decision. Mixed reachable lit/unlit scenes still reject explicitly.'
        if not same:changed.append(case_id)
        cases[case_id]=row;new_ids.append(case_id);groups.add(row['reviewGroupFingerprint'])
    if len(new_ids)!=owner['actual_public_model_forms'] or len(groups)!=1:raise ValueError('Expected four public forms sharing the declared visual comparison')
    data['cases']=list(cases.values());isolated=[r for r in cases.values() if r.get('evidence',{}).get('type')=='isolated-model-execution'];data['evidenceCollection'].update(isolatedForms=len(isolated),isolatedModels=len({r['modelId'] for r in isolated}))
    data['summary'].update(captures=sum(bool(r.get('capture')) for r in cases.values()),variants=len(cases),load=dict(Counter(r['load']['status'] for r in cases.values())),render=dict(Counter(r['render']['status'] for r in cases.values())))
    # This incremental operation must preserve all unrelated rows and saved user decisions.
    assert data.get('savedReviews')==before.get('savedReviews')
    old_cases={r['id']:r for r in before['cases']}
    assert all(cases[k]==r for k,r in old_cases.items() if k not in new_ids)
    queue=read(DIST/'review-queue.json');queued={r['caseId']:r for r in queue['queue']}
    guide=next(m for m in data['models'] if m['id']==model)['reviewGuidance']
    for case_id in new_ids:
        row=cases[case_id];queued[case_id]={'caseId':case_id,'modelId':model,'variant':row['variant'],'group':'fresh-ready-for-review' if row['visualChanged'] else queued.get(case_id,{}).get('group','existing-unreviewed'),'url':'/#'+urlencode({'model':model,'variant':case_id}),'comparisonFingerprint':row['comparisonFingerprint'],'reviewGroupFingerprint':row['reviewGroupFingerprint'],'captureSha256':row['capture']['sha256'],'captureKind':'image','loadStatus':'passed','renderStatus':'passed','freshness':{'nativeRecaptureVerified':True,'fingerprintChanged':case_id in changed},'check':guide['checks'][0],'limitations':' '.join(limitations),'source':row['sourceUrl'],'evidence':row['evidence'],'savedReviewStillMatches':False,'savedReview':None,'relatedFeedback':[]}
    queue['queue']=list(queued.values());queue['summary']=dict(Counter(r['group'] for r in queued.values()))
    write(DIST/'data.json',data);write(DIST/'review-queue.json',queue)
    report={'model':model,'publicFormsAdded':len(new_ids),'newVisualReviewComparisons':len(groups) if changed else 0,'changedCaseIds':changed,'totalCaptures':data['summary']['captures'],'unchangedCasesPreserved':len(old_cases)-sum(k in old_cases for k in new_ids),'savedVerdictsUnchanged':True,'receiptReusedWithoutRerun':suite['sha256'],'dataSha256':sha(DIST/'data.json')}
    write(ROOT/'Temp/gltf-validation'/f'{model.lower()}-site-ingest.json',report);print(json.dumps(report))
    return report

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('manifest');parser.add_argument('--sha256',required=True);args=parser.parse_args();ingest(args.manifest,args.sha256)
