using Kairo.Contracts.Operations;
using Kairo.Modules.CampaignRooms.Data;
using Kairo.SharedKernel.Agents;

namespace Kairo.Modules.CampaignRooms.Rooms;

internal sealed class CampaignUsageFilter(CampaignRoomsDbContext db, AgentUsageContext usage) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (!Guid.TryParse(context.HttpContext.Request.RouteValues["campaignId"]?.ToString(), out var campaign)) return await next(context);
        var room = await db.Rooms.AsNoTracking().SingleOrDefaultAsync(row => row.Id == campaign, context.HttpContext.RequestAborted);
        if (room is null) return TypedResults.NotFound();
        using var scope = usage.Enter(new AgentUsageScope(room.BrandId, room.Id));
        return await next(context);
    }

    public static async Task<Ok<CampaignUsage>> ReadAsync(Guid campaignId, IUsageLedger ledger, HttpContext http, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "private, no-store";
        return TypedResults.Ok(await ledger.ReadAsync(campaignId, ct));
    }
}
