# Tiny TRS conversion: actual failures and reviewed correction

After the actual nineteen-case baseline and explicit source-window release, the
reviewed patch is applied to `GltfModelRig.cs`. It is frozen for the next coherent
Engine stage; it is not installed and has no post-change runtime pass yet.

## Actual evidence

Receipt
`Temp/gltf-validation/direct-gpu/fee5-feature-reds-79/probe-01-editor_direct_gpu_tests/receipt-01.json`,
SHA256 `cf229f85cd7b0e48fc6edcd8ac8d45e01ecb56153247f90f907072701df81b75`,
ran the frozen eighteen-case converter helper. Fourteen passed. The only four
failures were valid 45-degree Z-rotated TRS matrices with scale tuples:

| Case | Scale tuple |
| --- | --- |
| tiny_uniform_1e6 | `(1e-6, 1e-6, 1e-6)` |
| tiny_uniform_1e12 | `(1e-12, 1e-12, 1e-12)` |
| tiny_x_only | `(1e-12, 2, 3)` |
| tiny_nonuniform | `(1e-12, 2e-12, 3e-12)` |

Each uses translation `(3,5,-7)`. `tiny-transform-actual-matrices.json`
reconstructs the exact FLOAT input rows from the frozen helper expressions using
float32 rounding. It explicitly distinguishes those reconstructed literals from
a runtime matrix dump. All zero-axis, all-zero and reflected cases passed. The
largest measured successful reconstruction error was `2.3841858e-7` relative.
Both genuine shear controls rejected correctly.

## Numerical cause and actual observation

The [.NET 10 implementation of Matrix4x4.Decompose](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Private.CoreLib/src/System/Numerics/Matrix4x4.Impl.cs)
uses the absolute `DecomposeEpsilon = 0.0001f` when recovering missing basis
vectors. Rows below that length may be replaced by a canonical axis or a cross
product. This is appropriate for an exactly absent row; for a nonzero tiny row
it can discard rotation or its sign. The engine's existing scale-relative
reconstruction guard then rejects the changed affine matrix as intended.

The original receipt records only `TryNativeTransform=false`. The subsequent
nineteen-case actual baseline below records raw Numerics outputs and confirms
that Numerics reports success while returning a materially wrong reconstruction.

For these four matrices, squaring `1e-12` components still produces normal FLOAT
values, so norm underflow cannot explain them. Norm underflow is a separate
smaller-scale case: the new controls include `1e-20` and `1e-30`. The current
eight-machine-epsilon row guard is not the cause to weaken; it prevents accepting
the wrong reconstruction.

## Applied smallest shared correction

`tiny-transform-normalization.patch` targets only `GltfModelRig.TryNativeTransform`
and new private helpers in the same file. Its exact reviewed bytes were applied
after the coordinator confirmed shared-source release.

1. Keep the original decomposition and reconstruction path first, preserving
   currently successful ordinary, zero-scale, reflected and scale-100 results.
2. Only after that path rejects, compute each source row's length in double.
   Every finite FLOAT square fits in double. Preserve exact zero rows and reject
   nonfinite or unrepresentable FLOAT scale magnitudes.
3. Normalize each nonzero row before calling Numerics again. This makes its
   absolute missing-basis threshold irrelevant to nonzero source scales.
4. Restore each row's magnitude into the returned signed scale.
5. Reconstruct against the **original matrix**, using the unchanged
   `MatchesTrsRow` guard at eight FLOAT machine epsilons per original row. Do not
   compare only the normalized basis, widen tolerances, or add an absolute floor.

Source/GPU matrices, skinning palettes, morph ordering and full-affine rendering
are untouched. The fallback only tries to express an already authored matrix in
the existing public TRS type.

## Tests written before the patch

`SimpleSkinTinyTransformRegressionTests.Run()` has nineteen cases:

- The four actual failures and three cases bracketing Numerics' fixed threshold.
- Three tiny reflection patterns and three zero-axis/tiny-survivor combinations.
- Two controls for float norm subnormal/underflow arithmetic.
- Three shear controls, including tiny shear beside a huge unrelated axis.
- The scale-100 reconstruction control.

Its acceptance calls the installed shared converter, never a copied producer.
It independently compares the resulting public Transform to literal input rows
with a `2e-6` row-relative bound and no absolute floor. Numerics return values,
scale, quaternion and raw reconstruction errors are recorded separately.

The coordinator will build/install this change with the next coherent Engine
stage, then rerun the unchanged nineteen cases and original eighteen cases
through both legacy and public-adapter entries. Original failure receipts remain
intact. No post-change numerical or runtime pass is claimed yet.

## Actual nineteen-case baseline and exact failure classification

Receipt `Temp/gltf-validation/direct-gpu/c5c0-public-cadence-material-diagnostics-25/probe-01-editor_direct_gpu_tests/receipt-01.json`,
SHA256 `165598de63db1add007c7ddedb19b5d4ecb7c95501302d5c117e68255ac20f7f`,
contains nineteen converter cases: **7 passed, 12 failed, 0 incomplete**. Runtime
was `10.0.13`, informational version
`10.0.13-servicing.26504.104+4099f83977f4d29604a9aeb971fb0a7bc1d5f699`.
All twelve failed cases have `NumericsAccepted=true`, `Accepted=false`, and an
independent finite literal TRS expectation. These are conversion failures, not
helper exceptions or tolerance changes.

| Failed cases | Count | Actual diagnosis |
| --- | ---: | --- |
| `actual_tiny_uniform_1e6`, `actual_tiny_uniform_1e12`, `actual_tiny_x_only`, `actual_tiny_nonuniform` | 4 | Normal FLOAT squared norms; raw reconstructed row relative error reaches 2–2.414, losing rotation/sign. The tiny-X case has about 2.0 error on X while other rows remain near float roundoff. |
| `below_numerics_fixed_basis_threshold` | 1 | Scale 9.9e-5 returns quaternion `(1,0,0,0)`, row errors `(1,2.4142,2)`; exact-threshold and above-threshold controls pass. |
| `tiny_reflected_all`, `tiny_reflected_nonuniform` | 2 | Tiny basis recovery loses the source reflection/rotation; errors reach 2.4142 and about 2.0. `tiny_reflected_x` already passes and stays on the original path. |
| `zero_x_with_tiny_survivors`, `zero_y_with_tiny_survivors`, `zero_z_with_tiny_survivors` | 3 | Exact absent rows reconstruct exactly, but nonzero surviving rows lose rotation/sign; errors reach 1, 2.4142, and 1 respectively. Zero scale itself is representable. |
| `row_square_subnormal` | 1 | Input row lengths near 1e-20 produce Numerics scales `9.999973E-21`, exposing FLOAT norm precision loss alongside wrong recovered basis; row errors reach 2.4142. |
| `row_square_underflows_float` | 1 | Nonzero row lengths near 1e-30 produce Numerics scales `(0,0,0)`; all row errors equal 1. FLOAT norm underflow loses the entire matrix basis. |

The seven passing controls are at/above threshold, tiny reflected-X, all three
shear rejection cases, and scale100. The existing reconstruction guard correctly
rejects every wrong decomposition; it must not be widened.

Applied source SHA256:
`9db4ff4455c2e7899e1e82eed241644eddbd650d6d863674d7d75c4e3f0894df`.
The same nineteen-case helper remains
`4705116f8e0d5f236d73b5bd3e9f4b8ddc798ae018bd3594a1677e03c9da46fc`;
the original eighteen-case helper remains
`835f3465560b94864bbbc8db35d0094c63b7bcbe33b04985f6c59e278bf9d752`.
