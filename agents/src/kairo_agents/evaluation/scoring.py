import math
from typing import Any


def score_run(cases: list[dict[str, Any]], results: list[dict[str, Any]]) -> dict[str, Any]:
    expected = {case["id"]: case for case in cases}
    actual = {result["id"]: result for result in results}
    failures: list[str] = []
    if len(expected) != len(cases) or len(actual) != len(results):
        failures.append("duplicate_case_results")
    if expected.keys() != actual.keys():
        failures.append("incomplete_evaluation")
    planted = detected = controls = false_flags = critical = critical_rejected = 0
    quality: list[float] = []
    for key, case in expected.items():
        if case["kind"] == "guardian":
            if case.get("expectedRule"):
                planted += 1
                critical += int(bool(case.get("critical")))
            else:
                controls += 1
        result = actual.get(key)
        if result is None or result.get("complete") is not True:
            failures.append("incomplete:" + key)
            continue
        if case["kind"] == "guardian":
            found = set(result.get("rules", []))
            issue = case.get("expectedRule")
            if issue:
                hit = issue in found
                detected += int(hit)
                if case.get("critical"):
                    critical_rejected += int(hit and result.get("accepted") is False)
            else:
                false_flags += int(bool(found) or result.get("accepted") is not True)
        else:
            if result.get("formatPassed") is not True:
                failures.append("invalid_format:" + key)
            if case["kind"] in ("brief", "copy"):
                score = result.get("score")
                if isinstance(score, bool) or not isinstance(score, (int, float)):
                    failures.append("missing_score:" + key)
                elif not math.isfinite(score) or not 0 <= score <= 1:
                    failures.append("invalid_score:" + key)
                else:
                    quality.append(score)
                    if score < 0.8:
                        failures.append("quality_below_threshold:" + key)
    if not planted or not controls or not critical:
        failures.append("missing_guardian_coverage")
    recall = detected / planted if planted else 0
    false_rate = false_flags / controls if controls else 1
    if recall < 0.95:
        failures.append("guardian_detection_below_95_percent")
    if critical_rejected != critical:
        failures.append("critical_issue_accepted_or_missed")
    if false_rate > 0.05:
        failures.append("guardian_false_flags_above_5_percent")
    if len(quality) < 4:
        failures.append("missing_brief_or_copy_scores")
    return {
        "passed": not failures,
        "guardian": {
            "planted": planted,
            "detected": detected,
            "detectionRate": recall,
            "critical": critical,
            "criticalRejected": critical_rejected,
            "cleanControls": controls,
            "falseFlags": false_flags,
            "falseFlagRate": false_rate,
        },
        "qualityScores": quality,
        "failures": failures,
    }
