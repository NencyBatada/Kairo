from typing import Any

import numpy as np
from fastapi import APIRouter

from kairo_agents.agents.plan.proof_models import (
    CandidateView,
    LiftView,
    ProofPlanRequest,
    ProofPlanResponse,
    RegionHistory,
    WarningView,
)
from kairo_agents.measurement.holdout import RegionData, design

METHOD = "placebo-geo/1"

router = APIRouter()


@router.post("/plan/proof-plan", response_model=ProofPlanResponse)
def proof_plan(request: ProofPlanRequest) -> Any:
    plan = design(
        [region_data(region) for region in request.regions],
        max_days=request.max_days,
        target_lift=request.target_lift,
        fixed_holdouts=request.holdout_ids,
    )
    return ProofPlanResponse(
        kpi=plan.kpi,
        test_ids=plan.test_ids,
        holdout_ids=plan.holdout_ids,
        candidates=[
            CandidateView(id=item.id, score=item.score, share=item.share, in_area=item.in_area)
            for item in plan.candidates
        ],
        inside_area=plan.inside_area,
        window_days=plan.window_days,
        lifts=[
            LiftView(days=item.days, lift=item.lift, placebos=item.placebos) for item in plan.lifts
        ],
        min_detectable_lift=plan.lift,
        placebo_windows=plan.placebos,
        holdout_share=plan.holdout_share,
        holdout_daily_revenue=plan.holdout_daily_revenue,
        holdout_cost=plan.holdout_cost,
        data_days=plan.data_days,
        warnings=[WarningView(code=item.code, message=item.message) for item in plan.warnings],
        method=METHOD,
    )


def region_data(region: RegionHistory) -> RegionData:
    return RegionData(
        id=region.id,
        in_area=region.in_area,
        orders=array(region.orders),
        conversions=array(region.conversions),
        revenue=array(region.revenue),
        searches=array(region.searches),
    )


def array(values: list[float] | None) -> np.ndarray | None:
    return None if values is None else np.asarray(values, dtype=float)
