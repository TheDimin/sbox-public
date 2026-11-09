# Binary and primitive validation repair

The installed stage C pure suite reported six failures in
`GltfCoreDataConformanceTests`: five accepted-format fixtures with an incomplete
skinning attribute pair, and one accepted custom unsigned-int vertex attribute.
The stage C receipt is
`Temp/gltf-validation/combined-stage-c-pure745/editor-receipt.json`
(SHA256 `41dd3ac9ac05dcd8b785ac5f93fbc4d41591aff2b845417dd30bf61eea0922dd`).

## Published baseline and newer text

This explicitly glTF 2.0.1 conformance suite uses the published specification,
revision `8e798b02d254cea97659a333cfcb20875b62bdd4`. It prohibits unsigned-int
application-specific mesh vertex attributes. The corresponding accessor schema
also restricts unsigned-int use to primitive indices.
[Published specification source](https://github.com/KhronosGroup/glTF/blob/8e798b02d254cea97659a333cfcb20875b62bdd4/specification/2.0/Specification.adoc#L1151-L1156),
[published accessor schema](https://github.com/KhronosGroup/glTF/blob/8e798b02d254cea97659a333cfcb20875b62bdd4/specification/2.0/schema/accessor.schema.json#L21-L24).

The newer repository revision `9d23a5a5b1d10f22d1554e847f2a1a3f24222392`
relaxes this narrative rule to a recommendation against uint32 vertex attributes,
allowing future versions or extensions to enable them. Its accessor schema still
contains the older hard prohibition. Therefore the two revisions must not be
silently treated as one validation contract. Newer-draft compatibility needs a
separate version and extension review; this patch does not label the newer
narrative invalid.
[Newer narrative](https://github.com/KhronosGroup/glTF/blob/9d23a5a5b1d10f22d1554e847f2a1a3f24222392/specification/2.0/Specification.adoc#L1463-L1475),
[same-revision accessor schema](https://github.com/KhronosGroup/glTF/blob/9d23a5a5b1d10f22d1554e847f2a1a3f24222392/specification/2.0/schema/accessor.schema.json#L20-L23).

The official validator's published issue table currently classifies
`MESH_PRIMITIVE_ATTRIBUTES_ACCESSOR_UNSIGNED_INT` as an error, corroborating the
published baseline. This is a source audit, not a claim that the validator was
executed on these fixtures.
[Validator issues](https://github.com/KhronosGroup/glTF-Validator/blob/main/ISSUES.md),
[validator implementation](https://github.com/KhronosGroup/glTF-Validator/blob/main/lib/src/errors.dart).

## Failure accounting and source changes

| Failures | Classification | Repair |
|---|---|---|
| JOINTS_0 U8 and U16 accepted-format rows | Invalid positive fixtures: required WEIGHTS_0 missing | Add a valid FLOAT VEC4 partner, retaining the tested accessor and expected type |
| WEIGHTS_0 FLOAT, normalized U8 and normalized U16 accepted-format rows | Invalid positive fixtures: required JOINTS_0 missing | Add a valid unsigned-byte VEC4 partner, retaining the tested accessor and expected type/normalization |
| Custom SCALAR unsigned-int vertex row | Missing mesh-role validation under the published baseline | Reject componentType5125 only in the mesh-attribute validator |

The pair rule requires JOINTS_n and WEIGHTS_n together for each n. Both positive
and negative layout fixtures now satisfy that rule unless missing-pair rejection
is specifically under test. Implicit-zero data remains intentional: these tests
exercise accessor layouts, not evaluated skinning sums or animation.
[Published skinning rules](https://github.com/KhronosGroup/glTF/blob/8e798b02d254cea97659a333cfcb20875b62bdd4/specification/2.0/Specification.adoc#L1431-L1486).

`GltfDocument.Primitives.cs` adds the check to `ValidateAttribute`, which is used
by `ReadPrimitive` and `DescribePrimitiveUpload`. General accessor decoding and
primitive index decoding remain unchanged. The independent
`EXT_mesh_gpu_instancing` instance-attribute reader does not call this mesh-role
validator and is unchanged. Its custom-attribute type policy requires its own
extension review, rather than inheriting a mesh-vertex rule accidentally.
[Instancing specification](https://github.com/KhronosGroup/glTF/blob/9d23a5a5b1d10f22d1554e847f2a1a3f24222392/extensions/2.0/Vendor/EXT_mesh_gpu_instancing/README.md#extending-nodes-with-instance-attributes).

Seven new controls accompany the existing assertions: two missing-pair cases,
four invalid shape/normalization cases with valid partners, and one independent
unsigned-int role case. The last checks actual U32 triangle indices and their
owned byte snapshot, rejection of the same accessor as a custom vertex attribute
through both mesh entry points, and continued general unsigned-index decoding.
Existing format assertions and index-width coverage are retained.

Tests were written before the producer check. Runtime execution belongs to the
validation coordinator; no passing result is claimed by this source document.
