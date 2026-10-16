import logging
from typing import Any

import numpy as np
from fastapi import APIRouter
from fastapi.responses import JSONResponse
from google.genai import types

from kairo_agents.agents.plan.api import usage_of
from kairo_agents.agents.prove.api import view
from kairo_agents.agents.prove.lab import clean_lab
from kairo_agents.agents.prove.lab_models import (
    CandidateView,
    DraftedLab,
    LabCheckResponse,
    LabData,
    LabDayView,
    LabFacts,
    LabOutcome,
    LabRunRequest,
    LabRunResponse,
    WarningView,
)
from kairo_agents.agents.prove.prompt import LAB_PROMPT_VERSION, lab_instructions
from kairo_agents.agents.prove.report_api import ClientSource, model_name
from kairo_agents.agents.sense.prompt import data_block
from kairo_agents.measurement.holdout import PRE_DAYS
from kairo_agents.measurement.lab import Check, chart, check
from kairo_agents.measurement.lift import METHOD, Figures, measure, pooled

logger = logging.getLogger(__name__)
router = APIRouter()


@router.post("/prove/lab/check", response_model=LabCheckResponse)
def lab_check(request: LabData) -> Any:
    return check_view(checked(request))


@router.post("/prove/lab/run", response_model=LabRunResponse)
async def lab_run(request: LabRunRequest, clients: ClientSource) -> Any:
    result = checked(request)
    series = arrays(request)
    test = pooled([series[key] for key in request.affected], request.days)
    holdout = pooled([series[key] for key in result.comparison], request.days)
    if test is None or holdout is None:
        return JSONResponse(status_code=422, content={"error": "no_comparison"})
    reading = measure(test, holdout, request.start, request.change_days)
    outcome = outcome_of(reading.figures)
    names = {item.key: item.name for item in request.series}
    facts = facts_of(request, result, reading.figures, reading.reason, outcome, names)
    model = model_name()
    try:
        response = await clients().aio.models.generate_content(
            model=model,
            contents=data_block(facts),
            config=types.GenerateContentConfig(
                system_instruction=lab_instructions(),
                response_mime_type="application/json",
                response_schema=DraftedLab,
                temperature=0.2,
            ),
        )
        drafted = DraftedLab.model_validate_json(response.text or "")
    except Exception:
        logger.exception("Prove couldn't explain the Impact Lab result.")
        return JSONResponse(status_code=502, content={"error": "model_failed"})
    cleaned = clean_lab(drafted, facts)
    if cleaned is None:
        logger.warning("Prove rejected an Impact Lab draft with unsupported or missing text.")
        return JSONResponse(status_code=502, content={"error": "model_failed"})
    return LabRunResponse(
        status="ok" if reading.figures is not None else "insufficient_data",
        reason=reading.reason,
        outcome=outcome,
        figures=view(reading.figures),
        fit=result.fit,
        detectable=result.detectable,
        placebos=reading.figures.placebos if reading.figures is not None else result.placebos,
        warnings=warning_views(result),
        daily=[
            LabDayView(offset=day.offset, actual=day.actual, expected=day.expected)
            for day in chart(test, holdout, request.start, request.change_days)
        ],
        summary=cleaned.summary,
        explanation=cleaned.explanation,
        inconclusive_reason=cleaned.inconclusive_reason,
        next_advice=cleaned.next_advice,
        method=METHOD,
        model=model,
        prompt_version=LAB_PROMPT_VERSION,
        usage=usage_of(response),
    )


def checked(request: LabData) -> Check:
    return check(
        arrays(request),
        request.affected,
        request.comparison,
        request.start,
        request.change_days,
        request.planned_days,
    )


def arrays(request: LabData) -> dict[str, np.ndarray]:
    return {item.key: np.asarray(item.values, dtype=float) for item in request.series}


def outcome_of(figures: Figures | None) -> LabOutcome:
    if figures is None or not figures.conclusive:
        return "inconclusive"
    return "rose" if figures.lift > 0 else "fell"


def check_view(result: Check) -> LabCheckResponse:
    return LabCheckResponse(
        candidates=[
            CandidateView(key=item.key, fit=item.fit, suggested=item.suggested)
            for item in result.candidates
        ],
        comparison=result.comparison,
        fit=result.fit,
        detectable=result.detectable,
        placebos=result.placebos,
        warnings=warning_views(result),
    )


def warning_views(result: Check) -> list[WarningView]:
    return [
        WarningView.model_validate({"code": item.code, "value": item.value})
        for item in result.warnings
    ]


def facts_of(
    request: LabRunRequest,
    result: Check,
    figures: Figures | None,
    reason: str | None,
    outcome: LabOutcome,
    names: dict[str, str],
) -> LabFacts:
    return LabFacts(
        name=request.name,
        what_changed=request.what_changed,
        metric=request.metric,
        source=request.source,
        currency=request.currency,
        affected=[names[key] for key in request.affected],
        comparison=[names[key] for key in result.comparison],
        change_days=request.change_days,
        planned_days=request.planned_days,
        history_days=request.start,
        min_history_days=PRE_DAYS,
        outcome=outcome,
        reason=reason,
        lift=figures.lift if figures else None,
        low=figures.low if figures else None,
        high=figures.high if figures else None,
        actual=figures.actual if figures else None,
        expected=figures.expected if figures else None,
        extra=figures.extra if figures else None,
        extra_low=figures.extra_low if figures else None,
        extra_high=figures.extra_high if figures else None,
        fit=result.fit,
        detectable=result.detectable,
        placebos=figures.placebos if figures else result.placebos,
        warnings=warning_views(result),
    )
