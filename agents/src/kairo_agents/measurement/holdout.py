import math
from dataclasses import dataclass, field

import numpy as np

Z_POWER = 1.959964 + 0.841621
PRE_DAYS = 56
RECENT_DAYS = 28
WEEK = 7
WINDOWS = (14, 21, 28)
MIN_WINDOW = 7
MIN_PLACEBOS = 3
ENOUGH_PLACEBOS = 20
HIGH_LIFT = 0.20
MIN_SHARE = 0.15
MAX_SHARE = 0.25
MIN_HOLDOUTS = 2
OUTSIDE_HOLDOUTS = 3
MIN_INSIDE = 3
WEIGHTS = {"outcome": 0.5, "conversions": 0.3, "searches": 0.2}


@dataclass(frozen=True)
class RegionData:
    id: str
    in_area: bool
    orders: np.ndarray | None = None
    conversions: np.ndarray | None = None
    revenue: np.ndarray | None = None
    searches: np.ndarray | None = None


@dataclass(frozen=True)
class Candidate:
    id: str
    score: float
    share: float
    in_area: bool


@dataclass(frozen=True)
class WindowLift:
    days: int
    lift: float | None
    placebos: int


@dataclass(frozen=True)
class Warning:
    code: str
    message: str


@dataclass
class Design:
    kpi: str
    test_ids: list[str] = field(default_factory=list)
    holdout_ids: list[str] = field(default_factory=list)
    candidates: list[Candidate] = field(default_factory=list)
    inside_area: bool = True
    window_days: int | None = None
    lifts: list[WindowLift] = field(default_factory=list)
    lift: float | None = None
    placebos: int = 0
    holdout_share: float | None = None
    holdout_daily_revenue: float | None = None
    holdout_cost: float | None = None
    data_days: int = 0
    warnings: list[Warning] = field(default_factory=list)

    def warn(self, code: str, message: str) -> None:
        if all(warning.code != code for warning in self.warnings):
            self.warnings.append(Warning(code, message))


def design(
    regions: list[RegionData],
    max_days: int | None = None,
    target_lift: float = 0.10,
    fixed_holdouts: list[str] | None = None,
) -> Design:
    kpi = choose_kpi(regions)
    plan = Design(kpi=kpi)
    if kpi == "conversions":
        plan.warn(
            "conversions_kpi",
            "There's no regional sales data, so the test measures website conversions instead.",
        )

    series = {region.id: outcome_of(region, kpi) for region in regions}
    series = {key: value for key, value in series.items() if value is not None}
    start = first_day(list(series.values()))
    plan.data_days = 0 if start is None else len(next(iter(series.values()))) - start
    if plan.data_days < PRE_DAYS:
        plan.warn(
            "short_history",
            "There's less than 8 weeks of history for these regions, so the test may miss a real "
            "lift. Wait for more data or add regions.",
        )
    if start is None:
        plan.warn("no_history", "There's no regional history yet, so Kairo can't design a test.")
        return plan

    usable = {
        region.id: region
        for region in regions
        if region.id in series and recent_total(series[region.id][start:]) > 0
    }
    trimmed = {key: series[key][start:] for key in usable}
    area = [key for key, region in usable.items() if region.in_area]
    outside = [key for key, region in usable.items() if not region.in_area]
    if not area:
        plan.warn(
            "no_area_history",
            "None of the campaign's regions has recent history, so Kairo can't design a test.",
        )
        return plan

    plan.inside_area = len(area) >= MIN_INSIDE
    if fixed_holdouts:
        plan.inside_area = any(usable[key].in_area for key in fixed_holdouts if key in usable)
    pool = area if plan.inside_area else outside
    if len(pool) < MIN_HOLDOUTS:
        plan.warn(
            "few_candidates",
            "Too few regions have history to hold some back, so the test will be weak.",
        )

    baselines = {key: recent_mean(value) for key, value in trimmed.items()}
    area_baseline = sum(baselines[key] for key in area)
    plan.candidates = rank(pool, area, usable, trimmed, baselines, area_baseline, start)

    if fixed_holdouts:
        plan.holdout_ids = [key for key in dict.fromkeys(fixed_holdouts) if key in usable]
    elif plan.inside_area:
        plan.holdout_ids = pick_inside(plan.candidates, len(area))
    else:
        plan.holdout_ids = [candidate.id for candidate in plan.candidates[:OUTSIDE_HOLDOUTS]]
    if not plan.inside_area:
        plan.warn(
            "outside_controls",
            "The campaign covers too few regions to hold some back, so Kairo compares it with "
            "similar regions outside it. The result will be less certain.",
        )

    held = set(plan.holdout_ids)
    plan.test_ids = [key for key in area if key not in held]
    if not plan.test_ids or not plan.holdout_ids:
        plan.warn(
            "no_test_regions",
            "Kairo needs at least one test region and one holdout region to measure lift.",
        )
        return plan

    if area_baseline > 0 and plan.inside_area:
        plan.holdout_share = round(
            sum(baselines[key] for key in plan.holdout_ids) / area_baseline, 4
        )

    test = np.sum([trimmed[key] for key in plan.test_ids], axis=0)
    holdout = np.sum([trimmed[key] for key in plan.holdout_ids], axis=0)
    choose_window(plan, test, holdout, max_days, target_lift)
    cost(plan, regions, start)
    return plan


