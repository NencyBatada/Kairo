using Kairo.Contracts.Media;
using Kairo.Modules.Accessibility.Data;
using Kairo.SharedKernel.Files;
using Microsoft.AspNetCore.Mvc;
using Kairo.SharedKernel.Agents;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kairo.Modules.Accessibility;

public sealed class AccessibilityModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AccessibilityDbContext>((provider, options) => options.UseKairoDatabase(provider, AccessibilityDbContext.SchemaName));
        services.AddScoped<AlternativeService>();
        services.TryAddScoped<AgentUsageContext>();
    }
    public void MapApi(IEndpointRouteBuilder api)
    {
        var routes = api.MapBrandApi().MapGroup("/media-accessibility").AddEndpointFilter<AlternativeErrorFilter>();
        routes.MapGet("", ReadAsync).Produces<AlternativeView>().Produces(200, contentType: "text/vtt").Produces(200, contentType: "video/mp4").Produces(404);
        routes.MapPut("", async (AlternativeWrite request, AlternativeService service, CancellationToken ct) => TypedResults.Ok(await service.SaveAsync(request, false, ct)));
        routes.MapPost("/confirm", async (AlternativeWrite request, AlternativeService service, CancellationToken ct) => TypedResults.Ok(await service.SaveAsync(request, true, ct)));
        routes.MapPost("/draft", async (AlternativeWrite request, AlternativeService service, CancellationToken ct) => TypedResults.Ok(await service.DraftAsync(request, ct)));
        routes.MapPost("/describe", async (AlternativeWrite request, AlternativeService service, CancellationToken ct) => TypedResults.Ok(await service.DescribeAsync(request, ct)));
        routes.MapPost("/described", UploadAsync).Produces<AlternativeView>().DisableAntiforgery();
    }
    public void MapJobs(IEndpointRouteBuilder jobs) { }
    public Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken) => services.GetRequiredService<AccessibilityDbContext>().Database.MigrateAsync(cancellationToken);

    private static async Task<IResult> ReadAsync([AsParameters] MediaQuery query, AlternativeService service, IFileStore files, HttpContext http, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "private, no-store";
        http.Response.Headers.XContentTypeOptions = "nosniff";
        if (query.File is null) return TypedResults.Ok(await service.ReadAsync(query.Target, ct));
        var (text, key) = await service.FileAsync(query.Target, query.File, query.Revision, query.SourceHash, ct);
        if (text is not null) return TypedResults.Text(text, "text/vtt; charset=utf-8");
        try { return TypedResults.Stream(await files.OpenAsync(FileBuckets.GeneratedMedia, key!, ct), "video/mp4", enableRangeProcessing: true); }
        catch (StoredFileMissingException) { return TypedResults.NotFound(); }
    }

    private static async Task<IResult> UploadAsync([AsParameters] MediaQuery query, HttpRequest request, AlternativeService service, CancellationToken ct)
    {
        await service.SourceAsync(query.Target, true, ct);
        if (query.Revision is null || query.SourceHash is null) return TypedResults.Problem(statusCode: 400, title: "Supply the current alternatives revision and source hash.");
        if (request.ContentType != "video/mp4" || request.ContentLength is > 25_000_000)
            return TypedResults.Problem(statusCode: 400, title: "Supply an MP4 file of at most 25 MB.");
        using var buffer = new MemoryStream();
        var chunk = new byte[64 * 1024];
        int count;
        while ((count = await request.Body.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + count > 25_000_000) return TypedResults.Problem(statusCode: 413, title: "The described video exceeds 25 MB.");
            await buffer.WriteAsync(chunk.AsMemory(0, count), ct);
        }
        return TypedResults.Ok(await service.UploadDescriptionAsync(query.Target, query.Revision.Value, query.SourceHash, buffer.ToArray(), ct));
    }
}

internal sealed record MediaQuery(string Kind, Guid Id, int Version, Guid? CampaignId = null, Guid? ConversationId = null, string? Name = null, string? File = null, int? Revision = null, string? SourceHash = null)
{
    public MediaTarget Target => new(Kind, Id, Version, CampaignId, ConversationId, Name);
}

internal sealed class AlternativeErrorFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        context.HttpContext.Response.Headers.CacheControl = "private, no-store";
        try { return await next(context); }
        catch (AlternativeException error) { return TypedResults.Problem(statusCode: error.Status, title: error.Message, extensions: new Dictionary<string, object?> { ["code"] = error.Code }); }
        catch (DbUpdateConcurrencyException) { return TypedResults.Problem(statusCode: 409, title: "These alternatives changed. Reload and review them again."); }
    }
}
