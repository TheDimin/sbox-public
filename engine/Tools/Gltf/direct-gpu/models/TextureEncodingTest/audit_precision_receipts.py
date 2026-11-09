#!/usr/bin/env python3
"""Summarize already executed GPU receipts; no engine/GPU work or policy changes."""
import hashlib
import base64
import json
import math
import struct
from pathlib import Path

ROOT = Path(__file__).resolve().parents[6]
BASE = Path(__file__).resolve().parent
RECEIPTS = {
    'samples': ('c5c0-public-cadence-material-diagnostics-25', '165598de63db1add007c7ddedb19b5d4ecb7c95501302d5c117e68255ac20f7f'),
    'differential': ('c5c0-source-emission-and-precision-fixed-2', 'abd55378afb65e4263454cf010a286927bdfa04561ca91c31ba8fbc206b38712'),
}


def read_receipt(entry):
    folder, digest = entry
    path = ROOT/'Temp/gltf-validation/direct-gpu'/folder/'probe-01-editor_direct_gpu_tests/receipt-01.json'
    raw = path.read_bytes()
    assert hashlib.sha256(raw).hexdigest() == digest, path
    return json.loads(raw)


def encode(linear):
    return 12.92*linear if linear <= .0031308 else 1.055*linear**(1/2.4)-.055


def decode(encoded):
    return encoded/12.92 if encoded <= .04045 else ((encoded+.055)/1.055)**2.4


def float32(value):
    return struct.unpack('<f', struct.pack('<f', value))[0]


samples = read_receipt(RECEIPTS['samples'])
codes = {}
for measurement in samples['measurements']:
    if not measurement.get('suite', '').endswith('TextureEncodingGpuSamplingTests'):
        continue
    for row in measurement['value'] if isinstance(measurement.get('value'), list) else []:
        if isinstance(row, dict) and row.get('IsSrgb'):
            for expected, actual in zip(row['Expected'][:3], row['Actual'][:3]):
                code = round(255*encode(expected))
                actual = float32(actual)
                assert math.isfinite(actual) and 0 <= actual <= 1
                codes.setdefault(code, (expected, actual))

rows = []
for code, (expected, actual) in sorted(codes.items()):
    code_error = abs(255*encode(actual)-code) if 0 < code < 255 else abs(actual-code/255)
    lo, hi = (code/255, code/255) if code in (0, 255) else (decode((code-.5)/255), decode((code+.5)/255))
    rows.append({'encoded_byte': code, 'analytic_linear': expected, 'actual_linear': actual,
                 'absolute_linear_error': abs(actual-expected), 'absolute_encoded_byte_error': code_error,
                 'inside_d3d_srgb_to_float_bound': lo <= actual <= hi,
                 'd3d_allowed_linear_interval': [lo, hi]})
assert len(rows) == 11

# Two additional independent sampler Load(mip1) observations in the same
# verified receipt, after both native/static textures exposed all authored mips.
sampler_codes = []
for measurement in samples['measurements']:
    if not measurement.get('suite', '').endswith('TextureSettingsMipDiagnosticTests'):
        continue
    observations = [value for value in measurement['value'] if value.get('Phase') == 'gpu_observation' and value.get('RequestedFrames') == 64
                    and value.get('Name') in ('native_full_chain', 'explicit_full_chain_upload')]
    assert {value['Name'] for value in observations} == {'native_full_chain', 'explicit_full_chain_upload'}
    for value in observations:
        encoded = base64.b64decode(next(item['Encoded'] for item in value['NativeGetPixels'] if item['Mip'] == 1))
        assert encoded == bytes([160, 227, 160, 255])
        output = next(item['Actual'] for item in value['ShaderResults'] if item['Mode'] == 2)
        actual = [float32(float(item.strip())) for item in output.strip('<>').split(',')]
        assert actual == [.3515625, .76953125, .3515625, 1]
    for code, actual in ((160, .3515625), (227, .76953125)):
        expected = decode(code/255)
        sampler_codes.append({'encoded_byte': code, 'analytic_linear': expected, 'actual_linear': actual,
                              'absolute_linear_error': abs(actual-expected), 'absolute_encoded_byte_error': abs(255*encode(actual)-code),
                              'path': 'TextureSettingsMipDiagnosticTests Mode2 Load(mip1), native and explicit upload agree',
                              'native_encoded_bytes': [160, 227, 160, 255]})
assert len(sampler_codes) == 2

differential = read_receipt(RECEIPTS['differential'])
controls = []
for measurement in differential['measurements']:
    if measurement.get('suite', '').endswith('TextureEncodingPrecisionTests') and isinstance(measurement.get('value'), list):
        controls = [row for row in measurement['value'] if isinstance(row, dict) and 'Path' in row and 'Expected' in row]
assert len(controls) == 8
summary = {
    'scope': 'Read-only audit of historical actual GPU results. No modified test acceptance, producer or new GPU execution.',
    'receipt_sha256': {name: item[1] for name, item in RECEIPTS.items()},
    'actual_engine_mvid': differential['engine_mvid'], 'actual_gltf_mvid': differential['gltf_mvid'],
    'representative_code_count': len(rows), 'rows': rows, 'additional_sampler_codes': sampler_codes,
    'combined_distinct_code_count': len(rows) + len(sampler_codes), 'differential_controls': controls,
    'all_representative_values_inside_d3d_code_space_bound': all(row['inside_d3d_srgb_to_float_bound'] for row in rows),
    'maximum_absolute_linear_error': max(row['absolute_linear_error'] for row in rows),
    'maximum_absolute_encoded_byte_error': max(row['absolute_encoded_byte_error'] for row in rows),
    'classification_limit': 'D3D bound is not asserted to be a Vulkan normative guarantee. Actual host uses native sRGB read path; strict .001 result remains failed. No full256-code sweep was executed.',
}
output = (Path(__file__).resolve().parents[6]/'research/gltf-validation/direct-gpu/models/TextureEncodingTest') / 'precision-measurements.json'
output.write_text(json.dumps(summary, indent=2)+'\n')
print(json.dumps({'path': str(output), 'sha256': hashlib.sha256(output.read_bytes()).hexdigest(),
                  'codes': len(rows), 'maximum_linear_error': summary['maximum_absolute_linear_error'],
                  'maximum_code_error': summary['maximum_absolute_encoded_byte_error']}))
