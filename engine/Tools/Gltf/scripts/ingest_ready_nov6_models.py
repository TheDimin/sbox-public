#!/usr/bin/env python3
"""Bounded ingest of reviewed owner manifests; never executes engine tests."""
import copy,json,shutil
from collections import Counter
from urllib.parse import urlencode
from ingest_completed_model import ROOT,DIST,CORPUS,read,write,verified,sha
from visual_comparison_identity import comparison_identity,decoded_media,digest
from group_visual_reviews import group_identity

def main():
 data=read(DIST/'data.json');before=copy.deepcopy(data);cases={r['id']:r for r in data['cases']};changed=[]
 for name,expected in [('ABeautifulGame','87ff8ebc3692e9254f8bb145e438a916ff4e1692c12eb45736d77e4f4d43fb24'),('MeshoptCubeTest','45cf33f9b6032544c2cf489d83ac68b0e84ab57d5d213e5c928da540f9c6aac8')]:
  owner=read(verified(ROOT/f'research/gltf-validation/direct-gpu/models/{name}/result.json',expected));base=cases[name+('/glTF'if name=='ABeautifulGame'else'/glTF-Meshopt')];ref=base['reference'];refpixels=decoded_media(verified(CORPUS/ref['path'],ref['sha256']))
  if name=='ABeautifulGame':
   forms=owner['forms'];proofs=owner['evidence'][-2:];limits=owner['limitations'];notice='Core-material fallback: optional transmission/volume are not reproduced. Camera/ambient/directional/point lighting are inferred; upstream HDRI, ACES and +2EV are absent. Judge silhouettes and core surface rendering; appearance approval is pending.';camera={'recipe':'inferred close camera; constant ambient/directional/point lighting; no upstream HDRI/ACES/+2EV'}
  else:
   forms=[dict(owner['canonical_compiled'],name=owner['canonical_compiled']['form'],capture=owner['captures'][0])];proofs=owner['receipts'][-2:];limits=owner['limitations']+['1024px inferred oblique camera/light; darker than upstream. Still after two actual playback loops, not a cadence-matched animation. Human appearance approval is pending.'];notice='One canonical compiled asset. Compare all25cubes and10labels; lighting is darker and camera/exposure inferred. Two ordinary playback loops and five GPU pose samples passed; this still does not demonstrate matched animation cadence or full PBR conformance.';camera=owner['presentation_recipe']
  receipts=[]
  for p in proofs:
   h=p.get('receipt_sha256',p.get('sha256'));receipt=read(verified(p.get('receipt',p.get('path')),h));assert not receipt.get('cleanup_errors') and not receipt.get('incomplete_cases');receipts.append((h,receipt))
  for i,f in enumerate(forms):
   cap=f['capture'];image=verified(cap['path'],cap['sha256']);target=DIST/'assets/completed'/f"{cap['sha256']}.png";target.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(image,target);pixels=decoded_media(target);assert pixels['frames'][0]['size']==[cap['width'],cap['height']]
   derived=bool(f.get('derived'));sourcevariant=f.get('actual_upstream_variant','glTF-Meshopt');caseid=name+'/'+sourcevariant+('::canonical-compiled'if derived else'');row=copy.deepcopy(cases.get(name+'/'+sourcevariant,base));h,receipt=receipts[1 if name=='ABeautifulGame'and i==2 else 0]
   applied={'camera':camera,'complete':True,'limitations':limits}
   row.update(id=caseid,sourceCaseId=name+'/'+sourcevariant,variant=('Canonical compiled .glb_c'if derived else sourcevariant+' source'),capture={'url':str(target.relative_to(DIST)),'sha256':cap['sha256'],'kind':'image','frames':1,'duration':0},recipeApplied=applied,recipe={'camera':camera,'matchStatus':'needs-tuning','provenance':'Actual owner public execution; inferred presentation','limitations':limits},referenceNotice=notice,load={'status':'passed','error':None},render={'status':'passed','error':None},reason=None,visualReview={'status':'pending','reviewer':None},updated=True,visualChanged=True,compatibleReviewFingerprints=[],visualIdentityVersion=2)
   row['evidence']={'type':'isolated-model-execution','sourceForm':name+'/'+sourcevariant,'derivedCompiled':derived,'canonicalCompiled':derived,'manifestSha256':expected,'receiptSha256':h,'supplementalReceiptSha256':[x[0]for x in receipts],'producer':{'engine_mvid':receipt['engine_mvid'],'gltf_mvid':receipt['gltf_mvid']},'inputSha256':f['input_sha256'],'scope':f.get('purpose',owner.get('acceptance_scope')),'limitations':limits}
   row['comparisonFingerprint']=comparison_identity(row,pixels,refpixels);row['reviewGroupFingerprint']=group_identity(row,pixels,refpixels);row['provenanceFingerprint']=digest(row['evidence']);row['whatChanged']=notice
   row['coverageRole']='canonical-compiled'if derived else'distinct-source'if i==2 else'identical-source-proof';cases[caseid]=row;changed.append(caseid)
  if name=='ABeautifulGame':
   assert cases[name+'/glTF']['reviewGroupFingerprint']==cases[name+'/glTF::canonical-compiled']['reviewGroupFingerprint']
   model=next(m for m in data['models']if m['id']==name);model['reviewGuidance']['checks']=['Compare chess-piece silhouettes and core surface textures; optional transmission/volume use fallback and cannot be accepted from this view.'];model['reviewGuidance']['limits']=notice;model['attributionNotice']=owner['attribution']
 for old in before['cases']:
  if old['id']not in changed:assert cases[old['id']]==old
 assert data['savedReviews']==before['savedReviews']
 data['cases']=list(cases.values());isolated=[r for r in cases.values()if r.get('evidence',{}).get('type')=='isolated-model-execution'];data['evidenceCollection'].update(isolatedForms=len(isolated),isolatedModels=len({r['modelId']for r in isolated}));data['summary'].update(captures=sum(bool(r.get('capture'))for r in cases.values()),variants=len(cases),load=dict(Counter(r['load']['status']for r in cases.values())),render=dict(Counter(r['render']['status']for r in cases.values())))
 queue=read(DIST/'review-queue.json');queued={r['caseId']:r for r in queue['queue']}
 for caseid in changed:
  r=cases[caseid];guide=next(m for m in data['models']if m['id']==r['modelId'])['reviewGuidance'];queued[caseid]={'caseId':caseid,'modelId':r['modelId'],'variant':r['variant'],'group':'fresh-ready-for-review','url':'/#'+urlencode({'model':r['modelId'],'variant':caseid}),'comparisonFingerprint':r['comparisonFingerprint'],'reviewGroupFingerprint':r['reviewGroupFingerprint'],'captureSha256':r['capture']['sha256'],'captureKind':'image','loadStatus':'passed','renderStatus':'passed','freshness':{'nativeRecaptureVerified':True,'fingerprintChanged':True},'check':guide['checks'][0],'limitations':' '.join(r['recipeApplied']['limitations']),'source':r['sourceUrl'],'evidence':r['evidence'],'savedReviewStillMatches':False,'savedReview':None,'relatedFeedback':[]}
 queue['queue']=list(queued.values());queue['summary']=dict(Counter(r['group']for r in queued.values()));write(DIST/'data.json',data);write(DIST/'review-queue.json',queue)
 report={'changedCases':changed,'newVisualGroups':len({cases[x]['reviewGroupFingerprint']for x in changed}),'totalCaptures':data['summary']['captures'],'totalCases':len(cases),'unchangedExistingCases':len(before['cases'])-sum(x in {r['id']for r in before['cases']}for x in changed),'savedVerdictsUnchanged':True,'newestFeedbackProcessed':False};write(ROOT/'Temp/gltf-validation/ready-models-site-ingest.json',report);print(json.dumps(report))
if __name__=='__main__':main()
