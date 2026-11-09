"""Static fixture checks; native texture compilation and IBL need editor evidence."""
import hashlib
from pathlib import Path
import unittest

ROOT = (Path(__file__).resolve().parents[4]/'research/gltf-validation') / "fixtures/environment"


class EnvironmentFixtureTests(unittest.TestCase):
    def test_original_radiance_bytes_and_dimensions(self):
        data = (ROOT / "artist_workshop_1k.hdr").read_bytes()
        self.assertEqual(hashlib.sha256(data).hexdigest(),
                         "f6da9025acadb1149c973fe2bef53d55f4e7144285a87ec1a357520fbcad4767")
        self.assertTrue(data.startswith(b"#?RADIANCE\n"))
        self.assertIn(b"FORMAT=32-bit_rle_rgbe\n", data[:256])
        self.assertIn(b"-Y 512 +X 1024\n", data[:256])

    def test_native_import_proposal_preserves_hdr_and_roughness_mips(self):
        vtex = (ROOT / "artist_workshop_1k.vtex").read_text()
        for declaration in ('"m_typeString" "string" "CUBE"',
                            '"m_outputTypeString" "string" "CUBE"',
                            '"m_colorSpace" "string" "linear"',
                            '"m_outputFormat" "string" "BC6H"',
                            '"m_algorithm" "string" "GGXCubeMapBlur"',
                            '"m_bNoLod" "bool" "0"'):
            self.assertIn(declaration, vtex)
        self.assertIn("research/gltf-validation/fixtures/environment/artist_workshop_1k.hdr", vtex)


if __name__ == "__main__":
    unittest.main()
