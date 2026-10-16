using Kairo.Contracts.Identity;
using Kairo.Modules.CampaignRooms.Data;
using Kairo.Modules.CampaignRooms.Rooms;

namespace Kairo.Modules.CampaignRooms.Assets;

internal sealed record AgentCallView(string Route, string Digest, string? Language);

internal sealed record AssetInputView(Guid AssetId, int Version, string Kind);

internal sealed record AssetSourcesView(string[] ProductIds, string[] PhotoUrls, AssetInputView[] Inputs);

internal sealed record AssetHistoryVersionView(
    int Version,
    string Origin,
    DateTimeOffset CreatedAt,
    CampaignPersonView? CreatedBy,
    string? Instruction,
    Guid? StageRunId,
    string? Model,
    string? PromptVersion,
    AgentCallView[] AgentCalls,
    AssetSourcesView Sources,
    int? BrandKitVersion,
    int BriefVersion,
    int? PreviousVersion,
    string[] ChangedFields,
    CampaignPersonView? ApprovedBy,
    DateTimeOffset? ApprovedAt,
    string[] MemoryUses,
    AssetContentView Content,
    string? MediaUrl,
    AssetDisclosureView Disclosure);

internal sealed record AssetHistoryView(
    Guid AssetId,
    Guid CampaignId,
    string Kind,
    string CampaignType,
    string Language,
    string Status,
    bool Locked,
    int CurrentVersion,
    AssetHistoryVersionView[] Versions);

internal sealed class AssetHistory(CampaignRoomsDbContext db, IBrandMembers members)
{
    public const string FormerMember = "Former member";

    public async Task<AssetHistoryView?> ReadAsync(Guid roomId, Guid assetId, CancellationToken cancellationToken)
    {
        var asset = await db.Assets.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == assetId && row.RoomId == roomId && !row.Hidden, cancellationToken);
        if (asset is null)
        {
            return null;
        }

        var versions = await db.AssetVersions.AsNoTracking()
            .Where(version => version.AssetId == asset.Id && version.RoomId == roomId)
            .OrderByDescending(version => version.Version)
            .ToListAsync(cancellationToken);
        var people = (await members.ListAsync(cancellationToken)).ToDictionary(member => member.UserId, member => member.Name);
        return new AssetHistoryView(
            asset.Id,
            roomId,
            asset.Kind,
            asset.CampaignType,
            asset.Language,
            asset.Status,
            asset.Locked,
            asset.CurrentVersion,
            [.. versions.Select(version => View(asset, version, people))]);
    }

    private static AssetHistoryVersionView View(Asset asset, AssetVersion version, IReadOnlyDictionary<Guid, string> people)
    {
        var content = version.Read();
        var provenance = version.ReadProvenance();
        return new AssetHistoryVersionView(
            version.Version,
            version.Origin,
            version.CreatedAt,
            Person(version.CreatedBy, people),
            version.Instruction,
            version.StageRunId,
            version.Model,
            version.PromptVersion,
            [.. provenance.AgentCalls.Select(call => new AgentCallView(call.Route, call.Digest, call.Language))],
            new AssetSourcesView(
                [.. provenance.ProductIds],
                [.. provenance.PhotoUrls],
                [.. provenance.Inputs.Select(input => new AssetInputView(input.AssetId, input.Version, input.Kind))]),
            provenance.BrandKitVersion,
            version.Provenance is null ? asset.BriefVersion : provenance.BriefVersion,
            provenance.PreviousVersion,
            [.. provenance.ChangedFields],
            Person(version.ApprovedBy, people),
            version.ApprovedAt,
            [.. provenance.MemoryUses],
            AssetViews.Content(content),
            content.Media is null ? null : AssetViews.MediaUrl(asset.BrandId, asset.RoomId, asset.Id, version.Version),
            AssetViews.Disclosure(DisclosureRules.For(asset.Kind, content)));
    }

    private static CampaignPersonView? Person(Guid? userId, IReadOnlyDictionary<Guid, string> people) =>
        userId is { } id ? new CampaignPersonView(id, people.TryGetValue(id, out var name) ? name : FormerMember) : null;
}
