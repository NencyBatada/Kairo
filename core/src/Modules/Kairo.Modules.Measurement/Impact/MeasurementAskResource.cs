using System.Text.Json;
using Kairo.Contracts.Ask;
using Kairo.Contracts.CampaignRooms;
using Kairo.Modules.Measurement.Data;
using Kairo.Modules.Measurement.Lab;

namespace Kairo.Modules.Measurement.Impact;

internal sealed class ReportAskResource(MeasurementDbContext db, ICampaignTests tests) : IAskResource
{
    public string Kind => "report";
    public BrandRoles Editors => BrandRoles.None;
    public async Task<AskDocument?> ReadAsync(AskTarget target, CancellationToken ct)
    {
        try
        {
            var data = AskJson.Result(await ImpactEndpoints.GetAsync(target.Id!.Value, db, tests, ct));
            return new("Impact report", $"campaigns/{target.Id}/results", data, AskJson.Revision(data));
        }
        catch (AskResourceException exception) when (exception.Status == 404) { return null; }
    }
    public Task<AskChange> PreviewAsync(AskTarget target, JsonElement changes, CancellationToken ct) => throw new AskResourceException("Reports are read-only.", 403);
    public Task ApplyAsync(AskTarget target, string revision, JsonElement payload, CancellationToken ct) => throw new AskResourceException("Reports are read-only.", 403);
}

internal sealed class LabAskResource(MeasurementDbContext db) : IAskResource
{
    public string Kind => "impact_lab";
    public BrandRoles Editors => BrandRoles.None;
    public async Task<AskDocument?> ReadAsync(AskTarget target, CancellationToken ct)
    {
        try
        {
            var data = AskJson.Result(await LabEndpoints.GetAsync(target.Id!.Value, db, ct));
            return new("Impact Lab result", "impact-lab", data, AskJson.Revision(data));
        }
        catch (AskResourceException exception) when (exception.Status == 404) { return null; }
    }
    public Task<AskChange> PreviewAsync(AskTarget target, JsonElement changes, CancellationToken ct) => throw new AskResourceException("Impact Lab results are read-only.", 403);
    public Task ApplyAsync(AskTarget target, string revision, JsonElement payload, CancellationToken ct) => throw new AskResourceException("Impact Lab results are read-only.", 403);
}
