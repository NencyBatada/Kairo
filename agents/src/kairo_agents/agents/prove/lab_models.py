from typing import Annotated, Literal, Self

from pydantic import Field, model_validator

from kairo_agents.agents.brand_kit.models import CamelModel
from kairo_agents.agents.plan.models import Usage
from kairo_agents.agents.prove.models import MAX_DAYS, FiguresView

MAX_SERIES = 200
MAX_PICKED = 50

Key = Annotated[str, Field(min_length=1, max_length=200)]
Values = Annotated[list[Annotated[float, Field(ge=0)]], Field(max_length=MAX_DAYS)]
WarningCode = Literal[
    "short_history", "no_comparison", "weak_comparison", "few_placebos", "noisy", "not_covered"
]
LabOutcome = Literal["rose", "fell", "inconclusive"]


class LabSeries(CamelModel):
    key: Key
    name: str = Field(min_length=1, max_length=200)
    values: Values


class LabData(CamelModel):
    days: int = Field(ge=1, le=MAX_DAYS)
    start: int = Field(ge=0, le=MAX_DAYS)
    change_days: int = Field(ge=1, le=MAX_DAYS)
    planned_days: int = Field(ge=1, le=MAX_DAYS)
    series: list[LabSeries] = Field(min_length=1, max_length=MAX_SERIES)
    affected: list[Key] = Field(min_length=1, max_length=MAX_PICKED)
    comparison: list[Key] | None = Field(default=None, max_length=MAX_PICKED)

    @model_validator(mode="after")
    def aligned(self) -> Self:
        if self.start + self.change_days > self.days:
            raise ValueError("The change days must fall inside the series.")
        keys = [item.key for item in self.series]
        if len(set(keys)) != len(keys):
            raise ValueError("Series keys must be unique.")
        for item in self.series:
            if len(item.values) != self.days:
                raise ValueError(f"Series {item.key} isn't {self.days} days.")
        known = set(keys)
        affected = set(self.affected)
        if not affected <= known:
            raise ValueError("Every affected series must be sent.")
        if self.comparison is not None:
            if not set(self.comparison) <= known:
                raise ValueError("Every comparison series must be sent.")
            if affected & set(self.comparison):
                raise ValueError("A series can't be both affected and a comparison.")
        return self


class CandidateView(CamelModel):
    key: str
    fit: float | None
    suggested: bool


class WarningView(CamelModel):
    code: WarningCode
    value: float | None


class LabCheckResponse(CamelModel):
    candidates: list[CandidateView]
    comparison: list[str]
    fit: float | None
    detectable: float | None
    placebos: int
    warnings: list[WarningView]


class LabRunRequest(LabData):
    name: str = Field(min_length=1, max_length=200)
    what_changed: str = Field(min_length=1, max_length=1000)
    metric: str = Field(min_length=1, max_length=60)
    source: str = Field(min_length=1, max_length=40)
    currency: str = Field(default="", max_length=3)

    @model_validator(mode="after")
    def compared(self) -> Self:
        if not self.comparison:
            raise ValueError("Pick at least one comparison series.")
        return self


class LabDayView(CamelModel):
    offset: int
    actual: float
    expected: float


class LabFacts(CamelModel):
    name: str
    what_changed: str
    metric: str
    source: str
    currency: str
    affected: list[str]
    comparison: list[str]
    change_days: int
    planned_days: int
    history_days: int
    min_history_days: int
    outcome: LabOutcome
    reason: str | None
    lift: float | None = None
    low: float | None = None
    high: float | None = None
    actual: float | None = None
    expected: float | None = None
    extra: float | None = None
    extra_low: float | None = None
    extra_high: float | None = None
    fit: float | None = None
    detectable: float | None = None
    placebos: int
    warnings: list[WarningView]


class DraftedLab(CamelModel):
    summary: str
    explanation: str
    inconclusive_reason: str | None = None
    next_advice: str | None = None


class LabRunResponse(CamelModel):
    status: Literal["ok", "insufficient_data"]
    reason: str | None
    outcome: LabOutcome
    figures: FiguresView | None
    fit: float | None
    detectable: float | None
    placebos: int
    warnings: list[WarningView]
    daily: list[LabDayView]
    summary: str
    explanation: str
    inconclusive_reason: str | None
    next_advice: str | None
    method: str
    model: str
    prompt_version: str
    usage: Usage | None = None
