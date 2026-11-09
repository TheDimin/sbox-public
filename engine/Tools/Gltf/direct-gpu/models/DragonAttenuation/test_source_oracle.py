"""Independent DragonAttenuation inputs and numerical expectations."""
import hashlib
import json
import struct
import unittest
from pathlib import Path

from PIL import Image

CHECKOUT = Path(__file__).resolve().parents[6]
MODEL = CHECKOUT / "Temp/gltf-reference/glTF-Sample-Assets/Models/DragonAttenuation"
SOURCE = MODEL / "glTF"
PINNED = {
    "glTF/DragonAttenuation.gltf": "bd97e0271b94edaf6eba1b9ea338bd0c990e18c1a9085a1c72aaadd8aba4b66b",
    "glTF/DragonAttenuation.bin": "f23edc2737ece1f00bbdda75a3b47214f4cd00dc899d90901914e37cb61557b1",
    "glTF/Dragon_ThicknessMap.jpg": "d43e2467430bbbf5601faa062c71b968f62580fe63b197a7677460758475e118",
    "glTF/checkerboard.png": "e60974e759e54f64e660676017b2565f8246c0b9e142338ffe51f7c4dcf6dce0",
    "screenshot/screenshot_large.png": "aedf321c2f52f98cf56203f09d218856cec9c4bd7a3a83ba9cd8d1f853d195c3",
    "screenshot/surface_color.png": "ea6262d3f180a2bb8c1d03ad9ab523b26c8f873044c5af9c0e0a84231548ac3f",
}
VOLUME = "KHR_materials_volume"
VARIANTS = "KHR_materials_variants"


class DragonSourceTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.doc = json.loads((SOURCE / "DragonAttenuation.gltf").read_bytes())

    def test_inputs_are_pinned(self):
        for path, expected in PINNED.items():
            with self.subTest(path=path):
                self.assertEqual(hashlib.sha256((MODEL / path).read_bytes()).hexdigest(), expected)

    def test_optical_variant_mapping(self):
        doc = self.doc
        self.assertNotIn("cameras", doc)
        dragon = doc["nodes"][1]
        self.assertEqual(dragon["name"], "Dragon")
        self.assertEqual(dragon["scale"], [0.25] * 3)
        primitive, = doc["meshes"][dragon["mesh"]]["primitives"]
        self.assertEqual(primitive["material"], 1)
        self.assertEqual(primitive["extensions"][VARIANTS]["mappings"], [
            {"material": 1, "variants": [0]},
            {"material": 2, "variants": [1]},
        ])
        self.assertEqual(doc["extensions"][VARIANTS]["variants"], [
            {"name": "Attenuation"},
            {"name": "Surface Color"},
        ])

        attenuation, surface = doc["materials"][1:3]
        self.assertEqual(attenuation["pbrMetallicRoughness"]["baseColorFactor"], [1] * 4)
        self.assertEqual(surface["pbrMetallicRoughness"]["baseColorFactor"], [0.921, 0.640, 0.064, 1])
        for material in (attenuation, surface):
            self.assertEqual(material["pbrMetallicRoughness"]["roughnessFactor"], 0)
            self.assertEqual(material["extensions"]["KHR_materials_transmission"]["transmissionFactor"], 1)
            self.assertNotIn("KHR_materials_ior", material["extensions"])
            volume = material["extensions"][VOLUME]
            self.assertEqual(volume["thicknessFactor"], 2.27)
            texture = volume["thicknessTexture"]
            self.assertEqual(texture["texCoord"], 0)
            self.assertEqual(doc["images"][doc["textures"][texture["index"]]["source"]]["uri"], "Dragon_ThicknessMap.jpg")

        self.assertEqual(attenuation["extensions"][VOLUME]["attenuationColor"], [0.921, 0.640, 0.064])
        self.assertEqual(attenuation["extensions"][VOLUME]["attenuationDistance"], 0.155)
        self.assertNotIn("attenuationDistance", surface["extensions"][VOLUME])
        self.assertNotIn("attenuationColor", surface["extensions"][VOLUME])

    def test_scaled_thickness_at_authored_texels(self):
        # The source image is grayscale. These points distinguish linear sampling
        # and world scaling, but cannot distinguish the R and G channel swizzle.
        # Sampling is a texel-center oracle, independent of native image filtering.
        doc = self.doc
        primitive, = doc["meshes"][1]["primitives"]
        accessor = doc["accessors"][primitive["attributes"]["TEXCOORD_0"]]
        view = doc["bufferViews"][accessor["bufferView"]]
        self.assertEqual((accessor["componentType"], accessor["type"]), (5126, "VEC2"))
        offset = view.get("byteOffset", 0) + accessor.get("byteOffset", 0)
        stride = view.get("byteStride", 8)
        binary = (SOURCE / doc["buffers"][view["buffer"]]["uri"]).read_bytes()
        material = doc["materials"][primitive["material"]]["extensions"][VOLUME]
        cases = [
            # Upper horn and thick central body in the inferred camera.
            (11624, (1726, 179), 66, 0.14688235294117646, (0.924978039, 0.655134847, 0.073909900)),
            (48116, (963, 1148), 242, 0.5385686274509804, (0.751303157, 0.212102555, 0.000071107)),
        ]
        with Image.open(SOURCE / "Dragon_ThicknessMap.jpg") as jpeg:
            image = jpeg.convert("RGB")
            for vertex, texel, green, path_metres, expected in cases:
                with self.subTest(vertex=vertex):
                    u, v = struct.unpack_from("<ff", binary, offset + vertex * stride)
                    self.assertEqual((int(u * image.width), int(v * image.height)), texel)
                    self.assertEqual(image.getpixel(texel)[1], green)
                    local = green / 255 * material["thicknessFactor"]
                    scaled = local * doc["nodes"][1]["scale"][0]
                    self.assertAlmostEqual(scaled, path_metres, places=9)
                    for color, transmittance in zip(material["attenuationColor"], expected, strict=True):
                        self.assertAlmostEqual(color ** (scaled / material["attenuationDistance"]), transmittance, places=8)
                    # Omitting the 0.25 node scale gives much darker green.
                    self.assertLess(0.64 ** (local / 0.155), expected[1] / 2)


if __name__ == "__main__":
    unittest.main()
