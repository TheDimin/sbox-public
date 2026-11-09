#!/usr/bin/env python3
"""Freeze owned tests against a verified installed producer; does not dispatch."""
from pathlib import Path
import argparse,json,hashlib

parser=argparse.ArgumentParser()
parser.add_argument('kind',choices=['shader','reader','native','candidate','public','convergence','hdri'])
parser.add_argument('--canonical-compiled')
args=parser.parse_args()
root=Path(__file__).resolve().parents[6]
owned=Path(__file__).resolve().parent
ready=json.loads((root/'Temp/gltf-validation/editor-session-ready.json').read_text())
assert ready['ready']
modules={row['name']:row for row in ready['loaded']['modules']}
def digest(path):return hashlib.sha256(Path(path).read_bytes()).hexdigest()
request={'version':1,'engine_sha256':modules['Sandbox.Engine']['sha256'],'gltf_sha256':modules['Sandbox.Gltf']['sha256'],'tools_sha256':modules['Sandbox.Tools']['sha256'],'framework_sha256':digest(root/'engine/Tests/Sandbox.Test.Gltf/bin/Release/net10.0/Microsoft.VisualStudio.TestPlatform.TestFramework.dll')}
sources=['engine/Tools/Gltf/direct-gpu/GltfGpuTriangleGateTests.cs','engine/Tools/Gltf/direct-gpu/models/Fox/GltfGpuBackendContractTests.cs','engine/Tools/Gltf/direct-gpu/models/Fox/GltfGpuRawTriangleChecks.cs']
prefix='Sandbox.Gltf.DirectGpu.MandarinOrangeValidation.'
def suite(name,count,properties=None):
 return {'type':prefix+name,'kind':'mstest','expected_cases':count,'setup':[{'kind':'property','name':key,'arguments':[value]} for key,value in {'CheckoutRoot':'$checkout',**(properties or {})}.items()],'measurements_property':'Measurements'}
artifacts=[]
if args.kind=='shader':
 sources=[str((owned/'DiffuseTransmissionShaderPreflightTests.cs').relative_to(root))]
 request['suites']=[suite('DiffuseTransmissionShaderPreflightTests',1)]
 artifacts += [owned/'gltf_diffuse_transmission_candidate.shader']
elif args.kind in ['reader','native','candidate']:
 names=['DiffuseTransmissionReaderTests'] if args.kind=='reader' else ['DiffuseTransmissionCandidateTests'] if args.kind=='native' else ['DiffuseTransmissionReaderTests','DiffuseTransmissionCandidateTests']
 sources += [str((owned/(name+'.cs')).relative_to(root)) for name in names]
 request['suites']=[suite(name,17 if name=='DiffuseTransmissionReaderTests' else 1) for name in names]
 artifacts += [(owned if name.endswith(('.shader', '.cs')) else (Path(__file__).resolve().parents[6]/'research/gltf-validation/direct-gpu/models/MandarinOrange'))/name for name in ['gltf_diffuse_transmission_candidate.shader','diffuse-native-lambert-reference.shader','diffuse-transmission-plane.gltf','diffuse-transmission-role-control.png','diffuse-transmission-seven-uv-samplers.gltf','diffuse-transmission-oracle.json']]
 if args.kind in ['native','candidate']:
  validation=(Path(__file__).resolve().parents[6]/'research/gltf-validation/direct-gpu/models/MandarinOrange') / 'diffuse-positive-fixtures-validation.json'
  validated=json.loads(validation.read_text())
  assert validated['status']=='passed_core_only'
  assert validated['validator_sha256']=='5cba1e097935c9efd929d24a49610824259b030f0ae45672273bfbcbb5b87a62'
  for fixture in validated['fixtures']:
   path=root/fixture['path']
   assert digest(path)==fixture['sha256'] and fixture['numErrors']==0 and not fixture['truncated']
   artifacts.append(path)
  artifacts += [validation,owned/'validate_diffuse_fixtures.py']

elif args.kind=='hdri':
 sources += [str((owned/'MandarinHdriPresentationTests.cs').relative_to(root))]
 request['suites']=[suite('MandarinHdriPresentationTests',1)]
 artifacts += [root/name for name in ['game/core/shaders/gltf_diffuse_transmission.shader','game/core/shaders/gltf_diffuse_transmission.shader_c','game/core/materials/gltf_reference_environment/field.exr','game/core/materials/gltf_reference_environment/field.vtex','game/core/materials/gltf_reference_environment/field.vtex_c','Temp/gltf-reference/glTF-Sample-Assets/Models/MandarinOrange/screenshot/screenshot-large.jpg']]
elif args.kind=='convergence':
 sources += [str((owned/'DiffuseTransmissionConvergenceTests.cs').relative_to(root))]
 request['suites']=[suite('DiffuseTransmissionConvergenceTests',1)]
 artifacts += [root/'game/core/shaders/gltf_diffuse_transmission.shader',root/'game/core/shaders/gltf_diffuse_transmission.shader_c',root/'Temp/gltf-reference/glTF-Sample-Assets/Models/MandarinOrange/screenshot/screenshot-large.jpg']
else:
 assert args.canonical_compiled,'Public capture must name the single canonical compiled artifact'
 canonical=Path(args.canonical_compiled).resolve()
 assert canonical.is_relative_to(root),'Canonical artifact must be a reviewable checkout path'
 sources += [str((owned/name).relative_to(root)) for name in ['DiffuseTransmissionPublicTests.cs','DiffuseTransmissionCompatibilityTests.cs']]
 public=suite('DiffuseTransmissionPublicTests',2,{'CanonicalCompiledPath':str(canonical)})
 public['setup'].append({'kind':'method','name':'PrepareCanonicalCompiled','arguments':[]})
 request['suites']=[suite('DiffuseTransmissionCompatibilityTests',1),public]
 validation=(Path(__file__).resolve().parents[6]/'research/gltf-validation/direct-gpu/models/MandarinOrange') / 'diffuse-compatibility-fixtures-validation.json'
 validated=json.loads(validation.read_text())
 assert validated['status']=='passed_core_only'
 for fixture in validated['fixtures']:
  path=root/fixture['path']
  assert digest(path)==fixture['sha256'] and fixture['numErrors']==0 and not fixture['truncated']
  artifacts.append(path)
 artifacts += [validation,owned/'validate_diffuse_compatibility.py',root/'game/core/shaders/gltf_diffuse_transmission.shader',root/'game/core/shaders/gltf_surface_five_uv.shader']
 request['derived_artifact']={'path':str(canonical.relative_to(root)),'producer':'actual installed GltfCompiler.CompileWithEmbeddedImages in frozen suite setup','scope':'One canonical artifact; output digest is measured in receipt. Source inputs remain pinned by model-source audit. Automatic native asset-graph packaging remains separate.'}
request['sources']=[{'path':path,'sha256':digest(root/path)} for path in sources]
request['frozen_artifacts']=[{'path':str(path.relative_to(root)),'sha256':digest(path)} for path in artifacts]
path=(Path(__file__).resolve().parents[6]/'research/gltf-validation/direct-gpu/models/MandarinOrange')/('diffuse-'+args.kind+'-request.json');path.write_text(json.dumps(request,indent=2)+'\n')
print(json.dumps({'path':str(path),'sha256':digest(path),'expected_cases':sum(s['expected_cases'] for s in request['suites']),'dispatch':False,'requires':'fresh installed producer verified by coordinator; shared lock and host guard around run_owned_tests.py'}))
