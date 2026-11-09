#!/usr/bin/env python3
"""Independent 1 cd finite-range receiver expectations in glTF metres."""
import json
import math

def illuminance(intensity, distance, radius):
    assert intensity >= 0 and distance > 0 and radius > 0
    return intensity * max(1 - (distance / radius) ** 4, 0) / distance ** 2

distances = [.5, 1., 2., 4., 5.]
expected = [illuminance(1., d, 4.) for d in distances]
assert expected == [3.9990234375, .99609375, .234375, 0., 0.]
assert all(math.isclose(illuminance(2., d, 4.), 2 * e) for d,e in zip(distances, expected))
assert illuminance(0, 1., 4.) == 0
print(json.dumps({'meters':distances,'range_m':4,'one_candela_illuminance_lux':expected,'unit_lambert_radiance': [v/math.pi for v in expected], 'scope':'Independent recommended finite-range formula and unit Lambert reference; no engine execution.'},indent=2))
