"""Reproduce the bounded source audit. No C# producer or GPU execution."""
import json
import math

from author_reference import HERE, PIN, build, morphed


def audit():
    r = build()
    report = {'revision': PIN, 'upstream_forms': [f['path'] for f in r['forms']], 'mesh': 0, 'node': 0,
              'target_count': 8, 'target_names': r['targetNames'], 'mesh_weights': r['meshWeights'],
              'node_weights': r['nodeWeights'], 'sparse_accessors': 0, 'interleaved_accessors': 0,
              'authored_tangents': False, 'attributes': ['POSITION', 'NORMAL', 'TEXCOORD_0', 'TEXCOORD_1'],
              'primitives': [], 'clips': [], 'normal_definedness': []}
    for p, primitive in enumerate(r['primitives']):
        targets = []
        for t, target in enumerate(primitive['targets']):
            targets.append({'target': t, 'POSITION_accessor': target['POSITION'], 'NORMAL_accessor': target['NORMAL'],
                            'nonzero_position_vertices': sum(any(c != 0 for c in v) for v in r['values'][str(target['POSITION'])]),
                            'nonzero_normal_vertices': sum(any(c != 0 for c in v) for v in r['values'][str(target['NORMAL'])])})
        report['primitives'].append({'primitive': p, 'vertices': primitive['vertexCount'],
                                     'indices': len(r['values'][str(primitive['indices'])]), 'material': primitive['material'], 'targets': targets})
    for c in r['clips']:
        report['clips'].append({'name': c['name'], 'key_count': len(c['times']), 'first_key': c['times'][0],
                                'last_key': c['times'][-1], 'interpolation': c['interpolation'],
                                'minimum_weight': min(min(w) for w in c['weights']), 'maximum_weight': max(max(w) for w in c['weights'])})
    for name, w in [('rest', [0] * 8), ('all_one', [1] * 8), ('signed_eight', r['signedWeights']),
                    ('ninth_mesh_default', [0] * 8 + [1.75]), ('ninth_signed_override', r['signedWeights'] + [-.375])]:
        lengths = [math.sqrt(sum(c * c for c in morphed(r, p, v, w, 'NORMAL')))
                   for p in range(2) for v in range(r['primitives'][p]['vertexCount'])]
        report['normal_definedness'].append({'state': name, 'vertices': len(lengths),
                                             'minimum_morphed_normal_length': min(lengths),
                                             'zero_normal_directions': sum(x <= 1e-12 for x in lengths)})
    report['evidence_scope'] = 'Independent standard-library source audit only; compiled producer data, C# assertions, device readback, image, wall-clock and performance gates unrun.'
    return report


if __name__ == '__main__':
    (HERE / 'source-audit.json').write_text(json.dumps(audit(), indent=2) + '\n')
