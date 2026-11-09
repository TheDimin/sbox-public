#!/usr/bin/env python3
"""Merge authenticated owner rows without changing source timestamps or fingerprints."""
import hashlib,json,collections
from pathlib import Path
ROOT=Path(__file__).resolve().parents[4];source=ROOT/'Temp/gltf-validation/private-review-latest-observed.json';raw=json.loads(source.read_text());rows=[]
for page in raw['pages']:
 d=page['structuredContent'];assert not d['model_projection']['truncated_values'];rows +=[json.loads(r['raw_json'])for r in d['rows']]
assert len({(r['caseId'],r['fingerprint'])for r in rows})==len(rows)
p=ROOT/'research/gltf-validation/user-feedback.json';original=p.read_bytes();old=json.loads(original);backup=ROOT/'Temp/gltf-validation/user-feedback-before-owner-sync.json'
if not backup.exists():backup.write_bytes(original)
baseline=json.loads(backup.read_text())
fields=['caseId','fingerprint','status','notes','reviewedAt','reviewer']
def identity(r):return tuple(r.get(k)for k in fields)
oldids={tuple(r.get(k)for k in fields if k!='fingerprint')for r in baseline['decisions']};new=[r for r in rows if tuple(r.get(k)for k in fields if k!='fingerprint')not in oldids];current={r['caseId']:r for r in old['decisions']};history=list(old.get('previousDecisions',[]))
for record in rows:
 prior=current.get(record['caseId'])
 if prior and identity(prior)!=identity(record):
  if (record.get('reviewedAt')or'')>=(prior.get('reviewedAt')or''):history.append(prior);current[record['caseId']]=record
  else:history.append(record)
 elif not prior:current[record['caseId']]=record
seen=set();history=[r for r in history if not(identity(r)in seen or seen.add(identity(r)))];old['decisions']=list(current.values());old['previousDecisions']=history;old['lastAuthenticatedReviewSync']={'source':str(source.relative_to(ROOT)),'sha256':hashlib.sha256(source.read_bytes()).hexdigest(),'observedAt':raw['observedAt'],'siteVersion':21,'recordsObserved':len(rows),'latestOriginalReviewTimestamp':max(r.get('reviewedAt','')for r in rows),'snapshotConsistency':'Bounded live pages; user can continue reviewing. Original timestamps/fingerprints preserved.'};p.write_text(json.dumps(old,ensure_ascii=False,indent=2)+'\n')
data=json.loads((ROOT/'Temp/gltf-validation/comparison-site/dist/data.json').read_text());cases={r['id']:r for r in data['cases']};matches=[r for r in rows if r['caseId']in cases and cases[r['caseId']]['comparisonFingerprint']==r['fingerprint']];groups={r.get('groupFingerprint')or cases[r['caseId']].get('reviewGroupFingerprint')or r['caseId']:r for r in matches};newgroups={r.get('groupFingerprint')or cases[r['caseId']].get('reviewGroupFingerprint')or r['caseId']:r for r in new if r in matches};comments={r.get('groupFingerprint')or r['caseId']:r for r in new if r.get('notes')};report={'sourceRows':len(rows),'newOrChangedFormDecisions':len(new),'newFormStatuses':dict(collections.Counter(r['status']for r in new)),'newOrChangedVisualGroups':len(newgroups),'newVisualGroupStatuses':dict(collections.Counter(r['status']for r in newgroups.values())),'newNonemptyCommentGroups':len(comments),'changedModelGroups':sorted({r['caseId'].split('/')[0]for r in new}),'matchingCurrentForms':len(matches),'currentVisualGroupStatuses':dict(collections.Counter(r['status']for r in groups.values())),'stableDecisions':len(current),'preservedHistoricalDecisions':len(history),'observedLatestReviewTimestamp':max(r.get('reviewedAt','')for r in rows),'comments':[{'model':r['caseId'].split('/')[0],'caseId':r['caseId'],'fingerprint':r['fingerprint'],'status':r['status'],'notes':r['notes'],'reviewedAt':r['reviewedAt']}for r in comments.values()]};(ROOT/'Temp/gltf-validation/private-review-sync-report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n');print(json.dumps({k:v for k,v in report.items()if k!='comments'}))
if __name__=='__main__':pass
