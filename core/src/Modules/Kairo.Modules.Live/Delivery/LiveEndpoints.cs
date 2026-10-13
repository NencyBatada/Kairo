using System.Globalization;
using System.Security.Claims;

namespace Kairo.Modules.Live.Delivery;

internal static class LiveEndpoints
{
    private const string LastEventIdHeader = "Last-Event-ID";

    public static void MapLive(this IEndpointRouteBuilder api)
    {
        api.MapBrandApi().MapGet("/live", Open);
    }

    private static ServerSentEventsResult<string> Open(HttpContext http, CurrentMember member, LiveStream stream) =>
        TypedResults.ServerSentEvents(stream.ReadAsync(
            new Viewer(member.BrandId, member.UserId, member.Roles, http.User.FindFirstValue(KairoClaims.SessionKey)),
            LastEventId(http.Request),
            http.RequestAborted));

    private static long? LastEventId(HttpRequest request) =>
        long.TryParse(request.Headers[LastEventIdHeader].ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            ? id
            : null;
}