def choose_kpi(regions: list[RegionData]) -> str:
    with_orders = [region for region in regions if has_data(region.orders)]
    if len(with_orders) >= 2 and any(region.in_area for region in with_orders):
        return "orders"
    return "conversions"


def outcome_of(region: RegionData, kpi: str) -> np.ndarray | None:
    values = region.orders if kpi == "orders" else region.conversions
    if values is None:
        return None
    return np.clip(np.nan_to_num(np.asarray(values, dtype=float)), 0, None)


def has_data(values: np.ndarray | None) -> bool:
    return values is not None and float(np.nansum(values)) > 0


def first_day(series: list[np.ndarray]) -> int | None:
    if not series:
        return None
    total = np.sum(series, axis=0)
    found = np.flatnonzero(total > 0)
    return int(found[0]) if found.size else None


def recent_total(values: np.ndarray) -> float:
    return float(np.sum(values[-PRE_DAYS:]))


def recent_mean(values: np.ndarray) -> float:
    return float(np.mean(values[-RECENT_DAYS:])) if values.size else 0.0


def weekly(values: np.ndarray) -> np.ndarray:
    weeks = values.size // WEEK
    if weeks == 0:
        return np.zeros(0)
    return values[values.size - weeks * WEEK :].reshape(weeks, WEEK).sum(axis=1)


def correlation(left: np.ndarray, right: np.ndarray) -> float | None:
    size = min(left.size, right.size)
    if size < 4:
        return None
    left, right = left[-size:], right[-size:]
    if np.std(left) == 0 or np.std(right) == 0:
        return None
    return float(np.corrcoef(left, right)[0, 1])


def rank(
    pool: list[str],
    area: list[str],
    usable: dict[str, RegionData],
    trimmed: dict[str, np.ndarray],
    baselines: dict[str, float],
    area_baseline: float,
    start: int,
) -> list[Candidate]:
    ranked = []
    for key in pool:
        reference = [other for other in area if other != key]
        scores = {}
        if reference:
            scores["outcome"] = correlation(
                weekly(trimmed[key]),
                weekly(np.sum([trimmed[other] for other in reference], axis=0)),
            )
            scores["conversions"] = component(usable, key, reference, "conversions", start, True)
            scores["searches"] = component(usable, key, reference, "searches", start, False)
        known = {name: value for name, value in scores.items() if value is not None}
        total = sum(WEIGHTS[name] for name in known)
        score = (
            100 * sum(WEIGHTS[name] * max(value, 0.0) for name, value in known.items()) / total
            if total
            else 0.0
        )
        share = baselines[key] / area_baseline if area_baseline > 0 else 0.0
        ranked.append(Candidate(key, round(score, 1), round(share, 4), usable[key].in_area))
    return sorted(ranked, key=lambda candidate: (-candidate.score, candidate.share, candidate.id))


def component(
    usable: dict[str, RegionData],
    key: str,
    reference: list[str],
    name: str,
    start: int,
    daily: bool,
) -> float | None:
    own = getattr(usable[key], name)
    others = [getattr(usable[other], name) for other in reference]
    others = [np.asarray(values, dtype=float) for values in others if values is not None]
    if own is None or not others:
        return None
    own = np.nan_to_num(np.asarray(own, dtype=float))
    total = np.nan_to_num(np.sum(others, axis=0))
    if daily:
        return correlation(weekly(own[start:]), weekly(total[start:]))
    return correlation(own, total)


