from copy import deepcopy

import pytest

from kairo_agents.evaluation.fixtures import Fixtures
from kairo_agents.evaluation.scoring import score_run


def baseline():
    cases = Fixtures().cases
    results = []
    for case in cases:
        value = {"id": case["id"], "complete": True}
        if case["kind"] == "guardian":
            value.update(
                rules=[case["expectedRule"]] if case.get("expectedRule") else [],
                accepted=not case.get("expectedRule"),
            )
        else:
            value.update(formatPassed=True, score=0.8)
        results.append(value)
    return cases, results


def test_the_approved_boundary_scores_pass_with_all_required_cases_complete():
    cases, results = baseline()
    score = score_run(cases, results)
    assert score["passed"]
    assert score["guardian"]["planted"] == 20
    assert score["guardian"]["cleanControls"] == 20


def test_nineteen_of_twenty_issues_and_one_clean_false_flag_are_the_exact_allowed_boundaries():
    cases, results = baseline()
    missed = next(case for case in cases if case.get("expectedRule") and not case.get("critical"))
    clean = next(
        case for case in cases if case["kind"] == "guardian" and not case.get("expectedRule")
    )
    for result in results:
        if result["id"] == missed["id"]:
            result.update(rules=[], accepted=True)
        if result["id"] == clean["id"]:
            result.update(rules=["brand.banned_words"], accepted=False)
    assert score_run(cases, results)["passed"]


@pytest.mark.parametrize(
    "problem", ["two_misses", "critical_accepted", "two_false_flags", "missing", "duplicate"]
)
def test_guardian_regressions_and_incomplete_runs_cannot_pass(problem):
    cases, results = baseline()
    if problem == "two_misses":
        names = [
            case["id"] for case in cases if case.get("expectedRule") and not case.get("critical")
        ][:2]
        for result in results:
            if result["id"] in names:
                result.update(rules=[], accepted=True)
    elif problem == "critical_accepted":
        critical = next(case for case in cases if case.get("critical"))
        next(result for result in results if result["id"] == critical["id"])["accepted"] = True
    elif problem == "two_false_flags":
        names = [
            case["id"]
            for case in cases
            if case["kind"] == "guardian" and not case.get("expectedRule")
        ][:2]
        for result in results:
            if result["id"] in names:
                result.update(rules=["policy.unsupported_claims"], accepted=False)
    elif problem == "missing":
        results.pop()
    else:
        results.append(deepcopy(results[-1]))
    assert not score_run(cases, results)["passed"]


@pytest.mark.parametrize("value", [None, True, float("nan"), float("inf"), 0.79, 1.1])
def test_missing_invalid_and_low_quality_scores_cannot_pass(value):
    cases, results = baseline()
    next(result for result in results if result["id"] == "copy-hi")["score"] = value
    assert not score_run(cases, results)["passed"]


def test_a_single_deterministic_format_failure_fails_the_run():
    cases, results = baseline()
    next(result for result in results if result["id"] == "generated-images")["formatPassed"] = False
    assert not score_run(cases, results)["passed"]
