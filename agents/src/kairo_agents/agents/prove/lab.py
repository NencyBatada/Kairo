from kairo_agents.agents.prove.lab_models import DraftedLab, LabFacts
from kairo_agents.agents.prove.report import NUMBER, RANGE_LEVEL, cut, parse, supported, walk

MAX_SUMMARY = 1200
MAX_EXPLANATION = 1000
MAX_ADVICE = 800
PERCENT_FIELDS = ("lift", "low", "high", "fit", "detectable")


def clean_lab(drafted: DraftedLab, facts: LabFacts) -> DraftedLab | None:
    summary = cut(drafted.summary, MAX_SUMMARY)
    explanation = cut(drafted.explanation, MAX_EXPLANATION)
    if not (summary and explanation):
        return None
    inconclusive = facts.outcome == "inconclusive"
    reason = cut(drafted.inconclusive_reason, MAX_ADVICE) if inconclusive else None
    advice = cut(drafted.next_advice, MAX_ADVICE) if inconclusive else None
    if inconclusive and not (reason and advice):
        return None
    allowed = allowed_numbers(facts)
    texts = [summary, explanation, reason or "", advice or ""]
    if any(not supported(token, allowed) for text in texts for token in NUMBER.findall(text)):
        return None
    return DraftedLab(
        summary=summary, explanation=explanation, inconclusive_reason=reason, next_advice=advice
    )


def allowed_numbers(facts: LabFacts) -> set[float]:
    values: set[float] = {float(RANGE_LEVEL)}
    for value in walk(facts.model_dump()):
        if isinstance(value, bool):
            continue
        if isinstance(value, int | float):
            values.add(abs(float(value)))
        elif isinstance(value, str):
            values.update(parse(token) for token in NUMBER.findall(value))
    ratios = [getattr(facts, field) for field in PERCENT_FIELDS]
    ratios += [
        warning.value for warning in facts.warnings if warning.code in ("noisy", "weak_comparison")
    ]
    values.update(abs(ratio) * 100 for ratio in ratios if ratio is not None)
    days = [facts.change_days, facts.planned_days, facts.history_days, facts.min_history_days]
    values.update(day / 7 for day in days if day % 7 == 0)
    return values
