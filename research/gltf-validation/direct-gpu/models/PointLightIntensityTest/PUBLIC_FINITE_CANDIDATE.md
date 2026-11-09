# Finite point lights through Model.Load

The [isolated patch](../../../../../engine/Tools/Gltf/direct-gpu/models/PointLightIntensityTest/public-finite-point.proposed.patch) connects the selected
scene's finite point-light plan to the ordinary managed glTF resource and
renderer instance. The [submission](public-finite-submission.json) pins the
patch, base files, public test helper, and six cases. The patch passed a dry
application check and the helper compiled against the actual installed managed
assemblies. Candidate Engine and named shader builds, installation, and the
six native cases are pending.

Source and canonical compiled loading share the same policy. A required
declaration with an unbounded point, spot, directional, or nonrepresentable
finite range throws before an instance creates native objects. For an optional
declaration, a selected scene containing an unsupported binding uses the
no-imported-light fallback as a unit. The portable compiler can still preserve
the glTF extension data. Selected finite ranges use the authored metre cutoff
and neither placement nor node scale changes radius or candela. A light-only
model retains an empty mesh list.

Each renderer instance creates and releases its own native point lights.
Creation, pose publication, placement, visibility, disable and disposal reuse
the same light-plan evaluation. The marker is a private live white texture
whose bindless index stays leased for the graphics session. Draw attributes
enable it only for default managed glTF draws in a world with a live finite
binding. Its disabled value is -1. The marked native light suppresses its
ordinary diffuse, specular, transmission and fog contributions. The glTF shader
recognizes the exact private cookie, bypasses cookie projection, and restores
the lobes on its local packed copy. Native clustering, ordinary cookies,
ordinary shadows, modulation and the stock BRDF remain on their existing
code paths. The shared include hooks compile only for the three opted-in glTF
roots. There is no vmdl shader root or installed binary change in the patch.

The highest-risk gate is a marked light with its native lobes off. It must
remain in native clusters for glTF receivers and leave stock vmdl pixels
unchanged. The public cases exercise both behaviors using actual default glTF
and vmdl material routes. They also verify light-only placement and cutoff,
ordinary point shadows, selected-scene visibility and source/compiled disposal.
The previous anonymous shader proved finite arithmetic with native lobes
enabled and therefore cannot substitute for these tests. The stock vmdl
control specifically checks for any effect from native point-cookie handling.
An active failure in either gate keeps the patch unpromotable.

The reviewed numerical and ownership receipts remain in [ACCEPTANCE.md](ACCEPTANCE.md).
Imported point shadows, glTF diffuse-transmission cross-feature pixels, other
camera/renderer paths, spots, directionals and unbounded culling require
separate evidence. Production view integration also needs resource-reset and
long-running marker lifetime tests.
