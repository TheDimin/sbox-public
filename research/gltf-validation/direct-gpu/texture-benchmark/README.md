# ABeautifulGame texture loading benchmark

The pinned `glTF-Binary/ABeautifulGame.glb` is read and verified before the
three iterations. Each iteration parses the same bytes, prepares the fifteen
source materials through the loaded engine's `PrepareSource`, and then times
public `Model.Load` and its material readiness using a new temporary alias. The
model and mount are destroyed before the next iteration. The input file is
already in the OS cache; resource identities are fresh. This is neither a cold
OS-cache test nor a persistent process-cache hit benchmark. No user cache is
cleared. Separate first-material sRGB color and linear normal compilations run
before preparation as warm-up probes; their encoded image reads are included in
`image_read_ms`, but precede `prepare_ms`. Other image reads occur during
preparation. Hashing output resources is outside preparation time. Allocations
are process-wide managed bytes accumulated during preparation and hashing; working set is a point sample after load, not
a peak. Queue, request compilation, input copy, hashing and cleanup are not
included in load or preparation timing. Resource hashes identify the serialized
outputs, including shared roles, and permit exact before/after checks.

Run only once the integration owner confirms the loaded producer is stable:

```sh
flock /tmp/codex-sbox-gltf-model-tests.lock \
  ~/.local/bin/codex-resource-guard run --budget-gib 4 -- \
  python3 engine/Tools/Gltf/direct-gpu/texture-benchmark/run.py LABEL
```

The 4 GiB additional budget follows the prior guarded ABeautifulGame capture;
reassess it if the workload changes. The runner refuses an existing label,
checks the guarded editor identity and project, requests `editor_status`,
verifies the full asset audit and loaded/disk assembly hashes, freezes the
request, and keeps raw results in `Temp/gltf-validation/direct-gpu/texture-benchmark/LABEL`.
The lease must encompass the runner and receipt inspection. A marker is a
locator, not proof of live producer identity; the editor probe verifies actual
loaded assemblies. A guard refusal is a pending run, not a reason to lower the
budget or launch another editor. Compare only matching inputs, runner, hardware,
and relevant producer changes; report variation and separate functional GPU
readback from these timing measurements.
