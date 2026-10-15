using System.Globalization;
using Kairo.Modules.Audit.Data;
using Microsoft.AspNetCore.Mvc;

namespace Kairo.Modules.Audit;

internal sealed record AuditLogQuery(
    [FromQuery(Name = "action")] string[]? Action,
    [FromQuery(Name = "actor")] string? Actor,
    [FromQuery(Name = "actorType")] string? ActorType,
    [FromQuery(Name = "from")] string? From,
    [FromQuery(Name = "to")] string? To);

internal sealed class AuditLogFilter
{
    public const int MaxActions = 50;
    public const int MaxActorLength = 200;

    private const int MinYear = 1900;
    private const int MaxYear = 9998;

    private static readonly string[] InstantFormats =
    [
        "yyyy-MM-dd'T'HH:mmK",
        "yyyy-MM-dd'T'HH:mm:ssK",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK",
    ];

    private AuditLogFilter(IReadOnlyList<string> actions, string? actor, string? actorType, DateTimeOffset? from, DateTimeOffset? until)
    {
        Actions = actions;
        Actor = actor;
        ActorType = actorType;
        From = from;
        Until = until;
    }

    public IReadOnlyList<string> Actions { get; }

    public string? Actor { get; }

    public string? ActorType { get; }

    public DateTimeOffset? From { get; }

    public DateTimeOffset? Until { get; }

    public static AuditLogFilter? TryCreate(AuditLogQuery query, TimeZoneInfo zone, Dictionary<string, string[]> errors)
    {
        var actions = (query.Action ?? [])
            .Where(action => !string.IsNullOrWhiteSpace(action))
            .Select(action => action.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (actions.Count > MaxActions)
        {
            errors["action"] = [$"Choose {MaxActions} actions or fewer."];
        }

        var actor = string.IsNullOrWhiteSpace(query.Actor) ? null : query.Actor.Trim();
        if (actor is { Length: > MaxActorLength })
        {
            errors["actor"] = [$"Use {MaxActorLength} characters or fewer."];
        }

        string? actorType = null;
        if (!string.IsNullOrWhiteSpace(query.ActorType))
        {
            if (AuditActorTypes.TryParse(query.ActorType.Trim(), out var type))
            {
                actorType = AuditActorTypes.KeyOf(type);
            }
            else
            {
                errors["actorType"] = ["Choose user, agent or system."];
            }
        }

        var from = ParseBound(query.From, zone, endOfDay: false, "from", errors);
        var until = ParseBound(query.To, zone, endOfDay: true, "to", errors);
        if (from is not null && until is not null && until <= from)
        {
            errors["to"] = ["Choose an end on or after the start."];
        }

        return errors.Count == 0 ? new AuditLogFilter(actions, actor, actorType, from, until) : null;
    }

    public IQueryable<AuditEntry> Apply(IQueryable<AuditEntry> entries)
    {
        if (Actions.Count > 0)
        {
            var actions = Actions.ToList();
            entries = entries.Where(entry => actions.Contains(entry.Action));
        }

        if (Actor is { } actor)
        {
            entries = entries.Where(entry =>
                entry.ActorName.Contains(actor)
                || (entry.ActorEmail != null && entry.ActorEmail.Contains(actor))
                || entry.ActorId == actor);
        }

        if (ActorType is { } actorType)
        {
            entries = entries.Where(entry => entry.ActorType == actorType);
        }

        if (From is { } from)
        {
            entries = entries.Where(entry => entry.OccurredAt >= from);
        }

        if (Until is { } until)
        {
            entries = entries.Where(entry => entry.OccurredAt < until);
        }

        return entries;
    }

    private static DateTimeOffset? ParseBound(string? value, TimeZoneInfo zone, bool endOfDay, string field, Dictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim();
        if (DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            && date.Year is >= MinYear and <= MaxYear)
        {
            var local = (endOfDay ? date.AddDays(1) : date).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
            return new DateTimeOffset(local, zone.GetUtcOffset(local));
        }

        if ((text.EndsWith('Z') || text.EndsWith('z') || HasOffset(text))
            && DateTimeOffset.TryParseExact(text, InstantFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var instant)
            && instant.Year is >= MinYear and <= MaxYear)
        {
            return endOfDay ? instant.AddTicks(1) : instant;
        }

        errors[field] = ["Use a date like 2026-10-14, or a time with its offset like 2026-10-14T09:30:00+05:30."];
        return null;
    }

    private static bool HasOffset(string text) =>
        text.Length > 6 && text[^6] is '+' or '-' && text[^3] == ':';
}
