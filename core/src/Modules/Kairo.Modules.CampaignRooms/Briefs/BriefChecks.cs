using System.Globalization;

namespace Kairo.Modules.CampaignRooms.Briefs;

internal sealed record BriefWarning(string Code, string Field, string Severity, string Message);

internal sealed record BriefSituation(
    DateOnly Today,
    DateOnly? LatestEnd,
    decimal? BudgetLimit,
    string LimitCurrency,
    IReadOnlyDictionary<string, string> ProductTitles,
    IReadOnlySet<string> InStock,
    bool MerchantCenterLinked,
    bool YouTubeConnected);

internal static class BriefSeverities
{
    public const string Blocking = "blocking";
    public const string Warning = "warning";
    public const string Info = "info";
}

internal static class BriefChecks
{
    public const int MaxDays = 90;

    public static IReadOnlyList<BriefWarning> Run(BriefContent content, BriefSituation situation)
    {
        var warnings = new List<BriefWarning>();
        void Add(string code, string field, string severity, string message) => warnings.Add(new BriefWarning(code, field, severity, message));

        if (string.IsNullOrWhiteSpace(content.Title))
        {
            Add("missing", BriefFields.Title, BriefSeverities.Blocking, "Give the campaign a title.");
        }

        if (string.IsNullOrWhiteSpace(content.Objective))
        {
            Add("missing", BriefFields.Objective, BriefSeverities.Blocking, "Say what the campaign should achieve.");
        }

        if (!BriefChoices.IsKpi(content.Kpi ?? ""))
        {
            Add("missing", BriefFields.Kpi, BriefSeverities.Blocking, "Choose the KPI the campaign is measured on.");
        }

        if ((content.CampaignTypes ?? []).Count == 0)
        {
            Add("missing", BriefFields.CampaignTypes, BriefSeverities.Blocking, "Choose at least one campaign type.");
        }

        Budget(content, situation, Add);
        Dates(content, situation, Add);

        if ((content.Regions ?? []).Count == 0)
        {
            Add("missing", BriefFields.Regions, BriefSeverities.Blocking, "Choose at least one region.");
        }

        if ((content.Audiences ?? []).Count == 0)
        {
            Add("missing", BriefFields.Audiences, BriefSeverities.Blocking, "Describe at least one audience.");
        }

        if ((content.KeyMessages ?? []).Count == 0)
        {
            Add("missing", BriefFields.KeyMessages, BriefSeverities.Blocking, "Add at least one key message.");
        }

        Products(content, situation, Add);

        if ((content.Languages ?? []).Count == 0)
        {
            Add("missing", BriefFields.Languages, BriefSeverities.Blocking, "Choose at least one language.");
        }

        var types = content.CampaignTypes ?? [];
        if (types.Contains(BriefChoices.Shopping) && !situation.MerchantCenterLinked)
        {
            Add("merchant_center_not_linked", BriefFields.CampaignTypes, BriefSeverities.Warning,
                "Merchant Center isn't linked, so the Shopping campaign will be held back at launch. Link it in Settings, Connections.");
        }

        if (types.Contains(BriefChoices.DemandGen) && !situation.YouTubeConnected)
        {
            Add("youtube_not_connected", BriefFields.CampaignTypes, BriefSeverities.Warning,
                "The YouTube channel isn't connected, so Demand Gen will run image ads only until it is.");
        }

        return warnings;
    }

    private static void Budget(BriefContent content, BriefSituation situation, Action<string, string, string, string> add)
    {
        if (content.Budget is not { Amount: > 0 } budget)
        {
            add("missing", BriefFields.Budget, BriefSeverities.Blocking, "Set the campaign's budget.");
            return;
        }

        if (situation.BudgetLimit is not { } limit)
        {
            return;
        }

        if (!string.IsNullOrEmpty(situation.LimitCurrency) && !string.Equals(budget.Currency, situation.LimitCurrency, StringComparison.OrdinalIgnoreCase))
        {
            add("budget_currency", BriefFields.Budget, BriefSeverities.Warning,
                $"The budget is in {budget.Currency} but the budget limit is in {situation.LimitCurrency}, so a budget owner must approve it.");
        }
        else if (budget.Amount > limit)
        {
            add("over_budget_limit", BriefFields.Budget, BriefSeverities.Info,
                $"The budget is above the {BudgetGuide.Money(limit, situation.LimitCurrency)} limit, so a budget owner must approve it.");
        }
    }

    private static void Dates(BriefContent content, BriefSituation situation, Action<string, string, string, string> add)
    {
        if (content.StartDate is not { } start || content.EndDate is not { } end)
        {
            add("missing", BriefFields.Dates, BriefSeverities.Blocking, "Set the start and end dates.");
            return;
        }

        if (start < situation.Today)
        {
            add("start_in_past", BriefFields.Dates, BriefSeverities.Blocking, "The start date has passed. Pick today or a later day.");
        }

        if (end < start)
        {
            add("end_before_start", BriefFields.Dates, BriefSeverities.Blocking, "The end date is before the start date.");
            return;
        }

        if (end.DayNumber - start.DayNumber + 1 > MaxDays)
        {
            add("too_long", BriefFields.Dates, BriefSeverities.Warning, $"The campaign runs longer than {MaxDays} days.");
        }

        if (situation.LatestEnd is { } latest && end > latest)
        {
            add("ends_after_moment", BriefFields.Dates, BriefSeverities.Warning,
                $"The campaign runs past {Day(latest)}, when the moment it was made for ends.");
        }
    }

    private static void Products(BriefContent content, BriefSituation situation, Action<string, string, string, string> add)
    {
        var products = content.ProductIds ?? [];
        if (products.Count == 0)
        {
            add("missing", BriefFields.Products, BriefSeverities.Blocking, "Choose at least one product.");
            return;
        }

        var gone = products.Where(id => !situation.InStock.Contains(id)).ToList();
        if (gone.Count == products.Count)
        {
            add("out_of_stock", BriefFields.Products, BriefSeverities.Blocking, "None of the brief's products are in stock.");
        }
        else if (gone.Count > 0)
        {
            var names = gone.Select(id => situation.ProductTitles.GetValueOrDefault(id, "A product"));
            add("out_of_stock", BriefFields.Products, BriefSeverities.Warning, $"Out of stock: {string.Join(", ", names)}.");
        }
    }

    public static string Day(DateOnly day) => day.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
}
