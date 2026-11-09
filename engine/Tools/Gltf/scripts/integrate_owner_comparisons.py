#!/usr/bin/env python3
"""Integrate bounded, verified owner captures into the single main comparison flow.

No captures execute here. Each source receipt retains its own execution/provenance.
"""
import argparse
import copy
import hashlib
import io
import json
from pathlib import Path
import re
import shutil
import subprocess
from urllib.parse import urlencode
from PIL import Image
from export_current_comparison import public_setup, public_run
from visual_comparison_identity import comparison_identity, decoded_media, decoded_media_bytes
from group_visual_reviews import reference_pixels

ROOT = Path(__file__).resolve().parents[4]
SITE = ROOT / 'Temp/gltf-validation/comparison-site'
DIST = SITE / 'dist'
CORPUS = ROOT / 'Temp/gltf-reference/glTF-Sample-Assets'

def read(path): return json.loads(Path(path).read_text())
def sha(path): return hashlib.sha256(Path(path).read_bytes()).hexdigest()
def write(path, value): Path(path).write_text(json.dumps(value, ensure_ascii=False, indent=2)+'\n')
def media_path(row):
    cap = row.get('capture') or row.get('apng') or {}
    return (cap.get('path') if isinstance(cap, dict) else cap) or row.get('capturePath') or row.get('mediaPath')
def rows(doc): return doc.get('cases', doc.get('rows', doc.get('denseCaptures', [doc])))
def form_id(model, label):
    variant = re.search(r'glTF(?:-[A-Za-z]+)?', label)
    variant = variant.group() if variant else 'glTF'
    compiled = 'compiled' in label.lower()
    return f'{model}/{variant}', compiled

def public_producer(producer):
    return public_run({'producer': producer})['producer']

def migrate(data, feedback, prior_document):
    current = {c['id']: c for c in data['cases']}
    historical = {}
    def historic(commit):
        if commit not in historical:
            historical[commit] = json.loads(subprocess.check_output(['git','show',commit+':dist/data.json'],cwd=SITE))
        return historical[commit]
    migrated = []
    for saved in feedback['decisions']:
        row = current.get(saved['caseId'])
        if not row: continue
        document = historic(saved.get('siteCommit', feedback['latestReviewSiteCommit']))
        before = next((c for c in document['cases'] if c['id']==saved['caseId']),None)
        if not before or before['comparisonFingerprint'] != saved['fingerprint']: continue
        try:
            cap = before.get('capture')
            old_pixels = decoded_media_bytes(subprocess.check_output(['git','show',saved.get('siteCommit',feedback['latestReviewSiteCommit'])+':dist/'+cap['url']],cwd=SITE)) if cap else None
            ref = before.get('reference') or {}
            old_ref = reference_pixels(ref)
            equal = comparison_identity(before,old_pixels,old_ref)==row['comparisonFingerprint']
        except (OSError, ValueError, subprocess.CalledProcessError): equal=False
        if equal:
            row.setdefault('compatibleReviewFingerprints',[]).append(saved['fingerprint'])
            migrated.append({**saved,'fingerprint':row['comparisonFingerprint']})
    data['savedReviews'] = migrated
    # Browser-local decisions for the immediately preceding publication can migrate too.
    for before in prior_document['cases']:
        row=current.get(before['id'])
        if not row: continue
        cap=before.get('capture');ref=before.get('reference') or {}
        try:
            pixels=decoded_media(DIST/cap['url']) if cap else None
            reference=reference_pixels(ref)
            if comparison_identity(before,pixels,reference)==row['comparisonFingerprint']:
                row.setdefault('compatibleReviewFingerprints',[]).append(before['comparisonFingerprint'])
        except (OSError,ValueError): pass
    for row in data['cases']:
        row['compatibleReviewFingerprints']=sorted(set(row.get('compatibleReviewFingerprints',[])))
    for row in data['cases']:
        row['visualChanged']=bool(row.get('updated')) and not bool(row.get('compatibleReviewFingerprints'))
    return len(migrated)

