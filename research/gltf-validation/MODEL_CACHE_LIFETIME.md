# Model wrapper cache lifetime

The installed stage C failed two deterministic native regressions before the fix:
`DestroyedProceduralModelImmediatelyLeavesBothCanonicalIndexes` retained the
destroyed wrapper, and `RetainedNativeHandleCreatesAValidWrapperAfterOriginalDestruction`
returned that same invalid wrapper for a valid copied native handle.
The unchanged-producer receipt is SHA256
`33b47fbe081d0df4b9ddccdee6ae588b4d69485b63e4860736dcc55d7d45ce2f`.
All owned allocation preconditions held. Three controls passed: an already
registered replacement survived old-owner worker destruction, and raw/compiled
actual Model.Load completed four owned load/unload cycles each.

`Model.FromNative` canonicalizes by native binding address. It consumes an
incoming strong handle when it finds a cached wrapper. Ordinary Model.Destroy
previously invalidated its handle without removing that canonical cache entry;
the glTF-specific branch removed it later during queued native release. Thus a
cached dead wrapper could be returned and the incoming valid handle discarded.
This also explains why allocator reuse can affect unrelated procedural references
and later glTF construction, but no individual historical exception is relabeled
as proven by the deterministic test.

The stage D repair removes only the disposing model's current indexes before
clearing its native handle. A cache overload compares the captured WeakReference
atomically and removes retained values only when they still reference that exact
Model. Existing unconditional Remove(key) users retain their current behavior.
The ordinary Dictionary used by legacy Model.Loaded is changed only on the
main thread, with an exact-owner check during queued release. A newer wrapper at
the same address remains canonical and retains its own native strong handle.

Finalization may clear a short weak target before queued cleanup runs. The
main-thread legacy cleanup also removes a captured dead weak entry, while
preserving any live replacement. A sixth positive source regression explicitly
constructs that dead-index state without relying on GC timing. It was added
after the five-row historical red run and has not yet executed.

The key-only leak-tracking callstack table lacks an owner token. The conditional
model path leaves those diagnostics until their normal cache clear, rather than
deleting a newer allocation's record by address. Tracking-off creates no such
records. This is diagnostic metadata retention, not retention of native handles.

The glTF resource release order remains source document, native model, owned
mesh handles, then material leases. No global cache clear, allocator retry or
new Model.FromNative fallback is used by the repair.

The positive tests use IModel.CopyStrongHandle to exercise the exact same native
binding without relying on random allocator reuse. Replacement-owner control
destroys the older wrapper on a worker, awaits normally, and checks canonical
identity/native validity after main-thread queue processing and real frames.

Only the pre-fix red run is established here. The coordinator must build/install
stage D, run all six lifetime rows, then repeat the previously affected model
construction and authored-rest controls on the exact installed producer.
