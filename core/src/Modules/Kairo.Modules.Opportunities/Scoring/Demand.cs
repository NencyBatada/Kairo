namespace Kairo.Modules.Opportunities.Scoring;

internal sealed record MonthlySearches(int Year, int Month, long Searches);

internal sealed class Demand
{
    public const int RecentMonths = 3;

    public Demand(IEnumerable<MonthlySearches> months)
    {
        Months = months.OrderBy(month => month.Year).ThenBy(month => month.Month).ToList();
    }

    public IReadOnlyList<MonthlySearches> Months { get; }

    public long LastMonth => Months.Count == 0 ? 0 : Months[^1].Searches;

    public long Average12 => Months.Count == 0 ? 0 : (long)Math.Round(Months.TakeLast(12).Average(month => month.Searches));

    public double Growth
    {
        get
        {
            if (Months.Count <= RecentMonths)
            {
                return 0;
            }

            var before = Months.Skip(Months.Count - 1 - RecentMonths).Take(RecentMonths).Average(month => month.Searches);
            if (before <= 0)
            {
                return LastMonth > 0 ? 1 : 0;
            }

            return Math.Round(LastMonth / before - 1, 4);
        }
    }

    public double? Mom => Months.Count < 2 ? null : Change(Months[^2].Searches);

    public double? Yoy => Months.Count < 13 ? null : Change(Months[^13].Searches);

    public static Demand Sum(IEnumerable<IEnumerable<MonthlySearches>> series) =>
        new(series
            .SelectMany(months => months)
            .GroupBy(month => (month.Year, month.Month))
            .Select(group => new MonthlySearches(group.Key.Year, group.Key.Month, group.Sum(month => month.Searches))));

    private double? Change(long earlier) => earlier <= 0 ? null : Math.Round((double)LastMonth / earlier - 1, 4);
}
