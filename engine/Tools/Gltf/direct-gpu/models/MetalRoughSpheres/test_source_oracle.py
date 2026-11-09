"""Independent pinned-source material and packaging checks; not renderer evidence."""
import hashlib
import json
import unittest
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[6] / 'Temp/gltf-reference/glTF-Sample-Assets/Models/MetalRoughSpheres'


class MetalRoughSpheresSourceTests(unittest.TestCase):
    def test_pinned_source_forms(self):
        paths = {'glTF/MetalRoughSpheres.gltf': 'a24fd6fd24b2baef80a5d56b465a55635f7ec79039414e25c341d1402cc28048',
                 'glTF-Binary/MetalRoughSpheres.glb': '450c05557b0823a10835e32c05caf77a7b175eba94516555c1c9bb084f563f01',
                 'glTF-Embedded/MetalRoughSpheres.gltf': '748e20d638978194ef773854266bcaa2e966af5cb9d4369e5120b7d43eaa1783'}
        for path, expected in paths.items():
            with self.subTest(path=path):
                self.assertEqual(hashlib.sha256((ROOT / path).read_bytes()).hexdigest(), expected)

    def test_texture_channel_literals(self):
        texture = ROOT / 'glTF/Spheres_MetalRough.png'
        self.assertEqual(hashlib.sha256(texture.read_bytes()).hexdigest(), '7b352787106705789df0e025a8ea2209074569cd87029369bc66080dfca78e5a')
        with Image.open(texture) as image:
            image = image.convert('RGBA')
            self.assertEqual(image.size, (1024, 1024))
            # G is roughness, B metallic: controls differ in both channels.
            self.assertEqual(image.getpixel((256, 256)), (0, 42, 213, 255))
            self.assertEqual(image.getpixel((512, 512)), (0, 128, 127, 255))
            self.assertEqual(image.getpixel((768, 768)), (0, 212, 43, 255))

    def test_source_material_and_sampler(self):
        doc = json.loads((ROOT / 'glTF/MetalRoughSpheres.gltf').read_text())
        pbr = doc['materials'][0]['pbrMetallicRoughness']
        self.assertEqual(pbr['baseColorTexture']['index'], 0)
        self.assertEqual(pbr['metallicRoughnessTexture']['index'], 1)
        self.assertEqual(doc['textures'][1]['source'], 1)
        self.assertEqual(doc['samplers'][0], {'magFilter': 9729, 'minFilter': 9986, 'wrapS': 33071, 'wrapT': 33071})
        self.assertEqual(len(doc['meshes']), 5)
        self.assertTrue(all(x['primitives'][0]['material'] == 0 for x in doc['meshes']))


class MetalRoughSpheresGridOracle(unittest.TestCase):
    def test_gray_grid_extremes_and_wrong_channel_controls(self):
        """Read literal front vertices and texels independently of the renderer.

        The root maps local Z to vertical and the near gray layer has local Y=-1.
        Each selected vertex is on the front pole of a sphere. These assertions
        establish the actual grid axes, including controls that fail if G/B swap,
        red is used, or the linear texture is decoded as sRGB.
        """
        import struct
        doc = json.loads((ROOT / 'glTF/MetalRoughSpheres.gltf').read_text())
        raw = (ROOT / 'glTF/MetalRoughSpheres0.bin').read_bytes()
        texture = Image.open(ROOT / 'glTF/Spheres_MetalRough.png').convert('RGBA')
        expected = {(-9, 9): (0, 255), (9, 9): (255, 255),
                    (-9, -9): (0, 0), (9, -9): (255, 0),
                    (0, 0): (128, 127)}
        observed = {}
        def at(accessor_id, index, count):
            a = doc['accessors'][accessor_id]
            view = doc['bufferViews'][a['bufferView']]
            offset = view.get('byteOffset', 0) + a.get('byteOffset', 0) + index * view.get('byteStride', count * 4)
            return struct.unpack_from('<' + 'f' * count, raw, offset)
        for mesh in doc['meshes']:
            attrs = mesh['primitives'][0]['attributes']
            for i in range(doc['accessors'][attrs['POSITION']]['count']):
                n = at(attrs['NORMAL'], i, 3)
                if n[1] > -0.99999:
                    continue
                x, y, z = at(attrs['POSITION'], i, 3)
                key = (round(x), round(z))
                if abs(y + 1) > 0.0001 or key not in expected:
                    continue
                u, v = at(attrs['TEXCOORD_0'], i, 2)
                pixel = texture.getpixel((int(u * texture.width), int(v * texture.height)))
                observed[key] = pixel
        self.assertEqual(set(observed), set(expected))
        for key, (rough, metal) in expected.items():
            with self.subTest(sphere=key):
                self.assertEqual(observed[key], (0, rough, metal, 255))
        # At top-left a channel swap, red-channel metal or sRGB conversion of
        # the mid-grid value produces a numerically distinct wrong result.
        top_left = observed[(-9, 9)]
        self.assertNotEqual((top_left[2], top_left[1]), expected[(-9, 9)])
        self.assertNotEqual(top_left[0], expected[(-9, 9)][1])
        mid = observed[(0, 0)][1] / 255
        srgb_decoded = ((mid + 0.055) / 1.055) ** 2.4
        self.assertGreater(abs(mid - srgb_decoded), 0.2)
        pbr = doc['materials'][0]['pbrMetallicRoughness']
        self.assertEqual(pbr.get('metallicFactor', 1), 1)
        self.assertEqual(pbr.get('roughnessFactor', 1), 1)
        self.assertEqual(pbr.get('baseColorFactor', [1, 1, 1, 1]), [1, 1, 1, 1])
        self.assertNotIn('normalTexture', doc['materials'][0])


if __name__ == '__main__':
    unittest.main()
