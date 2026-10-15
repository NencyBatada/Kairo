using Kairo.Contracts.Audit;

namespace Kairo.Modules.Audit;

internal static class AuditActorTypes
{
    public static string KeyOf(AuditActorType type) => type switch
    {
        AuditActorType.User => "user",
        AuditActorType.Agent => "agent",
        AuditActorType.System => "system",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    public static AuditActorType Parse(string key) =>
        TryParse(key, out var type) ? type : AuditActorType.System;

    public static bool TryParse(string? key, out AuditActorType type)
    {
        foreach (var candidate in Enum.GetValues<AuditActorType>())
        {
            if (KeyOf(candidate) == key)
            {
                type = candidate;
                return true;
            }
        }

        type = default;
        return false;
    }
}