def integrate():
    data=read(DIST/'data.json'); prior=json.loads(subprocess.check_output(['git','show','268253b4ddca9bd08f4ca5b5cd3cfe4cc7d5fd00:dist/data.json'],cwd=SITE)); latest=read(DIST/'latest.json')
    proof=read(ROOT/'Temp/gltf-validation/latest-gallery-publication-proof.json')
    if sha(DIST/'latest.json')!=proof['latestJsonSha256']: raise ValueError('Owner manifest hash differs')
    receipts={}
    for path,expected in proof['sourceReceipts'].items():
        if sha(path)!=expected: raise ValueError('Owner receipt changed: '+path)
        receipts[expected]=(Path(path),read(path))
    current={c['id']:c for c in data['cases']}; recipes={c['id']:c for c in read(ROOT/'research/gltf-validation/reference-recipes.json')['cases']}
    updated=[]
    for model in latest['models']:
        for capture in model['captures']:
            base_id,compiled=form_id(model['id'],capture['label']); case_id=base_id+('::compiled' if compiled else '')
            item=copy.deepcopy(current[base_id]);item['id']=case_id;item['sourceCaseId']=base_id
            item['variant']=base_id.split('/')[1]+(' → derived compiled GLB' if compiled else '')
            source_path,document=receipts[capture['proofSha256']]
            candidates=[]
            for summary in rows(document):
                path=media_path(summary)
                if not path or not Path(path).is_file() or sha(path)!=capture['sha256']: continue
                summary_id=summary.get('id',summary.get('case',''))
                summary_label=summary.get('label','')
                summary_compiled=summary.get('form')=='compiled' or summary_label=='compiled' or 'derived-compiled' in summary_id
                if summary_id and '/glTF' in summary_id and summary_id!=base_id:continue
                if summary_label.startswith('glTF') and not summary_label.startswith(base_id.split('/')[1]+'-'):continue
                if compiled!=summary_compiled and len(rows(document))>1:continue
                actual=summary
                candidate=Path(summary.get('resultPath',Path(path).parent/'result.json'))
                if candidate.is_file():
                    native=read(candidate)
                    if media_path(native) and sha(media_path(native))==capture['sha256']:actual=native
                    elif native.get('animationFrames'):
                        encoded=decoded_media(DIST/capture['url'])['frames']
                        recorded=native['animationFrames']
                        if len(encoded)!=len(recorded):raise ValueError('Encoded/source frame count differs: '+case_id)
                        for frame,expected in zip(recorded,encoded):
                            if sha(frame['path'])!=frame['sha256'] or decoded_media(frame['path'])['frames'][0]['pixels']!=expected['pixels']:raise ValueError('Encoded frame differs from native sample')
                        actual=native
                candidates.append((summary,actual))
            if not candidates: raise ValueError('No matching executed owner form: '+case_id)
            summary,actual=candidates[0]
            if sha(DIST/capture['url'])!=capture['sha256']:raise ValueError('Copied owner media differs')
            item['capture']={k:capture[k] for k in ('url','sha256','kind')}
            pixels=decoded_media(DIST/capture['url'])
            frame_count=len(pixels['frames']);item['capture']['frames']=frame_count
            item['capture']['duration']=sum(f['durationMs'] for f in pixels['frames'])/1000
            if frame_count>1:
                poster=DIST/'assets/latest'/('poster-'+capture['sha256'][:24]+'.png')
                with Image.open(DIST/capture['url']) as im:im.convert('RGBA').save(poster)
                item['capture']['posterUrl']=str(poster.relative_to(DIST));item['capture']['sampled']=True
                # Individual frames allow honest pause/step/scrub without pretending video/APNG can seek.
                frame_dir=DIST/'assets/frames'/capture['sha256'][:24];frame_dir.mkdir(parents=True,exist_ok=True)
                frame_urls=[]
                with Image.open(DIST/capture['url']) as im:
                    for i in range(frame_count):
                        im.seek(i);p=frame_dir/f'{i}.png'
                        if not p.exists():im.convert('RGBA').save(p,optimize=True)
                        frame_urls.append(str(p.relative_to(DIST)))
                item['capture']['frameUrls']=frame_urls;item['capture']['frameDurationsMs']=[f['durationMs'] for f in pixels['frames']]
            item['reference']=copy.deepcopy(model['reference']);item['recipe']=public_setup(actual.get('recipeSnapshot') or recipes[base_id])
            item['recipeApplied']=public_setup(actual.get('recipeApplied') or summary.get('recipeApplied'))
            if item['recipeApplied'] is None:
                item['recipeApplied']={'complete':False,'limitations':['Detailed applied setup is unavailable in this owner summary. '+model['limitations']]}
            item['load']={'status':'passed','error':None};item['render']={'status':'passed','error':None};item['reason']=None
            producer=actual.get('producer') or summary.get('producer') or document.get('producer')
            item['evidence']={'type':'isolated-model-execution','receiptSha256':capture['proofSha256'],
                'producer':public_producer(producer) if producer else {k:document[k] for k in ('engine_mvid','gltf_mvid','producerStage') if k in document},
                'sourceForm':base_id,'derivedCompiled':compiled,'scope':model['limitations']}
            item['whatChanged']=model['limitations'];item['updated']=True
            item['evidence']['nativeResultSha256']=sha(candidate) if candidate.is_file() else None
            item['evidence']['inputSha256']=(actual.get('input') or {}).get('sha256') if isinstance(actual.get('input'),dict) else actual.get('inputSha256')
            if item['reference'].get('animated'):
                refpath=CORPUS/item['reference']['path'];refdir=DIST/'assets/reference-frames'/item['reference']['sha256'][:24];refdir.mkdir(parents=True,exist_ok=True);urls=[];durations=[]
                with Image.open(refpath) as im:
                    for i in range(im.n_frames):
                        im.seek(i);p=refdir/f'{i}.png';im.convert('RGBA').save(p,optimize=True);urls.append(str(p.relative_to(DIST)));durations.append(im.info.get('duration',0))
                item['reference']['frameUrls']=urls;item['reference']['frameDurationsMs']=durations
            current[case_id]=item;updated.append(case_id)
    data['cases']=list(current.values())
    for item in data['cases']:
        cap=item.get('capture'); ref=item.get('reference') or {}
        cap_pixels=decoded_media(DIST/cap['url']) if cap else None
        reference=reference_pixels(ref)
        item['provenanceFingerprint']=hashlib.sha256(json.dumps(item.get('evidence') or {},sort_keys=True).encode()).hexdigest() if item.get('updated') else next((c.get('provenanceFingerprint',c.get('comparisonFingerprint')) for c in prior['cases'] if c['id']==item['id']),item.get('comparisonFingerprint'))
        item['comparisonFingerprint']=comparison_identity(item,cap_pixels,reference)
        item['visualIdentityVersion']=2
        if not item.get('updated'):item['evidence']={'type':'prior-corpus-execution','runId':prior['run']['id'],'scope':'Earlier corpus execution; producer and setup remain in the recorded run.'}
    data['evidenceCollection']={'type':'mixed-individual-executions','isolatedForms':len(updated),'isolatedModels':len(latest['models']),
        'scope':'Individually completed model executions plus earlier corpus results. This is not a combined batch run. Automatic capture success does not approve appearance.'}
    migrated=migrate(data,read(ROOT/'research/gltf-validation/user-feedback.json'),prior)
    data['reviewIdentityPolicy']='Decoded RGBA frames and frame timing, pinned reference pixels and meaningful review scope. Build IDs and container metadata do not request review.'
    data['summary']['captures']=sum(bool(c.get('capture')) for c in data['cases']);data['summary']['variants']=len(data['cases'])
    from group_visual_reviews import add_review_groups
    add_review_groups(data)
    write(DIST/'data.json',data)
    queue=[]
    saved={r['caseId']:r for r in data['savedReviews']}
    for row in data['cases']:
        decision=saved.get(row['id']);needs=bool(row.get('capture')) and decision is None
        queue.append({'caseId':row['id'],'modelId':row['modelId'],'variant':row['variant'],
            'group':'fresh-ready-for-review' if needs and row.get('visualChanged') else 'approved-current' if decision and decision['status']=='approved' else 'existing-unreviewed' if decision or row.get('capture') else 'blocked-or-not-run',
            'url':'/#'+urlencode({'model':row['modelId'],'variant':row['id']}),'comparisonFingerprint':row['comparisonFingerprint'],
            'loadStatus':row['load']['status'],'renderStatus':row['render']['status'],
            'freshness':{'nativeRecaptureVerified':row['evidence']['type']=='isolated-model-execution','fingerprintChanged':row.get('visualChanged',False)},
            'reviewGroupFingerprint':row.get('reviewGroupFingerprint'),
            'captureSha256':(row.get('capture') or {}).get('sha256'),'captureKind':(row.get('capture') or {}).get('kind'),
            'check':next(m for m in data['models'] if m['id']==row['modelId']).get('reviewGuidance',{}).get('checks',[''])[0],
            'limitations':row.get('whatChanged', ''),'source':row['sourceUrl'],'evidence':row['evidence'], 'savedReviewStillMatches':bool(decision),'savedReview':decision,'relatedFeedback':[]})
    from collections import Counter
    write(DIST/'review-queue.json',{'schemaVersion':2,'corpusPin':data['corpusPin'],'runId':prior['run']['id'],'runStatus':'mixed-executions','reviewsUnchanged':True,'publication':data['evidenceCollection']['scope'],'summary':dict(Counter(r['group'] for r in queue)),'queue':queue})
    report={'isolatedForms':len(updated),'models':len(latest['models']),'capturesInMain':data['summary']['captures'],'comparisonCount':len(data['cases']),'preservedSavedReviews':migrated,'updatedCases':updated}
    write(ROOT/'Temp/gltf-validation/main-comparison-integration.json',report);print(json.dumps(report))

if __name__=='__main__':integrate()
