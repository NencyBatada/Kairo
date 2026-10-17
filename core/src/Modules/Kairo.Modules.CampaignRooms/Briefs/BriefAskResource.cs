using System.Text.Json;
using Kairo.Contracts.Ask;
using Kairo.Modules.CampaignRooms.Data;
using Kairo.Modules.CampaignRooms.Rooms;

namespace Kairo.Modules.CampaignRooms.Briefs;

internal sealed class BriefAskResource(CampaignRoomsDbContext db, BriefEditor editor, BriefDesk desk, RoomWorkflow workflow, CurrentMember member) : IAskResource
{
    public string Kind => "brief";
    public BrandRoles Editors => BrandRoles.Marketer;
    public async Task<AskDocument?> ReadAsync(AskTarget target, CancellationToken ct)
    {
        var detail = await editor.DetailAsync(target.Id!.Value, ct);
        return detail is null ? null : new(detail.Brief?.Title ?? "Brief", $"campaigns/{target.Id}/brief", AskJson.Element(detail),
            AskJson.Revision(new { detail.CampaignVersion, briefVersion = detail.Brief?.Version, detail.Editable }));
    }
    public async Task<AskChange> PreviewAsync(AskTarget target, JsonElement changes, CancellationToken ct)
    {
        AskJson.Only(changes, "title", "objective", "kpi", "campaignTypes", "budget", "startDate", "endDate", "regions", "audiences", "keyMessages", "productIds", "landingPages", "languages");
        var (room, brief) = await CurrentAsync(target, ct);
        if (BriefEditor.Blocker(room) is { } blocker) throw new AskResourceException(blocker);
        var before = brief.Content.Normalized();
        var request = AskJson.Read<BriefEditRequest>(changes) with { Version = room.Version, BriefVersion = brief.Version };
        Budget(before.Budget?.Amount, request.Budget);
        var context = await desk.ReadAsync(room, before, true, ct);
        var errors = new Dictionary<string, string[]>();
        var after = BriefEditor.Apply(before, request, context, errors);
        if (errors.Count > 0) throw new AskResourceException(JsonSerializer.Serialize(errors), 400);
        if (BriefEditor.Changed(before, after).Count == 0) throw new AskResourceException("Nothing in the brief changed.", 400);
        return new(AskJson.Element(Input(room, brief, before)), AskJson.Element(Input(room, brief, after)), AskJson.Element(Input(room, brief, after)),
            [.. BriefChecks.Run(after, context.Situation()).Select(warning => warning.Message)]);
    }
    public async Task ApplyAsync(AskTarget target, string revision, JsonElement payload, CancellationToken ct)
    {
        await workflow.LockedAsync(target.Id!.Value, ct);
        AskJson.Unchanged(revision, await ReadAsync(target, ct));
        var (_, brief) = await CurrentAsync(target, ct);
        var request = AskJson.Read<BriefEditRequest>(payload);
        Budget(brief.Content.Normalized().Budget?.Amount, request.Budget);
        var result = await editor.EditAsync(target.Id.Value, request, member, ct);
        if (result.Error != BriefError.None) throw new AskResourceException(result.Message ?? JsonSerializer.Serialize(result.Errors));
    }
    internal static void Budget(decimal? current, decimal? requested)
    {
        if (requested is { } amount && (current is null || amount > current))
            throw new AskResourceException("Chat cannot raise spend. Use the standard budget and approval workflow.", 403);
    }
    private async Task<(Room Room, Brief Brief)> CurrentAsync(AskTarget target, CancellationToken ct)
    {
        var room = await db.Rooms.AsNoTracking().SingleOrDefaultAsync(row => row.Id == target.Id, ct) ?? throw new AskResourceException("This campaign is unavailable.", 404);
        var brief = await db.Briefs.AsNoTracking().Where(row => row.RoomId == room.Id).OrderByDescending(row => row.Version).FirstOrDefaultAsync(ct)
            ?? throw new AskResourceException("This brief isn't ready yet.");
        return (room, brief);
    }
    private static BriefEditRequest Input(Room room, Brief brief, BriefContent content) => new(room.Version, brief.Version, content.Title, content.Objective,
        content.Kpi, [.. content.CampaignTypes], content.Budget?.Amount, content.StartDate, content.EndDate, [.. content.Regions], [.. content.Audiences],
        [.. content.KeyMessages], [.. content.ProductIds], [.. content.LandingPages], [.. content.Languages]);
}