def pick_inside(candidates: list[Candidate], area_size: int) -> list[str]:
    room = area_size - 1
    chosen: list[Candidate] = []
    share = 0.0
    for candidate in candidates:
        if len(chosen) >= room or (share >= MIN_SHARE and len(chosen) >= MIN_HOLDOUTS):
            break
        if candidate.share > MAX_SHARE or share + candidate.share > MAX_SHARE:
            continue
        chosen.append(candidate)
        share += candidate.share
    for candidate in candidates:
        if len(chosen) >= min(MIN_HOLDOUTS, room):
            break
        if candidate not in chosen:
            chosen.append(candidate)
    return [candidate.id for candidate in chosen]


def log_lift(test: np.ndarray, holdout: np.ndarray, start: int, days: int) -> float | None:
    if start < PRE_DAYS or start + days > min(test.size, holdout.size):
        return None
    pre_test = test[start - PRE_DAYS : start].sum()
    pre_holdout = holdout[start - PRE_DAYS : start].sum()
    win_test = test[start : start + days].sum()
    win_holdout = holdout[start : start + days].sum()
    if min(pre_test, pre_holdout, win_test, win_holdout) <= 0:
        return None
    return math.log(win_test / win_holdout) - math.log(pre_test / pre_holdout)


def placebo_estimates(test: np.ndarray, holdout: np.ndarray, days: int) -> list[float]:
    estimates = []
    start = test.size - days
    while start >= PRE_DAYS:
        estimate = log_lift(test, holdout, start, days)
        if estimate is not None:
            estimates.append(estimate)
        start -= WEEK
    return estimates


def placebo_lift(test: np.ndarray, holdout: np.ndarray, days: int) -> WindowLift:
    estimates = placebo_estimates(test, holdout, days)
    if len(estimates) < MIN_PLACEBOS:
        return WindowLift(days, None, len(estimates))
    spread = float(np.std(estimates, ddof=1))
    return WindowLift(days, round(math.exp(Z_POWER * spread) - 1, 4), len(estimates))


def choose_window(
    plan: Design, test: np.ndarray, holdout: np.ndarray, max_days: int | None, target: float
) -> None:
    options = list(WINDOWS)
    if max_days is not None:
        fitting = [days for days in options if days <= max_days]
        if not fitting:
            plan.warn(
                "short_campaign",
                "The campaign runs for under 2 weeks, which is short for a reliable test.",
            )
            fitting = [max(max_days, MIN_WINDOW)]
        options = fitting
    plan.lifts = [placebo_lift(test, holdout, days) for days in options]
    measured = [entry for entry in plan.lifts if entry.lift is not None]
    reached = [entry for entry in measured if entry.lift <= target]
    chosen = (
        reached[0]
        if reached
        else min(measured, key=lambda entry: entry.lift)
        if measured
        else plan.lifts[0]
    )
    plan.window_days = chosen.days
    plan.lift = chosen.lift
    plan.placebos = chosen.placebos
    if chosen.lift is None:
        plan.warn(
            "no_lift_estimate",
            "There isn't enough history to work out the smallest lift this test can detect.",
        )
        return
    if 0 < chosen.placebos < ENOUGH_PLACEBOS:
        plan.warn(
            "few_test_windows",
            "Kairo could only check the test against a few past periods, so the smallest "
            "detectable lift is a rough guide.",
        )
    if chosen.lift > HIGH_LIFT:
        plan.warn(
            "lift_too_high",
            f"The test can only detect a lift of {chosen.lift:.0%} or more. Run it longer or "
            "add more regions.",
        )
    elif chosen.lift > target:
        plan.warn(
            "lift_above_target",
            f"The test can detect a lift of {chosen.lift:.0%} or more, above the {target:.0%} "
            "Kairo aims for. A longer window or more regions would help.",
        )


def cost(plan: Design, regions: list[RegionData], start: int) -> None:
    held = set(plan.holdout_ids)
    revenue = [
        np.nan_to_num(np.asarray(region.revenue, dtype=float))[start:]
        for region in regions
        if region.id in held and region.revenue is not None
    ]
    if not revenue:
        return
    daily = recent_mean(np.sum(revenue, axis=0))
    plan.holdout_daily_revenue = round(daily, 2)
    if not plan.inside_area:
        plan.holdout_cost = 0.0
        return
    if plan.lift is not None and plan.window_days is not None:
        plan.holdout_cost = round(daily * plan.window_days * plan.lift, 2)
