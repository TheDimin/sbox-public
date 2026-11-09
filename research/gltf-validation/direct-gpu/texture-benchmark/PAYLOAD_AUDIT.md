# ABeautifulGame prepared texture payload

The source-lit route prepares this sample's original 8-bit JPEG images as
RGBA8888 native resources. Each 2048×2048 texture has twelve mips: 22,369,620
pixel bytes plus 68 bytes of native resource header. The 33 distinct compiled
contents total 738,197,460 pixel bytes or 738,199,704 serialized bytes. The
generated cache adds 48 bytes per entry. This is a CPU/native-resource byte
count, not a measured GPU-memory or residency count and not the size of the
authored compressed JPEG files.

Fifteen source materials have 56 role bindings: 13 base-color, 15 normal,
15 metallic-roughness and 13 occlusion. Occlusion shares the metallic-roughness
image in each of those 13 materials; several other materials also repeat the
same content. Summing a 22,369,688-byte resource for every binding gives
1,252,702,528 bytes, but that count includes duplicate references and is not
the number of distinct resources or a measured allocation. Transfer function,
mip need and role sampler remain distinct contracts even when source image
bytes match.

The source-lit path uses RGBA8888 for both sRGB color and linear 8-bit data,
setting the native linear flag for the latter. A valid 8-bit PNG used as a
linear role is not automatically expanded to float on this route. A separate
legacy stock adapter converts a metallic-roughness input into two RGBA32F
resources: it repacks the roughness and metalness channels and bakes arbitrary
linear material factors before generating mips. Float precision avoids losing
sub-byte factor precision; that is a route-specific shader adaptation, not a
general property of PNG or of this sample's source-lit path.

The currently observed duplicate costs include cache payload reads and digest
verification, managed preparation/serialization buffers, native-resource
temporary-file copies and repeated role hashing. Reusing owned live leases,
verified file-backed native resources with an explicit lifetime, or eliminating
intermediate copies can reduce loader work without modifying image quality.
An 8-bit-channel reduction, mip removal, recompression or alternate GPU format
would be a separate representation/quality decision and is not included here.
The cold JPEG decode and mip construction remains real work unless a previously
prepared, identity-validated generated resource is available.
