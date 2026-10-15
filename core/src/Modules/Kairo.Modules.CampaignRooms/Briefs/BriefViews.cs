using Kairo.Contracts.Identity;
using Kairo.Modules.CampaignRooms.Rooms;

namespace Kairo.Modules.CampaignRooms.Briefs;

internal sealed record BriefBudgetView(decimal Amount, string Currency);

internal sealed record BriefEvidenceView(string Label, string Value, string? Source);

internal sealed record BriefReasonView(string Field, string Reason, BriefEvidenceView[] Evidence);

internal sealed record BriefProductView(string Id, string Title, decimal? Price, string? Currency, string? Link, bool InStock);

internal sealed record SmartBriefView(
    int Version,
    string Origin,
    string Title,
    string Objective,
    string Kpi,
    string[] CampaignTypes,
    BriefBudgetView? Budget,
    DateOnly? StartDate,
    DateOnly? EndDate,
    string[] Regions,
    string[] Audiences,
    string[] KeyMessages,
    BriefProductView[] Products,
    string[] LandingPages,
    string[] Languages,
    BriefReasonView[] Reasons,
    string? Model,
    string? PromptVersion,
    DateTimeOffset CreatedAt);

internal sealed record BriefWarningView(string Code, string Field, string Severity, string Message);

internal sealed record BriefChoiceView(string Value, string Label);

internal sealed record BriefRegionOptionView(string? Id, string Name, long? LastMonthSearches);

internal sealed record BriefOptionsView(
    BriefRegionOptionView[] Regions,
    BriefProductView[] Products,
    BriefChoiceView[] CampaignTypes,
    BriefChoiceView[] Kpis,
    BriefChoiceView[] Languages);

internal sealed record BriefVersionView(int Version, string Origin, CampaignPersonView? By, string? Instruction, DateTimeOffset CreatedAt);

internal sealed record BriefDetail(
    Guid CampaignId,
    int CampaignVersion,
    bool Editable,
    SmartBriefView? Brief,
    BriefWarningView[] Warnings,
    BriefBudgetView? BudgetLimit,
    BriefOptionsView Options,
    BriefVersionView[] History);

internal sealed record BriefChangeView(string Field, string Label, string Before, string After, string Reason);

internal sealed record BriefProposalView(
    Guid Id,
    int BriefVersion,
    string Instruction,
    string Summary,
    BriefChangeView[] Changes,
    SmartBriefView Preview,
    BriefWarningView[] Warnings,
    DateTimeOffset ExpiresAt);

internal sealed record BriefEditRequest(
    int? Version,
    int? BriefVersion,
    string? Title,
    string? Objective,
    string? Kpi,
    string[]? CampaignTypes,
    decimal? Budget,
    DateOnly? StartDate,
    DateOnly? EndDate,
    string[]? Regions,
    string[]? Audiences,
    string[]? KeyMessages,
    string[]? ProductIds,
    string[]? LandingPages,
    string[]? Languages);

internal sealed record BriefInstructionRequestBody(int? Version, int? BriefVersion, string? Instruction);

internal sealed record ApplyProposalRequest(int? Version);

internal static class BriefViews
{
    public static SmartBriefView View(Brief brief, BriefContent content, BriefDeskContext context) => new(
        brief.Version,
        brief.Origin,
        content.Title,
        content.Objective,
        content.Kpi ?? "",
        [.. content.CampaignTypes ?? []],
        content.Budget is { } budget ? new BriefBudgetView(budget.Amount, budget.Currency) : null,
        content.StartDate,
        content.EndDate,
        [.. content.Regions ?? []],
        [.. content.Audiences ?? []],
        [.. content.KeyMessages ?? []],
        [.. (content.ProductIds ?? []).Select(id => Product(context.Product(id) ?? new BriefProductOption(id, "Unknown product", null, null, null, false)))],
        [.. content.LandingPages ?? []],
        [.. content.Languages ?? []],
        [.. (content.Reasons ?? []).Select(reason => new BriefReasonView(
            reason.Field,
            reason.Reason,
            [.. (reason.Evidence ?? []).Select(item => new BriefEvidenceView(item.Label, item.Value, item.Source))]))],
        brief.Model,
        brief.PromptVersion,
        brief.CreatedAt);

    public static BriefProductView Product(BriefProductOption product) =>
        new(product.Id, product.Title, product.Price, product.Currency, product.Link, product.InStock);

    public static BriefWarningView[] Warnings(IEnumerable<BriefWarning> warnings) =>
        [.. warnings.Select(warning => new BriefWarningView(warning.Code, warning.Field, warning.Severity, warning.Message))];

    public static BriefOptionsView Options(BriefDeskContext context) => new(
        [.. context.Regions.Select(region => new BriefRegionOptionView(region.Id, region.Name, region.LastMonthSearches))],
        [.. context.Products.Select(Product)],
        [.. BriefChoices.CampaignTypes.Select(choice => new BriefChoiceView(choice.Value, choice.Label))],
        [.. BriefChoices.Kpis.Select(choice => new BriefChoiceView(choice.Value, choice.Label))],
        [.. BriefChoices.Languages.Select(choice => new BriefChoiceView(choice.Value, choice.Label))]);

    public static BriefBudgetView? Limit(BudgetCap limit) => limit.Amount is { } amount ? new BriefBudgetView(amount, limit.Currency) : null;

    public static string Display(string field, BriefContent content, BriefDeskContext context) => field switch
    {
        BriefFields.Title => content.Title,
        BriefFields.Objective => content.Objective,
        BriefFields.Kpi => BriefChoices.Kpis.FirstOrDefault(choice => choice.Value == content.Kpi).Label ?? content.Kpi ?? "",
        BriefFields.CampaignTypes => string.Join(", ", (content.CampaignTypes ?? []).Select(BriefChoices.CampaignTypeName)),
        BriefFields.Budget => content.Budget is { } budget ? BudgetGuide.Money(budget.Amount, budget.Currency) : "",
        BriefFields.Dates => content.StartDate is { } start && content.EndDate is { } end ? $"{BriefChecks.Day(start)} to {BriefChecks.Day(end)}" : "",
        BriefFields.Regions => string.Join(", ", content.Regions ?? []),
        BriefFields.Audiences => string.Join("; ", content.Audiences ?? []),
        BriefFields.KeyMessages => string.Join("; ", content.KeyMessages ?? []),
        BriefFields.Products => string.Join(", ", (content.ProductIds ?? []).Select(id => context.Product(id)?.Title ?? "Unknown product")),
        BriefFields.LandingPages => string.Join(", ", content.LandingPages ?? []),
        _ => string.Join(", ", (content.Languages ?? []).Select(BriefChoices.LanguageName)),
    };
}
