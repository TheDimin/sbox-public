#!/usr/bin/env python3
"""Three completed source-only Shoe material views; overall model/compiled review remains incomplete."""
import copy
from collections import Counter
from pathlib import Path
import json
import shutil
from urllib.parse import quote,urlencode
from ingest_completed_model import ROOT,DIST,CORPUS,read,write,verified,sha
from visual_comparison_identity import comparison_identity,decoded_media,digest
from group_visual_reviews import group_identity

def ingest():
    expected='d3b5c0afda1a54c9d1c519be2c740c1715335d5e65ac5a259a5fc4d59e2abfc7';owner=read(verified(ROOT/'research/gltf-validation/direct-gpu/models/MaterialsVariantsShoe/result.json',expected));review=owner['review_7eb'];data=read(DIST/'data.json');before=copy.deepcopy(data)
    receipt=read(verified(review['receipt'],review['sha256']))
    tests={tuple(t['arguments']):t['passed']for t in receipt['tests']if t['method']=='PublicVariantDrawAndReset'}
    if tests!={(False,):True,(True,):False} or receipt.get('cleanup_errors') or receipt['incomplete_cases'] or receipt['engine_mvid']!=owner['producer']['engine_mvid']:raise ValueError('Source-only bounded review proof differs')
    measurement=next(row for m in receipt['measurements']for row in m.get('value',[])if row.get('form')=='glTF/source')
    actuals={Path(c['Path']).name:c for c in measurement['captures']}
    if actuals['default.png']['Sha256']!=actuals['default_restored.png']['Sha256']:raise ValueError('Source default reset is not exact')
    source=verified(CORPUS/'Models/MaterialsVariantsShoe/glTF/MaterialsVariantsShoe.gltf',measurement['input_sha256']);document=read(source)
    variants=document['extensions']['KHR_materials_variants']['variants'];assert variants[0]['name']=='midnight';assert document['meshes'][0]['primitives'][0]['material']==0
    mappings=document['meshes'][0]['primitives'][0]['extensions']['KHR_materials_variants']['mappings'];assert any(0 in m['variants']and m['material']==0 for m in mappings)
    refpath=verified(review['reference']['path'],review['reference']['sha256']);refrelative=refpath.relative_to(CORPUS);reference={'path':str(refrelative),'url':f'https://raw.githubusercontent.com/KhronosGroup/glTF-Sample-Assets/{data["corpusPin"]}/'+quote(str(refrelative)),'sha256':review['reference']['sha256'],'kind':'image','animated':False};refpixels=decoded_media(refpath)
    limits=['Source-only visual review; the overall model remains incomplete. The compiled tight-frame default reset differs by69pixels, exceeding its64pixel threshold, and is not promoted as a pass.','Prior derived-compiled loading required external JPEG dependencies mounted; compiled-only packaging remains unproven.','192px source views use inferred orthographic camera/light/exposure, not calibrated upstream presentation. Human appearance approval is pending.','Full source+compiled GPU/material UV and sampler semantics, per-instance rendered independence/reload, Windows and model-editor parity remain unproven.']
    cases={r['id']:r for r in data['cases']};base_id='MaterialsVariantsShoe/glTF';media=DIST/'assets/completed';media.mkdir(parents=True,exist_ok=True);ids=[];groups=set()
    for capture in review['source_captures']:
        key='default'if capture['variant'].startswith('default')else capture['variant'];index={'default':0,'beach':1,'street':2}[key];case_id=base_id if key=='default'else base_id+'::material='+key
        actual=actuals[key+'.png'];assert actual['Sha256']==capture['sha256'];image=verified(capture['path'],capture['sha256']);target=media/(capture['sha256']+'.png');shutil.copy2(image,target);pixels=decoded_media(target);assert pixels['frames'][0]['size']==[192,192]
        row=copy.deepcopy(cases[base_id]);camera=json.loads(actual['CameraStateJson']);selection={'sourceMaterialIndex':index,'authoredNames':['default','midnight']if key=='default'else[key],'captureSelection':'default'if key=='default'else key}
        applied={'camera':camera,'materialVariant':selection,'complete':True,'limitations':limits}
        row.update(id=case_id,sourceCaseId=base_id,variant='glTF source · '+('Default / Midnight (material0)'if key=='default'else key.title()),capture={'url':str(target.relative_to(DIST)),'sha256':capture['sha256'],'kind':'image','frames':1,'duration':0},reference=reference,referenceNotice='Source-only material view: compare its colorway with the matching upstream label. Default and Midnight share authored material0 and one captured comparison. Camera/light are inferred; compiled reset remains69pixels over a64pixel threshold. Overall model acceptance is incomplete.',load={'status':'passed','error':None},render={'status':'passed','error':None},reason=None,recipeApplied=applied,recipe={'camera':camera,'matchStatus':'needs-tuning','provenance':review['source_recipe'],'limitations':limits},visualReview={'status':'pending','reviewer':None},updated=True,visualChanged=True,compatibleReviewFingerprints=[],visualIdentityVersion=2)
        row['evidence']={'type':'isolated-model-execution','sourceForm':base_id,'sourceMaterialSelection':selection,'derivedCompiled':False,'modelAcceptance':'incomplete','manifestSha256':expected,'receiptSha256':review['sha256'],'producer':{**owner['producer'],'gltf_mvid':receipt['gltf_mvid']},'inputSha256':measurement['input_sha256'],'sourceDefaultResetExact':True,'sourceDefaultResetSha256':actuals['default_restored.png']['Sha256'],'compiledReview':{'status':'failed','differentPixels':69,'threshold':64,'canonicalPromoted':False},'compiledLoaderScope':'Earlier retry passed only with external JPEGs mounted; not compiled-only packaging.','scope':measurement['scope'],'limitations':limits}
        row['comparisonFingerprint']=comparison_identity(row,pixels,refpixels);row['reviewGroupFingerprint']=group_identity(row,pixels,refpixels);row['provenanceFingerprint']=digest(row['evidence']);row['whatChanged']='Completed source material selection now has three tighter views: Default/Midnight material0, Beach and Street. Source default reset is byte-identical. Compiled reset discrepancy and dependency-mounted loader limits remain unresolved; this is not a full-model pass.'
        cases[case_id]=row;ids.append(case_id);groups.add(row['reviewGroupFingerprint'])
    assert len(ids)==3 and len(groups)==3
    for old in before['cases']:
        if old['id']!=base_id:assert cases[old['id']]==old
    assert data['savedReviews']==before['savedReviews']
    data['cases']=list(cases.values());isolated=[r for r in cases.values()if r.get('evidence',{}).get('type')=='isolated-model-execution'];data['evidenceCollection'].update(isolatedForms=len(isolated),isolatedModels=len({r['modelId']for r in isolated}));data['summary'].update(captures=sum(bool(r.get('capture'))for r in cases.values()),variants=len(cases),load=dict(Counter(r['load']['status']for r in cases.values())),render=dict(Counter(r['render']['status']for r in cases.values())))
    queue=read(DIST/'review-queue.json');queued={r['caseId']:r for r in queue['queue']};guide=next(m for m in data['models']if m['id']=='MaterialsVariantsShoe')['reviewGuidance']
    for case_id in ids:
        r=cases[case_id];queued[case_id]={'caseId':case_id,'modelId':'MaterialsVariantsShoe','variant':r['variant'],'group':'fresh-ready-for-review','url':'/#'+urlencode({'model':r['modelId'],'variant':case_id}),'comparisonFingerprint':r['comparisonFingerprint'],'reviewGroupFingerprint':r['reviewGroupFingerprint'],'captureSha256':r['capture']['sha256'],'captureKind':'image','loadStatus':'passed','renderStatus':'passed','freshness':{'nativeRecaptureVerified':True,'fingerprintChanged':True},'check':guide['checks'][0],'limitations':' '.join(limits),'source':r['sourceUrl'],'evidence':r['evidence'],'savedReviewStillMatches':False,'savedReview':None,'relatedFeedback':[]}
    queue['queue']=list(queued.values());queue['summary']=dict(Counter(r['group']for r in queued.values()));write(DIST/'data.json',data);write(DIST/'review-queue.json',queue)
    report={'model':'MaterialsVariantsShoe','overallAcceptance':'incomplete','sourceMaterialViews':3,'defaultMidnightSeparateDuplicateReview':False,'canonicalCompiledViews':0,'totalCaptures':data['summary']['captures'],'totalCases':len(cases),'unchangedOtherRows':len(before['cases'])-1,'savedVerdictsUnchanged':True};write(ROOT/'Temp/gltf-validation/shoe-site-ingest.json',report);print(json.dumps(report))
if __name__=='__main__':ingest()
