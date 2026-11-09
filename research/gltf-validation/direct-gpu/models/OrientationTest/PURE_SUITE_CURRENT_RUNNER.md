# Current pure suite runner handoff

The new `engine/Tools/Gltf/editor/editor_gltf_current_tests.csx` is separate
from the historical runner and manifests. The coordinator owns request creation,
resource admission, editor dispatch and receipt review. This file is a source
handoff, not an execution claim.

The runner requires an explicit request at
`Temp/gltf-validation/direct-gpu/current-pure-tests.json`. Its exact 13-field
schema is `PURE_SUITE_CURRENT_REQUEST.schema.json`; extra or missing fields,
duplicate properties, noncanonical GUIDs, wrong counts and unsupported
selections are rejected. It admits only `selection="all"`, manifest version 8,
the reviewed current proposal path and **47 classes / 463 methods / 460
DataRows / 816 cases**, plus **three separate codec preflights**.

The coordinator supplies actual loaded Engine/glTF MVIDs and matching disk
SHA256 values, the MSTest binary hash, a fresh 32-hex request ID and the exact
runner/manifest hashes. For the intended c5c0 stage the MVIDs are Engine
`2c054bba-c0fa-43b5-a3a2-39a34ff7577f` and glTF
`c869761c-5b80-462d-982a-bc3a688e57b6`. Reverify them immediately before dispatch;
a saved identity is not authorization to run against a changed producer.

The script compiles only the 47 SHA256-pinned test files, embeds four pinned
KTX fixtures and preserves the historical pure runner's BCL + actually loaded
glTF + MSTest metadata references. Engine/global vector types are excluded from
the helper's metadata references. The helper resolves the real loaded glTF
producer, verifies MSTest identity and rejects replacement `Sandbox.Gltf`
types. Reflection independently verifies every class's method/DataRow/case
counts before executing any case.

The three native codec preflights are recorded outside the 816-case count.
Preflight failure marks affected project cases blocked; it cannot produce a
passing cohort. Setup, compilation, cancellation and discovery failures retain
the unexecuted portion of the 816 cases and three preflights as incomplete.
Nonfinite float/double DataRow arguments are recorded as invariant strings so
JSON receipt serialization cannot erase or abort their results.

At the end the runner rehashes the request, script, proposal, every test source,
every fixture and test framework; it rechecks source inventory and both loaded
and disk producer identities. A passing result requires that verification,
clean collectible-context cleanup, all 816 cases and all three preflights.
The receipt always keeps `complete_gltf_suite=false`: these CPU/codec cases do
not establish GPU, corpus, public API, editor or performance parity.

The script returns `receipt_path`; the complete receipt is written under
`Temp/gltf-validation/direct-gpu/current-pure-tests/<run-id>/receipt.json`.
Historical runner SHA256 remains
`7214059a401c317c0d1e59be88bc1c3fb778347f9b689e476051bf7d565ffd63`,
and historical manifest SHA256 remains
`e2682e555098cbe81dcb67c92cb46ef2ca47394b665f4aa480c3c9463657d2b0`.
