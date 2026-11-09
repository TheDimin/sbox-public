# Native sRGB precision audit

The historical `.001` linear error test remains failed. It was a chosen analytical target, not a limit stated by glTF. Current results separate that target from wrong-role transfer, source decoding, alpha/origin, default texel semantics and visual acceptance. No test tolerance or production decoding path has changed.

## Actual measurements

The original sample receipt is `165598de63db1add007c7ddedb19b5d4ecb7c95501302d5c117e68255ac20f7f`. The float-buffer/target differential receipt is `abd55378afb65e4263454cf010a286927bdfa04561ca91c31ba8fbc206b38712`. `../../../../../engine/Tools/Gltf/direct-gpu/models/TextureEncodingTest/audit_precision_receipts.py` verifies both hashes and derives `precision-measurements.json` from completed rows only. These are eleven observed distinct codes, not an unexecuted full256 sweep.

| Encoded byte | Analytic linear | Actual linear | Absolute linear error | Error in encoded byte units |
| ---: | ---: | ---: | ---: | ---: |
| 0 | 0 | 0 | 0 | 0 |
| 3 | 0.0009105809506 | 0.0009155273438 | 4.946393e-06 | 0.01629639 |
| 5 | 0.001517634918 | 0.001525878906 | 8.243989e-06 | 0.02716064 |
| 32 | 0.0144438436 | 0.01440429688 | 3.954672e-05 | 0.05254815 |
| 64 | 0.05126945837 | 0.05126953125 | 7.287596e-08 | 4.621127e-05 |
| 71 | 0.06301001765 | 0.06298828125 | 2.17364e-05 | 0.01222242 |
| 90 | 0.1022417331 | 0.1020507812 | 0.0001909518 | 0.08099512 |
| 128 | 0.2158605001 | 0.2158203125 | 4.018761e-05 | 0.01101783 |
| 136 | 0.2462013267 | 0.24609375 | 0.0001075767 | 0.02731716 |
| 201 | 0.5840784179 | 0.5859375 | 0.001859082 | 0.2849066 |
| 255 | 1 | 1 | 0 | 0 |

All eleven values satisfy the Direct3D encoded-space limit discussed below, including exact endpoints. This comparison does **not** assert a Direct3D rule governs Vulkan. At byte201 the inverse transfer maps the observed result to 201.28490655, despite its linear error0.00185908211. At byte136 the encoded-space error is0.02731716.

Two additional observed codes from the same hashed receipt’s sampler diagnostic follow. Native and explicit static uploads agree at `Load(mip1)`; independent source/mip byte checks and final native GetPixels return `[160,227,160,255]`. These are separate observations, bringing the combined represented codes to 13 without claiming a full sweep.

| Encoded byte | Analytic linear | Actual linear | Absolute linear error | Error in encoded byte units |
| ---: | ---: | ---: | ---: | ---: |
| 160 | 0.3515325995 | 0.3515625 | 2.99005e-05 | 0.006167405 |
| 227 | 0.7681511472 | 0.76953125 | 0.001380103 | 0.1803384 |

Native SampleLevel and Load already return the same approximate values in a float buffer. The raw linear sample, uniform float buffer, native RGBA32F clear, literal pixel output and explicit shader EOTF controls retain their strict precision. Actual native runtime and bound target format are RGBA32323232F. This rules out an intermediate color-target/readback explanation for the observed discrepancy.

## Primary API and asset requirements

glTF 2.0.1 sections3.9.2/3.9.3 require color RGB to be decoded to linear values before calculations; alpha stays linear. They recommend decoding before interpolation. The cited sections provide no universal `.001` linear numeric bound. Specified omitted texture values remain a separate obligation and the actual purple factor-only output is a real missing-role defect. [glTF 2.0.1](https://registry.khronos.org/glTF/specs/2.0/glTF-2.0.html#materials)

Direct3D 11.3 §3.2.3.7 explicitly permits0.5 ULP on the encoded sRGB side for SRGB→FLOAT. Verification applies the ideal inverse conversion without integer rounding and compares against the original encoded value. Zero and one must be exact. Hence an API-permitted error interval depends on the encoded code and its transfer derivative; a fixed `.001` linear bound can be stricter. This is a confirmed Direct3D guarantee and a counterexample to treating `.001` as universally portable. [Microsoft functional specification](https://microsoft.github.io/DirectX-Specs/d3d/archive/D3D11_3_FunctionalSpec.htm)

Vulkan Image Reads specifies UNORM conversion followed by the sRGB EOTF; results are returned as binary32. Its Fundamentals chapter states generic arithmetic accuracy of about 1 part in10^5 and notes format-imposed precision limits. The inspected Image Reads/Texture Sampling sections do not state a dedicated sRGB conversion error bound equivalent to Direct3D’s0.5 code. Generic arithmetic precision alone does not establish how to grade this native conversion implementation. Vulkan normative compliance remains unresolved by this audit; it must not be inferred from the Direct3D result. [Vulkan Image Reads](https://docs.vulkan.org/spec/latest/chapters/images.html#images-reads), [Vulkan numeric computation](https://docs.vulkan.org/spec/latest/chapters/fundamentals.html#fundamentals-floating-point)

OpenGL 4.6 §8.24 specifies the curve and unchanged alpha, but no dedicated conversion error bound in that section. It allows conversion before or after filtering; the former is preferred. §2.3.4.1 gives general floating arithmetic requirements. These statements do not supply a direct numeric warrant to relabel the Vulkan observation as compliant. [OpenGL 4.6 core specification](https://registry.khronos.org/OpenGL/specs/gl/glspec46.core.pdf)

The historical OpenGL EXT_texture_sRGB issue 13 explicitly leaves the maintained precision/distribution to implementations, while describing higher linear precision as a reasonable expectation. This supports avoiding a universal `.001` assumption for native APIs; it is historical OpenGL text, not a Vulkan permission. [Khronos EXT_texture_sRGB](https://registry.khronos.org/OpenGL/extensions/EXT/EXT_texture_sRGB.txt)

Khronos’s Vulkan filtering CTS uses explicit verification thresholds that include render-target and filtering imprecision. This is primary conformance-test implementation evidence, not a replacement for a normative sRGB precision rule or a direct justification for this harness’s value. [Khronos Vulkan filtering tests](https://raw.githubusercontent.com/KhronosGroup/VK-GL-CTS/main/external/vulkancts/modules/vulkan/texture/vktTextureFilteringTests.cpp)

## Proposed decision, pending root approval

Retain the historical `.001` analytical failure. For portable native-shader acceptance, consider an explicitly named encoded-space accuracy rule: finite/range-correct RGB, exact endpoints, and inverse-transfer error≤0.5 encoded byte units; preserve strict linear/alpha controls, wrong-role separation and independent image comparisons. This is a proposed project policy grounded in a documented supported API, not a claim that Vulkan normatively adopts the same bound. It needs an explicit approval and separately frozen tests before execution.

There is no evidence justifying a custom compensating decode pipeline. Applying EOTF after an encoded-space bilinear sample would change filtering semantics. Expanding textures or replacing standard hardware sampling merely to satisfy the original analytical epsilon would add cost and depart from the intended provided-shader route. The six actual textured emission comparisons currently match the independent literal reference exactly. The absent-role Branch fix proceeds separately.

The sampler owner independently audited these primary routes and agreed: the `.001` analytical failure is established, but the available wording establishes neither Vulkan noncompliance nor compliance. The code-space rule remains an explicit proposed project acceptance policy. Root will raise that policy choice with the user after concrete material regressions; no numerical acceptance or source/test execution was changed by this audit.
