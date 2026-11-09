# Generated tangent candidate

No tangent generator is currently integrated. The proposed dependency is
`MikkTSpaceSharp` **0.5.3**, a .NET Standard 2.0 C# port generated from the
reference C implementation. Its author states it requires no native binaries.
The package is not present in the local NuGet cache and has not been restored,
compiled, run, or compared against upstream here. A package reference is not
yet added. The package listing reports a 17 October 2025 update and about 1,300
downloads at this inspection; the upstream repository shows one principal
author and a test directory, but current maintenance and test coverage have not
been independently verified. These facts are not a support guarantee.

The [package](https://www.nuget.org/packages/MikkTSpaceSharp/0.5.3),
[source repository](https://github.com/rds1983/MikkTSpaceSharp) and
[zlib license](https://raw.githubusercontent.com/rds1983/MikkTSpaceSharp/master/LICENSE)
must be checked against the exact resolved package bytes before adoption. The
port's license requires retaining its notice in any source distribution and
accurate attribution. Package transitive dependencies, source revision and
platform behavior remain unverified until inspection of the resolved package.

The port's README sample stores returned values by original vertex index. Do
not copy that approach: the [reference interface](https://raw.githubusercontent.com/mmikk/MikkTSpace/master/mikktspace.h)
returns individual face-corner results and expressly warns against overwriting
or averaging with the old index list. The pinned
[glTF specification](https://raw.githubusercontent.com/KhronosGroup/glTF/8e798b02d254cea97659a333cfcb20875b62bdd4/specification/2.0/Specification.adoc)
recommends default MikkTSpace with the normal texture's coordinates, flat
normals when source normals are missing, and tangent generation for morph
targets when base tangents are missing.

## Bounded initial adapter

For static triangle primitives with a normal map and absent authored tangents,
expand list/strip/fan topology using existing winding-preserving code. Preserve
the original accessor storage and original source indices; use a separate
corner-to-source mapping, identity submitted indices, and one generated FLOAT4
per corner. The Mikk callback selects positions, normals and the effective
normal texture coordinate set by source vertex. If normals are missing, provide
the actual face's flat normal. Return each result at its face-corner index.
Generated tangents must use an explicit corner-indexed GPU descriptor; all
existing attributes continue using the source-vertex map. Supplied tangents
remain untouched. The shader's existing full-affine normal/tangent and
reflection handling then applies once.

`GeneratedTangentOracleTests.Run` supplies independent exact shared-index
mirrored-seam, UV-selection and nonindexed controls, source immutability,
degenerate finiteness, and negative controls for index overwrite and handedness.
It is deliberately a producer-independent callback contract and is **unrun**.
Test strips/fans, UV override and absent normals at the integration boundary;
test authored tangents bypassing generation and compare more complex welds to
independent pinned reference Mikk results. The existing NormalTangentMirrorTest
fixture tests authored tangent direction, normal-map scale, UV transforms,
backfaces and green-channel orientation; it does not prove generation. Render
NormalTangentTest and ABeautifulGame only after the material and GPU paths are
implemented and independently validated.

## Allocation and limits

Let `C` be the number of expanded triangle corners. Generated tangent values
alone require `16*C` bytes; separate uint corner-to-source and identity-index
arrays require at least `8*C` more. A destination GPU tangent copy and input
staging may add further copies. Existing owned source attributes and the
library's internal welding/grouping allocations are additional. The latter
are **not yet bounded or audited**: no production admission threshold should
be called safe until the exact package implementation is inspected and its
temporary peak measured. Use checked arithmetic, a hard corner/workspace
budget enforced before allocation, and explicit pending/error reporting on
over-budget assets. Do not reject or mutate otherwise valid source documents.
Only the coordinator restores packages, runs builds, or measures large cases
under the resource guard.

The static first slice does not prove skinned or morphed normals/tangents,
texture transforms, anisotropy, correct mip filtering, sampler selection,
normal/occlusion realization, public rendering, visual approval, performance or
cross-platform packaging. Those remain separate gates.
