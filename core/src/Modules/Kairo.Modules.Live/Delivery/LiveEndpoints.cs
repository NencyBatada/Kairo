namespace Kairo.Modules.Live.Delivery;

internal static class LiveEndpoints
{
    public static void MapLive(this IEndpointRouteBuilder api)
    {
        api.MapBrandApi().MapGet("/live", Open);
    }

    private static ServerSentEventsResult<string> Open(HttpContext http, CurrentMember member, LiveStream stream) =>
        TypedResults.ServerSentEvents(stream.ReadAsync(new Viewer(member.BrandId, member.UserId, member.Roles), http.RequestAborted));
}
