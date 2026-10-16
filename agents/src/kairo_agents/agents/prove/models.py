from typing import Annotated, Literal, Self

from pydantic import Field, model_validator

from kairo_agents.agents.brand_kit.models import CamelModel

MAX_DAYS = 800

Series = Annotated[list[Annotated[float, Field(ge=0)]], Field(max_length=MAX_DAYS)]
RegionId = Annotated[str, Field(min_length=1, max_length=100)]


class LiftRegion(CamelModel):
    id: RegionId
    test: bool
    outcome: Series | None = None
    revenue: Series | None = None


class LiftRequest(CamelModel):
    kpi: Literal["orders", "conversions"]
    days: int = Field(ge=1, le=MAX_DAYS)
    start: int = Field(ge=0, le=MAX_DAYS)
    test_days: int = Field(ge=1, le=MAX_DAYS)
    regions: list[LiftRegion] = Field(max_length=120)

    @model_validator(mode="after")
    def aligned(self) -> Self:
        if self.start + self.test_days > self.days:
            raise ValueError("The test days must fall inside the series.")
        if not any(region.test for region in self.regions):
            raise ValueError("Send at least one test region.")
        if all(region.test for region in self.regions):
            raise ValueError("Send at least one holdout region.")
        for region in self.regions:
            for values in (region.outcome, region.revenue):
                if values is not None and len(values) != self.days:
                    raise ValueError(
                        f"Region {region.id} has a series that isn't {self.days} days."
                    )
        return self


class FiguresView(CamelModel):
    lift: float
    low: float
    high: float
    actual: float
    expected: float
    extra: float
    extra_low: float
    extra_high: float
    conclusive: bool
    placebos: int
    spread: float


class RegionLiftView(CamelModel):
    id: str
    status: Literal["ok", "insufficient_data"]
    reason: str | None
    figures: FiguresView | None


class DayView(CamelModel):
    actual: float
    expected: float


class LiftResponse(CamelModel):
    status: Literal["ok", "insufficient_data"]
    reason: str | None
    kpi: str
    test_days: int
    outcome: FiguresView | None
    revenue: FiguresView | None
    regions: list[RegionLiftView]
    daily: list[DayView]
    method: str
