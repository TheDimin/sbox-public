import hashlib
import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

import validator_adapter as adapter


class ValidatorAdapterTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)

    def report(self, messages=(), counts=(0, 0, 0, 0), truncated=False):
        return json.dumps({"validatorVersion": adapter.VALIDATOR_VERSION, "uri": "/example.glb",
                           "issues": dict(zip(("numErrors", "numWarnings", "numInfos", "numHints"), counts),
                                          messages=list(messages), truncated=truncated)})

    def test_clean_report_passes_but_never_claims_goal_or_render(self):
        result = adapter.parse_report(self.report(), 0)
        self.assertEqual(result["status"], "pass")
        summary = adapter.summarize([{"scope": "required", "source": result}], None, False)
        self.assertTrue(summary["source_validation_pass"])
        self.assertFalse(summary["full_goal_pass"])
        self.assertEqual(summary["render_parity"], "not-tested")

    def test_unsupported_extension_and_truncation_are_incomplete(self):
        unsupported = {"code": "UNSUPPORTED_EXTENSION", "severity": 2, "message": "unsupported"}
        self.assertEqual(adapter.parse_report(self.report([unsupported], (0, 0, 1, 0)), 0)["status"], "incomplete")
        self.assertEqual(adapter.parse_report(self.report(truncated=True), 0)["status"], "incomplete")

    def test_errors_remain_visible_and_not_waived(self):
        issue = {"code": "ACCESSOR_INVALID", "severity": 0, "message": "bad"}
        result = adapter.parse_report(self.report([issue], (1, 0, 0, 0)), 1)
        self.assertEqual(result["status"], "invalid")
        self.assertEqual(result["issues"]["messages"][0]["code"], "ACCESSOR_INVALID")
        self.assertFalse(adapter.summarize([{"scope": "required", "source": result}], None, False)["source_validation_pass"])

    def test_bad_report_counts_version_or_exit_status_are_rejected(self):
        malformed = ["{}", self.report().replace(adapter.VALIDATOR_VERSION, "other"),
                     self.report(counts=(1, 0, 0, 0)), self.report().replace('"numErrors": 0', '"numErrors": true'),
                     '{"validatorVersion":"x","validatorVersion":"y"}']
        for raw in malformed:
            with self.subTest(raw=raw), self.assertRaises(ValueError):
                adapter.parse_report(raw, 0)
        with self.assertRaises(ValueError):
            adapter.parse_report(self.report(), 1)

    def test_validator_hash_is_required(self):
        binary = self.root / "validator"
        binary.write_bytes(b"fake")
        with patch.object(adapter, "VALIDATOR_SHA256", hashlib.sha256(b"different").hexdigest()):
            with self.assertRaisesRegex(ValueError, "hash"):
                adapter.verify_validator(binary)
        with patch.object(adapter, "VALIDATOR_SHA256", hashlib.sha256(b"fake").hexdigest()):
            self.assertEqual(adapter.verify_validator(binary), hashlib.sha256(b"fake").hexdigest())

    def test_compiled_path_rejects_traversal_and_symlinks(self):
        for case_id in ("../outside", "model/..", "model/variant/extra", "/absolute", r"x/..\evil"):
            with self.subTest(case_id=case_id), self.assertRaises(ValueError):
                adapter.compiled_path(self.root, case_id)
        outside = self.root.parent / "outside-for-validator-test"
        (self.root / "model").symlink_to(outside)
        with self.assertRaises(ValueError):
            adapter.compiled_path(self.root, "model/variant")

    def test_report_cannot_overwrite_validator_or_write_inside_inputs(self):
        corpus = self.root / "corpus"
        compiled = self.root / "compiled"
        binary = self.root / "validator"
        for output in (binary, corpus / "report.json", compiled / "model" / "report.json"):
            with self.subTest(output=output), self.assertRaises(ValueError):
                adapter.validate_output_path(output, corpus, compiled, binary)
        adapter.validate_output_path(self.root / "report.json", corpus, compiled, binary)

    def test_compiler_receipt_requires_compile_mode_commit_and_unique_cases(self):
        commit = "a" * 40
        receipt = {"corpus_commit": commit, "compile_requested": True, "cases": [{"id": "Box/glTF"}]}
        self.assertIn("Box/glTF", adapter.compiler_receipt_index(receipt, commit))
        for changed in (dict(receipt, compile_requested=False), dict(receipt, corpus_commit="wrong"),
                        dict(receipt, cases=[{"id": "Box/glTF"}, {"id": "Box/glTF"}])):
            with self.subTest(receipt=changed), self.assertRaises(ValueError):
                adapter.compiler_receipt_index(changed, commit)

    def test_fresh_compile_requires_matching_source_artifact_and_success(self):
        asset = self.root / "model.glb_c"
        asset.write_bytes(b"output")
        source_sha = hashlib.sha256(b"source").hexdigest()
        artifact_sha = hashlib.sha256(b"output").hexdigest()
        case = {"id": "Box/glTF", "scope": "required"}
        record = {"id": case["id"], "scope": case["scope"], "source_sha256": source_sha,
                  "artifact_sha256": artifact_sha, "managed_container_roundtrip": {"status": "pass"}}
        self.assertEqual(adapter.attest_compiled(case, asset, source_sha, {case["id"]: record}), "pass")
        for changed in (dict(record, source_sha256="wrong"), dict(record, artifact_sha256="wrong"),
                        dict(record, scope="excluded"), dict(record, managed_container_roundtrip={"status": "fail"})):
            with self.subTest(record=changed):
                self.assertNotEqual(adapter.attest_compiled(case, asset, source_sha, {case["id"]: changed}), "pass")
        self.assertNotEqual(adapter.attest_compiled(case, asset, None, {case["id"]: dict(record, source_sha256=None)}), "pass")
        asset.unlink()
        self.assertNotEqual(adapter.attest_compiled(case, asset, source_sha, {case["id"]: record}), "pass")
        self.assertNotEqual(adapter.attest_compiled(case, asset, source_sha, {}), "pass")

    def test_unattested_compiled_diagnostic_cannot_count_as_fresh_pass(self):
        good = {"scope": "required", "source": {"status": "pass"},
                "compiled": {"status": "pass", "fresh_compile_attestation": "not-provided"}}
        self.assertFalse(adapter.summarize([good], None, True)["compiled_validation_pass"])
        good["compiled"]["fresh_compile_attestation"] = "pass"
        self.assertTrue(adapter.summarize([good], None, True)["compiled_validation_pass"])

    def test_artifact_is_rechecked_after_validation(self):
        record = {"artifact_sha256": "a" * 64, "packaged_dependency_sha256": {}}
        result = {"artifact_sha256": "a" * 64, "external_resource_sha256": {}}
        artifact = self.root / "Box/glTF/model.glb_c"
        self.assertEqual(adapter.finish_attestation("pass", result, record, artifact, self.root), "pass")
        self.assertEqual(adapter.finish_attestation("pass", dict(result, artifact_sha256="b" * 64), record, artifact, self.root), "artifact-changed-after-attestation")
        self.assertEqual(adapter.finish_attestation("not-provided", {}, None, artifact, self.root), "not-provided")

    def test_compiler_receipt_must_cover_exact_packaged_dependency_hashes(self):
        artifact = self.root / "Box/glTF/model.glb_c"
        digest = "b" * 64
        record = {"artifact_sha256": "a" * 64, "packaged_dependency_sha256": {"images/image.png": digest}}
        result = {"artifact_sha256": "a" * 64, "external_resource_sha256": {"Box/glTF/images/image.png": digest}}
        self.assertEqual(adapter.finish_attestation("pass", result, record, artifact, self.root), "pass")
        for changed in ({}, {"images/image.png": "c" * 64}, {"../outside": digest},
                        {"images/image.png": digest, "images/extra.png": digest}):
            with self.subTest(receipt=changed):
                self.assertNotEqual(adapter.finish_attestation("pass", result, dict(record, packaged_dependency_sha256=changed), artifact, self.root), "pass")
        self.assertNotEqual(adapter.finish_attestation("pass", result, {"artifact_sha256": "a" * 64}, artifact, self.root), "pass")
        self.assertNotEqual(adapter.finish_attestation("pass", dict(result, external_resource_sha256={}), record, artifact, self.root), "pass")

    def test_compiled_asset_is_checked_in_place_and_temporary_copy_removed(self):
        asset = self.root / "model.glb_c"
        asset.write_bytes(b"glTF-example")
        seen = []

        def inspect(binary, temp):
            seen.append(temp)
            self.assertEqual(temp.parent, asset.parent)
            self.assertEqual(temp.suffix, ".glb")
            self.assertEqual(temp.read_bytes(), asset.read_bytes())
            return {"status": "pass", "report": {"info": {"resources": []}}}

        with patch.object(adapter.validate, "read_asset", return_value={}), patch.object(adapter, "run_validator", side_effect=inspect):
            result = adapter.validate_compiled(self.root / "validator", asset)
        self.assertEqual(result["artifact_sha256"], hashlib.sha256(b"glTF-example").hexdigest())
        self.assertFalse(seen[0].exists())
        self.assertEqual(asset.read_bytes(), b"glTF-example")

    def test_compiled_temporary_copy_removed_when_validator_fails(self):
        asset = self.root / "model.glb_c"
        asset.write_bytes(b"glTF-example")
        with patch.object(adapter.validate, "read_asset", return_value={}), patch.object(adapter, "run_validator", side_effect=ValueError("bad report")):
            with self.assertRaisesRegex(ValueError, "bad report"):
                adapter.validate_compiled(self.root / "validator", asset)
        self.assertEqual(sorted(p.name for p in self.root.iterdir()), ["model.glb_c"])

    def test_malformed_compiled_asset_is_still_sent_to_validator(self):
        asset = self.root / "model.glb_c"
        asset.write_bytes(b"broken")
        with patch.object(adapter, "run_validator", return_value={"status": "invalid", "report": {}}) as run:
            result = adapter.validate_compiled(self.root / "validator", asset)
        run.assert_called_once()
        self.assertEqual(result["status"], "invalid")
        self.assertTrue(result["resource_issues"])

    def test_compiled_resources_must_be_present_inside_package_root(self):
        parent = self.root / "case"
        parent.mkdir()
        artifact = parent / "model.glb_c"
        (parent / "texture.png").write_bytes(b"pixels")
        report = {"info": {"resources": [
            {"storage": "external", "uri": "texture.png"},
            {"storage": "external", "uri": "missing.png"},
            {"storage": "external", "uri": "../../outside.png"},
        ]}}
        hashes, problems = adapter.compiled_resources(self.root, artifact, report)
        self.assertEqual(hashes["case/texture.png"], hashlib.sha256(b"pixels").hexdigest())
        self.assertEqual(len(problems), 2)

    def test_compiled_resource_symlink_cannot_escape_package_root(self):
        parent = self.root / "case"
        parent.mkdir()
        (parent / "escape").symlink_to(self.root.parent)
        report = {"info": {"resources": [{"storage": "external", "uri": "escape/outside.png"}]}}
        hashes, problems = adapter.compiled_resources(self.root, parent / "model.glb_c", report)
        self.assertEqual(hashes, {})
        self.assertEqual(len(problems), 1)

    def test_absent_resource_inventory_is_not_assumed_empty(self):
        hashes, problems = adapter.compiled_resources(self.root, self.root / "model.glb_c", {})
        self.assertEqual(hashes, {})
        self.assertTrue(problems)

    def test_empty_subset_inventory_error_and_missing_compiled_cannot_pass(self):
        self.assertFalse(adapter.summarize([], None, True)["source_validation_pass"])
        good = {"status": "pass"}
        cases = [{"scope": "required", "source": good, "compiled": {"status": "missing"}},
                 {"scope": "error", "source": {"status": "inventory-error"}}]
        summary = adapter.summarize(cases, None, True)
        self.assertFalse(summary["source_validation_pass"])
        self.assertFalse(summary["compiled_validation_pass"])
        self.assertFalse(adapter.summarize([{"scope": "required", "source": good}], "Box/", False)["source_validation_pass"])


if __name__ == "__main__":
    unittest.main()
