using System.Security.Claims;

namespace Kairo.Modules.Live.Delivery;

internal static class LiveEndpoints
{
    private const string LastEventIdHeader = "Last-Event-ID";

    public static void MapLive(this IEndpointRouteBuilder api)
    {
        api.MapBrandApi().MapGet("/live", Open);
    }

    private static ServerSentEventsResult<string> Open(HttpContext http, CurrentMember member, LiveStream stream)
    {
        http.Response.OnStarting(() =>
        {
            http.Response.Headers.CacheControl = "no-cache, no-store, no-transform";
            http.Response.Headers["X-Accel-Buffering"] = "no";
            return Task.CompletedTask;
        });
        return TypedResults.ServerSentEvents(stream.ReadAsync(
            new Viewer(member.BrandId, member.UserId, member.Roles, http.User.FindFirstValue(KairoClaims.SessionKey)),
            LivePosition.Parse(http.Request.Headers[LastEventIdHeader].ToString()),
            http.RequestAborted));
    }
}
