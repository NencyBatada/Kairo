namespace Kairo.Modules.CampaignRooms.Briefs;

internal sealed class Brief : IBrandOwned
{
    public Guid Id { get; set; }

    public Guid BrandId { get; set; }

    public Guid RoomId { get; set; }

    public int Version { get; set; }

    public BriefContent Content { get; set; } = new();

    public string Origin { get; set; } = BriefOrigins.Drafted;

    public Guid? StageRunId { get; set; }

    public string? Model { get; set; }

    public string? PromptVersion { get; set; }

    public Guid? CreatedBy { get; set; }

    public string? Instruction { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

internal sealed class BriefContent
{
    public string Title { get; set; } = "";

    public string Objective { get; set; } = "";

    public string Kpi { get; set; } = "";

    public List<string> CampaignTypes { get; set; } = [];

    public BriefBudget? Budget { get; set; }

    public DateOnly? StartDate { get; set; }

    public DateOnly? EndDate { get; set; }

    public List<string> Regions { get; set; } = [];

    public List<string> Audiences { get; set; } = [];

    public List<string> KeyMessages { get; set; } = [];

    public List<string> ProductIds { get; set; } = [];

    public List<string> LandingPages { get; set; } = [];

    public List<string> Languages { get; set; } = [];

    public List<BriefReason> Reasons { get; set; } = [];

    public string Goal { get; set; } = "";

    public string Audience { get; set; } = "";

    public string KeyMessage { get; set; } = "";

    public string? Offer { get; set; }

    public List<string> Channels { get; set; } = [];

    public string Timing { get; set; } = "";

    public bool IsSmart => !string.IsNullOrEmpty(Kpi);

    public BriefContent Normalized()
    {
        if (IsSmart)
        {
            return this;
        }

        return new BriefContent
        {
            Title = Title,
            Objective = Goal,
            Kpi = "",
            CampaignTypes = [.. Channels ?? []],
            Regions = [.. Regions ?? []],
            Audiences = string.IsNullOrWhiteSpace(Audience) ? [] : [Audience],
            KeyMessages = string.IsNullOrWhiteSpace(KeyMessage) ? [] : [KeyMessage],
            ProductIds = [.. ProductIds ?? []],
            Offer = Offer,
            Timing = Timing,
        };
    }

    public BriefContent Copy() => new()
    {
        Title = Title,
        Objective = Objective,
        Kpi = Kpi,
        CampaignTypes = [.. CampaignTypes ?? []],
        Budget = Budget is null ? null : new BriefBudget { Amount = Budget.Amount, Currency = Budget.Currency },
        StartDate = StartDate,
        EndDate = EndDate,
        Regions = [.. Regions ?? []],
        Audiences = [.. Audiences ?? []],
        KeyMessages = [.. KeyMessages ?? []],
        ProductIds = [.. ProductIds ?? []],
        LandingPages = [.. LandingPages ?? []],
        Languages = [.. Languages ?? []],
        Reasons = [.. (Reasons ?? []).Select(reason => reason.Copy())],
        Goal = Goal,
        Audience = Audience,
        KeyMessage = KeyMessage,
        Offer = Offer,
        Channels = [.. Channels ?? []],
        Timing = Timing,
    };

    public BriefReason? ReasonFor(string field) => (Reasons ?? []).FirstOrDefault(reason => reason.Field == field);

    public void SetReason(string field, string reason, IEnumerable<BriefEvidence>? evidence = null)
    {
        Reasons ??= [];
        var existing = ReasonFor(field);
        var kept = evidence?.ToList() ?? existing?.Evidence ?? [];
        Reasons.RemoveAll(entry => entry.Field == field);
        Reasons.Add(new BriefReason { Field = field, Reason = reason, Evidence = kept });
        Reasons.Sort((left, right) => BriefFields.Order(left.Field).CompareTo(BriefFields.Order(right.Field)));
    }
}

internal sealed class BriefBudget
{
    public decimal Amount { get; set; }

    public string Currency { get; set; } = "";
}

internal sealed class BriefReason
{
    public string Field { get; set; } = "";

    public string Reason { get; set; } = "";

    public List<BriefEvidence> Evidence { get; set; } = [];

    public BriefReason Copy() => new()
    {
        Field = Field,
        Reason = Reason,
        Evidence = [.. (Evidence ?? []).Select(item => new BriefEvidence { Label = item.Label, Value = item.Value, Source = item.Source })],
    };
}

internal sealed class BriefEvidence
{
    public string Label { get; set; } = "";

    public string Value { get; set; } = "";

    public string? Source { get; set; }
}

internal static class BriefOrigins
{
    public const string Starter = "starter";
    public const string Trigger = "trigger";
    public const string Drafted = "drafted";
    public const string Edit = "edit";
    public const string Instruction = "instruction";
}

internal static class BriefFields
{
    public const string Title = "title";
    public const string Objective = "objective";
    public const string Kpi = "kpi";
    public const string CampaignTypes = "campaignTypes";
    public const string Budget = "budget";
    public const string Dates = "dates";
    public const string Regions = "regions";
    public const string Audiences = "audiences";
    public const string KeyMessages = "keyMessages";
    public const string Products = "products";
    public const string LandingPages = "landingPages";
    public const string Languages = "languages";

    public static readonly string[] All =
        [Title, Objective, Kpi, CampaignTypes, Budget, Dates, Regions, Audiences, KeyMessages, Products, LandingPages, Languages];

    public static int Order(string field) => Array.IndexOf(All, field) is var index and >= 0 ? index : All.Length;

    public static string Label(string field) => field switch
    {
        Title => "Title",
        Objective => "Objective",
        Kpi => "KPI",
        CampaignTypes => "Campaign types",
        Budget => "Budget",
        Dates => "Dates",
        Regions => "Regions",
        Audiences => "Audiences",
        KeyMessages => "Key messages",
        Products => "Products",
        LandingPages => "Landing pages",
        _ => "Languages",
    };
}

internal static class BriefChoices
{
    public const string Search = "search";
    public const string Shopping = "shopping";
    public const string DemandGen = "demand_gen";

    public static readonly (string Value, string Label)[] CampaignTypes =
        [(Search, "Search"), (Shopping, "Shopping"), (DemandGen, "Demand Gen")];

    public static readonly (string Value, string Label)[] Kpis =
        [("orders", "Orders"), ("conversions", "Conversions"), ("revenue", "Revenue")];

    public static readonly (string Value, string Label)[] Languages =
    [
        ("en", "English"), ("hi", "Hindi"), ("mr", "Marathi"), ("gu", "Gujarati"), ("ta", "Tamil"), ("te", "Telugu"),
        ("kn", "Kannada"), ("ml", "Malayalam"), ("bn", "Bengali"), ("pa", "Punjabi"), ("or", "Odia"),
    ];

    public static bool IsCampaignType(string value) => CampaignTypes.Any(choice => choice.Value == value);

    public static bool IsKpi(string value) => Kpis.Any(choice => choice.Value == value);

    public static bool IsLanguage(string value) => Languages.Any(choice => choice.Value == value);

    public static string LanguageName(string value) => Languages.FirstOrDefault(choice => choice.Value == value).Label ?? value;

    public static string CampaignTypeName(string value) => CampaignTypes.FirstOrDefault(choice => choice.Value == value).Label ?? value;
}
