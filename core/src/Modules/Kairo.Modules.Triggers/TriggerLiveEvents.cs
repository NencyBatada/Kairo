using Kairo.Contracts.Live;

namespace Kairo.Modules.Triggers;

internal static class TriggerLiveEvents
{
    public static LiveEvent RulesChanged() => new("trigger_rules.changed", new { }, LiveAudience.Everyone);

    public static LiveEvent FestivalsChanged() => new("festivals.changed", new { }, LiveAudience.Everyone);
}
