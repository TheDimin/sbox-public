import hashlib
import json
import struct
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

import validate


class ValidationTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name)
        self.addCleanup(self.tmp.cleanup)
        self.manifest = {
            "corpus": {"commit": "a" * 40, "index": "Models/model-index.json"},
            "provisional_extensions": ["KHR_materials_unlit"],
            "required_coverage": ["core"],
        }
        self.model = self.root / "Models" / "Box"
        (self.model / "glTF").mkdir(parents=True)
        (self.model / "README.md").write_text("license")
        (self.root / "Models" / "model-index.json").write_text(json.dumps([
            {"name": "Box", "variants": {"glTF": "Box.gltf"}}
        ]))
        self.asset = self.model / "glTF" / "Box.gltf"
        self.asset.write_text('{"asset":{"version":"2.0"}}')

    def proof(self, **extra):
        artifact = self.root / "proof.txt"
        artifact.write_bytes(b"evidence")
        return {"result": "pass", "fallback": False,
                "adapter": {"name": "fixture", "version": "1"},
                "artifacts": [{"path": "proof.txt", "sha256": hashlib.sha256(b"evidence").hexdigest()}], **extra}

    def test_inventory_uses_asset_extensions_not_tags(self):
        self.asset.write_text(json.dumps({"extensionsUsed": ["KHR_unselected"]}))
        case = validate.inventory(self.root, self.manifest)[0]
        self.assertEqual(case["scope"], "excluded")
        self.assertEqual(case["unselected_extensions"], ["KHR_unselected"])

    def test_missing_external_or_lfs_is_an_error(self):
        self.asset.write_text(json.dumps({"buffers": [{"uri": "buffer.bin"}]}))
        self.assertEqual(validate.inventory(self.root, self.manifest)[0]["scope"], "error")
        (self.asset.parent / "buffer.bin").write_bytes(validate.LFS_HEADER + b"\n")
        self.assertIn("LFS", validate.inventory(self.root, self.manifest)[0]["error"])

    def test_traversal_and_inconsistent_required_extensions_fail(self):
        self.asset.write_text(json.dumps({"buffers": [{"uri": "../../../../secret"}]}))
        self.assertEqual(validate.inventory(self.root, self.manifest)[0]["scope"], "error")
        self.asset.write_text(json.dumps({"extensions": {"KHR_materials_unlit": {}}}))
        self.assertEqual(validate.inventory(self.root, self.manifest)[0]["scope"], "error")

    def test_duplicate_json_keys_rejected(self):
        for source in ('{"Box": 1, "Box": 2}', '{"cases":{"x":{},"x":{}}}'):
            with self.assertRaisesRegex(ValueError, "duplicate JSON key"):
                validate.load_json(source)
        self.asset.write_text('{"extensionsUsed": [], "extensionsUsed": ["KHR_unselected"]}')
        self.assertEqual(validate.inventory(self.root, self.manifest)[0]["scope"], "error")

    def test_missing_and_omitted_variants_cannot_pass_inventory(self):
        self.asset.unlink()
        self.assertEqual(validate.inventory(self.root, self.manifest)[0]["scope"], "error")
        self.asset.write_text('{}')
        missing_from_index = self.model / "glTF-Binary" / "Box.glb"
        missing_from_index.parent.mkdir()
        missing_from_index.write_bytes(b"unindexed")
        with self.assertRaisesRegex(ValueError, "unindexed asset variant"):
            validate.inventory(self.root, self.manifest)

    def test_duplicate_case_ids_and_unknown_scope_rejected(self):
        case = {"id": "Box/glTF", "scope": "required"}
        with self.assertRaisesRegex(ValueError, "duplicate case IDs"):
            validate.assess([case, dict(case)], self.manifest, {}, self.root)
        with self.assertRaisesRegex(ValueError, "unknown case scope"):
            validate.assess([{"id": "Box/glTF", "scope": "skip"}], self.manifest, {}, self.root)
        self.asset.write_text(json.dumps({"extensionsRequired": ["KHR_materials_unlit"]}))
        self.assertEqual(validate.inventory(self.root, self.manifest)[0]["scope"], "error")

    def test_glb_json_and_header(self):
        data = b'{"asset": {"version":"2.0"}}'
        data += b" " * (-len(data) % 4)
        path = self.root / "model.glb"
        path.write_bytes(struct.pack("<4sIIII", b"glTF", 2, 20 + len(data), len(data), 0x4E4F534A) + data)
        self.assertEqual(validate.read_asset(path)["asset"]["version"], "2.0")
        path.write_bytes(path.read_bytes()[:-1])
        with self.assertRaises(ValueError):
            validate.read_asset(path)

    def test_missing_and_wrong_pin_proofs_never_pass(self):
        cases = validate.inventory(self.root, self.manifest)
        self.assertFalse(validate.assess(cases, self.manifest, {}, self.root)["applicable_full_pass"])
        self.assertFalse(validate.assess(cases, self.manifest, {"corpus_commit": "wrong"}, self.root)["applicable_full_pass"])

    def test_checkout_requires_exact_pin_and_clean_corpus(self):
        with patch.object(validate, "git", side_effect=[str(self.root), "b" * 40]):
            with self.assertRaisesRegex(ValueError, "HEAD"):
                validate.verify_checkout(self.root, self.manifest["corpus"])
        with patch.object(validate, "git", side_effect=[str(self.root), "a" * 40, " M Models/Box/model.gltf"]):
            with self.assertRaisesRegex(ValueError, "local changes"):
                validate.verify_checkout(self.root, self.manifest["corpus"])
        with patch.object(validate, "git", side_effect=[str(self.root), "a" * 40, ""]):
            validate.verify_checkout(self.root, self.manifest["corpus"])

    def test_artifact_and_visual_evidence_are_mandatory(self):
        proof = self.proof()
        self.assertEqual(validate.proof_status(proof, self.root, "parser"), "adapter-integration-pending")
        self.assertEqual(validate.proof_status(proof, self.root, "render"), "missing-visual-review")
        no_adapter = dict(proof, adapter={})
        self.assertEqual(validate.proof_status(no_adapter, self.root, "parser"), "missing-adapter")
        proof["fallback"] = True
        self.assertEqual(validate.proof_status(proof, self.root, "parser"), "fallback-or-unspecified")
        proof["fallback"] = False
        (self.root / "proof.txt").write_bytes(b"changed")
        self.assertEqual(validate.proof_status(proof, self.root, "parser"), "artifact-hash-mismatch")

    def test_scoped_pass_and_full_corpus_distinction(self):
        case = validate.inventory(self.root, self.manifest)[0]
        proofs = {c: self.proof() for c in validate.CATEGORIES}
        proofs["render"].update(reviewer="artist", reference="reference capture", conditions="matched")
        evidence = {"corpus_commit": "a" * 40, "cases": {case["id"]: proofs},
                    "coverage": {"core": self.proof()},
                    "performance": self.proof(hardware="test", thresholds="agreed", metrics="recorded")}
        actual = validate.assess([case], self.manifest, evidence, self.root)
        self.assertFalse(actual["applicable_full_pass"])
        self.assertEqual(actual["cases"][0]["proofs"]["parser"], "adapter-integration-pending")
        # Exercise future aggregation logic without pretending test proofs are real adapters.
        with patch.object(validate, "proof_status", return_value="pass"):
            result = validate.assess([case], self.manifest, evidence, self.root)
            self.assertTrue(result["applicable_full_pass"])
            self.assertTrue(result["corpus_full_pass"])
            excluded = {"id": "Outside/glTF", "scope": "excluded", "exclusion_reason": "unselected extension"}
            result = validate.assess([case, excluded], self.manifest, evidence, self.root)
            self.assertTrue(result["applicable_full_pass"])
            self.assertFalse(result["corpus_full_pass"])
            self.assertEqual(result["cases"][1]["exclusion_reason"], "unselected extension")
            excluded["scope"] = "error"
            self.assertFalse(validate.assess([case, excluded], self.manifest, evidence, self.root)["applicable_full_pass"])

    def test_selected_optional_fallback_never_passes(self):
        self.asset.write_text(json.dumps({"extensionsUsed": ["KHR_materials_unlit"]}))
        self.assertEqual(validate.inventory(self.root, self.manifest)[0]["scope"], "required")
        self.assertEqual(validate.proof_status(self.proof(fallback=True), self.root, "parser"), "fallback-or-unspecified")

    def test_missing_required_proof_cannot_be_hidden_by_exclusion(self):
        case = validate.inventory(self.root, self.manifest)[0]
        proofs = {c: self.proof() for c in validate.CATEGORIES}
        proofs["render"].update(reviewer="artist", reference="reference capture", conditions="matched")
        evidence = {"corpus_commit": "a" * 40, "cases": {case["id"]: proofs},
                    "coverage": {"core": self.proof()},
                    "performance": self.proof(hardware="test", thresholds="agreed", metrics="recorded")}
        del proofs["numeric"]
        excluded = {"id": "Outside/glTF", "scope": "excluded", "exclusion_reason": "unselected extension"}
        with patch.object(validate, "proof_status", side_effect=lambda proof, base, category: "pass" if proof else "missing"):
            result = validate.assess([case, excluded], self.manifest, evidence, self.root)
            self.assertFalse(result["applicable_full_pass"])
            self.assertEqual(result["cases"][0]["proofs"]["numeric"], "missing")
            self.assertFalse(result["corpus_full_pass"])
            self.assertFalse(validate.assess([], self.manifest, evidence, self.root)["applicable_full_pass"])
            self.assertFalse(validate.assess([excluded], self.manifest, evidence, self.root)["applicable_full_pass"])

    def test_excluded_mixed_case_does_not_substitute_extension_coverage(self):
        self.manifest["required_coverage"].append("extension-KHR_materials_unlit")
        case = validate.inventory(self.root, self.manifest)[0]
        proofs = {c: self.proof() for c in validate.CATEGORIES}
        proofs["render"].update(reviewer="artist", reference="reference capture", conditions="matched")
        evidence = {"corpus_commit": "a" * 40, "cases": {case["id"]: proofs},
                    "coverage": {"core": self.proof()},
                    "performance": self.proof(hardware="test", thresholds="agreed", metrics="recorded")}
        mixed = {"id": "Mixed/glTF", "scope": "excluded", "exclusion_reason": "unselected extension"}
        with patch.object(validate, "proof_status", side_effect=lambda proof, base, category: "pass" if proof else "missing"):
            result = validate.assess([case, mixed], self.manifest, evidence, self.root)
            self.assertFalse(result["applicable_full_pass"])
            self.assertEqual(result["coverage"]["extension-KHR_materials_unlit"], "missing")


if __name__ == "__main__":
    unittest.main()
