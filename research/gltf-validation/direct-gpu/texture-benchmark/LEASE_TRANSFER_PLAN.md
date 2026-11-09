# Selected-packet texture lease transfer proposal

The current selected transaction prepares each selected lit material and calls
`GltfNativeTextureCache.AcquireBatch` for its role resources. It then calls
`AcquireSource`, which hashes and acquires the same resource bytes again for
each packet. The first set of leases remains alive until the transaction ends.
The warm diagnostic measured 810.74 ms for those second acquisitions of the
56 ABeautifulGame roles. This is a separate micro-measurement, not an asserted
end-to-end saving.

After the fresh-alias stage is validated and the adapter owner releases the
relevant methods, prepare one lease vector for **each currently missing packet
key** `(mesh, primitive, selected material)` in transaction order. Flatten the
vector's actual role/resource pairs into the existing batch acquisition, and
partition the returned independent leases back into vectors in exactly the
same order. Keep material preparation deduplicated by material index. The
packet acquisition takes its vector by exact packet key, verifies role count and
order against its own immutable prepared material, and transfers ownership to
`AcquireSource`. The adapter appends all transferred leases to its rollback
list before constructing bindings. It never treats a digest supplied by a
caller as trusted or uses a mutable-byte reference as a content identity.

Flattening every packet may hash more bytes than the current unique-material
prewarm. Measure both the role-vector count and total hashed bytes before and
after; do not assume the 810.74 ms diagnostic is the net saving. If duplicate
packets make this worse, add an owned cache-entry retain operation that forks
an existing **live** batch lease by ordinal and validates its owning context.
It must reject disposed leases and never infer content identity from mutable
byte-array references or an untrusted caller digest.

The transaction owns every vector not yet taken. Its scope disposes leftovers
on success and on failure; a packet/adapter owns every taken vector and releases
it through its existing context-scoped material lease. Duplicate roles or
primitives each receive separate reference-counted leases, even where they
share a native texture. Failed preparation, partial packet construction,
classification preflight failure, variant rollback, repeated state creation,
and disposal must each release exactly the leases they own while preserving
published packets and contexts. Unselected variants are not staged. Legacy and
unlit paths remain on their existing routes unless separately covered.

Before promotion, test multiple nodes sharing a primitive, two primitives
sharing a material, repeated roles sharing image bytes but using distinct
samplers, a mix of existing packet hits and new selected packets, a failed
later packet, failed preflight, independent instance variant states and
context-bound teardown. Run actual source and canonical compiled material/GPU
readbacks, including all mips and loaded-before-fallback content, and compare a
matched prepared-cache ABeautifulGame first-frame run. Measure peak process and
GPU memory where available; do not convert reduced hashing into a quality or
whole-model claim without that evidence. This is a plan, not applied code.
