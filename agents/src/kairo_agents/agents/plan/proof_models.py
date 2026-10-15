from typing import Annotated, Literal, Self

from pydantic import Field, model_validator

from kairo_agents.agents.brand_kit.models import CamelModel

MAX_DAYS = 400
MAX_MONTHS = 36

Series = Annotated[list[Annotated[float, Field(ge=0)]], Field(max_length=MAX_DAYS)]
Months = Annotated[list[Annotated[float, Field(ge=0)]], Field(max_length=MAX_MONTHS)]
RegionId = Annotated[str, Field(min_length=1, max_length=100)]


class RegionHistory(CamelModel):
    id: RegionId
    in_area: bool
    orders: Series | None = None
    conversions: Series | None = None
    revenue: Series | None = None
    searches: Months | None = None


class ProofPlanRequest(CamelModel):
    days: int = Field(ge=1, le=MAX_DAYS)
    max_days: int | None = Field(default=None, ge=1, le=366)
    target_lift: float = Field(default=0.10, gt=0, lt=1)
    holdout_ids: list[RegionId] | None = Field(default=None, max_length=20)
    regions: list[RegionHistory] = Field(max_length=120)

    @model_validator(mode="after")
    def aligned(self) -> Self:
        months = {len(region.searches) for region in self.regions if region.searches is not None}
        if len(months) > 1:
            raise ValueError("Every region's searches must cover the same months.")
        for region in self.regions:
            for series in (region.orders, region.conversions, region.revenue):
                if series is not None and len(series) != self.days:
                    raise ValueError(
                        f"Region {region.id} has a series that isn't {self.days} days."
                    )
        return self


class CandidateView(CamelModel):
    id: str
    score: float
    share: float
    in_area: bool


class LiftView(CamelModel):
    days: int
    lift: float | None
    placebos: int


class WarningView(CamelModel):
    code: str
    message: str


class ProofPlanResponse(CamelModel):
    kpi: Literal["orders", "conversions"]
    test_ids: list[str]
    holdout_ids: list[str]
    candidates: list[CandidateView]
    inside_area: bool
    window_days: int | None
    lifts: list[LiftView]
    min_detectable_lift: float | None
    placebo_windows: int
    holdout_share: float | None
    holdout_daily_revenue: float | None
    holdout_cost: float | None
    data_days: int
    warnings: list[WarningView]
    method: str
