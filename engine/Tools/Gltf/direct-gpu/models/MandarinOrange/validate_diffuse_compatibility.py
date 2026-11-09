#!/usr/bin/env python3
"""Pinned core preflight for exact factor-zero compatibility inputs only."""
from pathlib import Path
import copy,json,sys
owned=Path(__file__).resolve().parent
checkout=owned.parents[5]
sys.path.insert(0,str(checkout/'engine/Tools/Gltf/scripts'))
import validator_adapter as adapter
base=json.loads(((Path(__file__).resolve().parents[6]/'research/gltf-validation/direct-gpu/models/MandarinOrange') / 'diffuse-transmission-plane.gltf').read_text())
output=(Path(__file__).resolve().parents[6]/'research/gltf-validation/direct-gpu/models/MandarinOrange') / 'diffuse-compatibility-fixtures';output.mkdir(exist_ok=True)
reports=checkout/'Temp/gltf-validation/direct-gpu/mandarin-diffuse-compatibility-validation';reports.mkdir(parents=True,exist_ok=True)
binary=checkout/'Temp/gltf-validation/validator/gltf_validator'
digest=adapter.verify_validator(binary)
rows=[]
for roughness in (0,.5,1):
 for transmission in (False,True):
  root=copy.deepcopy(base);material=root['materials'][0]
  material['pbrMetallicRoughness']['roughnessFactor']=roughness
  if transmission:material['extensions']['KHR_materials_diffuse_transmission']['diffuseTransmissionFactor']=0
  else:material.pop('extensions');root.pop('extensionsUsed')
  name=('transmission_zero' if transmission else 'ordinary_core')+'_'+str(roughness).replace('.','_')
  path=output/(name+'.gltf');path.write_text(json.dumps(root,indent=2)+'\n')
  result=adapter.run_validator(binary,path)
  report=reports/(name+'.json');report.write_text(json.dumps(result['report'],indent=2)+'\n')
  rows.append({'name':name,'path':str(path.relative_to(checkout)),'sha256':adapter.sha256_file(path),'numErrors':result['issues']['numErrors'],'truncated':result['issues']['truncated'],'report':str(report.relative_to(checkout)),'report_sha256':adapter.sha256_file(report),'messages':result['issues']['messages']})
manifest={'status':'passed_core_only' if all(row['numErrors']==0 and not row['truncated'] for row in rows) else 'failed','validator_version':adapter.VALIDATOR_VERSION,'validator_sha256':digest,'scope':'Six exact positive inputs for native zero-factor versus ordinary core source-shader roughness comparison. DT unsupported notices retained; no native/extension acceptance.','fixtures':rows}
path=(Path(__file__).resolve().parents[6]/'research/gltf-validation/direct-gpu/models/MandarinOrange') / 'diffuse-compatibility-fixtures-validation.json';path.write_text(json.dumps(manifest,indent=2)+'\n')
print(json.dumps({'status':manifest['status'],'positive_inputs':len(rows),'core_errors':sum(row['numErrors'] for row in rows),'manifest':str(path)}))
sys.exit(0 if manifest['status']=='passed_core_only' else 1)
