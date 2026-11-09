#!/usr/bin/env python3
"""Run only inside the shared native-test flock and admitted host guard.

Example: flock /tmp/codex-sbox-gltf-model-tests.lock ~/.local/bin/codex-resource-guard run --budget-gib 4 -- python3 engine/Tools/Gltf/direct-gpu/texture-benchmark/run.py baseline
"""
import hashlib
import json
import shutil
import subprocess
import sys
import threading
import time
from pathlib import Path

root = Path(__file__).resolve().parents[5]
here = Path(__file__).resolve().parent
label = sys.argv[1]
attribution = len(sys.argv) > 2 and sys.argv[2] == 'attribution'
phases = len(sys.argv) > 2 and sys.argv[2] == 'phases'
batch = len(sys.argv) > 2 and sys.argv[2] == 'batch'
both = len(sys.argv) > 2 and sys.argv[2] == 'phases-batch'
candidate = len(sys.argv) > 2 and sys.argv[2] == 'candidate'
cache = len(sys.argv) > 2 and sys.argv[2] == 'cache'
retain_tests = len(sys.argv) > 2 and sys.argv[2] == 'retain-tests'
gpu = len(sys.argv) > 2 and sys.argv[2] == 'gpu'
public = len(sys.argv) > 2 and sys.argv[2] == 'public'
selected = len(sys.argv) > 2 and sys.argv[2] == 'selected'
selected_tests = len(sys.argv) > 2 and sys.argv[2] == 'selected-tests'
warm_attribution = len(sys.argv) > 2 and sys.argv[2] == 'warm-attribution'
warm_profile = len(sys.argv) > 2 and sys.argv[2] == 'warm-profile'
warm_profile_prepared = len(sys.argv) > 2 and sys.argv[2] == 'warm-profile-prepared'
no_invalidation = len(sys.argv) > 2 and sys.argv[2] == 'no-invalidation'
negative_recovery = len(sys.argv) > 2 and sys.argv[2] == 'negative-recovery'
canonical = len(sys.argv) > 2 and sys.argv[2] == 'canonical'
if not label.isascii() or not label.replace('-', '').isalnum():
    raise SystemExit('Use a simple unique run label')
output = root / 'Temp/gltf-validation/direct-gpu/texture-benchmark' / label
if output.exists():
    raise SystemExit('Output already exists; choose a new label')
ready = json.loads((root / 'Temp/gltf-validation/editor-session-ready.json').read_text())
host = json.loads(subprocess.check_output(['/home/damian/.local/bin/codex-resource-guard', 'status']))
editor = next(row for row in host['editors'] if row['pid'] == ready['pid'])
assert ready['ready'] and editor['start'] == ready['start'] and Path(editor['project']).resolve() == Path(ready['project']).resolve()
subprocess.run([str(root / 'game/bin/managed/sbox-cli'), '--connect', str(ready['pid']), '--call', 'editor_status'], cwd=root, check=True, timeout=30)
audit = json.loads((root / 'research/gltf-validation/direct-gpu/models/ABeautifulGame/source-audit.json').read_text())
def digest(path): return hashlib.sha256(path.read_bytes()).hexdigest()
def verify():
    for row in audit['files']:
        assert digest(root / row['path']) == row['sha256'], 'Pinned input changed: ' + row['path']
verify()
modules = {row['name']: row for row in ready['loaded']['modules']}
for row in modules.values():
    assert row['loadedMvid'] == row['diskMvid'] and digest(Path(row['path'])) == row['sha256'], 'Producer changed'
