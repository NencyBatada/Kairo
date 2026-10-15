using System.Globalization;
using Kairo.Modules.CampaignRooms.Rooms;

namespace Kairo.Modules.CampaignRooms.Briefs;

internal sealed record BudgetEstimate(decimal Low, decimal High, decimal Suggested, string Currency, int Days, string Basis, IReadOnlyList<BriefEvidence> Evidence)
{
    public decimal Clamp(decimal amount) => Math.Clamp(Math.Round(amount), Low, High);
}

internal static class BudgetGuide
{
    public const decimal ClickShare = 0.05m;
    public const decimal DaysPerMonth = 30m;
    public const decimal OrdersPerRegionPerDay = 2m;
    public const decimal MaxOrdersPerDay = 20m;
    public const decimal CostShareOfPrice = 0.3m;
    public const decimal DefaultPrice = 1000m;
    public const string Planner = "Google Ads Keyword Planner";
    public const string Catalogue = "Catalogue";

    public static BudgetEstimate Estimate(RoomEvidence evidence, IReadOnlyList<string> regions, int days, IReadOnlyList<decimal> prices, string currency)
    {
        days = Math.Max(days, 1);
        if (evidence.Bids is { Low: > 0 } bids && bids.High >= bids.Low && evidence.LastMonthSearches is long searches && searches > 0)
        {
            var share = RegionShare(evidence, regions);
            var clicks = searches * share / DaysPerMonth * ClickShare;
            var low = Nice(clicks * bids.Low * days);
            var high = Nice(clicks * bids.High * days);
            var suggested = Nice(clicks * (bids.Low + bids.High) / 2 * days);
            if (low >= 1)
            {
                var regional = Math.Round(searches * share);
                return new BudgetEstimate(
                    low,
                    Math.Max(high, low),
                    Math.Clamp(suggested, low, Math.Max(high, low)),
                    currency,
                    days,
                    $"{Count(regional)} searches a month in the chosen regions, about {Count(clicks)} clicks a day at a {Money(bids.Low, currency)} to {Money(bids.High, currency)} top-of-page bid, over {days} days.",
                    [
                        new BriefEvidence { Label = "Searches last month", Value = Count(searches), Source = Planner },
                        new BriefEvidence { Label = "Share in the chosen regions", Value = Share((double)share), Source = Planner },
                        new BriefEvidence { Label = "Top-of-page bid range", Value = $"{Money(bids.Low, currency)} to {Money(bids.High, currency)}", Source = Planner },
                        new BriefEvidence { Label = "Expected clicks a day", Value = Count(clicks) },
                        new BriefEvidence { Label = "Campaign days", Value = days.ToString(CultureInfo.InvariantCulture) },
                    ]);
            }
        }

        var price = prices.Count > 0 ? prices.Average() : DefaultPrice;
        var orders = Math.Min(OrdersPerRegionPerDay * Math.Max(regions.Count, 1), MaxOrdersPerDay);
        var cost = price * CostShareOfPrice;
        var middle = Nice(orders * cost * days);
        var floor = Nice(middle * 0.6m);
        var ceiling = Nice(middle * 1.5m);
        return new BudgetEstimate(
            Math.Max(floor, 1),
            Math.Max(ceiling, 1),
            Math.Max(middle, 1),
            currency,
            days,
            $"No keyword bids for this moment, so the budget assumes about {Count(orders)} orders a day at {Money(cost, currency)} each, 30% of the average price of {Money(price, currency)}, over {days} days.",
            [
                new BriefEvidence { Label = "Average product price", Value = Money(price, currency), Source = Catalogue },
                new BriefEvidence { Label = "Assumed orders a day", Value = Count(orders) },
                new BriefEvidence { Label = "Assumed cost per order", Value = Money(cost, currency) },
                new BriefEvidence { Label = "Campaign days", Value = days.ToString(CultureInfo.InvariantCulture) },
            ]);
    }

    public static decimal RegionShare(RoomEvidence evidence, IReadOnlyList<string> regions)
    {
        var known = (evidence.Regions ?? []).Where(region => region.Name is not null && region.LastMonthSearches is > 0).ToList();
        var total = known.Sum(region => region.LastMonthSearches!.Value);
        var chosen = known
            .Where(region => regions.Contains(region.Name!.Trim(), StringComparer.OrdinalIgnoreCase))
            .Sum(region => region.LastMonthSearches!.Value);
        return total > 0 && chosen > 0 ? Math.Min((decimal)chosen / total, 1m) : 1m;
    }

    public static decimal Nice(decimal amount)
    {
        if (amount <= 0)
        {
            return 0;
        }

        var digits = (int)Math.Floor(Math.Log10((double)amount));
        var step = (decimal)Math.Pow(10, Math.Max(digits - 1, 0));
        return Math.Round(amount / step, MidpointRounding.AwayFromZero) * step;
    }

    public static string Money(decimal amount, string currency) => currency switch
    {
        "INR" => "₹" + amount.ToString(amount == Math.Round(amount) ? "N0" : "N2", CultureInfo.GetCultureInfo("en-IN")),
        _ => $"{currency} {amount.ToString(amount == Math.Round(amount) ? "N0" : "N2", CultureInfo.InvariantCulture)}".Trim(),
    };

    public static string Share(double share) => $"{Math.Round(share * 100).ToString("0", CultureInfo.InvariantCulture)}%";

    public static string Count(decimal value) => Math.Round(value).ToString("N0", CultureInfo.InvariantCulture);
}
