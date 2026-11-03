# Resource GUIDs in game code

An asset-backed `Resource` can expose its identity through `resource.Guid`. Save a
non-empty GUID to key data such as statistics, and resolve a currently indexed
resource with `ResourceLibrary.Get<T>(guid)` or `TryGet<T>(guid, out var resource)`.
These APIs are available in game and editor code and should be used on the main thread.

```csharp
Guid id = ability.Guid;
if ( id != Guid.Empty && ResourceLibrary.TryGet<AbilityResource>( id, out var foundAbility ) )
{
    // Use the indexed ability. Its data may still be pending if it is a promise.
}
```

`Guid.Empty` means no GUID is currently known: for example, a procedural or embedded
resource may have no asset identity, and an identity may only become known later in
the load lifecycle. Do not save `Guid.Empty` as an asset key. A GUID may also be
temporarily assigned when conflicting assets have the same GUID; fix the collision
before relying on it as persistent identity.

GUID lookup searches the current resource system's in-memory indexes only. It returns
null (or false) for an empty, unknown, unavailable, collected, or wrong-type entry;
it does not discover, mount, or load an asset. A found game resource can be a promise
whose data is still pending, and a found native resource can later leave the weak
index when nothing keeps it alive. Keep a reference while using it and handle misses.

For assets not yet indexed, mount the containing package and load by a **current**
path using the appropriate resource loader, such as `Model.Load` or
`ResourceLibrary.LoadAsync<T>(path)` for supported types, then read and check its
GUID. `LoadAsync` does not load every resource type and game resources are normally
registered by the resource loader; it is not a general GUID discovery API. For
authored references, prefer storing the resource reference itself: engine JSON
serialization retains both GUID and path when known and uses its existing
GUID-first/path-fallback resolution. Path loading alone requires a current path.

A move or rename keeps the identity if the asset's GUID metadata is preserved and
the engine has registered the updated asset. An indexed resource remains findable by
GUID after its path is updated; lookup cannot find an unindexed moved asset merely
from its GUID. Copying, recreating, losing metadata, duplicate GUIDs, packaging, and
mount availability can affect identity or resolution. The network resource serializer
has a separate path-derived identifier; this API does not change that protocol.
