import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from bench_opendataloader import compare_evaluations, evaluate_gates


def evaluation(overall, documents, *, missing=0):
    return {
        "metrics": {
            "score": {
                "overall_mean": overall,
                "nid_mean": overall + 0.01,
            },
            "missing_predictions": missing,
        },
        "documents": [
            {
                "document_id": document_id,
                "scores": {"overall": score},
            }
            for document_id, score in documents.items()
        ],
    }


class ComparisonTests(unittest.TestCase):
    def test_reports_metric_and_document_deltas(self):
        baseline = evaluation(0.80, {"a": 0.8, "b": 0.6, "c": 0.7})
        candidate = evaluation(0.82, {"a": 0.9, "b": 0.5, "c": 0.7})

        result = compare_evaluations(baseline, candidate, top=1)

        self.assertAlmostEqual(result["deltas"]["overall_mean"], 0.02)
        self.assertEqual(result["documents"]["improved"], 1)
        self.assertEqual(result["documents"]["regressed"], 1)
        self.assertEqual(result["documents"]["unchanged"], 1)
        self.assertEqual(
            result["documents"]["largest_improvements"][0]["document_id"], "a"
        )
        self.assertEqual(
            result["documents"]["largest_regressions"][0]["document_id"], "b"
        )

    def test_reference_delta_is_reported(self):
        baseline = evaluation(0.80, {})
        candidate = evaluation(0.82, {})
        reference = evaluation(0.81, {})

        result = compare_evaluations(baseline, candidate, reference)

        self.assertAlmostEqual(
            result["candidate_vs_reference"]["overall_mean"], 0.01
        )

    def test_gates_cover_aggregate_document_missing_and_reference(self):
        comparison = compare_evaluations(
            evaluation(0.80, {"a": 0.8}),
            evaluation(0.79, {"a": 0.7}, missing=1),
            evaluation(0.81, {}),
        )

        failures = evaluate_gates(
            comparison,
            min_overall_delta=0.0,
            max_document_regression=0.05,
            max_missing=0,
            require_reference_lead=True,
        )

        self.assertEqual(len(failures), 4)


if __name__ == "__main__":
    unittest.main()
