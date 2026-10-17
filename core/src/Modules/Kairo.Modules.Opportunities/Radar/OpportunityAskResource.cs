using System.Text.Json;
using Kairo.Contracts.Ask;
using Kairo.Modules.Opportunities.Data;

namespace Kairo.Modules.Opportunities.Radar;

internal sealed class OpportunityAskResource(OpportunitiesDbContext db, OpportunityReader reader) : IAskResource
{
    public string Kind => "opportunity";
    public BrandRoles Editors => BrandRoles.None;
    public async Task<AskDocument?> ReadAsync(AskTarget target, CancellationToken ct)
    {
        var result = await OpportunityEndpoints.GetAsync(target.Id!.Value, db, reader, ct);
        try
        {
            var data = AskJson.Result(result);
            return new("Opportunity", $"opportunities/{target.Id}", data, AskJson.Revision(data));
        }
        catch (AskResourceException exception) when (exception.Status == 404) { return null; }
    }
    public Task<AskChange> PreviewAsync(AskTarget target, JsonElement changes, CancellationToken ct) => throw new AskResourceException("Opportunity changes use the existing Radar controls.", 403);
    public Task ApplyAsync(AskTarget target, string revision, JsonElement payload, CancellationToken ct) => throw new AskResourceException("Opportunity changes use the existing Radar controls.", 403);
}
