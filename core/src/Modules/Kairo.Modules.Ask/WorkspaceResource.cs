using System.Text.Json;
using Kairo.Contracts.Ask;
using Kairo.Contracts.Brands;
using Kairo.Contracts.BrandMemory;
using Kairo.Contracts.Measurement;

namespace Kairo.Modules.Ask;

internal sealed class WorkspaceResource(IBrandDirectory brands, IImpactReports reports, IBrandMemory memory, CurrentMember member, TimeProvider time) : IAskResource
{
    public string Kind => "workspace";
    public BrandRoles Editors => BrandRoles.None;
    public async Task<AskDocument?> ReadAsync(AskTarget target, CancellationToken ct)
    {
        var brand = (await brands.GetAsync([member.BrandId], ct)).SingleOrDefault();
        if (brand is null) return null;
        var data = AskJson.Element(new
        {
            brand.Name, brand.Currency, brand.TimeZone,
            reports = (await reports.ListReadyAsync(time.GetUtcNow().AddDays(-90), ct)).Take(50).Select(row => new
            {
                row.RoomId, row.Title, row.StartDate, row.ThroughDate, row.Outcome, row.Lift, row.Low, row.High, row.Spend,
                row.Extra, row.CostPerExtra, row.Summary, row.Flags, path = $"/{brand.Handle}/campaigns/{row.RoomId}/results",
            }),
            memory = await memory.RelevantAsync(new MemoryQuery(Limit: 50), ct),
            guidance = "Reports cover the most recent 90 days, up to 50 reports. Read individual reports and memory entries to cite their own links. Chat cannot approve, launch, or raise spend. Open a campaign's standard approval step for these requests.",
        });
        return new(brand.Name, "today", data, AskJson.Revision(data));
    }
    public Task<AskChange> PreviewAsync(AskTarget target, JsonElement changes, CancellationToken ct) => throw new AskResourceException("Workspace settings cannot be changed through chat.", 403);
    public Task ApplyAsync(AskTarget target, string revision, JsonElement payload, CancellationToken ct) => throw new AskResourceException("Workspace settings cannot be changed through chat.", 403);
}
