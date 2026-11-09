# Mixed-pass callback counter triage

The stage485 original mixed test failed before its first GPU position readback.
Receipt `f7aa221b0116150b48bd3b6cff4424b3516e4550b7094ffdfb26147c5d2dd2ef`
retains that failure; original helper inputs are archived by the coordinator.
An instrumentation-only rerun reproduced it. Snapshot JSON key collision prevented
normal export; the coordinator recovered the exact loaded helper without rerunning
any test. Recovery SHA256 is
`836c47854c425de75168cafb913428c7d86eb3b4875f499f86e227beec224da7`.

The 61 transcript events cover 30 callbacks (10 each opaque, depth and translucent),
six worker threads and two native views, with no dropped/instrumentation-error rows.
Every callback selects exactly the expected source row. Cached and native material
classes agree. Every selected row increments its draw count by exactly one.
Eleven global-counter windows increment by two because another class runs on a
parallel worker. In frame129134, translucent worker51 and depth worker115 both
start with global count0; each row submits once, both rows reach revision2, and the
global count reaches2. Aggregate revision2 is correct before the second wrapper
updates its local `seen` collection. No recorded aggregate-current snapshot has a
stale required row. The prior global-delta and delayed-seen assertions are invalid
for this measured native execution pattern.

Root approved changing only the helper oracle: require actual selected row identity,
material class, one selected-row draw, and aggregate completion against the actual
row revisions. Preserve independent GPU positions at three times, the 1e-4 inch
limit, unchanged-override readiness, deletion controls and empty readiness. Retain
the transcript. Explicitly flag same-row overlap as unresolved counter attribution;
it is not covered by the observed distinct-row overlap and is not silently accepted.
No production locks, callback serialization or producer changes are part of this
correction. General shared-context concurrency safety remains a separate question.
