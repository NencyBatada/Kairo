from typing import Annotated, Literal

from pydantic import Field

from kairo_agents.agents.brand_kit.models import CamelModel
from kairo_agents.agents.plan.models import Usage

Outcome = Literal["worked", "no_effect", "inconclusive"]
NextStep = Literal["scale_up", "change", "stop", "retest"]
FlagCode = Literal[
    "changed_after_lock",
    "approved_with_warnings",
    "ended_early",
    "account_change",
    "short_window",
]
Ratio = Annotated[float, Field(ge=-1, le=100)]
Amount = Annotated[float, Field(ge=0)]


class ReportRegion(CamelModel):
    name: str = Field(min_length=1, max_length=100)
    test: bool
    lift: Ratio | None = None
    low: Ratio | None = None
    high: Ratio | None = None
    conclusive: bool = False


class ReportFlag(CamelModel):
    code: FlagCode
    detail: str = Field(max_length=300)


class NextTest(CamelModel):
    days: int | None = Field(default=None, ge=1, le=365)
    add_regions: bool = False
    reasons: list[Annotated[str, Field(max_length=40)]] = Field(default=[], max_length=10)


class ReportRequest(CamelModel):
    title: str = Field(min_length=1, max_length=200)
    kpi: Literal["orders", "conversions"]
    currency: str = Field(max_length=3)
    days: int = Field(ge=0, le=800)
    planned_days: int = Field(ge=1, le=800)
    outcome: Outcome
    next_step: NextStep
    reason: str | None = Field(default=None, max_length=40)
    lift: Ratio | None = None
    low: Ratio | None = None
    high: Ratio | None = None
    actual: Amount | None = None
    expected: Amount | None = None
    extra: float | None = None
    extra_low: float | None = None
    extra_high: float | None = None
    revenue_lift: Ratio | None = None
    revenue_extra: float | None = None
    spend: Amount
    results: Amount
    cost_per_result: Amount | None = None
    cost_per_extra: Amount | None = None
    cost_per_extra_low: Amount | None = None
    cost_per_extra_high: Amount | None = None
    target_cost: Amount | None = None
    flags: list[ReportFlag] = Field(default=[], max_length=10)
    next_test: NextTest | None = None
    regions: list[ReportRegion] = Field(default=[], max_length=120)


class DraftedReport(CamelModel):
    summary: str
    next_step_reasoning: str
    inconclusive_reason: str | None = None
    next_test_advice: str | None = None


class ReportResponse(CamelModel):
    summary: str
    next_step_reasoning: str
    inconclusive_reason: str | None
    next_test_advice: str | None
    model: str
    prompt_version: str
    usage: Usage | None = None
