"""Independent source/fixture tests; these do not establish native IBL pixels."""
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

from PIL import Image

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[5]
MODEL = ROOT / 'Temp/gltf-reference/glTF-Sample-Assets/Models/EnvironmentTest'
spec = importlib.util.spec_from_file_location('environment_panorama', HERE / 'prepare_panorama.py')
panorama = importlib.util.module_from_spec(spec)
spec.loader.exec_module(panorama)
embedded_spec = importlib.util.spec_from_file_location('embedded_environment', HERE / 'extract_embedded_ibl.py')
embedded = importlib.util.module_from_spec(embedded_spec)
embedded_spec.loader.exec_module(embedded)


class SourceEnvironmentTests(unittest.TestCase):
    def test_rgbd_expansion_has_independent_hdr_divisor_and_gamma_controls(self):
        import numpy as np
        rgba = np.array([[[255, 128, 0, 64]]], np.uint8)
        actual = embedded.expand_rgbd(rgba)[0, 0]
        self.assertAlmostEqual(float(actual[0]), 255 / 64, places=6)
        self.assertAlmostEqual(float(actual[1]), (128 / 255) ** 2.2 / (64 / 255), places=6)
        self.assertEqual(float(actual[2]), 0)
        with self.assertRaisesRegex(ValueError, 'positive'):
            embedded.expand_rgbd(np.array([[[255, 255, 255, 0]]], np.uint8))
        np.testing.assert_array_equal(embedded.expand_rgbd(np.full((1, 1, 3), 255, np.uint8)), np.ones((1, 1, 3), np.float32))

    def test_cube_basis_cardinals_and_literal_face_rotation(self):
        import numpy as np
        faces = [np.full((2, 2, 3), face, np.float32) for face in range(6)]
        converted = embedded.engine_faces(faces)
        self.assertEqual([float(face[0, 0, 0]) for face in converted], [4, 5, 0, 1, 2, 3])
        faces[4][..., 0] = [[40, 41], [42, 43]]
        np.testing.assert_array_equal(embedded.engine_faces(faces)[0][..., 0], [[41, 43], [40, 42]])

    def test_three_rows_keep_authored_colors_and_linear_orm_channels(self):
        source = MODEL / 'glTF/EnvironmentTest.gltf'
        self.assertEqual(panorama.sha(source), 'c38d83e48ce1714e0dc9683ffbd8928da6bf2f11a7adeea50be5702bf055bfdb')
        doc = json.loads(source.read_text())
        self.assertEqual(len(doc['materials']), 3)
        self.assertEqual(doc['materials'][2]['pbrMetallicRoughness']['baseColorFactor'], [0, 0, 0, 1])
        folder = source.parent / 'EnvironmentTest_images'
        for name, expected_blue in [('roughness_metallic_0.png', 255), ('roughness_metallic_1.png', 0)]:
            with Image.open(folder / name) as original:
                image = original.convert('RGB')
                self.assertEqual(image.getextrema(), ((255, 255), (0, 255), (expected_blue, expected_blue)))
                # Texture roughness is authored in G, metallic in B. Sampling
                # these as sRGB or using R as roughness destroys the test grid.
                self.assertEqual(len(image.getchannel('G').getcolors()), 11)

    def test_optional_ibl_is_separate_from_core_fallback(self):
        source = MODEL / 'glTF-IBL/EnvironmentTest.gltf'
        self.assertEqual(panorama.sha(source), '43bb563c795f299fd14a2c0f3127f921768a7be1510f41310a6b046fde723679')
        doc = json.loads(source.read_text())
        self.assertIn('EXT_lights_image_based', doc['extensionsUsed'])
        self.assertNotIn('EXT_lights_image_based', doc.get('extensionsRequired', []))
        self.assertEqual(doc['scenes'][0]['extensions']['EXT_lights_image_based']['light'], 0)
        light = doc['extensions']['EXT_lights_image_based']['lights'][0]
        self.assertEqual(light['specularImageSize'], 256)
        self.assertEqual(len(light['irradianceCoefficients']), 9)
        self.assertTrue(all(len(row) == 3 for row in light['irradianceCoefficients']))
        self.assertEqual(len(light['specularImages']), 5)
        sizes = []
        data = (source.parent / doc['buffers'][0]['uri']).read_bytes()
        modes = []
        for mip, faces in enumerate(light['specularImages']):
            self.assertEqual(len(faces), 6)
            for index in faces:
                image = doc['images'][index]
                self.assertEqual(image['mimeType'], 'image/png')
                view = doc['bufferViews'][image['bufferView']]
                offset = view.get('byteOffset', 0)
                import io
                with Image.open(io.BytesIO(data[offset:offset + view['byteLength']])) as pixels:
                    self.assertEqual(pixels.size, (256 >> mip, 256 >> mip))
                    # PNG optimization omits alpha=255 on 23 LDR faces. Only
                    # seven images retain RGBD alpha; never assume one layout.
                    self.assertIn(pixels.mode, ('RGB', 'RGBA'))
                    modes.append(pixels.mode)
            sizes.append(256 >> mip)
        self.assertEqual(sizes, [256, 128, 64, 32, 16])
        self.assertEqual(modes.count('RGBA'), 7)
        self.assertEqual(modes.count('RGB'), 23)

    def test_fixture_rejects_changed_source_and_ldr(self):
        import numpy as np
        import OpenImageIO as oiio
        with tempfile.TemporaryDirectory(prefix='gltf-env-oracle-', dir='/tmp') as temporary:
            root = Path(temporary)
            source = root / 'input.exr'
            values = np.full((64, 128, 3), .25, np.float32)
            writer = oiio.ImageOutput.create(str(source))
            self.assertTrue(writer.open(str(source), oiio.ImageSpec(128, 64, 3, oiio.FLOAT)))
            self.assertTrue(writer.write_image(values)); writer.close()
            with self.assertRaisesRegex(ValueError, 'SHA256'):
                panorama.prepare(source, '0' * 64, root / 'changed', 'test', 'independent test')
            with self.assertRaisesRegex(ValueError, 'HDR'):
                panorama.prepare(source, panorama.sha(source), root / 'ldr', 'test', 'independent test')

    def test_original_hdr_exr_bytes_and_decoded_samples_survive(self):
        import numpy as np
        import OpenImageIO as oiio
        with tempfile.TemporaryDirectory(prefix='gltf-env-oracle-', dir='/tmp') as temporary:
            root = Path(temporary)
            source = root / 'input.exr'
            values = np.full((64, 128, 3), .25, np.float32)
            values[12, 41] = [2, 4, 8]
            writer = oiio.ImageOutput.create(str(source))
            self.assertTrue(writer.open(str(source), oiio.ImageSpec(128, 64, 3, oiio.FLOAT)))
            self.assertTrue(writer.write_image(values)); writer.close()
            result = panorama.prepare(source, panorama.sha(source), root / 'output', 'test', 'independent test')
            self.assertEqual(result['maximum'], 8)
            self.assertTrue(result['exactDecodedRoundtrip'])
            self.assertEqual(result['sourceSha256'], result['exrSha256'])
            self.assertEqual(result['nativeImport'], 'pending')


if __name__ == '__main__':
    unittest.main()
