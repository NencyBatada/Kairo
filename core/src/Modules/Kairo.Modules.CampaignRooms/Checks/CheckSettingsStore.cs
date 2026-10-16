using Kairo.Modules.CampaignRooms.Data;

namespace Kairo.Modules.CampaignRooms.Checks;

internal sealed class CheckSettingsStore(CampaignRoomsDbContext db)
{
    public async Task<EffectiveCheckSettings> GetAsync(Guid brandId, CancellationToken cancellationToken) =>
        EffectiveCheckSettings.From(await db.CheckSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(settings => settings.BrandId == brandId, cancellationToken));
}

internal sealed record CheckRuleView(
    string Id,
    string Name,
    string Description,
    string[] AppliesTo,
    string Severity,
    string DefaultSeverity,
    bool Adjustable,
    bool Overridable);

internal sealed record CustomCheckRuleView(string Id, string Text, string AppliesTo, string Severity);

internal sealed record CheckSettingsView(
    int Version,
    CheckRuleView[] Rules,
    CustomCheckRuleView[] CustomRules,
    int PassScore,
    int FixAttempts,
    Guid? UpdatedBy,
    DateTimeOffset? UpdatedAt)
{
    public static CheckSettingsView Of(EffectiveCheckSettings settings) => new(
        settings.Version,
        settings.Rules
            .Select(rule => new CheckRuleView(
                rule.Rule.Id,
                rule.Rule.Name,
                rule.Rule.Description,
                rule.Rule.AppliesTo,
                rule.Severity,
                rule.Rule.DefaultSeverity,
                rule.Rule.Adjustable,
                rule.Rule.Overridable))
            .ToArray(),
        settings.CustomRules.Select(rule => new CustomCheckRuleView(rule.Id, rule.Text, rule.AppliesTo, rule.Severity)).ToArray(),
        settings.PassScore,
        settings.FixAttempts,
        settings.UpdatedBy,
        settings.UpdatedAt);
}
