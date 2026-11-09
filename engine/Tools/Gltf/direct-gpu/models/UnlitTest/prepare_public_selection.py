"""Freeze this model's public probes against the newly verified installed producer.
This only writes model-local files; execution/request copy belongs inside the host guard+lock.
"""
from pathlib import Path
import hashlib
import json
import sys

root = Path(__file__).resolve().parents[6]
owned = Path(__file__).resolve().parent
ready = json.loads((root / 'Temp/gltf-validation/editor-session-ready.json').read_text())
assert ready['ready'] is True
h = lambda p: hashlib.sha256(Path(p).read_bytes()).hexdigest()
out = sys.argv[1] if len(sys.argv) > 1 else 'today-unlit-installed-images-01'
config = json.loads(((Path(__file__).resolve().parents[6]/'research/gltf-validation/direct-gpu/models/UnlitTest') / 'today-source-config-04.json').read_text())
config['OutputDirectory'] = str(root / 'Temp/gltf-validation/direct-gpu' / out)
config['PublicSource'] = True
config['SourceShaderSha256'] = h(owned / 'gltf_unlit_five_uv.shader')
config_path = (Path(__file__).resolve().parents[6]/'research/gltf-validation/direct-gpu/models/UnlitTest') / 'today-public-config.json'
config_path.write_text(json.dumps(config, indent=2) + '\n')
selection = {'version': 1}
for name, key in [('Sandbox.Engine', 'engine'), ('Sandbox.Gltf', 'gltf'), ('Sandbox.Tools', 'tools')]:
    selection[key + '_sha256'] = next(m['sha256'] for m in ready['loaded']['modules'] if m['name'] == name)
selection['framework_sha256'] = json.loads(((Path(__file__).resolve().parents[6]/'research/gltf-validation/direct-gpu/models/UnlitTest') / 'today-baseline-selection.json').read_text())['framework_sha256']
names = ['UnlitTestReference.cs', 'UnlitTestLiteralGeometry.cs', 'UnlitTestCoreTests.cs', 'UnlitTestGpuChecks.cs', 'UnlitTestImageChecks.cs', 'UnlitTestGraphCandidateGate.cs', 'UnlitTestGpuProbe.cs', 'UnlitFeatureFixtures.cs', 'UnlitFeatureRenderingTests.cs', 'UnlitFocusedFeatureExecution.cs', 'UnlitPublicMount.cs', 'UnlitPublicRenderingTests.cs', 'UnlitTestPublicLoadTests.cs', 'UnlitPublicSelectionTests.cs']
selection['sources'] = [{'path': str((owned/n).relative_to(root)), 'sha256': h(owned/n)} for n in names]
ns = 'Sandbox.Gltf.DirectGpu.UnlitTestValidation.'
def suite(name, count, setup):
    return {'type': ns+name, 'kind':'mstest', 'expected_cases':count, 'setup':setup}
configure = [{'kind':'method', 'name':'Configure', 'arguments':['$checkout',str(config_path)]}]
selection['suites'] = [suite('UnlitPublicCaptureExecution',1,configure),suite('UnlitFocusedFeatureExecution',1,configure),suite('UnlitTestPublicLoadTests',4,[{'kind':'property','name':'CheckoutRoot','arguments':['$checkout']}]),suite('UnlitPublicSelectionTests',6,[{'kind':'property','name':'CheckoutRoot','arguments':['$checkout']}])]
for s in selection['suites'][:2]: s['measurements_property'] = 'Measurements'
path = (Path(__file__).resolve().parents[6]/'research/gltf-validation/direct-gpu/models/UnlitTest') / 'today-public-selection.json'
path.write_text(json.dumps(selection,indent=2)+'\n')
print(json.dumps({'selection':str(path),'sha256':h(path),'cases':12,'producer':selection['engine_sha256'],'ready_pid':ready['pid']}))
