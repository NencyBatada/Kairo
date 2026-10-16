from typing import Any

import numpy as np
from fastapi import APIRouter

from kairo_agents.agents.prove.models import (
    DayView,
    FiguresView,
    LiftRegion,
    LiftRequest,
    LiftResponse,
    RegionLiftView,
)
from kairo_agents.measurement.lift import METHOD, Figures, measure, pooled

router = APIRouter()


@router.post("/prove/lift", response_model=LiftResponse)
def lift(request: LiftRequest) -> Any:
    tests = [region for region in request.regions if region.test]
    holdouts = [region for region in request.regions if not region.test]
    holdout = pooled([array(region.outcome) for region in holdouts], request.days)
    test = pooled([array(region.outcome) for region in tests], request.days)
    if test is None or holdout is None:
        return LiftResponse(
            status="insufficient_data",
            reason="no_outcome",
            kpi=request.kpi,
            test_days=request.test_days,
            outcome=None,
            revenue=None,
            regions=[],
            daily=[],
            method=METHOD,
        )

    reading = measure(test, holdout, request.start, request.test_days)
    return LiftResponse(
        status="ok" if reading.figures is not None else "insufficient_data",
        reason=reading.reason,
        kpi=request.kpi,
        test_days=request.test_days,
        outcome=view(reading.figures),
        revenue=revenue(request, tests, holdouts),
        regions=[region_lift(request, region, holdout) for region in tests],
        daily=[DayView(actual=actual, expected=expected) for actual, expected in reading.daily],
        method=METHOD,
    )


def revenue(
    request: LiftRequest, tests: list[LiftRegion], holdouts: list[LiftRegion]
) -> FiguresView | None:
    if any(region.revenue is None for region in request.regions):
        return None
    test = pooled([array(region.revenue) for region in tests], request.days)
    holdout = pooled([array(region.revenue) for region in holdouts], request.days)
    if test is None or holdout is None:
        return None
    return view(measure(test, holdout, request.start, request.test_days).figures)


def region_lift(request: LiftRequest, region: LiftRegion, holdout: np.ndarray) -> RegionLiftView:
    values = array(region.outcome)
    if values is None:
        return RegionLiftView(
            id=region.id, status="insufficient_data", reason="no_outcome", figures=None
        )
    reading = measure(values, holdout, request.start, request.test_days)
    return RegionLiftView(
        id=region.id,
        status="ok" if reading.figures is not None else "insufficient_data",
        reason=reading.reason,
        figures=view(reading.figures),
    )


def view(figures: Figures | None) -> FiguresView | None:
    if figures is None:
        return None
    return FiguresView(
        lift=figures.lift,
        low=figures.low,
        high=figures.high,
        actual=figures.actual,
        expected=figures.expected,
        extra=figures.extra,
        extra_low=figures.extra_low,
        extra_high=figures.extra_high,
        conclusive=figures.conclusive,
        placebos=figures.placebos,
        spread=figures.spread,
    )


def array(values: list[float] | None) -> np.ndarray | None:
    return None if values is None else np.asarray(values, dtype=float)
