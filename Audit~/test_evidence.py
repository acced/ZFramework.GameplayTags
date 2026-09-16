import json
import pathlib
import tempfile
import unittest
from release_gate import evaluate
from unity_acceptance import verify_results


class EvidenceGateTests(unittest.TestCase):
    def test_missing_evidence_blocks(self):
        self.assertFalse(evaluate({}, [], {}, "same")["release_approved"])

    def test_stale_source_blocks(self):
        native = [dict(status="passed", source_digest="old", requested_platform=p,
                       executed_tests=10, unity_version=v)
                  for p,v in [("EditMode","2021.3.48f1"),("EditMode","6000.0.1f1"),
                              ("PlayMode","6000.0.1f1"),("Android","6000.0.1f1"),("iOS","6000.0.1f1")]]
        self.assertFalse(evaluate({"managed_checks":"passed","source_digest":"same"},native,{},"same")["release_approved"])

    def test_complete_reviewable_evidence(self):
        native = [dict(status="passed", source_digest="same", requested_platform=p,
                       executed_tests=10, unity_version=v)
                  for p,v in [("EditMode","2021.3.48f1"),("EditMode","6000.0.1f1"),
                              ("PlayMode","6000.0.1f1"),("Android","6000.0.1f1"),("iOS","6000.0.1f1")]]
        review=dict(status="approved", source_digest="same", reviewer="fixture",
                    target_reports=["fixture-only"], rationale="unit test, not real approval")
        self.assertTrue(evaluate({"managed_checks":"passed","source_digest":"same"},native,review,"same")["release_approved"])
        native[0]["source_digest"]="old"
        self.assertFalse(evaluate({"managed_checks":"passed","source_digest":"same"},native,review,"same")["release_approved"])

    def test_empty_skipped_failed_and_missing_coverage(self):
        cases = [
            '<test-run result="Passed"/>',
            '<test-run result="Passed"><test-case name="A" result="Skipped"/></test-run>',
            '<test-run result="Failed"><test-case name="A" result="Failed"/></test-run>',
            '<test-run result="Passed"><test-case name="B" result="Passed"/></test-run>',
        ]
        with tempfile.TemporaryDirectory() as directory:
            path=pathlib.Path(directory)/"result.xml"
            for value in cases:
                path.write_text(value)
                with self.assertRaises(RuntimeError):
                    verify_results(path, ["A"])
            path.write_text('<test-run result="Passed"><test-case name="A" result="Passed"/></test-run>')
            self.assertEqual(1,verify_results(path,["A"])[0])


if __name__ == "__main__":
    unittest.main()
