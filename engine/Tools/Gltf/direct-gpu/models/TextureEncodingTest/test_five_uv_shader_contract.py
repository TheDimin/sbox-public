"""Static ABI checks only; a shader compiler and GPU samples remain required."""

import pathlib
import json
import math
import re
import unittest

SOURCE = pathlib.Path(__file__).with_name("gltf_surface_five_uv.shader")
ROLES = ("BaseColor", "MetallicRoughness", "Normal", "Occlusion", "Emissive")


class FiveUvShaderContract(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.source = SOURCE.read_text()

    def test_nonconflicting_physical_vertex_inputs_and_interpolants(self):
        for slot, semantic in ((2, 4), (3, 5), (4, 6)):
            self.assertRegex(self.source, rf"float2\s+vGltfUv{slot}\s*:\s*TEXCOORD{semantic}\s*;")
        self.assertRegex(self.source, r"float4\s+vGltfUv23\s*:\s*TEXCOORD8\s*;")
        self.assertRegex(self.source, r"float2\s+vGltfUv4\s*:\s*TEXCOORD9\s*;")
        self.assertIn("i.vGltfUv23 = float4( v.vGltfUv2, v.vGltfUv3 )", self.source)
        self.assertIn("i.vGltfUv4 = v.vGltfUv4", self.source)

    def test_every_role_has_independent_selector_rows_texture_and_sampler(self):
        for role in ROLES:
            with self.subTest(role=role):
                self.assertEqual(1, len(re.findall(rf'Attribute\( "Gltf{role}Sampler" \)', self.source)))
                self.assertIn(f"g_flGltfHas{role}Texture > 0.5", self.source)
                self.assertIn(f"g_flGltf{role}UvSlot, g_vGltf{role}UvRowU, g_vGltf{role}UvRowV", self.source)
                self.assertIn(f"g_tGltf{role}Texture.Sample( g_sGltf{role}, uv{role} )", self.source)
        self.assertEqual(5, len(re.findall(r"= GltfUv\( i, g_flGltf\w+UvSlot", self.source)))

    def test_selector_covers_all_five_distinct_inputs(self):
        for expression in ("i.vTextureCoords.xy", "i.vTextureCoords.zw", "i.vGltfUv23.xy", "i.vGltfUv23.zw", "i.vGltfUv4"):
            self.assertIn(f"uv = {expression}", self.source)
        self.assertIn("dot( float3( uv, 1 ), rowU )", self.source)
        self.assertIn("dot( float3( uv, 1 ), rowV )", self.source)

    def test_role_transfer_and_surface_channels(self):
        for role in ROLES:
            expected = "True" if role in ("BaseColor", "Emissive") else "False"
            self.assertRegex(self.source, rf"Texture2D g_tGltf{role}Texture\s*<[^>]*SrgbRead\( {expected} \)")
        self.assertIn("sampleMetallicRoughness.g * g_flGltfRoughnessFactor", self.source)
        self.assertIn("sampleMetallicRoughness.b * g_flGltfMetallicFactor", self.source)
        self.assertIn("lerp( 1, sampleOcclusion.r, g_flGltfOcclusionStrength )", self.source)
        self.assertIn("normalTs.xy *= g_flGltfNormalScale", self.source)
        self.assertIn("m.TextureCoords = uvBaseColor", self.source)
        self.assertIn("return ShadingModelStandard::Shade( m )", self.source)

    def test_gltf_normal_basis_compensates_stock_flip_and_matches_literal_oracle(self):
        self.assertEqual(1, self.source.count("normalTs.y = -normalTs.y;"))
        self.assertLess(self.source.index("normalTs.y = -normalTs.y;"), self.source.index("TransformNormal( normalTs"))
        oracle_path = SOURCE.parent.parent / "NormalTangentMirrorTest" / "asymmetric-normal-uv-reference.json"
        oracle = json.loads(oracle_path.read_text())
        for cell in oracle["cells"]:
            x, y, z = (2 * channel / 255 - 1 for channel in cell["rgb8"])
            x *= oracle["scale"]
            y *= oracle["scale"]
            length = math.sqrt(x * x + y * y + z * z)
            # Fixture N=(1,0,0), T=(0,1,0), B=w*cross(N,T).
            observed = (z / length, x / length, cell["w"] * y / length)
            for actual, expected in zip(observed, cell["front_engine_normal"]):
                self.assertAlmostEqual(expected, actual, places=7)

    def test_linear_gltf_vertex_color_and_engine_instance_tint_are_both_applied(self):
        self.assertIn("saturate( i.vColor.rgb ) * i.vVertexColor.rgb", self.source)
        self.assertIn("m.Opacity = i.vVertexColor.a", self.source)
        self.assertNotIn("SrgbGammaToLinear( i.vColor", self.source)

    def test_backface_flips_shading_and_geometric_normals_together(self):
        self.assertIn("m.Normal = -m.Normal", self.source)
        self.assertIn("m.GeometricNormal = -m.GeometricNormal", self.source)


if __name__ == "__main__":
    unittest.main()
