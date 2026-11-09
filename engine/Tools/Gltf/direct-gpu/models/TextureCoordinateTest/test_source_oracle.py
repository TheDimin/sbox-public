"""Independent oracle sensitivity tests, authored before the model probes."""
import copy
import pathlib
import unittest

import author_reference as oracle


class TextureCoordinateOracleTests(unittest.TestCase):
    def setUp(self):
        self.root = pathlib.Path(__file__).resolve().parents[6]
        self.model = self.root / "Temp/gltf-reference/glTF-Sample-Assets/Models/TextureCoordinateTest"

    def test_three_actual_variants_have_identical_authored_geometry(self):
        forms = oracle.read_forms(self.model)
        self.assertEqual([f["name"] for f in forms], ["glTF", "glTF-Embedded", "glTF-Binary"])
        for form in forms[1:]:
            self.assertEqual(forms[0]["primitives"], form["primitives"])

    def test_view_origins_are_observable_and_uvs_are_top_left(self):
        form = oracle.read_forms(self.model)[0]
        for primitive in form["primitives"]:
            for semantic, stream in primitive["streams"].items():
                self.assertEqual(stream["accessorOffset"], 0)
                self.assertNotEqual(stream["viewOffset"], 0)
                self.assertEqual(stream["normalized"], False)
                if semantic.startswith("TEXCOORD"):
                    self.assertEqual(stream["componentType"], 5126)
                    self.assertEqual(stream["stride"], 8)
        # The model intentionally maps upper-left source image rows to upper-left geometry.
        top_left = form["primitives"][1]
        for position, uv in zip(top_left["streams"]["POSITION"]["values"], top_left["streams"]["TEXCOORD_0"]["values"]):
            self.assertAlmostEqual(uv[0], (position[0] + 1.2) * .4, places=5)
            self.assertAlmostEqual(uv[1], (1.2 - position[1]) * .4, places=5)

    def test_integer_interleaved_fixture_detects_offset_stride_and_set_errors(self):
        source, buffers = oracle.load(self.model / "glTF/TextureCoordinateTest.gltf")
        original = copy.deepcopy(source)
        fixture, fixture_buffers = oracle.layout_fixture(source, buffers)
        self.assertEqual(source, original, "Fixture creation must preserve the pinned source.")
        for mesh in [0, 1, 2, 4]:
            attributes = fixture["meshes"][mesh]["primitives"][0]["attributes"]
            uv0 = oracle.accessor(fixture, fixture_buffers, attributes["TEXCOORD_0"])
            uv1 = oracle.accessor(fixture, fixture_buffers, attributes["TEXCOORD_1"])
            self.assertEqual((uv1["accessorOffset"], uv1["stride"], uv1["componentType"], uv1["normalized"]), (4, 12, 5123, True))
            self.assertGreater(uv1["viewOffset"], 0)
            for a, b in zip(uv0["values"], uv1["values"]):
                self.assertAlmostEqual(b[0], 1-a[0], delta=1/65535)
                self.assertAlmostEqual(b[1], a[1], delta=1/65535)
            wrong_offset = oracle.accessor(fixture, fixture_buffers, attributes["TEXCOORD_1"], offset_delta=-4)
            wrong_stride = oracle.accessor(fixture, fixture_buffers, attributes["TEXCOORD_1"], forced_stride=4)
            self.assertNotEqual(uv1["values"], wrong_offset["values"])
            self.assertNotEqual(uv1["values"], wrong_stride["values"])
            material = fixture["materials"][fixture["meshes"][mesh]["primitives"][0]["material"]]
            self.assertEqual(material["pbrMetallicRoughness"]["baseColorTexture"]["texCoord"], 1)

    def test_png_landmarks_include_asymmetric_label_control(self):
        landmarks = oracle.landmarks(self.model / "glTF/TextureCoordinateTemplate.png")
        self.assertEqual(len(landmarks), 12)
        for point in landmarks:
            self.assertNotEqual(point["rgba"], point["wrongVrgba"])
            self.assertEqual(point["rgba"][3], 255)


if __name__ == "__main__":
    unittest.main()
