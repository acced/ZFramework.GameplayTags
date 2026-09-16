import copy
import json
from pathlib import Path
import tempfile
import unittest
from perf_regression import WATCHED, compare


class PerformanceComparisonTests(unittest.TestCase):
    def fixture(self):
        return {"passed": True, "rows": [
            {"name": name, "size": size, "ns": [10.0] * 7,
             "allocated_bytes": [0.0] * 7, "retained_capacity": None}
            for name, size in WATCHED]}

    def check_pair(self, left, right):
        with tempfile.TemporaryDirectory() as directory:
            a, b = Path(directory) / "reference.json", Path(directory) / "candidate.json"
            a.write_text(json.dumps(left))
            b.write_text(json.dumps(right))
            return compare([a], [b])

    def test_all_watched_rows_and_regression_are_retained(self):
        left = self.fixture()
        right = copy.deepcopy(left)
        right["rows"][0]["ns"] = [12.0] * 7
        rows = self.check_pair(left, right)
        self.assertEqual(len(rows), 9)
        self.assertEqual(sum(r["ratio"] > 1.05 for r in rows), 1)
        self.assertTrue(all(r["watched"] for r in rows))

    def test_missing_watched_row_is_an_error(self):
        right = self.fixture()
        right["rows"].pop()
        with self.assertRaises(RuntimeError):
            self.check_pair(self.fixture(), right)

    def test_duplicate_row_is_an_error(self):
        right = self.fixture()
        right["rows"].append(copy.deepcopy(right["rows"][0]))
        with self.assertRaises(RuntimeError):
            self.check_pair(self.fixture(), right)

    def test_bad_samples_or_correctness_cannot_pass(self):
        for value in (0.0, float("nan"), float("inf")):
            right = self.fixture()
            right["rows"][0]["ns"][0] = value
            with self.assertRaises(RuntimeError):
                self.check_pair(self.fixture(), right)
        right = self.fixture()
        right["passed"] = False
        with self.assertRaises(RuntimeError):
            self.check_pair(self.fixture(), right)
