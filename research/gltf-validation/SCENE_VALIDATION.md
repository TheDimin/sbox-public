# Scene hierarchy and local-matrix validation

Stage C's actual in-engine pure receipt reported six remaining scene failures:
`combined-stage-c-pure745/editor-receipt.json`, SHA256
`41dd3ac9ac05dcd8b785ac5f93fbc4d41591aff2b845417dd30bf61eea0922dd`.
They are two defined-empty arrays, one non-root scene reference and three invalid
local matrices. These are separate from authored skeleton APIs and GPU rest pose.

The published baseline is revision
`8e798b02d254cea97659a333cfcb20875b62bdd4`. Its node and scene schemas require
nonempty arrays when `children` or scene `nodes` is present. Omitting either
property remains valid. Scene roots cannot appear in any node's child list;
different scenes may share the same valid root.
[Node schema](https://github.com/KhronosGroup/glTF/blob/8e798b02d254cea97659a333cfcb20875b62bdd4/specification/2.0/schema/node.schema.json#L13-L20),
[scene schema](https://github.com/KhronosGroup/glTF/blob/8e798b02d254cea97659a333cfcb20875b62bdd4/specification/2.0/schema/scene.schema.json#L8-L15),
[scene narrative](https://github.com/KhronosGroup/glTF/blob/8e798b02d254cea97659a333cfcb20875b62bdd4/specification/2.0/Specification.adoc#L614-L652).

Local matrices must represent TRS rather than perspective or shear. This rule
applies to each authored local matrix, never to the product of valid ancestor
transforms: nonuniform scale combined with descendant rotation may produce
inherited world shear. The implementation preserves the original matrix exactly.
[Published transformations](https://github.com/KhronosGroup/glTF/blob/8e798b02d254cea97659a333cfcb20875b62bdd4/specification/2.0/Specification.adoc#L682-L705).

`GetDefaultScene` checks selected root references against the existing global
parent map. `NodeParents` rejects defined-empty child arrays. A new local-matrix
helper checks the affine row and relative orthogonality of the three linear rows
in double precision. It uses eight float32 epsilons for angular rounding, rather
than an absolute positional error: large translation cannot hide a shear, and
scale100 or extreme finite scales do not increase the normalized error. This is
validation tolerance, not a renderer approximation or matrix rewrite.

Existing singular-scale controls remain accepted. Zero rows can be represented
by zero scale; the helper does not require inversion or a unique decomposition.
The newer draft's explicit nonzero-column matrix restriction is a separate
compatibility difference and is not silently imported into this repair.
[Newer transformation text](https://github.com/KhronosGroup/glTF/blob/9d23a5a5b1d10f22d1554e847f2a1a3f24222392/specification/2.0/Specification.adoc#transformations).

Twelve new controls were written before the producer repair: missing properties,
six rounded TRS matrices including zero,100,1e20 and1e-20 scales, four genuinely
sheared matrices at different scales with huge translation, and the published
decimal-rounded camera example. Existing inherited-shear, reflected-scale and
singular-scale tests remain unchanged.

This is source-only stage D work. Execution belongs to the validation coordinator;
stage C's receipt must not be relabeled as proof of this later source.