suite = 'BatchAcquisitionExperiment' if batch else 'TextureAcquisitionPhases' if phases else 'TextureAttribution' if attribution else 'BeautifulTextureBenchmark'
suites = ['RetainAdapterTests', 'BatchCacheTests', 'SelectedBatchTests'] if retain_tests else ['NegativeAliasRecovery'] if negative_recovery else ['NoInvalidationExperiment'] if no_invalidation else ['PresentationLightingTests'] if canonical else ['PreparedWarmup', 'WarmRenderedAttribution', 'WarmStageAttribution'] if warm_profile_prepared else ['WarmRenderedAttribution', 'WarmStageAttribution'] if warm_profile else ['WarmStageAttribution'] if warm_attribution else ['SelectedBatchTests', 'PreparedCacheTests'] if selected_tests else ['SelectedModelBenchmark'] if selected else ['PublicCompiledReadback'] if public else ['BatchGpuReadback'] if gpu else ['BatchCacheTests'] if cache else ['BatchCacheTests', 'BeautifulTextureBenchmark'] if candidate else ['TextureAcquisitionPhases', 'BatchAcquisitionExperiment'] if both else [suite]
sources = [root / 'engine/Tools/Gltf/direct-gpu/models/ABeautifulGame/PresentationLightingTests.cs'] if canonical else [here / (name + '.cs') for name in suites]
if selected or warm_profile or warm_profile_prepared:
    sources += [root / 'engine/Tools/Gltf/direct-gpu/GltfGpuTriangleGateTests.cs', root / 'engine/Tools/Gltf/direct-gpu/models/Fox/GltfGpuBackendContractTests.cs', root / 'engine/Tools/Gltf/direct-gpu/models/Fox/GltfGpuRawTriangleChecks.cs']
request = {
    'version': 1,
    'engine_sha256': modules['Sandbox.Engine']['sha256'],
    'gltf_sha256': modules['Sandbox.Gltf']['sha256'],
    'tools_sha256': modules['Sandbox.Tools']['sha256'],
    'framework_sha256': '81048167804a8d178ecd712dae394aca647bd1560c0406f3eabe2e596b465fad',
    'sources': [{'path': str(path.relative_to(root)), 'sha256': digest(path)} for path in sources],
    'suites': [{'type': ('Sandbox.Gltf.DirectGpu.ABeautifulGameValidation.' if canonical else 'Sandbox.Gltf.DirectGpu.TextureBenchmark.') + name, 'kind': 'mstest', 'expected_cases': 2 if name == 'BatchGpuReadback' else 1,
                **({} if name in ('SelectedBatchTests', 'PreparedCacheTests') else {'setup': [{'kind': 'method', 'name': 'Configure', 'arguments': ['$checkout']}]}), **({} if name in ('RetainAdapterTests', 'BatchCacheTests', 'SelectedBatchTests', 'PreparedCacheTests') else {'measurements_property': 'Measurements'})} for name in suites]
}
output.mkdir(parents=True)
(output / 'request.json').write_text(json.dumps(request, indent=2) + '\n')
(output / 'session.json').write_text(json.dumps(ready, indent=2) + '\n')
shutil.copyfile(output / 'request.json', root / 'Temp/gltf-validation/direct-gpu/current-tests.json')
stop = threading.Event()
rss = []
def sample_rss():
    while not stop.is_set():
        try:
            status = Path('/proc') / str(ready['pid']) / 'status'
            for line in status.read_text().splitlines():
                if line.startswith('VmRSS:'):
                    rss.append({'monotonic': time.monotonic(), 'bytes': int(line.split()[1]) * 1024})
                    break
        except (OSError, ValueError):
            pass
        stop.wait(0.2)
monitor = threading.Thread(target=sample_rss)
monitor.start()
try:
    code = subprocess.call([sys.executable, str(root / 'engine/Tools/Gltf/scripts/run_editor_probes.py'), 'editor_direct_gpu_tests.csx', '--pid', str(ready['pid']), '--project', ready['project'], '--output', str(output / 'probe'), '--timeout', '1200', '--confirmed-discovery', ready['discoveryPath'], '--confirmed-sha256', ready['discoverySha256']], cwd=root)
finally:
    stop.set()
    monitor.join()
    (output / 'rss-samples.json').write_text(json.dumps({'interval_seconds': .2, 'editor_pid': ready['pid'], 'samples': rss}, indent=2) + '\n')
verify()
receipts = list(output.rglob('receipt-*.json'))
if len(receipts) != 1: raise SystemExit('Expected one benchmark receipt')
receipt = json.loads(receipts[0].read_text())
print(json.dumps({'receipt': str(receipts[0]), 'sha256': digest(receipts[0]), 'passed': receipt.get('passed'), 'error': receipt.get('error'), 'measurements': receipt.get('measurements')}, indent=2))
raise SystemExit(code or (0 if receipt.get('passed') else 1))
